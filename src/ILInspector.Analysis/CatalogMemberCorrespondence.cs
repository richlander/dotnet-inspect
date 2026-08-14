using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

public enum CatalogMemberCorrespondenceKind
{
    Exact,
    Indeterminate,
}

public enum CatalogTypeShapeKind
{
    Definition,
    DegradedDefinition,
    GenericInstance,
    SzArray,
    Array,
    ByRef,
    Pointer,
    Pinned,
    Modified,
    FunctionPointer,
    GenericParameter,
    MethodGenericParameter,
}

/// <summary>
/// Hashable open-signature shape whose named leaves contain only
/// catalog-issued correspondence currency.
/// </summary>
public sealed class CatalogTypeShape : IEquatable<CatalogTypeShape>
{
    CatalogTypeShape(
        CatalogTypeShapeKind kind,
        DefinitionJoinToken? definition = null,
        UnresolvedBindingKey? unresolvedBinding = null,
        MetadataTypeDefinitionName? typeName = null,
        CatalogTypeShape? elementType = null,
        ImmutableArray<CatalogTypeShape> components = default,
        int rank = 0,
        int genericParameterIndex = -1,
        bool isRequiredModifier = false,
        byte signatureHeader = 0,
        int genericArity = 0,
        int requiredParameterCount = 0)
    {
        Kind = kind;
        Definition = definition;
        UnresolvedBinding = unresolvedBinding;
        TypeName = typeName;
        ElementType = elementType;
        Components = components.IsDefault ? [] : components;
        Rank = rank;
        GenericParameterIndex = genericParameterIndex;
        IsRequiredModifier = isRequiredModifier;
        SignatureHeader = signatureHeader;
        GenericArity = genericArity;
        RequiredParameterCount = requiredParameterCount;
    }

    public CatalogTypeShapeKind Kind { get; }
    public DefinitionJoinToken? Definition { get; }
    public UnresolvedBindingKey? UnresolvedBinding { get; }
    public MetadataTypeDefinitionName? TypeName { get; }
    public CatalogTypeShape? ElementType { get; }
    public ImmutableArray<CatalogTypeShape> Components { get; }
    public int Rank { get; }
    public int GenericParameterIndex { get; }
    public bool IsRequiredModifier { get; }
    public byte SignatureHeader { get; }
    public int GenericArity { get; }
    public int RequiredParameterCount { get; }

    internal static CatalogTypeShape Resolved(
        DefinitionJoinToken token) =>
        new(
            CatalogTypeShapeKind.Definition,
            definition: token);

    internal static CatalogTypeShape Degraded(
        UnresolvedBindingKey binding,
        MetadataTypeDefinitionName typeName) =>
        new(
            CatalogTypeShapeKind.DegradedDefinition,
            unresolvedBinding: binding,
            typeName: typeName);

    internal static CatalogTypeShape Unary(
        CatalogTypeShapeKind kind,
        CatalogTypeShape element,
        int rank = 0) =>
        new(kind, elementType: element, rank: rank);

    internal static CatalogTypeShape GenericInstance(
        CatalogTypeShape definition,
        ImmutableArray<CatalogTypeShape> arguments) =>
        new(
            CatalogTypeShapeKind.GenericInstance,
            elementType: definition,
            components: arguments);

    internal static CatalogTypeShape Modified(
        CatalogTypeShape modifier,
        CatalogTypeShape unmodified,
        bool isRequired) =>
        new(
            CatalogTypeShapeKind.Modified,
            elementType: unmodified,
            components: [modifier],
            isRequiredModifier: isRequired);

    internal static CatalogTypeShape FunctionPointer(
        byte signatureHeader,
        int genericArity,
        int requiredParameterCount,
        CatalogTypeShape returnType,
        ImmutableArray<CatalogTypeShape> parameterTypes) =>
        new(
            CatalogTypeShapeKind.FunctionPointer,
            elementType: returnType,
            components: parameterTypes,
            signatureHeader: signatureHeader,
            genericArity: genericArity,
            requiredParameterCount: requiredParameterCount);

    internal static CatalogTypeShape GenericParameter(
        bool method,
        int index) =>
        new(
            method
                ? CatalogTypeShapeKind.MethodGenericParameter
                : CatalogTypeShapeKind.GenericParameter,
            genericParameterIndex: index);

