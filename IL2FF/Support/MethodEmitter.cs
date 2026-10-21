using FishLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldReference = Mono.Cecil.FieldReference;
using Instruction = Mono.Cecil.Cil.Instruction;
using TypeReference = Mono.Cecil.TypeReference;

namespace IL2FF.Support;

public class MethodEmitter
{
    private readonly MethodDefinition _method;
    private readonly TypeDefinition _declaringType;
    private readonly Script _script;
    private readonly int _bytecodeBaseOffset;
    private readonly int _lineNumbersBaseOffset;
    private readonly int _localVariablesBaseOffset;

    private readonly Stack<Register> _registers = new();
    private readonly List<FishLib.Instruction> _instructions = [];
    private readonly List<ushort> _lineNumbers = [];
    private readonly List<Register> _localVarRegs = [];
    private readonly List<LocalVariable> _localVariables = [];
    private readonly List<(int branchVmIdx, Instruction targetCilInst)> _branchFixups = [];

    private ushort _currentRegOffset;
    private ushort _regCount;
    private readonly Register _me;
    private readonly Register _loc1;
    private readonly Register _loc2;
    private readonly Register _loc3;
    private readonly Register _loc4;
    private readonly int _argumentsBegin;
    private readonly int _argumentsSize;
    private readonly int _localVarsSize;

    public IReadOnlyList<FishLib.Instruction> Instructions => _instructions;
    public IReadOnlyList<ushort> LineNumbers => _lineNumbers;
    public IReadOnlyList<LocalVariable> LocalVariables => _localVariables;

    public MethodEmitter(
        MethodDefinition method,
        TypeDefinition declaringType,
        Script script,
        int bytecodeBaseOffset,
        int lineNumbersBaseOffset,
        int localVariablesBaseOffset)
    {
        _method = method;
        _declaringType = declaringType;
        _script = script;
        _bytecodeBaseOffset = bytecodeBaseOffset;
        _lineNumbersBaseOffset = lineNumbersBaseOffset;
        _localVariablesBaseOffset = localVariablesBaseOffset;

        // Initialize arguments
        _argumentsBegin = _script.Arguments.Count;
        var argOffset = (ushort)(method.IsStatic ? 0 : 4);

        foreach (var param in method.Parameters)
        {
            var paramTypeRef = param.ParameterType.ToTypeReference();
            _script.TypeReferences.AddSafe(paramTypeRef);
            var typeRefIdx = _script.TypeReferences.IndexOf(paramTypeRef).MinZero();

            var machineType = param.ParameterType.GetFishType();
            argOffset = Helpers.AlignArgOffset(argOffset, machineType);

            _script.Arguments.Add(new()
            {
                TypeReferenceIndex = typeRefIdx,
                Offset = argOffset
            });

            argOffset += (ushort)Helpers.GetMachineTypeSize(machineType);
        }
        _argumentsSize = method.Parameters.Count;

        // Initialize local variables
        _currentRegOffset = (ushort)((argOffset + 0x3) & ~0x3);

        if (method.Body.HasVariables)
        {
            foreach (var vDef in method.Body.Variables)
            {
                var machineType = vDef.VariableType.GetFishType();
                var typeSize = (ushort)Helpers.GetMachineTypeSize(machineType);

                _currentRegOffset = Helpers.AlignOffset(_currentRegOffset, machineType);

                Register varReg = new(_currentRegOffset, machineType);
                _localVarRegs.Add(varReg);

                var vTypeRef = vDef.VariableType.ToTypeReference();
                _script.TypeReferences.AddSafe(vTypeRef);
                var typeRefIdx = _script.TypeReferences.IndexOf(vTypeRef).MinZero();

                _localVariables.Add(new()
                {
                    TypeReferenceIndex = typeRefIdx,
                    Offset = _currentRegOffset
                });

                _currentRegOffset += typeSize;
            }
        }

        _localVarsSize = _localVarRegs.Count;

        var meType = method.IsStatic
            ? method.Parameters.Count > 0 ? method.Parameters[0].ParameterType.GetFishType() : MachineType.VOID
            : MachineType.OBJECT;
        _me = new(0, meType);

        _loc1 = GetLocalReg(0);
        _loc2 = GetLocalReg(1);
        _loc3 = GetLocalReg(2);
        _loc4 = GetLocalReg(3);
        _regCount = (ushort)((_currentRegOffset + 0xF) & ~0xF);
    }

