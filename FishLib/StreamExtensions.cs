using System.Globalization;
using System.Numerics;
using System.Text;
using CwLibNet.Enums;
using CwLibNet.IO.Serializer;
using CwLibNet.IO.Streams;
using CwLibNet.Types;
using CwLibNet.Types.Data;

namespace FishLib;

public static class StreamExtensions
{

    extension(MemoryOutputStream stream)
    {
        private void DynamicU32(uint value, Header header)
        {
            if ((header.CompressionFlags & CompressionFlags.USE_COMPRESSED_INTEGERS) != 0)
            {
                stream.Uleb128(value);
            }
            else
            {
                stream.U32(value);
            }
        }

        private void DynamicS32(int value, Header header)
        {
            if ((header.CompressionFlags & CompressionFlags.USE_COMPRESSED_INTEGERS) != 0)
            {
                stream.Uleb128((uint)value);
            }
            else
            {
                stream.S32(value);
            }
        }

        public void Resource(ResourceDescriptor? resource, Header header)
        {
            if (resource != null && resource.IsValid())
            {
                if (header.Revision >= 0x22E)
                {
                    stream.DynamicU32((uint)resource.GetFlags(), header);
                }

                byte type = (byte)(resource.IsGUID() ? header.Revision >= 0x189 ? 2 : 1 : header.Revision >= 0x189 ? 1 : 2);
                stream.U8(type);
                if (type == 0)
                {
                    return;
                }
                if (resource.IsGUID())
                {
                    stream.DynamicS32((int)resource.GetGUID()!.Value.Value, header);
                }
                else if (resource.IsHash())
                {
                    stream.Sha1(resource.GetSHA1());
                }
                return;
            }

            if (header.Revision >= 0x22E)
            {
                stream.DynamicU32(0, header);
            }

            stream.U8(0);
        }

        public void WriteModifiers(Modifiers modifiers, Header header)
        {
            if (header.Revision >= 0x3d9)
                stream.U16((ushort)modifiers);
            else
                stream.DynamicU32((uint)modifiers, header);
        }

        public void WriteClassName(string str, Header header)
        {
            if ((header.CompressionFlags & CompressionFlags.USE_COMPRESSED_INTEGERS) != 0)
                stream.DynamicS32(str.Length * 2, header);
            else
                stream.DynamicS32(str.Length, header);
            stream.Bytes(Encoding.ASCII.GetBytes(str));
        }

        public void WriteBytecode(List<string> instructions, Header header)
        {
            stream.DynamicS32(instructions.Count, header);
            if ((header.CompressionFlags & CompressionFlags.USE_COMPRESSED_INTEGERS) != 0)
            {
                foreach (var bytecode in instructions)
                {
                    var val = BigInteger.Parse(bytecode, NumberStyles.AllowHexSpecifier, null);
                    do
                    {
                        var currentByte = (byte)(val & 0x7F);
                        val >>>= 7;
                        if (val != 0) currentByte |= 0x80;
                        stream.U8(currentByte);
                    } while (val != 0);
                }
            }
            else
            {
                foreach (var bytecode in instructions)
                {
                    stream.Bytes(Convert.FromHexString(bytecode));
                }
            }
        }

        public void WriteIntArray(List<int> intArr, Header header, bool overrideFlags = false)
        {
            if ((header.CompressionFlags & CompressionFlags.USE_COMPRESSED_VECTORS) != 0 && !overrideFlags)
            {
                stream.DynamicS32(intArr.Count, header);
                if (intArr.Count == 0) return;
                var digits = 0;
                var max = intArr.Count > 0 ? intArr.Max() : 0;
                if (max > 0) digits++;
                if (max >> 8 != 0) digits++;
                if (max >> 16 != 0) digits++;
                if (max >> 24 != 0) digits++;
                stream.DynamicS32(digits, header);
                var byteArr = new byte[intArr.Count * digits];
                for (var i = 0; i < intArr.Count; ++i)
                {
                    for (var n = 0; n < digits; ++n)
                    {
                        byteArr[i + n * intArr.Count] = (byte)(intArr[i] >> (n * 8));
                    }
                }
                stream.Bytes(byteArr);
                return;
            }
            stream.DynamicS32(intArr.Count, header);
            foreach (var t in intArr)
            {
                stream.DynamicS32(t, header);
            }
        }

