using System;using Mono.Cecil;using Mono.Cecil.Cil;
internal static class DumpCatch{
  private static int Main(string[] a){
    var rp=new ReaderParameters{ReadingMode=ReadingMode.Immediate,InMemory=true};
    using(var asm=AssemblyDefinition.ReadAssembly(a[0],rp)){
      foreach(ModuleDefinition mod in asm.Modules)
        foreach(TypeDefinition td in mod.Types)
          if(td.Name=="FormMain")
            foreach(TypeDefinition nt in td.NestedTypes){
              if(nt.Name.IndexOf("25-0")<0) continue;
              foreach(MethodDefinition m in nt.Methods){
                if(!m.HasBody) continue;
                Console.WriteLine("方法: "+nt.Name+"::"+m.Name+"  处理器数="+m.Body.ExceptionHandlers.Count);
                foreach(var eh in m.Body.ExceptionHandlers){
                  Console.WriteLine("  处理器 "+eh.HandlerType+"  catch="+(eh.CatchType==null?"null":eh.CatchType.Name)+"  start=IL_"+eh.HandlerStart.Offset.ToString("X4"));
                  Instruction i=eh.HandlerStart; int c=0;
                  while(i!=null && i!=eh.HandlerEnd && c<10){ Console.WriteLine("     IL_"+i.Offset.ToString("X4")+" "+i.OpCode+" "+(i.Operand??"")); i=i.Next; c++; }
                }
              }
            }
    }
    return 0;
  }
}
