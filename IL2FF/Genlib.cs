using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using CwLibNet.Types.Archives;
using CwLibNet.Types.Data;
using CwLibNet.Types.Databases;
using Fisher;
using FishLib;
using Mono.Cecil;
using FieldAttributes = System.Reflection.FieldAttributes;
using Guid = Fisher.Guid;
using MethodAttributes = System.Reflection.MethodAttributes;
using TypeAttributes = System.Reflection.TypeAttributes;
using TypeReference = FishLib.TypeReference;

namespace IL2FF;

public static class Genlib
{
    private static readonly Dictionary<string, TypeBuilder> Types = [];
    private static readonly Dictionary<string, TypeBuilder> TypesByGuid = [];
    private static readonly Dictionary<string, string> SuperClassByGuid = [];
    private static Script? _currentScript;

    public static void Generate(string map, string farc)
    {
        FileDb fileDb = new(map);
        FileArchive archive = new(farc);
        GUID[] guids = [.. fileDb.Where(e => e.Path.EndsWith(".ff")).Select(e => e.GetGuid())];
        var targetFrameworkAttr = new CustomAttributeBuilder(
            typeof(TargetFrameworkAttribute).GetConstructor([typeof(string)])!,
            [".NETCoreApp,Version=v10.0"],
            [typeof(TargetFrameworkAttribute).GetProperty("FrameworkDisplayName")!],
            [".NET 10.0"]
        );
        PersistedAssemblyBuilder builder = new(new AssemblyName("LittleBigPlanet"), SysRuntime, [targetFrameworkAttr]);
        var module = builder.DefineDynamicModule("LittleBigPlanet.dll");

        // Pass 1: Create all TypeBuilders
        foreach (var guid in guids)
        {
            var data = archive.Extract(fileDb.Get(guid)!.Sha1);
            if (data == null)
            {
                Console.WriteLine($"N: {guid} ignored due to extraction failure...");
                continue;
            }
            var script = Script.FromResource(data);
            var type = module.DefineType("LittleBigPlanet." + script.Name, TypeAttributes.Public | TypeAttributes.Class);
            type.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(Guid).GetConstructor(Type.EmptyTypes)!,
                [],
                [typeof(Guid).GetField("Value")!],
                [guid.Value]
            ));
            Types.Add(script.Name, type);
            TypesByGuid.Add(guid.Value.ToString(), type);