        public void WriteFloatArray(List<float> floatArr, Header header)
        {
            stream.DynamicS32(floatArr.Count, header);
            foreach (var t in floatArr)
            {
                stream.F32(t);
            }
        }

        private void WriteUShortArray(List<ushort> shortArr, Header header)
        {
            stream.DynamicS32(shortArr.Count, header);
            foreach (var t in shortArr)
            {
                stream.U16(t);
            }
        }

        public void WriteStringA(List<string> stringAs, Header header)
        {
            List<int> strLens = [];
            var offset = 0;
            foreach (var str in stringAs)
            {
                strLens.Add(offset);
                offset += Encoding.ASCII.GetByteCount(str) + 1;
            }
            stream.WriteIntArray(strLens, header);
            stream.DynamicS32(offset, header);
            foreach (var str in stringAs)
            {
                stream.Bytes(Encoding.ASCII.GetBytes(str));
                stream.U8(0);
            }
        }

        public void WriteStringW(List<string> stringWs, Header header)
        {
            List<int> strLens = [];
            var offset = 0;
            foreach (var str in stringWs)
            {
                strLens.Add(offset);
                offset += str.Length + 1;
            }
            stream.WriteIntArray(strLens, header);
            stream.DynamicS32(offset, header);
            foreach (var str in stringWs)
            {
                stream.Bytes(Encoding.BigEndianUnicode.GetBytes(str));
                stream.U16(0);
            }
        }

        public void WriteTypeReferences(List<TypeReference> references, Header header)
        {
            stream.DynamicU32((uint)references.Count, header);
            foreach (var reference in references)
            {
                stream.U8((byte)reference.MachineType);
                stream.U8((byte)reference.FishType);
                stream.U8((byte)reference.DimensionCount);
                stream.U8((byte)reference.ArrayBaseMachineType);
                stream.Resource(reference.Script, header);
                if (reference.TypeNameIndex != null)
                    stream.DynamicU32((uint)reference.TypeNameIndex.Value, header);
                else stream.DynamicU32(unchecked((uint)-1), header);
            }
        }

        public void WriteFieldReferences(List<FieldReference> references, Header header)
        {
            stream.DynamicU32((uint)references.Count, header);
            foreach (var reference in references)
            {
                stream.DynamicU32((uint)reference.TypeReferenceIndex, header);
                stream.DynamicU32((uint)reference.NameStringIndex, header);
            }
        }

        public void WriteFunctionReferences(List<FunctionReference> references, Header header)
        {
            stream.DynamicU32((uint)references.Count, header);
            foreach (var reference in references)
            {
                stream.DynamicU32((uint)reference.TypeReferenceIndex, header);
                stream.DynamicU32((uint)reference.NameIndex, header);
            }
        }

        public void WriteFieldDefinitions(List<FieldDefinition> definitions, Header header)
        {
            stream.DynamicU32((uint)definitions.Count, header);
            foreach (var definition in definitions)
            {
                stream.WriteModifiers(definition.Modifiers, header);
                stream.DynamicU32((uint)definition.TypeReferenceIndex, header);
                stream.DynamicU32((uint)definition.NameIndex, header);
            }
        }

        public void WritePropertyDefinitions(List<PropertyDefinition> definitions, Header header)
        {
            stream.DynamicU32((uint)definitions.Count, header);
            foreach (var definition in definitions)
            {
                stream.WriteModifiers(definition.Modifiers, header);
                stream.DynamicU32((uint)definition.TypeReferenceIndex, header);
                stream.DynamicU32((uint)definition.NameIndex, header);
                stream.DynamicU32((uint)definition.GetFunctionIndex, header);
                if (definition.SetFunctionIndex != null)
                    stream.DynamicU32((uint)definition.SetFunctionIndex.Value, header);
                else stream.DynamicU32(unchecked((uint)-1), header);
            }
        }

