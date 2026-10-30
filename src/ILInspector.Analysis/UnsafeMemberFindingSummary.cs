using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// One role of an unsafe member finding without provenance: its occurrence
/// count and, for an explicit-contract call, its distinct callees.
/// </summary>
public sealed record UnsafeMemberRoleSummary(
    UnsafeMemberUseKind Kind,
    int Count,
    ImmutableArray<string> Callees)
{
    public bool Equals(UnsafeMemberRoleSummary? other)
        => other is not null
            && Kind == other.Kind
            && Count == other.Count
            && Callees.SequenceEqual(other.Callees, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Count);
        foreach (string callee in Callees)
            hash.Add(callee, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>
/// An unsafe member finding's value without provenance, as the Unsafe Members
/// row presents it: exposure, explicit contract, and each role in inventory
/// order. Physical bodies, generated names, and IL offsets are provenance and
/// never part of it, so comparison classifies a member as changed only when
/// this value differs.
/// </summary>
public sealed record UnsafeMemberFindingSummary(
    UnsafeMemberExposure Exposure,
    bool HasExplicitUnsafeContract,
    ImmutableArray<UnsafeMemberRoleSummary> Roles)
{
    public static UnsafeMemberFindingSummary Of(UnsafeMemberFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return new(
            finding.Exposure,
            finding.HasExplicitUnsafeContract,
            RolesOf(finding.Evidence));
    }

    /// <summary>Each role in inventory order, with its count and distinct callees.</summary>
    public static ImmutableArray<UnsafeMemberRoleSummary> RolesOf(
        ImmutableArray<UnsafeMemberFindingEvidence> evidence) =>
        [
            .. evidence
                .GroupBy(static item => item.Kind)
                .OrderBy(static group => group.Key)
                .Select(static group => new UnsafeMemberRoleSummary(
                    group.Key,
                    group.Count(),
                    group.Key == UnsafeMemberUseKind.ExplicitContractCall
                        ? [
                            .. group
                                .Select(static item => item.Detail)
                                .Distinct(StringComparer.Ordinal)
                                .Order(StringComparer.Ordinal),
                        ]
                        : [])),
        ];

    /// <summary>The words a safe-boundary state is presented with.</summary>
    public static string SafeBoundaryText(UnsafeMemberSafeBoundary boundary) =>
        boundary switch
        {
            UnsafeMemberSafeBoundary.Yes => "safe boundary",
            UnsafeMemberSafeBoundary.No => "not a safe boundary",
            UnsafeMemberSafeBoundary.NotApplicable => "legacy rules",
            UnsafeMemberSafeBoundary.Unknown => "rules unknown",
            _ => throw new InvalidOperationException(
                $"Unknown safe-boundary state '{boundary}'."),
        };

    /// <summary>The words an exposure is presented with.</summary>
    public static string ExposureText(UnsafeMemberExposure exposure) =>
        exposure switch
        {
            UnsafeMemberExposure.Public => "Public",
            UnsafeMemberExposure.NonPublic => "Non-public",
            UnsafeMemberExposure.Unknown => "Unknown",
            _ => throw new InvalidOperationException(
                $"Unknown unsafe member exposure '{exposure}'."),
        };

    /// <summary>The words a role is presented with.</summary>
    public static string RoleText(UnsafeMemberUseKind kind) =>
        kind switch
        {
            UnsafeMemberUseKind.ExplicitContract => "explicit contract",
            UnsafeMemberUseKind.PointerDereference => "pointer dereference",
            UnsafeMemberUseKind.IndirectCall => "indirect call",
            UnsafeMemberUseKind.StackAllocation => "stack allocation",
            UnsafeMemberUseKind.ExplicitContractCall => "explicit-contract call",
            _ => throw new InvalidOperationException(
                $"Unknown unsafe member role '{kind}'."),
        };

    /// <summary>The text of one role: its words, a count suffix when repeated, and its callees.</summary>
    public static string RoleText(
        UnsafeMemberRoleSummary role,
        Func<string, string> formatCallee)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(formatCallee);
        string words = RoleText(role.Kind);
        string text = role.Count > 1 ? $"{words} ×{role.Count}" : words;
        return role.Callees.IsEmpty
            ? text
            : text + ": " + string.Join(", ", role.Callees.Select(formatCallee));
    }

    /// <summary>
    /// The value as one line: exposure, the explicit contract when present,
    /// and the roles separated by semicolons.
    /// </summary>
    public override string ToString()
    {
        string exposure = ExposureText(Exposure);
        string roles = string.Join(
            "; ",
            Roles.Select(static role => RoleText(role, static callee => callee)));
        return HasExplicitUnsafeContract
            ? $"{exposure}, explicit: {roles}"
            : $"{exposure}: {roles}";
    }

    public bool Equals(UnsafeMemberFindingSummary? other)
        => other is not null
            && Exposure == other.Exposure
            && HasExplicitUnsafeContract == other.HasExplicitUnsafeContract
            && Roles.SequenceEqual(other.Roles);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Exposure);
        hash.Add(HasExplicitUnsafeContract);
        foreach (UnsafeMemberRoleSummary role in Roles)
            hash.Add(role);
        return hash.ToHashCode();
    }
}
