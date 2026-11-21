// 诊断 2：在 InstallPackModrinth 的两处并不可见的抛错点上
//   1) 把异常消息改成可识别标记（MRPACK-JSONERR / MRPACK-NO-MCVER）
//   2) 抛错前先写一行 PCL 日志（Logger.Info），把标记落到 Log1.txt
//   3) 抛错改成返回 null，避免再被 PCL 兜底成"无法确定适合的操作"
using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class ModpackMarker
{
    private const string Marker1 = "MRPACK-JSONERR";
    private const string Marker2 = "MRPACK-NO-MCVER";

    internal static void Patch(AssemblyDefinition asm)
    {
        ModuleDefinition mod = asm.MainModule;
        TypeDefinition modpack = null, logger = null;
        foreach (TypeDefinition td in mod.Types)
        {
            if (td.Name == "ModModpack") modpack = td;
            if (td.Name == "Logger") logger = td;
        }
        if (modpack == null) { Console.WriteLine("[!] 找不到 ModModpack"); return; }

        MethodDefinition logInfo = null;
        if (logger != null)
            foreach (MethodDefinition m in logger.Methods)
                if (m.Name == "Info" && m.Parameters.Count == 3 && m.Parameters[0].ParameterType.FullName == "System.String")
                { logInfo = m; break; }

        MethodReference logRef = logInfo != null ? mod.Import(logInfo) : null;

        int n = 0;
        foreach (MethodDefinition m in modpack.Methods)
        {
            if (m.Name != "InstallPackModrinth" || !m.HasBody) continue;

            var il = m.Body.Instructions;
            for (int i = 0; i < il.Count; i++)
            {
                if (il[i].OpCode != OpCodes.Ldstr) continue;
                string s = il[i].Operand as string;
                if (s == null) continue;

                bool isJson = s.IndexOf("安装信息存在问题") >= 0;
                bool isVer = s.IndexOf("未提供 Minecraft 版本信息") >= 0;
                if (!isJson && !isVer) continue;

                string marker = isJson ? Marker1 : Marker2;
                il[i].Operand = marker;                       // 换成标记

                // 找到这条消息之后的 throw
                int idxThrow = -1;
                for (int k = i + 1; k < il.Count && k <= i + 6; k++)
                    if (il[k].OpCode == OpCodes.Throw) { idxThrow = k; break; }
                if (idxThrow < 0) continue;

                // throw 之前插入日志调用
                if (logRef != null)
                {
                    var ins = new[]
                    {
                        Instruction.Create(OpCodes.Ldstr, marker),
                        Instruction.Create(OpCodes.Ldc_I4_0),
                        Instruction.Create(OpCodes.Ldstr, "PCL2Plus-Diag"),
                        Instruction.Create(OpCodes.Call, logRef)
                    };
                    ILProcessor p = m.Body.GetILProcessor();
                    foreach (var x in ins) p.InsertBefore(il[idxThrow], x);
                }

                // throw 改成 pop（异常对象）+ ldnull + ret
                ILProcessor p2 = m.Body.GetILProcessor();
                Instruction after = il[idxThrow].Next;
                p2.InsertBefore(il[idxThrow], Instruction.Create(OpCodes.Pop));
                il[idxThrow].OpCode = OpCodes.Ldnull;
                il[idxThrow].Operand = null;
                p2.InsertAfter(il[idxThrow], Instruction.Create(OpCodes.Ret));
                n++;
            }
        }
        Console.WriteLine("[i] Modrinth 诊断标记：" + n + " 处" + (logRef == null ? "（未找到 Logger.Info，仅改消息）" : "（已接日志）"));
    }
}
