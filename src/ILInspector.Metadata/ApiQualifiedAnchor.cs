using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace ILInspector.Metadata;

public enum ApiQualifiedAnchorFormat
{
    V1 = 1,
}

public sealed record ApiQualifiedAssemblyFamily
{
    public ApiQualifiedAssemblyFamily(
        string name,
        string? culture,
        string? publicKeyToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
        Culture = AssemblyReferenceIdentity.NormalizeCulture(culture);
        PublicKeyToken = publicKeyToken ?? string.Empty;
    }

    public string Name { get; }

    public string Culture { get; }

    public string PublicKeyToken { get; }

    public bool Equals(ApiQualifiedAssemblyFamily? other)
        => other is not null &&
            string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Culture, other.Culture, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(PublicKeyToken, other.PublicKeyToken, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Name, StringComparer.OrdinalIgnoreCase);
        hash.Add(Culture, StringComparer.OrdinalIgnoreCase);
        hash.Add(PublicKeyToken, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }
}

public sealed record ApiQualifiedTypeDefinitionIdentity
{
    public ApiQualifiedTypeDefinitionIdentity(
        ApiQualifiedAssemblyFamily assembly,
        InertString @namespace,
        ImmutableArray<InertString> segments,
        ImmutableArray<int> introducedGenericParameterCounts)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (segments.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one type-name segment is required.", nameof(segments));
        }

        if (introducedGenericParameterCounts.IsDefault ||
            segments.Length != introducedGenericParameterCounts.Length)
        {
            throw new ArgumentException(
                "Type-name segments and introduced generic-parameter counts must have equal lengths.",
                nameof(introducedGenericParameterCounts));
        }

        if (introducedGenericParameterCounts.Any(static count => count < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(introducedGenericParameterCounts),
                "Introduced generic-parameter counts must be non-negative.");
        }

        Assembly = assembly;
        Namespace = @namespace.EnsurePermitted(TextPolicy.Field);
        Segments = segments
            .Select(static segment =>
                segment.EnsurePermitted(TextPolicy.Field))
            .ToImmutableArray();
        IntroducedGenericParameterCounts = introducedGenericParameterCounts;
    }

    public ApiQualifiedAssemblyFamily Assembly { get; }

    public InertString Namespace { get; }

    public ImmutableArray<InertString> Segments { get; }

    public ImmutableArray<int> IntroducedGenericParameterCounts { get; }

    public bool Equals(ApiQualifiedTypeDefinitionIdentity? other)
        => other is not null &&
            Assembly == other.Assembly &&
            Namespace == other.Namespace &&
            Segments.AsSpan().SequenceEqual(other.Segments.AsSpan()) &&
            IntroducedGenericParameterCounts.AsSpan().SequenceEqual(
                other.IntroducedGenericParameterCounts.AsSpan());

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Assembly);
        hash.Add(Namespace);

        foreach (InertString segment in Segments)
        {
            hash.Add(segment);
        }

        foreach (int count in IntroducedGenericParameterCounts)
        {
            hash.Add(count);
        }

        return hash.ToHashCode();
    }
}

public abstract record ApiQualifiedTypeIdentity
{
    private ApiQualifiedTypeIdentity()
    {
    }

    public sealed record Primitive(InertString Name) : ApiQualifiedTypeIdentity;

    public sealed record Named(
        ApiQualifiedTypeDefinitionIdentity Definition,
        bool IsValueType) : ApiQualifiedTypeIdentity;

    public sealed record GenericInstance(
        ApiQualifiedTypeDefinitionIdentity Definition,
        bool IsValueType,
        ImmutableArray<ApiQualifiedTypeIdentity> TypeArguments) : ApiQualifiedTypeIdentity
    {
        public bool Equals(GenericInstance? other)
            => other is not null &&
                Definition == other.Definition &&
                IsValueType == other.IsValueType &&
                TypeArguments.AsSpan().SequenceEqual(other.TypeArguments.AsSpan());

        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(Definition);
            hash.Add(IsValueType);
            foreach (ApiQualifiedTypeIdentity argument in TypeArguments)
            {
                hash.Add(argument);
            }

            return hash.ToHashCode();
        }
    }

    public sealed record GenericParameter(bool IsMethodParameter, int Index) : ApiQualifiedTypeIdentity;

    public sealed record SzArray(ApiQualifiedTypeIdentity ElementType) : ApiQualifiedTypeIdentity;

    public sealed record MdArray(
        ApiQualifiedTypeIdentity ElementType,
        int Rank,
        ImmutableArray<int> Sizes,
        ImmutableArray<int> LowerBounds) : ApiQualifiedTypeIdentity
    {
        public bool Equals(MdArray? other)
            => other is not null &&
                ElementType == other.ElementType &&
                Rank == other.Rank &&
                Sizes.AsSpan().SequenceEqual(other.Sizes.AsSpan()) &&
                LowerBounds.AsSpan().SequenceEqual(other.LowerBounds.AsSpan());

        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(ElementType);
            hash.Add(Rank);

            foreach (int size in Sizes)
            {
                hash.Add(size);
            }

            foreach (int lowerBound in LowerBounds)
            {
                hash.Add(lowerBound);
            }

            return hash.ToHashCode();
        }
    }

    public sealed record Pointer(ApiQualifiedTypeIdentity ElementType) : ApiQualifiedTypeIdentity;

    public sealed record ByRef(ApiQualifiedTypeIdentity ElementType) : ApiQualifiedTypeIdentity;

    public sealed record FunctionPointer(
        ApiQualifiedMethodSignature Signature) : ApiQualifiedTypeIdentity;

    public sealed record Modified(
        ApiQualifiedTypeIdentity Modifier,
        ApiQualifiedTypeIdentity UnmodifiedType,
        bool IsRequired) : ApiQualifiedTypeIdentity;
}

