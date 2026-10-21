using System.Globalization;

namespace FishLib;

public static class InstructionMapper
{
    private static InstructionType GetInstructionType(byte opCode) =>
        (InstructionType)opCode;

    public static InstructionType GetInstructionType(string bytecode)
    {
        if (bytecode.Length >= 16 && byte.TryParse(bytecode.AsSpan(14, 2), NumberStyles.HexNumber, null, out var op))
        {
            return GetInstructionType(op);
        }
        return InstructionType.NOP;
    }

    public static byte Mov(MachineType type) => type switch
    {
        MachineType.BOOL => (byte)InstructionType.MOVb,
        MachineType.CHAR => (byte)InstructionType.MOVc,
        MachineType.S32 => (byte)InstructionType.MOVi,
        MachineType.S64 => (byte)InstructionType.MOVs64,
        MachineType.F32 => (byte)InstructionType.MOVf,
        MachineType.V4 => (byte)InstructionType.MOVv4,
        MachineType.M44 => (byte)InstructionType.MOVm44,
        MachineType.RAWPTR => (byte)InstructionType.MOVrp,
        MachineType.REFPTR => (byte)InstructionType.MOVcp,
        MachineType.SAFEPTR => (byte)InstructionType.MOVsp,
        MachineType.OBJECT => (byte)InstructionType.MOVo,
        _ => (byte)InstructionType.MOVi
    };
}

public class Register(ushort id, MachineType type)
{
    public readonly ushort Id = id;
    private string Name => $"r{Id}";
    public MachineType Type = type;

    public override string ToString()
    {
        return Name;
    }
}

public interface IArgument
{
    public byte[] ToBytes();
}

public class NoOpArgument : IArgument
{
    public byte[] ToBytes()
    {
        return [.. "\0\0\0\0\0\0"u8];
    }
}

public class LoadConstArgument : IArgument
{
    public required Register Destination;
    public int Index;

    public byte[] ToBytes()
    {
        return [.. BitConverter.GetBytes(Index).Reverse(), .. BitConverter.GetBytes(Destination.Id).Reverse()];
    }
}

public class UnaryArgument : IArgument
{
    public required Register Destination;
    public required Register Source;

    public byte[] ToBytes()
    {
        return [0, 0, .. BitConverter.GetBytes(Source.Id).Reverse(), .. BitConverter.GetBytes(Destination.Id).Reverse()];
    }
}

public class BinaryArgument : IArgument
{
    public required Register Destination;
    public required Register Source1;
    public required Register Source2;

    public byte[] ToBytes()
    {
        return [.. BitConverter.GetBytes(Source2.Id).Reverse(), .. BitConverter.GetBytes(Source1.Id).Reverse(), .. BitConverter.GetBytes(Destination.Id).Reverse()];
    }
}

public class GetBuiltinMemberArgument : IArgument
{
    public required Register Destination;
    public required Register Base;

    public byte[] ToBytes()
    {
        return [0, 0, .. BitConverter.GetBytes(Base.Id).Reverse(), .. BitConverter.GetBytes(Destination.Id).Reverse()];
    }
}

public class SetBuiltinMemberArgument : IArgument
{
    public required Register Source;
    public required Register Base;

    public byte[] ToBytes()
    {
        return [0, 0, .. BitConverter.GetBytes(Base.Id).Reverse(), .. BitConverter.GetBytes(Source.Id).Reverse()];
    }
}

public class GetMemberArgument : IArgument
{
    public ushort FieldIdx;
    public required Register Base;
    public required Register Destination;

    public byte[] ToBytes()
    {
        return [.. BitConverter.GetBytes(FieldIdx).Reverse(), .. BitConverter.GetBytes(Base.Id).Reverse(), .. BitConverter.GetBytes(Destination.Id).Reverse()];
    }
}

public class SetMemberArgument : IArgument
{
    public ushort FieldIdx;
    public required Register Base;
    public required Register Source;

    public byte[] ToBytes()
    {
        return [.. BitConverter.GetBytes(FieldIdx).Reverse(), .. BitConverter.GetBytes(Base.Id).Reverse(), .. BitConverter.GetBytes(Source.Id).Reverse()];
    }
}

public class BranchArgument : IArgument
{
    public int Offset;
    public required Register Source;
    public byte[] ToBytes()
    {
        return [.. BitConverter.GetBytes(Offset).Reverse(), .. BitConverter.GetBytes(Source.Id).Reverse()];
    }
}

public class ArgArgument : IArgument
{
    public required Register Source;
    public ushort ArgIdx;

    public byte[] ToBytes()
    {
        return [0, 0, .. BitConverter.GetBytes(ArgIdx).Reverse(), .. BitConverter.GetBytes(Source.Id).Reverse()];
    }
}

public class CallArgument : IArgument
{
    public required Register Destination;
    public ushort CallIdx;

    public byte[] ToBytes()
    {
        return [0, 0, .. BitConverter.GetBytes(CallIdx).Reverse(), .. BitConverter.GetBytes(Destination.Id).Reverse()];
    }
}

public class LoadNullConstArgument : IArgument
{
    public required Register Destination;

    public byte[] ToBytes()
    {
        return [0, 0, 0, 0, .. BitConverter.GetBytes(Destination.Id).Reverse()];
    }
}

public class ReturnArgument : IArgument
{
    public required Register Source;

    public byte[] ToBytes()
    {
        return [0, 0, .. BitConverter.GetBytes(Source.Id).Reverse(), 0, 0];
    }
}

public class Instruction
{
    public byte Opcode;
    public MachineType Type;
    public required IArgument Parameters;

    private byte[] ToBytes()
    {
        return [.. Parameters.ToBytes(), (byte)Type, Opcode];
    }

    public string ToBytecode()
    {
        return Convert.ToHexString(ToBytes());
    }
}
