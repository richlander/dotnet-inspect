using System.Collections.Immutable;
using InertText;

namespace ILInspector.Analysis;

public readonly record struct ResourceEffectLanguageIdentity
{
    public const string Version1Value = "resource-effects/1";

    public ResourceEffectLanguageIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static ResourceEffectLanguageIdentity Version1 { get; } =
        new(Version1Value);

    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct ResourceEffectModelIdentity
{
    public ResourceEffectModelIdentity(string value)
    {
        ResourceEffectIdentifiers.RequireQualified(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct ResourceKindIdentity
{
    public ResourceKindIdentity(string value)
    {
        ResourceEffectIdentifiers.RequireQualified(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct ResourceEffectLocalIdentity
{
    public ResourceEffectLocalIdentity(string value)
    {
        ResourceEffectIdentifiers.RequireLocalIdentifier(value, nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value ?? string.Empty;
}

public enum ResourceEffectGenericVariableKind
{
    Type,
    Method,
}

public readonly record struct ResourceEffectGenericVariable
{
    public ResourceEffectGenericVariable(
        ResourceEffectGenericVariableKind kind,
        int index)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Kind = kind;
        Index = index;
    }

    public ResourceEffectGenericVariableKind Kind { get; }
    public int Index { get; }

    public override string ToString()
        => $"{(Kind == ResourceEffectGenericVariableKind.Type ? "type" : "method")}[{Index}]";
}

public sealed class ResourceKindDefinition : IEquatable<ResourceKindDefinition>
{
    readonly ImmutableArray<ResourceDeclarationProvenance> _provenances;

    public ResourceKindDefinition(
        ResourceKindIdentity identity,
        int arity,
        ImmutableArray<ResourceDeclarationProvenance> provenances)
    {
        if (identity == default)
            throw new ArgumentException("Resource-kind identity must not be default.", nameof(identity));
        ArgumentOutOfRangeException.ThrowIfNegative(arity);
        _provenances =
            ImmutableArrayValueEquality.RequireInitialized(provenances, nameof(provenances));
        if (_provenances.IsEmpty)
            throw new ArgumentException("A resource-kind definition requires provenance.", nameof(provenances));
        Identity = identity;
        Arity = arity;
    }

    public ResourceKindIdentity Identity { get; }
    public int Arity { get; }
    public ImmutableArray<ResourceDeclarationProvenance> Provenances => _provenances;

    public bool Equals(ResourceKindDefinition? other)
        => other is not null
            && Identity == other.Identity
            && Arity == other.Arity;

    public override bool Equals(object? obj)
        => obj is ResourceKindDefinition other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Identity, Arity);
}

public sealed record ResourceKindReference
{
    readonly ImmutableArray<ResourceEffectGenericVariable> _arguments;

    public ResourceKindReference(
        ResourceKindIdentity identity,
        ImmutableArray<ResourceEffectGenericVariable> arguments = default)
    {
        if (identity == default)
            throw new ArgumentException("Resource-kind identity must not be default.", nameof(identity));
        Identity = identity;
        _arguments = arguments.IsDefault ? [] : arguments;
    }

    public ResourceKindIdentity Identity { get; }
    public ImmutableArray<ResourceEffectGenericVariable> Arguments => _arguments;

    public bool Equals(ResourceKindReference? other)
        => other is not null
            && Identity == other.Identity
            && _arguments.SequenceEqual(other._arguments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Identity);
        ImmutableArrayValueEquality.AddToHash(ref hash, _arguments);
        return hash.ToHashCode();
    }

    public override string ToString()
        => _arguments.IsEmpty
            ? Identity.ToString()
            : $"{Identity}<{string.Join(",", _arguments)}>";
}

public enum ResourceAssemblyVersionPolicyKind
{
    Any,
    Exact,
}

public sealed record ResourceAssemblyVersionPolicy
{
    public ResourceAssemblyVersionPolicy(
        ResourceAssemblyVersionPolicyKind kind,
        Version? version = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if ((kind == ResourceAssemblyVersionPolicyKind.Exact) != (version is not null))
        {
            throw new ArgumentException(
                "An exact policy requires a version and an unconstrained policy forbids one.",
                nameof(version));
        }
        Kind = kind;
        Version = version;
    }

    public ResourceAssemblyVersionPolicyKind Kind { get; }
    public Version? Version { get; }

    public static ResourceAssemblyVersionPolicy Any { get; } =
        new(ResourceAssemblyVersionPolicyKind.Any);

    public static ResourceAssemblyVersionPolicy Exact(Version version)
        => new(ResourceAssemblyVersionPolicyKind.Exact, version);
}

public sealed record ResourceAssemblySelector
{
    public ResourceAssemblySelector(
        string simpleName,
        string? publicKeyToken,
        ResourceAssemblyVersionPolicy version,
        bool allowCoreLibraryFacade = false)
    {
        ResourceEffectIdentifiers.RequireAssemblyName(simpleName, nameof(simpleName));
        ArgumentNullException.ThrowIfNull(version);
        if (publicKeyToken is not null
            && (publicKeyToken.Length != 16
                || publicKeyToken.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ArgumentException(
                "A public-key token must contain exactly 16 hexadecimal characters.",
                nameof(publicKeyToken));
        }
        SimpleName = simpleName;
        PublicKeyToken = publicKeyToken?.ToLowerInvariant();
        Version = version;
        AllowCoreLibraryFacade = allowCoreLibraryFacade;
    }

    public string SimpleName { get; }
    public string? PublicKeyToken { get; }
    public ResourceAssemblyVersionPolicy Version { get; }
    public bool AllowCoreLibraryFacade { get; }
}

public sealed record ResourceTypeNameSegment
{
    public ResourceTypeNameSegment(string metadataName, int genericArity)
    {
        ArgumentNullException.ThrowIfNull(metadataName);
        if (metadataName.Length == 0)
            throw new ArgumentException("A metadata-name segment must not be empty.", nameof(metadataName));
        ArgumentOutOfRangeException.ThrowIfNegative(genericArity);
        MetadataName = metadataName;
        GenericArity = genericArity;
    }

    public string MetadataName { get; }
    public int GenericArity { get; }
}

public abstract record ResourceTypeExpression
{
    private protected ResourceTypeExpression()
    {
    }

    public sealed record Variable : ResourceTypeExpression
    {
        public Variable(ResourceEffectGenericVariable value) => Value = value;
        public ResourceEffectGenericVariable Value { get; }
    }

    public sealed record Named : ResourceTypeExpression
    {
        readonly ImmutableArray<ResourceTypeNameSegment> _segments;
        readonly ImmutableArray<ResourceTypeExpression> _arguments;

        public Named(
            ResourceAssemblySelector assembly,
            string @namespace,
            ImmutableArray<ResourceTypeNameSegment> segments,
            ImmutableArray<ResourceTypeExpression> arguments = default)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentNullException.ThrowIfNull(@namespace);
            _segments = ImmutableArrayValueEquality.RequireInitialized(segments, nameof(segments));
            if (_segments.IsEmpty)
                throw new ArgumentException("A named type requires at least one segment.", nameof(segments));
            _arguments = arguments.IsDefault
                ? []
                : ImmutableArrayValueEquality.RequireInitialized(arguments, nameof(arguments));
            int arity = _segments.Sum(segment => segment.GenericArity);
            if (_arguments.Length != arity)
            {
                throw new ArgumentException(
                    "The number of type arguments must equal the sum of segment generic arities.",
                    nameof(arguments));
            }
            Assembly = assembly;
            Namespace = @namespace;
        }

        public ResourceAssemblySelector Assembly { get; }
        public string Namespace { get; }
        public ImmutableArray<ResourceTypeNameSegment> Segments => _segments;
        public ImmutableArray<ResourceTypeExpression> Arguments => _arguments;

        public bool Equals(Named? other)
            => other is not null
                && Assembly == other.Assembly
                && Namespace == other.Namespace
                && _segments.SequenceEqual(other._segments)
                && _arguments.SequenceEqual(other._arguments);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Assembly);
            hash.Add(Namespace);
            ImmutableArrayValueEquality.AddToHash(ref hash, _segments);
            ImmutableArrayValueEquality.AddToHash(ref hash, _arguments);
            return hash.ToHashCode();
        }
    }

    public sealed record SzArray(ResourceTypeExpression Element) : ResourceTypeExpression
    {
        public ResourceTypeExpression Element { get; } =
            Element ?? throw new ArgumentNullException(nameof(Element));
    }

    public sealed record Array : ResourceTypeExpression
    {
        public Array(ResourceTypeExpression element, int rank)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rank);
            Element = element;
            Rank = rank;
        }

        public ResourceTypeExpression Element { get; }
        public int Rank { get; }
    }

    public sealed record ByReference(ResourceTypeExpression Element) : ResourceTypeExpression
    {
        public ResourceTypeExpression Element { get; } =
            Element ?? throw new ArgumentNullException(nameof(Element));
    }

    public sealed record Pointer(ResourceTypeExpression Element) : ResourceTypeExpression
    {
        public ResourceTypeExpression Element { get; } =
            Element ?? throw new ArgumentNullException(nameof(Element));
    }
}

public enum ResourceEffectMemberKind
{
    Method,
    Constructor,
    PropertyGetter,
    PropertySetter,
    Field,
}

public enum ResourceEffectRefKind
{
    Value,
    Ref,
    In,
    Out,
}

public enum ResourceEffectCallingConvention
{
    Default,
    VarArgs,
    CDecl,
    StdCall,
    ThisCall,
    FastCall,
}

public sealed record ResourceEffectParameterSelector
{
    public ResourceEffectParameterSelector(
        ResourceTypeExpression type,
        ResourceEffectRefKind refKind)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!Enum.IsDefined(refKind))
            throw new ArgumentOutOfRangeException(nameof(refKind));
        Type = type;
        RefKind = refKind;
    }

    public ResourceTypeExpression Type { get; }
    public ResourceEffectRefKind RefKind { get; }
}

