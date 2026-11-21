// PCL2Plus 启动器 —— 保证补丁在 PCL 更新后依然生效
// 用法：PCL2Plus.exe [--run^|--ensure^|--restore^|--status]
// C# 5 语法（csc v4.0.30319 可编译）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Resources;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal sealed class EmbeddedResolver : BaseAssemblyResolver
{
    private readonly Dictionary<string, AssemblyDefinition> _cache =
        new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);

    public void LoadEmbedded(ModuleDefinition module)
    {
        foreach (var res in module.Resources)
        {
            var er = res as EmbeddedResource;
            if (er == null || !er.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using (var rs = new ResourceReader(er.GetResourceStream()))
                {
                    var e = rs.GetEnumerator();
                    while (e.MoveNext())
                    {
                        var bytes = e.Value as byte[];
                        if (bytes == null || bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z') continue;
                        try
                        {
                            var a = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes, false),
                                new ReaderParameters { AssemblyResolver = this, ReadingMode = ReadingMode.Immediate });
                            if (!_cache.ContainsKey(a.Name.Name)) _cache[a.Name.Name] = a;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        return Resolve(name, new ReaderParameters { AssemblyResolver = this });
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        AssemblyDefinition a;
        if (_cache.TryGetValue(name.Name, out a)) return a;
        a = base.Resolve(name, parameters);
        _cache[name.Name] = a;
        return a;
    }
}

internal static class Program
{
    private const string GateText = "不再提供 MC 下载功能";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string mode = args.Length > 0 ? args[0] : "--run";

        // --wait-pid N：等旧 PCL2 进程退出后再改写 exe（否则文件被占用会写失败）
        int waitPid = 0;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--wait-pid") int.TryParse(args[i + 1], out waitPid);
        if (waitPid > 0)
        {
            Console.WriteLine("[i] 等待旧进程退出：" + waitPid);
            for (int t = 0; t < 40; t++)
            {
                bool alive = false;
                try { System.Diagnostics.Process.GetProcessById(waitPid); alive = true; }
                catch { }
                if (!alive) break;
                System.Threading.Thread.Sleep(200);
            }
            System.Threading.Thread.Sleep(400);
        }

        // --exe <path>：显式指定要注入的 PCL 可执行文件（默认取同目录下的名字）
        string exeArg = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--exe") exeArg = args[i + 1];

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string exe = string.IsNullOrEmpty(exeArg)
            ? Path.Combine(baseDir, "Plain Craft Launcher 2.exe")
            : Path.GetFullPath(exeArg);
        if (string.IsNullOrEmpty(exeArg)) baseDir = AppDomain.CurrentDomain.BaseDirectory;
        else baseDir = Path.GetDirectoryName(exe);
        if (!File.Exists(exe))
        {
            Console.WriteLine("[x] 找不到 PCL 可执行文件：" + exe);
            Pause();
            return 2;
        }

        string pluginDll = Path.Combine(baseDir, "PCL2Plus.dll");
        string pclDir = Path.Combine(baseDir, "PCL");
        string pluginDir = Path.Combine(pclDir, "Plugins");
        string baselineDir = Path.Combine(pluginDir, "baseline");
        Directory.CreateDirectory(baselineDir);

        string pending = Path.Combine(pluginDir, "download-restore.pending");
        string enabled = Path.Combine(pluginDir, "download-restore.enabled");
        string baseline = Path.Combine(baselineDir, "PCL2.base.exe");

        bool wantDownload = File.Exists(pending) || File.Exists(enabled);

        bool noUi = false;
        bool force = false;
        string patchCsv = null, hookDll = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--patch") patchCsv = args[i + 1];
            if (args[i] == "--hook") hookDll = args[i + 1];
        }
        bool hookOnly = false;
        for (int i = 0; i < args.Length; i++) if (args[i] == "--hook-only") hookOnly = true;
        for (int i = 0; i < args.Length; i++) if (args[i] == "--force") force = true;
        for (int i = 0; i < args.Length; i++) if (args[i] == "--no-ui") noUi = true;

        if (mode == "--restore")
        {
            string bak = exe + ".pcl2plus.bak";
            string baselineFile = Path.Combine(baselineDir, "PCL2.base.exe");

            string src = File.Exists(bak) ? bak : (File.Exists(baselineFile) ? baselineFile : null);
            if (src == null) { Console.WriteLine("[x] 找不到备份（.pcl2plus.bak 或 baseline）"); return 3; }

            try
            {
                Exception lastR = null;
                for (int i = 0; i < 10; i++)
                {
                    try { File.Copy(src, exe, true); lastR = null; break; }
                    catch (Exception e) { lastR = e; System.Threading.Thread.Sleep(300); }
                }
                if (lastR != null) { Console.WriteLine("[x] 还原失败：" + lastR.Message); return 4; }

                try { if (File.Exists(enabled)) File.Delete(enabled); } catch { }
                try { if (File.Exists(pending)) File.Delete(pending); } catch { }
                Console.WriteLine("[√] 已还原为官方原版：" + src);
                Console.WriteLine("    目标：" + exe + "  (" + new FileInfo(exe).Length + " 字节)");
                return 0;
            }
            catch (Exception ex2) { Console.WriteLine("[x] 还原异常：" + ex2.Message); return 5; }
        }

        if (mode == "--status")
        {
            var st = Inspect(exe);
            Console.WriteLine("目标      : " + exe);
            Console.WriteLine("版本      : " + FileVersion(exe));
            Console.WriteLine("插件钩子  : " + (st.Hook ? "已挂" : "未挂"));
            Console.WriteLine("下载补丁  : " + (st.Download ? "已应用" : "未应用"));
            Console.WriteLine("插件 DLL  : " + (File.Exists(pluginDll) ? "就绪" : "缺失"));
            Console.WriteLine("开关标记  : " + (wantDownload ? "启用" : "关闭"));
            Console.WriteLine("基线副本  : " + (File.Exists(baseline) ? "有" : "无"));
            Pause();
            return 0;
        }

        if (mode == "--restore")
        {
            File.Delete(pending);
            if (File.Exists(enabled)) File.Delete(enabled);
            wantDownload = false;
            Console.WriteLine("[i] 已关闭下载补丁开关");
        }

        if (mode != "--status" && File.Exists(pending))
        {
            File.WriteAllText(enabled, DateTime.Now.ToString("s"));
            File.Delete(pending);
            wantDownload = true;
        }

        // 插件 DLL 就位
        if (!File.Exists(pluginDll))
        {
            string local = Path.Combine(baseDir, "plugin", "PCL2Plus.dll");
            if (File.Exists(local)) File.Copy(local, pluginDll, true);
        }
        if (!File.Exists(pluginDll))
        {
            Console.WriteLine("[!] 缺少 PCL2Plus.dll，插件页将不可用（补丁仍可应用）");
        }

        bool changed = Ensure(exe, baseline, pluginDll, wantDownload, noUi, force, hookOnly, patchCsv, hookDll);
        Console.WriteLine(changed ? "[√] 已更新 PCL2（基线 + 钩子" + (wantDownload ? " + 下载补丁" : "") + "）"
                                 : "[√] 已是目标状态，无需修改");

        if (mode == "--run")
        {
            var psi = new ProcessStartInfo(exe) { WorkingDirectory = baseDir, UseShellExecute = true };
            Process.Start(psi);
        }
        else Pause();
        return 0;
    }

    private static void Pause()
    {
        if (Environment.UserInteractive)
        {
            Console.WriteLine("按任意键继续…");
            try { Console.ReadKey(true); } catch { }
        }
    }

    private static string FileVersion(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).FileVersion; } catch { return "?"; }
    }

    private static string Sha(string path)
    {
        using (var sha = SHA256.Create())
        using (var fs = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
    }

    private struct State { public bool Hook; public bool Download; public bool NavSlot; }

    private static State Inspect(string exe)
    {
        var st = new State();
        try
        {
            using (var asm = AssemblyDefinition.ReadAssembly(exe,
                new ReaderParameters { ReadingMode = ReadingMode.Deferred }))
            {
                var frm = asm.MainModule.Types.FirstOrDefault(t => t.FullName == "PCL.FormMain");
                if (frm != null)
                {
                    var ctor = frm.Methods.FirstOrDefault(m => m.Name == ".ctor");
                    if (ctor != null && ctor.HasBody)
                    {
                        st.Hook = ctor.Body.Instructions.Any(i =>
                            i.OpCode == OpCodes.Call && i.Operand is MethodReference &&
                            ((MethodReference)i.Operand).DeclaringType.Name == "Bootstrap" &&
                            ((MethodReference)i.Operand).Name == "Init");
                    }
                }
                var setup = asm.MainModule.Types.FirstOrDefault(t => t.FullName == "PCL.PageSetupUI");
                if (setup != null)
                {
                    var hr = setup.Methods.FirstOrDefault(m => m.Name == "HiddenRefresh");
                    if (hr != null && hr.HasBody)
                    {
                        var ins = hr.Body.Instructions;
                        for (int i = 0; i < ins.Count - 1; i++)
                        {
                            var mr = ins[i].Operand as MethodReference;
                            if (mr != null && mr.Name == "get_BtnTitleSelect2" &&
                                ins[i + 1].OpCode == OpCodes.Ldc_I4_0)
                            { st.NavSlot = true; break; }
                        }
                    }
                }

                var pd = asm.MainModule.Types.FirstOrDefault(t => t.FullName == "PCL.PageDownloadInstall");
                if (pd != null)
                {
                    var li = pd.Methods.FirstOrDefault(m => m.Name == "LoaderInit");
                    st.Download = li != null && li.HasBody && li.Body.Instructions.Any(i =>
                        i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference &&
                        ((FieldReference)i.Operand).Name == "DlClientListLoader");
                }
            }
        }
        catch { }
        return st;
    }

    /// <summary>确保 exe = 基线 + 钩子 [+ 下载补丁]</summary>
    private static bool Ensure(string exe, string baseline, string pluginDll, bool wantDownload, bool noUi, bool force, bool hookOnly, string patchCsv, string hookDll)
    {
        State cur = Inspect(exe);

        // 1) 维护基线：当前 exe 未打钩子（=官方原版）且版本/内容变化时刷新基线
        // 基线判定：优先用首次注入前保存的 .pcl2plus.bak；否则仅在"既无钩子也无下载补丁"时认为它是原版
        if (!File.Exists(baseline) && File.Exists(exe + ".pcl2plus.bak"))
        {
            File.Copy(exe + ".pcl2plus.bak", baseline, true);
            Console.WriteLine("[i] 从备份取回基线：" + exe + ".pcl2plus.bak");
        }
        if (!File.Exists(baseline) && !cur.Hook && !cur.Download)
        {
            if (!File.Exists(baseline) || Sha(baseline) != Sha(exe))
            {
                File.Copy(exe, baseline, true);
                Console.WriteLine("[i] 已刷新基线副本");
            }
        }
        if (!File.Exists(baseline))
        {
            Console.WriteLine("[x] 缺少基线副本，无法继续");
            return false;
        }

        // 2) 目标状态
        bool need = force || (noUi || hookOnly
            ? (cur.Hook || (!hookOnly && cur.NavSlot) || cur.Download != wantDownload)
            : (!cur.Hook || !cur.NavSlot || (wantDownload && !cur.Download) || (!wantDownload && cur.Download)));
        if (!need) return false;

        string tmp = Path.Combine(Path.GetTempPath(), "pcl2plus_" + Guid.NewGuid().ToString("N") + ".exe");
        var resolver = new EmbeddedResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(baseline)));
        var rp = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadingMode = ReadingMode.Immediate,
            InMemory = true,
        };

        using (var asm = AssemblyDefinition.ReadAssembly(baseline, rp))
        {
            resolver.LoadEmbedded(asm.MainModule);

            bool wantDownloadFinal = wantDownload;
            if (patchCsv != null) wantDownloadFinal = patchCsv.Contains("download");
            bool wantLegal = patchCsv == null || patchCsv.Contains("legal");
            bool wantDispatch = patchCsv == null || patchCsv.Contains("dispatch");

            if (wantDownloadFinal)
            {
                PatchDownload(asm);
            }
            if (wantDispatch) DispatchRestorer.Patch(asm);    // 先恢复派发（它需要看到原始的法律抛错位置）
            if (wantLegal) LegalGatePatcher.Patch(asm);
            // 诊断插桩已停用（VB 的 Filter 处理器无法安全插桩），保留干净版本   // 解除"法律法规"硬阻拦（整合包安装 / 拖入安装）
            // --no-ui：不注入插件页与宿主钩子，只保留下载补丁
            string hookTarget = hookDll != null ? Path.Combine(Path.GetDirectoryName(exe), hookDll) : pluginDll;
            bool hookWanted = hookDll != null || (patchCsv == null && !noUi);
            if (hookWanted && !string.IsNullOrEmpty(hookTarget) && File.Exists(hookTarget))
            {
                if (!noUi && !hookOnly && hookDll == null) PatchNavSlot(asm);   // 只有带页面时才放行导航槽
                if (!noUi) InjectHook(asm, hookTarget);
            }

            asm.Write(tmp, new WriterParameters());
        }

        // 3) 覆盖（先备份当前）
        string bak = exe + ".pcl2plus.bak";
        if (!File.Exists(bak)) File.Copy(exe, bak);
        Exception last = null;
        for (int retry = 0; retry < 10; retry++)
        {
            try { File.Copy(tmp, exe, true); last = null; break; }
            catch (Exception e) { last = e; System.Threading.Thread.Sleep(300); }
        }
        if (last != null) { Console.WriteLine("[x] 写入失败（文件被占用）：" + last.Message); return false; }
        File.Delete(tmp);
        Console.WriteLine("[i] 已写入：" + exe);
        return true;
    }

    private static void PatchDownload(AssemblyDefinition asm)
    {
        var ctor = asm.MainModule.Types.First(t => t.FullName == "PCL.PageDownloadLeft")
            .Methods.First(m => m.Name == ".ctor");
        Unhide(ctor, "ItemInstall");
        Unhide(ctor, "LabResource");

        var li = asm.MainModule.Types.First(t => t.FullName == "PCL.PageDownloadInstall")
            .Methods.First(m => m.Name == "LoaderInit");
        var il = li.Body.Instructions;
        int newobj = -1;
        for (int i = 0; i < il.Count; i++)
        {
            var mr = il[i].Operand as MethodReference;
            if (il[i].OpCode == OpCodes.Newobj && mr != null && mr.DeclaringType.Name.StartsWith("LoaderTask`2"))
            { newobj = i; break; }
        }
        if (newobj < 0) return;
        int start = newobj;
        for (int i = newobj - 1; i >= 0; i--)
        {
            var s = il[i].Operand as string;
            if (il[i].OpCode == OpCodes.Ldstr && s != null && s.Contains("下载")) { start = i; break; }
        }
        if (start == newobj) return;

        var field = asm.MainModule.Types.First(t => t.FullName == "PCL.ModDownload")
            .Fields.First(f => f.Name == "DlClientListLoader");
        var imported = asm.MainModule.ImportReference(field);
        var first = il[start];
        for (int k = newobj; k >= start; k--) il.RemoveAt(k);
        il.Insert(start, Instruction.Create(OpCodes.Ldsfld, imported));
        foreach (var instr in il)
        {
            var t = instr.Operand as Instruction;
            if (t != null && !il.Contains(t)) instr.Operand = first;
            var arr = instr.Operand as Instruction[];
            if (arr != null)
                for (int j = 0; j < arr.Length; j++)
                    if (!il.Contains(arr[j])) arr[j] = first;
        }
    }

    private static void Unhide(MethodDefinition m, string prop)
    {
        var il = m.Body.Instructions;
        for (int i = 0; i < il.Count - 1; i++)
        {
            var mr = il[i].Operand as MethodReference;
            if (mr == null || mr.Name != "get_" + prop) continue;
            var nxt = il[i + 1];
            if (nxt.OpCode != OpCodes.Ldc_I4_0)
            {
                nxt.OpCode = OpCodes.Ldc_I4_0;
                nxt.Operand = null;
            }
            return;
        }
    }

    /// <summary>PageSetupUI.HiddenRefresh 会把索引 2 的导航按钮强制 Collapsed，改成 Visible</summary>
    private static void PatchNavSlot(AssemblyDefinition asm)
    {
        var setup = asm.MainModule.Types.FirstOrDefault(t => t.FullName == "PCL.PageSetupUI");
        if (setup == null) return;
        var hr = setup.Methods.FirstOrDefault(m => m.Name == "HiddenRefresh");
        if (hr == null || !hr.HasBody) return;
        var ins = hr.Body.Instructions;
        for (int i = 0; i < ins.Count - 1; i++)
        {
            var mr = ins[i].Operand as MethodReference;
            if (mr == null || mr.Name != "get_BtnTitleSelect2") continue;
            var nxt = ins[i + 1];
            if (nxt.OpCode != OpCodes.Ldc_I4_0)
            {
                nxt.OpCode = OpCodes.Ldc_I4_0;
                nxt.Operand = null;
                Console.WriteLine("[i] 已放行导航槽 2（HiddenRefresh 不再隐藏「插件」）");
            }
            return;
        }
    }

    /// <summary>在 FormMain..ctor 末尾插入 call PCL2Plus.Bootstrap::Init()</summary>
    private static void InjectHook(AssemblyDefinition asm, string pluginDll)
    {
        var refAsm = AssemblyDefinition.ReadAssembly(pluginDll);
        var bootstrap = refAsm.MainModule.Types.First(t => t.FullName == "PCL2Plus.Bootstrap")
            .Methods.First(m => m.Name == "Init");
        var imported = asm.MainModule.ImportReference(bootstrap);

        var ctor = asm.MainModule.Types.First(t => t.FullName == "PCL.FormMain")
            .Methods.First(m => m.Name == ".ctor");
        var il = ctor.Body.Instructions;

        // 已注入则跳过
        foreach (var i in il)
        {
            var mr = i.Operand as MethodReference;
            if (i.OpCode == OpCodes.Call && mr != null && mr.Name == "Init" &&
                mr.DeclaringType.Name == "Bootstrap") return;
        }

        int lastRet = -1;
        for (int i = 0; i < il.Count; i++) if (il[i].OpCode == OpCodes.Ret) lastRet = i;
        if (lastRet < 0) return;

        var ip = ctor.Body.GetILProcessor();
        var anchor = il[lastRet];
        ip.InsertBefore(anchor, Instruction.Create(OpCodes.Nop));
        ip.InsertBefore(anchor, Instruction.Create(OpCodes.Call, imported));
        ip.InsertBefore(anchor, Instruction.Create(OpCodes.Nop));
    }
}
