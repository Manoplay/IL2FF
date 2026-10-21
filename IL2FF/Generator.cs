using System.Text;
using CwLibNet.Types.Data;
using FishLib;
using IL2FF.Support;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CompressionFlags = CwLibNet.Enums.CompressionFlags;
using CwResourceType = CwLibNet.Enums.ResourceType;
using FieldDefinition = Mono.Cecil.FieldDefinition;
using FieldReference = Mono.Cecil.FieldReference;
using TypeReference = Mono.Cecil.TypeReference;

namespace IL2FF;

public static class Generator
{
    private static TypeDefinition? _currentCompiledType;
    private static Script _script = new();

    private static bool IsEntrypoint(TypeDefinition type)
    {
        if (type.IsInterface) return false;
        return type.HasCustomAttributes && type.CustomAttributes.Any(attr => attr.AttributeType.FullName is "Fisher.Entrypoint");
    }

    private static string GetOutputFileName(string className)
    {
        var sb = new StringBuilder();
        foreach (var c in className)
        {
            if (c == ' ')
            {
                sb.Append('_');
            }
            else if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        var result = sb.ToString();
        if (string.IsNullOrEmpty(result))
            result = "script";
        return result + ".ff";
    }

    private static string CompileType(TypeDefinition type, string outputDirectory = "")
    {
        var fileName = GetOutputFileName(type.Name);
        WriteScript(type);

        var resources = new HashSet<ResourceDescriptor>();
        _script.TypeReferences.ForEach(reference =>
        {
            if (reference.Script != null)
                resources.Add(reference.Script);
        });

        _script.Export();

        var outputPath = string.IsNullOrEmpty(outputDirectory)
            ? fileName
            : Path.Combine(outputDirectory, fileName);

        var parentDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }
        var compressedBytes = _script.CompressResource(resources);
        if (compressedBytes == null)
        {
            throw new InvalidOperationException($"Resource compression failed for {type.FullName}.");
        }
        File.WriteAllBytes(outputPath, compressedBytes);
        return outputPath;
    }

    public static List<string> CompileAll(ModuleDefinition module, string outputDirectory = "")
    {
        List<string> compiledFiles =
            [.. from type in module.GetTypes() where IsEntrypoint(type) select CompileType(type, outputDirectory)];
        return compiledFiles;
    }

    private static void WriteScript(TypeDefinition type)
    {
        _currentCompiledType = type;
        _script = new();
        var revision = 626;
        var branchID = 19524;
        var branchRevision = 23;

        var entrypointAttr = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName is "Fisher.Entrypoint");
        if (entrypointAttr == null) throw new InvalidOperationException("Trying to compile a non-Entrypoint class.");
        if (entrypointAttr.HasConstructorArguments) // try with constructor arguments
        {
            if (entrypointAttr.ConstructorArguments.Count > 0 && entrypointAttr.ConstructorArguments[0].Value != null)
                revision = Convert.ToInt32(entrypointAttr.ConstructorArguments[0].Value);
            if (entrypointAttr.ConstructorArguments.Count > 1 && entrypointAttr.ConstructorArguments[1].Value != null)
                branchID = Convert.ToInt32(entrypointAttr.ConstructorArguments[1].Value);
            if (entrypointAttr.ConstructorArguments.Count > 2 && entrypointAttr.ConstructorArguments[2].Value != null)
                branchRevision = Convert.ToInt32(entrypointAttr.ConstructorArguments[2].Value);
        }
        if (entrypointAttr.HasProperties) // try with properties
        {
            foreach (var prop in entrypointAttr.Properties)
            {
                switch (prop.Name)
                {
                    case "Revision" when prop.Argument.Value != null:
                        revision = Convert.ToInt32(prop.Argument.Value);
                        break;
                    case "BranchID" when prop.Argument.Value != null:
                        branchID = Convert.ToInt32(prop.Argument.Value);
                        break;
                    case "BranchRevision" when prop.Argument.Value != null:
                        branchRevision = Convert.ToInt32(prop.Argument.Value);
                        break;
                }
            }
        }