public sealed record ResourceEffectMemberSelector
{
    readonly ImmutableArray<ResourceEffectParameterSelector> _parameters;

    public ResourceEffectMemberSelector(
        ResourceTypeExpression.Named declaringType,
        string metadataName,
        ResourceEffectMemberKind kind,
        bool isStatic,
        int genericArity,
        ResourceEffectCallingConvention callingConvention,
        bool hasThis,
        bool explicitThis,
        ImmutableArray<ResourceEffectParameterSelector> parameters,
        ResourceTypeExpression returnType)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(metadataName);
        if (metadataName.Length == 0)
            throw new ArgumentException("A member metadata name must not be empty.", nameof(metadataName));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(callingConvention))
            throw new ArgumentOutOfRangeException(nameof(callingConvention));
        ArgumentOutOfRangeException.ThrowIfNegative(genericArity);
        _parameters =
            ImmutableArrayValueEquality.RequireInitialized(parameters, nameof(parameters));
        ArgumentNullException.ThrowIfNull(returnType);
        if (kind == ResourceEffectMemberKind.Field
            && (genericArity != 0
                || !_parameters.IsEmpty
                || hasThis
                || explicitThis
                || callingConvention != ResourceEffectCallingConvention.Default))
        {
            throw new ArgumentException(
                "A field selector cannot carry method signature shape.",
                nameof(kind));
        }
        if (kind != ResourceEffectMemberKind.Field
            && (isStatic == hasThis || (explicitThis && !hasThis)))
        {
            throw new ArgumentException(
                "Static members cannot have this, and instance members must have this.",
                nameof(hasThis));
        }
        DeclaringType = declaringType;
        MetadataName = metadataName;
        Kind = kind;
        IsStatic = isStatic;
        GenericArity = genericArity;
        CallingConvention = callingConvention;
        HasThis = hasThis;
        ExplicitThis = explicitThis;
        ReturnType = returnType;
    }

    public ResourceTypeExpression.Named DeclaringType { get; }
    public string MetadataName { get; }
    public ResourceEffectMemberKind Kind { get; }
    public bool IsStatic { get; }
    public int GenericArity { get; }
    public ResourceEffectCallingConvention CallingConvention { get; }
    public bool HasThis { get; }
    public bool ExplicitThis { get; }
    public ImmutableArray<ResourceEffectParameterSelector> Parameters => _parameters;
    public ResourceTypeExpression ReturnType { get; }

    public bool Equals(ResourceEffectMemberSelector? other)
        => other is not null
            && DeclaringType == other.DeclaringType
            && MetadataName == other.MetadataName
            && Kind == other.Kind
            && IsStatic == other.IsStatic
            && GenericArity == other.GenericArity
            && CallingConvention == other.CallingConvention
            && HasThis == other.HasThis
            && ExplicitThis == other.ExplicitThis
            && _parameters.SequenceEqual(other._parameters)
            && ReturnType == other.ReturnType;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeclaringType);
        hash.Add(MetadataName);
        hash.Add(Kind);
        hash.Add(IsStatic);
        hash.Add(GenericArity);
        hash.Add(CallingConvention);
        hash.Add(HasThis);
        hash.Add(ExplicitThis);
        ImmutableArrayValueEquality.AddToHash(ref hash, _parameters);
        hash.Add(ReturnType);
        return hash.ToHashCode();
    }
}