        public void WriteFunctionDefinitions(List<FunctionDefinition> definitions, Header header)
        {
            stream.DynamicU32((uint)definitions.Count, header);
            foreach (var definition in definitions)
            {
                stream.WriteModifiers(definition.Modifiers, header);
                stream.DynamicU32((uint)definition.TypeReferenceIndex, header);
                stream.DynamicU32((uint)definition.NameStringIndex, header);
                if (header.Revision >= 0x3d9)
                {
                    stream.U16((ushort)definition.ArgumentsBegin);
                    stream.U16((ushort)definition.ArgumentsSize);
                    stream.U16((ushort)definition.BytecodeBegin);
                    stream.U16((ushort)definition.BytecodeSize);
                    stream.U16((ushort)definition.LineNumbersBegin);
                    stream.U16((ushort)definition.LineNumbersSize);
                    stream.U16((ushort)definition.LocalVariablesBegin);
                    stream.U16((ushort)definition.LocalVariablesSize);
                }
                else
                {
                    stream.DynamicU32((uint)definition.ArgumentsBegin, header);
                    stream.DynamicU32((uint)(definition.ArgumentsBegin + definition.ArgumentsSize), header);
                    stream.DynamicU32((uint)definition.BytecodeBegin, header);
                    stream.DynamicU32((uint)(definition.BytecodeBegin + definition.BytecodeSize), header);
                    stream.DynamicU32((uint)definition.LineNumbersBegin, header);
                    stream.DynamicU32((uint)(definition.LineNumbersBegin + definition.LineNumbersSize), header);
                    stream.DynamicU32((uint)definition.LocalVariablesBegin, header);
                    stream.DynamicU32((uint)(definition.LocalVariablesBegin + definition.LocalVariablesSize), header);
                }
                stream.DynamicU32((uint)definition.StackSize, header);
            }
        }

        public void WriteSharedArguments(List<Argument> arguments, Header header)
        {
            stream.DynamicU32((uint)arguments.Count, header);
            foreach (var argument in arguments)
            {
                stream.DynamicU32((uint)argument.TypeReferenceIndex, header);
                stream.DynamicU32((uint)argument.Offset, header);
            }
        }

        public void WriteLineNumbers(List<ushort> lineNumbers, Header header)
        {
            stream.WriteUShortArray(lineNumbers, header);
        }

        public void WriteLocalVariables(LocalVariable[] localVariables, Header header)
        {
            stream.DynamicU32((uint)localVariables.Length, header);
            foreach (var variable in localVariables)
            {
                stream.WriteModifiers(variable.Modifiers, header);
                stream.DynamicU32((uint)variable.TypeReferenceIndex, header);
                stream.DynamicU32((uint)variable.NameIndex, header);
                stream.DynamicU32((uint)variable.Offset, header);
            }
        }
    }

    extension(MemoryInputStream stream)
    {
        private uint DynamicU32(Header header)
        {
            if ((header.CompressionFlags & CompressionFlags.USE_COMPRESSED_INTEGERS) != 0)
            {
                return (uint)stream.Uleb128();
            }
            return (uint)stream.U32();
        }

        private int DynamicS32(Header header)
        {
            if ((header.CompressionFlags & CompressionFlags.USE_COMPRESSED_INTEGERS) != 0)
            {
                return (int)stream.Uleb128();
            }
            return stream.S32();
        }



        public Modifiers ReadModifiers(Header header)
        {
            if (header.Revision >= 0x3d9)
                return (Modifiers)stream.U16();
            return (Modifiers)stream.DynamicU32(header);
        }

