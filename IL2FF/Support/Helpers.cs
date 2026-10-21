using FishLib;
using Mono.Cecil;
using TypeReference = Mono.Cecil.TypeReference;

namespace IL2FF.Support;

public static class Helpers
{
    public static bool IsVectorType(TypeReference? typeRef) => typeRef?.Name is "Vec2" or "Vec3" or "Vec4";

    public static bool IsMatrixType(TypeReference? typeRef) => typeRef?.FullName is "Fisher.Matrix4x4" || typeRef?.Name == "Matrix4x4";

    public static int GetMachineTypeSize(MachineType machineType)
    {
        return machineType switch
        {
            MachineType.VOID => 0,
            MachineType.S64 or MachineType.F64 => 8,
            MachineType.V4 => 16,
            MachineType.M44 => 64,
            _ => 4
        };
    }

    public static ushort AlignOffset(ushort offset, MachineType machineType)
    {
        ushort align = machineType switch
        {
            MachineType.V4 => 16,
            MachineType.M44 => 64,
            MachineType.S64 or MachineType.F64 => 8,
            MachineType.CHAR => 2,
            MachineType.BOOL => 1,
            MachineType.VOID => 1,
            _ => 4
        };
        return (ushort)((offset + (align - 1)) & ~(align - 1));
    }

    public static ushort AlignArgOffset(ushort offset, MachineType machineType)
    {
        return AlignOffset(offset, machineType);
    }

    public static bool IsNative(this MethodDefinition method)
    {
        return method.HasCustomAttributes && method.CustomAttributes.Any(attr => attr.AttributeType.FullName == "Fisher.Native");
    }
}