public abstract record ResourceEffectTargetSelector
{
    private protected ResourceEffectTargetSelector()
    {
    }

    public sealed record Type(ResourceTypeExpression.Named Selector)
        : ResourceEffectTargetSelector
    {
        public ResourceTypeExpression.Named Selector { get; } =
            Selector ?? throw new ArgumentNullException(nameof(Selector));
    }

    public sealed record Member(ResourceEffectMemberSelector Selector)
        : ResourceEffectTargetSelector
    {
        public ResourceEffectMemberSelector Selector { get; } =
            Selector ?? throw new ArgumentNullException(nameof(Selector));
    }
}

public abstract record ResourceEffectLocation
{
    private protected ResourceEffectLocation()
    {
    }

    public sealed record Receiver : ResourceEffectLocation;
    public sealed record Return : ResourceEffectLocation;
    public sealed record Constructed : ResourceEffectLocation;

    public sealed record Parameter : ResourceEffectLocation
    {
        public Parameter(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            Index = index;
        }
        public int Index { get; }
    }

    public sealed record Operation : ResourceEffectLocation
    {
        public Operation(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            Index = index;
        }
        public int Index { get; }
    }

    public sealed record CallbackParameter : ResourceEffectLocation
    {
        public CallbackParameter(int callbackIndex, int parameterIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(callbackIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(parameterIndex);
            CallbackIndex = callbackIndex;
            ParameterIndex = parameterIndex;
        }
        public int CallbackIndex { get; }
        public int ParameterIndex { get; }
    }

