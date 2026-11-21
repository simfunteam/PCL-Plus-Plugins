// PCL2Plus 插件：接管文件拖拽，自己安装整合包（.mrpack / .zip）
// 注入方式：启动器 --hook-only（只挂钩子 + 下载补丁，不加"插件"页）
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using PCL;

namespace PCL2Plus
{
    internal static class DropHook
    {
        private static bool _installed;

        internal static void Install()
        {
            try
            {
                if (_installed) return;
                var frm = PluginHost.Frm;
                if (frm == null) return;

                // 关键：在 DragEnter 阶段就判定并把 Effects 置 None，
                // 这样 Windows 不会派发 Drop，PCL 自己的拖拽处理器完全不会触发。
                frm.AllowDrop = true;
                var content = frm.Content as UIElement;
                if (content != null) content.AllowDrop = true;

                // PCL 切页时会重设 AllowDrop / 重建内容树，这里定时兜底
                var timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(2);
                timer.Tick += delegate
                {
                    try
                    {
                        frm.AllowDrop = true;
                        var c2 = frm.Content as UIElement;
                        if (c2 != null) c2.AllowDrop = true;
                    }
                    catch { }
                };
                timer.Start();

                if (content != null)
                    content.AddHandler(System.Windows.DragDrop.DragEnterEvent,
                        new DragEventHandler(OnDragEnter), true);

                frm.AddHandler(System.Windows.DragDrop.DragEnterEvent,
                    new DragEventHandler(OnDragEnter), true);
                frm.AddHandler(System.Windows.DragDrop.DragOverEvent,
                    new DragEventHandler(OnDragOver2), true);
                frm.AddHandler(System.Windows.DragDrop.DropEvent,
                    new DragEventHandler(OnDrop), true);
                _installed = true;
                PluginHost.Log("DropHook 已安装（DragEnter 拦截模式）");
            }
            catch (Exception ex) { PluginHost.Log("DropHook 安装失败：" + ex); }
        }

        private static void OnDragEnter(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                string file = files[0];
                if (!IsPack(file)) return;

                e.Effects = DragDropEffects.None;   // 阻止 Windows 派发 Drop → PCL 收不到
                e.Handled = true;
                PluginHost.Log("DragEnter 拦截：" + file);
                ModpackInstaller.Start(file);
            }
            catch (Exception ex) { PluginHost.Log("DragEnter 异常：" + ex); }
        }