        public ResourceDescriptor? ReadResource(Header header)
        {
            int flags = 0;
            if (header.Revision >= 0x22E)
            {
                flags = (int)stream.DynamicU32(header);
            }

            var type = stream.U8();
            if (type == 0) return null;

            ResourceDescriptor? resource = null;
            if (header.Revision >= 0x189)
            {
                switch (type)
                {
                    case 2:
                    {
                        var guid = (uint)stream.DynamicS32(header);
                        resource = new ResourceDescriptor(guid, ResourceType.Script);
                        break;
                    }
                    case 1:
                    {
                        var sha = stream.Sha1();
                        resource = new ResourceDescriptor(sha, ResourceType.Script);
                        break;
                    }
                }
            }
            else
            {
                switch (type)
                {
                    case 1:
                    {
                        var guid = (uint)stream.DynamicS32(header);
                        resource = new ResourceDescriptor(guid, ResourceType.Script);
                        break;
                    }
                    case 2:
                    {
                        var sha = stream.Sha1();
                        resource = new ResourceDescriptor(sha, ResourceType.Script);
                        break;
                    }
                }
            }

            if (resource != null && header.Revision >= 0x22E)
            {
                resource.SetFlags(flags);
            }

            return resource;
        }

        public string ReadClassName(Header header)
        {
            var nameSize = (header.CompressionFlags & CompressionFlags.USE_COMPRESSED_INTEGERS) != 0
                ? stream.DynamicS32(header) / 2
                : stream.DynamicS32(header);
            return stream.Str(nameSize);
        }

        private string ReadBytecode(Header header)
        {
            if ((header.CompressionFlags & CompressionFlags.USE_COMPRESSED_INTEGERS) != 0)
            {
                BigInteger result = new();
                var shift = 0;
                while (true)
                {
                    var b = (byte)stream.U8();
                    result |= (BigInteger)(b & 0x7F) << shift;
                    shift += 7;
                    if ((b & 0x80) == 0) break;
                }
                var str = result.ToString("X").PadLeft(16, '0');
                return str.Length == 17 ? str[1..] : str;
            }
            var code = stream.Bytes(8);
            return Convert.ToHexString(code ?? []);
        }

        public List<string> ReadInstructions(Header header)
        {
            var count = stream.DynamicS32(header);
            List<string> instructions = new(count);
            for (var i = 0; i < count; ++i)
            {
                instructions.Add(stream.ReadBytecode(header));
            }
            return instructions;
        }

        public List<int> ReadIntArray(Header header, bool overrideFlags = false)
        {
            List<int> ints = [];
            if ((header.CompressionFlags & CompressionFlags.USE_COMPRESSED_VECTORS) != 0 && !overrideFlags)
            {
                var count = stream.DynamicS32(header);
                if (count == 0) return ints;
                var digits = stream.DynamicS32(header);
                var tableBytes = stream.Bytes(count * digits);
                for (var i = 0; i < count; ++i)
                {
                    var value = 0;
                    var shift = 0;
                    for (var n = 0; n < digits; ++n)
                    {
                        value |= tableBytes[i + n * count] << shift;
                        shift += 8;
                    }
                    ints.Add(value);
                }
                return ints;
            }
            var c = stream.DynamicS32(header);
            for (var i = 0; i < c; ++i)
            {
                ints.Add(stream.DynamicS32(header));
            }
            return ints;
        }

        public List<int> ReadInts(Header header)
        {
            var count = stream.DynamicS32(header);
            List<int> ints = new(count);
            for (var i = 0; i < count; ++i)
            {
                ints.Add(stream.DynamicS32(header));
            }
            return ints;
        }

        public List<float> ReadFloatArray(Header header)
        {
            var count = stream.DynamicS32(header);
            List<float> floats = new(count);
            for (var i = 0; i < count; ++i)
            {
                floats.Add(stream.F32());
            }
            return floats;
        }

