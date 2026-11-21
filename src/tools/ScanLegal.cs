// 独立扫描器：列出指定 exe 里所有包含「法律」字符串的 IL 指令及其所在方法
using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class ScanLegal
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("用法：ScanLegal.exe <某 PCL.exe>");
            return 1;
        }
        var rp = new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true };
        using (var asm = AssemblyDefinition.ReadAssembly(args[0], rp))
        {
            int n = 0;
            foreach (ModuleDefinition mod in asm.Modules)
                foreach (TypeDefinition td in mod.Types)
                    n += Scan(td);
            Console.WriteLine("---- 合计命中：" + n);
        }
        return 0;
    }

    private static int Scan(TypeDefinition td)
    {
        int n = 0;
        foreach (MethodDefinition m in td.Methods)
        {
            if (!m.HasBody) continue;
            var il = m.Body.Instructions;
            for (int i = 0; i < il.Count; i++)
            {
                string s = il[i].Operand as string;
                if (s == null || s.IndexOf("法律") < 0) continue;

                string next = i + 1 < il.Count ? il[i + 1].OpCode.Name : "-";
                string next2 = i + 2 < il.Count ? il[i + 2].OpCode.Name : "-";
                Console.WriteLine(td.FullName + " :: " + m.Name);
                Console.WriteLine("    IL_" + il[i].Offset.ToString("X4") + " ldstr  \"" + s + "\"");
                Console.WriteLine("    后续指令: " + next + " , " + next2);
                n++;
                break;
            }
        }
        foreach (TypeDefinition nt in td.NestedTypes) n += Scan(nt);
        return n;
    }
}
