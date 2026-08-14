using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>The physical metadata location represented by a graph storage key.</summary>
public enum GraphNodeStorageKind
{
    Definition,
    CallSite,
}

/// <summary>
/// Total identity for one physical graph occurrence. It retains evidence even
/// when catalog correspondence cannot be established.
/// </summary>
public sealed class GraphNodeStorageKey : IEquatable<GraphNodeStorageKey>
{
    readonly AssemblyAcquisitionRegistration _source;

    GraphNodeStorageKey(
        AssemblyAcquisitionRegistration source,
        AssemblyReferenceIdentity assemblyIdentity,
        Guid moduleVersionId,
        GraphNodeStorageKind kind,
        int methodToken,
        int ilOffset,
        int operandToken)
    {
        _source = source;
        AssemblyIdentity = assemblyIdentity;
        ModuleVersionId = moduleVersionId;
        Kind = kind;
        MethodToken = methodToken;
        ILOffset = ilOffset;
        OperandToken = operandToken;
    }

    internal AssemblyReferenceIdentity AssemblyIdentity { get; }
    public Guid ModuleVersionId { get; }
    public GraphNodeStorageKind Kind { get; }
    public int MethodToken { get; }
    public int ILOffset { get; }
    public int OperandToken { get; }

    internal static GraphNodeStorageKey Definition(
        ResolvedAssemblyReference source,
        Guid moduleVersionId,
        int methodToken) =>
        new(
            source.Registration,
            source.Identity,
            moduleVersionId,
            GraphNodeStorageKind.Definition,
            methodToken,
            ilOffset: -1,
            operandToken: 0);

    internal static GraphNodeStorageKey CallSite(
        ResolvedAssemblyReference source,
        Guid moduleVersionId,
        DirectCall call) =>
        new(
            source.Registration,
            source.Identity,
            moduleVersionId,
            GraphNodeStorageKind.CallSite,
            call.Caller.MetadataToken,
            call.ILOffset,
            call.OperandToken);

    public bool Equals(GraphNodeStorageKey? other) =>
        other is not null
        && ReferenceEquals(_source, other._source)
        && ModuleVersionId == other.ModuleVersionId
        && Kind == other.Kind
        && MethodToken == other.MethodToken
        && ILOffset == other.ILOffset
        && OperandToken == other.OperandToken;

    public override bool Equals(object? obj) =>
        obj is GraphNodeStorageKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            RuntimeHelpers.GetHashCode(_source),
            ModuleVersionId,
            Kind,
            MethodToken,
            ILOffset,
            OperandToken);
}

/// <summary>The identity domain used to collapse graph occurrences.</summary>
public enum GraphNodeIdentityKind
{
    Storage = 0,
    CatalogCorrespondence = 1,
    Structural = 2,
    ArtifactMember = 3,
    DetachedCatalog = 4,
}

/// <summary>
/// Analysis-owned graph identity. Catalog correspondence is preferred when
/// available; physical storage identity keeps incomplete occurrences distinct.
/// </summary>
public sealed class GraphNodeIdentity : IEquatable<GraphNodeIdentity>
{
    readonly object _value;

    GraphNodeIdentity(GraphNodeIdentityKind kind, object value)
    {
        Kind = kind;
        _value = value;
    }

    public GraphNodeIdentityKind Kind { get; }

    /// <summary>
    /// Whether this identity contains no catalog generation or live acquisition
    /// registration.
    /// </summary>
    public bool IsPortable =>
        Kind is GraphNodeIdentityKind.Structural
            or GraphNodeIdentityKind.ArtifactMember
            or GraphNodeIdentityKind.DetachedCatalog;

    internal static GraphNodeIdentity FromStorage(
        GraphNodeStorageKey storage) =>
        new(GraphNodeIdentityKind.Storage, storage);

    internal static GraphNodeIdentity FromCorrespondence(
        CatalogMemberJoinKey correspondence) =>
        new(GraphNodeIdentityKind.CatalogCorrespondence, correspondence);

