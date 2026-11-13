using System.Collections.Immutable;

namespace DotnetInspector.Sections;

public sealed record DependencyEcosystemCompositionRow(
    string EcosystemId,
    string EcosystemTitle,
    int PackageVersions);

public sealed record DependencyEcosystemCompositionDocument(
    int PackageVersions,
    int UnclassifiedPackageVersions,
    ImmutableArray<DependencyEcosystemCompositionRow> Ecosystems,
    ImmutableArray<string> Targets,
    bool IsComplete)
{
    public bool Equals(DependencyEcosystemCompositionDocument? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && PackageVersions == other.PackageVersions
        && UnclassifiedPackageVersions == other.UnclassifiedPackageVersions
        && IsComplete == other.IsComplete
        && DependencyValueEquality.SequenceEqual(Ecosystems, other.Ecosystems)
        && DependencyValueEquality.SequenceEqual(Targets, other.Targets);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PackageVersions);
        hash.Add(UnclassifiedPackageVersions);
        hash.Add(IsComplete);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Ecosystems);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Targets);
        return hash.ToHashCode();
    }
}
