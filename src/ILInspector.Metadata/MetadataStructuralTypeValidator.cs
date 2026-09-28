using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal static class MetadataStructuralTypeValidator
{
    internal static string? ValidatePropertySignature(
        MethodSignature<TypeNode> signature,
        int typeParameterCount,
        string subject)
    {
        if (signature.Header.RawValue is not (0x08 or 0x28)
            || signature.GenericParameterCount != 0
            || signature.RequiredParameterCount
                != signature.ParameterTypes.Length)
        {
            return $"{subject} does not carry a valid PropertySig header.";
        }

        string? failure = ValidateSignatureType(
            signature.ReturnType,
            allowByReference: true,
            allowVoid: false,
            allowTypedReference: true,
            $"{subject} value type");
        if (failure is not null)
            return failure;

        failure = Validate(
            signature.ReturnType,
            typeParameterCount,
            methodParameterCount: 0,
            $"{subject} value type");
        if (failure is not null)
            return failure;

        foreach (TypeNode parameter in signature.ParameterTypes)
        {
            failure = ValidateSignatureType(
                parameter,
                allowByReference: true,
                allowVoid: false,
                allowTypedReference: true,
                $"{subject} index parameter");
            if (failure is not null)
                return failure;

            failure = Validate(
                parameter,
                typeParameterCount,
                methodParameterCount: 0,
                $"{subject} index parameter");
            if (failure is not null)
                return failure;
        }
        return null;
    }

    internal static string? ValidateTypeSignature(
        TypeNode node,
        int typeParameterCount,
        int methodParameterCount,
        string subject) =>
        ValidateSignatureType(
            node,
            allowByReference: false,
            allowVoid: false,
            allowTypedReference: false,
            subject)
        ?? Validate(
            node,
            typeParameterCount,
            methodParameterCount,
            subject);

    internal static string? ValidateAccessorMethodSignature(
        MethodSignature<TypeNode> signature,
        int typeParameterCount,
        string subject)
    {
        SignatureHeader header = signature.Header;
        if ((header.RawValue & 0x80) != 0
            || header.Kind != SignatureKind.Method
            || header.HasExplicitThis
            || header.IsGeneric
            || header.CallingConvention
                != SignatureCallingConvention.Default
            || signature.GenericParameterCount != 0
            || signature.RequiredParameterCount
                != signature.ParameterTypes.Length)
        {
            return $"{subject} does not carry a complete ordinary method signature.";
        }

        string? failure = ValidateSignatureType(
            signature.ReturnType,
            allowByReference: true,
            allowVoid: true,
            allowTypedReference: true,
            $"{subject} return type");
        if (failure is not null)
            return failure;

        failure = Validate(
            signature.ReturnType,
            typeParameterCount,
            methodParameterCount: 0,
            $"{subject} return type");
        if (failure is not null)
            return failure;

        foreach (TypeNode parameter in signature.ParameterTypes)
        {
            failure = ValidateSignatureType(
                parameter,
                allowByReference: true,
                allowVoid: false,
                allowTypedReference: true,
                $"{subject} parameter");
            if (failure is not null)
                return failure;

            failure = Validate(
                parameter,
                typeParameterCount,
                methodParameterCount: 0,
                $"{subject} parameter");
            if (failure is not null)
                return failure;
        }
        return null;
    }

    internal static string? Validate(
        TypeNode node,
        int typeParameterCount,
        int methodParameterCount,
        string subject)
    {
        if (node is GenericParameterNode parameter)
        {
            int count = parameter.IsMethodParameter
                ? methodParameterCount
                : typeParameterCount;
            return (uint)parameter.Index < (uint)count
                ? null
                : $"{subject} references "
                    + $"{(parameter.IsMethodParameter ? "MVAR" : "VAR")} "
                    + $"{parameter.Index} outside its authenticated context of {count} parameters.";
        }

        if (node is GenericTypeNode generic)
        {
            int declaredArity =
                DeclaredGenericArity(generic.MetadataName);
            if (declaredArity != generic.Arguments.Length)
            {
                return $"{subject} constructs a type with "
                    + $"{generic.Arguments.Length} arguments for "
                    + $"{declaredArity} authenticated generic parameters.";
            }
        }
        foreach (TypeNode child in Children(node))
        {
            string? failure = Validate(
                child,
                typeParameterCount,
                methodParameterCount,
                subject);
            if (failure is not null)
                return failure;
        }
        return null;
    }

    static string? ValidateSignatureType(
        TypeNode node,
        bool allowByReference,
        bool allowVoid,
        bool allowTypedReference,
        string subject)
    {
        switch (node)
        {
            case PrimitiveTypeNode { Name: "void" }:
                return allowVoid
                    ? null
                    : $"{subject} cannot be void.";

            case PrimitiveTypeNode { Name: "TypedReference" }:
                return allowTypedReference
                    ? null
                    : $"{subject} cannot contain a nested typed reference.";

            case ModifiedTypeNode modified:
                return ValidateSignatureType(
                        modified.Modifier,
                        allowByReference: false,
                        allowVoid: false,
                        allowTypedReference: false,
                        $"{subject} custom modifier")
                    ?? ValidateSignatureType(
                        modified.Inner,
                        allowByReference,
                        allowVoid,
                        allowTypedReference,
                        subject);

            case PinnedTypeNode:
                return $"{subject} cannot be pinned.";

            case ByRefTypeNode byReference:
                return !allowByReference
                    ? $"{subject} contains a nested by-reference type."
                    : ValidateSignatureType(
                        byReference.ElementType,
                        allowByReference: false,
                        allowVoid: false,
                        allowTypedReference: false,
                        subject);

            case PointerTypeNode pointer:
                return ValidateSignatureType(
                    pointer.ElementType,
                    allowByReference: false,
                    allowVoid: true,
                    allowTypedReference: false,
                    $"{subject} pointer target");

            case SZArrayTypeNode array:
                return ValidateSignatureType(
                    array.ElementType,
                    allowByReference: false,
                    allowVoid: false,
                    allowTypedReference: false,
                    $"{subject} array element");

            case MDArrayTypeNode array:
                if (array.Rank <= 0
                    || array.ArraySizes.Length > array.Rank
                    || array.ArrayLowerBounds.Length > array.Rank)
                {
                    return $"{subject} has an invalid array shape.";
                }
                return ValidateSignatureType(
                    array.ElementType,
                    allowByReference: false,
                    allowVoid: false,
                    allowTypedReference: false,
                    $"{subject} array element");

            case GenericTypeNode generic:
                foreach (TypeNode argument in generic.Arguments)
                {
                    string? failure = ValidateSignatureType(
                        argument,
                        allowByReference: false,
                        allowVoid: false,
                        allowTypedReference: false,
                        $"{subject} generic argument");
                    if (failure is not null)
                        return failure;
                }
                return null;

            case FunctionPointerTypeNode functionPointer:
                return ValidateFunctionPointer(
                    functionPointer.Signature,
                    subject);

            default:
                return null;
        }
    }

    static string? ValidateFunctionPointer(
        MethodSignature<TypeNode> signature,
        string subject)
    {
        SignatureHeader header = signature.Header;
        if ((header.RawValue & 0x80) != 0
            || header.Kind != SignatureKind.Method
            || header.HasExplicitThis && !header.IsInstance
            || signature.RequiredParameterCount < 0
            || signature.RequiredParameterCount
                > signature.ParameterTypes.Length
            || signature.RequiredParameterCount
                    != signature.ParameterTypes.Length
                && header.CallingConvention
                    != SignatureCallingConvention.VarArgs
            || header.CallingConvention is not (
                SignatureCallingConvention.Default
                or SignatureCallingConvention.CDecl
                or SignatureCallingConvention.StdCall
                or SignatureCallingConvention.ThisCall
                or SignatureCallingConvention.FastCall
                or SignatureCallingConvention.VarArgs
                or SignatureCallingConvention.Unmanaged))
        {
            return $"{subject} contains an invalid function-pointer signature.";
        }

        string? failure = ValidateSignatureType(
            signature.ReturnType,
            allowByReference: true,
            allowVoid: true,
            allowTypedReference: true,
            $"{subject} function-pointer return type");
        if (failure is not null)
            return failure;

        foreach (TypeNode parameter in signature.ParameterTypes)
        {
            failure = ValidateSignatureType(
                parameter,
                allowByReference: true,
                allowVoid: false,
                allowTypedReference: true,
                $"{subject} function-pointer parameter");
            if (failure is not null)
                return failure;
        }
        return null;
    }

    static IEnumerable<TypeNode> Children(TypeNode node) =>
        node switch
        {
            GenericTypeNode generic => generic.Arguments,
            SZArrayTypeNode array => [array.ElementType],
            MDArrayTypeNode array => [array.ElementType],
            PointerTypeNode pointer => [pointer.ElementType],
            ByRefTypeNode byReference => [byReference.ElementType],
            FunctionPointerTypeNode functionPointer =>
                [
                    functionPointer.Signature.ReturnType,
                    .. functionPointer.Signature.ParameterTypes,
                ],
            ModifiedTypeNode modified =>
                [modified.Modifier, modified.Inner],
            PinnedTypeNode pinned => [pinned.Inner],
            _ => [],
        };

    internal static int DeclaredGenericArity(
        MetadataTypeNameParts? parts)
    {
        if (parts is null)
            return 0;

        int arity = 0;
        bool hasAuthenticatedCounts =
            parts.IntroducedTypeParameterCounts is { } counts
            && counts.Count == parts.Segments.Count;
        for (int index = 0; index < parts.Segments.Count; index++)
        {
            arity = checked(
                arity
                    + (hasAuthenticatedCounts
                        ? parts.IntroducedTypeParameterCounts![index]
                        : MetadataNameArity.OfSegment(
                            parts.Segments[index])));
        }
        return arity;
    }
}
