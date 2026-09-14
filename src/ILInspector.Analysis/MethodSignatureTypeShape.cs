using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal enum MethodSignatureTypePosition
{
    DeclaringType,
    MethodReturn,
    MethodParameter,
    ByRefTarget,
    ArrayElement,
    PointerTarget,
    GenericInstanceDefinition,
    GenericInstanceArgument,
    MethodSpecificationArgument,
    CustomModifierType,
    ModifiedType,
    FunctionPointerReturn,
    FunctionPointerParameter,
    OpenMethodReturn,
    OpenMethodParameter,
}

internal enum MethodSignatureTypeFailure
{
    Incomplete,
    Unsupported,
    Ambiguous,
    WorkLimitExceeded,
}

internal readonly record struct MethodSignatureTypeInspection(
    MethodSignatureTypeFailure? Failure,
    long Nodes,
    bool ContainsGenericParameter)
{
    internal bool IsSupported => Failure is null;
}

internal readonly record struct MethodSignatureTypeRoot(
    TypeRef Type,
    MethodSignatureTypePosition Position);

/// <summary>
/// Owns bounded traversal and positional legality for Analysis signature types.
/// </summary>
internal static class MethodSignatureTypeShape
{
    const byte HasThisSignatureFlag = 0x20;
    const byte ExplicitThisSignatureFlag = 0x40;
    const byte ReservedSignatureFlag = 0x80;

    internal static MethodSignatureTypeInspection InspectInvocation(
        MemberRef member,
        int typeGenericArity,
        int methodGenericArity,
        long maxNodes = long.MaxValue,
        Func<TypeRef, int?, MethodSignatureTypeFailure?>?
            validateDefinition = null,
        Action<TypeRef>? visit = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        return Inspect(
            InvocationTypes(member),
            typeGenericArity,
            methodGenericArity,
            maxNodes,
            validateDefinition,
            visit);
    }

    internal static MethodSignatureTypeInspection InspectMethodSignature(
        MemberRef member,
        int typeGenericArity,
        int methodGenericArity,
        long maxNodes = long.MaxValue,
        Func<TypeRef, int?, MethodSignatureTypeFailure?>?
            validateDefinition = null,
        Action<TypeRef>? visit = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        return Inspect(
            MethodTypes(member),
            typeGenericArity,
            methodGenericArity,
            maxNodes,
            validateDefinition,
            visit);
    }

    internal static MethodSignatureTypeInspection InspectMethodSignature(
        MethodSignature<TypeRef> signature,
        int typeGenericArity,
        int methodGenericArity,
        long maxNodes = long.MaxValue,
        Func<TypeRef, int?, MethodSignatureTypeFailure?>?
            validateDefinition = null,
        Action<TypeRef>? visit = null) =>
        Inspect(
            SignatureTypes(
                signature,
                MethodSignatureTypePosition.MethodReturn,
                MethodSignatureTypePosition.MethodParameter),
            typeGenericArity,
            methodGenericArity,
            maxNodes,
            validateDefinition,
            visit);

    internal static MethodSignatureTypeInspection InspectOpenSignature(
        MemberRef member,
        int typeGenericArity,
        int methodGenericArity,
        long maxNodes = long.MaxValue,
        Func<TypeRef, int?, MethodSignatureTypeFailure?>?
            validateDefinition = null,
        Action<TypeRef>? visit = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        return Inspect(
            OpenSignatureTypes(member),
            typeGenericArity,
            methodGenericArity,
            maxNodes,
            validateDefinition,
            visit);
    }

    internal static MethodSignatureTypeInspection
        InspectCorrespondencePlan(
            MemberRef member,
            int typeGenericArity,
            int methodGenericArity,
            long maxNodes = long.MaxValue,
            Func<TypeRef, int?, MethodSignatureTypeFailure?>?
                validateDefinition = null,
            Action<TypeRef>? visit = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        return Inspect(
            CorrespondencePlanTypes(member),
            typeGenericArity,
            methodGenericArity,
            maxNodes,
            validateDefinition,
            visit);
    }