    public FunctionDefinition Emit()
    {
        EmitNop();

        EmitBaseConstructorCallIfNeeded();

        var cilToVmMap = new int[_method.Body.Instructions.Count + 1];

        for (var cilIdx = 0; cilIdx < _method.Body.Instructions.Count; cilIdx++)
        {
            var instruction = _method.Body.Instructions[cilIdx];
            cilToVmMap[cilIdx] = _instructions.Count;

            // mapping instruction classes to the more specific emitter
            // might think of moving to a class handler and a group dictionary
            // but for now it works, and it's easy to read
            switch (instruction.OpCode.Code)
            {
                case Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg or Code.Ldarg_S:
                    EmitLoadArgument(instruction);
                    break;
                case Code.Ldarga or Code.Ldarga_S:
                    EmitLoadArgumentAddress(instruction);
                    break;
                case Code.Ldc_I4 or Code.Ldc_I4_S or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or
                     Code.Ldc_I4_3 or Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or
                     Code.Ldc_I4_8 or Code.Ldc_I4_M1:
                    EmitLoadIntConstant(instruction, cilIdx);
                    break;
                case Code.Ldc_R4:
                    EmitLoadFloatConstant(instruction);
                    break;
                case Code.Ldc_I8:
                    EmitLoadLongConstant(instruction);
                    break;
                case Code.Ldstr:
                    EmitLoadStringConstant(instruction);
                    break;
                case Code.Ldnull:
                    EmitLoadNull();
                    break;
                case Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc or Code.Ldloc_S:
                    EmitLoadLocal(instruction);
                    break;
                case Code.Ldloca or Code.Ldloca_S:
                    EmitLoadLocalAddress(instruction);
                    break;
                case Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3 or Code.Stloc or Code.Stloc_S:
                    EmitStoreLocal(instruction);
                    break;
                case Code.Initobj:
                    EmitInitObj(instruction);
                    break;
                case Code.Add or Code.Sub or Code.Mul or Code.Div or Code.And or Code.Or or Code.Xor or
                     Code.Shl or Code.Shr or Code.Shr_Un:
                    EmitBinaryOp(instruction.OpCode.Code);
                    break;
                case Code.Not:
                    EmitNot();
                    break;
                case Code.Ceq or Code.Cgt or Code.Cgt_Un or Code.Clt or Code.Clt_Un:
                    EmitComparison(instruction.OpCode.Code);
                    break;
                case Code.Conv_I8 or Code.Conv_U8 or Code.Conv_I4 or Code.Conv_U4 or
                     Code.Conv_I or Code.Conv_U or Code.Conv_R4 or Code.Conv_R8:
                    EmitConversion(instruction.OpCode.Code);
                    break;
                case Code.Ldfld:
                    EmitFieldLoad(instruction);
                    break;
                case Code.Stfld:
                    EmitFieldStore(instruction);
                    break;
                case Code.Ldsfld:
                    EmitStaticFieldLoad(instruction);
                    break;
                case Code.Stsfld:
                    EmitStaticFieldStore(instruction);
                    break;
                case Code.Br or Code.Br_S or Code.Brfalse or Code.Brfalse_S or Code.Brtrue or Code.Brtrue_S:
                    EmitBranch(instruction);
                    break;
                case Code.Newobj or Code.Call or Code.Callvirt:
                    EmitCall(instruction);
                    break;
                case Code.Ret:
                    EmitReturn();
                    break;
                default:
                    EmitNop();
                    break;
            }
        }

        cilToVmMap[_method.Body.Instructions.Count] = _instructions.Count;

        // Fixup branch targets
        foreach (var (branchVmIdx, targetCilInst) in _branchFixups)
        {
            var targetCilIdx = _method.Body.Instructions.IndexOf(targetCilInst);
            var targetVmIdx = targetCilIdx >= 0 ? cilToVmMap[targetCilIdx] : _instructions.Count;
            var vmOffset = targetVmIdx - branchVmIdx;

            if (_instructions[branchVmIdx].Parameters is BranchArgument branchArg)
            {
                branchArg.Offset = vmOffset;
            }
        }

        _lineNumbers.AddRange(Enumerable.Repeat((ushort)1, _instructions.Count));

        var stackSize = (ushort)((_regCount + 0xF) & ~0xF);
        return new FunctionDefinition
        {
            BytecodeBegin = _bytecodeBaseOffset,
            BytecodeSize = _instructions.Count,
            LineNumbersBegin = _lineNumbersBaseOffset,
            LineNumbersSize = _lineNumbers.Count,
            LocalVariablesBegin = _localVariablesBaseOffset,
            LocalVariablesSize = _localVarsSize,
            Modifiers = Generator.GetModifiers(_method),
            TypeReferenceIndex = _script.TypeReferences.IndexOf(_method.ReturnType.ToTypeReference()).MinZero(),
            NameStringIndex = _script.AStringTable.IndexOf(_method.Mangle()).MinZero(),
            ArgumentsBegin = _argumentsBegin,
            ArgumentsSize = _argumentsSize,
            StackSize = stackSize
        };
    }

