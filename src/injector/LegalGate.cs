// 解除 PCL 2.99 里"为遵守相关法律法规，PCL 不再提供 MC 下载功能"的硬阻拦
// 扫描全程序集：把 ldstr("…法律…") + newobj Exception(string) + throw 替换成正常返回
using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class LegalGatePatcher
{
    internal static void Patch(AssemblyDefinition asm)
    {
        int n = 0;
        foreach (ModuleDefinition mod in asm.Modules)
            foreach (TypeDefinition td in mod.Types)
                n += PatchType(td);
        Console.WriteLine("[i] 已清除法律法规拦截：" + n + " 处");
    }

    private static int PatchType(TypeDefinition td)
    {
        // 下载页的加载器若被改成空返回，调用方会拿到空数据抛 NRE，
        // 这里整体跳过 PageDownloadInstall，交给 --patch download 去处理。
        if (td.Name != null && td.Name.StartsWith("PageDownloadInstall")) return 0;
        int n = 0;
        foreach (MethodDefinition m in td.Methods) n += PatchMethod(m);
        foreach (TypeDefinition nt in td.NestedTypes) n += PatchType(nt);
        return n;
    }

    private static int PatchMethod(MethodDefinition m)
    {
        if (!m.HasBody) return 0;
        if (m.Name == "ModpackInstall" && m.DeclaringType.Name == "ModModpack") return 0;   // 交给 DispatchRestorer
        int done = 0;
        var il = m.Body.Instructions;

        for (int i = 0; i < il.Count; i++)
        {
            if (il[i].OpCode != OpCodes.Ldstr) continue;
            string text = il[i].Operand as string;
            if (text == null || text.IndexOf("法律") < 0) continue;

            int idxNew = -1, idxThrow = -1;
            for (int k = i + 1; k < il.Count && k <= i + 10; k++)
            {
                if (il[k].OpCode == OpCodes.Newobj && idxNew < 0) idxNew = k;
                if (il[k].OpCode == OpCodes.Throw) { idxThrow = k; break; }
            }
            if (idxNew < 0 || idxThrow < 0) continue;

            il[i].OpCode = OpCodes.Nop; il[i].Operand = null;
            il[idxNew].OpCode = OpCodes.Nop; il[idxNew].Operand = null;

            bool isVoid = m.ReturnType.FullName == "System.Void";
            il[idxThrow].OpCode = isVoid ? OpCodes.Ret : OpCodes.Rethrow;
            done++;
        }
        return done;
    }
}
