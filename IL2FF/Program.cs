using FishLib;
using IL2FF;
using Mono.Cecil;

if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
{
    PrintUsage();
    return 0;
}

var command = args[0].ToLowerInvariant();

switch (command)
{
    case "compile":
        if (args.Length < 2)
        {
            Console.WriteLine("E: Invalid syntax for 'compile'.");
            Console.WriteLine("Usage: fishcc compile <file.dll> [out_dir]");
            return 1;
        }
        var outputDir = args.Length > 2 ? args[2] : Directory.GetCurrentDirectory();
        return CompileAssembly(args[1], outputDir);

    case "disasm":
        if (args.Length < 2) return 1;
        var script = Script.FromResource(File.ReadAllBytes(args[1]));
        Console.WriteLine($"Class: {script.Name}, Self: {script.Self}, Super: {script.SuperClassScript}");
        Console.WriteLine("=== STRINGAS TABLE ===");
        for (var i = 0; i < script.AStringTable.Count; i++)
            Console.WriteLine($"  [{i:D2}] {script.AStringTable[i]}");
        Console.WriteLine("\n=== TYPE REFERENCES ===");
        for (var i = 0; i < script.TypeReferences.Count; i++)
        {
            var tr = script.TypeReferences[i];
            var name = tr.TypeNameIndex.HasValue && tr.TypeNameIndex.Value < script.AStringTable.Count ? script.AStringTable[tr.TypeNameIndex.Value] : "none";
            var scriptStr = tr.Script != null ? tr.Script.ToString() : "none";
            Console.WriteLine($"  [{i:D2}] MachineType: {tr.MachineType}, FishType: {tr.FishType}, ArrayBase: {tr.ArrayBaseMachineType}, StringIdx: {tr.TypeNameIndex?.ToString() ?? "-1"} ({name}), Script: {scriptStr}");
        }
        Console.WriteLine("\n=== FUNCTION REFERENCES ===");
        for (var i = 0; i < script.FunctionReferences.Count; i++)
        {
            var fr = script.FunctionReferences[i];
            var name = fr.NameIndex < script.AStringTable.Count ? script.AStringTable[fr.NameIndex] : "N/A";
            Console.WriteLine($"  [{i:D2}] {name} (TypeIdx: {fr.TypeReferenceIndex})");
        }
        Console.WriteLine("\n=== FUNCTION DEFINITIONS ===");
        for (var i = 0; i < script.Functions.Count; i++)
        {
            var fd = script.Functions[i];
            var name = fd.NameStringIndex < script.AStringTable.Count ? script.AStringTable[fd.NameStringIndex] : "N/A";
            Console.WriteLine($"  [{i:D2}] {name} (Mods: {fd.Modifiers}, Bytecode: {fd.BytecodeBegin}..{fd.BytecodeBegin + fd.BytecodeSize}, ArgsBegin: {fd.ArgumentsBegin}, ArgsSize: {fd.ArgumentsSize}, LocVarsBegin: {fd.LocalVariablesBegin}, LocVarsSize: {fd.LocalVariablesSize}, StackSize: {fd.StackSize})");
        }
        Console.WriteLine("\n=== FIELD DEFINITIONS ===");
        for (var i = 0; i < script.FieldDefinitions.Count; i++)
        {
            var f = script.FieldDefinitions[i];
            var name = f.NameIndex < script.AStringTable.Count ? script.AStringTable[f.NameIndex] : "N/A";
            Console.WriteLine($"  [{i:D2}] {name} (Mods: {f.Modifiers}, TypeIdx: {f.TypeReferenceIndex})");
        }
        Console.WriteLine("\n=== FIELD REFERENCES ===");
        for (var i = 0; i < script.FieldReferences.Count; i++)
        {
            var fr = script.FieldReferences[i];
            var name = fr.NameStringIndex < script.AStringTable.Count ? script.AStringTable[fr.NameStringIndex] : "N/A";
            Console.WriteLine($"  [{i:D2}] {name} (TypeIdx: {fr.TypeReferenceIndex})");
        }
        Console.WriteLine("\n=== SHARED ARGUMENTS ===");
        for (var i = 0; i < script.Arguments.Count; i++)
        {
            var sa = script.Arguments[i];
            Console.WriteLine($"  [{i:D2}] TypeRefIdx: {sa.TypeReferenceIndex}, Offset: {sa.Offset} (0x{sa.Offset:X2})");
        }
        Console.WriteLine("\n=== LOCAL VARIABLES ===");
        for (var i = 0; i < script.LocalVariables.Length; i++)
        {
            var lv = script.LocalVariables[i];
            Console.WriteLine($"  [{i:D2}] TypeRefIdx: {lv.TypeReferenceIndex}, Offset: {lv.Offset} (0x{lv.Offset:X2})");
        }
        Console.WriteLine("\n=== INSTRUCTIONS ===");
        for (var i = 0; i < script.Bytecode.Count; i++)
        {
            var bytecode = script.Bytecode[i];
            var instType = InstructionMapper.GetInstructionType(bytecode);
            Console.WriteLine($"  [{i:D4}] {instType,-20} | Bytecode={bytecode}");
        }
        return 0;

    case "genlib":
        if (args.Length < 2)
        {
            Console.WriteLine("E: Invalid syntax for 'genlib'.");
            Console.WriteLine("Usage: fishcc genlib <map> <farc>");
            return 1;
        }

        var map = args[1];
        var farc = args[2];
        Genlib.Generate(map, farc);
        return 0;

    default:
        Console.WriteLine($"Invalid command: '{args[0]}'");
        Console.ResetColor();
        PrintUsage();
        return 1;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  fishcc compile <file.dll> [out_dir]          Convert a CIL assembly to FF scripts");
    Console.WriteLine("  fishcc disasm <file.ff>                      Disassemble a Fish script");
    Console.WriteLine("  fishcc genlib <map> <farc>                   Generate the core library from the game data");
}

static int CompileAssembly(string inputDllPath, string outputDir = "")
{
    if (!File.Exists(inputDllPath))
    {
        Console.WriteLine($"E: no such file or directory '{inputDllPath}'.");
        return 1;
    }

    try
    {
        var resolver = new DefaultAssemblyResolver();
        var dllDir = Path.GetDirectoryName(Path.GetFullPath(inputDllPath));
        if (!string.IsNullOrEmpty(dllDir))
        {
            resolver.AddSearchDirectory(dllDir);
        }

        var assembly = AssemblyDefinition.ReadAssembly(inputDllPath, new() { AssemblyResolver = resolver });
        var module = assembly.MainModule;

        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Directory.GetCurrentDirectory();
        }

        var compiledFiles = Generator.CompileAll(module, outputDir);

        if (compiledFiles.Count != 0) return 0;
        Console.WriteLine($"W: No entrypoint found in '{inputDllPath}'.");
        return 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"E: {ex.Message}");
        return 1;
    }
}