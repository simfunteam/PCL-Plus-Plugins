// 从主程序内嵌资源中供给依赖程序集，解决 Cecil 写入时的 AssemblyResolutionException
using System;
using System.Collections.Generic;
using System.IO;
using System.Resources;
using Mono.Cecil;

internal sealed class EmbeddedResolver : BaseAssemblyResolver
{
    private readonly Dictionary<string, AssemblyDefinition> _cache =
        new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);

    public void LoadEmbedded(ModuleDefinition module)
    {
        foreach (var res in module.Resources)
        {
            var er = res as EmbeddedResource;
            if (er == null || !er.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                using (var rs = new ResourceReader(er.GetResourceStream()))
                {
                    var e = rs.GetEnumerator();
                    while (e.MoveNext())
                    {
                        var name = e.Key as string;
                        if (string.IsNullOrEmpty(name)) continue;
                        var val = e.Value;
                        var bytes = val as byte[];
                        if (bytes == null) continue;
                        if (!LooksLikeAssembly(bytes)) continue;
                        try
                        {
                            var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes, false),
                                new ReaderParameters { AssemblyResolver = this, ReadingMode = ReadingMode.Immediate });
                            if (!_cache.ContainsKey(asm.Name.Name))
                                _cache[asm.Name.Name] = asm;
                        }
                        catch { /* 不是合法程序集，跳过 */ }
                    }
                }
            }
            catch { /* 非标准 resources 容器，跳过 */ }
        }
        Console.WriteLine(string.Format("[i] 内嵌依赖已装载：{0} 个（{1}）",
            _cache.Count, string.Join(", ", new List<string>(_cache.Keys))));
    }

    private static bool LooksLikeAssembly(byte[] b)
    {
        return b.Length > 0x40 && b[0] == 'M' && b[1] == 'Z';
    }

    // 只重写双参版本，单参转调自身实现，避免 base 内部回调造成无限递归
    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        return Resolve(name, new ReaderParameters { AssemblyResolver = this });
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        AssemblyDefinition a;
        if (_cache.TryGetValue(name.Name, out a)) return a;
        try
        {
            a = base.Resolve(name, parameters);
            _cache[name.Name] = a;
            return a;
        }
        catch (AssemblyResolutionException)
        {
            Console.WriteLine("[!] 无法解析程序集：" + name.FullName);
            throw;
        }
    }
}
