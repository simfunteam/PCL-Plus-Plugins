// NiyouPacker —— 把插件工程打包成 .niyou
// 用法：
//   NiyouPacker.exe <插件工程目录> <输出.niyou>
//   NiyouPacker.exe --list <某.niyou>
//
// 插件工程结构：
//   manifest.json          必需：id/name/version/author/description/patches/hook
//   payload/               可选：要随插件放到 PCL 目录的 DLL 等文件
//
// .niyou 就是一个 zip，内部固定：
//   manifest.json
//   payload/*              （原样拷贝到 PCL 目录）
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web.Script.Serialization;

internal static class NiyouPacker
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--list") { List(args[1]); return 0; }
            if (args.Length != 2)
            {
                Console.WriteLine("用法：NiyouPacker.exe <插件工程目录> <输出.niyou>");
                Console.WriteLine("      NiyouPacker.exe --list <某.niyou>");
                return 1;
            }

            string src = Path.GetFullPath(args[0]);
            string dst = Path.GetFullPath(args[1]);

            string manifestPath = Path.Combine(src, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Console.WriteLine("[x] 缺少 manifest.json：" + manifestPath);
                return 2;
            }

            // 校验 manifest
            string text = File.ReadAllText(manifestPath, Encoding.UTF8);
            var ser = new JavaScriptSerializer();
            var obj = (Dictionary<string, object>)ser.DeserializeObject(text);
            foreach (string key in new[] { "id", "name", "version" })
                if (!obj.ContainsKey(key) || string.IsNullOrEmpty(Convert.ToString(obj[key])))
                {
                    Console.WriteLine("[x] manifest 缺少字段：" + key);
                    return 3;
                }

            if (File.Exists(dst)) File.Delete(dst);
            using (var zip = ZipFile.Open(dst, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(manifestPath, "manifest.json", CompressionLevel.Optimal);

                string payload = Path.Combine(src, "payload");
                int n = 0;
                if (Directory.Exists(payload))
                    foreach (string f in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
                    {
                        string rel = "payload/" + f.Substring(payload.Length).TrimStart('\\', '/').Replace('\\', '/');
                        zip.CreateEntryFromFile(f, rel, CompressionLevel.Optimal);
                        n++;
                    }
                Console.WriteLine("[√] 已打包：" + Path.GetFileName(dst));
                Console.WriteLine("    插件：" + obj["name"] + "  v" + obj["version"]);
                Console.WriteLine("    附带文件：" + n + " 个");
                Console.WriteLine("    大小：" + new FileInfo(dst).Length / 1024 + " KB");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[x] " + ex.Message);
            return 9;
        }
    }

    private static void List(string file)
    {
        using (var zip = ZipFile.OpenRead(file))
        {
            var e = zip.GetEntry("manifest.json");
            if (e == null) { Console.WriteLine("[x] 不是有效的 .niyou（缺少 manifest.json）"); return; }
            using (var sr = new StreamReader(e.Open(), Encoding.UTF8))
                Console.WriteLine(sr.ReadToEnd());
            Console.WriteLine("---- 内容 ----");
            foreach (var x in zip.Entries) Console.WriteLine("  " + x.FullName + "  (" + x.Length + " 字节)");
        }
    }
}
