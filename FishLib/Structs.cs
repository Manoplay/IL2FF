using CwLibNet.Types.Data;
using static CwLibNet.Enums.CompressionFlags;

namespace FishLib;

public record FieldReference(int TypeReferenceIndex, int NameStringIndex)
{
    public FieldReference() : this(0, 0)
    {
    }
}

public struct PropertyDefinition()
{
    public Modifiers Modifiers = 0;
    public int TypeReferenceIndex = 0;
    public int NameIndex = 0;
    public int GetFunctionIndex = 0;
    public int? SetFunctionIndex = null;
}

public record TypeReference
{
    public MachineType MachineType { get; init; } = MachineType.OBJECT;
    public FishType FishType { get; init; } = 0;
    public int DimensionCount { get; init; } = 0;
    public MachineType ArrayBaseMachineType { get; init; } = 0;
    public ResourceDescriptor? Script = null;
    public int? TypeNameIndex { get; set; }
}

public record FunctionReference
{
    public int TypeReferenceIndex { get; init; }
    public int NameIndex { get; init; }
}

public record FieldDefinition
{
    public Modifiers Modifiers { get; init; } = 0;
    public int TypeReferenceIndex { get; init; }
    public int NameIndex { get; init; }
}

public record Argument
{
    public int TypeReferenceIndex { get; init; }
    public int Offset { get; init; }
}

public struct LocalVariable
{
    public Modifiers Modifiers;
    public int TypeReferenceIndex;
    public int NameIndex;
    public int Offset;
}

public record FunctionDefinition
{
    public Modifiers Modifiers;
    public int TypeReferenceIndex;
    public int NameStringIndex;
    public int ArgumentsBegin;
    public int ArgumentsSize;
    public int BytecodeBegin;
    public int BytecodeSize;
    public int LineNumbersBegin;
    public int LineNumbersSize;
    public int LocalVariablesBegin;
    public int LocalVariablesSize;
    public int StackSize;
}

public class Header
{
    public int Revision = 0;
    public short BranchId = 0;
    public short BranchRevision = 0;
    public byte CompressionFlags = USE_ALL_COMPRESSION;
}