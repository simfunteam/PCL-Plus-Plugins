// 诊断注入（稳健版）：
// VB 编译出的 Catch 多为 Filter 处理器，处理器入口栈状态不可靠，
// 所以先找到处理器里第一条 stloc（把异常存进局部变量），在它之后插入：
//   ldloc 该变量; callvirt Exception::get_Message; ldc.i4.2; call ModMain::Hint(...)
using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class CatchInstrumenter
{
    internal static void Patch(AssemblyDefinition asm)
    {
        ModuleDefinition mod = asm.MainModule;
        TypeDefinition modpack = null, modMain = null, formMain = null;
        foreach (TypeDefinition td in mod.Types)
        {
            if (td.Name == "ModModpack") modpack = td;
            if (td.Name == "ModMain") modMain = td;
            if (td.Name == "FormMain") formMain = td;
        }
        if (modpack == null || modMain == null) { Console.WriteLine("[!] 缺少目标类型"); return; }

        MethodDefinition hint = null;
        foreach (MethodDefinition m in modMain.Methods)
            if (m.Name == "Hint" && m.Parameters.Count >= 2 && m.Parameters[0].ParameterType.FullName == "System.String")
            { hint = m; if (m.Parameters.Count == 2) break; }
        if (hint == null) { Console.WriteLine("[!] 找不到 Hint"); return; }

        MethodReference getMessage = mod.Import(typeof(Exception).GetProperty("Message").GetGetMethod());
        MethodReference hintRef = mod.Import(hint);

        int patched = 0;
        foreach (MethodDefinition m in modpack.Methods)
        {
            if (m.Name != "ModpackInstall" || !m.HasBody || m.Parameters.Count != 3) continue;
            patched += Instrument(m, getMessage, hint, hintRef);
        }
        if (formMain != null)
            foreach (TypeDefinition nt in formMain.NestedTypes)
            {
                if (nt.Name.IndexOf("25-0") < 0) continue;
                foreach (MethodDefinition m in nt.Methods)
                    if (m.HasBody) patched += Instrument(m, getMessage, hint, hintRef);
            }

        Console.WriteLine("[i] 已插入异常诊断提示：" + patched + " 处");
    }

    private static int Instrument(MethodDefinition m, MethodReference getMessage,
                                  MethodDefinition hint, MethodReference hintRef)
    {
        int n = 0;
        foreach (ExceptionHandler eh in m.Body.ExceptionHandlers)
        {
            if (eh.HandlerType != ExceptionHandlerType.Catch && eh.HandlerType != ExceptionHandlerType.Filter) continue;
            if (eh.CatchType != null && eh.CatchType.FullName != "System.Exception") continue;

            // 在处理器体内找第一条 stloc / stloc.N
            Instruction st = null;
            for (Instruction i = eh.HandlerStart; i != null && i != eh.HandlerEnd; i = i.Next)
            {
                if (i.OpCode == OpCodes.Stloc || i.OpCode == OpCodes.Stloc_0 ||
                    i.OpCode == OpCodes.Stloc_1 || i.OpCode == OpCodes.Stloc_2 || i.OpCode == OpCodes.Stloc_3)
                { st = i; break; }
            }
            if (st == null) continue;

            ILProcessor il = m.Body.GetILProcessor();
            Instruction anchor = st.Next;      // 从下一条开始插
            Instruction loadVar = null;
            if (st.Operand is VariableDefinition) loadVar = Instruction.Create(OpCodes.Ldloc, (VariableDefinition)st.Operand);
            else if (st.OpCode == OpCodes.Stloc_0) loadVar = Instruction.Create(OpCodes.Ldloc_0);
            else if (st.OpCode == OpCodes.Stloc_1) loadVar = Instruction.Create(OpCodes.Ldloc_1);
            else if (st.OpCode == OpCodes.Stloc_2) loadVar = Instruction.Create(OpCodes.Ldloc_2);
            else if (st.OpCode == OpCodes.Stloc_3) loadVar = Instruction.Create(OpCodes.Ldloc_3);
            if (loadVar == null) continue;

            il.InsertBefore(anchor, loadVar);
            il.InsertBefore(anchor, Instruction.Create(OpCodes.Callvirt, getMessage));
            il.InsertBefore(anchor, Instruction.Create(OpCodes.Ldc_I4_2));
            for (int k = 2; k < hint.Parameters.Count; k++)
            {
                string tn = hint.Parameters[k].ParameterType.FullName;
                il.InsertBefore(anchor, tn == "System.String" ? Instruction.Create(OpCodes.Ldnull)
                                                              : Instruction.Create(OpCodes.Ldc_I4_0));
            }
            il.InsertBefore(anchor, Instruction.Create(OpCodes.Call, hintRef));
            n++;
        }
        return n;
    }
}
