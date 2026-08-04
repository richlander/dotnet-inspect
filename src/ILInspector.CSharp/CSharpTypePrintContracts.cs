using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public enum CSharpBodyPolicy
{
    Skeleton,
    Full,
    Stub
}

public abstract record CSharpMemberBody
{
    /// <summary>
    /// True when this body must be emitted under a C# <c>async</c> modifier.
    /// This is a body-only fact: skeleton declarations deliberately ignore it.
    /// </summary>
    public bool RequiresAsyncModifier { get; init; }

    /// <summary>
    /// True when this body requires an <c>unsafe</c> member context beyond any
    /// unsafe signature already represented by <see cref="ApiMember.IsUnsafe"/>.
    /// </summary>
    public bool RequiresUnsafeModifier { get; init; }

    /// <summary>
    /// True when a finalizer member (<see cref="ApiMember.IsFinalizer"/>) must
    /// be rendered as the literal <c>void Finalize()</c> method rather than the
    /// <c>~Type()</c> destructor syntax, because the body was <em>not</em>
    /// recovered as a canonical destructor (<c>IrFunction.IsDestructor</c>).
    /// A body-only fact: recompiling <c>~Type() { … }</c> re-injects the
    /// mandatory <c>base.Finalize()</c> the compiler forbids writing, so the
    /// destructor spelling is only faithful when that scaffold was recovered.
    /// </summary>
    public bool SuppressDestructorSyntax { get; init; }
}

public enum CSharpConstructorInitializerKind
{
    This,
    Base
}

public sealed class CSharpConstructorInitializer
{
    public CSharpConstructorInitializer(
        CSharpConstructorInitializerKind kind,
        IReadOnlyList<string> arguments)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Any(argument => argument is null))
            throw new ArgumentException("Constructor initializer arguments cannot contain null.", nameof(arguments));

        Kind = kind;
        Arguments = arguments.ToArray();
    }

    public CSharpConstructorInitializerKind Kind { get; }
    public IReadOnlyList<string> Arguments { get; }
}

public sealed record CSharpBlockBody(
    string Source,
    CSharpConstructorInitializer? ConstructorInitializer = null) : CSharpMemberBody;

public sealed record CSharpFieldInitializer(string Source) : CSharpMemberBody;

public enum CSharpAccessorBodyKind
{
    Auto,
    Throw,
    Block
}

public sealed record CSharpAccessorBody(CSharpAccessorBodyKind Kind, string? Source = null)
{
    public static CSharpAccessorBody Auto { get; } = new(CSharpAccessorBodyKind.Auto);

    public static CSharpAccessorBody Throw { get; } = new(CSharpAccessorBodyKind.Throw);

    public static CSharpAccessorBody Block(string source)
        => new(CSharpAccessorBodyKind.Block, source);
}

public sealed record CSharpPropertyBody(
    CSharpAccessorBody? Getter,
    CSharpAccessorBody? Setter) : CSharpMemberBody;

public sealed record CSharpEventBody(
    CSharpAccessorBody Adder,
    CSharpAccessorBody Remover) : CSharpMemberBody;

public sealed record CSharpMemberPolicy(
    ApiMember Member,
    CSharpBodyPolicy BodyPolicy,
    CSharpMemberBody? Body = null);

public sealed class CSharpTypePrintRequest
{
    public CSharpTypePrintRequest(
        ApiType type,
        CSharpBodyPolicy bodyPolicy = CSharpBodyPolicy.Skeleton,
        IReadOnlyList<ApiMember>? members = null,
        IReadOnlyList<CSharpMemberPolicy>? memberPolicyOverrides = null,
        IReadOnlyList<ApiParameter>? primaryConstructorParameters = null,
        IReadOnlyList<CSharpTypePrintRequest>? nestedTypes = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!Enum.IsDefined(bodyPolicy))
            throw new ArgumentOutOfRangeException(nameof(bodyPolicy));

        var memberArray = (members ?? type.Members
            ?? throw new ArgumentException(
                $"Type '{type.FullName}' has a null member collection.",
                nameof(type)))
            .ToArray();
        if (memberArray.Any(member => member is null))
            throw new ArgumentException("Type print members cannot contain null entries.", nameof(members));

