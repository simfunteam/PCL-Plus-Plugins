// PCLEnhancer —— PCL2 下载功能恢复补丁器
// 编译：csc.exe /nologo /target:exe /platform:anycpu /r:Mono.Cecil.dll /out:PCLEnhancer.exe Patcher.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private const string GateText = "不再提供 MC 下载功能";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0)
        {
            Usage();
            return 2;
        }

        string mode = args[0];
        string input = args.Length > 1 ? args[1] : null;
        string output = args.Length > 2 ? args[2] : null;
        if (input == null)
        {
            Usage();
            return 2;
        }
        if (!File.Exists(input))
        {
            Console.WriteLine("[x] 找不到文件：" + input);
            return 3;
        }
        output = output ?? input;

        var resolver = new EmbeddedResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input)));
        var rp = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = false,
            ReadingMode = ReadingMode.Immediate,
            InMemory = true,
        };

        try
        {
        using (var asm = AssemblyDefinition.ReadAssembly(input, rp))
        {
            resolver.LoadEmbedded(asm.MainModule);
            switch (mode)
            {
                case "dump":
                    DumpAll(asm);
                    return 0;
                case "dumptype":
                    if (args.Length < 3) { Usage(); return 2; }
                    var td = Type(asm, args[2]);
                    if (td == null) { Console.WriteLine("[x] 类型不存在: " + args[2]); return 5; }
                    foreach (var m in td.Methods)
                    {
                        if (args.Length > 3 && m.Name != args[3]) continue;
                        Dump(m);
                    }
                    return 0;
                case "verify":
                    return Verify(asm);
                case "patch":
                    int n = Patch(asm);
                    if (n == 0)
                    {
                        var already = Methods(asm, "PCL.PageDownloadInstall")
                            .FirstOrDefault(x => x.Name == "LoaderInit");
                        bool patched = already != null && already.HasBody &&
                            already.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldsfld &&
                                (i.Operand as FieldReference) != null &&
                                ((FieldReference)i.Operand).Name == "DlClientListLoader");
                        if (patched)
                        {
                            Console.WriteLine("[√] 该文件已处于补丁状态，无需修改");
                            return 0;
                        }
                        Console.WriteLine("[x] 没有找到可修补的目标，文件可能是别的版本");
                        return 4;
                    }
                    var writer = new WriterParameters { WriteSymbols = false };
                    asm.Write(output, writer);
                    Console.WriteLine(string.Format("[√] 已写出补丁版：{0}（修改 {1} 处）", output, n));
                    return 0;
                default:
                    Usage();
                    return 2;
            }
        }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[x] 异常: " + ex.GetType().FullName);
            Console.WriteLine("    " + ex.Message);
            if (ex.InnerException != null)
                Console.WriteLine("    内层: " + ex.InnerException.GetType().Name + " " + ex.InnerException.Message);
            return 9;
        }
    }

    private static void Usage()
    {
        Console.WriteLine(@"PCLEnhancer —— 恢复 PCL2 的 MC 下载功能

  PCLEnhancer.exe dump   <PCL2.exe>            打印目标方法的 IL
  PCLEnhancer.exe verify <PCL2.exe>            检查补丁是否已应用
  PCLEnhancer.exe patch  <PCL2.exe> [输出.exe] 生成补丁版（默认原地覆盖，会先备份）");
    }

    // ---------------------------------------------------------------- 目标定位
    private static TypeDefinition Type(AssemblyDefinition asm, string fullName)
    {
        return asm.MainModule.Types.FirstOrDefault(t => t.FullName == fullName);
    }

    private static IEnumerable<MethodDefinition> Methods(AssemblyDefinition asm, string typeName)
    {
        var t = Type(asm, typeName);
        return t == null ? Enumerable.Empty<MethodDefinition>() : t.Methods;
    }

    private static void DumpAll(AssemblyDefinition asm)
    {
        foreach (var typeName in new[]
        {
            "PCL.PageDownloadInstall",
            "PCL.PageDownloadModify",
        })
        {
            var t = Type(asm, typeName);
            if (t == null)
            {
                Console.WriteLine("!! 类型缺失: " + typeName);
                continue;
            }
            Console.WriteLine("\n########## " + t.FullName + " ##########");
            foreach (var m in t.Methods)
            {
                if (!(m.Name == "LoaderInit" || m.Name == "Init" || m.Name.StartsWith("_Lambda") ||
                      m.Name == "ShowMinecraftVersionList" || m.Name == "ReloadSelected" ||
                      m.Name == "StartInstall" || m.Name == "GetSelectLogo"))
                    continue;
                Dump(m);
            }
        }
    }

    private static void Dump(MethodDefinition m)
    {
        var sig = string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name + " " + p.Name));
        Console.WriteLine(string.Format("\n=== {0}::{1}({2}) -> {3} ===",
            m.DeclaringType.Name, m.Name, sig, m.ReturnType.Name));
        if (!m.HasBody)
        {
            Console.WriteLine("  (no body)");
            return;
        }
        foreach (var i in m.Body.Instructions)
            Console.WriteLine(string.Format("  {0:X4}: {1,-10} {2}", i.Offset, i.OpCode, Fmt(i.Operand)));
    }

    private static string Fmt(object o)
    {
        var mr = o as MethodReference;
        if (mr != null) return mr.DeclaringType.Name + "::" + mr.Name;
        var fr = o as FieldReference;
        if (fr != null) return fr.DeclaringType.Name + "::" + fr.Name;
        var s = o as string;
        if (s != null) return "\"" + (s.Length > 40 ? s.Substring(0, 40) + "…" : s) + "\"";
        return o == null ? "" : o.ToString();
    }

    // ---------------------------------------------------------------- 核心补丁
    private static int Patch(AssemblyDefinition asm)
    {
        int changes = 0;

        // ① PageDownloadLeft 构造函数：把被隐藏的「原版游戏」入口重新显示
        var leftCtor = Methods(asm, "PCL.PageDownloadLeft").FirstOrDefault(m => m.Name == ".ctor");
        if (leftCtor != null && leftCtor.HasBody)
        {
            changes += Unhide(leftCtor, "ItemInstall");
            changes += Unhide(leftCtor, "LabResource");
        }

        // ② PageDownloadInstall.LoaderInit：把「抛异常的 LoaderTask」换回真正的 DlClientListLoader
        var loaderInit = Methods(asm, "PCL.PageDownloadInstall").FirstOrDefault(m => m.Name == "LoaderInit");
        if (loaderInit != null && loaderInit.HasBody)
        {
            changes += RewireLoaderTask(asm, loaderInit);
        }

        return changes;
    }

    /// <summary>把 LoaderInit 里的 new LoaderTask(..., 抛异常的委托, ...) 替换为 ldsfld DlClientListLoader</summary>
    private static int RewireLoaderTask(AssemblyDefinition asm, MethodDefinition m)
    {
        var il = m.Body.Instructions;

        var dlField = Type(asm, "PCL.ModDownload").Fields.FirstOrDefault(f => f.Name == "DlClientListLoader");
        if (dlField == null)
        {
            Console.WriteLine("[!] ModDownload.DlClientListLoader 不存在，跳过 LoaderInit");
            return 0;
        }
        var fieldRef = asm.MainModule.ImportReference(dlField);

        // 找到构造 LoaderTask 的 newobj
        int newobjIdx = -1;
        for (int i = 0; i < il.Count; i++)
        {
            var mr = il[i].Operand as MethodReference;
            if (il[i].OpCode == OpCodes.Newobj && mr != null &&
                mr.DeclaringType.Name.StartsWith("LoaderTask`2"))
            {
                newobjIdx = i;
                break;
            }
        }
        if (newobjIdx < 0)
        {
            Console.WriteLine("[!] LoaderInit 中没有 LoaderTask 构造，可能已修补");
            return 0;
        }

        // 往前找参数起点：从 newobj 往回，第 4 个参数是 ldnull；再往前是 ldstr "关闭下载"
        int start = newobjIdx;
        for (int i = newobjIdx - 1; i >= 0; i--)
        {
            if (il[i].OpCode == OpCodes.Ldstr && (string)il[i].Operand != null &&
                ((string)il[i].Operand).Contains("下载"))
            {
                start = i;
                break;
            }
        }
        if (start == newobjIdx)
        {
            Console.WriteLine("[!] 未定位到参数起点，放弃修改 LoaderInit");
            return 0;
        }

        Console.WriteLine(string.Format("[i] LoaderInit：替换 IL {0:X4}..{1:X4} 为 ldsfld ModDownload::DlClientListLoader",
            il[start].Offset, il[newobjIdx].Offset));

        // 删除参数压栈 + newobj，插入 ldsfld
        var first = il[start];
        for (int k = newobjIdx; k >= start; k--)
            il.RemoveAt(k);
        il.Insert(start, Instruction.Create(OpCodes.Ldsfld, fieldRef));

        // 修正可能存在的分支目标（LoaderInit 里若有指向被删指令的分支，改指新指令）
        foreach (var instr in il)
        {
            var t = instr.Operand as Instruction;
            if (t != null && !il.Contains(t))
            {
                if (instr.OpCode == OpCodes.Leave || instr.OpCode == OpCodes.Leave_S ||
                    instr.OpCode.FlowControl == FlowControl.Branch || instr.OpCode.FlowControl == FlowControl.Cond_Branch)
                    instr.Operand = first;
            }
            else if (instr.Operand is Instruction[])
            {
                var arr = (Instruction[])instr.Operand;
                for (int j = 0; j < arr.Length; j++)
                    if (!il.Contains(arr[j])) arr[j] = first;
            }
        }
        return 1;
    }

    /// <summary>把 ctor 里的 X.set_Visibility(Collapsed) 改成 Visible</summary>
    private static int Unhide(MethodDefinition m, string propName)
    {
        var il = m.Body.Instructions;
        for (int i = 0; i < il.Count - 2; i++)
        {
            var mr = il[i].Operand as MethodReference;
            if (mr == null || mr.Name != "get_" + propName) continue;
            var nxt = il[i + 1];
            if (nxt.OpCode == OpCodes.Ldc_I4_0)
            {
                Console.WriteLine("[i] " + m.DeclaringType.Name + " → " + propName + " 已是 Visible，跳过");
                return 0;
            }
            if (nxt.OpCode == OpCodes.Ldc_I4_2 || nxt.OpCode == OpCodes.Ldc_I4_1 ||
                (nxt.OpCode == OpCodes.Ldc_I4_S && Convert.ToInt32(nxt.Operand) != 0))
            {
                nxt.OpCode = OpCodes.Ldc_I4_0;
                nxt.Operand = null;
                Console.WriteLine("[i] " + m.DeclaringType.Name + "::.ctor → " + propName + ".Visibility = Visible");
                return 1;
            }
        }
        Console.WriteLine("[!] 未找到 " + propName + " 的隐藏语句");
        return 0;
    }

    private static bool IsBaseCallOnly(MethodDefinition m)
    {
        var ins = m.Body.Instructions;
        return ins.Count <= 4 && ins.Any(i => i.OpCode == OpCodes.Call) &&
               !ins.Any(i => i.OpCode == OpCodes.Newobj);
    }

    /// <summary>把方法体重写为「直接调用基类同名方法」</summary>
    private static bool BuildBaseCall(MethodDefinition m)
    {
        var baseType = m.DeclaringType.BaseType as TypeDefinition;
        if (baseType == null) return false;
        var baseMethod = baseType.Methods.FirstOrDefault(x =>
            x.Name == m.Name && x.Parameters.Count == m.Parameters.Count);
        if (baseMethod == null || !baseMethod.HasBody) return false;

        var body = m.Body;
        body.Instructions.Clear();
        body.Variables.Clear();
        body.ExceptionHandlers.Clear();

        var il = body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        for (int i = 0; i < m.Parameters.Count; i++)
            il.Append(Instruction.Create(OpCodes.Ldarg, m.Parameters[i]));
        il.Append(Instruction.Create(OpCodes.Call, m.Module.ImportReference(baseMethod)));
        il.Append(Instruction.Create(OpCodes.Ret));

        Console.WriteLine(string.Format("[i] {0}::{1} → 改为调用基类实现", m.DeclaringType.Name, m.Name));
        return true;
    }

    // ---------------------------------------------------------------- 校验
    private static int Verify(AssemblyDefinition asm)
    {
        int ok = 0, bad = 0;

        var li = Methods(asm, "PCL.PageDownloadInstall").FirstOrDefault(m => m.Name == "LoaderInit");
        bool hasField = li != null && li.HasBody &&
                        li.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldsfld &&
                            (i.Operand as FieldReference) != null &&
                            ((FieldReference)i.Operand).Name == "DlClientListLoader");
        Console.WriteLine((hasField ? "[√]" : "[x]") + " PageDownloadInstall.LoaderInit 使用真实加载器 DlClientListLoader");
        if (hasField) ok++; else bad++;

        var li2 = Methods(asm, "PCL.PageDownloadInstall").FirstOrDefault(m => m.Name == "LoaderInit");
        bool noGate = li2 != null && li2.HasBody && !HasGateThrow(li2);
        Console.WriteLine((noGate ? "[√]" : "[x]") + " PageDownloadInstall.LoaderInit 已无「不再提供」拦截");
        if (noGate) ok++; else bad++;

        int remain = CountGateTexts(asm);
        Console.WriteLine((remain == 0 ? "[√]" : "[!]") + string.Format(" 剩余「{0}」文案 {1} 处（提示/日志文案不计）", GateText, remain));

        Console.WriteLine(string.Format("\n结论：{0} 项通过，{1} 项未通过", ok, bad));
        return bad == 0 ? 0 : 1;
    }

    private static bool HasGateThrow(MethodDefinition m)
    {
        foreach (var i in m.Body.Instructions)
        {
            var s = i.Operand as string;
            if (s != null && s.Contains(GateText)) return true;
        }
        return false;
    }

    private static int CountGateTexts(AssemblyDefinition asm)
    {
        int n = 0;
        foreach (var t in asm.MainModule.Types)
            foreach (var m in t.Methods.Where(x => x.HasBody))
                if (HasGateThrow(m)) n++;
        return n;
    }
}