    public sealed record CallbackReturn : ResourceEffectLocation
    {
        public CallbackReturn(int callbackIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(callbackIndex);
            CallbackIndex = callbackIndex;
        }
        public int CallbackIndex { get; }
    }

    public sealed record Field : ResourceEffectLocation
    {
        public Field(
            ResourceEffectLocation root,
            ResourceEffectLocalIdentity selector)
        {
            ArgumentNullException.ThrowIfNull(root);
            if (root is not Receiver and not Parameter and not Return)
            {
                throw new ArgumentException(
                    "A field location must be rooted in receiver, parameter, or return.",
                    nameof(root));
            }
            if (selector == default)
                throw new ArgumentException("Field selector must not be default.", nameof(selector));
            Root = root;
            Selector = selector;
        }

        public ResourceEffectLocation Root { get; }
        public ResourceEffectLocalIdentity Selector { get; }
    }

    public sealed record ResolvedField : ResourceEffectLocation
    {
        public ResolvedField(
            ResourceEffectLocation root,
            ResourceEffectMemberSelector selector)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(selector);
            if (root is not Receiver and not Parameter and not Return)
            {
                throw new ArgumentException(
                    "A resolved field location must be rooted in receiver, parameter, or return.",
                    nameof(root));
            }
            if (selector.Kind != ResourceEffectMemberKind.Field)
                throw new ArgumentException("The resolved selector must identify a field.", nameof(selector));
            Root = root;
            Selector = selector;
        }

        public ResourceEffectLocation Root { get; }
        public ResourceEffectMemberSelector Selector { get; }
    }
}

public abstract record ResourceEffectCompletion
{
    private protected ResourceEffectCompletion()
    {
    }

    public sealed record Entry : ResourceEffectCompletion;
    public sealed record NormalReturn : ResourceEffectCompletion;
    public sealed record ExceptionalExit : ResourceEffectCompletion;
    public sealed record SuccessfulAwait : ResourceEffectCompletion;

    public sealed record Outcome : ResourceEffectCompletion
    {
        public Outcome(ResourceEffectLocalIdentity identity)
        {
            if (identity == default)
                throw new ArgumentException("Outcome identity must not be default.", nameof(identity));
            Identity = identity;
        }
        public ResourceEffectLocalIdentity Identity { get; }
    }

    public sealed record ResolvedOutcome : ResourceEffectCompletion
    {
        public ResolvedOutcome(
            ResourceEffectLocation source,
            ResourceEffectOutcomeTest test)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(test);
            Source = source;
            Test = test;
        }

        public ResourceEffectLocation Source { get; }
        public ResourceEffectOutcomeTest Test { get; }
    }
}

public abstract record ResourceEffectSignatureLocation
{
    private protected ResourceEffectSignatureLocation()
    {
    }

    public sealed record Receiver : ResourceEffectSignatureLocation;
    public sealed record Return : ResourceEffectSignatureLocation;
    public sealed record Parameter : ResourceEffectSignatureLocation
    {
        public Parameter(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            Index = index;
        }
        public int Index { get; }
    }
}

public abstract record ResourceEffectGuard
{
    private protected ResourceEffectGuard()
    {
    }

