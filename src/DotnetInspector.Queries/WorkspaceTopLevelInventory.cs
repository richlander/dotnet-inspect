using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum WorkspaceTopLevelInventoryEntryKind
{
    Package,
    ExactLibrary,
    PackagePrefix,
    Ecosystem,
}

public sealed record WorkspaceTopLevelInventoryKindFilter
{
    public WorkspaceTopLevelInventoryKindFilter(
        IEnumerable<WorkspaceTopLevelInventoryEntryKind> kinds)
        : this((kinds ?? throw new ArgumentNullException(nameof(kinds)))
            .ToImmutableArray())
    {
    }

    [JsonConstructor]
    public WorkspaceTopLevelInventoryKindFilter(
        ImmutableArray<WorkspaceTopLevelInventoryEntryKind> kinds)
    {
        Kinds = kinds;
    }

    public ImmutableArray<WorkspaceTopLevelInventoryEntryKind> Kinds { get; }
}

public sealed record WorkspaceTopLevelInventoryRequest
{
    public WorkspaceTopLevelInventoryRequest()
        : this(filter: null)
    {
    }

    [JsonConstructor]
    public WorkspaceTopLevelInventoryRequest(
        WorkspaceTopLevelInventoryKindFilter? filter)
    {
        Filter = filter;
    }

    public static WorkspaceTopLevelInventoryRequest All { get; } = new();

    public WorkspaceTopLevelInventoryKindFilter? Filter { get; }
}

public sealed record WorkspaceTopLevelInventoryEntryKey
{
    [JsonConstructor]
    public WorkspaceTopLevelInventoryEntryKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsCanonical(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a canonical Workspace inventory entry key.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    internal static WorkspaceTopLevelInventoryEntryKey Package(int index) =>
        new($"package:{index}");

    internal static WorkspaceTopLevelInventoryEntryKey Registration(int index) =>
        new($"registration:{index}");

    public override string ToString() => Value;

    static bool IsCanonical(string value)
    {
        ReadOnlySpan<char> suffix;
        if (value.StartsWith("package:", StringComparison.Ordinal))
            suffix = value.AsSpan("package:".Length);
        else if (value.StartsWith("registration:", StringComparison.Ordinal))
            suffix = value.AsSpan("registration:".Length);
        else
            return false;

        if (suffix.IsEmpty || (suffix.Length > 1 && suffix[0] == '0'))
            return false;

        foreach (char character in suffix)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return int.TryParse(suffix, out int index) && index >= 0;
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelExactLibraryCoordinate.Package),
    "package")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelExactLibraryCoordinate.Platform),
    "platform")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelExactLibraryCoordinate.Project),
    "project")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelExactLibraryCoordinate.Local),
    "local")]
public abstract record WorkspaceTopLevelExactLibraryCoordinate(
    AssemblyReferenceIdentity LibraryIdentity)
{
    public sealed record Package(
        string PackageId,
        string PackageVersion,
        AssemblyReferenceIdentity LibraryIdentity)
        : WorkspaceTopLevelExactLibraryCoordinate(LibraryIdentity);

    public sealed record Platform(
        PlatformLibraryPopulationDeclaration Population,
        AssemblyReferenceIdentity LibraryIdentity)
        : WorkspaceTopLevelExactLibraryCoordinate(LibraryIdentity);

    public sealed record Project(
        AssemblyReferenceIdentity LibraryIdentity)
        : WorkspaceTopLevelExactLibraryCoordinate(LibraryIdentity);

    public sealed record Local(
        AssemblyReferenceIdentity LibraryIdentity)
        : WorkspaceTopLevelExactLibraryCoordinate(LibraryIdentity);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelEcosystemPopulation.ExactLibrary),
    "exactLibrary")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelEcosystemPopulation.Platform),
    "platform")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelEcosystemPopulation.PackagePrefix),
    "packagePrefix")]
public abstract record WorkspaceTopLevelEcosystemPopulation
{
    private protected WorkspaceTopLevelEcosystemPopulation()
    {
    }