    internal static GraphNodeIdentity FromArtifactMember(
        GraphNodeStorageKey definition)
    {
        if (definition.Kind != GraphNodeStorageKind.Definition)
        {
            throw new ArgumentException(
                "Artifact-member identity requires definition storage.",
                nameof(definition));
        }

        return new(
            GraphNodeIdentityKind.ArtifactMember,
            new ArtifactMemberKey(
                definition.AssemblyIdentity,
                definition.ModuleVersionId,
                definition.MethodToken));
    }

    /// <summary>
    /// Creates a document-local identity for evidence that cannot safely join
    /// another occurrence.
    /// </summary>
    public static GraphNodeIdentity CreateDocumentLocal() =>
        new(GraphNodeIdentityKind.DetachedCatalog, new object());

    /// <summary>Creates a structural identity for an unbound member value.</summary>
    public static GraphNodeIdentity FromMember(MemberRef member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return new(
            GraphNodeIdentityKind.Structural,
            GraphStructuralMemberKey.Create(member));
    }

    public bool Equals(GraphNodeIdentity? other) =>
        other is not null
        && Kind == other.Kind
        && _value.Equals(other._value);

    public override bool Equals(object? obj) =>
        obj is GraphNodeIdentity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, _value);

    public static bool operator ==(
        GraphNodeIdentity? left,
        GraphNodeIdentity? right) =>
        Equals(left, right);

    public static bool operator !=(
        GraphNodeIdentity? left,
        GraphNodeIdentity? right) =>
        !Equals(left, right);

    sealed record ArtifactMemberKey(
        AssemblyReferenceIdentity AssemblyIdentity,
        Guid ModuleVersionId,
        int MethodToken);
}

/// <summary>How strongly a graph occurrence corresponds to other occurrences.</summary>
public enum GraphCorrespondenceKind
{
    Local,
    Exact,
    Indeterminate,
    Incomplete,
}

/// <summary>
/// Physical graph evidence plus the optional catalog projection that supplied
/// its logical identity.
/// </summary>
public sealed class GraphNodeEvidence
{
    internal GraphNodeEvidence(
        GraphNodeStorageKey storage,
        GraphNodeIdentity identity,
        CatalogMemberJoinProjection? correspondence)
    {
        Storage = storage;
        Identity = identity;
        Correspondence = correspondence;
    }

    public GraphNodeStorageKey Storage { get; }
    public GraphNodeIdentity Identity { get; }
    public CatalogMemberJoinProjection? Correspondence { get; }

    public GraphCorrespondenceKind Kind => Correspondence switch
    {
        CatalogMemberJoinProjection.Issued issued
            when issued.Evidence.Any(static evidence =>
                evidence
                    is MemberCorrespondenceEvidence.UnresolvedBinding
                    {
                        Outcome: TypeResolutionOutcome.Unavailable,
                    }) =>
            GraphCorrespondenceKind.Incomplete,
        CatalogMemberJoinProjection.Issued issued
            when issued.Key.Kind == CatalogMemberCorrespondenceKind.Exact =>
            GraphCorrespondenceKind.Exact,
        CatalogMemberJoinProjection.Issued =>
            GraphCorrespondenceKind.Indeterminate,
        CatalogMemberJoinProjection.Incomplete =>
            GraphCorrespondenceKind.Incomplete,
        _ => GraphCorrespondenceKind.Local,
    };
}

sealed class GraphStructuralMemberKey : IEquatable<GraphStructuralMemberKey>
{
    GraphStructuralMemberKey(
        GraphStructuralTypeShape declaringType,
        string name,
        MemberKind memberKind,
        int genericArity,
        bool hasThis,
        byte signatureHeader,
        int requiredParameterCount,
        ImmutableArray<GraphStructuralTypeShape> parameterTypes,
        GraphStructuralTypeShape returnType)
    {
        DeclaringType = declaringType;
        Name = name;
        MemberKind = memberKind;
        GenericArity = genericArity;
        HasThis = hasThis;
        SignatureHeader = signatureHeader;
        RequiredParameterCount = requiredParameterCount;
        ParameterTypes = parameterTypes;
        ReturnType = returnType;
    }

    GraphStructuralTypeShape DeclaringType { get; }
    string Name { get; }
    MemberKind MemberKind { get; }
    int GenericArity { get; }
    bool HasThis { get; }
    byte SignatureHeader { get; }
    int RequiredParameterCount { get; }
    ImmutableArray<GraphStructuralTypeShape> ParameterTypes { get; }
    GraphStructuralTypeShape ReturnType { get; }