    public bool Equals(CatalogTypeShape? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || Kind != other.Kind
            || Definition != other.Definition
            || UnresolvedBinding != other.UnresolvedBinding
            || TypeName != other.TypeName
            || !Equals(ElementType, other.ElementType)
            || Rank != other.Rank
            || GenericParameterIndex != other.GenericParameterIndex
            || IsRequiredModifier != other.IsRequiredModifier
            || SignatureHeader != other.SignatureHeader
            || GenericArity != other.GenericArity
            || RequiredParameterCount != other.RequiredParameterCount
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
        obj is CatalogTypeShape other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Definition);
        hash.Add(UnresolvedBinding);
        hash.Add(TypeName);
        hash.Add(ElementType);
        hash.Add(Rank);
        hash.Add(GenericParameterIndex);
        hash.Add(IsRequiredModifier);
        hash.Add(SignatureHeader);
        hash.Add(GenericArity);
        hash.Add(RequiredParameterCount);
        foreach (CatalogTypeShape component in Components)
            hash.Add(component);
        return hash.ToHashCode();
    }

    public static bool operator ==(
        CatalogTypeShape? left,
        CatalogTypeShape? right) =>
        Equals(left, right);

    public static bool operator !=(
        CatalogTypeShape? left,
        CatalogTypeShape? right) =>
        !Equals(left, right);
}

/// <summary>
/// Generation-scoped member correspondence over one open metadata signature.
/// </summary>
public sealed class CatalogMemberJoinKey
    : IEquatable<CatalogMemberJoinKey>
{
    internal CatalogMemberJoinKey(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        CatalogMemberCorrespondenceKind kind,
        CatalogTypeShape declaringType,
        string name,
        MemberKind memberKind,
        int genericArity,
        bool hasThis,
        byte signatureHeader,
        int requiredParameterCount,
        ImmutableArray<CatalogTypeShape> parameterTypes,
        CatalogTypeShape returnType)
    {
        Catalog = catalog;
        Generation = generation;
        Kind = kind;
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

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public CatalogMemberCorrespondenceKind Kind { get; }
    public CatalogTypeShape DeclaringType { get; }
    public string Name { get; }
    public MemberKind MemberKind { get; }
    public int GenericArity { get; }
    public bool HasThis { get; }
    public byte SignatureHeader { get; }
    public int RequiredParameterCount { get; }
    public ImmutableArray<CatalogTypeShape> ParameterTypes { get; }
    public CatalogTypeShape ReturnType { get; }

    public bool Equals(CatalogMemberJoinKey? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || Catalog != other.Catalog
            || !ReferenceEquals(Generation, other.Generation)
            || Kind != other.Kind
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
        obj is CatalogMemberJoinKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Catalog);
        hash.Add(Generation);
        hash.Add(Kind);
        hash.Add(DeclaringType);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(MemberKind);
        hash.Add(GenericArity);
        hash.Add(HasThis);
        hash.Add(SignatureHeader);
        hash.Add(RequiredParameterCount);
        foreach (CatalogTypeShape parameter in ParameterTypes)
            hash.Add(parameter);
        hash.Add(ReturnType);
        return hash.ToHashCode();
    }

    public static bool operator ==(
        CatalogMemberJoinKey? left,
        CatalogMemberJoinKey? right) =>
        Equals(left, right);

    public static bool operator !=(
        CatalogMemberJoinKey? left,
        CatalogMemberJoinKey? right) =>
        !Equals(left, right);
}

public abstract class MemberCorrespondenceEvidence
{
    private protected MemberCorrespondenceEvidence()
    {
    }

    public sealed class DuplicateArtifact : MemberCorrespondenceEvidence
    {
        internal DuplicateArtifact(
            MetadataTypeDefinitionName type,
            DuplicateArtifactEvidence evidence)
        {
            Type = type;
            Evidence = evidence;
        }

        public MetadataTypeDefinitionName Type { get; }
        public DuplicateArtifactEvidence Evidence { get; }
    }

    public sealed class UnresolvedBinding : MemberCorrespondenceEvidence
    {
        internal UnresolvedBinding(
            MetadataTypeDefinitionName type,
            TypeResolutionOutcome outcome)
        {
            Type = type;
            Outcome = outcome;
        }

        public MetadataTypeDefinitionName Type { get; }
        public TypeResolutionOutcome Outcome { get; }
    }
}

public abstract class MemberCorrespondenceFailure
{
    private protected MemberCorrespondenceFailure()
    {
    }

