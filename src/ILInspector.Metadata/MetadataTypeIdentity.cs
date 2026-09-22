using System.Collections.Immutable;
using InertText;

namespace ILInspector.Metadata;

public sealed record MetadataAssemblyIdentity(
    InertString Name,
    Version? Version,
    InertString? Culture,
    InertString? PublicKeyToken);

public sealed record MetadataTypeScopeIdentity(
    MetadataTypeScopeKind Kind,
    Guid ModuleVersionId,
    InertString? ModuleName,
    MetadataAssemblyIdentity? Assembly);

public sealed record MetadataNamedTypeIdentity(
    MetadataTypeScopeIdentity Scope,
    InertString Namespace,
    ImmutableArray<InertString> Segments,
    ImmutableArray<int> IntroducedGenericParameterCounts)
{
    public bool Equals(MetadataNamedTypeIdentity? other) =>
        other is not null
        && Equals(Scope, other.Scope)
        && Equals(Namespace, other.Namespace)
        && MetadataIdentitySequence.Equal(
            Segments,
            other.Segments)
        && MetadataIdentitySequence.Equal(
            IntroducedGenericParameterCounts,
            other.IntroducedGenericParameterCounts);

    public override int GetHashCode() =>
        MetadataIdentitySequence.Hash(
            Scope,
            Namespace,
            MetadataIdentitySequence.Hash(Segments),
            MetadataIdentitySequence.Hash(
                IntroducedGenericParameterCounts));
}

public abstract record MetadataTypeIdentity
{
    private protected MetadataTypeIdentity()
    {
    }

    public sealed record Primitive(InertString Name)
        : MetadataTypeIdentity;

    public sealed record Named(
        MetadataNamedTypeIdentity Definition,
        bool IsValueType)
        : MetadataTypeIdentity;

    public sealed record GenericInstance(
        MetadataNamedTypeIdentity Definition,
        bool IsValueType,
        ImmutableArray<MetadataTypeIdentity> Arguments)
        : MetadataTypeIdentity
    {
        public bool Equals(GenericInstance? other) =>
            other is not null
            && Equals(Definition, other.Definition)
            && IsValueType == other.IsValueType
            && MetadataIdentitySequence.Equal(
                Arguments,
                other.Arguments);

        public override int GetHashCode() =>
            MetadataIdentitySequence.Hash(
                Definition,
                IsValueType,
                MetadataIdentitySequence.Hash(Arguments));
    }

    public sealed record GenericParameter(
        bool IsMethodParameter,
        int Index)
        : MetadataTypeIdentity;

    public sealed record SzArray(MetadataTypeIdentity Element)
        : MetadataTypeIdentity;

    public sealed record Array(
        MetadataTypeIdentity Element,
        int Rank,
        ImmutableArray<int> Sizes,
        ImmutableArray<int> LowerBounds)
        : MetadataTypeIdentity
    {
        public bool Equals(Array? other) =>
            other is not null
            && Equals(Element, other.Element)
            && Rank == other.Rank
            && MetadataIdentitySequence.Equal(Sizes, other.Sizes)
            && MetadataIdentitySequence.Equal(
                LowerBounds,
                other.LowerBounds);

        public override int GetHashCode() =>
            MetadataIdentitySequence.Hash(
                Element,
                Rank,
                MetadataIdentitySequence.Hash(Sizes),
                MetadataIdentitySequence.Hash(LowerBounds));
    }

    public sealed record Pointer(MetadataTypeIdentity Element)
        : MetadataTypeIdentity;

    public sealed record ByReference(MetadataTypeIdentity Element)
        : MetadataTypeIdentity;

    public sealed record FunctionPointer(
        MetadataMethodSignatureIdentity Signature)
        : MetadataTypeIdentity;

    public sealed record Modified(
        MetadataTypeIdentity Modifier,
        MetadataTypeIdentity Type,
        bool IsRequired)
        : MetadataTypeIdentity;

    public sealed record Pinned(MetadataTypeIdentity Type)
        : MetadataTypeIdentity;
}

public sealed record MetadataMethodSignatureIdentity(
    byte Header,
    int GenericParameterCount,
    int RequiredParameterCount,
    MetadataTypeIdentity ReturnType,
    ImmutableArray<MetadataTypeIdentity> ParameterTypes)
{
    public bool Equals(MetadataMethodSignatureIdentity? other) =>
        other is not null
        && Header == other.Header
        && GenericParameterCount == other.GenericParameterCount
        && RequiredParameterCount == other.RequiredParameterCount
        && Equals(ReturnType, other.ReturnType)
        && MetadataIdentitySequence.Equal(
            ParameterTypes,
            other.ParameterTypes);

    public override int GetHashCode() =>
        MetadataIdentitySequence.Hash(
            Header,
            GenericParameterCount,
            RequiredParameterCount,
            ReturnType,
            MetadataIdentitySequence.Hash(ParameterTypes));
}

internal static class MetadataIdentitySequence
{
    internal static bool Equal<T>(
        ImmutableArray<T> left,
        ImmutableArray<T> right)
    {
        if (left.IsDefault || right.IsDefault)
            return left.IsDefault == right.IsDefault;
        return left.AsSpan().SequenceEqual(right.AsSpan());
    }

    internal static int Hash<T>(ImmutableArray<T> values)
    {
        if (values.IsDefault)
            return 0;

        var hash = new HashCode();
        foreach (T value in values)
            hash.Add(value);
        return hash.ToHashCode();
    }

    internal static int Hash<T1, T2, T3>(
        T1 first,
        T2 second,
        T3 third) =>
        HashCode.Combine(first, second, third);

    internal static int Hash<T1, T2, T3, T4>(
        T1 first,
        T2 second,
        T3 third,
        T4 fourth) =>
        HashCode.Combine(first, second, third, fourth);

    internal static int Hash<T1, T2, T3, T4, T5>(
        T1 first,
        T2 second,
        T3 third,
        T4 fourth,
        T5 fifth) =>
        HashCode.Combine(first, second, third, fourth, fifth);
}