    public sealed record ExactRuntimeType(
        ResourceEffectLocation Subject,
        ResourceEffectSignatureLocation Expected)
        : ResourceEffectGuard
    {
        public ResourceEffectLocation Subject { get; } =
            Subject ?? throw new ArgumentNullException(nameof(Subject));
        public ResourceEffectSignatureLocation Expected { get; } =
            Expected ?? throw new ArgumentNullException(nameof(Expected));
    }
}

public abstract record ResourceEffectOutcomeTest
{
    private protected ResourceEffectOutcomeTest()
    {
    }

    public sealed record Boolean(bool Value) : ResourceEffectOutcomeTest;
    public sealed record Enum : ResourceEffectOutcomeTest
    {
        public Enum(string value)
        {
            ResourceEffectIdentifiers.RequireDottedIdentifier(
                value,
                requireDot: false,
                nameof(value));
            Value = value;
        }
        public string Value { get; }
    }
    public sealed record Null : ResourceEffectOutcomeTest;
    public sealed record NonNull : ResourceEffectOutcomeTest;
    public sealed record ExactType : ResourceEffectOutcomeTest
    {
        public ExactType(string selector)
        {
            ResourceEffectIdentifiers.RequireQualified(selector, nameof(selector));
            Selector = selector;
        }
        public string Selector { get; }
    }
}

public enum ResourceDeclaredValueKind
{
    DeclaredType,
    DeclaredField,
}

public abstract record ResourceAuthorityKey
{
    private protected ResourceAuthorityKey()
    {
    }

    public sealed record Value : ResourceAuthorityKey;

    public sealed record Singleton : ResourceAuthorityKey
    {
        readonly ImmutableArray<ResourceEffectGenericVariable> _arguments;

        public Singleton(ImmutableArray<ResourceEffectGenericVariable> arguments)
        {
            _arguments =
                ImmutableArrayValueEquality.RequireInitialized(arguments, nameof(arguments));
        }

        public ImmutableArray<ResourceEffectGenericVariable> Arguments => _arguments;

        public bool Equals(Singleton? other)
            => other is not null && _arguments.SequenceEqual(other._arguments);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            ImmutableArrayValueEquality.AddToHash(ref hash, _arguments);
            return hash.ToHashCode();
        }
    }
}

public enum ResourceBorrowAccess
{
    Read,
    Write,
}

public abstract record ResourceBorrowScope
{
    private protected ResourceBorrowScope()
    {
    }

    public sealed record Call : ResourceBorrowScope;
    public sealed record Callback : ResourceBorrowScope
    {
        public Callback(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            Index = index;
        }
        public int Index { get; }
    }
}

public enum ResourceBorrowMaterialization
{
    None,
}

public enum ResourceDerivationRelation
{
    SameValue,
    Alias,
    Borrow,
}

public enum ResourcePassIdentity
{
    Preserve,
}

public enum ResourceCallbackExecution
{
    Synchronous,
}

public enum ResourceCallbackCardinality
{
    ExactlyOnce,
}

public enum ResourceOperationBoundary
{
    Transparent,
    Ordinary,
}

public enum ResourceOperationThrows
{
    Never,
    Possible,
}

public abstract record ResourceEffect
{
    private protected ResourceEffect()
    {
    }

    public sealed record Resource(
        ResourceKindReference Kind,
        ResourceDeclaredValueKind? Value,
        ResourceEffectLocalIdentity? Selector)
        : ResourceEffect
    {
        public ResourceKindReference Kind { get; } =
            Kind ?? throw new ArgumentNullException(nameof(Kind));
        public ResourceDeclaredValueKind? Value { get; } = Value;
        public ResourceEffectLocalIdentity? Selector { get; } = Selector;
    }

    public sealed record Authority(
        ResourceKindReference Kind,
        ResourceEffectLocation Target,
        ResourceAuthorityKey Key)
        : ResourceEffect
    {
        public ResourceKindReference Kind { get; } =
            Kind ?? throw new ArgumentNullException(nameof(Kind));
        public ResourceEffectLocation Target { get; } =
            Target ?? throw new ArgumentNullException(nameof(Target));
        public ResourceAuthorityKey Key { get; } =
            Key ?? throw new ArgumentNullException(nameof(Key));
    }

    public sealed record Acquire(
        ResourceKindReference Kind,
        ResourceEffectLocation Target,
        ResourceEffectCompletion When,
        ResourceEffectLocation? Correspondence,
        ResourceEffectLocation? Lender)
        : ResourceEffect
    {
        public ResourceKindReference Kind { get; } =
            Kind ?? throw new ArgumentNullException(nameof(Kind));
        public ResourceEffectLocation Target { get; } =
            Target ?? throw new ArgumentNullException(nameof(Target));
        public ResourceEffectCompletion When { get; } =
            When ?? throw new ArgumentNullException(nameof(When));
        public ResourceEffectLocation? Correspondence { get; } = Correspondence;
        public ResourceEffectLocation? Lender { get; } = Lender;
    }