public sealed record ApiQualifiedMethodSignature(
    byte Header,
    int GenericParameterCount,
    int RequiredParameterCount,
    ApiQualifiedTypeIdentity ReturnType,
    ImmutableArray<ApiQualifiedTypeIdentity> ParameterTypes)
{
    public bool Equals(ApiQualifiedMethodSignature? other)
        => other is not null &&
            Header == other.Header &&
            GenericParameterCount == other.GenericParameterCount &&
            RequiredParameterCount == other.RequiredParameterCount &&
            ReturnType == other.ReturnType &&
            ParameterTypes.AsSpan().SequenceEqual(other.ParameterTypes.AsSpan());

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Header);
        hash.Add(GenericParameterCount);
        hash.Add(RequiredParameterCount);
        hash.Add(ReturnType);

        foreach (ApiQualifiedTypeIdentity parameterType in ParameterTypes)
        {
            hash.Add(parameterType);
        }

        return hash.ToHashCode();
    }
}

public sealed record ApiQualifiedParameter(
    ApiQualifiedTypeIdentity Type,
    bool IsIn,
    bool IsOut);

public sealed record ApiQualifiedTypeDeclaration(
    ApiQualifiedTypeDefinitionIdentity Definition,
    MetadataTypeDeclarationCategory Category);

public abstract record ApiQualifiedMemberDeclaration
{
    private ApiQualifiedMemberDeclaration()
    {
    }

    public abstract ApiDeclarationKind Kind { get; }

    public abstract string RawName { get; }

    public abstract bool IsStatic { get; }

    public sealed record Method(
        string Name,
        bool Static,
        int GenericArity,
        byte Header,
        int RequiredParameterCount,
        ApiQualifiedTypeIdentity ReturnType,
        ImmutableArray<ApiQualifiedParameter> Parameters) : ApiQualifiedMemberDeclaration
    {
        public override ApiDeclarationKind Kind => ApiDeclarationKind.Method;

        public override string RawName => Name;

        public override bool IsStatic => Static;

        public bool Equals(Method? other)
            => other is not null &&
                string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                Static == other.Static &&
                GenericArity == other.GenericArity &&
                Header == other.Header &&
                RequiredParameterCount == other.RequiredParameterCount &&
                ReturnType == other.ReturnType &&
                Parameters.AsSpan().SequenceEqual(other.Parameters.AsSpan());

        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(Name, StringComparer.Ordinal);
            hash.Add(Static);
            hash.Add(GenericArity);
            hash.Add(Header);
            hash.Add(RequiredParameterCount);
            hash.Add(ReturnType);

            foreach (ApiQualifiedParameter parameter in Parameters)
            {
                hash.Add(parameter);
            }

            return hash.ToHashCode();
        }
    }

    public sealed record Property(
        string Name,
        bool Static,
        int GenericArity,
        byte Header,
        int RequiredParameterCount,
        ApiQualifiedTypeIdentity ValueType,
        ImmutableArray<ApiQualifiedParameter> Parameters) : ApiQualifiedMemberDeclaration
    {
        public override ApiDeclarationKind Kind => ApiDeclarationKind.Property;

        public override string RawName => Name;

        public override bool IsStatic => Static;

        public bool Equals(Property? other)
            => other is not null &&
                string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                Static == other.Static &&
                GenericArity == other.GenericArity &&
                Header == other.Header &&
                RequiredParameterCount == other.RequiredParameterCount &&
                ValueType == other.ValueType &&
                Parameters.AsSpan().SequenceEqual(other.Parameters.AsSpan());

        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(Name, StringComparer.Ordinal);
            hash.Add(Static);
            hash.Add(GenericArity);
            hash.Add(Header);
            hash.Add(RequiredParameterCount);
            hash.Add(ValueType);

            foreach (ApiQualifiedParameter parameter in Parameters)
            {
                hash.Add(parameter);
            }

            return hash.ToHashCode();
        }
    }

    public sealed record Event(
        string Name,
        bool Static,
        ApiQualifiedTypeIdentity EventType) : ApiQualifiedMemberDeclaration
    {
        public override ApiDeclarationKind Kind => ApiDeclarationKind.Event;

        public override string RawName => Name;

        public override bool IsStatic => Static;
    }

    public sealed record Field(
        string Name,
        bool Static,
        ApiQualifiedTypeIdentity FieldType) : ApiQualifiedMemberDeclaration
    {
        public override ApiDeclarationKind Kind => ApiDeclarationKind.Field;

        public override string RawName => Name;

        public override bool IsStatic => Static;
    }
}

