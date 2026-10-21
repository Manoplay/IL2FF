// ReSharper disable UnusedMember.Global

using CwLibNet.IO.Streams;
using CwLibNet.Types.Data;

namespace FishLib;

public class Script
{
    public byte[]? Buffer;
    public ResourceDescriptor? Self;
    public Header Header = new();
    public ResourceDescriptor? UpToDateScript;
    public string Name = "";
    public ResourceDescriptor? SuperClassScript;
    public Modifiers Modifiers;
    public List<TypeReference> TypeReferences = [];
    public List<FieldReference> FieldReferences = [];
    public List<FunctionReference> FunctionReferences = [];
    public List<FieldDefinition> FieldDefinitions = [];
    public List<PropertyDefinition> PropertyDefinitions = [];
    public List<FunctionDefinition> Functions = [];
    public List<string> AStringTable = [];
    public List<string> WStringTable = [];
    public List<int>? ConstantTableS64;
    public List<float> ConstantTableF32 = [];
    public List<int> DependingGuids = [];
    public List<string> Bytecode = [];
    public List<Argument> Arguments = [];
    public List<ushort> LineNumbers = [];
    public LocalVariable[] LocalVariables = [];

    public Script()
    {
    }

    private Script(MemoryInputStream stream, Header? header = null)
    {
        if (header != null)
            Header = header;

        if (Header.Revision <= 0x33a)
            UpToDateScript = stream.ReadResource(Header);

        Name = stream.ReadClassName(Header);
        SuperClassScript = stream.ReadResource(Header);
        Modifiers = stream.ReadModifiers(Header);
        TypeReferences = stream.ReadTypeReferences(Header);
        FieldReferences = stream.ReadFieldReferences(Header);
        FunctionReferences = stream.ReadFunctionReferences(Header);
        FieldDefinitions = stream.ReadFieldDefinitions(Header);
        PropertyDefinitions = stream.ReadPropertyDefinitions(Header);
        Functions = stream.ReadFunctionDefinitions(Header);
        Arguments = stream.ReadSharedArguments(Header);
        Bytecode = stream.ReadInstructions(Header);
        LineNumbers = stream.ReadLineNumbers(Header);
        LocalVariables = stream.ReadLocalVariables(Header);
        AStringTable = stream.ReadStrings(Header, false);
        WStringTable = stream.ReadStrings(Header, true);
        if (Header.Revision > 0x272)
            ConstantTableS64 = stream.ReadInts(Header);
        ConstantTableF32 = stream.ReadFloatArray(Header);
        if (Header.Revision >= 0x1ec)
            DependingGuids = stream.ReadIntArray(Header, true);
    }

    public Script(byte[] decompressedBytes, Header? header = null)
        : this(new MemoryInputStream(decompressedBytes), header)
    {
        Buffer = decompressedBytes;
    }

    public static Script FromResource(byte[] resourceData)
    {
        var decompressed = StreamExtensions.Decompress(resourceData, out var header);
        var script = new Script(new MemoryInputStream(decompressed), header)
        {
            Buffer = decompressed
        };
        return script;
    }

    public static Script FromFile(string filePath) => FromResource(File.ReadAllBytes(filePath));

    public byte[]? Export(MemoryOutputStream? stream = null)
    {
        stream ??= new MemoryOutputStream(0x100000);

        if (Header.Revision <= 0x33a)
            stream.Resource(UpToDateScript, Header);
        stream.WriteClassName(Name, Header);
        stream.Resource(SuperClassScript, Header);
        stream.WriteModifiers(Modifiers, Header);
        stream.WriteTypeReferences(TypeReferences, Header);
        stream.WriteFieldReferences(FieldReferences, Header);
        stream.WriteFunctionReferences(FunctionReferences, Header);
        stream.WriteFieldDefinitions(FieldDefinitions, Header);
        stream.WritePropertyDefinitions(PropertyDefinitions, Header);
        stream.WriteFunctionDefinitions(Functions, Header);
        stream.WriteSharedArguments(Arguments, Header);
        stream.WriteBytecode(Bytecode, Header);
        stream.WriteLineNumbers(LineNumbers, Header);
        stream.WriteLocalVariables(LocalVariables, Header);
        stream.WriteStringA(AStringTable, Header);
        stream.WriteStringW(WStringTable, Header);
        if (Header.Revision > 0x272)
        {
            ConstantTableS64 ??= [];
            stream.WriteIntArray(ConstantTableS64, Header, true);
        }
        stream.WriteFloatArray(ConstantTableF32, Header);
        if (Header.Revision >= 0x1ec)
            stream.WriteIntArray(DependingGuids, Header, true);

        var exported = stream.Shrink().GetBuffer();
        Buffer = exported;
        return exported;
    }

    public byte[] CompressResource(HashSet<ResourceDescriptor>? resources = null)
    {
        var raw = Buffer ?? Export();
        if (raw == null) return [];
        if (resources != null) return StreamExtensions.Compress(raw, Header, resources);
        resources = [];
        TypeReferences.ForEach(reference =>
        {
            if (reference.Script != null)
                resources.Add(reference.Script);
        });
        return StreamExtensions.Compress(raw, Header, resources);
    }
}