    public sealed record Move(
        ResourceEffectLocation Source,
        ResourceEffectLocation Target,
        ResourceEffectCompletion When,
        ResourceKindReference? Kind)
        : ResourceEffect
    {
        public ResourceEffectLocation Source { get; } =
            Source ?? throw new ArgumentNullException(nameof(Source));
        public ResourceEffectLocation Target { get; } =
            Target ?? throw new ArgumentNullException(nameof(Target));
        public ResourceEffectCompletion When { get; } =
            When ?? throw new ArgumentNullException(nameof(When));
        public ResourceKindReference? Kind { get; } = Kind;
    }

    public sealed record Consume(
        ResourceEffectLocation Source,
        ResourceEffectLocation.Operation Target,
        ResourceKindReference? Kind)
        : ResourceEffect
    {
        public ResourceEffectLocation Source { get; } =
            Source ?? throw new ArgumentNullException(nameof(Source));
        public ResourceEffectLocation.Operation Target { get; } =
            Target ?? throw new ArgumentNullException(nameof(Target));
        public ResourceKindReference? Kind { get; } = Kind;
    }

    public sealed record Release(
        ResourceEffectLocation Source,
        ResourceEffectCompletion When,
        ResourceKindReference? Kind,
        ResourceEffectLocation? Correspondence,
        ResourceEffectLocation? Observation)
        : ResourceEffect
    {
        public ResourceEffectLocation Source { get; } =
            Source ?? throw new ArgumentNullException(nameof(Source));
        public ResourceEffectCompletion When { get; } =
            When ?? throw new ArgumentNullException(nameof(When));
        public ResourceKindReference? Kind { get; } = Kind;
        public ResourceEffectLocation? Correspondence { get; } = Correspondence;
        public ResourceEffectLocation? Observation { get; } = Observation;
    }

    public sealed record Borrow(
        ResourceEffectLocation Source,
        ResourceEffectLocation Target,
        ResourceBorrowAccess Access,
        ResourceBorrowScope Scope,
        ResourceKindReference? Kind,
        ResourceEffectLocation? Lender,
        ResourceBorrowMaterialization? Materialization)
        : ResourceEffect
    {
        public ResourceEffectLocation Source { get; } =
            Source ?? throw new ArgumentNullException(nameof(Source));
        public ResourceEffectLocation Target { get; } =
            Target ?? throw new ArgumentNullException(nameof(Target));
        public ResourceBorrowAccess Access { get; } = Access;
        public ResourceBorrowScope Scope { get; } =
            Scope ?? throw new ArgumentNullException(nameof(Scope));
        public ResourceKindReference? Kind { get; } = Kind;
        public ResourceEffectLocation? Lender { get; } = Lender;
        public ResourceBorrowMaterialization? Materialization { get; } = Materialization;
    }

    public sealed record Derive(
        ResourceEffectLocation Source,
        ResourceEffectLocation Target,
        ResourceDerivationRelation Relation,
        ResourceEffectGuard? Guard)
        : ResourceEffect
    {
        public ResourceEffectLocation Source { get; } =
            Source ?? throw new ArgumentNullException(nameof(Source));
        public ResourceEffectLocation Target { get; } =
            Target ?? throw new ArgumentNullException(nameof(Target));
        public ResourceDerivationRelation Relation { get; } = Relation;
        public ResourceEffectGuard? Guard { get; } = Guard;
    }

    public sealed record Pass(
        ResourceEffectLocation Source,
        ResourceEffectLocation Target,
        ResourcePassIdentity? Identity)
        : ResourceEffect
    {
        public ResourceEffectLocation Source { get; } =
            Source ?? throw new ArgumentNullException(nameof(Source));
        public ResourceEffectLocation Target { get; } =
            Target ?? throw new ArgumentNullException(nameof(Target));
        public ResourcePassIdentity? Identity { get; } = Identity;
    }

    public sealed record Independent(
        ResourceEffectLocation Source,
        ResourceEffectLocation Target)
        : ResourceEffect
    {
        public ResourceEffectLocation Source { get; } =
            Source ?? throw new ArgumentNullException(nameof(Source));
        public ResourceEffectLocation Target { get; } =
            Target ?? throw new ArgumentNullException(nameof(Target));
    }