public abstract record ApiQualifiedAnchor
{
    private ApiQualifiedAnchor(
        ApiQualifiedAnchorFormat format,
        ApiQualifiedAssemblyFamily assemblyFamily)
    {
        Format = format;
        AssemblyFamily = assemblyFamily;
    }

    public ApiQualifiedAnchorFormat Format { get; }

    public ApiQualifiedAssemblyFamily AssemblyFamily { get; }

    public sealed record Type : ApiQualifiedAnchor
    {
        internal Type(
            ApiQualifiedAnchorFormat format,
            ApiQualifiedAssemblyFamily assemblyFamily,
            ApiQualifiedTypeDeclaration declaration)
            : base(format, assemblyFamily)
        {
            Declaration = declaration;
        }

        public ApiQualifiedTypeDeclaration Declaration { get; }
    }

    public sealed record Member : ApiQualifiedAnchor
    {
        internal Member(
            ApiQualifiedAnchorFormat format,
            ApiQualifiedAssemblyFamily assemblyFamily,
            ApiQualifiedTypeDeclaration declaringType,
            ApiQualifiedMemberDeclaration declaration,
            MemberAnchor companionAnchor)
            : base(format, assemblyFamily)
        {
            DeclaringType = declaringType;
            Declaration = declaration;
            CompanionAnchor = companionAnchor;
        }

        public ApiQualifiedTypeDeclaration DeclaringType { get; }

        public ApiQualifiedMemberDeclaration Declaration { get; }

        public MemberAnchor CompanionAnchor { get; }

        public bool Equals(Member? other)
            => other is not null &&
                Format == other.Format &&
                AssemblyFamily == other.AssemblyFamily &&
                DeclaringType == other.DeclaringType &&
                Declaration == other.Declaration;

        public override int GetHashCode()
            => HashCode.Combine(Format, AssemblyFamily, DeclaringType, Declaration);
    }
}

public enum ApiQualifiedAnchorStage
{
    AddressValidation,
    AssemblyFamily,
    DeclarationEvidence,
    NamedTypeResolution,
    Projection,
}

public enum ApiQualifiedAnchorRefusalReason
{
    UnsupportedImage,
    UnsupportedShape,
    ModuleScopedType,
    ResolverRequired,
    UnresolvedType,
    AmbiguousType,
}

public enum ApiQualifiedAnchorFailureReason
{
    InvalidAddress,
    MalformedMetadata,
    InconsistentResolution,
    WorkLimitExceeded,
}

public sealed record ApiQualifiedAnchorRefusal(
    ApiQualifiedAnchorStage Stage,
    ApiQualifiedAnchorRefusalReason Reason,
    string Detail);

public sealed record ApiQualifiedAnchorFailure(
    ApiQualifiedAnchorStage Stage,
    ApiQualifiedAnchorFailureReason Reason,
    string Detail);

public abstract record ApiQualifiedAnchorResult(MetadataOperationCounters Counters)
{
    public sealed record Complete(
        MetadataDeclarationLocation Location,
        ApiQualifiedAnchor Anchor,
        MetadataOperationCounters Counters) : ApiQualifiedAnchorResult(Counters);

    public sealed record Refused(
        ApiQualifiedAnchorRefusal Refusal,
        MetadataOperationCounters Counters) : ApiQualifiedAnchorResult(Counters);

    public sealed record Failed(
        ApiQualifiedAnchorFailure Failure,
        MetadataOperationCounters Counters) : ApiQualifiedAnchorResult(Counters);
}

public interface IApiQualifiedTypeDefinitionResolver
{
    ApiQualifiedTypeDefinitionResolution Resolve(
        MetadataNamedTypeIdentity reference,
        CancellationToken cancellationToken = default);
}

public abstract record ApiQualifiedTypeDefinitionResolution
{
    private ApiQualifiedTypeDefinitionResolution()
    {
    }

    public sealed record Resolved(
        ApiQualifiedTypeDefinitionIdentity Definition) : ApiQualifiedTypeDefinitionResolution;

    public sealed record Refused(
        ApiQualifiedAnchorRefusalReason Reason,
        string Detail) : ApiQualifiedTypeDefinitionResolution;

    public sealed record Failed(
        string Detail) : ApiQualifiedTypeDefinitionResolution;
}
