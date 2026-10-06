using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

static class ArrayPoolUseClassifier
{
    internal static UseClassification ClassifyUse(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        int loadOffset,
        int slot,
        TypeRef? valueType = null,
        bool isArgument = false)
    {
        if (!TryFindInstruction(instructions, loadOffset, out int index, out var load)
            || !IsLoadSlotOrAddress(load, slot, isArgument))
            return UseClassification.OwnershipTransfer("Rented array use could not be classified.");

        int extra = 0;
        for (int i = index + 1; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            var opcode = instruction.OpCode;
            if (IsSimpleArgumentPush(opcode))
            {
                extra++;
                continue;
            }
            if (opcode == ILOpCode.Ldlen)
                return extra == 0 ? UseClassification.LocalUse : UseClassification.OwnershipTransfer("Rented array length use is ambiguous.");
            if (IsElementRead(opcode))
                return extra == 1 ? UseClassification.LocalUse : UseClassification.OwnershipTransfer("Rented array element read is ambiguous.");
            if (IsElementStore(opcode))
                return extra == 2 ? UseClassification.LocalUse : UseClassification.OwnershipTransfer("Rented array element store is ambiguous.");
            if (IsFieldStore(opcode))
                return UseClassification.StoreAt(instruction.Offset);
            if (IsLocalStore(opcode))
                return UseClassification.AliasOrField("Rented array is stored into another local.");
            if (opcode == ILOpCode.Ret)
            {
                return extra == 0
                    ? UseClassification.ReturnAt(instruction.Offset)
                    : UseClassification.OwnershipTransfer(
                        "Rented array return shape is ambiguous.");
            }
            if (calls.TryGetValue(instruction.Offset, out var callee))
            {
                int parameterIndex =
                    callee.ParameterTypes.Length - extra - 1;
                if (IsArrayPoolReturn(callee)
                    && parameterIndex == 0)
                {
                    return UseClassification.ReleaseAt(
                        instruction.Offset);
                }
                int consumedArguments =
                    callee.ParameterTypes.Length
                    + (instruction.OpCode != ILOpCode.Newobj && callee.HasThis
                        ? 1
                        : 0);
                if (consumedArguments <= extra)
                {
                    extra -= consumedArguments;
                    if (instruction.OpCode == ILOpCode.Newobj
                        || !FrameworkIdentity.IsCoreLibraryType(
                            callee.ReturnType,
                            "System",
                            "Void"))
                    {
                        extra++;
                    }
                    continue;
                }
                return UseClassification.CrossMethod(
                    $"Rented array is passed to {callee.DeclaringType.Name}::{callee.Name}.",
                    IsNonThrowingSetupBoundary(callee, valueType),
                    new ResourceExceptionBoundary(instruction.Offset, callee),
                    parameterIndex);
            }
            return UseClassification.OwnershipTransfer($"Rented array reaches unsupported opcode {opcode}.");
        }

        return UseClassification.OwnershipTransfer("Rented array use reaches the end of the instruction stream.");
    }

    internal static bool TryFindNextNonNop(ImmutableArray<DecodedInstruction> instructions, int offset, out DecodedInstruction instruction)
        => OwnershipValueFlowInstructions.TryFindNextNonNop(
            instructions,
            offset,
            out instruction);

    internal static bool TryFindInstruction(ImmutableArray<DecodedInstruction> instructions, int offset, out int index, out DecodedInstruction instruction)
        => OwnershipValueFlowInstructions.TryFindInstruction(
            instructions,
            offset,
            out index,
            out instruction);

    internal static bool TryReadStoreLocal(DecodedInstruction instruction, out int slot)
        => OwnershipValueFlowInstructions.TryReadStoreLocal(
            instruction,
            out slot);

    internal static bool IsLoadLocalOrAddress(
        DecodedInstruction instruction,
        int slot)
        => OwnershipValueFlowInstructions.IsLoadLocalOrAddress(
            instruction,
            slot);

    internal static bool TryReadLoadLocalAddress(
        DecodedInstruction instruction,
        out int slot) =>
        OwnershipValueFlowInstructions.TryReadLoadLocalAddress(
            instruction,
            out slot);

    static bool IsLoadSlotOrAddress(
        DecodedInstruction instruction,
        int slot,
        bool isArgument)
        => isArgument
            ? instruction.OpCode switch
            {
                ILOpCode.Ldarg_0 => slot == 0,
                ILOpCode.Ldarg_1 => slot == 1,
                ILOpCode.Ldarg_2 => slot == 2,
                ILOpCode.Ldarg_3 => slot == 3,
                ILOpCode.Ldarg_s or ILOpCode.Ldarg
                    or ILOpCode.Ldarga_s or ILOpCode.Ldarga
                    => instruction.OperandValue == slot,
                _ => false,
            }
            : IsLoadLocalOrAddress(instruction, slot);

    internal static bool IsSimpleArgumentPush(ILOpCode opcode)
        => OwnershipValueFlowInstructions.IsSimpleArgumentPush(opcode);

    static bool IsElementRead(ILOpCode opcode)
        => opcode is ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2
            or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8
            or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_ref;

    static bool IsElementStore(ILOpCode opcode)
        => opcode is ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
            or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
            or ILOpCode.Stelem_ref;

    static bool IsFieldStore(ILOpCode opcode)
        => opcode is ILOpCode.Stfld or ILOpCode.Stsfld;

    static bool IsLocalStore(ILOpCode opcode)
        => opcode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
            or ILOpCode.Stloc_s or ILOpCode.Stloc;