    internal static GraphStructuralMemberKey Create(MemberRef member)
    {
        const byte CallingConventionMask = 0x0F;
        const byte VarargCallingConvention = 0x05;
        const byte Generic = 0x10;
        const byte Instance = 0x20;
        const byte ExplicitThis = 0x40;

        bool erase = GenericMemberIdentity.ShouldErase(
            member.DeclaringType,
            member.ParameterTypes,
            member.ReturnType,
            member.TypeArguments);
        ImmutableArray<TypeRef> parameters = erase
            ? member.OpenSignatureParameters
            : member.ParameterTypes;
        int required = parameters.Length;
        if ((member.SignatureHeader & CallingConventionMask)
            == VarargCallingConvention
            && member.RequiredParameterCount >= 0
            && member.RequiredParameterCount <= parameters.Length)
        {
            required = member.RequiredParameterCount;
            parameters = parameters[..required];
        }

        byte header = (byte)(
            member.SignatureHeader
            & (CallingConventionMask | ExplicitThis));
        if (member.GenericArity > 0)
            header |= Generic;
        if (member.HasThis)
            header |= Instance;

        return new GraphStructuralMemberKey(
            GraphStructuralTypeShape.Create(
                GenericMemberIdentity.OpenDeclaringType(
                    member.DeclaringType)),
            member.Name,
            member.Kind,
            member.GenericArity,
            member.HasThis,
            header,
            required,
            [.. parameters.Select(GraphStructuralTypeShape.Create)],
            GraphStructuralTypeShape.Create(member.OpenSignatureReturn));
    }

    public bool Equals(GraphStructuralMemberKey? other)
    {
        if (other is null
            || DeclaringType != other.DeclaringType
            || !string.Equals(Name, other.Name, StringComparison.Ordinal)
            || MemberKind != other.MemberKind
            || GenericArity != other.GenericArity
            || HasThis != other.HasThis
            || SignatureHeader != other.SignatureHeader
            || RequiredParameterCount != other.RequiredParameterCount
            || ReturnType != other.ReturnType
            || ParameterTypes.Length != other.ParameterTypes.Length)
        {
            return false;
        }

        for (int i = 0; i < ParameterTypes.Length; i++)
        {
            if (ParameterTypes[i] != other.ParameterTypes[i])
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is GraphStructuralMemberKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeclaringType);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(MemberKind);
        hash.Add(GenericArity);
        hash.Add(HasThis);
        hash.Add(SignatureHeader);
        hash.Add(RequiredParameterCount);
        foreach (GraphStructuralTypeShape parameter in ParameterTypes)
            hash.Add(parameter);
        hash.Add(ReturnType);
        return hash.ToHashCode();
    }
}

sealed class GraphStructuralTypeShape : IEquatable<GraphStructuralTypeShape>
{
    const int MaxDepth = 256;
    const byte CallingConventionMask = 0x0F;
    const byte VarargCallingConvention = 0x05;

    GraphStructuralTypeShape(
        TypeRefKind kind,
        string assembly,
        string @namespace,
        string name,
        int rank,
        int genericParameterIndex,
        string unsupportedReason,
        byte signatureHeader,
        int genericArity,
        int requiredParameterCount,
        bool isRequiredModifier,
        GraphStructuralTypeShape? elementType,
        ImmutableArray<GraphStructuralTypeShape> components)
    {
        Kind = kind;
        Assembly = assembly;
        Namespace = @namespace;
        Name = name;
        Rank = rank;
        GenericParameterIndex = genericParameterIndex;
        UnsupportedReason = unsupportedReason;
        SignatureHeader = signatureHeader;
        GenericArity = genericArity;
        RequiredParameterCount = requiredParameterCount;
        IsRequiredModifier = isRequiredModifier;
        ElementType = elementType;
        Components = components;
    }