    public sealed record ExactLibrary(
        WorkspaceTopLevelExactLibraryCoordinate Coordinate)
        : WorkspaceTopLevelEcosystemPopulation;

    public sealed record Platform(
        PlatformLibraryPopulationDeclaration Population)
        : WorkspaceTopLevelEcosystemPopulation;

    public sealed record PackagePrefix(
        PackagePrefixDeclaration Prefix)
        : WorkspaceTopLevelEcosystemPopulation;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WorkspaceTopLevelPackageState.Ready), "ready")]
[JsonDerivedType(typeof(WorkspaceTopLevelPackageState.Pending), "pending")]
[JsonDerivedType(typeof(WorkspaceTopLevelPackageState.Failed), "failed")]
public abstract record WorkspaceTopLevelPackageState
{
    private protected WorkspaceTopLevelPackageState()
    {
    }

    public sealed record Ready : WorkspaceTopLevelPackageState;

    public sealed record Pending : WorkspaceTopLevelPackageState;

    public sealed record Failed(ArtifactRootFailure Failure)
        : WorkspaceTopLevelPackageState;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WorkspaceTopLevelPackageEntry), "package")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelExactLibraryEntry),
    "exactLibrary")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelPackagePrefixEntry),
    "packagePrefix")]
[JsonDerivedType(typeof(WorkspaceTopLevelEcosystemEntry), "ecosystem")]
public abstract record WorkspaceTopLevelInventoryEntry(
    WorkspaceTopLevelInventoryEntryKey Key,
    int SourceOrder)
{
    [JsonIgnore]
    public abstract WorkspaceTopLevelInventoryEntryKind Kind { get; }
}

public sealed record WorkspaceTopLevelPackageEntry(
    WorkspaceTopLevelInventoryEntryKey Key,
    int SourceOrder,
    string PackageId,
    string PackageVersion,
    string Producer,
    string? RequestedTargetFramework,
    string? SelectedTargetFramework,
    string? EffectiveTargetFramework,
    string? RuntimeIdentifier,
    PackageCompileAssetSelectionStatus AssetSelectionStatus,
    WorkspaceTopLevelPackageState State)
    : WorkspaceTopLevelInventoryEntry(Key, SourceOrder)
{
    [JsonIgnore]
    public override WorkspaceTopLevelInventoryEntryKind Kind =>
        WorkspaceTopLevelInventoryEntryKind.Package;
}

public sealed record WorkspaceTopLevelExactLibraryEntry(
    WorkspaceTopLevelInventoryEntryKey Key,
    int SourceOrder,
    WorkspaceTopLevelExactLibraryCoordinate Coordinate)
    : WorkspaceTopLevelInventoryEntry(Key, SourceOrder)
{
    [JsonIgnore]
    public override WorkspaceTopLevelInventoryEntryKind Kind =>
        WorkspaceTopLevelInventoryEntryKind.ExactLibrary;
}

public sealed record WorkspaceTopLevelPackagePrefixEntry(
    WorkspaceTopLevelInventoryEntryKey Key,
    int SourceOrder,
    PackagePrefixDeclaration Prefix)
    : WorkspaceTopLevelInventoryEntry(Key, SourceOrder)
{
    [JsonIgnore]
    public override WorkspaceTopLevelInventoryEntryKind Kind =>
        WorkspaceTopLevelInventoryEntryKind.PackagePrefix;
}

public sealed record WorkspaceTopLevelEcosystemEntry(
    WorkspaceTopLevelInventoryEntryKey Key,
    int SourceOrder,
    string Id,
    ImmutableArray<string> NamespaceRoots,
    ImmutableArray<PackageCoordinate> CorePackages,
    ImmutableArray<WorkspaceTopLevelEcosystemPopulation> Populations,
    bool HasIntegrationScanner)
    : WorkspaceTopLevelInventoryEntry(Key, SourceOrder)
{
    [JsonIgnore]
    public override WorkspaceTopLevelInventoryEntryKind Kind =>
        WorkspaceTopLevelInventoryEntryKind.Ecosystem;
}