    public sealed class SourceMismatch : MemberCorrespondenceFailure
    {
        internal SourceMismatch(string memberAssembly, string sourceAssembly)
        {
            MemberAssembly = memberAssembly;
            SourceAssembly = sourceAssembly;
        }

        public string MemberAssembly { get; }
        public string SourceAssembly { get; }
    }

    public sealed class OpenSignatureUnavailable
        : MemberCorrespondenceFailure
    {
        internal OpenSignatureUnavailable()
        {
        }
    }

    public sealed class InvalidRequiredParameterCount
        : MemberCorrespondenceFailure
    {
        internal InvalidRequiredParameterCount(
            int requiredParameterCount,
            int parameterCount)
        {
            RequiredParameterCount = requiredParameterCount;
            ParameterCount = parameterCount;
        }

        public int RequiredParameterCount { get; }
        public int ParameterCount { get; }
    }

    public sealed class MissingResolutionProvenance
        : MemberCorrespondenceFailure
    {
        internal MissingResolutionProvenance(TypeRef type) => Type = type;
        public TypeRef Type { get; }
    }

    public sealed class UnsupportedTypeShape
        : MemberCorrespondenceFailure
    {
        internal UnsupportedTypeShape(TypeRef type) => Type = type;
        public TypeRef Type { get; }
    }

    public sealed class MalformedTypeShape : MemberCorrespondenceFailure
    {
        internal MalformedTypeShape(TypeRef type, string reason)
        {
            Type = type;
            Reason = reason;
        }

        public TypeRef Type { get; }
        public string Reason { get; }
    }

    public sealed class ShapeDepthExceeded : MemberCorrespondenceFailure
    {
        internal ShapeDepthExceeded(int limit) => Limit = limit;
        public int Limit { get; }
    }

    public sealed class Resolution : MemberCorrespondenceFailure
    {
        internal Resolution(TypeResolutionOutcome outcome) =>
            Outcome = outcome;
        public TypeResolutionOutcome Outcome { get; }
    }

    public sealed class ExpansionRequired : MemberCorrespondenceFailure
    {
        internal ExpansionRequired(ResolutionPlanRequest request) =>
            Request = request;
        public ResolutionPlanRequest Request { get; }
    }

    public sealed class StaleGeneration : MemberCorrespondenceFailure
    {
        internal StaleGeneration(
            AssemblyCatalogGenerationId currencyGeneration,
            AssemblyCatalogGenerationId currentGeneration)
        {
            CurrencyGeneration = currencyGeneration;
            CurrentGeneration = currentGeneration;
        }

        public AssemblyCatalogGenerationId CurrencyGeneration { get; }
        public AssemblyCatalogGenerationId CurrentGeneration { get; }
    }
}

public abstract class CatalogMemberJoinProjection
{
    private protected CatalogMemberJoinProjection()
    {
    }

    public sealed class Issued : CatalogMemberJoinProjection
    {
        internal Issued(
            CatalogMemberJoinKey key,
            ImmutableArray<MemberCorrespondenceEvidence> evidence)
        {
            Key = key;
            Evidence = evidence;
        }

        public CatalogMemberJoinKey Key { get; }
        public ImmutableArray<MemberCorrespondenceEvidence> Evidence { get; }
    }

    public sealed class Incomplete : CatalogMemberJoinProjection
    {
        internal Incomplete(
            ImmutableArray<MemberCorrespondenceFailure> failures) =>
            Failures = failures;

        public ImmutableArray<MemberCorrespondenceFailure> Failures { get; }
    }
}

/// <summary>
/// One reusable open-signature recipe. Its distinct requests may be unioned
/// into a frozen context before projecting the recipe into join currency.
/// </summary>
public sealed class CatalogMemberCorrespondencePlan
{
    const byte CallingConventionMask = 0x0F;
    const byte VarargCallingConvention = 0x05;

    readonly PlannedType _declaringType;
    readonly string _name;
    readonly MemberKind _memberKind;
    readonly int _genericArity;
    readonly bool _hasThis;
    readonly byte _signatureHeader;
    readonly int _requiredParameterCount;
    readonly ImmutableArray<PlannedType> _parameterTypes;
    readonly PlannedType _returnType;
    readonly ImmutableArray<MemberCorrespondenceFailure> _structuralFailures;