    static bool IsArrayPoolReturn(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "Return"
           && member.HasThis
           && member.ReturnType.Equals(TypeRef.CoreLib("System", "Void"))
           && member.ParameterTypes.Length is 1 or 2
           && member.ParameterTypes[0].Kind == TypeRefKind.SzArray
           && IsArrayPoolType(member.DeclaringType);

    static bool IsArrayPoolType(TypeRef type)
        => FrameworkIdentity.IsKnownFrameworkType(type, "System.Buffers", "System.Buffers", "ArrayPool`1")
           || FrameworkIdentity.IsCoreLibraryType(type, "System.Buffers", "ArrayPool`1");

    internal static bool IsNonThrowingSetupBoundary(
        MemberRef member,
        TypeRef? valueType = null)
    {
        if (member.Kind == MemberKind.Unsupported)
            return false;

        if (member.Name == "KeepAlive" && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "GC"))
            return true;

        if (member.Name == "Copy" && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "Array"))
            return true;

        if (member.Name == "Clear" && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "Array"))
            return true;

        if (member.Name == "CopyTo" && IsSpanType(member.DeclaringType))
            return true;

        if (member.Name == "AsSpan"
            && (FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "MemoryExtensions")
                || FrameworkIdentity.IsKnownFrameworkType(member.DeclaringType, "System.Memory", "System", "MemoryExtensions")))
        {
            return true;
        }

        if (IsArrayToTypedWrapperConversion(member)
            && valueType?.Equals(member.ParameterTypes[0]) == true)
        {
            return true;
        }

        if (IsExactArrayWrapperConstructor(member, valueType)
            || IsMutableToReadOnlyWrapperConversion(member))
        {
            return true;
        }

        return member.Name is "Clear" or "Fill"
            && IsSpanType(member.DeclaringType);
    }

    internal static bool IsTransparentWrapperBoundary(MemberRef member)
        => member.Name == ".ctor"
            && member.ParameterTypes is [{ Kind: TypeRefKind.SzArray }, ..]
            && IsTypedWrapperType(member.DeclaringType);

    static bool IsArrayToTypedWrapperConversion(MemberRef member)
        => member.Name == "op_Implicit"
            && member.ParameterTypes is [{ Kind: TypeRefKind.SzArray }]
            && IsTypedWrapperType(member.ReturnType);

    static bool IsExactArrayWrapperConstructor(
        MemberRef member,
        TypeRef? valueType)
        => member.Name == ".ctor"
            && member.ParameterTypes is [{ Kind: TypeRefKind.SzArray } array]
            && valueType?.Equals(array) == true
            && IsTypedWrapperType(member.DeclaringType);

    static bool IsMutableToReadOnlyWrapperConversion(MemberRef member)
        => member.Name == "op_Implicit"
            && member.ParameterTypes is [var source]
            && ((IsFrameworkGenericType(source, "Memory`1")
                    && IsFrameworkGenericType(
                        member.ReturnType,
                        "ReadOnlyMemory`1"))
                || (IsSpanType(source)
                    && IsReadOnlySpanType(member.ReturnType)))
            && source.TypeArguments.SequenceEqual(
                member.ReturnType.TypeArguments);

    internal static bool IsTypedWrapperType(TypeRef type)
        => IsSpanType(type)
            || IsReadOnlySpanType(type)
            || IsFrameworkGenericType(type, "Memory`1")
            || IsFrameworkGenericType(type, "ReadOnlyMemory`1");

    static bool IsSpanType(TypeRef type)
        => IsFrameworkGenericType(type, "Span`1");

    static bool IsReadOnlySpanType(TypeRef type)
        => IsFrameworkGenericType(type, "ReadOnlySpan`1");

    static bool IsFrameworkGenericType(TypeRef type, string name)
    {
        if (type.Kind != TypeRefKind.GenericInstance || type.ElementType is not { } definition)
            return false;

        return FrameworkIdentity.IsCoreLibraryType(definition, "System", name)
            || FrameworkIdentity.IsKnownFrameworkType(
                definition,
                "System.Memory",
                "System",
                name);
    }

    internal enum UseKind
    {
        Release,
        LocalUse,
        Store,
        Return,
        Forward,
        Unknown,
    }

    internal readonly record struct UseClassification(
        UseKind Kind,
        string CandidateShape,
        string Evidence,
        bool NonThrowingSetupBoundary = false,
        ResourceExceptionBoundary? Boundary = null,
        int OperationOffset = -1,
        int ParameterIndex = -1)
    {
        public static UseClassification ReleaseAt(int offset) =>
            new(UseKind.Release, "", "", OperationOffset: offset);
        public static UseClassification LocalUse { get; } = new(UseKind.LocalUse, "", "");
        public static UseClassification StoreAt(int offset) =>
            new(UseKind.Store, "alias-or-field-suppressed", "Rented array is stored into a field.", OperationOffset: offset);
        public static UseClassification ReturnAt(int offset) =>
            new(UseKind.Return, "ownership-transfer-suppressed", "Rented array escapes through the return value.", OperationOffset: offset);
        public static UseClassification AliasOrField(string evidence) => new(UseKind.Unknown, "alias-or-field-suppressed", evidence);
        public static UseClassification CrossMethod(
            string evidence,
            bool nonThrowingSetupBoundary,
            ResourceExceptionBoundary boundary,
            int parameterIndex)
            => new(
                UseKind.Forward,
                "cross-method-suppressed",
                evidence,
                nonThrowingSetupBoundary,
                boundary,
                boundary.ILOffset,
                parameterIndex);
        public static UseClassification OwnershipTransfer(string evidence) => new(UseKind.Unknown, "ownership-transfer-suppressed", evidence);
    }

}