        private List<ushort> ReadUShortArray(Header header)
        {
            var count = stream.DynamicS32(header);
            List<ushort> shorts = new(count);
            for (var i = 0; i < count; ++i)
            {
                shorts.Add((ushort)stream.U16());
            }
            return shorts;
        }

        public List<string> ReadStrings(Header header, bool wide)
        {
            List<string> strings = [];
            var strLens = stream.ReadIntArray(header);
            var stringTableSize = stream.DynamicS32(header);
            if (wide) stringTableSize *= 2;
            var stringTable = stream.Bytes(stringTableSize);
            if (wide)
            {
                strings.AddRange(strLens.Select((t, i) => Encoding.BigEndianUnicode.GetString((i == strLens.Count - 1
                    ? stringTable?[(t * 2)..(stringTableSize - 2)]
                    : stringTable?[(t * 2)..(strLens[i + 1] * 2 - 2)]) ?? [])));
            }
            else
            {
                strings.AddRange(strLens.Select((t, i) => i == strLens.Count - 1
                    ? Encoding.ASCII.GetString(stringTable?[t..(stringTableSize - 1)] ?? [])
                    : Encoding.UTF8.GetString(stringTable?[t..(strLens[i + 1] - 1)] ?? [])));
            }
            return strings;
        }

        public List<TypeReference> ReadTypeReferences(Header header)
        {
            var count = stream.DynamicS32(header);
            List<TypeReference> references = new(count);
            for (var i = 0; i < count; ++i)
            {
                TypeReference reference = new()
                {
                    MachineType = (MachineType)stream.U8(),
                    FishType = (FishType)stream.U8(),
                    DimensionCount = stream.U8(),
                    ArrayBaseMachineType = (MachineType)stream.U8(),
                    Script = stream.ReadResource(header)
                };
                var userTypeCheck = stream.DynamicS32(header);
                if (userTypeCheck != -1)
                    reference.TypeNameIndex = userTypeCheck;
                references.Add(reference);
            }
            return references;
        }

        public List<FieldReference> ReadFieldReferences(Header header)
        {
            var count = stream.DynamicS32(header);
            List<FieldReference> references = new(count);
            for (var i = 0; i < count; ++i)
            {
                references.Add(new FieldReference
                {
                    TypeReferenceIndex = stream.DynamicS32(header),
                    NameStringIndex = stream.DynamicS32(header)
                });
            }
            return references;
        }

        public List<FunctionReference> ReadFunctionReferences(Header header)
        {
            var count = stream.DynamicS32(header);
            List<FunctionReference> references = new(count);
            for (var i = 0; i < count; ++i)
            {
                references.Add(new FunctionReference
                {
                    TypeReferenceIndex = stream.DynamicS32(header),
                    NameIndex = stream.DynamicS32(header)
                });
            }
            return references;
        }

        public List<FieldDefinition> ReadFieldDefinitions(Header header)
        {
            var count = stream.DynamicS32(header);
            List<FieldDefinition> definitions = new(count);
            for (var i = 0; i < count; ++i)
            {
                definitions.Add(new FieldDefinition
                {
                    Modifiers = stream.ReadModifiers(header),
                    TypeReferenceIndex = stream.DynamicS32(header),
                    NameIndex = stream.DynamicS32(header)
                });
            }
            return definitions;
        }

        public List<PropertyDefinition> ReadPropertyDefinitions(Header header)
        {
            var count = stream.DynamicS32(header);
            List<PropertyDefinition> definitions = new(count);
            for (var i = 0; i < count; ++i)
            {
                PropertyDefinition definition = new()
                {
                    Modifiers = stream.ReadModifiers(header),
                    TypeReferenceIndex = stream.DynamicS32(header),
                    NameIndex = stream.DynamicS32(header),
                    GetFunctionIndex = stream.DynamicS32(header)
                };
                var userTypeCheck = stream.DynamicS32(header);
                if (userTypeCheck != -1)
                    definition.SetFunctionIndex = userTypeCheck;
                definitions.Add(definition);
            }
            return definitions;
        }

