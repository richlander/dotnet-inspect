using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

internal readonly record struct MethodDeclarationSafety(
    bool HasUnsafeApiMember,
    bool HasUnsafeSignature);

internal readonly record struct MethodLocalSafety(
    bool HasUnsafeLocals);

internal static class MethodSafetyAnalysis
{
    internal static MethodDeclarationSafety InspectDeclaration(
        MethodIdentity method,
        ImmutableArray<UnsafeEvidence>.Builder evidence)
    {
        bool hasUnsafeApiMember = IsUnsafeApi(method.DeclaringType);
        if (hasUnsafeApiMember)
        {
            evidence.Add(new UnsafeEvidence(
                method,
                "Unsafe API member",
                FormatMethod(method),
                "api",
                ILOffset: null,
                OperandToken: null));
        }

        var unsafeTypes = method.ParameterTypes
            .Append(method.ReturnType)
            .Where(ContainsUnsafeType)
            .Select(type => type.ToQualifiedDisplayString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        bool hasUnsafeSignature = unsafeTypes.Count > 0;
        if (hasUnsafeSignature)
        {
            evidence.Add(new UnsafeEvidence(
                method,
                "Unsafe signature",
                string.Join(", ", unsafeTypes),
                "signature",
                ILOffset: null,
                OperandToken: null));
        }

        return new MethodDeclarationSafety(
            hasUnsafeApiMember,
            hasUnsafeSignature);
    }

    internal static MethodLocalSafety InspectLocals(
        MethodBodyAnalysisContext context,
        ImmutableArray<UnsafeEvidence>.Builder evidence)
    {
        bool found = false;
        for (int index = 0; index < context.LocalTypes.Length; index++)
        {
            var local = context.LocalTypes[index];
            if (local.Kind == TypeRefKind.Pinned)
            {
                evidence.Add(new UnsafeEvidence(
                    context.Method,
                    "Pinned local",
                    $"V_{index}: {local.ToQualifiedDisplayString()}",
                    "local",
                    ILOffset: null,
                    OperandToken: null));
                found = true;
                continue;
            }
            if (ContainsUnsafeType(local))
            {
                evidence.Add(new UnsafeEvidence(
                    context.Method,
                    "Pointer local",
                    $"V_{index}: {local.ToQualifiedDisplayString()}",
                    "local",
                    ILOffset: null,
                    OperandToken: null));
                found = true;
            }
        }

        return new MethodLocalSafety(found);
    }

    internal static ImmutableArray<UnsafetyOccurrence> CollectOccurrences(
        MethodBodyAnalysisContext context,
        Func<int, string?> calliReturnDetail)
    {
        var occurrences = ImmutableArray.CreateBuilder<UnsafetyOccurrence>();
        var localValues = new Dictionary<int, StackValueKind>();
        var stack = new List<StackValueKind>();
        foreach (var instruction in context.Instructions.Instructions)
        {
            int offset = instruction.Offset;
            var operation = instruction.OpCode;
            try
            {
                switch (operation)
                {
                    case ILOpCode.Calli:
                    {
                        int token = checked((int)instruction.OperandValue);
                        occurrences.Add(new UnsafetyOccurrence(
                            context.Method,
                            offset,
                            UnsafetyKind.CallIndirect,
                            calliReturnDetail(token)));
                        stack.Clear();
                        break;
                    }
                    case ILOpCode.Localloc:
                        stack.Add(StackValueKind.Pointer);
                        occurrences.Add(new UnsafetyOccurrence(
                            context.Method,
                            offset,
                            UnsafetyKind.StackAlloc,
                            "byte*"));
                        break;
                    case ILOpCode.Ldind_i1:
                    case ILOpCode.Ldind_u1:
                    case ILOpCode.Ldind_i2:
                    case ILOpCode.Ldind_u2:
                    case ILOpCode.Ldind_i4:
                    case ILOpCode.Ldind_u4:
                    case ILOpCode.Ldind_i8:
                    case ILOpCode.Ldind_i:
                    case ILOpCode.Ldind_r4:
                    case ILOpCode.Ldind_r8:
                    case ILOpCode.Ldind_ref:
                        if (Pop(stack, out var address)
                            && address == StackValueKind.Pointer)
                        {
                            occurrences.Add(new UnsafetyOccurrence(
                                context.Method,
                                offset,
                                UnsafetyKind.Deref,
                                IndirectTypeDetail(operation)));
                        }
                        stack.Add(StackValueKind.Other);
                        break;
                    case ILOpCode.Stind_i1:
                    case ILOpCode.Stind_i2:
                    case ILOpCode.Stind_i4:
                    case ILOpCode.Stind_i8:
                    case ILOpCode.Stind_i:
                    case ILOpCode.Stind_r4:
                    case ILOpCode.Stind_r8:
                    case ILOpCode.Stind_ref:
                        if (Pop(stack, out _)
                            && Pop(stack, out address)
                            && address == StackValueKind.Pointer)
                        {
                            occurrences.Add(new UnsafetyOccurrence(
                                context.Method,
                                offset,
                                UnsafetyKind.Deref,
                                IndirectTypeDetail(operation)));
                        }
                        break;
                    case ILOpCode.Dup:
                        stack.Add(
                            stack.Count == 0
                                ? StackValueKind.Unknown
                                : stack[^1]);
                        break;
                    case ILOpCode.Conv_u:
                    case ILOpCode.Conv_i:
                    case ILOpCode.Nop:
                        break;
                    default:
                        if (TryReadAddressLoad(
                                instruction,
                                out var addressKind))
                        {
                            stack.Add(addressKind);
                            break;
                        }
                        if (MethodInstructionFacts.TryReadLocalSlot(
                                instruction,
                                out var access))
                        {
                            if (access.IsStore)
                            {
                                Pop(stack, out var value);
                                if (!access.IsArgument)
                                    localValues[access.Slot] = value;
                            }
                            else
                            {
                                stack.Add(SlotKind(
                                    access.IsArgument,
                                    access.Slot,
                                    context.Method,
                                    context.LocalTypes,
                                    localValues));
                            }
                            break;
                        }
                        if (PushConstant(instruction))
                        {
                            stack.Add(StackValueKind.Other);
                            break;
                        }
                        stack.Clear();
                        break;
                }
            }
            catch (Exception ex)
                when (ex is
                    BadImageFormatException
                    or InvalidOperationException
                    or ArgumentException
                    or OverflowException
                    or IndexOutOfRangeException)
            {
                break;
            }
        }
        return occurrences.ToImmutable();
    }

    internal static UnsafeEvidence? InspectCall(
        MethodIdentity caller,
        MemberRef callee,
        CallKind kind,
        int offset,
        int token)
        => IsUnsafeCall(callee)
            ? new UnsafeEvidence(
                caller,
                "Unsafe call",
                FormatMember(callee),
                FormatCallKind(kind),
                offset,
                token)
            : null;

    internal static UnsafeEvidence CallIndirect(
        MethodIdentity caller,
        int offset,
        int token)
        => new(
            caller,
            "Unsafe operation",
            "calli",
            "calli",
            offset,
            token);

    internal static UnsafeEvidence? InspectOperation(
        MethodIdentity caller,
        ILOpCode operation,
        int offset,
        bool includeIndirectOperations)
        => UnsafeOpcodeName(operation, includeIndirectOperations)
            is { } operationName
            ? new UnsafeEvidence(
                caller,
                "Unsafe operation",
                operationName,
                "opcode",
                offset,
                OperandToken: null)
            : null;

    enum StackValueKind
    {
        Unknown,
        Other,
        Pointer,
        ManagedRef,
    }

    static bool Pop(
        List<StackValueKind> stack,
        out StackValueKind value)
    {
        if (stack.Count == 0)
        {
            value = StackValueKind.Unknown;
            return false;
        }
        value = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return true;
    }

    static StackValueKind SlotKind(
        bool isArgument,
        int slot,
        MethodIdentity caller,
        ImmutableArray<TypeRef> locals,
        IReadOnlyDictionary<int, StackValueKind> localValues)
    {
        if (isArgument)
        {
            if (!caller.IsStatic)
            {
                if (slot == 0)
                    return StackValueKind.Other;
                slot--;
            }
            return slot >= 0 && slot < caller.ParameterTypes.Length
                ? TypeStackKind(caller.ParameterTypes[slot])
                : StackValueKind.Unknown;
        }
        if (localValues.TryGetValue(slot, out var value)
            && value == StackValueKind.Pointer)
        {
            return value;
        }
        return slot >= 0 && slot < locals.Length
            ? TypeStackKind(locals[slot])
            : StackValueKind.Unknown;
    }

    static StackValueKind TypeStackKind(TypeRef type)
        => type.Kind switch
        {
            TypeRefKind.Pointer => StackValueKind.Pointer,
            TypeRefKind.ByRef => StackValueKind.ManagedRef,
            TypeRefKind.Pinned
                when type.ElementType?.Kind == TypeRefKind.Pointer
                => StackValueKind.Pointer,
            _ => StackValueKind.Other,
        };

    static bool TryReadAddressLoad(
        DecodedInstruction instruction,
        out StackValueKind kind)
    {
        kind = StackValueKind.ManagedRef;
        switch (instruction.OpCode)
        {
            case ILOpCode.Ldloca_s:
            case ILOpCode.Ldarga_s:
            case ILOpCode.Ldloca:
            case ILOpCode.Ldarga:
                return true;
            default:
                kind = StackValueKind.Unknown;
                return false;
        }
    }

    static bool PushConstant(DecodedInstruction instruction)
        => instruction.OpCode is
            ILOpCode.Ldc_i4_m1
            or ILOpCode.Ldc_i4_0
            or ILOpCode.Ldc_i4_1
            or ILOpCode.Ldc_i4_2
            or ILOpCode.Ldc_i4_3
            or ILOpCode.Ldc_i4_4
            or ILOpCode.Ldc_i4_5
            or ILOpCode.Ldc_i4_6
            or ILOpCode.Ldc_i4_7
            or ILOpCode.Ldc_i4_8
            or ILOpCode.Ldnull
            or ILOpCode.Ldc_i4_s
            or ILOpCode.Ldc_i4
            or ILOpCode.Ldc_r4
            or ILOpCode.Ldc_i8
            or ILOpCode.Ldc_r8;

    static string? IndirectTypeDetail(ILOpCode operation)
        => operation switch
        {
            ILOpCode.Ldind_i1 or ILOpCode.Stind_i1 => "sbyte",
            ILOpCode.Ldind_u1 => "byte",
            ILOpCode.Ldind_i2 or ILOpCode.Stind_i2 => "short",
            ILOpCode.Ldind_u2 => "ushort",
            ILOpCode.Ldind_i4 or ILOpCode.Stind_i4 => "int",
            ILOpCode.Ldind_u4 => "uint",
            ILOpCode.Ldind_i8 or ILOpCode.Stind_i8 => "long",
            ILOpCode.Ldind_i or ILOpCode.Stind_i => "nint",
            ILOpCode.Ldind_r4 or ILOpCode.Stind_r4 => "float",
            ILOpCode.Ldind_r8 or ILOpCode.Stind_r8 => "double",
            ILOpCode.Ldind_ref or ILOpCode.Stind_ref => "object",
            _ => null,
        };

    static bool IsUnsafeCall(MemberRef member)
        => IsUnsafeApi(member.DeclaringType)
            || member.ParameterTypes
                .Append(member.ReturnType)
                .Any(ContainsUnsafeType);

    static bool IsUnsafeApi(TypeRef type)
        => FrameworkIdentity.IsCoreLibraryType(
                type,
                "System.Runtime.CompilerServices",
                "Unsafe")
            || FrameworkIdentity.IsKnownFrameworkType(
                type,
                "System.Runtime.CompilerServices.Unsafe",
                "System.Runtime.CompilerServices",
                "Unsafe");

    static bool ContainsUnsafeType(TypeRef type)
    {
        if (type.Kind is TypeRefKind.Pointer or TypeRefKind.Pinned)
            return true;
        if (type.Kind == TypeRefKind.Unsupported
            && type.UnsupportedReason.Contains(
                "function pointer",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (type.ElementType is not null
            && ContainsUnsafeType(type.ElementType))
        {
            return true;
        }
        return type.TypeArguments.Any(ContainsUnsafeType);
    }

    static string FormatMember(MemberRef member)
    {
        if (member.Kind == MemberKind.Unsupported)
            return member.DeclaringType.ToDisplayString();

        string name = member.Name;
        if (member.TypeArguments.Length > 0)
        {
            name +=
                $"<{string.Join(", ", member.TypeArguments.Select(
                    type => type.ToQualifiedDisplayString()))}>";
        }
        return
            $"{member.DeclaringType.ToQualifiedDisplayString()}.{name}(" +
            $"{string.Join(", ", member.ParameterTypes.Select(
                parameter => parameter.ToQualifiedDisplayString()))})";
    }

    static string FormatMethod(MethodIdentity method)
        => $"{method.DeclaringType.ToQualifiedDisplayString()}." +
            $"{method.Name}(" +
            $"{string.Join(", ", method.ParameterTypes.Select(
                parameter => parameter.ToQualifiedDisplayString()))})";

    static string FormatCallKind(CallKind kind)
        => kind switch
        {
            CallKind.Call => "call",
            CallKind.CallVirtual => "callvirt",
            CallKind.NewObject => "newobj",
            CallKind.LoadFunction => "ldftn",
            CallKind.LoadVirtualFunction => "ldvirtftn",
            _ => "calli",
        };

    static string? UnsafeOpcodeName(
        ILOpCode operation,
        bool includeIndirectOperations)
        => operation switch
        {
            ILOpCode.Localloc => "localloc",
            ILOpCode.Cpblk => "cpblk",
            ILOpCode.Initblk => "initblk",
            ILOpCode.Ldind_i1 when includeIndirectOperations => "ldind.i1",
            ILOpCode.Ldind_u1 when includeIndirectOperations => "ldind.u1",
            ILOpCode.Ldind_i2 when includeIndirectOperations => "ldind.i2",
            ILOpCode.Ldind_u2 when includeIndirectOperations => "ldind.u2",
            ILOpCode.Ldind_i4 when includeIndirectOperations => "ldind.i4",
            ILOpCode.Ldind_u4 when includeIndirectOperations => "ldind.u4",
            ILOpCode.Ldind_i8 when includeIndirectOperations => "ldind.i8",
            ILOpCode.Ldind_i when includeIndirectOperations => "ldind.i",
            ILOpCode.Ldind_r4 when includeIndirectOperations => "ldind.r4",
            ILOpCode.Ldind_r8 when includeIndirectOperations => "ldind.r8",
            ILOpCode.Ldind_ref when includeIndirectOperations
                => "ldind.ref",
            ILOpCode.Stind_ref when includeIndirectOperations
                => "stind.ref",
            ILOpCode.Stind_i1 when includeIndirectOperations => "stind.i1",
            ILOpCode.Stind_i2 when includeIndirectOperations => "stind.i2",
            ILOpCode.Stind_i4 when includeIndirectOperations => "stind.i4",
            ILOpCode.Stind_i8 when includeIndirectOperations => "stind.i8",
            ILOpCode.Stind_i when includeIndirectOperations => "stind.i",
            ILOpCode.Stind_r4 when includeIndirectOperations => "stind.r4",
            ILOpCode.Stind_r8 when includeIndirectOperations => "stind.r8",
            _ => null,
        };
}