        var memberPolicyArray = memberPolicyOverrides?.ToArray() ?? [];
        if (memberPolicyArray.Any(policy => policy is null))
        {
            throw new ArgumentException(
                "Member policy overrides cannot contain null entries.",
                nameof(memberPolicyOverrides));
        }
        foreach (var policy in memberPolicyArray)
        {
            if (policy.Member is null)
            {
                throw new ArgumentException(
                    "Member policy overrides require a member.",
                    nameof(memberPolicyOverrides));
            }
            if (!Enum.IsDefined(policy.BodyPolicy))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(memberPolicyOverrides),
                    policy.BodyPolicy,
                    "Member policy overrides require a defined body policy.");
            }
            ValidateBody(policy.Body, nameof(memberPolicyOverrides));
        }

        var primaryConstructorParameterArray = primaryConstructorParameters?.ToArray() ?? [];
        if (primaryConstructorParameterArray.Any(parameter => parameter is null))
        {
            throw new ArgumentException(
                "Primary-constructor parameters cannot contain null entries.",
                nameof(primaryConstructorParameters));
        }

        var nestedTypeArray = nestedTypes?.ToArray() ?? [];
        if (nestedTypeArray.Any(request => request is null))
            throw new ArgumentException("Nested type requests cannot contain null entries.", nameof(nestedTypes));

        Type = type;
        BodyPolicy = bodyPolicy;
        Members = memberArray;
        MemberPolicyOverrides = memberPolicyArray;
        PrimaryConstructorParameters = primaryConstructorParameterArray;
        NestedTypes = nestedTypeArray;
    }

    public ApiType Type { get; }

    public string Namespace => Type.Namespace ?? "";

    public string Name => Type.Name;

    public string Kind => Type.Kind;

    public IReadOnlyList<TypeParameter> TypeParameters => Type.TypeParameters;

    public CSharpBodyPolicy BodyPolicy { get; }

    public IReadOnlyList<ApiMember> Members { get; }

    public IReadOnlyList<CSharpMemberPolicy> MemberPolicyOverrides { get; }

    public IReadOnlyList<ApiParameter> PrimaryConstructorParameters { get; }

    public IReadOnlyList<CSharpTypePrintRequest> NestedTypes { get; }

    static void ValidateBody(CSharpMemberBody? body, string parameterName)
    {
        switch (body)
        {
            case null:
                return;
            case CSharpBlockBody { Source: null }:
            case CSharpFieldInitializer { Source: null }:
                throw new ArgumentException("Member body source cannot be null.", parameterName);
            case CSharpPropertyBody property:
                ValidateAccessor(property.Getter, parameterName);
                ValidateAccessor(property.Setter, parameterName);
                return;
            case CSharpEventBody { Adder: null } or CSharpEventBody { Remover: null }:
                throw new ArgumentException("Event bodies require add and remove accessors.", parameterName);
            case CSharpEventBody eventBody:
                ValidateAccessor(eventBody.Adder, parameterName);
                ValidateAccessor(eventBody.Remover, parameterName);
                return;
            case CSharpBlockBody:
            case CSharpFieldInitializer:
                return;
            default:
                throw new ArgumentException(
                    $"Unsupported member body shape '{body.GetType().Name}'.",
                    parameterName);
        }
    }

    static void ValidateAccessor(CSharpAccessorBody? accessor, string parameterName)
    {
        if (accessor is null)
            return;
        if (!Enum.IsDefined(accessor.Kind))
            throw new ArgumentOutOfRangeException(parameterName, accessor.Kind, "Accessor body kind must be defined.");
        if (accessor.Kind == CSharpAccessorBodyKind.Block && accessor.Source is null)
            throw new ArgumentException("Block accessor source cannot be null.", parameterName);
        if (accessor.Kind != CSharpAccessorBodyKind.Block && accessor.Source is not null)
            throw new ArgumentException("Only block accessors can carry source.", parameterName);
    }
}

public sealed record CSharpTypePrintOptions
{
    public bool IncludeCustomAttributes { get; init; }