        public List<FunctionDefinition> ReadFunctionDefinitions(Header header)
        {
            var count = stream.DynamicS32(header);
            List<FunctionDefinition> definitions = new(count);
            for (var i = 0; i < count; ++i)
            {
                FunctionDefinition definition = new()
                {
                    Modifiers = stream.ReadModifiers(header),
                    TypeReferenceIndex = stream.DynamicS32(header),
                    NameStringIndex = stream.DynamicS32(header),
                    ArgumentsBegin = header.Revision >= 0x3d9 ? stream.U16() : stream.DynamicS32(header)
                };
                definition.ArgumentsSize = (header.Revision >= 0x3d9 ? stream.U16() : stream.DynamicS32(header)) - (header.Revision >= 0x3d9 ? 0 : definition.ArgumentsBegin);
                definition.BytecodeBegin = header.Revision >= 0x3d9 ? stream.U16() : stream.DynamicS32(header);
                definition.BytecodeSize = (header.Revision >= 0x3d9 ? stream.U16() : stream.DynamicS32(header)) - (header.Revision >= 0x3d9 ? 0 : definition.BytecodeBegin);
                definition.LineNumbersBegin = header.Revision >= 0x3d9 ? stream.U16() : stream.DynamicS32(header);
                definition.LineNumbersSize = (header.Revision >= 0x3d9 ? stream.U16() : stream.DynamicS32(header)) - (header.Revision >= 0x3d9 ? 0 : definition.LineNumbersBegin);
                definition.LocalVariablesBegin = header.Revision >= 0x3d9 ? stream.U16() : stream.DynamicS32(header);
                definition.LocalVariablesSize = (header.Revision >= 0x3d9 ? stream.U16() : stream.DynamicS32(header)) - (header.Revision >= 0x3d9 ? 0 : definition.LocalVariablesBegin);
                definition.StackSize = stream.DynamicS32(header);
                definitions.Add(definition);
            }
            return definitions;
        }

        public List<Argument> ReadSharedArguments(Header header)
        {
            var count = stream.DynamicS32(header);
            List<Argument> sharedArguments = new(count);
            for (var i = 0; i < count; ++i)
            {
                sharedArguments.Add(new Argument
                {
                    TypeReferenceIndex = stream.DynamicS32(header),
                    Offset = stream.DynamicS32(header)
                });
            }
            return sharedArguments;
        }

        public List<ushort> ReadLineNumbers(Header header)
        {
            return stream.ReadUShortArray(header);
        }

        public LocalVariable[] ReadLocalVariables(Header header)
        {
            var count = stream.DynamicS32(header);
            var localvariables = new LocalVariable[count];
            for (var i = 0; i < count; ++i)
            {
                localvariables[i].Modifiers = stream.ReadModifiers(header);
                localvariables[i].TypeReferenceIndex = stream.DynamicS32(header);
                localvariables[i].NameIndex = stream.DynamicS32(header);
                localvariables[i].Offset = stream.DynamicS32(header);
            }
            return localvariables;
        }
    }

    public static byte[] Compress(byte[] decompressedData, Header header, HashSet<ResourceDescriptor> deps)
    {
        var data = new SerializationData(
            decompressedData,
            new Revision(header.Revision, header.BranchId, header.BranchRevision),
            header.CompressionFlags,
            ResourceType.Script,
            SerializationType.BINARY,
            [.. deps]
        );
        return SerializedResource.Compress(data, true) ?? [];
    }

    public static byte[] Decompress(byte[] resourceData, out Header header)
    {
        var resource = new SerializedResource(resourceData);
        var rev = resource.GetRevision();
        header = new Header
        {
            Revision = rev.Head,
            BranchId = rev.BranchId,
            BranchRevision = rev.BranchRevision,
            CompressionFlags = resource.GetCompressionFlags()
        };
        return resource.GetStream().GetBuffer() ?? [];
    }
}