    public sealed record Callback(
        ResourceEffectLocation.Parameter Delegate,
        ResourceBorrowScope.Callback Scope,
        ResourceCallbackExecution Execution,
        ResourceCallbackCardinality Cardinality)
        : ResourceEffect
    {
        public ResourceEffectLocation.Parameter Delegate { get; } =
            Delegate ?? throw new ArgumentNullException(nameof(Delegate));
        public ResourceBorrowScope.Callback Scope { get; } =
            Scope ?? throw new ArgumentNullException(nameof(Scope));
        public ResourceCallbackExecution Execution { get; } = Execution;
        public ResourceCallbackCardinality Cardinality { get; } = Cardinality;
    }

    public sealed record Accept(
        ResourceEffectLocation Source,
        ResourceEffectLocation Target,
        ResourceEffectCompletion When,
        ResourceKindReference? Kind,
        ResourceEffectLocalIdentity? Order)
        : ResourceEffect
    {
        public ResourceEffectLocation Source { get; } =
            Source ?? throw new ArgumentNullException(nameof(Source));
        public ResourceEffectLocation Target { get; } =
            Target ?? throw new ArgumentNullException(nameof(Target));
        public ResourceEffectCompletion When { get; } =
            When ?? throw new ArgumentNullException(nameof(When));
        public ResourceKindReference? Kind { get; } = Kind;
        public ResourceEffectLocalIdentity? Order { get; } = Order;
    }

    public sealed record Operation(
        ResourceOperationBoundary Boundary,
        ResourceOperationThrows Throws,
        ResourceEffectGuard? Guard)
        : ResourceEffect;

    public sealed record Outcome(
        ResourceEffectLocalIdentity Identity,
        ResourceEffectLocation Source,
        ResourceEffectOutcomeTest Test)
        : ResourceEffect
    {
        public ResourceEffectLocalIdentity Identity { get; } = Identity;
        public ResourceEffectLocation Source { get; } =
            Source ?? throw new ArgumentNullException(nameof(Source));
        public ResourceEffectOutcomeTest Test { get; } =
            Test ?? throw new ArgumentNullException(nameof(Test));
    }
}

public enum ResourceDeclarationAuthority
{
    ProductShipped,
    CallerSupplied,
    ProducerAsserted,
    CompilerAsserted,
}

public sealed record ResourceDeclarationProvenance
{
    public ResourceDeclarationProvenance(
        ResourceEffectModelIdentity model,
        ResourceDeclarationAuthority authority,
        InertString sourceIdentity,
        int declarationOrdinal)
    {
        if (model == default)
            throw new ArgumentException("Model identity must not be default.", nameof(model));
        if (!Enum.IsDefined(authority))
            throw new ArgumentOutOfRangeException(nameof(authority));
        if (sourceIdentity.IsEmpty)
            throw new ArgumentException("Source identity must not be empty.", nameof(sourceIdentity));
        ArgumentOutOfRangeException.ThrowIfNegative(declarationOrdinal);
        Model = model;
        Authority = authority;
        SourceIdentity = sourceIdentity;
        DeclarationOrdinal = declarationOrdinal;
    }

    public ResourceEffectModelIdentity Model { get; }
    public ResourceDeclarationAuthority Authority { get; }
    public InertString SourceIdentity { get; }
    public int DeclarationOrdinal { get; }
}

public sealed record ResourceEffectSourceStatement
{
    public ResourceEffectSourceStatement(
        string text,
        ResourceDeclarationProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(provenance);
        Text = text;
        Provenance = provenance;
    }

    public string Text { get; }
    public ResourceDeclarationProvenance Provenance { get; }
}

public sealed record ResourceEffectTargetDeclaration
{
    readonly ImmutableArray<ResourceEffectSourceStatement> _statements;

    public ResourceEffectTargetDeclaration(
        ResourceEffectTargetSelector target,
        ImmutableArray<ResourceEffectSourceStatement> statements)
    {
        ArgumentNullException.ThrowIfNull(target);
        _statements =
            ImmutableArrayValueEquality.RequireInitialized(statements, nameof(statements));
        Target = target;
    }

    public ResourceEffectTargetSelector Target { get; }
    public ImmutableArray<ResourceEffectSourceStatement> Statements => _statements;
}

public sealed record ResourceEffectModelDefinition
{
    readonly ImmutableArray<ResourceKindDefinition> _resourceKinds;
    readonly ImmutableArray<ResourceEffectTargetDeclaration> _declarations;
    readonly ImmutableArray<NormalizedResourceEffectDeclaration> _normalizedDeclarations;