            var superClassGuid = script.SuperClassScript?.GetGUID()?.Value.ToString();
            if (!string.IsNullOrEmpty(superClassGuid) && superClassGuid != "0")
            {
                SuperClassByGuid[guid.Value.ToString()] = superClassGuid;
            }
        }

        // Step 1.5: Set parent types for derived classes
        foreach (var (typeGuidStr, baseGuidStr) in SuperClassByGuid)
        {
            var typeBuilder = TypesByGuid[typeGuidStr];
            if (TypesByGuid.TryGetValue(baseGuidStr, out var baseTypeBuilder))
            {
                typeBuilder.SetParent(baseTypeBuilder);
            }
            else
            {
                typeBuilder.SetParent(GetCoreType(typeof(object)));
            }
        }

        // Step 2: Populate fields and methods
        foreach (var guid in guids)
        {
            var data = archive.Extract(fileDb.Get(guid)!.Sha1);
            if (data == null)
            {
                continue;
            }
            _currentScript = Script.FromResource(data);
            PopulateType(Types[_currentScript.Name]);
            _currentScript = null;
        }
        foreach (var type in Types.Values)
        {
            type.CreateType();
        }
        // Step 3: Save
        using (var stream = File.Create("LittleBigPlanet.dll"))
        {
            builder.Save(stream);
        }
        // Step 4: Remove all dependencies except System.Runtime
        FixCoreLibReference("LittleBigPlanet.dll");
    }

    private static void FixCoreLibReference(string assemblyPath)
    {
        using var stream = File.Open(assemblyPath, FileMode.Open, FileAccess.ReadWrite);
        var asm = AssemblyDefinition.ReadAssembly(stream);
        var modified = false;
        foreach (var aref in asm.MainModule.AssemblyReferences.Where(aref => aref.Name is "System.Private.CoreLib" or "System.Runtime"))
        {
            aref.Name = "System.Runtime";
            aref.PublicKey = [];
            aref.PublicKeyToken = [0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a];
            modified = true;
        }

        if (!modified) return;
        stream.Position = 0;
        asm.Write(stream);
        stream.SetLength(stream.Position);
    }

    private static readonly Assembly SysRuntime = LoadRefCoreAssembly();

    private static Assembly LoadRefCoreAssembly()
    {
        const string sdkBase = @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref";
        if (!Directory.Exists(sdkBase)) return Assembly.Load("System.Runtime");
        var dirs = Directory.GetDirectories(sdkBase);
        if (dirs.Length <= 0) return Assembly.Load("System.Runtime");
        Array.Sort(dirs);
        var latest = dirs[^1];
        var refDll = Path.Combine(latest, "ref", "net10.0", "System.Runtime.dll");
        if (!File.Exists(refDll)) return Assembly.Load("System.Runtime");
        var alc = new AssemblyLoadContext("RefContext", isCollectible: false);
        return alc.LoadFromAssemblyPath(refDll);
    }

    private static Type GetCoreType(Type type)
    {
        if (type == typeof(void)) return SysRuntime.GetType("System.Void")!;
        return SysRuntime.GetType(type.FullName!) ?? type;
    }

    private static Type GetSystemType(int typeReferenceIdx)
    {
        return _currentScript!.TypeReferences[typeReferenceIdx].ToType();
    }

    private static Type ToType(this MachineType type)
    {
        return type switch
        {
            MachineType.BOOL => GetCoreType(typeof(bool)).MakeArrayType(),
            MachineType.VOID => GetCoreType(typeof(void)).MakeArrayType(),
            MachineType.CHAR => GetCoreType(typeof(char)).MakeArrayType(),
            MachineType.S32 => GetCoreType(typeof(int)).MakeArrayType(),
            MachineType.F32 => GetCoreType(typeof(float)).MakeArrayType(),
            MachineType.V4 => typeof(Vec4).MakeArrayType(),
            MachineType.M44 => typeof(Matrix4x4).MakeArrayType(),
            MachineType.S64 => GetCoreType(typeof(long)).MakeArrayType(),
            MachineType.F64 => GetCoreType(typeof(double)).MakeArrayType(),
            _ => GetCoreType(typeof(object)).MakeArrayType()
        };
    }

    private static Type ToType(this TypeReference type)
    {
        var typeName = _currentScript?.AStringTable.ElementAtOrDefault(type.TypeNameIndex ?? -1);
        if (typeName == "String")
        {
            return GetCoreType(typeof(string));
        }
        if (typeName?[0] == 'P')
            return GetCoreType(typeof(object));
        if (type.DimensionCount > 0)
        {
            return type.ArrayBaseMachineType.ToType();
        }
        var scriptItem = type.Script?.GetGUID()?.Value.ToString();
        var resType = type.FishType switch
        {
            FishType.INT => GetCoreType(typeof(int)),
            FishType.LONG => GetCoreType(typeof(long)),
            FishType.FLOAT => GetCoreType(typeof(float)),
            FishType.DOUBLE => GetCoreType(typeof(double)),
            FishType.BOOL => GetCoreType(typeof(bool)),
            FishType.CHAR => GetCoreType(typeof(char)),
            FishType.VOID => type.MachineType == MachineType.VOID
                ? GetCoreType(typeof(void))
                : scriptItem != null && TypesByGuid.TryGetValue(scriptItem, out var t1) ? t1 : GetCoreType(typeof(object)),
            FishType.V2 => typeof(Vec2),
            FishType.V3 => typeof(Vec3),
            FishType.V4 => typeof(Vec4),
            FishType.M44 => typeof(Matrix4x4),
            FishType.GUID => GetCoreType(typeof(uint)),
            _ => scriptItem != null && TypesByGuid.TryGetValue(scriptItem, out var t2) ? t2 : GetCoreType(typeof(object))
        };

        return resType.Name == "String" ? GetCoreType(typeof(string)) : resType;
    }

    private static void PopulateType(TypeBuilder type)
    {
        foreach (var field in _currentScript!.FieldDefinitions)
        {
            if (field.Modifiers.HasFlag(Modifiers.EPHEMERAL))
            {
                type.DefineField(_currentScript.AStringTable[field.NameIndex], typeof(void*), FieldAttributes.Public | FieldAttributes.Static);
                continue;
            }
            var attributes = field.Modifiers.HasFlag(Modifiers.PUBLIC) ? FieldAttributes.Public : FieldAttributes.Private;
            if (field.Modifiers.HasFlag(Modifiers.STATIC))
                attributes |= FieldAttributes.Static;
            type.DefineField(_currentScript.AStringTable[field.NameIndex], GetSystemType(field.TypeReferenceIndex), attributes);
        }
        HashSet<string> definedCtors = [];
        foreach (var meth in _currentScript.Functions)
        {
            var attributes = meth.Modifiers.HasFlag(Modifiers.PUBLIC)
                ? MethodAttributes.Public
                : meth.Modifiers.HasFlag(Modifiers.PROTECTED) ? MethodAttributes.Family : MethodAttributes.Private;

            if (meth.Modifiers.HasFlag(Modifiers.STATIC))
            {
                attributes |= MethodAttributes.Static;
            }
            else
            {
                var isVirtualInVm = meth.Modifiers.HasFlag(Modifiers.VIRTUAL) ||
                                    meth.Modifiers.HasFlag(Modifiers.OVERRIDE) ||
                                    meth.Modifiers.HasFlag(Modifiers.ABSTRACT) ||
                                    !meth.Modifiers.HasFlag(Modifiers.STATIC);

                if (isVirtualInVm)
                {
                    attributes |= MethodAttributes.Virtual | MethodAttributes.HideBySig;
                    if (!meth.Modifiers.HasFlag(Modifiers.OVERRIDE))
                    {
                        attributes |= MethodAttributes.NewSlot;
                    }
                }
            }

            var rawName = _currentScript.AStringTable[meth.NameStringIndex];
            var (cleanName, paramTypes) = UnmangleMethod(rawName);
            var isCtor = cleanName == ".ctor" || cleanName.Equals(_currentScript.Name, StringComparison.OrdinalIgnoreCase);

            if (isCtor)
            {
                var ctorSig = string.Join(",", paramTypes.Select(p => p.FullName ?? p.Name));
                if (definedCtors.Add(ctorSig))
                {
                    type.DefineConstructor(attributes, CallingConventions.Standard, paramTypes);
                }
            }
            else if (Types.ContainsKey(cleanName))
            {
                // Skip constructor methods of other/base classes
            }
            else
            {
                var builder = type.DefineMethod(cleanName, attributes);
                var returnType = GetSystemType(meth.TypeReferenceIndex);
                if (type.Name == "String" && (returnType == type || returnType.Name == "String"))
                {
                    returnType = GetCoreType(typeof(string));
                }
                builder.SetReturnType(returnType);
                if (paramTypes.Length > 0)
                {
                    builder.SetParameters(paramTypes);
                }
            }
        }
    }

    private static (string CleanName, Type[] ParamTypes) UnmangleMethod(string mangledName)
    {
        var separatorIdx = mangledName.IndexOf("__", StringComparison.Ordinal);
        if (separatorIdx < 0)
        {
            throw new InvalidOperationException("Not a valid Fish name");
        }

        var cleanName = mangledName[..separatorIdx];
        var mangledParams = mangledName[(separatorIdx + 2)..];
        List<Type> paramTypes = [];

        var i = 0;
        while (i < mangledParams.Length)
        {
            var isArray = false;
            while (i < mangledParams.Length && mangledParams[i] == '[')
            {
                isArray = true;
                i++;
            }

            if (i >= mangledParams.Length) break;

            var ch = mangledParams[i];
            Type? paramType;

            i++;
            if (ch == 'Q')
            {
                var len = 0;
                while (i < mangledParams.Length && char.IsDigit(mangledParams[i]))
                {
                    len = len * 10 + (mangledParams[i] - '0');
                    i++;
                }

                if (len > 0 && i + len <= mangledParams.Length)
                {
                    var className = mangledParams.Substring(i, len);
                    i += len;
                    paramType = ResolveClassName(className);
                }
                else
                {
                    paramType = GetCoreType(typeof(object));
                }
            }
            else
            {
                paramType = ch switch
                {
                    'v' => null,
                    'b' => GetCoreType(typeof(bool)),
                    'w' or 'c' => GetCoreType(typeof(char)),
                    'i' => GetCoreType(typeof(int)),
                    'f' => GetCoreType(typeof(float)),
                    'j' => GetCoreType(typeof(long)),
                    'd' => GetCoreType(typeof(double)),
                    'p' => typeof(Vec2),
                    'q' => typeof(Vec3),
                    'r' => typeof(Vec4),
                    'm' => typeof(Matrix4x4),
                    'P' => GetCoreType(typeof(object)),
                    'g' => GetCoreType(typeof(uint)),
                    _ => GetCoreType(typeof(object))
                };
            }

            if (paramType == null) continue;
            if (isArray)
            {
                paramType = paramType.MakeArrayType();
            }
            paramTypes.Add(paramType);
        }

        return (cleanName, [.. paramTypes]);
    }

    private static Type ResolveClassName(string className)
    {
        switch (className)
        {
            case "String":
                return GetCoreType(typeof(string));
            case "Object":
                return GetCoreType(typeof(object));
            case "Vec2":
                return typeof(Vec2);
            case "Vec3":
                return typeof(Vec3);
            case "Vec4":
                return typeof(Vec4);
            case "Matrix4x4":
                return typeof(Matrix4x4);
        }

        if (Types.TryGetValue(className, out var builderType)) return builderType;
        return GetCoreType(typeof(object));
    }
}