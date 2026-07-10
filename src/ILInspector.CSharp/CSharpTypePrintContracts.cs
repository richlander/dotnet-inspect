using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public enum CSharpBodyPolicy
{
    Skeleton,
    Full,
    Stub
}

public sealed record CSharpMemberPolicy(ApiMember Member, CSharpBodyPolicy BodyPolicy);

public sealed class CSharpTypePrintRequest
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

        var memberArray = members?.ToArray();
        if (memberArray?.Any(member => member is null) == true)
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
        }

        Type = type;
        BodyPolicy = bodyPolicy;
        Members = memberArray;
        MemberPolicyOverrides = memberPolicyArray;
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