    private void EmitNop()
    {
        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.NOP,
            Parameters = new NoOpArgument()
        });
    }

    private Register GetLocalReg(int index)
    {
        if (index >= 0 && index < _localVarRegs.Count)
            return _localVarRegs[index];

        while (_localVarRegs.Count <= index)
        {
            var vType = _method.Body.HasVariables && _localVarRegs.Count < _method.Body.Variables.Count
                ? _method.Body.Variables[_localVarRegs.Count].VariableType.GetFishType()
                : MachineType.S32;
            _currentRegOffset = Helpers.AlignOffset(_currentRegOffset, vType);
            Register varReg = new(_currentRegOffset, vType);
            _localVarRegs.Add(varReg);
            _currentRegOffset += (ushort)Helpers.GetMachineTypeSize(vType);
        }
        return _localVarRegs[index];
    }

    private ushort GetParamOffset(int targetParamIndex)
    {
        var offset = (ushort)(_method.IsStatic ? 0 : 4);
        for (var idx = 0; idx < targetParamIndex && idx < _method.Parameters.Count; idx++)
        {
            var pType = _method.Parameters[idx].ParameterType.GetFishType();
            offset = Helpers.AlignArgOffset(offset, pType);
            offset += (ushort)Helpers.GetMachineTypeSize(pType);
        }
        if (targetParamIndex < _method.Parameters.Count)
        {
            offset = Helpers.AlignArgOffset(offset, _method.Parameters[targetParamIndex].ParameterType.GetFishType());
        }
        return offset;
    }

    private void EmitBaseConstructorCallIfNeeded()
    {
        if (_method.Name != ".ctor" || _declaringType.BaseType == null ||
            _declaringType.BaseType.FullName == "System.Object") return;
        var baseTypeRef = _declaringType.BaseType.ToTypeReference();
        _script.TypeReferences.AddSafe(baseTypeRef);
        var baseTypeIdx = _script.TypeReferences.IndexOf(baseTypeRef).MinZero();

        _script.AStringTable.AddSafe(".init__");

        var baseInitFuncRef = new FunctionReference
        {
            NameIndex = _script.AStringTable.IndexOf(".init__").MinZero(),
            TypeReferenceIndex = baseTypeIdx
        };
        _script.FunctionReferences.AddSafe(baseInitFuncRef);
        var baseInitCallIdx = _script.FunctionReferences.IndexOf(baseInitFuncRef).MinZero();

        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.ARG,
            Type = MachineType.SAFEPTR,
            Parameters = new ArgArgument { Source = _me, ArgIdx = 0 }
        });
        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.CALL,
            Type = MachineType.VOID,
            Parameters = new CallArgument { Destination = new(0xFFFF, MachineType.VOID), CallIdx = (ushort)baseInitCallIdx }
        });
    }

    private void EmitLoadArgument(Instruction instruction)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldarg_0:
                _registers.Push(_me);
                break;
            case Code.Ldarg_1:
                PushArgByIndex(_method.IsStatic ? 1 : 0);
                break;
            case Code.Ldarg_2:
                PushArgByIndex(_method.IsStatic ? 2 : 1);
                break;
            case Code.Ldarg_3:
                PushArgByIndex(_method.IsStatic ? 3 : 2);
                break;
            case Code.Ldarg:
            case Code.Ldarg_S:
                if (instruction.Operand is ParameterDefinition pDef)
                {
                    var pOffset = GetParamOffset(pDef.Index);
                    _registers.Push(new(pOffset, pDef.ParameterType.GetFishType()));
                }
                else
                {
                    _registers.Push(_me);
                }
                break;
        }
    }

    private void PushArgByIndex(int pIndex)
    {
        if (pIndex < _method.Parameters.Count)
        {
            var pType = _method.Parameters[pIndex].ParameterType.GetFishType();
            var pOffset = GetParamOffset(pIndex);
            _registers.Push(new(pOffset, pType));
        }
        else
        {
            _registers.Push(_me);
        }
    }

    private void EmitLoadArgumentAddress(Instruction instruction)
    {
        if (instruction.Operand is ParameterDefinition pDefAddress)
        {
            var pOffset = (ushort)(_method.IsStatic ? 0 : 4);
            for (var idx = 0; idx < pDefAddress.Index; idx++)
            {
                pOffset += (ushort)Helpers.GetMachineTypeSize(_method.Parameters[idx].ParameterType.GetFishType());
            }
            _registers.Push(new(pOffset, pDefAddress.ParameterType.GetFishType()));
        }
        else
        {
            _registers.Push(_me);
        }
    }

    private void EmitLoadLocal(Instruction instruction)
    {
        // we use a custom handler for local variables, mapping them from
        // cil to fish using registers, not FF variables.
        switch (instruction.OpCode.Code)
        {
            case Code.Ldloc_0:
                _loc1.Type = _method.Body.Variables[0].VariableType.GetFishType();
                _registers.Push(_loc1);
                break;
            case Code.Ldloc_1:
                _loc2.Type = _method.Body.Variables[1].VariableType.GetFishType();
                _registers.Push(_loc2);
                break;
            case Code.Ldloc_2:
                _loc3.Type = _method.Body.Variables[2].VariableType.GetFishType();
                _registers.Push(_loc3);
                break;
            case Code.Ldloc_3:
                _loc4.Type = _method.Body.Variables[3].VariableType.GetFishType();
                _registers.Push(_loc4);
                break;
            case Code.Ldloc:
            case Code.Ldloc_S:
                // >= 4
                if (instruction.Operand is VariableDefinition vDefLoad)
                {
                    _registers.Push(GetLocalReg(vDefLoad.Index));
                }
                break;
        }
    }

    private void EmitLoadLocalAddress(Instruction instruction)
    {
        if (instruction.Operand is VariableDefinition vDefAddr)
        {
            var targetLoc = GetLocalReg(vDefAddr.Index);
            if (vDefAddr.Index < _method.Body.Variables.Count)
                targetLoc.Type = _method.Body.Variables[vDefAddr.Index].VariableType.GetFishType();
            _registers.Push(targetLoc);
        }
        else
        {
            throw new InvalidOperationException("Invalid bytecode from CIL");
        }

    }

    private void EmitStoreLocal(Instruction instruction)
    {
        Register target;
        switch (instruction.OpCode.Code)
        {
            case Code.Stloc_0: target = _loc1; break;
            case Code.Stloc_1: target = _loc2; break;
            case Code.Stloc_2: target = _loc3; break;
            case Code.Stloc_3: target = _loc4; break;
            case Code.Stloc or Code.Stloc_S when instruction.Operand is VariableDefinition vDef:
                target = GetLocalReg(vDef.Index);
                break;
            default:
                return;
        }

        var taken = _registers.Count > 0 ? _registers.Pop() : target;
        target.Type = taken.Type;
        _instructions.Add(new()
        {
            Opcode = InstructionMapper.Mov(taken.Type),
            Parameters = new UnaryArgument
            {
                Destination = target,
                Source = taken
            }
        });
    }

    private void EmitInitObj(Instruction instruction)
    {
        if (_registers.Count > 0)
        {
            var targetReg = _registers.Pop();
            if (instruction.Operand is TypeReference initTypeRef && Helpers.IsVectorType(initTypeRef))
            {
                targetReg.Type = MachineType.V4;
            }
        }

        EmitNop();
    }

    private void EmitLoadIntConstant(Instruction instruction, int cilIdx)
    {
        if (instruction.OpCode.Code is Code.Ldc_I4 or Code.Ldc_I4_S)
        {
            var intVal = instruction.Operand switch
            {
                int i => i,
                sbyte sb => sb,
                byte b => b,
                _ => Convert.ToInt32(instruction.Operand)
            };

            if (IsLongContext(cilIdx))
            {
                _regCount = Helpers.AlignOffset(_regCount, MachineType.S64);
                Register register = new(_regCount, MachineType.S64);
                _registers.Push(register);
                _regCount += 8;
                _instructions.Add(new()
                {
                    Opcode = (byte)InstructionType.LCs64,
                    Parameters = new LoadConstArgument
                    {
                        Destination = register,
                        Index = intVal
                    },
                    Type = MachineType.S64
                });
            }
            else
            {
                Register register = new(_regCount, MachineType.S32);
                _registers.Push(register);
                _regCount += 4;
                _instructions.Add(new()
                {
                    Opcode = (byte)InstructionType.LCi,
                    Parameters = new LoadConstArgument
                    {
                        Destination = register,
                        Index = intVal
                    },
                    Type = MachineType.S32
                });
            }
        }
        else // Ldc_I4_0..8, Ldc_I4_M1 - in cil they are special instructions
        {
            var val = instruction.OpCode.Code switch
            {
                Code.Ldc_I4_0 => 0,
                Code.Ldc_I4_1 => 1,
                Code.Ldc_I4_2 => 2,
                Code.Ldc_I4_3 => 3,
                Code.Ldc_I4_4 => 4,
                Code.Ldc_I4_5 => 5,
                Code.Ldc_I4_6 => 6,
                Code.Ldc_I4_7 => 7,
                Code.Ldc_I4_8 => 8,
                Code.Ldc_I4_M1 => -1,
                _ => 0
            };

            var isBoolContext = false;
            if (instruction.OpCode.Code is Code.Ldc_I4_0 or Code.Ldc_I4_1)
            {
                if (_method.ReturnType.GetFishType() == MachineType.BOOL)
                {
                    isBoolContext = true;
                }
                else if (cilIdx + 1 < _method.Body.Instructions.Count)
                {
                    var nextInst = _method.Body.Instructions[cilIdx + 1];
                    if (nextInst.OpCode.Code is Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3)
                    {
                        var locIdx = nextInst.OpCode.Code switch
                        {
                            Code.Stloc_0 => 0,
                            Code.Stloc_1 => 1,
                            Code.Stloc_2 => 2,
                            Code.Stloc_3 => 3,
                            _ => 0
                        };
                        if (_method.Body.HasVariables && locIdx < _method.Body.Variables.Count &&
                            _method.Body.Variables[locIdx].VariableType.GetFishType() == MachineType.BOOL)
                        {
                            isBoolContext = true;
                        }
                    }
                    else if (nextInst.Operand is VariableDefinition vDef &&
                             vDef.VariableType.GetFishType() == MachineType.BOOL ||
                             nextInst.Operand is ParameterDefinition pDefContext &&
                             pDefContext.ParameterType.GetFishType() == MachineType.BOOL ||
                             nextInst.Operand is FieldReference fDef &&
                             fDef.FieldType.GetFishType() == MachineType.BOOL)
                    {
                        isBoolContext = true;
                    }
                }
            }

            var regType = isBoolContext ? MachineType.BOOL : MachineType.S32;
            var lcOpcode = isBoolContext ? (byte)InstructionType.LCb : (byte)InstructionType.LCi;
            var encodedVal = isBoolContext ? val != 0 ? unchecked((int)0x80000000) : 0 : val;

            Register x = new(_regCount, regType);
            _registers.Push(x);
            _regCount += 4;
            _instructions.Add(new()
            {
                Opcode = lcOpcode,
                Parameters = new LoadConstArgument
                {
                    Destination = x,
                    Index = encodedVal
                },
                Type = regType
            });
        }
    }

    private bool IsLongContext(int cilIdx)
    {
        if (cilIdx >= _method.Body.Instructions.Count - 1) return false;
        var nextInst = _method.Body.Instructions[cilIdx + 1];
        switch (nextInst.OpCode.Code)
        {
            case Code.Conv_I8 or Code.Conv_U8:
                return true;
            case Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3:
            {
                var locIdx = nextInst.OpCode.Code switch
                {
                    Code.Stloc_0 => 0,
                    Code.Stloc_1 => 1,
                    Code.Stloc_2 => 2,
                    Code.Stloc_3 => 3,
                    _ => 0
                };
                return _method.Body.HasVariables && locIdx < _method.Body.Variables.Count &&
                       _method.Body.Variables[locIdx].VariableType.GetFishType() == MachineType.S64;
            }
            default:
            {
                return nextInst.Operand is VariableDefinition vDef && vDef.VariableType.GetFishType() == MachineType.S64 ||
                       nextInst.Operand is ParameterDefinition pDefContext && pDefContext.ParameterType.GetFishType() == MachineType.S64 ||
                       nextInst.Operand is FieldReference fDef && fDef.FieldType.GetFishType() == MachineType.S64;
            }
        }
    }

    private void EmitLoadFloatConstant(Instruction instruction)
    {
        var floatVal = Convert.ToSingle(instruction.Operand);
        Register fReg = new(_regCount, MachineType.F32);
        _registers.Push(fReg);
        _regCount += 4;
        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.LCf,
            Parameters = new LoadConstArgument
            {
                Destination = fReg,
                Index = BitConverter.SingleToInt32Bits(floatVal)
            },
            Type = MachineType.F32
        });
    }

    private void EmitLoadLongConstant(Instruction instruction)
    {
        var longVal = Convert.ToInt64(instruction.Operand);
        _regCount = Helpers.AlignOffset(_regCount, MachineType.S64);
        Register register = new(_regCount, MachineType.S64);
        _registers.Push(register);
        _regCount += 8;
        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.LCs64,
            Parameters = new LoadConstArgument
            {
                Destination = register,
                Index = unchecked((int)longVal)
            },
            Type = MachineType.S64
        });
    }

    private void EmitLoadStringConstant(Instruction instruction)
    {
        Register loaded = new(_regCount, MachineType.OBJECT);
        _registers.Push(loaded);
        _regCount += 4;
        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.LCsw,
            Parameters = new LoadConstArgument
            {
                Destination = loaded,
                Index = _script.WStringTable.FindIndex(e => e == (string)instruction.Operand)
            }
        });
    }

    private void EmitLoadNull()
    {
        Register nullReg = new(_regCount, MachineType.SAFEPTR);
        _registers.Push(nullReg);
        _regCount += 4;
        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.LC_NULLsp,
            Parameters = new LoadNullConstArgument
            {
                Destination = nullReg // very easy :)
            }
        });
    }

    private void EmitBinaryOp(Code code)
    {
        var arg2 = _registers.Pop();
        var arg1 = _registers.Pop();

        switch (code)
        {
            case Code.Add:
            {
                var resType = arg1.Type == MachineType.S64 || arg2.Type == MachineType.S64 ? MachineType.S64 : arg1.Type;
                var opcode = resType switch
                {
                    MachineType.S64 => (byte)InstructionType.ADDs64,
                    MachineType.V4 => (byte)InstructionType.ADDv4,
                    MachineType.F32 => (byte)InstructionType.ADDf,
                    _ => (byte)InstructionType.ADDi
                };
                EmitBinaryInstruction(opcode, resType, arg1, arg2);
                break;
            }
            case Code.Sub:
            {
                var resType = arg1.Type == MachineType.S64 || arg2.Type == MachineType.S64 ? MachineType.S64 : arg1.Type;
                var opcode = resType switch
                {
                    MachineType.S64 => (byte)InstructionType.ADDs64,
                    MachineType.V4 => (byte)InstructionType.SUBv4,
                    MachineType.F32 => (byte)InstructionType.SUBf,
                    _ => (byte)InstructionType.SUBi
                };
                EmitBinaryInstruction(opcode, resType, arg1, arg2);
                break;
            }
            case Code.Mul:
            {
                var resType = arg1.Type == MachineType.S64 || arg2.Type == MachineType.S64 ? MachineType.S64 : arg1.Type;
                var opcode = resType switch
                {
                    MachineType.M44 => (byte)InstructionType.MULm44,
                    MachineType.V4 => (byte)InstructionType.MULSv4,
                    MachineType.F32 => (byte)InstructionType.MULf,
                    _ => (byte)InstructionType.MULi
                };
                EmitBinaryInstruction(opcode, resType, arg1, arg2);
                break;
            }
            case Code.Div:
            {
                var resType = arg1.Type == MachineType.S64 || arg2.Type == MachineType.S64 ? MachineType.S64 : arg1.Type;
                var opcode = resType switch
                {
                    MachineType.V4 => (byte)InstructionType.DIVSv4,
                    MachineType.F32 => (byte)InstructionType.DIVf,
                    _ => (byte)InstructionType.DIVi
                };
                EmitBinaryInstruction(opcode, resType, arg1, arg2);
                break;
            }
            case Code.And:
            case Code.Or:
            case Code.Xor:
            {
                var resType = arg1.Type == MachineType.S64 || arg2.Type == MachineType.S64 ? MachineType.S64 :
                                     arg1.Type == MachineType.BOOL && arg2.Type == MachineType.BOOL ? MachineType.BOOL : MachineType.S32;
                var opcode = (code, resType) switch
                {
                    (Code.And, MachineType.S64) => (byte)InstructionType.BIT_ANDs64,
                    (Code.And, MachineType.BOOL) => (byte)InstructionType.BIT_ANDb,
                    (Code.And, _) => (byte)InstructionType.BIT_ANDi,
                    (Code.Or, MachineType.S64) => (byte)InstructionType.BIT_ORs64,
                    (Code.Or, MachineType.BOOL) => (byte)InstructionType.BIT_ORb,
                    (Code.Or, _) => (byte)InstructionType.BIT_ORi,
                    (Code.Xor, MachineType.S64) => (byte)InstructionType.BIT_XORs64,
                    (Code.Xor, MachineType.BOOL) => (byte)InstructionType.BIT_XORb,
                    (Code.Xor, _) => (byte)InstructionType.BIT_XORi,
                    _ => (byte)InstructionType.NOP
                };
                Register result = new(_regCount, resType);
                _regCount += (ushort)Helpers.GetMachineTypeSize(resType);
                _registers.Push(result);
                _instructions.Add(new()
                {
                    Opcode = opcode,
                    Type = resType,
                    Parameters = new BinaryArgument
                    {
                        Destination = result,
                        Source1 = arg1,
                        Source2 = arg2
                    }
                });
                break;
            }
            case Code.Shl:
            case Code.Shr:
            case Code.Shr_Un:
            {
                Register result = new(_regCount, MachineType.S32);
                _regCount += 4;
                _registers.Push(result);
                _instructions.Add(new()
                {
                    Opcode = code == Code.Shl ? (byte)InstructionType.SLAi : (byte)InstructionType.SRAi,
                    Type = MachineType.S32,
                    Parameters = new BinaryArgument
                    {
                        Destination = result,
                        Source1 = arg1,
                        Source2 = arg2
                    }
                });
                break;
            }
        }
    }

    private void EmitBinaryInstruction(byte opcode, MachineType resType, Register arg1, Register arg2)
    {
        _regCount = Helpers.AlignOffset(_regCount, resType);
        Register result = new(_regCount, resType);
        _regCount += (ushort)Helpers.GetMachineTypeSize(resType);
        _registers.Push(result);
        _instructions.Add(new()
        {
            Opcode = opcode,
            Type = resType,
            Parameters = new BinaryArgument
            {
                Destination = result,
                Source1 = arg1,
                Source2 = arg2
            }
        });
    }

    private void EmitNot()
    {
        var arg1 = _registers.Pop();
        var resType = arg1.Type;
        Register result = new(_regCount, resType);
        _regCount += (ushort)Helpers.GetMachineTypeSize(resType);
        _registers.Push(result);
        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.BIT_NEGi,
            Type = resType,
            Parameters = new UnaryArgument
            {
                Destination = result,
                Source = arg1
            }
        });
    }

    private void EmitComparison(Code code)
    {
        var arg2Cmp = _registers.Pop();
        var arg1Cmp = _registers.Pop();

        switch (code)
        {
            case Code.Ceq:
            {
                var opcode = arg1Cmp.Type == MachineType.S64 || arg2Cmp.Type == MachineType.S64
                    ? (byte)InstructionType.EQs64
                    : (byte)InstructionType.EQi;
                _regCount = Helpers.AlignOffset(_regCount, MachineType.BOOL);
                Register resCmp = new(_regCount, MachineType.BOOL);
                _regCount += 4;
                _registers.Push(resCmp);
                _instructions.Add(new()
                {
                    Opcode = opcode,
                    Parameters = new BinaryArgument
                    {
                        Destination = resCmp,
                        Source1 = arg1Cmp,
                        Source2 = arg2Cmp
                    }
                });
                break;
            }
            case Code.Cgt:
            case Code.Cgt_Un:
            {
                Register resCmp = new(_regCount, MachineType.BOOL);
                _regCount += 4;
                _registers.Push(resCmp);
                _instructions.Add(new()
                {
                    Opcode = (byte)InstructionType.GTi,
                    Parameters = new BinaryArgument
                    {
                        Destination = resCmp,
                        Source1 = arg1Cmp,
                        Source2 = arg2Cmp
                    }
                });
                break;
            }
            case Code.Clt:
            case Code.Clt_Un:
            {
                Register resCmp = new(_regCount, MachineType.BOOL);
                _regCount += 4;
                _registers.Push(resCmp);
                _instructions.Add(new()
                {
                    Opcode = (byte)InstructionType.LTi,
                    Parameters = new BinaryArgument
                    {
                        Destination = resCmp,
                        Source1 = arg1Cmp,
                        Source2 = arg2Cmp
                    }
                });
                break;
            }
        }
    }

    private void EmitConversion(Code code)
    {
        switch (code)
        {
            case Code.Conv_I8:
            case Code.Conv_U8:
            {
                var arg = _registers.Count > 0 ? _registers.Pop() : _loc1;
                if (arg.Type == MachineType.S64)
                {
                    _registers.Push(arg);
                }
                else
                {
                    _regCount = Helpers.AlignOffset(_regCount, MachineType.S64);
                    Register result = new(_regCount, MachineType.S64);
                    _regCount += 8;
                    _registers.Push(result);
                    _instructions.Add(new()
                    {
                        Opcode = (byte)InstructionType.MOVs64,
                        Type = MachineType.S64,
                        Parameters = new UnaryArgument
                        {
                            Destination = result,
                            Source = arg
                        }
                    });
                }
                break;
            }
            case Code.Conv_I4:
            case Code.Conv_U4:
            case Code.Conv_I:
            case Code.Conv_U:
            {
                var arg = _registers.Count > 0 ? _registers.Pop() : _loc1;
                _regCount = Helpers.AlignOffset(_regCount, MachineType.S32);
                Register result = new(_regCount, MachineType.S32);
                _regCount += 4;
                _registers.Push(result);
                _instructions.Add(new()
                {
                    Opcode = (byte)InstructionType.MOVi,
                    Type = MachineType.S32,
                    Parameters = new UnaryArgument
                    {
                        Destination = result,
                        Source = arg
                    }
                });
                break;
            }
            case Code.Conv_R4:
            case Code.Conv_R8:
            {
                var arg = _registers.Count > 0 ? _registers.Pop() : _loc1;
                if (arg.Type == MachineType.F32)
                {
                    _registers.Push(arg);
                }
                else
                {
                    _regCount = Helpers.AlignOffset(_regCount, MachineType.F32);
                    Register result = new(_regCount, MachineType.F32);
                    _regCount += 4;
                    _registers.Push(result);
                    _instructions.Add(new()
                    {
                        Opcode = (byte)InstructionType.FLOATi,
                        Type = MachineType.F32,
                        Parameters = new UnaryArgument
                        {
                            Destination = result,
                            Source = arg
                        }
                    });
                }
                break;
            }
        }
    }

    private void EmitFieldLoad(Instruction instruction)
    {
        if (instruction.Operand is not FieldReference fRefLoad) return;

        var instanceReg = _registers.Count > 0 ? _registers.Pop() : _me;
        if (Helpers.IsVectorType(fRefLoad.DeclaringType))
        {
            var fName = fRefLoad.Name.ToLower();
            var getOpcode = fName switch
            {
                "x" => (byte)InstructionType.GET_V4_X,
                "y" => (byte)InstructionType.GET_V4_Y,
                "z" => (byte)InstructionType.GET_V4_Z,
                "w" => (byte)InstructionType.GET_V4_W,
                _ => throw new InvalidOperationException($"Unknown vector member '{fRefLoad.Name}'")
            };
            Register destReg = new(_regCount, MachineType.F32);
            _regCount += 4;
            _registers.Push(destReg);
            _instructions.Add(new()
            {
                Opcode = getOpcode,
                Type = MachineType.F32,
                Parameters = new GetBuiltinMemberArgument
                {
                    Destination = destReg,
                    Base = instanceReg
                }
            });
        }
        else if (Helpers.IsMatrixType(fRefLoad.DeclaringType))
        {
            var (getOpcode, resType) = fRefLoad.Name.ToLower() switch
            {
                "m00" or "xx" => ((byte)InstructionType.GET_M44_XX, MachineType.F32),
                "m01" or "xy" => ((byte)InstructionType.GET_M44_XY, MachineType.F32),
                "m02" or "xz" => ((byte)InstructionType.GET_M44_XZ, MachineType.F32),
                "m03" or "xw" => ((byte)InstructionType.GET_M44_XW, MachineType.F32),
                "m10" or "yx" => ((byte)InstructionType.GET_M44_YX, MachineType.F32),
                "m11" or "yy" => ((byte)InstructionType.GET_M44_YY, MachineType.F32),
                "m12" or "yz" => ((byte)InstructionType.GET_M44_YZ, MachineType.F32),
                "m13" or "yw" => ((byte)InstructionType.GET_M44_YW, MachineType.F32),
                "m20" or "zx" => ((byte)InstructionType.GET_M44_ZX, MachineType.F32),
                "m21" or "zy" => ((byte)InstructionType.GET_M44_ZY, MachineType.F32),
                "m22" or "zz" => ((byte)InstructionType.GET_M44_ZZ, MachineType.F32),
                "m23" or "zw" => ((byte)InstructionType.GET_M44_ZW, MachineType.F32),
                "m30" or "wx" => ((byte)InstructionType.GET_M44_WX, MachineType.F32),
                "m31" or "wy" => ((byte)InstructionType.GET_M44_WY, MachineType.F32),
                "m32" or "wz" => ((byte)InstructionType.GET_M44_WZ, MachineType.F32),
                "m33" or "ww" => ((byte)InstructionType.GET_M44_WW, MachineType.F32),
                "rx" or "rx_" or "row0" or "r0" => ((byte)InstructionType.GET_M44_rX, MachineType.V4),
                "ry" or "ry_" or "row1" or "r1" => ((byte)InstructionType.GET_M44_rY, MachineType.V4),
                "rz" or "rz_" or "row2" or "r2" => ((byte)InstructionType.GET_M44_rZ, MachineType.V4),
                "rw" or "rw_" or "row3" or "r3" => ((byte)InstructionType.GET_M44_rW, MachineType.V4),
                "cx" or "cx_" or "col0" or "c0" => ((byte)InstructionType.GET_M44_cX, MachineType.V4),
                "cy" or "cy_" or "col1" or "c1" => ((byte)InstructionType.GET_M44_cY, MachineType.V4),
                "cz" or "cz_" or "col2" or "c2" => ((byte)InstructionType.GET_M44_cZ, MachineType.V4),
                "cw" or "cw_" or "col3" or "c3" => ((byte)InstructionType.GET_M44_cW, MachineType.V4),
                _ => throw new InvalidOperationException($"Unknown matrix member '{fRefLoad.Name}'")
            };
            if (resType == MachineType.V4)
            {
                _regCount = (ushort)((_regCount + 0xF) & ~0xF);
            }
            Register destReg = new(_regCount, resType);
            _regCount += (ushort)Helpers.GetMachineTypeSize(resType);
            _registers.Push(destReg);
            _instructions.Add(new()
            {
                Opcode = getOpcode,
                Type = resType,
                Parameters = new GetBuiltinMemberArgument
                {
                    Destination = destReg,
                    Base = instanceReg
                }
            });
        }
        else
        {
            var fieldRef = fRefLoad.ToFieldReference();
            _script.FieldReferences.AddSafe(fieldRef);
            var fieldIdx = _script.FieldReferences.IndexOf(fieldRef).MinZero();
            var fType = fRefLoad.FieldType.GetFishType();
            Register destReg = new(_regCount, fType);
            _regCount += (ushort)Helpers.GetMachineTypeSize(fType);
            _registers.Push(destReg);

            var getOpcode = instanceReg.Type switch
            {
                MachineType.REFPTR => (byte)InstructionType.GET_RP_MEMBER,
                _ => (byte)InstructionType.GET_SP_MEMBER
            };

            _instructions.Add(new()
            {
                Opcode = getOpcode,
                Type = fType,
                Parameters = new GetMemberArgument
                {
                    Destination = destReg,
                    Base = instanceReg,
                    FieldIdx = (ushort)fieldIdx
                }
            });
        }
    }

    private void EmitFieldStore(Instruction instruction)
    {
        if (instruction.Operand is not FieldReference fRefStore) return;

        var valReg = _registers.Count > 0 ? _registers.Pop() : _loc1;
        var instanceReg = _registers.Count > 0 ? _registers.Pop() : _me;
        if (Helpers.IsVectorType(fRefStore.DeclaringType)) // handle write in vector
        {
            var setOpcode = fRefStore.Name.ToLower() switch
            {
                "x" => (byte)InstructionType.SET_V4_X,
                "y" => (byte)InstructionType.SET_V4_Y,
                "z" => (byte)InstructionType.SET_V4_Z,
                "w" => (byte)InstructionType.SET_V4_W,
                _ => throw new InvalidOperationException($"Unknown vector member '{fRefStore.Name}'")
            };
            _instructions.Add(new()
            {
                Opcode = setOpcode,
                Type = MachineType.F32,
                Parameters = new SetBuiltinMemberArgument
                {
                    Source = valReg,
                    Base = instanceReg
                }
            });
        }
        else if (Helpers.IsMatrixType(fRefStore.DeclaringType)) // handle write in matrix
        {
            var fName = fRefStore.Name.ToLower();
            var (setOpcode, valType) = fName switch
            {
                "m00" or "xx" => ((byte)InstructionType.SET_M44_XX, MachineType.F32),
                "m01" or "xy" => ((byte)InstructionType.SET_M44_XY, MachineType.F32),
                "m02" or "xz" => ((byte)InstructionType.SET_M44_XZ, MachineType.F32),
                "m03" or "xw" => ((byte)InstructionType.SET_M44_XW, MachineType.F32),
                "m10" or "yx" => ((byte)InstructionType.SET_M44_YX, MachineType.F32),
                "m11" or "yy" => ((byte)InstructionType.SET_M44_YY, MachineType.F32),
                "m12" or "yz" => ((byte)InstructionType.SET_M44_YZ, MachineType.F32),
                "m13" or "yw" => ((byte)InstructionType.SET_M44_YW, MachineType.F32),
                "m20" or "zx" => ((byte)InstructionType.SET_M44_ZX, MachineType.F32),
                "m21" or "zy" => ((byte)InstructionType.SET_M44_ZY, MachineType.F32),
                "m22" or "zz" => ((byte)InstructionType.SET_M44_ZZ, MachineType.F32),
                "m23" or "zw" => ((byte)InstructionType.SET_M44_ZW, MachineType.F32),
                "m30" or "wx" => ((byte)InstructionType.SET_M44_WX, MachineType.F32),
                "m31" or "wy" => ((byte)InstructionType.SET_M44_WY, MachineType.F32),
                "m32" or "wz" => ((byte)InstructionType.SET_M44_WZ, MachineType.F32),
                "m33" or "ww" => ((byte)InstructionType.SET_M44_WW, MachineType.F32),
                "rx" or "rx_" or "row0" or "r0" => ((byte)InstructionType.SET_M44_rX, MachineType.V4),
                "ry" or "ry_" or "row1" or "r1" => ((byte)InstructionType.SET_M44_rY, MachineType.V4),
                "rz" or "rz_" or "row2" or "r2" => ((byte)InstructionType.SET_M44_rZ, MachineType.V4),
                "rw" or "rw_" or "row3" or "r3" => ((byte)InstructionType.SET_M44_rW, MachineType.V4),
                "cx" or "cx_" or "col0" or "c0" => ((byte)InstructionType.SET_M44_cX, MachineType.V4),
                "cy" or "cy_" or "col1" or "c1" => ((byte)InstructionType.SET_M44_cY, MachineType.V4),
                "cz" or "cz_" or "col2" or "c2" => ((byte)InstructionType.SET_M44_cZ, MachineType.V4),
                "cw" or "cw_" or "col3" or "c3" => ((byte)InstructionType.SET_M44_cW, MachineType.V4),
                _ => throw new InvalidOperationException($"Unknown matrix member '{fRefStore.Name}'")
            };
            _instructions.Add(new()
            {
                Opcode = setOpcode,
                Type = valType,
                Parameters = new SetBuiltinMemberArgument
                {
                    Source = valReg,
                    Base = instanceReg
                }
            });
        }
        else // handle real field store: outside special types
        {
            var fieldRef = fRefStore.ToFieldReference();
            _script.FieldReferences.AddSafe(fieldRef);
            var fieldIdx = _script.FieldReferences.IndexOf(fieldRef).MinZero();

            var setOpcode = instanceReg.Type switch
            {
                MachineType.REFPTR => (byte)InstructionType.SET_RP_MEMBER,
                _ => (byte)InstructionType.SET_SP_MEMBER
            };

            _instructions.Add(new()
            {
                Opcode = setOpcode,
                Type = valReg.Type,
                Parameters = new SetMemberArgument
                {
                    Source = valReg,
                    Base = instanceReg,
                    FieldIdx = (ushort)fieldIdx
                }
            });
        }
    }

    private void EmitStaticFieldLoad(Instruction instruction)
    {
        if (instruction.Operand is not FieldReference fRefLdsfld) return;

        var fieldRef = fRefLdsfld.ToFieldReference();
        _script.FieldReferences.AddSafe(fieldRef);
        var fieldIdx = _script.FieldReferences.IndexOf(fieldRef).MinZero();
        var fType = fRefLdsfld.FieldType.GetFishType();
        Register destReg = new(_regCount, fType);
        _regCount += (ushort)Helpers.GetMachineTypeSize(fType);
        _registers.Push(destReg);

        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.GET_SP_MEMBER,
            Type = fType,
            Parameters = new GetMemberArgument
            {
                Destination = destReg,
                Base = _me,
                FieldIdx = (ushort)fieldIdx
            }
        });
    }

    private void EmitStaticFieldStore(Instruction instruction)
    {
        if (instruction.Operand is not FieldReference fRefStsfld) return;

        var valReg = _registers.Count > 0 ? _registers.Pop() : _loc1;
        var fieldRef = fRefStsfld.ToFieldReference();
        _script.FieldReferences.AddSafe(fieldRef);
        var fieldIdx = _script.FieldReferences.IndexOf(fieldRef).MinZero();

        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.SET_SP_MEMBER,
            Type = valReg.Type,
            Parameters = new SetMemberArgument
            {
                Source = valReg,
                Base = _me,
                FieldIdx = (ushort)fieldIdx
            }
        });
    }

    private void EmitBranch(Instruction instruction)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Br:
            case Code.Br_S:
            {
                var branchVmIdx = _instructions.Count;
                _branchFixups.Add((branchVmIdx, (Instruction)instruction.Operand));
                _instructions.Add(new()
                {
                    Opcode = (byte)InstructionType.B,
                    Parameters = new BranchArgument
                    {
                        Source = new(0xFFFF, MachineType.VOID),
                        Offset = 0
                    }
                });
                break;
            }
            case Code.Brfalse:
            case Code.Brfalse_S:
            case Code.Brtrue:
            case Code.Brtrue_S:
            {
                var condReg = _registers.Count > 0 ? _registers.Pop() : _loc1;
                var branchVmIdx = _instructions.Count;
                _branchFixups.Add((branchVmIdx, (Instruction)instruction.Operand));
                _instructions.Add(new()
                {
                    Opcode = instruction.OpCode.Code is Code.Brtrue or Code.Brtrue_S
                        ? (byte)InstructionType.BNEZ
                        : (byte)InstructionType.BEZ,
                    Parameters = new BranchArgument
                    {
                        Source = condReg,
                        Offset = 0
                    }
                });
                break;
            }
        }
    }

    private void EmitCall(Instruction instruction)
    {
        var methodRef = (MethodReference)instruction.Operand;

        if (methodRef.Name == ".ctor")
        {
            if (Helpers.IsVectorType(methodRef.DeclaringType))
            {
                var paramCount = methodRef.Parameters.Count;
                var paramRegs = new Register[paramCount];
                for (var i = paramCount - 1; i >= 0; i--)
                {
                    paramRegs[i] = _registers.Count > 0 ? _registers.Pop() : new(0, MachineType.F32);
                }

                Register targetReg;
                if (instruction.OpCode.Code != Code.Newobj && methodRef.HasThis && _registers.Count > 0)
                {
                    targetReg = _registers.Pop();
                }
                else
                {
                    _regCount = (ushort)((_regCount + 0xF) & ~0xF);
                    targetReg = new Register(_regCount, MachineType.V4);
                    _regCount += 16;
                    _registers.Push(targetReg);
                }

                targetReg.Type = MachineType.V4;

                for (var i = 0; i < paramCount; i++)
                {
                    var valReg = paramRegs[i];
                    if (valReg.Type != MachineType.F32)
                    {
                        _regCount = Helpers.AlignOffset(_regCount, MachineType.F32);
                        Register fValReg = new(_regCount, MachineType.F32);
                        _regCount += 4;
                        _instructions.Add(new()
                        {
                            Opcode = (byte)InstructionType.FLOATi,
                            Type = MachineType.F32,
                            Parameters = new UnaryArgument
                            {
                                Destination = fValReg,
                                Source = valReg
                            }
                        });
                        valReg = fValReg;
                    }

                    var setOpcode = i switch
                    {
                        0 => (byte)InstructionType.SET_V4_X,
                        1 => (byte)InstructionType.SET_V4_Y,
                        2 => (byte)InstructionType.SET_V4_Z,
                        _ => (byte)InstructionType.SET_V4_W
                    };

                    _instructions.Add(new()
                    {
                        Opcode = setOpcode,
                        Type = MachineType.VOID,
                        Parameters = new SetBuiltinMemberArgument
                        {
                            Base = targetReg,
                            Source = valReg
                        }
                    });
                }

                return;
            } // handle vector creation via constructor

            for (var i = 0; i < methodRef.Parameters.Count; i++)
                if (_registers.Count > 0)
                    _registers.Pop();
            if (methodRef.HasThis && _registers.Count > 0) _registers.Pop();
            return;
        }

        var normalParamCount = methodRef.Parameters.Count;
        var normalParamRegs = new Register[normalParamCount];
        for (var i = normalParamCount - 1; i >= 0; i--)
        {
            normalParamRegs[i] = _registers.Count > 0 ? _registers.Pop() : new(0, methodRef.Parameters[i].ParameterType.GetFishType());
        }

        Register? instanceReg = null;
        if (methodRef.HasThis)
        {
            instanceReg = _registers.Count > 0 ? _registers.Pop() : _me;
        }

        ushort targetArgOffset = 0;
        if (instanceReg != null)
        {
            var instanceType = instanceReg.Type != MachineType.VOID ? instanceReg.Type : MachineType.SAFEPTR;
            _instructions.Add(new()
            {
                Opcode = (byte)InstructionType.ARG,
                Type = instanceType,
                Parameters = new ArgArgument
                {
                    Source = instanceReg,
                    ArgIdx = 0
                }
            });
            targetArgOffset = 4;
        }

        for (var i = 0; i < normalParamCount; i++)
        {
            var argReg = normalParamRegs[i];
            var paramType = methodRef.Parameters[i].ParameterType.GetFishType();
            targetArgOffset = Helpers.AlignArgOffset(targetArgOffset, paramType);

            _instructions.Add(new()
            {
                Opcode = (byte)InstructionType.ARG,
                Type = paramType,
                Parameters = new ArgArgument
                {
                    Source = argReg,
                    ArgIdx = targetArgOffset
                }
            });

            targetArgOffset += (ushort)Helpers.GetMachineTypeSize(paramType);
        }

        var funcRef = methodRef.ToFunctionReference();
        _script.FunctionReferences.AddSafe(funcRef);
        var callIdx = _script.FunctionReferences.IndexOf(funcRef).MinZero();

        var returnMachineType = methodRef.ReturnType.GetFishType();
        Register returnReg;
        if (returnMachineType != MachineType.VOID)
        {
            _regCount = Helpers.AlignOffset(_regCount, returnMachineType);
            returnReg = new(_regCount, returnMachineType);
            _regCount += (ushort)Helpers.GetMachineTypeSize(returnMachineType);
            _registers.Push(returnReg);
        }
        else
        {
            returnReg = new(0xFFFF, MachineType.VOID);
        }

        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.CALL,
            Type = returnMachineType,
            Parameters = new CallArgument
            {
                Destination = returnReg,
                CallIdx = (ushort)callIdx
            }
        });
    }

    private void EmitReturn()
    {
        var returnType = _method.ReturnType.GetFishType();
        var retReg = returnType != MachineType.VOID && _registers.Count > 0
            ? _registers.Pop()
            : new(0, MachineType.VOID); // bunch of zeroes: as done in real scripts

        _instructions.Add(new()
        {
            Opcode = (byte)InstructionType.RET,
            Type = returnType,
            Parameters = new ReturnArgument
            {
                Source = retReg
            }
        });
    }
}