    internal static MethodSignatureTypeInspection
        InspectMethodSpecificationArguments(
            IEnumerable<TypeRef> arguments,
            int typeGenericArity,
            int methodGenericArity,
            long maxNodes = long.MaxValue,
            Func<TypeRef, int?, MethodSignatureTypeFailure?>?
                validateDefinition = null,
            Action<TypeRef>? visit = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return Inspect(
            arguments.Select(argument =>
                new MethodSignatureTypeRoot(
                    argument,
                    MethodSignatureTypePosition
                        .MethodSpecificationArgument)),
            typeGenericArity,
            methodGenericArity,
            maxNodes,
            validateDefinition,
            visit);
    }

    internal static MethodSignatureTypeInspection Inspect(
        TypeRef type,
        MethodSignatureTypePosition position,
        int typeGenericArity,
        int methodGenericArity,
        long maxNodes = long.MaxValue,
        Func<TypeRef, int?, MethodSignatureTypeFailure?>?
            validateDefinition = null,
        Action<TypeRef>? visit = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Inspect(
            [new MethodSignatureTypeRoot(type, position)],
            typeGenericArity,
            methodGenericArity,
            maxNodes,
            validateDefinition,
            visit);
    }

    internal static MethodSignatureTypeInspection Inspect(
        IEnumerable<MethodSignatureTypeRoot> roots,
        int typeGenericArity,
        int methodGenericArity,
        long maxNodes = long.MaxValue,
        Func<TypeRef, int?, MethodSignatureTypeFailure?>?
            validateDefinition = null,
        Action<TypeRef>? visit = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentOutOfRangeException.ThrowIfNegative(typeGenericArity);
        ArgumentOutOfRangeException.ThrowIfNegative(methodGenericArity);
        ArgumentOutOfRangeException.ThrowIfNegative(maxNodes);

        var pending = new Stack<(
            TypeRef Type,
            MethodSignatureTypePosition Position,
            int? ConstructedArity,
            MethodSignatureTypePosition? ModifiedFrom)>();
        foreach (MethodSignatureTypeRoot root in roots)
        {
            ArgumentNullException.ThrowIfNull(root.Type);
            pending.Push((root.Type, root.Position, null, null));
        }

        long nodes = 0;
        bool containsGenericParameter = false;
        MethodSignatureTypeFailure? failure = null;
        while (pending.Count > 0)
        {
            (TypeRef current, MethodSignatureTypePosition position,
                int? constructedArity,
                MethodSignatureTypePosition? modifiedFrom) =
                    pending.Pop();
            nodes++;
            if (nodes > maxNodes)
            {
                return new(
                    MethodSignatureTypeFailure.WorkLimitExceeded,
                    nodes,
                    containsGenericParameter);
            }
            visit?.Invoke(current);

            switch (current.Kind)
            {
                case TypeRefKind.Definition:
                    bool validDefinition =
                        current.ElementType is null
                        && current.TypeArguments.IsEmpty
                        && current.ModifierType is null
                        && current.UnmodifiedType is null
                        && current.FunctionPointerSignature is null
                        && (position
                                != MethodSignatureTypePosition
                                    .GenericInstanceDefinition
                            || constructedArity is not null)
                        && !IsDisallowedIntrinsic(
                            current,
                            position,
                            modifiedFrom);
                    if (!validDefinition)
                    {
                        AddFailure(
                            MethodSignatureTypeFailure.Unsupported);
                    }
                    else if (validateDefinition is not null)
                    {
                        AddFailure(
                            validateDefinition(
                                current,
                                constructedArity));
                    }
                    break;

                case TypeRefKind.GenericInstance:
                    if (!AllowsGenericInstance(position)
                        || current.ElementType is null
                        || current.ElementType.Kind
                            != TypeRefKind.Definition
                        || current.TypeArguments.IsDefaultOrEmpty)
                    {
                        AddFailure(
                            MethodSignatureTypeFailure.Unsupported);
                    }
                    if (current.ElementType is not null)
                    {
                        pending.Push((
                            current.ElementType,
                            MethodSignatureTypePosition
                                .GenericInstanceDefinition,
                            current.TypeArguments.IsDefault
                                ? null
                                : current.TypeArguments.Length,
                            null));
                    }
                    if (!current.TypeArguments.IsDefault)
                    {
                        foreach (TypeRef argument in current.TypeArguments)
                        {
                            pending.Push((
                                argument,
                                MethodSignatureTypePosition
                                    .GenericInstanceArgument,
                                null,
                                null));
                        }
                    }
                    break;

                case TypeRefKind.SzArray:
                    AddUnary(
                        current,
                        AllowsGeneralType(position),
                        MethodSignatureTypePosition.ArrayElement);
                    break;

                case TypeRefKind.Array:
                    if (!AllowsGeneralType(position)
                        || current.ElementType is null
                        || current.Rank <= 0
                        || current.ArraySizes.Length > current.Rank
                        || current.ArrayLowerBounds.Length > current.Rank)
                    {
                        AddFailure(
                            MethodSignatureTypeFailure.Unsupported);
                    }
                    if (current.ElementType is not null)
                    {
                        pending.Push((
                            current.ElementType,
                            MethodSignatureTypePosition.ArrayElement,
                            null,
                            null));
                    }
                    break;

                case TypeRefKind.ByRef:
                    AddUnary(
                        current,
                        AllowsByRef(position, modifiedFrom),
                        MethodSignatureTypePosition.ByRefTarget);
                    break;

                case TypeRefKind.Pointer:
                    AddUnary(
                        current,
                        AllowsPointer(position, modifiedFrom),
                        MethodSignatureTypePosition.PointerTarget);
                    break;

                case TypeRefKind.Pinned:
                    AddFailure(
                        MethodSignatureTypeFailure.Unsupported);
                    if (current.ElementType is not null)
                    {
                        pending.Push((
                            current.ElementType,
                            position,
                            null,
                            modifiedFrom));
                    }
                    break;

                case TypeRefKind.GenericParameter:
                    containsGenericParameter = true;
                    if (!AllowsGenericParameter(position, modifiedFrom)
                        || (uint)current.GenericParameterIndex
                            >= (uint)typeGenericArity)
                    {
                        AddFailure(
                            MethodSignatureTypeFailure.Unsupported);
                    }
                    break;

                case TypeRefKind.MethodGenericParameter:
                    containsGenericParameter = true;
                    if (!AllowsGenericParameter(position, modifiedFrom)
                        || (uint)current.GenericParameterIndex
                            >= (uint)methodGenericArity)
                    {
                        AddFailure(
                            MethodSignatureTypeFailure.Unsupported);
                    }
                    break;

                case TypeRefKind.Unsupported:
                    bool hasModifiedPayload =
                        current.ModifierType is not null
                        || current.UnmodifiedType is not null;
                    bool hasFunctionPointerPayload =
                        current.FunctionPointerSignature is not null;
                    if (!hasModifiedPayload
                        && !hasFunctionPointerPayload)
                    {
                        AddFailure(
                            MethodSignatureTypeFailure.Unsupported);
                    }
                    if (hasModifiedPayload)
                    {
                        if (!AllowsGeneralType(position)
                            || current.ModifierType is null
                            || current.UnmodifiedType is null
                            || hasFunctionPointerPayload)
                        {
                            AddFailure(
                                MethodSignatureTypeFailure.Unsupported);
                        }
                        if (current.ModifierType is not null)
                        {
                            pending.Push((
                                current.ModifierType,
                                MethodSignatureTypePosition
                                    .CustomModifierType,
                                null,
                                null));
                        }
                        if (current.UnmodifiedType is not null)
                        {
                            pending.Push((
                                current.UnmodifiedType,
                                MethodSignatureTypePosition.ModifiedType,
                                null,
                                position
                                    == MethodSignatureTypePosition
                                        .ModifiedType
                                    ? modifiedFrom
                                    : position));
                        }
                    }
                    if (current.FunctionPointerSignature
                        is { } function)
                    {
                        if (!AllowsFunctionPointer(
                                position,
                                modifiedFrom)
                            || hasModifiedPayload
                            || !HasSupportedFunctionPointerHeader(function)
                            || function.ParameterTypes.IsDefault
                            || function.RequiredParameterCount < 0
                            || function.RequiredParameterCount
                                > function.ParameterTypes.Length)
                        {
                            AddFailure(
                                MethodSignatureTypeFailure.Unsupported);
                        }
                        pending.Push((
                            function.ReturnType,
                            MethodSignatureTypePosition
                                .FunctionPointerReturn,
                            null,
                            null));
                        if (!function.ParameterTypes.IsDefault)
                        {
                            foreach (TypeRef parameter
                                in function.ParameterTypes)
                            {
                                pending.Push((
                                    parameter,
                                    MethodSignatureTypePosition
                                        .FunctionPointerParameter,
                                    null,
                                    null));
                            }
                        }
                    }
                    break;

                default:
                    AddFailure(
                        MethodSignatureTypeFailure.Unsupported);
                    break;
            }
        }

        return new(failure, nodes, containsGenericParameter);

        void AddUnary(
            TypeRef current,
            bool allowed,
            MethodSignatureTypePosition targetPosition)
        {
            if (!allowed || current.ElementType is null)
            {
                AddFailure(
                    MethodSignatureTypeFailure.Unsupported);
            }
            if (current.ElementType is not null)
            {
                pending.Push((
                    current.ElementType,
                    targetPosition,
                    null,
                    null));
            }
        }

        void AddFailure(MethodSignatureTypeFailure? candidate) =>
            failure = Stronger(failure, candidate);
    }