        private static void OnDragOver2(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                if (IsPack(files[0])) { e.Effects = DragDropEffects.None; e.Handled = true; }
            }
            catch { }
        }

        private static void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                e.Effects = files != null && files.Length > 0 && IsPack(files[0])
                    ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;      // 阻断 PCL 自己的 DragOver
            }
        }

        private static void OnDrop(object sender, DragEventArgs e)
        {
            try
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                string file = files[0];
                if (!IsPack(file)) return;

                e.Handled = true;      // 阻断 PCL 的拖拽处理
                PluginHost.Log("DropHook 接管：" + file);
                ModpackInstaller.Start(file);
            }
            catch (Exception ex) { PluginHost.Log("DropHook 异常：" + ex); }
        }

        private static bool IsPack(string f)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            return ext == ".mrpack" || ext == ".zip";
        }
    }

    internal static class ModpackInstaller
    {
        // PCL 的实例目录：<PCL目录>\.minecraft\versions（可用 PCL 配置里的 McFolder 覆盖）
        private static string McFolder()
        {
            string baseDir = Path.GetDirectoryName(PluginHost.Frm.GetType().Assembly.Location);
            string mc = null;
            try
            {
                string ini = Path.Combine(baseDir, "PCL", "Setup.ini");
                if (File.Exists(ini))
                    foreach (string line in File.ReadAllLines(ini, Encoding.UTF8))
                    {
                        int c = line.IndexOf(':');
                        if (c <= 0) continue;
                        string k = line.Substring(0, c).Trim();
                        if (k == "LaunchFolderSelect" || k == "McFolder")
                        {
                            string v = line.Substring(c + 1).Trim().Trim('"');
                            if (v.Length > 0) mc = v;
                        }
                    }
            }
            catch { }

            if (string.IsNullOrEmpty(mc)) mc = Path.Combine(baseDir, ".minecraft");
            mc = mc.TrimEnd('\\', '/');
            Directory.CreateDirectory(mc);
            Directory.CreateDirectory(Path.Combine(mc, "versions"));
            return mc;
        }

        private static string VersionsDir() { return Path.Combine(McFolder(), "versions"); }

        private static readonly object _lock = new object();
        private static string _busy = null;
        private static DateTime _lastSame = DateTime.MinValue;
        private static string _lastFile = null;
        internal static void Start(string file)
        {
            // DragEnter 会重复触发：同一文件 3 秒内只处理一次
            if (_lastFile == file && (DateTime.Now - _lastSame).TotalSeconds < 3)
            {
                PluginHost.Log("忽略重复触发：" + Path.GetFileName(file));
                return;
            }
            _lastFile = file;
            _lastSame = DateTime.Now;

            lock (_lock)
            {
                if (_busy != null)
                {
                    Hint("已有整合包正在安装，请稍后……", false);
                    return;
                }
                _busy = file;
            }

            var task = new ModLoader.LoaderTask<string, string>(
                "整合包安装：" + Path.GetFileName(file),
                delegate(ModLoader.LoaderTask<string, string> tt)
                {
                    try { Work(file, tt); }
                    finally { lock (_lock) { _busy = null; } }
                },
                delegate { return file; });
            try
            {
                var combo = new ModLoader.LoaderCombo<string>("整合包安装：" + Path.GetFileName(file),
                    new ModLoader.LoaderBase[] { task });
                ModLoader.LoaderTaskbarAdd(combo);
                combo.Start(file);
                PluginHost.Log("已加入 PCL 任务栏并启动");
            }
            catch (Exception ex)
            {
                PluginHost.Log("加入任务栏失败，退回直接启动：" + ex.Message);
                task.Start(file);
            }
        }

        private static void Hint(string text, bool red)
        {
            try { ModMain.Hint(text, red ? ModMain.HintType.Red : ModMain.HintType.Blue); } catch { }
        }

        private static void Log(string s) { PluginHost.Log("[安装] " + s); }

        private static void Work(string file, ModLoader.LoaderTask<string, string> task)
        {
            string tmp = null;
            try
            {
                Hint("正在读取整合包……", false);
                Log("开始：" + file);
                try
                {
                    PluginHost.Frm.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { PluginHost.Frm.PageChange(FormMain.PageType.Download); }
                        catch (Exception ex) { Log("切到下载页失败：" + ex.Message); }
                    }));
                }
                catch { }

                string name, mcVersion = null, loader = null, loaderVer = null;
                var files = new List<string[]>();
                List<string> overrides = new List<string>();

                using (var zip = ZipFile.OpenRead(file))
                {
                    ZipArchiveEntry idx = zip.GetEntry("modrinth.index.json");
                    if (idx == null)
                    {
                        // CurseForge / 通用 zip：读 manifest.json，先建实例 + 解压 overrides，
                        // 模组文件需要 CurseForge 接口，这里不做自动下载。
                        var cfEntry = zip.GetEntry("manifest.json");
                        if (cfEntry == null) { Hint("无法识别的整合包（既没有 modrinth.index.json 也没有 manifest.json）", true); return; }
                        string cfJson;
                        using (var sr0 = new StreamReader(cfEntry.Open(), Encoding.UTF8)) cfJson = sr0.ReadToEnd();
                        var cfSer = new System.Web.Script.Serialization.JavaScriptSerializer();
                        var cfObj = (Dictionary<string, object>)cfSer.DeserializeObject(cfJson);
                        name = cfObj.ContainsKey("name") ? Convert.ToString(cfObj["name"]) : Path.GetFileNameWithoutExtension(file);
                        if (cfObj.ContainsKey("minecraft"))
                        {
                            var mcObj = (Dictionary<string, object>)cfObj["minecraft"];
                            if (mcObj.ContainsKey("version")) mcVersion = Convert.ToString(mcObj["version"]);
                        }
                        if (cfObj.ContainsKey("minecraft"))
                        {
                            var mcObj2 = (Dictionary<string, object>)cfObj["minecraft"];
                            if (mcObj2.ContainsKey("modLoaders"))
                            {
                                var arr = (object[])mcObj2["modLoaders"];
                                foreach (object mo in arr)
                                {
                                    var md = (Dictionary<string, object>)mo;
                                    bool prim = md.ContainsKey("primary") && Convert.ToBoolean(md["primary"]);
                                    string lid = md.ContainsKey("id") ? Convert.ToString(md["id"]) : "";
                                    if (!prim && loader != null) continue;
                                    if (lid.StartsWith("neoforge-")) { loader = "neoforge"; loaderVer = lid.Substring("neoforge-".Length); }
                                    else if (lid.StartsWith("forge-")) { loader = "forge"; loaderVer = lid.Substring("forge-".Length); }
                                    else if (lid.StartsWith("fabric-")) { loader = "fabric"; loaderVer = lid.Substring("fabric-".Length); }
                                }
                            }
                        }
                        if (cfObj.ContainsKey("files"))
                        {
                            foreach (object o in (object[])cfObj["files"])
                            {
                                var f = (Dictionary<string, object>)o;
                                if (f.ContainsKey("required") && !Convert.ToBoolean(f["required"])) continue;
                                string pj = f.ContainsKey("projectID") ? Convert.ToString(f["projectID"]) : null;
                                string fl = f.ContainsKey("fileID") ? Convert.ToString(f["fileID"]) : null;
                                if (pj != null && fl != null) files.Add(new string[] { "CF:" + pj + ":" + fl, "" });
                            }
                        }
                        Log("CurseForge 包：待下载 " + files.Count + " 个文件，加载器 " + loader + " " + loaderVer);
                        goto BuildInstance;
                    }

                    string json;
                    using (var sr = new StreamReader(idx.Open(), Encoding.UTF8)) json = sr.ReadToEnd();
                    var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var obj = (Dictionary<string, object>)ser.DeserializeObject(json);

                    name = obj.ContainsKey("name") ? Convert.ToString(obj["name"]) : Path.GetFileNameWithoutExtension(file);
                    if (obj.ContainsKey("dependencies"))
                    {
                        var dep = (Dictionary<string, object>)obj["dependencies"];
                        if (dep.ContainsKey("minecraft")) mcVersion = Convert.ToString(dep["minecraft"]);
                        if (dep.ContainsKey("fabric-loader")) { loader = "fabric"; loaderVer = Convert.ToString(dep["fabric-loader"]); }
                        if (dep.ContainsKey("quilt-loader")) { loader = "quilt"; loaderVer = Convert.ToString(dep["quilt-loader"]); }
                        if (dep.ContainsKey("forge")) { loader = "forge"; loaderVer = Convert.ToString(dep["forge"]); }
                    }
                    if (obj.ContainsKey("files"))
                    {
                        foreach (object o in (object[])obj["files"])
                        {
                            var f = (Dictionary<string, object>)o;
                            string path = f.ContainsKey("path") ? Convert.ToString(f["path"]) : null;
                            string url = null;
                            if (f.ContainsKey("downloads"))
                            {
                                var d = (object[])f["downloads"];
                                if (d.Length > 0) url = Convert.ToString(d[0]);
                            }
                            if (path != null && url != null) files.Add(new string[] { path, url });
                        }
                    }
                }

                if (name == null) name = Path.GetFileNameWithoutExtension(file);
                name = Sanitize(name);
                Log("包名：" + name + "  MC：" + mcVersion + "  加载器：" + loader + " " + loaderVer + "  文件：" + files.Count);

                BuildInstance:
                string versions = VersionsDir();
                string inst = Path.Combine(versions, name);
                Directory.CreateDirectory(inst);

                // 1) 下载文件：全部下完后再对失败项重试，最多 3 轮
                string mirror = null;
                try
                {
                    string mf = Path.Combine(Path.GetDirectoryName(PluginHost.Frm.GetType().Assembly.Location), @"PCL\Plugins\mirror.txt");
                    if (File.Exists(mf)) mirror = File.ReadAllText(mf).Trim();
                }
                catch { }

                var pending = new List<string[]>(files);
                var finalFailed = new List<string>();
                int round = 0;

                while (pending.Count > 0 && round <= 3)
                {
                    if (round > 0)
                    {
                        Log("第 " + round + " 轮重试，待处理 " + pending.Count + " 个文件");
                        Hint("重试失败文件（第 " + round + "/3 轮）：" + pending.Count + " 个……", false);
                    }

                    var next = new List<string[]>();
                    int done = 0;
                    Log("本轮开始，共 " + pending.Count + " 个文件");

                    foreach (var pair in pending)
                    {
                        string url = pair[1];
                        string dest = null;

                        if (pair[0].StartsWith("CF:"))
                        {
                            string[] seg = pair[0].Split(':');
                            string api = "https://mod.mcimirror.top/curseforge/v1/mods/" + seg[1] + "/files/" + seg[2];
                            string cfName = null;
                            // 元数据请求同样要看门狗，否则会卡在镜像接口上
                            try
                            {
                                string apic = api, nm = null, du = null;
                                var metaThread = new System.Threading.Thread(delegate ()
                                {
                                    try
                                    {
                                        var jr = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(apic);
                                        jr.Timeout = 12000; jr.ReadWriteTimeout = 12000; jr.UserAgent = "PCL2Plus/1.0";
                                        string body;
                                        using (var rp = jr.GetResponse())
                                        using (var rs0 = rp.GetResponseStream())
                                        using (var sr1 = new StreamReader(rs0, Encoding.UTF8)) body = sr1.ReadToEnd();
                                        var jo = (Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(body);
                                        var data = (Dictionary<string, object>)jo["data"];
                                        nm = Convert.ToString(data["fileName"]);
                                        du = Convert.ToString(data["downloadUrl"]);
                                    }
                                    catch { }
                                });
                                metaThread.IsBackground = true; metaThread.Start();
                                if (metaThread.Join(15000)) { cfName = nm; if (!string.IsNullOrEmpty(du)) url = du; }
                                else Log("取 CF 文件信息超时：" + pair[0]);
                            }
                            catch (Exception ex) { Log("取 CF 文件信息失败 " + pair[0] + " : " + ex.Message); }
                            if (string.IsNullOrEmpty(cfName) || string.IsNullOrEmpty(url)) { next.Add(pair); continue; }
                            dest = Path.Combine(inst, "mods", cfName);
                        }
                        else
                        {
                            dest = Path.Combine(inst, pair[0].Replace('/', '\\'));
                        }

                        try { Directory.CreateDirectory(Path.GetDirectoryName(dest)); } catch { }
                        try { if (File.Exists(dest) && new FileInfo(dest).Length > 0) { done++; continue; } } catch { }

                        bool ok = false;
                        try
                        {
                            string u = url, dpath = dest;
                            bool fin = false;
                            var th = new System.Threading.Thread(delegate ()
                            {
                                try
                                {
                                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(u);
                                    req.Timeout = 15000; req.ReadWriteTimeout = 15000; req.UserAgent = "PCL2Plus/1.0";
                                    using (var resp = req.GetResponse())
                                    using (var rs = resp.GetResponseStream())
                                    using (var fs = File.Create(dpath)) rs.CopyTo(fs);
                                    fin = true;
                                }
                                catch { }
                            });
                            th.IsBackground = true; th.Start();
                            if (!th.Join(12000)) { try { if (File.Exists(dpath)) File.Delete(dpath); } catch { } }
                            ok = fin;
                        }
                        catch { ok = false; }

                        if (!ok) next.Add(pair);

                        done++;
                        try { task.Progress = pending.Count == 0 ? 0.5 : (double)done / pending.Count * 0.9; } catch { }
                        if (done % 3 == 0 || done == 1)
                            Log("进度 " + done + "/" + pending.Count + "（本轮失败 " + next.Count + "）" + (ok ? "" : " 刚刚失败：" + pair[0]));
                        if (done % 3 == 0 || done == pending.Count)
                            Hint("正在下载整合包文件 " + done + "/" + pending.Count + (next.Count > 0 ? "（失败 " + next.Count + "）" : "") + "……", false);
                    }

                    pending = next;
                    round++;
                    if (pending.Count == 0) break;
                }

                int failed = pending.Count;
                if (failed > 0)
                {
                    try
                    {
                        string failLog = Path.Combine(inst, "PCL-Plus-安装失败清单.txt");
                        var sb2 = new StringBuilder();
                        sb2.AppendLine("以下文件重试 3 轮后仍失败，可手动补下载：");
                        foreach (var q in pending) sb2.AppendLine(q[0] + "  " + q[1]);
                        File.WriteAllText(failLog, sb2.ToString(), new UTF8Encoding(false));
                        Log("失败清单已写入：" + failLog);
                    }
                    catch { }
                    Hint("有 " + failed + " 个文件重试 3 轮仍失败，清单见实例目录下的 PCL-Plus-安装失败清单.txt", true);
                }
                // 2) 解压 overrides
                tmp = Path.Combine(Path.GetTempPath(), "pclplus_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                using (var zip = ZipFile.OpenRead(file))
                {
                    foreach (var e in zip.Entries)
                    {
                        if (!e.FullName.StartsWith("overrides/")) continue;
                        string rel = e.FullName.Substring("overrides/".Length);
                        if (rel.Length == 0) continue;
                        string dest = Path.Combine(inst, rel.Replace('/', '\\'));
                        if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        try { e.ExtractToFile(dest, true); } catch (Exception ex2) { failed++; Log("解压失败 " + rel + " : " + ex2.Message); }
                    }
                }

                // 3) 版本 JSON + jar：直接复制基版（PCL 只认完整 JSON，且需要同名 jar）
                string baseDir2 = Path.Combine(Path.GetDirectoryName(inst), mcVersion ?? "");
                string verJson = Path.Combine(inst, name + ".json");
                string baseJson = Path.Combine(baseDir2, (mcVersion ?? "") + ".json");
                string baseJar = Path.Combine(baseDir2, (mcVersion ?? "") + ".jar");

                // Fabric：优先向 meta.fabricmc.net 取官方 profile JSON（含 mainClass 与 libraries）
                string fabricProfile = null;
                if (loader == "fabric" && !string.IsNullOrEmpty(mcVersion) && !string.IsNullOrEmpty(loaderVer))
                {
                    string url = "https://meta.fabricmc.net/v2/versions/loader/" + mcVersion + "/" + loaderVer + "/profile/json";
                    try
                    {
                        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                        req.Timeout = 15000; req.ReadWriteTimeout = 15000; req.UserAgent = "PCL2Plus/1.0";
                        using (var resp = req.GetResponse())
                        using (var rs = resp.GetResponseStream())
                        using (var sr = new StreamReader(rs, Encoding.UTF8))
                            fabricProfile = sr.ReadToEnd();
                        Log("已获取 Fabric profile：" + url);
                    }
                    catch (Exception ex) { Log("获取 Fabric profile 失败：" + ex.Message); }
                }

                string fabricJson = loader == "fabric" ? FindFabricBase(Path.GetDirectoryName(inst), mcVersion) : null;
                string useJson = fabricProfile != null ? null : (fabricJson ?? baseJson);
                if (fabricJson != null) Log("使用 Fabric 基版 JSON：" + fabricJson);

                if (fabricProfile != null)
                {
                    var rx = new System.Text.RegularExpressions.Regex("(\"id\"\\s*:\\s*\")[^\"]*(\")");
                    string txtP = rx.Replace(fabricProfile, "$1" + name.Replace("$", "$$") + "$2", 1);
                    File.WriteAllText(verJson, txtP, new UTF8Encoding(false));
                    Log("版本 JSON 使用 Fabric profile（含加载器库）");
                }
                else if (useJson != null && File.Exists(useJson))
                {
                    string txt = File.ReadAllText(useJson, Encoding.UTF8);
                    string fromId = fabricJson != null ? Path.GetFileNameWithoutExtension(useJson) : (mcVersion ?? "");
                    txt = System.Text.RegularExpressions.Regex.Replace(txt,
                        "(\"id\"\\s*:\\s*\")" + System.Text.RegularExpressions.Regex.Escape(fromId) + "(\")",
                        "$1" + name.Replace("$", "$$") + "$2");
                    File.WriteAllText(verJson, txt, new UTF8Encoding(false));
                    Log("版本 JSON 由基版复制：" + useJson);
                }
                else
                {
                    File.WriteAllText(verJson,
                        "{\n  \"id\": \"" + name + "\",\n  \"type\": \"release\"\n}\n", new UTF8Encoding(false));
                    Log("警告：缺少基版 JSON，使用最小 JSON：" + baseJson);
                }

                string dstJar = Path.Combine(inst, name + ".jar");
                if (!File.Exists(dstJar) && File.Exists(baseJar))
                {
                    try { File.Copy(baseJar, dstJar, true); Log("已复制基版 jar"); }
                    catch (Exception ex) { Log("复制 jar 失败：" + ex.Message); }
                }
                // 4) 独立版本标记（PCL 用它识别为"独立实例"）
                string pclSetup = Path.Combine(inst, "PCL", "Setup.ini");
                Directory.CreateDirectory(Path.GetDirectoryName(pclSetup));
                var iniSb = new StringBuilder();
                iniSb.Append("[Setup]\r\n");
                iniSb.Append("VersionArgumentIndie=1\r\n");
                iniSb.Append("VersionArgumentIndieV2=True\r\n");
                iniSb.Append("IsStar=False\r\n");
                if (loader == "fabric" && !string.IsNullOrEmpty(loaderVer)) iniSb.Append("VersionFabric=" + loaderVer + "\r\n");
                if (loader == "quilt" && !string.IsNullOrEmpty(loaderVer)) iniSb.Append("VersionFabric=" + loaderVer + "\r\n");
                if (loader == "forge" && !string.IsNullOrEmpty(loaderVer)) iniSb.Append("VersionForge=" + loaderVer + "\r\n");
                File.WriteAllText(pclSetup, iniSb.ToString(), new UTF8Encoding(false));
                Log("已写 Setup.ini（加载器：" + loader + " " + loaderVer + "）");

                try { task.Progress = 1.0; } catch { }
                Log("完成：" + inst + "  失败项：" + failed);
                Hint("整合包已安装：" + name + (mcVersion != null ? "（MC " + mcVersion + "）" : "") +
                     (failed > 0 ? "，其中 " + failed + " 个文件失败（详见插件日志）" : ""), false);
            }
            catch (Exception ex)
            {
                Log("安装异常：" + ex);
                Hint("整合包安装失败：" + ex.Message, true);
            }
            finally
            {
                try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
        }

        /// <summary>在 versions 目录里找同 Minecraft 版本的 Fabric 实例 JSON（含 fabric libraries）</summary>
        private static string FindFabricBase(string versionsDir, string mcVersion)
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(versionsDir))
                {
                    string nm = Path.GetFileName(dir);
                    if (nm.IndexOf("Fabric", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string json = Path.Combine(dir, nm + ".json");
                    if (!File.Exists(json)) continue;
                    try
                    {
                        string txt = File.ReadAllText(json, Encoding.UTF8);
                        if (txt.IndexOf("\"" + mcVersion + "\"") < 0) continue;
                        if (txt.IndexOf("fabricmc", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        return json;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }
    }
}