public sealed record WorkspaceTopLevelPreparation(
    WorkspaceScopeOperationKind Kind,
    int RequestedPackageCount,
    DateTimeOffset Deadline);

public sealed record WorkspaceTopLevelInventoryDocument(
    WorkspaceTopLevelInventoryKindFilter? Filter,
    int TotalEntryCount,
    int SelectedEntryCount,
    ImmutableArray<WorkspaceTopLevelInventoryEntry> Entries,
    WorkspaceTopLevelPreparation? Preparation);

public enum WorkspaceTopLevelInventoryRejection
{
    InvalidFilter,
}

public enum WorkspaceTopLevelInventoryUnavailableReason
{
    InvalidAuthority,
    InvalidShareBasis,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelInventoryOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelInventoryOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(WorkspaceTopLevelInventoryOutcome.Unavailable),
    "unavailable")]
public abstract record WorkspaceTopLevelInventoryOutcome
{
    private protected WorkspaceTopLevelInventoryOutcome()
    {
    }

    public sealed record Available(
        WorkspaceTopLevelInventoryDocument Document)
        : WorkspaceTopLevelInventoryOutcome;

    public sealed record Rejected(
        WorkspaceTopLevelInventoryRejection Reason)
        : WorkspaceTopLevelInventoryOutcome;

    public sealed record Unavailable(
        WorkspaceTopLevelInventoryUnavailableReason Reason)
        : WorkspaceTopLevelInventoryOutcome;
}

internal sealed record WorkspaceTopLevelInventoryQueryExecution(
    WorkspaceTopLevelInventoryOutcome Outcome,
    WorkspaceTopLevelInventorySelectionReceipt Selection);

internal static class WorkspaceTopLevelInventoryQuery
{
    static readonly ImmutableArray<WorkspaceTopLevelInventoryEntryKind>
        s_kindOrder =
        [
            WorkspaceTopLevelInventoryEntryKind.Package,
            WorkspaceTopLevelInventoryEntryKind.ExactLibrary,
            WorkspaceTopLevelInventoryEntryKind.PackagePrefix,
            WorkspaceTopLevelInventoryEntryKind.Ecosystem,
        ];