    static IEnumerable<MethodSignatureTypeRoot> InvocationTypes(
        MemberRef member)
    {
        foreach (MethodSignatureTypeRoot root in MethodTypes(member))
            yield return root;
        foreach (TypeRef argument in member.TypeArguments)
        {
            yield return new(
                argument,
                MethodSignatureTypePosition.MethodSpecificationArgument);
        }
    }

    static IEnumerable<MethodSignatureTypeRoot>
        CorrespondencePlanTypes(MemberRef member)
    {
        yield return new(
            GenericMemberIdentity.OpenDeclaringType(
                member.DeclaringType),
            MethodSignatureTypePosition.DeclaringType);
        foreach (MethodSignatureTypeRoot root
            in OpenSignatureTypes(member))
        {
            yield return root;
        }
    }

    static IEnumerable<MethodSignatureTypeRoot> MethodTypes(
        MemberRef member)
    {
        yield return new(
            member.DeclaringType,
            MethodSignatureTypePosition.DeclaringType);
        yield return new(
            member.ReturnType,
            MethodSignatureTypePosition.MethodReturn);
        foreach (TypeRef parameter in member.ParameterTypes)
        {
            yield return new(
                parameter,
                MethodSignatureTypePosition.MethodParameter);
        }
    }

    static IEnumerable<MethodSignatureTypeRoot> OpenSignatureTypes(
        MemberRef member)
    {
        yield return new(
            member.OpenSignatureReturn,
            MethodSignatureTypePosition.OpenMethodReturn);
        foreach (TypeRef parameter in member.OpenSignatureParameters)
        {
            yield return new(
                parameter,
                MethodSignatureTypePosition.OpenMethodParameter);
        }
    }

