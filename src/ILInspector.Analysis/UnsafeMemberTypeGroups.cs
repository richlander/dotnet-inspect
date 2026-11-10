using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// How many census findings carry each unsafe member dimension: presence,
/// public exposure, propagation, and safe boundary. Safe-boundary counts only
/// decided members; <see cref="SafeBoundaryUndecided"/> counts the members
/// whose module rules make the question inapplicable or unknown.
/// </summary>
public sealed record UnsafeMemberCounts(
    int Members,
    int Public,
    int Propagating,
    int SafeBoundaries,
    int SafeBoundaryUndecided)
{
    public static UnsafeMemberCounts Of(IEnumerable<UnsafeMemberFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        int members = 0, exposed = 0, propagating = 0, boundaries = 0, undecided = 0;
        foreach (UnsafeMemberFinding finding in findings)
        {
            members++;
            if (finding.Exposure == UnsafeMemberExposure.Public)
                exposed++;
            if (finding.PropagatesUnsafe)
                propagating++;
            switch (finding.SafeBoundary)
            {
                case UnsafeMemberSafeBoundary.Yes:
                    boundaries++;
                    break;
                case UnsafeMemberSafeBoundary.NotApplicable or UnsafeMemberSafeBoundary.Unknown:
                    undecided++;
                    break;
            }
        }
        return new(members, exposed, propagating, boundaries, undecided);
    }
}

/// <summary>
/// The census findings one Type declares, in their given order, with the
/// Type's dimension counts: the Library scope of the unsafe member census's
/// progressive disclosure.
/// </summary>
public sealed record UnsafeMemberTypeGroup(
    TypeRef DeclaringType,
    ImmutableArray<UnsafeMemberFinding> Members,
    UnsafeMemberCounts Counts)
{
    public bool Equals(UnsafeMemberTypeGroup? other)
        => other is not null
            && Equals(DeclaringType, other.DeclaringType)
            && Members.SequenceEqual(other.Members)
            && Equals(Counts, other.Counts);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeclaringType);
        foreach (UnsafeMemberFinding member in Members)
            hash.Add(member);
        hash.Add(Counts);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Groups findings by declaring Type. Types appear in the order of their
    /// first finding, and each keeps its findings in the given order, so a
    /// consumer that orders the census once orders both scopes.
    /// </summary>
    public static ImmutableArray<UnsafeMemberTypeGroup> Group(
        IEnumerable<UnsafeMemberFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var order = new List<TypeRef>();
        var byType = new Dictionary<TypeRef, ImmutableArray<UnsafeMemberFinding>.Builder>();
        foreach (UnsafeMemberFinding finding in findings)
        {
            TypeRef type = finding.Member.DeclaringType;
            if (!byType.TryGetValue(type, out var members))
            {
                members = ImmutableArray.CreateBuilder<UnsafeMemberFinding>();
                byType.Add(type, members);
                order.Add(type);
            }
            members.Add(finding);
        }
        return
        [
            .. order.Select(type =>
            {
                ImmutableArray<UnsafeMemberFinding> members = byType[type].ToImmutable();
                return new UnsafeMemberTypeGroup(type, members, UnsafeMemberCounts.Of(members));
            }),
        ];
    }
}