    CatalogMemberCorrespondencePlan(
        PlannedType declaringType,
        string name,
        MemberKind memberKind,
        int genericArity,
        bool hasThis,
        byte signatureHeader,
        int requiredParameterCount,
        ImmutableArray<PlannedType> parameterTypes,
        PlannedType returnType,
        ImmutableArray<TypeResolutionRequest> requests,
        ImmutableArray<MemberCorrespondenceFailure> structuralFailures)
    {
        _declaringType = declaringType;
        _name = name;
        _memberKind = memberKind;
        _genericArity = genericArity;
        _hasThis = hasThis;
        _signatureHeader = signatureHeader;
        _requiredParameterCount = requiredParameterCount;
        _parameterTypes = parameterTypes;
        _returnType = returnType;
        Requests = requests;
        _structuralFailures = structuralFailures;
    }

    public ImmutableArray<TypeResolutionRequest> Requests { get; }
    public bool IsStructurallyComplete => _structuralFailures.IsEmpty;

    public static CatalogMemberCorrespondencePlan Create(
        ResolvedAssemblyReference source,
        MethodIdentity member)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(member);

        var initialFailures =
            ImmutableArray.CreateBuilder<MemberCorrespondenceFailure>();
        if (!string.Equals(
                source.Identity.Name,
                member.AssemblyName,
                StringComparison.OrdinalIgnoreCase))
        {
            initialFailures.Add(
                new MemberCorrespondenceFailure.SourceMismatch(
                    member.AssemblyName,
                    source.Identity.Name));
        }

