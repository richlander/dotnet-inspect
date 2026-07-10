using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public enum CSharpBodyPolicy
{
    Skeleton,
    Full,
    Stub
}

public abstract record CSharpMemberBody;

public sealed record CSharpBlockBody(string Source) : CSharpMemberBody;

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

public enum CSharpNamespaceStyle
{
    FileScoped,
    BlockScoped
}

public sealed record CSharpTypePrintOptions
{
    public bool IncludeCustomAttributes { get; init; }

    public CSharpNamespaceStyle NamespaceStyle { get; init; } = CSharpNamespaceStyle.FileScoped;
}

public sealed record CSharpTypeSourceUnit(string? Namespace, string Source);

public sealed record CSharpTypePrintDiagnostic(string TypeName, string Message);

public sealed record CSharpTypePrintResult(
    ImmutableArray<CSharpTypeSourceUnit> Units,
    ImmutableArray<CSharpTypePrintDiagnostic> Diagnostics);