    internal static WorkspaceTopLevelInventoryQueryExecution Execute(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceScopeSnapshot scope,
        WorkspaceTopLevelInventoryRequest request)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);

        if (!IsValidAuthority(definition, scope))
        {
            return Unavailable(
                WorkspaceTopLevelInventoryUnavailableReason.InvalidAuthority);
        }

        ImmutableArray<ProjectedEntry> complete =
            ProjectCompleteInventory(definition, scope);
        if (!TryNormalizeFilter(request.Filter, out var filter))
        {
            return new(
                new WorkspaceTopLevelInventoryOutcome.Rejected(
                    WorkspaceTopLevelInventoryRejection.InvalidFilter),
                WorkspaceTopLevelInventorySelectionReceipt.Empty);
        }

        var entries =
            ImmutableArray.CreateBuilder<WorkspaceTopLevelInventoryEntry>();
        var selections = ImmutableDictionary.CreateBuilder<
            WorkspaceTopLevelInventoryEntryKey,
            WorkspaceTopLevelInventorySelection>();
        foreach (ProjectedEntry projected in complete)
        {
            if (filter is not null
                && !filter.Kinds.Contains(projected.Entry.Kind))
            {
                continue;
            }

            entries.Add(projected.Entry);
            selections.Add(projected.Entry.Key, projected.Selection);
        }

        ImmutableArray<WorkspaceTopLevelInventoryEntry> selected =
            entries.ToImmutable();
        var document = new WorkspaceTopLevelInventoryDocument(
            filter,
            complete.Length,
            selected.Length,
            selected,
            ProjectPreparation(scope.Preparing));
        return new(
            new WorkspaceTopLevelInventoryOutcome.Available(document),
            WorkspaceTopLevelInventorySelectionReceipt.Create(
                definition,
                selections.ToImmutable()));
    }

    internal static bool IsValidAuthority(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceScopeSnapshot scope) =>
        ReferenceEquals(definition.Workspace, definition.Registrations.Workspace)
        && ReferenceEquals(definition.Workspace, definition.Scope.Workspace)
        && ReferenceEquals(definition.Workspace, scope.Revision.Workspace)
        && ReferenceEquals(definition.Scope, scope.Revision);

    internal static WorkspaceTopLevelInventoryQueryExecution Unavailable(
        WorkspaceTopLevelInventoryUnavailableReason reason) =>
        new(
            new WorkspaceTopLevelInventoryOutcome.Unavailable(reason),
            WorkspaceTopLevelInventorySelectionReceipt.Empty);

    static ImmutableArray<ProjectedEntry> ProjectCompleteInventory(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceScopeSnapshot scope)
    {
        var entries = ImmutableArray.CreateBuilder<ProjectedEntry>(
            scope.Packages.Length
                + definition.Registrations.Registrations.Length);

        for (int index = 0; index < scope.Packages.Length; index++)
        {
            WorkspacePackageOccurrenceDescriptor package =
                scope.Packages[index];
            WorkspacePackageDescriptor descriptor = package.Occurrence.Package;
            RealizedMemberCoordinate.Package coordinate =
                descriptor.Coordinate;
            WorkspaceTopLevelInventoryEntryKey key =
                WorkspaceTopLevelInventoryEntryKey.Package(index);
            entries.Add(new(
                new WorkspaceTopLevelPackageEntry(
                    key,
                    index + 1,
                    descriptor.PackageId,
                    descriptor.PackageVersion,
                    coordinate.Producer,
                    descriptor.RequestedTargetFramework,
                    descriptor.SelectedTargetFramework,
                    descriptor.TargetFramework,
                    descriptor.RuntimeIdentifier,
                    descriptor.SelectionStatus,
                    ProjectPackageState(package.Realization.Status)),
                new WorkspaceTopLevelInventorySelection.Package(
                    package.Occurrence.Identity,
                    index)));
        }

        ImmutableArray<WorkspaceRegistration> registrations =
            definition.Registrations.Registrations;
        for (int index = 0; index < registrations.Length; index++)
        {
            WorkspaceTopLevelInventoryEntryKey key =
                WorkspaceTopLevelInventoryEntryKey.Registration(index);
            WorkspaceRegistration registration = registrations[index];
            WorkspaceTopLevelInventoryEntry entry = registration switch
            {
                WorkspaceRegistration.ExactLibrary library =>
                    new WorkspaceTopLevelExactLibraryEntry(
                        key,
                        index + 1,
                        ProjectExactLibraryCoordinate(library.Coordinate)),
                WorkspaceRegistration.PackagePrefix prefix =>
                    new WorkspaceTopLevelPackagePrefixEntry(
                        key,
                        index + 1,
                        prefix.Prefix),
                WorkspaceRegistration.Ecosystem ecosystem =>
                    ProjectEcosystem(key, index + 1, ecosystem.Declaration),
                _ => throw new InvalidOperationException(
                    "Unknown Workspace registration arm."),
            };
            entries.Add(new(
                entry,
                new WorkspaceTopLevelInventorySelection.Registration(
                    entry.Kind,
                    index)));
        }

        return entries.MoveToImmutable();
    }

    static WorkspaceTopLevelPackageState ProjectPackageState(
        ArtifactRootRealizationStatus status) =>
        status switch
        {
            ArtifactRootRealizationStatus.Ready =>
                new WorkspaceTopLevelPackageState.Ready(),
            ArtifactRootRealizationStatus.Pending =>
                new WorkspaceTopLevelPackageState.Pending(),
            ArtifactRootRealizationStatus.Failed failed =>
                new WorkspaceTopLevelPackageState.Failed(failed.Failure),
            _ => throw new InvalidOperationException(
                "Unknown artifact-root realization state."),
        };

    static WorkspaceTopLevelExactLibraryCoordinate
        ProjectExactLibraryCoordinate(
            ExactLibrarySourceCoordinate coordinate)
    {
        AssemblyReferenceIdentity identity =
            coordinate.LibraryIdentity.Identity;
        return coordinate switch
        {
            ExactLibrarySourceCoordinate.Package package =>
                new WorkspaceTopLevelExactLibraryCoordinate.Package(
                    package.PackageCoordinate.PackageId,
                    package.PackageCoordinate.Version,
                    identity),
            ExactLibrarySourceCoordinate.Platform platform =>
                new WorkspaceTopLevelExactLibraryCoordinate.Platform(
                    platform.Population,
                    identity),
            ExactLibrarySourceCoordinate.Project =>
                new WorkspaceTopLevelExactLibraryCoordinate.Project(identity),
            ExactLibrarySourceCoordinate.Local =>
                new WorkspaceTopLevelExactLibraryCoordinate.Local(identity),
            _ => throw new InvalidOperationException(
                "Unknown exact-Library source coordinate."),
        };
    }

    static WorkspaceTopLevelEcosystemEntry ProjectEcosystem(
        WorkspaceTopLevelInventoryEntryKey key,
        int sourceOrder,
        WorkspaceEcosystemRegistrationDeclaration declaration)
    {
        var populations = ImmutableArray.CreateBuilder<
            WorkspaceTopLevelEcosystemPopulation>(
                declaration.Populations.Length);
        foreach (WorkspaceEcosystemPopulationDeclaration population
            in declaration.Populations)
        {
            populations.Add(population switch
            {
                WorkspaceEcosystemPopulationDeclaration.ExactLibrary library =>
                    new WorkspaceTopLevelEcosystemPopulation.ExactLibrary(
                        ProjectExactLibraryCoordinate(library.Coordinate)),
                WorkspaceEcosystemPopulationDeclaration.Platform platform =>
                    new WorkspaceTopLevelEcosystemPopulation.Platform(
                        platform.Population),
                WorkspaceEcosystemPopulationDeclaration.PackagePrefix prefix =>
                    new WorkspaceTopLevelEcosystemPopulation.PackagePrefix(
                        prefix.Prefix),
                _ => throw new InvalidOperationException(
                    "Unknown Workspace ecosystem population arm."),
            });
        }

        return new(
            key,
            sourceOrder,
            declaration.Id.Value,
            declaration.NamespaceRoots,
            declaration.CorePackages,
            populations.MoveToImmutable(),
            declaration.IntegrationScanner is not null);
    }

    static WorkspaceTopLevelPreparation? ProjectPreparation(
        WorkspaceScopePreparationDescriptor? preparation) =>
        preparation is null
            ? null
            : new(
                preparation.Kind,
                preparation.RequestedPackageCount,
                preparation.Deadline);

    static bool TryNormalizeFilter(
        WorkspaceTopLevelInventoryKindFilter? requested,
        out WorkspaceTopLevelInventoryKindFilter? normalized)
    {
        normalized = null;
        if (requested is null)
            return true;
        if (requested.Kinds.IsDefaultOrEmpty)
            return false;

        var selected = new HashSet<WorkspaceTopLevelInventoryEntryKind>();
        foreach (WorkspaceTopLevelInventoryEntryKind kind in requested.Kinds)
        {
            if (!Enum.IsDefined(kind))
                return false;
            selected.Add(kind);
        }

        var kinds = ImmutableArray.CreateBuilder<
            WorkspaceTopLevelInventoryEntryKind>(selected.Count);
        foreach (WorkspaceTopLevelInventoryEntryKind kind in s_kindOrder)
        {
            if (selected.Contains(kind))
                kinds.Add(kind);
        }

        normalized = new(kinds.MoveToImmutable());
        return true;
    }

    sealed record ProjectedEntry(
        WorkspaceTopLevelInventoryEntry Entry,
        WorkspaceTopLevelInventorySelection Selection);
}
