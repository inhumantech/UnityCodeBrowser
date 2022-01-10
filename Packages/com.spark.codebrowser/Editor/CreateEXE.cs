using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

public class CreateEXE {
    //====================================================================================================//
    public void Compile() {
        var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Spark", new Version()), "Spark", ModuleKind.Console);
        var module = assembly.MainModule;
        var writeline = module.ImportReference(typeof(Console).GetMethod("WriteLine", new[] { typeof(string) }));
        var read = module.ImportReference(typeof(Console).GetMethod("Read"));
        var readline = module.ImportReference(typeof(Console).GetMethod("ReadLine"));
        var clear = module.ImportReference(typeof(Console).GetMethod("Clear"));

        var program = new TypeDefinition("Spark", "Program", Mono.Cecil.TypeAttributes.Class | Mono.Cecil.TypeAttributes.Public, module.ImportReference(typeof(object)));
        module.Types.Add(program);

        var main = new MethodDefinition("Main", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.ImportReference(typeof(void)));
        program.Methods.Add(main);

        // Ops //
        var IL = main.Body.GetILProcessor();
        {
            IL.Emit(OpCodes.Ldstr, "What");
            IL.Emit(OpCodes.Call, writeline);

            IL.Emit(OpCodes.Call, readline);
            IL.Emit(OpCodes.Pop);

            IL.Emit(OpCodes.Call, clear);

            IL.Emit(OpCodes.Ldstr, "the hell?");
            IL.Emit(OpCodes.Call, writeline);

            IL.Emit(OpCodes.Call, readline);
            IL.Emit(OpCodes.Pop);

            IL.Emit(OpCodes.Ldstr, "1 + 2");
            IL.Emit(OpCodes.Call, writeline);

            IL.Emit(OpCodes.Ldc_I4, 1);
            IL.Emit(OpCodes.Ldc_I4, 2);
            IL.Emit(OpCodes.Add);

            IL.Emit(OpCodes.Call, writeline);

            IL.Emit(OpCodes.Ret);
        }

        assembly.EntryPoint = main;
        assembly.Write(@"c:\Spark\Spark.exe");
    }
}