        _script.Header = new()
        {
            Revision = revision,
            CompressionFlags = CompressionFlags.USE_ALL_COMPRESSION,
            BranchId = (short)branchID,
            BranchRevision = (short)branchRevision
        };
        
        var typeGuid = TypeReference.GetGuidValue(type);
        var baseGuid = type.BaseType != null ? TypeReference.GetGuidValue(type.BaseType.Resolve()) : "0";

        _script.Self = TypeReference.CreateScriptDescriptor(typeGuid);
        _script.UpToDateScript = TypeReference.CreateScriptDescriptor(typeGuid);
        _script.SuperClassScript = baseGuid != "0" ? TypeReference.CreateScriptDescriptor(baseGuid) : null;
        _script.Name = type.Name;
        var classModifiers = Modifiers.PUBLIC;
        if (type.HasCustomAttributes)
        {
            if (type.CustomAttributes.Any(a => a.AttributeType.Name == "Divergent"))
                classModifiers |= Modifiers.DIVERGENT;
            if (type.CustomAttributes.Any(a => a.AttributeType.Name == "Ephemeral"))
                classModifiers |= Modifiers.EPHEMERAL;
        }
        _script.Modifiers = classModifiers;
        _script.LineNumbers = [];
        _script.LocalVariables = [];
        _script.Arguments = [];
        _script.FunctionReferences = [];
        _script.FieldReferences = [];
        _script.FieldDefinitions = [];
        _script.PropertyDefinitions = [];
        _script.Functions = [];
        _script.TypeReferences = [];
        _script.Bytecode = [];
        _script.ConstantTableF32 = [];
        _script.ConstantTableS64 = [];
        _script.DependingGuids = [];
        _script.Buffer = [];
        _script.AStringTable = [type.Name];
        _script.WStringTable = [""];
        LoadDeclarations(type);
        LoadSystemDeclarations();
        LoadBytecodeAndFunctions(type);
    }

    private static void LoadBytecodeAndFunctions(TypeDefinition type)
    {
        int lastOffset = 0, lastLineOffset = 0;
        List<FunctionDefinition> methodDefinitions = [];
        List<LocalVariable> allLocalVariables = [];

        foreach (var method in type.Methods)
        {
            if (method.IsNative())
            {
                methodDefinitions.Add(CompileNativeMethod(method));
                continue;
            }

            var emitter = new MethodEmitter(method, type, _script, lastOffset, lastLineOffset, allLocalVariables.Count);
            var funcDef = emitter.Emit();
            methodDefinitions.Add(funcDef);
            allLocalVariables.AddRange(emitter.LocalVariables);

            _script.Bytecode.AddRange(emitter.Instructions.Select(e => e.ToBytecode()));
            _script.LineNumbers.AddRange(emitter.LineNumbers);

            lastOffset += emitter.Instructions.Count;
            lastLineOffset += emitter.LineNumbers.Count;
        }

        var defaultCtor = EmitDefaultConstructor(type, allLocalVariables.Count, lastOffset, lastLineOffset);
        methodDefinitions.Add(defaultCtor);

        _script.Functions.AddRange(methodDefinitions);
        _script.LocalVariables = [.. allLocalVariables];
    }

    private static FunctionDefinition CompileNativeMethod(MethodDefinition method)
    {
        var nativeModifiers = Modifiers.NATIVE;
        if (method.IsPublic) nativeModifiers |= Modifiers.PUBLIC;
        if (method.IsPrivate) nativeModifiers |= Modifiers.PRIVATE;
        if (method.IsFamily) nativeModifiers |= Modifiers.PROTECTED;
        if (method.IsStatic) nativeModifiers |= Modifiers.STATIC;

        var nativeArgsBegin = _script.Arguments.Count;
        var nativeArgOffset = (ushort)(method.IsStatic ? 0 : 4);

        foreach (var param in method.Parameters)
        {
            var paramTypeRef = param.ParameterType.ToTypeReference();
            _script.TypeReferences.AddSafe(paramTypeRef);
            var typeRefIdx = _script.TypeReferences.IndexOf(paramTypeRef).MinZero();

            var machineType = param.ParameterType.GetFishType();
            nativeArgOffset = machineType switch
            {
                MachineType.V4 => (ushort)((nativeArgOffset + 0xF) & ~0xF),
                MachineType.M44 => (ushort)((nativeArgOffset + 0x3F) & ~0x3F),
                _ => nativeArgOffset
            };

            _script.Arguments.Add(new()
            {
                TypeReferenceIndex = typeRefIdx,
                Offset = nativeArgOffset
            });

            nativeArgOffset += (ushort)Helpers.GetMachineTypeSize(machineType);
        }

        var nativeArgsSize = method.Parameters.Count;

        var returnTypeRef = method.ReturnType.ToTypeReference();
        _script.TypeReferences.AddSafe(returnTypeRef);
        var returnTypeIdx = _script.TypeReferences.IndexOf(returnTypeRef).MinZero();

        var nativeMangle = method.Mangle();
        if (!_script.AStringTable.Contains(nativeMangle))
            _script.AStringTable.Add(nativeMangle);

        return new()
        {
            BytecodeBegin = 0,
            BytecodeSize = 0,
            LineNumbersBegin = 0,
            LineNumbersSize = 0,
            LocalVariablesBegin = 0,
            LocalVariablesSize = 0,
            Modifiers = nativeModifiers,
            TypeReferenceIndex = returnTypeIdx,
            NameStringIndex = _script.AStringTable.IndexOf(nativeMangle).MinZero(),
            ArgumentsBegin = nativeArgsBegin,
            ArgumentsSize = nativeArgsSize,
            StackSize = 0
        };
    }

    private static FunctionDefinition EmitDefaultConstructor(TypeDefinition type, int localVariablesCount, int lastOffset, int lastLineOffset)
    {
        if (!_script.AStringTable.Contains(".ctor__"))
            _script.AStringTable.Add(".ctor__");

        var baseCtorTypeRef = type.BaseType?.ToTypeReference() ?? type.ToTypeReference();
        _script.TypeReferences.AddSafe(baseCtorTypeRef);
        var baseCtorTypeIdx = _script.TypeReferences.IndexOf(baseCtorTypeRef).MinZero();

        var baseCtorFuncRef = new FunctionReference
        {
            NameIndex = _script.AStringTable.IndexOf(".ctor__").MinZero(),
            TypeReferenceIndex = baseCtorTypeIdx
        };
        _script.FunctionReferences.AddSafe(baseCtorFuncRef);
        var baseCtorCallIdx = _script.FunctionReferences.IndexOf(baseCtorFuncRef).MinZero();

        Register meStub = new(0, MachineType.OBJECT);
        var ctorCodeBegin = lastOffset;
        List<FishLib.Instruction> ctorStubInstructions = [
            new() { Opcode = (byte)InstructionType.NOP, Parameters = new NoOpArgument() },
            new() { Opcode = (byte)InstructionType.ARG, Type = MachineType.SAFEPTR, Parameters = new ArgArgument { Source = meStub, ArgIdx = 0 } },
            new() { Opcode = (byte)InstructionType.CALL, Type = MachineType.VOID, Parameters = new CallArgument { Destination = new(0xFFFF, MachineType.VOID), CallIdx = (ushort)baseCtorCallIdx } },
            new() { Opcode = (byte)InstructionType.RET, Type = MachineType.VOID, Parameters = new ReturnArgument { Source = new(0, MachineType.VOID) } }
        ];

        _script.Bytecode.AddRange(ctorStubInstructions.Select(e => e.ToBytecode()));
        _script.LineNumbers.AddRange(Enumerable.Repeat((ushort)1, 4));

        return new()
        {
            BytecodeBegin = ctorCodeBegin,
            BytecodeSize = 4,
            LineNumbersBegin = lastLineOffset,
            LineNumbersSize = 4,
            LocalVariablesBegin = localVariablesCount,
            LocalVariablesSize = 0,
            Modifiers = Modifiers.PUBLIC,
            TypeReferenceIndex = _script.TypeReferences.IndexOf(Void).MinZero(),
            NameStringIndex = _script.AStringTable.IndexOf(".ctor__").MinZero(),
            ArgumentsBegin = _script.Arguments.Count,
            ArgumentsSize = 0,
            StackSize = 16
        };
    }
    internal static Modifiers GetModifiers(MethodDefinition method)
    {
        Modifiers methodModifiers = 0;
        if (method.IsPublic) methodModifiers |= Modifiers.PUBLIC;
        if (method.IsPrivate) methodModifiers |= Modifiers.PRIVATE;
        if (method.IsFamily) methodModifiers |= Modifiers.PROTECTED;
        if (method.IsStatic) methodModifiers |= Modifiers.STATIC;
        if (method.IsVirtual) methodModifiers |= Modifiers.VIRTUAL;
        if (method.IsReuseSlot) methodModifiers |= Modifiers.OVERRIDE;
        if (method.IsAbstract) methodModifiers |= Modifiers.ABSTRACT;

        if (method.HasCustomAttributes)
        {
            if (method.CustomAttributes.Any(a => a.AttributeType.FullName == "Fisher.Divergent"))
                methodModifiers |= Modifiers.DIVERGENT;
            if (method.CustomAttributes.Any(a => a.AttributeType.FullName == "Fisher.Ephemeral"))
                methodModifiers |= Modifiers.EPHEMERAL;
        }
        if (methodModifiers == 0) methodModifiers = Modifiers.PUBLIC;
        return methodModifiers;
    }

    internal static FishLib.FieldReference ToFieldReference(this FieldReference field)
    {
        var typeRef = field.DeclaringType.ToTypeReference();

        if (!_script.AStringTable.Contains(field.Name))
            _script.AStringTable.Add(field.Name);
        _script.TypeReferences.AddSafe(typeRef);

        return new()
        {
            NameStringIndex = _script.AStringTable.IndexOf(field.Name).MinZero(),
            TypeReferenceIndex = _script.TypeReferences.IndexOf(typeRef).MinZero()
        };
    }

    private static FishLib.FieldDefinition ToFieldDefinition(this FieldDefinition field)
    {
        var typeRef = field.FieldType.ToTypeReference();
        if (typeRef == null)
            throw new InvalidOperationException($"Internal fields must have valid types: field '{field.Name}'");

        if (!_script.AStringTable.Contains(field.Name))
            _script.AStringTable.Add(field.Name);
        _script.TypeReferences.AddSafe(typeRef);

        Modifiers modifiers = 0;
        if (field.IsPublic) modifiers |= Modifiers.PUBLIC;
        if (field.IsPrivate) modifiers |= Modifiers.PRIVATE;
        if (field.IsFamily) modifiers |= Modifiers.PROTECTED;
        if (field.IsStatic) modifiers |= Modifiers.STATIC;

        if (!field.HasCustomAttributes)
            return new()
            {
                NameIndex = _script.AStringTable.IndexOf(field.Name).MinZero(),
                Modifiers = modifiers,
                TypeReferenceIndex = _script.TypeReferences.IndexOf(typeRef).MinZero()
            };
        // special attributes not available in CIL
        if (field.CustomAttributes.Any(a => a.AttributeType.FullName == "Fisher.Divergent"))
            modifiers |= Modifiers.DIVERGENT;
        if (field.CustomAttributes.Any(a => a.AttributeType.FullName == "Fisher.Ephemeral"))
            modifiers |= Modifiers.EPHEMERAL;

        return new()
        {
            NameIndex = _script.AStringTable.IndexOf(field.Name).MinZero(),
            Modifiers = modifiers,
            TypeReferenceIndex = _script.TypeReferences.IndexOf(typeRef).MinZero()
        };
    }

    internal static FunctionReference ToFunctionReference(this MethodReference method)
    {
        var isExtension = method is { HasParameters: true, HasThis: false } &&
                          (method.DeclaringType.Name.EndsWith("Extensions") ||
                           method.Resolve()?.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute") == true);

        var declaringType = isExtension ? method.Parameters[0].ParameterType : method.DeclaringType;
        var declaringTypeRef = declaringType.ToTypeReference();
        _script.TypeReferences.AddSafe(declaringTypeRef);

        var mangle = method.Mangle();
        if (!_script.AStringTable.Contains(mangle))
            _script.AStringTable.Add(mangle);

        return new()
        {
            NameIndex = _script.AStringTable.IndexOf(mangle).MinZero(),
            TypeReferenceIndex = _script.TypeReferences.IndexOf(declaringTypeRef).MinZero()
        };
    }

    extension(TypeReference type)
    {
        internal MachineType GetFishType()
    {
        if (TypeReference.IsStringType(type) || type.Name is "Resource" || type.FullName is "LittleBigPlanet.Resource" || TypeReference.GetGuidValue(type) == "12055")
        {
            return MachineType.OBJECT;
        }

        var resolvedType = type.Resolve();
        if (resolvedType != null && (resolvedType.CustomAttributes.Any(a => a.AttributeType.Name is "Entrypoint" or "Guid") || resolvedType.BaseType?.Name is "Tweakable" or "RScript"))
        {
            return MachineType.SAFEPTR;
        }

        return type.FullName switch
        {
            "System.Void" => MachineType.VOID,
            "System.Int32" => MachineType.S32,
            "System.Int64" => MachineType.S64,
            "System.Single" => MachineType.F32,
            "System.Double" => MachineType.F64,
            "System.Boolean" => MachineType.BOOL,
            "System.Char" => MachineType.CHAR,
            "System.String" => MachineType.OBJECT,
            _ when Helpers.IsVectorType(type) => MachineType.V4,
            "Fisher.Matrix4x4" or "Native.Matrix4x4" => MachineType.M44,
            _ => MachineType.OBJECT
        };
    }

    private static string GetTypeNameString(TypeReference typeRef)
    {
        var resolved = typeRef.Resolve();
        if (resolved?.HasCustomAttributes != true)
            return typeRef.IsNested
                ? $"{typeRef.DeclaringType.Name}.{typeRef.Name}"
                : !string.IsNullOrEmpty(typeRef.Namespace) && typeRef.Namespace != "System" &&
                  typeRef.Namespace != "LittleBigPlanet" && typeRef.Namespace != "Fisher"
                    ? $"{typeRef.Namespace}.{typeRef.Name}"
                    : typeRef.Name;
        var attr = resolved.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name is "TypeName" or "TypeNameAttribute");
        if (attr != null && attr.ConstructorArguments.Count > 0)
        {
            return attr.ConstructorArguments[0].Value?.ToString() ?? typeRef.Name;
        }
        if (resolved.CustomAttributes.Any(a => a.AttributeType.Name == "Entrypoint"))
        {
            return typeRef.Name;
        }
        return typeRef.IsNested
            ? $"{typeRef.DeclaringType.Name}.{typeRef.Name}"
            : !string.IsNullOrEmpty(typeRef.Namespace) && typeRef.Namespace != "System" && typeRef.Namespace != "LittleBigPlanet" && typeRef.Namespace != "Fisher"
                ? $"{typeRef.Namespace}.{typeRef.Name}"
                : typeRef.Name;
    }

    private static bool IsStringType(TypeReference? t)
    {
        switch (t)
        {
            case null:
                return false;
            case { Namespace: "System", Name: "String" } or { Namespace: "LittleBigPlanet", Name: "String" }:
                return true;
            default:
            {
                var resolved = t.Resolve();
                return resolved is { Namespace: "System", Name: "String" } or { Namespace: "LittleBigPlanet", Name: "String" };
            }
        }
    }

    private static string GetStringScriptGuid(TypeReference? typeRef = null)
    {
        if (typeRef?.Module == null) return "0";
        try
        {
            var seguedRef = typeRef.Module.AssemblyReferences.FirstOrDefault(a => a.Name == "LittleBigPlanet");
            if (seguedRef == null || typeRef.Module.AssemblyResolver == null)
            {
                return "0";
            }
            var seguedAsm = typeRef.Module.AssemblyResolver.Resolve(seguedRef);
            var stringTypeDef = seguedAsm?.MainModule.GetType("LittleBigPlanet.String");
            var guidAttr =
                stringTypeDef?.CustomAttributes.FirstOrDefault(e =>
                    e.AttributeType.Name is "Guid" or "GuidAttribute");
            if (guidAttr != null)
            {
                if (guidAttr.HasConstructorArguments && guidAttr.ConstructorArguments.Count > 0)
                    return guidAttr.ConstructorArguments[0].Value?.ToString() ?? "0";
                if (guidAttr.HasProperties && guidAttr.Properties.Count > 0)
                    return guidAttr.Properties[0].Argument.Value?.ToString() ?? "0";
                if (guidAttr.HasFields && guidAttr.Fields.Count > 0)
                    return guidAttr.Fields[0].Argument.Value?.ToString() ?? "0";
            }
        }
        catch
        {
            // Fallback to canonical LBP String GUID
        }
        return "0";
    }

    private static string GetGuidValue(TypeReference? typeRef)
    {
        if (typeRef == null) return "0";
        if (TypeReference.IsStringType(typeRef)) return TypeReference.GetStringScriptGuid(typeRef);

        var typeDef = typeRef as TypeDefinition ?? typeRef.Resolve();
        if (typeDef == null) return "0";
        if (TypeReference.IsStringType(typeDef)) return TypeReference.GetStringScriptGuid(typeDef);

        var guidAttr = typeDef.CustomAttributes.FirstOrDefault(e => e.AttributeType.Name is "Guid" or "GuidAttribute");
        if (guidAttr == null) return "0";
        if (guidAttr.HasConstructorArguments && guidAttr.ConstructorArguments.Count > 0)
            return guidAttr.ConstructorArguments[0].Value?.ToString() ?? "0";
        if (guidAttr.HasProperties && guidAttr.Properties.Count > 0)
            return guidAttr.Properties[0].Argument.Value?.ToString() ?? "0";
        if (guidAttr.HasFields && guidAttr.Fields.Count > 0)
            return guidAttr.Fields[0].Argument.Value?.ToString() ?? "0";

        return "0";
    }

    private static ResourceDescriptor CreateScriptDescriptor(string guid)
    {
        var desc = new ResourceDescriptor(guid, CwResourceType.Script);
        desc.SetFlags(CompressionFlags.USE_ALL_COMPRESSION);
        return desc;
    }

    internal FishLib.TypeReference ToTypeReference()
    {
        if (_currentCompiledType != null && (type.FullName == _currentCompiledType.FullName || type.Resolve()?.FullName == _currentCompiledType.FullName))
        {
            return new()
            {
                ArrayBaseMachineType = MachineType.VOID,
                MachineType = MachineType.SAFEPTR,
                DimensionCount = 0,
                FishType = FishType.VOID,
                Script = _script.Self,
                TypeNameIndex = null
            };
        }

        if (TypeReference.IsStringType(type))
        {
            return new()
            {
                ArrayBaseMachineType = MachineType.VOID,
                MachineType = MachineType.OBJECT,
                DimensionCount = 0,
                FishType = FishType.VOID,
                Script = TypeReference.CreateScriptDescriptor(TypeReference.GetStringScriptGuid(type)),
                TypeNameIndex = null
            };
        }

        var isBuiltInValueType = type.Name switch
        {
            "Void" or "Boolean" or "Char" or "Int32" or "Single" or "Double" or "Int64" when type.Namespace == "System" => true, // CIL types
            "Vec2" or "Vec3" or "Vec4" or "Vector2" or "Vector3" or "Vector4" or "Matrix4x4" when type.Namespace == "Fisher" => true, // custom types
            _ => false
        };

        if (isBuiltInValueType)
        {
            switch (type.Name)
            {
                case "Void" when type.Namespace == "System":
                    return Void;
                case "Boolean" when type.Namespace == "System":
                    return Bool;
            }

            var fishType = type.Name switch
            {
                "Char" => FishType.CHAR,
                "Int32" => FishType.INT,
                "Single" => FishType.FLOAT,
                "Double" => FishType.DOUBLE,
                "Int64" => FishType.LONG,
                "Vec2" or "Vector2" => FishType.V2,
                "Vec3" or "Vector3" => FishType.V3,
                "Vec4" or "Vector4" => FishType.V4,
                "Matrix4x4" => FishType.M44,
                _ => FishType.VOID
            };

            return new()
            {
                ArrayBaseMachineType = MachineType.VOID,
                MachineType = type.GetFishType(),
                DimensionCount = 0,
                FishType = fishType,
                TypeNameIndex = null
            };
        }

        var guid = TypeReference.GetGuidValue(type);
        if (guid != "0")
        {
            return new()
            {
                ArrayBaseMachineType = MachineType.VOID,
                MachineType = type.GetFishType(),
                DimensionCount = 0,
                FishType = FishType.VOID,
                Script = TypeReference.CreateScriptDescriptor(guid),
                TypeNameIndex = null
            };
        }

        _script.AStringTable.AddSafe(TypeReference.GetTypeNameString(type));

        type.Resolve();
        return new()
        {
            ArrayBaseMachineType = MachineType.SAFEPTR,
            MachineType = type.GetFishType(),
            DimensionCount = 0,
            FishType = FishType.VOID,
            TypeNameIndex = _script.AStringTable.IndexOf(TypeReference.GetTypeNameString(type)).MinZero()
        };
    }

    private string Mangle()
    {
        var arrayPrefix = "";
        if (type.IsArray)
        {
            arrayPrefix = "[[[[[[[[[[[[[[[[[[[[["; // offers 20-items arrays by default due to CIL limitations
        }

        var name = type.Name;
        switch (name)
        {
            case "Vec2" or "Vector2":
                return arrayPrefix + "p";
            case "Vec3" or "Vector3":
                return arrayPrefix + "q";
            case "Vec4" or "Vector4":
                return arrayPrefix + "r";
            case "Matrix4x4":
                return arrayPrefix + "m";
            case "UInt32":
                return arrayPrefix + "g";
            default:
            {
                var typeNameStr = TypeReference.GetTypeNameString(type);

                return arrayPrefix + type.GetFishType() switch
                {
                    MachineType.VOID => "v",
                    MachineType.BOOL => "b",
                    MachineType.CHAR => "c",
                    MachineType.S32 => "i",
                    MachineType.F32 => "f",
                    MachineType.S64 => "j",
                    MachineType.F64 => "d",
                    MachineType.V4 => "r",
                    MachineType.SAFEPTR or MachineType.OBJECT or MachineType.RAWPTR => "Q" + typeNameStr.Length + typeNameStr,
                    MachineType.M44 => "m",
                    _ => throw new NotImplementedException()
                };
            }
        }
    }
    }

    public static MachineType GetMachineType(this Type type)
    {
        return type.Name switch
        {
            "Vec2" or "Vec3" or "Vec4" or "Vector2" or "Vector3" or "Vector4" => MachineType.V4,
            "Matrix4x4" => MachineType.M44,
            _ => type.FullName switch
            {
                "System.Void" => MachineType.VOID,
                "System.Int32" => MachineType.S32,
                "System.Int64" => MachineType.S64,
                "System.Single" => MachineType.F32,
                "System.Double" => MachineType.F64,
                "System.Boolean" => MachineType.BOOL,
                "System.Char" => MachineType.CHAR,
                "System.String" => MachineType.SAFEPTR,
                _ => MachineType.OBJECT
            }
        };
    }

    public static string Mangle(this MethodDefinition method)
    {
        if (method.Name == ".ctor") return ".init__";
        var sb = new StringBuilder();
        sb.Append(method.Name);
        sb.Append("__");
        foreach (var param in method.Parameters)
        {
            sb.Append(param.ParameterType.Mangle());
        }
        return sb.ToString();
    }

    public static string Mangle(this MethodReference method)
    {
        var isExtension = method is { HasParameters: true, HasThis: false } &&
                          (method.DeclaringType.Name.EndsWith("Extensions") ||
                           method.Resolve()?.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute") == true);

        if (method.Name == ".ctor") return ".init__";

        var sb = new StringBuilder();
        sb.Append(method.Name);
        sb.Append("__");
        var paramsToMangle = isExtension ? method.Parameters.Skip(1) : method.Parameters;
        foreach (var param in paramsToMangle)
        {
            sb.Append(param.ParameterType.Mangle());
        }
        return sb.ToString();
    }

    internal static readonly FishLib.TypeReference Void = new()
    {
        FishType = FishType.VOID,
        MachineType = MachineType.VOID,
        ArrayBaseMachineType = MachineType.VOID,
        TypeNameIndex = null,
        DimensionCount = 0
    };

    internal static readonly FishLib.TypeReference Bool = new()
    {
        FishType = FishType.BOOL,
        MachineType = MachineType.BOOL,
        ArrayBaseMachineType = MachineType.VOID,
        TypeNameIndex = null,
        DimensionCount = 0
    };

    private static void LoadSystemDeclarations()
    {
        _script.TypeReferences.AddRange([Void, Bool]);
    }

    private static void LoadDeclarations(TypeDefinition type)
    {

        // add myself
        _script.TypeReferences.AddSafe(type.ToTypeReference());

        foreach (var field in type.Fields)
        {
            _script.AStringTable.Add(field.Name);
            _script.FieldDefinitions.Add(field.ToFieldDefinition());
        }

        foreach (var method in type.Methods)
        {
            _script.AStringTable.AddSafe(method.Mangle());
            if (method.ReturnType.FullName != "System.Void")
            {
                _script.AStringTable.AddSafe(TypeReference.GetTypeNameString(method.ReturnType));
                _script.TypeReferences.AddSafe(method.ReturnType.ToTypeReference());
            }
            foreach (var param in method.Parameters)
            {
                _script.AStringTable.AddSafe(TypeReference.GetTypeNameString(param.ParameterType));
                _script.TypeReferences.AddSafe(param.ParameterType.ToTypeReference());
            }

            if (method is not { HasBody: true, Body: not null }) continue;
            foreach (var var in method.Body.Instructions)
            {
                if (var.OpCode.Code == Code.Ldstr)
                {
                    if ((string)var.Operand != "")
                        _script.WStringTable.AddSafe((string)var.Operand);
                } // load strings wide
                switch (var.Operand)
                {
                    case TypeReference typeRef:
                    {
                        _script.AStringTable.AddSafe(typeRef.Name);
                        _script.TypeReferences.AddSafe(typeRef.ToTypeReference());
                        break;
                    }
                    case MethodReference methodRef:
                    {
                        if (methodRef.Name == ".ctor" && Helpers.IsVectorType(methodRef.DeclaringType))
                        {
                            break;
                        }
                        _script.AStringTable.AddSafe(methodRef.DeclaringType.Name);
                        _script.TypeReferences.AddSafe(methodRef.DeclaringType.ToTypeReference());
                        _script.AStringTable.AddSafe(methodRef.Resolve().Mangle());
                        _script.FunctionReferences.AddSafe(methodRef.ToFunctionReference());
                        break;
                    }
                    case FieldReference fieldRef:
                    {
                        _script.AStringTable.AddSafe(fieldRef.DeclaringType.Name);
                        _script.TypeReferences.AddSafe(fieldRef.DeclaringType.ToTypeReference());
                        _script.AStringTable.AddSafe(fieldRef.FieldType.Name);
                        _script.TypeReferences.AddSafe(fieldRef.FieldType.ToTypeReference());
                        _script.FieldReferences.AddSafe(fieldRef.ToFieldReference());
                        break;
                    }
                } // load basic refs from CIL
            }
        }
    }
}
