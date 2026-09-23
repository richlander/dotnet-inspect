using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal sealed class MetadataMethodStructuralSignature
{
    readonly Action<
        MetadataMethodImplementationFailureSite,
        MetadataOperationDimension,
        long> _charge;
    readonly Func<
        MetadataMethodImplementationFailureSite,
        MetadataMethodImplementationFailureReason,
        string,
        MetadataMethodImplementationRejectedException> _reject;

    internal MetadataMethodStructuralSignature(
        Action<
            MetadataMethodImplementationFailureSite,
            MetadataOperationDimension,
            long> charge,
        Func<
            MetadataMethodImplementationFailureSite,
            MetadataMethodImplementationFailureReason,
            string,
            MetadataMethodImplementationRejectedException> reject)
    {
        ArgumentNullException.ThrowIfNull(charge);
        ArgumentNullException.ThrowIfNull(reject);
        _charge = charge;
        _reject = reject;
    }

    internal void Validate(
        MethodSignature<TypeNode> signature,
        int typeParameterCount,
        int methodParameterCount,
        string subject,
        MetadataMethodImplementationFailureSite site)
    {
        ValidateType(
            signature.ReturnType,
            typeParameterCount,
            methodParameterCount,
            subject,
            site);
        foreach (TypeNode parameter in signature.ParameterTypes)
        {
            ValidateType(
                parameter,
                typeParameterCount,
                methodParameterCount,
                subject,
                site);
        }
    }

    internal bool Match(
        MethodSignature<TypeNode> left,
        MethodSignature<TypeNode> right,
        ImmutableArray<TypeNode> leftTypeArguments,
        ImmutableArray<TypeNode> rightTypeArguments,
        MetadataMethodImplementationFailureSite site)
    {
        if (left.Header.RawValue != right.Header.RawValue
            || left.GenericParameterCount
                != right.GenericParameterCount
            || left.RequiredParameterCount
                != right.RequiredParameterCount
            || left.ParameterTypes.Length
                != right.ParameterTypes.Length
            || !TypesMatch(
                left.ReturnType,
                right.ReturnType,
                leftTypeArguments,
                rightTypeArguments,
                site))
        {
            return false;
        }

        for (int index = 0; index < left.ParameterTypes.Length; index++)
        {
            if (!TypesMatch(
                    left.ParameterTypes[index],
                    right.ParameterTypes[index],
                    leftTypeArguments,
                    rightTypeArguments,
                    site))
            {
                return false;
            }
        }
        return true;
    }

    internal void ValidateType(
        TypeNode node,
        int typeParameterCount,
        int methodParameterCount,
        string subject,
        MetadataMethodImplementationFailureSite site)
    {
        string? failure =
            MetadataStructuralTypeValidator.Validate(
                node,
                typeParameterCount,
                methodParameterCount,
                subject);
        if (failure is not null)
        {
            throw _reject(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                failure);
        }
    }

    bool TypesMatch(
        TypeNode left,
        TypeNode right,
        ImmutableArray<TypeNode> leftTypeArguments,
        ImmutableArray<TypeNode> rightTypeArguments,
        MetadataMethodImplementationFailureSite site)
    {
        (left, bool leftSubstituted) =
            Substitute(left, leftTypeArguments, site);
        (right, bool rightSubstituted) =
            Substitute(right, rightTypeArguments, site);
        ImmutableArray<TypeNode> leftNestedArguments =
            leftSubstituted ? [] : leftTypeArguments;
        ImmutableArray<TypeNode> rightNestedArguments =
            rightSubstituted ? [] : rightTypeArguments;
        if (left.GetType() != right.GetType())
            return false;

        return (left, right) switch
        {
            (PrimitiveTypeNode l, PrimitiveTypeNode r) =>
                l.IsReferenceType == r.IsReferenceType
                && string.Equals(
                    l.Name,
                    r.Name,
                    StringComparison.Ordinal),
            (NamedTypeNode l, NamedTypeNode r) =>
                l.IsReferenceType == r.IsReferenceType
                && NamedTypesMatch(l, r),
            (GenericTypeNode l, GenericTypeNode r) =>
                l.IsReferenceType == r.IsReferenceType
                && NamedTypesMatch(l, r)
                && TypeSequencesMatch(
                    l.Arguments,
                    r.Arguments,
                    leftNestedArguments,
                    rightNestedArguments,
                    site),
            (SZArrayTypeNode l, SZArrayTypeNode r) =>
                TypesMatch(
                    l.ElementType,
                    r.ElementType,
                    leftNestedArguments,
                    rightNestedArguments,
                    site),
            (MDArrayTypeNode l, MDArrayTypeNode r) =>
                l.Rank == r.Rank
                && l.ArraySizes.AsSpan().SequenceEqual(
                    r.ArraySizes.AsSpan())
                && l.ArrayLowerBounds.AsSpan().SequenceEqual(
                    r.ArrayLowerBounds.AsSpan())
                && TypesMatch(
                    l.ElementType,
                    r.ElementType,
                    leftNestedArguments,
                    rightNestedArguments,
                    site),
            (PointerTypeNode l, PointerTypeNode r) =>
                TypesMatch(
                    l.ElementType,
                    r.ElementType,
                    leftNestedArguments,
                    rightNestedArguments,
                    site),
            (ByRefTypeNode l, ByRefTypeNode r) =>
                TypesMatch(
                    l.ElementType,
                    r.ElementType,
                    leftNestedArguments,
                    rightNestedArguments,
                    site),
            (GenericParameterNode l, GenericParameterNode r) =>
                l.IsMethodParameter == r.IsMethodParameter
                && l.Index == r.Index,
            (FunctionPointerTypeNode l,
                FunctionPointerTypeNode r) =>
                Match(
                    l.Signature,
                    r.Signature,
                    leftNestedArguments,
                    rightNestedArguments,
                    site),
            (ModifiedTypeNode l, ModifiedTypeNode r) =>
                l.IsRequired == r.IsRequired
                && TypesMatch(
                    l.Modifier,
                    r.Modifier,
                    leftNestedArguments,
                    rightNestedArguments,
                    site)
                && TypesMatch(
                    l.Inner,
                    r.Inner,
                    leftNestedArguments,
                    rightNestedArguments,
                    site),
            (PinnedTypeNode l, PinnedTypeNode r) =>
                TypesMatch(
                    l.Inner,
                    r.Inner,
                    leftNestedArguments,
                    rightNestedArguments,
                    site),
            _ => false,
        };
    }

    bool TypeSequencesMatch(
        ImmutableArray<TypeNode> left,
        ImmutableArray<TypeNode> right,
        ImmutableArray<TypeNode> leftTypeArguments,
        ImmutableArray<TypeNode> rightTypeArguments,
        MetadataMethodImplementationFailureSite site)
    {
        if (left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (!TypesMatch(
                    left[index],
                    right[index],
                    leftTypeArguments,
                    rightTypeArguments,
                    site))
            {
                return false;
            }
        }
        return true;
    }

    (TypeNode Node, bool Substituted) Substitute(
        TypeNode node,
        ImmutableArray<TypeNode> typeArguments,
        MetadataMethodImplementationFailureSite site)
    {
        if (typeArguments.IsEmpty)
            return (node, false);

        _charge(
            site,
            MetadataOperationDimension.GenericSubstitutionNodes,
            1);
        if (node is not GenericParameterNode
            {
                IsMethodParameter: false,
                Index: var index,
            })
        {
            return (node, false);
        }
        if ((uint)index >= (uint)typeArguments.Length)
        {
            throw _reject(
                site,
                MetadataMethodImplementationFailureReason
                    .MalformedMetadata,
                "A declaration signature references a type parameter outside its constructed owner.");
        }
        return (typeArguments[index], true);
    }

    static bool NamedTypesMatch(
        NamedTypeNode left,
        NamedTypeNode right) =>
        TypeNamesMatch(
            left.MetadataName,
            right.MetadataName)
        && ScopesMatch(left.ExactScope, right.ExactScope);

    static bool NamedTypesMatch(
        GenericTypeNode left,
        GenericTypeNode right) =>
        TypeNamesMatch(
            left.MetadataName,
            right.MetadataName)
        && ScopesMatch(left.ExactScope, right.ExactScope);

    static bool TypeNamesMatch(
        MetadataTypeNameParts? left,
        MetadataTypeNameParts? right)
    {
        if (left is null || right is null
            || !string.Equals(
                left.Namespace,
                right.Namespace,
                StringComparison.Ordinal)
            || left.Segments.Count != right.Segments.Count)
        {
            return false;
        }
        for (int index = 0; index < left.Segments.Count; index++)
        {
            if (!string.Equals(
                    left.Segments[index],
                    right.Segments[index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    static bool ScopesMatch(
        MetadataTypeScopeDescriptor? left,
        MetadataTypeScopeDescriptor? right)
    {
        if (left is null || right is null
            || left.Kind != right.Kind
            || left.ModuleVersionId != right.ModuleVersionId
            || !string.Equals(
                left.ModuleName,
                right.ModuleName,
                StringComparison.Ordinal))
        {
            return false;
        }
        if (left.Assembly is null || right.Assembly is null)
            return left.Assembly is null && right.Assembly is null;
        return left.Assembly.IsEquivalentTo(right.Assembly);
    }
}
