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
    const int MaxShapeDepth = 256;
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
        var projection = new Projection(context, outcomes);
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

        var builder = new Builder(source, initialFailures);
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

    sealed class Builder
    {
        readonly ResolvedAssemblyReference _source;
        readonly Dictionary<TypeResolutionRequest, int> _requestIndices =
            new(TypeResolutionRequestComparer.Instance);

        internal Builder(
            ResolvedAssemblyReference source,
            ImmutableArray<MemberCorrespondenceFailure>.Builder failures)
        {
            _source = source;
            Failures = failures;
        }

        internal List<TypeResolutionRequest> Requests { get; } = [];
        internal ImmutableArray<MemberCorrespondenceFailure>.Builder Failures
            { get; }

        internal PlannedType Plan(TypeRef? type, int depth)
        {
            if (type is null)
            {
                TypeRef placeholder = TypeRef.Unsupported("missing type");
                Failures.Add(
                    new MemberCorrespondenceFailure.MalformedTypeShape(
                        placeholder,
                        "type is null"));
                return PlannedType.Invalid;
            }
            if (depth >= MaxShapeDepth)
            {
                Failures.Add(
                    new MemberCorrespondenceFailure.ShapeDepthExceeded(
                        MaxShapeDepth));
                return PlannedType.Invalid;
            }

            switch (type.Kind)
            {
                case TypeRefKind.Definition:
                    if (type.Resolution is null)
                    {
                        Failures.Add(
                            new MemberCorrespondenceFailure
                                .MissingResolutionProvenance(type));
                        return PlannedType.Invalid;
                    }

                    TypeResolutionRequest request =
                        TypeResolutionRequestFactory.Create(
                            _source,
                            type.Resolution);
                    if (!_requestIndices.TryGetValue(
                            request,
                            out int requestIndex))
                    {
                        requestIndex = Requests.Count;
                        Requests.Add(request);
                        _requestIndices.Add(request, requestIndex);
                    }
                    return PlannedType.Named(
                        requestIndex,
                        type.Resolution.Type);

                case TypeRefKind.GenericInstance:
                    if (type.ElementType is null
                        || type.TypeArguments.IsDefault)
                    {
                        return Malformed(
                            type,
                            "generic instance is incomplete");
                    }
                    return PlannedType.GenericInstance(
                        Plan(type.ElementType, depth + 1),
                        PlanMany(type.TypeArguments, depth + 1));

                case TypeRefKind.SzArray:
                case TypeRefKind.ByRef:
                case TypeRefKind.Pointer:
                case TypeRefKind.Pinned:
                    if (type.ElementType is null)
                        return Malformed(type, "element type is missing");
                    return PlannedType.Unary(
                        type.Kind,
                        Plan(type.ElementType, depth + 1));

                case TypeRefKind.Array:
                    if (type.ElementType is null || type.Rank <= 0)
                        return Malformed(type, "array shape is invalid");
                    return PlannedType.Unary(
                        type.Kind,
                        Plan(type.ElementType, depth + 1),
                        type.Rank);

                case TypeRefKind.GenericParameter:
                case TypeRefKind.MethodGenericParameter:
                    if (type.GenericParameterIndex < 0)
                    {
                        return Malformed(
                            type,
                            "generic parameter index is negative");
                    }
                    return PlannedType.GenericParameter(
                        type.Kind,
                        type.GenericParameterIndex);

                case TypeRefKind.Unsupported
                    when type.FunctionPointerSignature is { } signature:
                    return PlannedType.FunctionPointer(
                        signature.Header.RawValue,
                        signature.GenericParameterCount,
                        signature.RequiredParameterCount,
                        Plan(signature.ReturnType, depth + 1),
                        PlanMany(
                            signature.ParameterTypes,
                            depth + 1));

                case TypeRefKind.Unsupported
                    when type.ModifierType is not null
                        && type.UnmodifiedType is not null:
                    return PlannedType.Modified(
                        Plan(type.ModifierType, depth + 1),
                        Plan(type.UnmodifiedType, depth + 1),
                        type.IsRequiredModifier);

                case TypeRefKind.Unsupported:
                    Failures.Add(
                        new MemberCorrespondenceFailure
                            .UnsupportedTypeShape(type));
                    return PlannedType.Invalid;

                default:
                    return Malformed(type, "unknown type shape");
            }
        }

        ImmutableArray<PlannedType> PlanMany(
            ImmutableArray<TypeRef> types,
            int depth)
        {
            if (types.IsDefault)
                return [];
            var builder =
                ImmutableArray.CreateBuilder<PlannedType>(types.Length);
            foreach (TypeRef type in types)
                builder.Add(Plan(type, depth));
            return builder.MoveToImmutable();
        }

        PlannedType Malformed(TypeRef type, string reason)
        {
            Failures.Add(
                new MemberCorrespondenceFailure.MalformedTypeShape(
                    type,
                    reason));
            return PlannedType.Invalid;
        }
    }

    sealed class Projection
    {
        readonly TypeResolutionContext _context;
        readonly TypeResolutionOutcome[] _outcomes;
        readonly HashSet<int> _failedRequests = [];
        readonly HashSet<DefinitionJoinToken> _duplicateTokens = [];
        readonly HashSet<(
            UnresolvedBindingKey Binding,
            MetadataTypeDefinitionName Type)> _unresolvedBindings = [];

        internal Projection(
            TypeResolutionContext context,
            TypeResolutionOutcome[] outcomes)
        {
            _context = context;
            _outcomes = outcomes;
        }

        internal ImmutableArray<MemberCorrespondenceFailure>.Builder Failures
            { get; } =
                ImmutableArray.CreateBuilder<MemberCorrespondenceFailure>();
        internal ImmutableArray<MemberCorrespondenceEvidence>.Builder Evidence
            { get; } =
                ImmutableArray.CreateBuilder<MemberCorrespondenceEvidence>();

        internal CatalogTypeShape? Project(PlannedType planned)
        {
            switch (planned.Kind)
            {
                case PlannedTypeKind.Invalid:
                    return null;
                case PlannedTypeKind.Named:
                    return ProjectNamed(
                        planned.RequestIndex,
                        planned.TypeName!);
                case PlannedTypeKind.GenericInstance:
                {
                    CatalogTypeShape? definition =
                        Project(planned.ElementType!);
                    ImmutableArray<CatalogTypeShape>? components =
                        ProjectMany(planned.Components);
                    return definition is null || components is null
                        ? null
                        : CatalogTypeShape.GenericInstance(
                            definition,
                            components.Value);
                }
                case PlannedTypeKind.SzArray:
                case PlannedTypeKind.Array:
                case PlannedTypeKind.ByRef:
                case PlannedTypeKind.Pointer:
                case PlannedTypeKind.Pinned:
                {
                    CatalogTypeShape? element =
                        Project(planned.ElementType!);
                    return element is null
                        ? null
                        : CatalogTypeShape.Unary(
                            ToCatalogKind(planned.Kind),
                            element,
                            planned.Rank);
                }
                case PlannedTypeKind.Modified:
                {
                    CatalogTypeShape? modifier =
                        Project(planned.Components[0]);
                    CatalogTypeShape? unmodified =
                        Project(planned.ElementType!);
                    return modifier is null || unmodified is null
                        ? null
                        : CatalogTypeShape.Modified(
                            modifier,
                            unmodified,
                            planned.IsRequiredModifier);
                }
                case PlannedTypeKind.FunctionPointer:
                {
                    CatalogTypeShape? returnType =
                        Project(planned.ElementType!);
                    ImmutableArray<CatalogTypeShape>? parameters =
                        ProjectMany(planned.Components);
                    return returnType is null || parameters is null
                        ? null
                        : CatalogTypeShape.FunctionPointer(
                            planned.SignatureHeader,
                            planned.GenericArity,
                            planned.RequiredParameterCount,
                            returnType,
                            parameters.Value);
                }
                case PlannedTypeKind.GenericParameter:
                case PlannedTypeKind.MethodGenericParameter:
                    return CatalogTypeShape.GenericParameter(
                        planned.Kind
                            == PlannedTypeKind.MethodGenericParameter,
                        planned.GenericParameterIndex);
                default:
                    throw new InvalidOperationException(
                        "Unknown planned type shape.");
            }
        }

        CatalogTypeShape? ProjectNamed(
            int requestIndex,
            MetadataTypeDefinitionName type)
        {
            TypeResolutionOutcome outcome = _outcomes[requestIndex];
            switch (outcome)
            {
                case TypeResolutionOutcome.Resolved resolved:
                    return ProjectResolved(
                        requestIndex,
                        type,
                        resolved);
                case TypeResolutionOutcome.UnboundBinding unbound:
                    return ProjectUnresolved(
                        requestIndex,
                        type,
                        unbound.Binding,
                        outcome);
                case TypeResolutionOutcome.Unavailable unavailable:
                    return ProjectUnresolved(
                        requestIndex,
                        type,
                        unavailable.Binding,
                        outcome);
                case TypeResolutionOutcome.Rejected
                    {
                        Failure:
                            TypeResolutionFailure.PlanExpansionRequired
                            expansion
                    }:
                    AddFailure(
                        requestIndex,
                        new MemberCorrespondenceFailure.ExpansionRequired(
                            expansion.Request));
                    return null;
                default:
                    AddFailure(
                        requestIndex,
                        new MemberCorrespondenceFailure.Resolution(outcome));
                    return null;
            }
        }

        CatalogTypeShape? ProjectResolved(
            int requestIndex,
            MetadataTypeDefinitionName type,
            TypeResolutionOutcome.Resolved resolved)
        {
            switch (_context.ProjectDefinitionJoinToken(
                resolved.Definition.Key))
            {
                case DefinitionJoinTokenProjection.Issued issued:
                    if (issued.Token.Kind
                            == DefinitionJoinKind
                                .IndeterminateDuplicateArtifact
                        && _duplicateTokens.Add(issued.Token))
                    {
                        Evidence.Add(
                            new MemberCorrespondenceEvidence
                                .DuplicateArtifact(
                                    type,
                                    issued.Token.Evidence
                                    ?? throw new InvalidOperationException(
                                        "Duplicate join token has no evidence.")));
                    }
                    return CatalogTypeShape.Resolved(issued.Token);
                case DefinitionJoinTokenProjection.IncomparableCatalogs:
                    throw new InvalidOperationException(
                        "A context resolved currency from another catalog.");
                case DefinitionJoinTokenProjection.StaleGeneration stale:
                    AddFailure(
                        requestIndex,
                        new MemberCorrespondenceFailure.StaleGeneration(
                            stale.DefinitionGeneration,
                            stale.CurrentGeneration));
                    return null;
                default:
                    throw new InvalidOperationException(
                        "Unknown definition-token projection.");
            }
        }

        CatalogTypeShape? ProjectUnresolved(
            int requestIndex,
            MetadataTypeDefinitionName type,
            UnresolvedBindingReference binding,
            TypeResolutionOutcome outcome)
        {
            switch (_context.ProjectUnresolvedBindingKey(binding))
            {
                case UnresolvedBindingKeyProjection.Issued issued:
                    if (_unresolvedBindings.Add((issued.Key, type)))
                    {
                        Evidence.Add(
                            new MemberCorrespondenceEvidence
                                .UnresolvedBinding(type, outcome));
                    }
                    return CatalogTypeShape.Degraded(
                        issued.Key,
                        type);
                case UnresolvedBindingKeyProjection.IncomparableCatalogs:
                    throw new InvalidOperationException(
                        "A context resolved currency from another catalog.");
                case UnresolvedBindingKeyProjection.StaleGeneration stale:
                    AddFailure(
                        requestIndex,
                        new MemberCorrespondenceFailure.StaleGeneration(
                            stale.BindingGeneration,
                            stale.CurrentGeneration));
                    return null;
                default:
                    throw new InvalidOperationException(
                        "Unknown unresolved-binding projection.");
            }
        }

        ImmutableArray<CatalogTypeShape>? ProjectMany(
            ImmutableArray<PlannedType> planned)
        {
            var builder =
                ImmutableArray.CreateBuilder<CatalogTypeShape>(
                    planned.Length);
            foreach (PlannedType item in planned)
            {
                CatalogTypeShape? projected = Project(item);
                if (projected is not null)
                    builder.Add(projected);
            }
            return builder.Count == planned.Length
                ? builder.MoveToImmutable()
                : null;
        }

        void AddFailure(
            int requestIndex,
            MemberCorrespondenceFailure failure)
        {
            if (_failedRequests.Add(requestIndex))
                Failures.Add(failure);
        }

        static CatalogTypeShapeKind ToCatalogKind(
            PlannedTypeKind kind) =>
            kind switch
            {
                PlannedTypeKind.SzArray =>
                    CatalogTypeShapeKind.SzArray,
                PlannedTypeKind.Array =>
                    CatalogTypeShapeKind.Array,
                PlannedTypeKind.ByRef =>
                    CatalogTypeShapeKind.ByRef,
                PlannedTypeKind.Pointer =>
                    CatalogTypeShapeKind.Pointer,
                PlannedTypeKind.Pinned =>
                    CatalogTypeShapeKind.Pinned,
                _ => throw new InvalidOperationException(
                    "Planned type is not unary."),
            };
    }

    enum PlannedTypeKind
    {
        Invalid,
        Named,
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

    sealed class PlannedType
    {
        PlannedType(
            PlannedTypeKind kind,
            int requestIndex = -1,
            MetadataTypeDefinitionName? typeName = null,
            PlannedType? elementType = null,
            ImmutableArray<PlannedType> components = default,
            int rank = 0,
            int genericParameterIndex = -1,
            bool isRequiredModifier = false,
            byte signatureHeader = 0,
            int genericArity = 0,
            int requiredParameterCount = 0)
        {
            Kind = kind;
            RequestIndex = requestIndex;
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

        internal static PlannedType Invalid { get; } =
            new(PlannedTypeKind.Invalid);

        internal PlannedTypeKind Kind { get; }
        internal int RequestIndex { get; }
        internal MetadataTypeDefinitionName? TypeName { get; }
        internal PlannedType? ElementType { get; }
        internal ImmutableArray<PlannedType> Components { get; }
        internal int Rank { get; }
        internal int GenericParameterIndex { get; }
        internal bool IsRequiredModifier { get; }
        internal byte SignatureHeader { get; }
        internal int GenericArity { get; }
        internal int RequiredParameterCount { get; }

        internal static PlannedType Named(
            int requestIndex,
            MetadataTypeDefinitionName typeName) =>
            new(
                PlannedTypeKind.Named,
                requestIndex: requestIndex,
                typeName: typeName);

        internal static PlannedType GenericInstance(
            PlannedType definition,
            ImmutableArray<PlannedType> arguments) =>
            new(
                PlannedTypeKind.GenericInstance,
                elementType: definition,
                components: arguments);

        internal static PlannedType Unary(
            TypeRefKind kind,
            PlannedType element,
            int rank = 0) =>
            new(
                kind switch
                {
                    TypeRefKind.SzArray => PlannedTypeKind.SzArray,
                    TypeRefKind.Array => PlannedTypeKind.Array,
                    TypeRefKind.ByRef => PlannedTypeKind.ByRef,
                    TypeRefKind.Pointer => PlannedTypeKind.Pointer,
                    TypeRefKind.Pinned => PlannedTypeKind.Pinned,
                    _ => throw new InvalidOperationException(
                        "Type is not unary."),
                },
                elementType: element,
                rank: rank);

        internal static PlannedType Modified(
            PlannedType modifier,
            PlannedType unmodified,
            bool isRequired) =>
            new(
                PlannedTypeKind.Modified,
                elementType: unmodified,
                components: [modifier],
                isRequiredModifier: isRequired);

        internal static PlannedType FunctionPointer(
            byte signatureHeader,
            int genericArity,
            int requiredParameterCount,
            PlannedType returnType,
            ImmutableArray<PlannedType> parameterTypes) =>
            new(
                PlannedTypeKind.FunctionPointer,
                elementType: returnType,
                components: parameterTypes,
                signatureHeader: signatureHeader,
                genericArity: genericArity,
                requiredParameterCount: requiredParameterCount);

        internal static PlannedType GenericParameter(
            TypeRefKind kind,
            int index) =>
            new(
                kind == TypeRefKind.MethodGenericParameter
                    ? PlannedTypeKind.MethodGenericParameter
                    : PlannedTypeKind.GenericParameter,
                genericParameterIndex: index);
    }
}