    TypeRefKind Kind { get; }
    string Assembly { get; }
    string Namespace { get; }
    string Name { get; }
    int Rank { get; }
    int GenericParameterIndex { get; }
    string UnsupportedReason { get; }
    byte SignatureHeader { get; }
    int GenericArity { get; }
    int RequiredParameterCount { get; }
    bool IsRequiredModifier { get; }
    GraphStructuralTypeShape? ElementType { get; }
    ImmutableArray<GraphStructuralTypeShape> Components { get; }

    internal static GraphStructuralTypeShape Create(TypeRef type) =>
        Create(type, depth: 0);

    static GraphStructuralTypeShape Create(TypeRef type, int depth)
    {
        if (depth >= MaxDepth)
        {
            return new(
                TypeRefKind.Unsupported,
                "",
                "",
                "",
                0,
                -1,
                "shape depth exceeded",
                0,
                0,
                0,
                false,
                null,
                []);
        }

        GraphStructuralTypeShape? element;
        ImmutableArray<GraphStructuralTypeShape> components;
        byte signatureHeader = 0;
        int genericArity = 0;
        int requiredParameterCount = 0;
        bool isRequiredModifier = false;
        if (type.FunctionPointerSignature is { } signature)
        {
            element = Create(signature.ReturnType, depth + 1);
            components = signature.ParameterTypes.IsDefault
                ? []
                : [.. signature.ParameterTypes.Select(
                    parameter => Create(parameter, depth + 1))];
            signatureHeader = signature.Header.RawValue;
            genericArity = signature.GenericParameterCount;
            requiredParameterCount =
                (signature.Header.RawValue & CallingConventionMask)
                    == VarargCallingConvention
                        ? signature.RequiredParameterCount
                        : signature.ParameterTypes.Length;
        }
        else if (type.ModifierType is not null
            && type.UnmodifiedType is not null)
        {
            element = Create(type.UnmodifiedType, depth + 1);
            components = [Create(type.ModifierType, depth + 1)];
            isRequiredModifier = type.IsRequiredModifier;
        }
        else
        {
            element = type.ElementType is { } elementType
                ? Create(elementType, depth + 1)
                : null;
            components = [.. type.TypeArguments.Select(
                argument => Create(argument, depth + 1))];
        }

        return new(
            type.Kind,
            type.Assembly,
            type.Namespace,
            type.Name,
            type.Rank,
            type.GenericParameterIndex,
            type.UnsupportedReason,
            signatureHeader,
            genericArity,
            requiredParameterCount,
            isRequiredModifier,
            element,
            components);
    }

    public bool Equals(GraphStructuralTypeShape? other)
    {
        if (other is null
            || Kind != other.Kind
            || !string.Equals(Assembly, other.Assembly, StringComparison.Ordinal)
            || !string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
            || !string.Equals(Name, other.Name, StringComparison.Ordinal)
            || Rank != other.Rank
            || GenericParameterIndex != other.GenericParameterIndex
            || !string.Equals(
                UnsupportedReason,
                other.UnsupportedReason,
                StringComparison.Ordinal)
            || SignatureHeader != other.SignatureHeader
            || GenericArity != other.GenericArity
            || RequiredParameterCount != other.RequiredParameterCount
            || IsRequiredModifier != other.IsRequiredModifier
            || ElementType != other.ElementType
            || Components.Length != other.Components.Length)
        {
            return false;
        }

        for (int i = 0; i < Components.Length; i++)
        {
            if (Components[i] != other.Components[i])
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is GraphStructuralTypeShape other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Assembly, StringComparer.Ordinal);
        hash.Add(Namespace, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Rank);
        hash.Add(GenericParameterIndex);
        hash.Add(UnsupportedReason, StringComparer.Ordinal);
        hash.Add(SignatureHeader);
        hash.Add(GenericArity);
        hash.Add(RequiredParameterCount);
        hash.Add(IsRequiredModifier);
        hash.Add(ElementType);
        foreach (GraphStructuralTypeShape component in Components)
            hash.Add(component);
        return hash.ToHashCode();
    }

    public static bool operator ==(
        GraphStructuralTypeShape? left,
        GraphStructuralTypeShape? right) =>
        Equals(left, right);

    public static bool operator !=(
        GraphStructuralTypeShape? left,
        GraphStructuralTypeShape? right) =>
        !Equals(left, right);
}