    public ResourceEffectModelDefinition(
        ResourceEffectLanguageIdentity language,
        ResourceEffectModelIdentity identity,
        ImmutableArray<ResourceKindDefinition> resourceKinds,
        ImmutableArray<ResourceEffectTargetDeclaration> declarations,
        ImmutableArray<NormalizedResourceEffectDeclaration> normalizedDeclarations = default)
    {
        if (language == default)
            throw new ArgumentException("Language identity must not be default.", nameof(language));
        if (identity == default)
            throw new ArgumentException("Model identity must not be default.", nameof(identity));
        _resourceKinds =
            ImmutableArrayValueEquality.RequireInitialized(resourceKinds, nameof(resourceKinds));
        _declarations =
            ImmutableArrayValueEquality.RequireInitialized(declarations, nameof(declarations));
        _normalizedDeclarations = normalizedDeclarations.IsDefault
            ? []
            : ImmutableArrayValueEquality.RequireInitialized(
                normalizedDeclarations,
                nameof(normalizedDeclarations));
        Language = language;
        Identity = identity;
    }

    public ResourceEffectLanguageIdentity Language { get; }
    public ResourceEffectModelIdentity Identity { get; }
    public ImmutableArray<ResourceKindDefinition> ResourceKinds => _resourceKinds;
    public ImmutableArray<ResourceEffectTargetDeclaration> Declarations => _declarations;
    public ImmutableArray<NormalizedResourceEffectDeclaration> NormalizedDeclarations
        => _normalizedDeclarations;
}

public sealed class NormalizedResourceEffectDeclaration
    : IEquatable<NormalizedResourceEffectDeclaration>
{
    readonly ImmutableArray<ResourceDeclarationProvenance> _provenances;

    public NormalizedResourceEffectDeclaration(
        ResourceEffectTargetSelector target,
        ResourceEffect effect,
        ImmutableArray<ResourceDeclarationProvenance> provenances)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(effect);
        _provenances =
            ImmutableArrayValueEquality.RequireInitialized(provenances, nameof(provenances));
        if (_provenances.IsEmpty)
            throw new ArgumentException("A normalized declaration requires provenance.", nameof(provenances));
        Target = target;
        Effect = effect;
    }

    public ResourceEffectTargetSelector Target { get; }
    public ResourceEffect Effect { get; }
    public ImmutableArray<ResourceDeclarationProvenance> Provenances => _provenances;

    public bool Equals(NormalizedResourceEffectDeclaration? other)
        => other is not null
            && Target == other.Target
            && Effect == other.Effect;

    public override bool Equals(object? obj)
        => obj is NormalizedResourceEffectDeclaration other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Target, Effect);
}

static class ResourceEffectIdentifiers
{
    public static void RequireQualified(string value, string parameterName)
    {
        RequireDottedIdentifier(value, requireDot: true, parameterName);
    }

    public static void RequireLocalIdentifier(string value, string parameterName)
        => RequireDottedIdentifier(value, requireDot: false, parameterName, allowDot: false);

    public static void RequireDottedIdentifier(
        string value,
        bool requireDot,
        string parameterName)
        => RequireDottedIdentifier(value, requireDot, parameterName, allowDot: true);

    static void RequireDottedIdentifier(
        string value,
        bool requireDot,
        string parameterName,
        bool allowDot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        int segmentLength = 0;
        bool hasDot = false;
        foreach (char character in value)
        {
            if (character == '.')
            {
                if (!allowDot)
                    throw new ArgumentException("A local identity cannot contain dot.", parameterName);
                if (segmentLength == 0)
                    throw new ArgumentException("Identifier segments must not be empty.", parameterName);
                segmentLength = 0;
                hasDot = true;
                continue;
            }
            if (!(character is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '_'
                    or '-')
                || (segmentLength == 0 && character is >= '0' and <= '9'))
            {
                throw new ArgumentException(
                    "Identifiers use ASCII letters, digits, underscore, hyphen, and dot.",
                    parameterName);
            }
            segmentLength++;
        }
        if (segmentLength == 0)
            throw new ArgumentException("Identifier segments must not be empty.", parameterName);
        if (requireDot && !hasDot)
        {
            throw new ArgumentException(
                "A qualified identity must contain at least two dot-separated segments.",
                parameterName);
        }
    }

    public static void RequireAssemblyName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character =>
                !(character is >= 'a' and <= 'z'
                    or >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '.'
                    or '_'
                    or '-')))
        {
            throw new ArgumentException("Assembly simple names must use bounded ASCII spelling.", parameterName);
        }
    }
}
