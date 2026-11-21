// 恢复 2.99 被删掉的整合包派发：在 ModpackInstall 里原本 switch(num) 的位置
// 直接调用 InstallPackModrinth（.mrpack / modrinth.index.json），
// 其它格式仍走原来的失败路径（不影响现状）。
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DispatchRestorer
{
    internal static void Patch(AssemblyDefinition asm)
    {
        ModuleDefinition mod = asm.MainModule;
        TypeDefinition modpack = null;
        foreach (TypeDefinition td in mod.Types) if (td.Name == "ModModpack") modpack = td;
        if (modpack == null) { Console.WriteLine("[!] 找不到 ModModpack"); return; }

        MethodDefinition install = null;
        foreach (MethodDefinition m in modpack.Methods)
            if (m.Name == "ModpackInstall" && m.Parameters.Count == 3) install = m;
        if (install == null) { Console.WriteLine("[!] 找不到 ModpackInstall"); return; }

        MethodDefinition modrinth = null;
        foreach (MethodDefinition m in modpack.Methods)
            if (m.Name == "InstallPackModrinth") modrinth = m;
        if (modrinth == null) { Console.WriteLine("[!] 找不到 InstallPackModrinth"); return; }

        var il = install.Body.Instructions;
        int idxStr = -1, idxThrow = -1;
        for (int i = 0; i < il.Count; i++)
        {
            if (il[i].OpCode != OpCodes.Ldstr) continue;
            string s = il[i].Operand as string;
            if (s == null || s.IndexOf("法律") < 0) continue;
            idxStr = i;
            for (int k = i + 1; k < il.Count && k <= i + 6; k++)
                if (il[k].OpCode == OpCodes.Throw) { idxThrow = k; break; }
            if (idxThrow > 0) break;
        }
        if (idxStr < 0 || idxThrow < 0) { Console.WriteLine("[!] 找不到法律抛错位置（可能已打过旧补丁）"); return; }

        // 局部变量：zipArchive、text
        VariableDefinition vZip = null, vBase = null;
        foreach (VariableDefinition v in install.Body.Variables)
        {
            if (v.VariableType.FullName.EndsWith("ZipArchive")) vZip = v;
            if (v.VariableType.FullName == "System.String" && vBase == null) vBase = v;
        }
        if (vZip == null || vBase == null) { Console.WriteLine("[!] 找不到 zipArchive / text 局部变量"); return; }

        // 新增局部保存返回值
        var vRes = new VariableDefinition(install.ReturnType);
        install.Body.Variables.Add(vRes);

        // 结尾块：ldloc res; ret
        Instruction endLabel = Instruction.Create(OpCodes.Ldloc, vRes);
        Instruction endRet = Instruction.Create(OpCodes.Ret);

        // 构造调用序列
        var seq = new[]
        {
            Instruction.Create(OpCodes.Ldarg_0),                 // File
            Instruction.Create(OpCodes.Ldloc, vZip),             // zipArchive
            Instruction.Create(OpCodes.Ldloc, vBase),            // archiveBaseFolder
            Instruction.Create(OpCodes.Ldarg_1),                 // InstanceName
            Instruction.Create(OpCodes.Ldarg_2),                 // Logo
            Instruction.Create(OpCodes.Call, mod.Import(modrinth)),
            Instruction.Create(OpCodes.Stloc, vRes),
            Instruction.Create(OpCodes.Leave, endLabel)
        };

        // 用新序列替换 [idxStr, idxThrow]
        Instruction first = il[idxStr];
        ILProcessor proc = install.Body.GetILProcessor();
        for (int k = idxStr; k <= idxThrow; k++)
        {
            il[k].OpCode = OpCodes.Nop;
            il[k].Operand = null;
        }
        Instruction anchor = il[idxThrow].Next;
        foreach (Instruction ins in seq) proc.InsertBefore(anchor, ins);

        // 把结尾块追加到方法最后
        Instruction last = il[il.Count - 1];
        proc.InsertAfter(last, endLabel);
        proc.InsertAfter(endLabel, endRet);

        Console.WriteLine("[i] 已恢复 mrpack 派发 → InstallPackModrinth");
    }
}