    static IEnumerable<MethodSignatureTypeRoot> SignatureTypes(
        MethodSignature<TypeRef> signature,
        MethodSignatureTypePosition returnPosition,
        MethodSignatureTypePosition parameterPosition)
    {
        yield return new(signature.ReturnType, returnPosition);
        foreach (TypeRef parameter in signature.ParameterTypes)
            yield return new(parameter, parameterPosition);
    }

    static bool AllowsGeneralType(
        MethodSignatureTypePosition position) =>
        position is not (
            MethodSignatureTypePosition.CustomModifierType
            or MethodSignatureTypePosition.GenericInstanceDefinition);

    static bool AllowsGenericInstance(
        MethodSignatureTypePosition position) =>
        AllowsGeneralType(position)
        || position == MethodSignatureTypePosition.CustomModifierType;

    static bool AllowsByRef(
        MethodSignatureTypePosition position,
        MethodSignatureTypePosition? modifiedFrom)
    {
        MethodSignatureTypePosition effective =
            EffectivePosition(position, modifiedFrom);
        return effective is
            MethodSignatureTypePosition.MethodReturn
            or MethodSignatureTypePosition.MethodParameter
            or MethodSignatureTypePosition.FunctionPointerReturn
            or MethodSignatureTypePosition.FunctionPointerParameter
            or MethodSignatureTypePosition.OpenMethodReturn
            or MethodSignatureTypePosition.OpenMethodParameter;
    }