        return CreateCore(
            source,
            GenericMemberIdentity.OpenDeclaringType(member.DeclaringType),
            member.Name,
            member.Name is ".ctor" or ".cctor"
                ? MemberKind.Constructor
                : MemberKind.Method,
            member.GenericArity,
            hasThis: !member.IsStatic,
            CanonicalSignatureHeader(
                member.SignatureHeader,
                hasThis: !member.IsStatic,
                member.GenericArity),
            member.RequiredParameterCount,
            member.ParameterTypes,
            member.ReturnType,
            initialFailures);
    }

    public static CatalogMemberCorrespondencePlan Create(
        ResolvedAssemblyReference source,
        MemberRef member)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(member);

        var initialFailures =
            ImmutableArray.CreateBuilder<MemberCorrespondenceFailure>();
        bool requiresOpenSignature =
            GenericMemberIdentity.IsGenericType(member.DeclaringType)
            || member.GenericArity > 0
            || member.TypeArguments.Length > 0
            || GenericMemberIdentity.ContainsGenericParameter(
                member.ReturnType)
            || member.ParameterTypes.Any(
                GenericMemberIdentity.ContainsGenericParameter);
        bool retainedOpenSignatureIsIncomplete =
            member.OpenReturnType is not null
                ? member.OpenParameterTypes.Length
                    != member.ParameterTypes.Length
                : !member.OpenParameterTypes.IsEmpty;
        bool methodArgumentsAreMalformed =
            !member.TypeArguments.IsEmpty
            && member.TypeArguments.Length != member.GenericArity;
        if ((requiresOpenSignature && member.OpenReturnType is null)
            || retainedOpenSignatureIsIncomplete
            || methodArgumentsAreMalformed)
        {
            initialFailures.Add(
                new MemberCorrespondenceFailure.OpenSignatureUnavailable());
        }

        ImmutableArray<TypeRef> parameters =
            member.OpenSignatureParameters;
        TypeRef returnType = member.OpenSignatureReturn;
        return CreateCore(
            source,
            GenericMemberIdentity.OpenDeclaringType(member.DeclaringType),
            member.Name,
            member.Kind,
            member.GenericArity,
            member.HasThis,
            CanonicalSignatureHeader(
                member.SignatureHeader,
                member.HasThis,
                member.GenericArity),
            member.RequiredParameterCount,
            parameters,
            returnType,
            initialFailures);
    }

    public CatalogMemberJoinProjection Project(
        TypeResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_structuralFailures.IsEmpty)
        {
            return new CatalogMemberJoinProjection.Incomplete(
                _structuralFailures);
        }

        TypeResolutionOutcome[] outcomes = Requests
            .Select(context.Resolve)
            .ToArray();
        var projection = new CatalogMemberJoinProjector(
            context,
            outcomes);
        CatalogTypeShape? declaring =
            projection.Project(_declaringType);
        var parameters =
            ImmutableArray.CreateBuilder<CatalogTypeShape>(
                _parameterTypes.Length);
        foreach (PlannedType parameter in _parameterTypes)
        {
            CatalogTypeShape? shape = projection.Project(parameter);
            if (shape is not null)
                parameters.Add(shape);
        }

        CatalogTypeShape? returnType =
            projection.Project(_returnType);
        if (projection.Failures.Count > 0
            || declaring is null
            || returnType is null
            || parameters.Count != _parameterTypes.Length)
        {
            return new CatalogMemberJoinProjection.Incomplete(
                projection.Failures.ToImmutable());
        }

        CatalogMemberCorrespondenceKind kind =
            projection.Evidence.Count == 0
                ? CatalogMemberCorrespondenceKind.Exact
                : CatalogMemberCorrespondenceKind.Indeterminate;
        var key = new CatalogMemberJoinKey(
            context.Catalog,
            context.Generation,
            kind,
            declaring,
            _name,
            _memberKind,
            _genericArity,
            _hasThis,
            _signatureHeader,
            _requiredParameterCount,
            parameters.MoveToImmutable(),
            returnType);
        return new CatalogMemberJoinProjection.Issued(
            key,
            projection.Evidence.ToImmutable());
    }

    /// <summary>
    /// Compares two complete projections. When resolution is unavailable on
    /// either side, exact assembly-reference or intrinsic-core-library
    /// contracts retain the metadata-level correspondence without collapsing
    /// distinct reference identities or type names. A separately established
    /// type correspondence may also vouch for repeated occurrences of that
    /// exact request pair elsewhere in the signature.
    /// </summary>
    internal bool CorrespondsTo(
        CatalogMemberCorrespondencePlan other,
        CatalogMemberJoinProjection.Issued projection,
        CatalogMemberJoinProjection.Issued otherProjection,
        TypeResolutionRequest? correspondingTypeRequest = null,
        TypeResolutionRequest? otherCorrespondingTypeRequest = null)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(otherProjection);

        CatalogMemberJoinKey key = projection.Key;
        CatalogMemberJoinKey otherKey = otherProjection.Key;
        if (key.Equals(otherKey))
            return true;
        if (key.Catalog != otherKey.Catalog
            || !ReferenceEquals(key.Generation, otherKey.Generation)
            || !string.Equals(key.Name, otherKey.Name, StringComparison.Ordinal)
            || key.MemberKind != otherKey.MemberKind
            || key.GenericArity != otherKey.GenericArity
            || key.HasThis != otherKey.HasThis
            || key.SignatureHeader != otherKey.SignatureHeader
            || key.RequiredParameterCount != otherKey.RequiredParameterCount
            || key.ParameterTypes.Length != otherKey.ParameterTypes.Length)
        {
            return false;
        }

        bool usedUnresolvedContract = false;
        if (!CompatibleType(
                _declaringType,
                key.DeclaringType,
                other,
                other._declaringType,
                otherKey.DeclaringType,
                correspondingTypeRequest,
                otherCorrespondingTypeRequest,
                ref usedUnresolvedContract)
            || !CompatibleType(
                _returnType,
                key.ReturnType,
                other,
                other._returnType,
                otherKey.ReturnType,
                correspondingTypeRequest,
                otherCorrespondingTypeRequest,
                ref usedUnresolvedContract))
        {
            return false;
        }

        for (int i = 0; i < _parameterTypes.Length; i++)
        {
            if (!CompatibleType(
                    _parameterTypes[i],
                    key.ParameterTypes[i],
                    other,
                    other._parameterTypes[i],
                    otherKey.ParameterTypes[i],
                    correspondingTypeRequest,
                    otherCorrespondingTypeRequest,
                    ref usedUnresolvedContract))
            {
                return false;
            }
        }

        return key.Kind == otherKey.Kind || usedUnresolvedContract;
    }

    bool CompatibleType(
        PlannedType planned,
        CatalogTypeShape shape,
        CatalogMemberCorrespondencePlan other,
        PlannedType otherPlanned,
        CatalogTypeShape otherShape,
        TypeResolutionRequest? correspondingTypeRequest,
        TypeResolutionRequest? otherCorrespondingTypeRequest,
        ref bool usedUnresolvedContract)
    {
        if (shape.Equals(otherShape))
            return true;
        if (planned.Kind != otherPlanned.Kind)
            return false;

        switch (planned.Kind)
        {
            case PlannedTypeKind.Named:
                bool equivalent =
                    (shape.Kind == CatalogTypeShapeKind.DegradedDefinition
                        || otherShape.Kind
                            == CatalogTypeShapeKind.DegradedDefinition)
                    && (EquivalentUnresolvedContract(
                            Requests[planned.RequestIndex],
                            other.Requests[otherPlanned.RequestIndex])
                        || MatchesEstablishedCorrespondence(
                            Requests[planned.RequestIndex],
                            correspondingTypeRequest,
                            other.Requests[otherPlanned.RequestIndex],
                            otherCorrespondingTypeRequest));
                usedUnresolvedContract |= equivalent;
                return equivalent;
            case PlannedTypeKind.GenericInstance:
                return CompatibleType(
                        planned.ElementType!,
                        shape.ElementType!,
                        other,
                        otherPlanned.ElementType!,
                        otherShape.ElementType!,
                        correspondingTypeRequest,
                        otherCorrespondingTypeRequest,
                        ref usedUnresolvedContract)
                    && CompatibleMany(
                        planned.Components,
                        shape.Components,
                        other,
                        otherPlanned.Components,
                        otherShape.Components,
                        correspondingTypeRequest,
                        otherCorrespondingTypeRequest,
                        ref usedUnresolvedContract);
            case PlannedTypeKind.SzArray:
            case PlannedTypeKind.Array:
            case PlannedTypeKind.ByRef:
            case PlannedTypeKind.Pointer:
            case PlannedTypeKind.Pinned:
                return planned.Rank == otherPlanned.Rank
                    && CompatibleType(
                        planned.ElementType!,
                        shape.ElementType!,
                        other,
                        otherPlanned.ElementType!,
                        otherShape.ElementType!,
                        correspondingTypeRequest,
                        otherCorrespondingTypeRequest,
                        ref usedUnresolvedContract);
            case PlannedTypeKind.Modified:
                return planned.IsRequiredModifier
                        == otherPlanned.IsRequiredModifier
                    && CompatibleType(
                        planned.ElementType!,
                        shape.ElementType!,
                        other,
                        otherPlanned.ElementType!,
                        otherShape.ElementType!,
                        correspondingTypeRequest,
                        otherCorrespondingTypeRequest,
                        ref usedUnresolvedContract)
                    && CompatibleMany(
                        planned.Components,
                        shape.Components,
                        other,
                        otherPlanned.Components,
                        otherShape.Components,
                        correspondingTypeRequest,
                        otherCorrespondingTypeRequest,
                        ref usedUnresolvedContract);
            case PlannedTypeKind.FunctionPointer:
                return planned.SignatureHeader
                        == otherPlanned.SignatureHeader
                    && planned.GenericArity == otherPlanned.GenericArity
                    && planned.RequiredParameterCount
                        == otherPlanned.RequiredParameterCount
                    && CompatibleType(
                        planned.ElementType!,
                        shape.ElementType!,
                        other,
                        otherPlanned.ElementType!,
                        otherShape.ElementType!,
                        correspondingTypeRequest,
                        otherCorrespondingTypeRequest,
                        ref usedUnresolvedContract)
                    && CompatibleMany(
                        planned.Components,
                        shape.Components,
                        other,
                        otherPlanned.Components,
                        otherShape.Components,
                        correspondingTypeRequest,
                        otherCorrespondingTypeRequest,
                        ref usedUnresolvedContract);
            case PlannedTypeKind.GenericParameter:
            case PlannedTypeKind.MethodGenericParameter:
                return planned.GenericParameterIndex
                    == otherPlanned.GenericParameterIndex;
            default:
                return false;
        }
    }

    bool CompatibleMany(
        ImmutableArray<PlannedType> planned,
        ImmutableArray<CatalogTypeShape> shapes,
        CatalogMemberCorrespondencePlan other,
        ImmutableArray<PlannedType> otherPlanned,
        ImmutableArray<CatalogTypeShape> otherShapes,
        TypeResolutionRequest? correspondingTypeRequest,
        TypeResolutionRequest? otherCorrespondingTypeRequest,
        ref bool usedUnresolvedContract)
    {
        if (planned.Length != otherPlanned.Length
            || shapes.Length != otherShapes.Length
            || planned.Length != shapes.Length)
        {
            return false;
        }

        for (int i = 0; i < planned.Length; i++)
        {
            if (!CompatibleType(
                    planned[i],
                    shapes[i],
                    other,
                    otherPlanned[i],
                    otherShapes[i],
                    correspondingTypeRequest,
                    otherCorrespondingTypeRequest,
                    ref usedUnresolvedContract))
            {
                return false;
            }
        }

        return true;
    }

    static bool EquivalentUnresolvedContract(
        TypeResolutionRequest request,
        TypeResolutionRequest other)
    {
        if (request.Type != other.Type)
            return false;

        return (request.Start, other.Start) switch
        {
            (
                TypeResolutionStart.Reference left,
                TypeResolutionStart.Reference right) =>
                    left.Value == right.Value
                    && left.Scope == right.Scope,
            (
                TypeResolutionStart.CoreLibrary left,
                TypeResolutionStart.CoreLibrary right) =>
                    left.Scope == right.Scope,
            _ => false,
        };
    }

    static bool MatchesEstablishedCorrespondence(
        TypeResolutionRequest request,
        TypeResolutionRequest? correspondingTypeRequest,
        TypeResolutionRequest other,
        TypeResolutionRequest? otherCorrespondingTypeRequest) =>
        correspondingTypeRequest is not null
        && otherCorrespondingTypeRequest is not null
        && TypeResolutionRequestComparer.Instance.Equals(
            request,
            correspondingTypeRequest)
        && TypeResolutionRequestComparer.Instance.Equals(
            other,
            otherCorrespondingTypeRequest);

    static CatalogMemberCorrespondencePlan CreateCore(
        ResolvedAssemblyReference source,
        TypeRef declaringType,
        string name,
        MemberKind memberKind,
        int genericArity,
        bool hasThis,
        byte signatureHeader,
        int requiredParameterCount,
        ImmutableArray<TypeRef> parameterTypes,
        TypeRef returnType,
        ImmutableArray<MemberCorrespondenceFailure>.Builder initialFailures)
    {
        if (string.IsNullOrEmpty(name))
        {
            initialFailures.Add(
                new MemberCorrespondenceFailure.MalformedTypeShape(
                    declaringType,
                    "member name is empty"));
        }
        if (genericArity < 0)
        {
            initialFailures.Add(
                new MemberCorrespondenceFailure.MalformedTypeShape(
                    declaringType,
                    "method generic arity is negative"));
        }
        if (parameterTypes.IsDefault)
        {
            initialFailures.Add(
                new MemberCorrespondenceFailure.MalformedTypeShape(
                    declaringType,
                    "parameter collection is uninitialized"));
            parameterTypes = [];
        }

        bool isVararg =
            (signatureHeader & CallingConventionMask)
            == VarargCallingConvention;
        int identityParameterCount = parameterTypes.Length;
        if (isVararg)
        {
            if (requiredParameterCount < 0
                || requiredParameterCount > parameterTypes.Length)
            {
                initialFailures.Add(
                    new MemberCorrespondenceFailure
                        .InvalidRequiredParameterCount(
                            requiredParameterCount,
                            parameterTypes.Length));
            }
            else
            {
                identityParameterCount = requiredParameterCount;
            }
        }
        else
        {
            requiredParameterCount = parameterTypes.Length;
        }

        var builder = new CatalogMemberCorrespondencePlanner(
            source,
            initialFailures);
        PlannedType plannedDeclaring =
            builder.Plan(declaringType, depth: 0);
        var plannedParameters =
            ImmutableArray.CreateBuilder<PlannedType>(
                identityParameterCount);
        for (int i = 0; i < identityParameterCount; i++)
        {
            plannedParameters.Add(
                builder.Plan(parameterTypes[i], depth: 0));
        }
        PlannedType plannedReturn =
            builder.Plan(returnType, depth: 0);

        return new CatalogMemberCorrespondencePlan(
            plannedDeclaring,
            name ?? "",
            memberKind,
            genericArity,
            hasThis,
            signatureHeader,
            requiredParameterCount,
            plannedParameters.MoveToImmutable(),
            plannedReturn,
            builder.Requests.ToImmutableArray(),
            builder.Failures.ToImmutable());
    }

    static byte CanonicalSignatureHeader(
        byte signatureHeader,
        bool hasThis,
        int genericArity)
    {
        const byte Generic = 0x10;
        const byte Instance = 0x20;
        const byte ExplicitThis = 0x40;

        byte canonical = (byte)(
            signatureHeader
            & (CallingConventionMask | ExplicitThis));
        if (genericArity > 0)
            canonical |= Generic;
        if (hasThis)
            canonical |= Instance;
        return canonical;
    }
}
