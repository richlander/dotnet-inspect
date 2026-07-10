using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public enum CSharpBodyPolicy
{
    Skeleton,
    Full,
    Stub
}

public sealed record CSharpMemberPolicy
{
    public CSharpMemberPolicy(ApiMember member, CSharpBodyPolicy bodyPolicy)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (!Enum.IsDefined(bodyPolicy))
            throw new ArgumentOutOfRangeException(nameof(bodyPolicy));

        Member = member;
        BodyPolicy = bodyPolicy;
    }

    public ApiMember Member { get; }

    public CSharpBodyPolicy BodyPolicy { get; }
}

public sealed record CSharpTypePrintRequest
{
    public CSharpTypePrintRequest(
        ApiType type,
        CSharpBodyPolicy bodyPolicy = CSharpBodyPolicy.Skeleton,
        IReadOnlyList<ApiMember>? members = null,
        IReadOnlyList<CSharpMemberPolicy>? memberPolicyOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!Enum.IsDefined(bodyPolicy))
            throw new ArgumentOutOfRangeException(nameof(bodyPolicy));
        if (members?.Any(member => member is null) == true)
            throw new ArgumentException("Type print members cannot contain null entries.", nameof(members));
        if (memberPolicyOverrides?.Any(policy => policy is null) == true)
        {
            throw new ArgumentException(
                "Member policy overrides cannot contain null entries.",
                nameof(memberPolicyOverrides));
        }

        Type = type;
        BodyPolicy = bodyPolicy;
        Members = members?.ToArray();
        MemberPolicyOverrides = memberPolicyOverrides?.ToArray() ?? [];
    }

    public ApiType Type { get; }

    public CSharpBodyPolicy BodyPolicy { get; }

    public IReadOnlyList<ApiMember>? Members { get; }

    public IReadOnlyList<CSharpMemberPolicy> MemberPolicyOverrides { get; }
}

public sealed record CSharpTypePrintOptions
{
    public bool IncludeCustomAttributes { get; init; }
}

public sealed record CSharpTypeSourceUnit(string? Namespace, string Source);

public sealed record CSharpTypePrintDiagnostic(string TypeName, string Message);

public sealed record CSharpTypePrintResult(
    ImmutableArray<CSharpTypeSourceUnit> Units,
    ImmutableArray<CSharpTypePrintDiagnostic> Diagnostics);