    static bool AllowsPointer(
        MethodSignatureTypePosition position,
        MethodSignatureTypePosition? modifiedFrom)
    {
        if (position == MethodSignatureTypePosition.ModifiedType
            && modifiedFrom
                == MethodSignatureTypePosition.MethodSpecificationArgument)
        {
            return true;
        }
        return EffectivePosition(position, modifiedFrom) is not (
            MethodSignatureTypePosition.DeclaringType
            or MethodSignatureTypePosition.MethodSpecificationArgument
            or MethodSignatureTypePosition.GenericInstanceArgument
            or MethodSignatureTypePosition.CustomModifierType
            or MethodSignatureTypePosition.GenericInstanceDefinition);
    }

    static bool AllowsFunctionPointer(
        MethodSignatureTypePosition position,
        MethodSignatureTypePosition? modifiedFrom) =>
        AllowsPointer(position, modifiedFrom);

    static bool HasSupportedFunctionPointerHeader(
        MethodSignature<TypeRef> signature) =>
        signature.Header.Kind == SignatureKind.Method
        && (signature.Header.RawValue & ReservedSignatureFlag) == 0
        && ((signature.Header.RawValue & ExplicitThisSignatureFlag) == 0
            || (signature.Header.RawValue & HasThisSignatureFlag) != 0)
        && (!signature.Header.IsGeneric
            || signature.GenericParameterCount > 0);

    static bool AllowsGenericParameter(
        MethodSignatureTypePosition position,
        MethodSignatureTypePosition? modifiedFrom) =>
        EffectivePosition(position, modifiedFrom) is not (
            MethodSignatureTypePosition.DeclaringType
            or MethodSignatureTypePosition.CustomModifierType
            or MethodSignatureTypePosition.GenericInstanceDefinition);

    static bool IsDisallowedIntrinsic(
        TypeRef type,
        MethodSignatureTypePosition position,
        MethodSignatureTypePosition? modifiedFrom)
    {
        MethodSignatureTypePosition effective =
            EffectivePosition(position, modifiedFrom);
        if (FrameworkIdentity.IsCoreLibraryType(
                type,
                "System",
                "Void"))
        {
            return effective is not (
                MethodSignatureTypePosition.MethodReturn
                or MethodSignatureTypePosition.PointerTarget
                or MethodSignatureTypePosition.FunctionPointerReturn
                or MethodSignatureTypePosition.OpenMethodReturn);
        }
        if (!FrameworkIdentity.IsCoreLibraryType(
                type,
                "System",
                "TypedReference"))
        {
            return false;
        }
        return effective is not (
            MethodSignatureTypePosition.MethodReturn
            or MethodSignatureTypePosition.MethodParameter
            or MethodSignatureTypePosition.FunctionPointerReturn
            or MethodSignatureTypePosition.FunctionPointerParameter
            or MethodSignatureTypePosition.OpenMethodReturn
            or MethodSignatureTypePosition.OpenMethodParameter);
    }

    static MethodSignatureTypePosition EffectivePosition(
        MethodSignatureTypePosition position,
        MethodSignatureTypePosition? modifiedFrom) =>
        position == MethodSignatureTypePosition.ModifiedType
            ? modifiedFrom ?? position
            : position;

    static MethodSignatureTypeFailure? Stronger(
        MethodSignatureTypeFailure? current,
        MethodSignatureTypeFailure? candidate)
    {
        if (candidate is null)
            return current;
        if (current is null
            || candidate == MethodSignatureTypeFailure.WorkLimitExceeded)
        {
            return candidate;
        }
        if (current == MethodSignatureTypeFailure.WorkLimitExceeded
            || current == MethodSignatureTypeFailure.Ambiguous
            || candidate == MethodSignatureTypeFailure.Ambiguous)
        {
            return current == MethodSignatureTypeFailure.WorkLimitExceeded
                ? current
                : MethodSignatureTypeFailure.Ambiguous;
        }
        if (current == MethodSignatureTypeFailure.Unsupported
            || candidate == MethodSignatureTypeFailure.Unsupported)
        {
            return MethodSignatureTypeFailure.Unsupported;
        }
        return MethodSignatureTypeFailure.Incomplete;
    }
}