    /// <summary>
    /// Namespaces to emit as <c>using</c> directives in the composed
    /// <see cref="CSharpTypePrintResult.Source"/>. Escaped, de-duplicated, and
    /// ordinal-ordered at composition time. Ignored when <see cref="IncludeUsings"/>
    /// is false. The per-type <see cref="CSharpTypePrintResult.Units"/> never carry
    /// their own using directives; under
    /// <see cref="CSharpTypeNamePolicy.ShortWithUsings"/> or
    /// <see cref="CSharpTypeNamePolicy.ContextualShort"/> a unit's source may be
    /// file-context-relative. A per-unit consumer must compose against
    /// <see cref="CSharpTypePrintResult.Source"/> or select
    /// <see cref="CSharpTypeNamePolicy.Qualified"/> for self-contained unit source.
    /// </summary>
    public IReadOnlyList<string> Usings { get; init; } = [];

    /// <summary>
    /// When true (the default), <see cref="Usings"/> are emitted in the composed
    /// source. Set false to suppress the using block entirely.
    /// </summary>
    public bool IncludeUsings { get; init; } = true;

    /// <summary>
    /// Assembly-level attribute bodies (without the surrounding <c>[assembly: ]</c>)
    /// emitted at the top of the composed source. Empty by default.
    /// </summary>
    public IReadOnlyList<string> AssemblyAttributes { get; init; } = [];

    /// <summary>
    /// Module-level attribute bodies (without the surrounding <c>[module: ]</c>)
    /// emitted at the top of the composed source. Empty by default.
    /// </summary>
    public IReadOnlyList<string> ModuleAttributes { get; init; } = [];

    /// <summary>
    /// Controls type-name spelling across the complete output unit. The default
    /// preserves the existing type-printer behavior by deriving imports for the unit.
    /// <see cref="CSharpTypeNamePolicy.Qualified"/> keeps references qualified, while
    /// <see cref="CSharpTypeNamePolicy.ContextualShort"/> uses only caller context.
    /// </summary>
    public CSharpTypeNamePolicy TypeNamePolicy { get; init; } =
        CSharpTypeNamePolicy.ShortWithUsings;

    /// <summary>
    /// When true, the composed source begins with <c>#pragma warning disable</c>.
    /// Off by default; compile-back callers opt in.
    /// </summary>
    public bool EmitPragmaWarningDisable { get; init; }
}

/// <summary>
/// One rendered type declaration. Under
/// <see cref="CSharpTypeNamePolicy.ShortWithUsings"/> or
/// <see cref="CSharpTypeNamePolicy.ContextualShort"/> the <paramref name="Source"/>
/// may be file-context-relative.
/// </summary>
public sealed record CSharpTypeSourceUnit(string? Namespace, string Source);

public sealed record CSharpTypePrintDiagnostic(string TypeName, string Message);

public sealed record CSharpTypePrintResult
{
    readonly Lazy<string> _source;

    public CSharpTypePrintResult(
        ImmutableArray<CSharpTypeSourceUnit> units,
        ImmutableArray<CSharpTypePrintDiagnostic> diagnostics,
        ImmutableHashSet<string> usings,
        Func<string> sourceFactory)
    {
        ArgumentNullException.ThrowIfNull(usings);
        ArgumentNullException.ThrowIfNull(sourceFactory);
        Units = units;
        Diagnostics = diagnostics;
        Usings = usings.ToImmutableHashSet(StringComparer.Ordinal);
        _source = new Lazy<string>(sourceFactory);
    }

    public ImmutableArray<CSharpTypeSourceUnit> Units { get; }

    public ImmutableArray<CSharpTypePrintDiagnostic> Diagnostics { get; }

    /// <summary>
    /// The immutable set of raw namespace identities emitted as using directives
    /// in <see cref="Source"/>. Names are escaped only while rendering source.
    /// </summary>
    public ImmutableHashSet<string> Usings { get; }

    /// <summary>
    /// The composed compilation-unit source. Composed lazily on first access so
    /// callers that only read <see cref="Units"/> do not pay for it.
    /// </summary>
    public string Source => _source.Value;

    // Value equality excludes the lazy source field so comparison does not force
    // composition or degrade to reference identity of the Lazy wrapper.
    public bool Equals(CSharpTypePrintResult? other)
        => other is not null
            && Units.SequenceEqual(other.Units)
            && Diagnostics.SequenceEqual(other.Diagnostics)
            && Usings.SetEquals(other.Usings);

    public override int GetHashCode()
        => HashCode.Combine(Units.Length, Diagnostics.Length, Usings.Count);
}
