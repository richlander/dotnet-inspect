namespace ILInspector.Metadata;

internal static class MetadataStructuralTypeValidator
{
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
