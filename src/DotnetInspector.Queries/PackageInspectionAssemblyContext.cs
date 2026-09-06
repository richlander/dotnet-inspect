using System.Collections.Immutable;

using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One projected entry supplied to the host's existing group binding policy.</summary>
public sealed record PackageInspectionAssemblyReference(
    PackageInspectionAssembly Selection,
    ResolvedAssemblyReference Assembly);

public abstract record PackageInspectionAssemblyOutcome(PackageInspectionAssembly Selection)
{
    public sealed record Available(
        PackageInspectionAssembly Selection,
        AssemblyContextGroup Group,
        AssemblyContextParticipant Participant) : PackageInspectionAssemblyOutcome(Selection);

    public sealed record Unavailable(
        PackageInspectionAssembly Selection,
        string Reason,
        ImmutableArray<ArtifactSetAdmissionFailure> PublicationFailures)
        : PackageInspectionAssemblyOutcome(Selection);

    public sealed record WithoutAssembly(
        PackageInspectionAssembly Selection,
        ArtifactAssemblyProjectionOutcome Projection)
        : PackageInspectionAssemblyOutcome(Selection);
}

/// <summary>
/// Exact inspection participants and per-entry failures. Artifact sessions
/// remain owned by the asynchronous workspace after these groups are disposed.
/// </summary>
public sealed class PackageInspectionAssemblyContext : IDisposable
{
    internal PackageInspectionAssemblyContext(
        ImmutableArray<AssemblyContextGroup> groups,
        ImmutableArray<PackageInspectionAssemblyOutcome> assemblies)
    {
        Groups = groups;
        Assemblies = assemblies;
    }

    public ImmutableArray<AssemblyContextGroup> Groups { get; }
    public ImmutableArray<PackageInspectionAssemblyOutcome> Assemblies { get; }

    public void Dispose()
    {
        List<Exception>? failures = null;
        foreach (AssemblyContextGroup group in Groups)
        {
            try
            {
                group.Dispose();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }
        if (failures is not null)
            throw new AggregateException(failures);
    }
}

public sealed partial class InspectionWorkspace
{
    /// <summary>
    /// Realizes an exact inspection selection, preserving its binding contexts
    /// and independent entry outcomes without invoking compile-asset selection.
    /// </summary>
    /// <remarks>
    /// Calls are serialized with other group admissions. The aggregate budget
    /// covers both artifact snapshots and group images; MaxAssembliesPerRole
    /// limits this complete selection. The optional group limit can only reduce
    /// the group's share. The default policy binds only within each group.
    /// </remarks>
    public async ValueTask<PackageInspectionAssemblyContext>
        RealizePackageInspectionAsync(
            PackageInspectionSelection selection,
            Func<ImmutableArray<PackageInspectionAssemblyReference>, IAssemblyBindingPolicy>?
                bindingPolicy = null,
            PackageAssemblyContextRealizationOptions? options = null,
            AssemblyContextGroupOptions? groupOptions = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (_lifetimeMode != InspectionWorkspaceLifetimeMode.Asynchronous)
            throw new InvalidOperationException(
                "Artifact-backed package inspection requires an asynchronous workspace.");
        options ??= new PackageAssemblyContextRealizationOptions();
        options.Validate();
        groupOptions?.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAssetCount(selection.Assemblies.Length, options);
        long remainingBytes = options.MaxAggregateRetainedImageBytes / 2;
        if (remainingBytes <= 0 || remainingBytes > int.MaxValue
            || options.MaxAssemblyEntryBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (!ReferenceEquals(
                selection.Input.ContentGenerationIdentity,
                selection.Input.Content.GenerationIdentity))
            throw new ArgumentException(
                "The inspection input no longer identifies its retained content.",
                nameof(selection));
        if (options.RequireDeclaredEntryLengths
            && selection.Input.Content is not IPackageContentEntryManifest
            && !selection.Assemblies.IsEmpty)
            throw new InvalidOperationException(
                "The selected package content cannot preflight declared entry lengths.");

        var acquired = new List<InspectionArtifact>();
        var groups = new List<AssemblyContextGroup>();
        var outcomes = new Dictionary<PackageInspectionAssembly, PackageInspectionAssemblyOutcome>();
        try
        {
            foreach (PackageInspectionAssembly entry in selection.Assemblies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!selection.Entries.Contains(entry.Path))
                {
                    outcomes.Add(entry, new PackageInspectionAssemblyOutcome.Unavailable(
                        entry, "The selected entry is missing from the retained package content.", []));
                    continue;
                }
                if (remainingBytes == 0)
                {
                    outcomes.Add(entry, new PackageInspectionAssemblyOutcome.Unavailable(
                        entry, "The selected image exceeds the retained artifact budget.", []));
                    continue;
                }
                if (selection.Input.Content is IPackageContentEntryManifest manifest
                    && manifest.TryGetEntryLength(entry.Path, out long declaredLength)
                    && (declaredLength < 0
                        || declaredLength > Math.Min(
                            remainingBytes, options.MaxAssemblyEntryBytes)))
                {
                    outcomes.Add(entry, new PackageInspectionAssemblyOutcome.Unavailable(
                        entry, "The selected image exceeds the configured artifact byte limit.", []));
                    continue;
                }

                var session = new ArtifactSetSession(ArtifactLimits(
                    1, remainingBytes, options));
                ArtifactQueryLease? lease = null;
                try
                {
                    PackageArtifactPublication publication =
                        await PublishPackageArtifactsAsync(
                            session,
                            [new PackageArtifactSource(
                                new PackageInspectionArtifactProvenance(
                                    selection.Input.Coordinate,
                                    selection.Input.SourceCoordinate,
                                    selection.Input.ProducerKey,
                                    selection.Input.ContentGenerationIdentity,
                                    selection.Identity,
                                    entry),
                                token => OpenPackageEntry(
                                    selection.Input.Content, entry.Path,
                                    Math.Min(remainingBytes, options.MaxAssemblyEntryBytes),
                                    token))],
                            cancellationToken).ConfigureAwait(false);
                    if (publication.Rejection is { } rejected)
                    {
                        await session.DisposeAsync().ConfigureAwait(false);
                        if (session.CleanupFailures.Count > 0)
                            throw new AggregateException(
                                [PublicationFailure(rejected), .. session.CleanupFailures]);
                        outcomes.Add(entry, new PackageInspectionAssemblyOutcome.Unavailable(
                            entry,
                            "The selected image could not be read or exceeded the artifact byte limit.",
                            [.. rejected.Failures]));
                        continue;
                    }

                    lease = publication.Lease!;
                    ProjectedPackageArtifact artifact = publication.Artifacts[0];
                    AssemblyResolutionProvenance provenance =
                        selection.Input.Provenance(entry.TargetFramework);
                    if (artifact.Projection is not ArtifactAssemblyProjectionOutcome.Projected
                        && ResolvedAssemblyReference.CreateFromStreamIfManaged(
                            artifact.Content.OpenRead, provenance) is null)
                    {
                        lease.Dispose();
                        await session.DisposeAsync().ConfigureAwait(false);
                        if (session.CleanupFailures.Count > 0)
                            throw new AggregateException(session.CleanupFailures);
                        outcomes.Add(entry, new PackageInspectionAssemblyOutcome.WithoutAssembly(
                            entry, artifact.Projection));
                        continue;
                    }
                    long retainedBytes;
                    using (Stream retained = artifact.Content.OpenRead())
                        retainedBytes = retained.Length;
                    ResolvedAssemblyReference assembly = CreatePackageArtifactAssembly(
                        artifact, provenance, acquired.Count, out _);
                    remainingBytes -= retainedBytes;
                    acquired.Add(new InspectionArtifact(
                        new PackageInspectionAssemblyReference(entry, assembly),
                        session, lease, retainedBytes));
                }
                catch (Exception failure) when (
                    failure is BadImageFormatException or ArgumentOutOfRangeException or OverflowException)
                {
                    await CleanupFailedArtifactRealizationAsync(
                        null, lease, session, failure).ConfigureAwait(false);
                    if (failure.Data.Contains(
                        "DotnetInspector.Artifacts.Workspaces.CleanupFailures"))
                        throw;
                    outcomes.Add(entry, new PackageInspectionAssemblyOutcome.Unavailable(
                        entry, "The selected image contains invalid metadata.", []));
                }
                catch (InvalidDataException failure)
                {
                    await CleanupFailedArtifactRealizationAsync(
                        null, lease, session, failure).ConfigureAwait(false);
                    if (failure.Data.Contains(
                        "DotnetInspector.Artifacts.Workspaces.CleanupFailures"))
                        throw;
                    outcomes.Add(entry, new PackageInspectionAssemblyOutcome.Unavailable(
                        entry, "The selected package entry is invalid or exceeds the artifact byte limit.", []));
                }
                catch (Exception failure)
                {
                    await CleanupFailedArtifactRealizationAsync(
                        null, lease, session, failure).ConfigureAwait(false);
                    throw;
                }
            }

            IGrouping<string, InspectionArtifact>[] contexts =
            [
                .. acquired.GroupBy(
                    item => item.Reference.Selection.ContextKey
                        ?? item.Reference.Selection.TargetFramework ?? "",
                    StringComparer.OrdinalIgnoreCase),
            ];
            foreach (IGrouping<string, InspectionArtifact> context in contexts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ImmutableArray<InspectionArtifact> entries = [.. context];
                ImmutableArray<PackageInspectionAssemblyReference> references =
                    [.. entries.Select(item => item.Reference)];
                IAssemblyBindingPolicy policy = bindingPolicy is null
                    ? new PackageAssemblyContextRoles.RoleBindingPolicy(
                        [.. references.Select(item => item.Assembly)])
                    : bindingPolicy(references);
                ArgumentNullException.ThrowIfNull(policy);
                long groupBudget = entries.Sum(item => item.RetainedBytes);
                AssemblyContextGroup group = CreateAssemblyContextGroup(
                    references.Select(item => new AssemblyContextParticipant(item.Assembly, policy)),
                    new AssemblyContextGroupOptions
                    {
                        MaxRetainedImageBytes = Math.Min(
                            groupBudget, groupOptions?.MaxRetainedImageBytes ?? groupBudget),
                    });
                groups.Add(group);
                for (int index = 0; index < entries.Length; index++)
                {
                    InspectionArtifact artifact = entries[index];
                    outcomes.Add(artifact.Reference.Selection,
                        new PackageInspectionAssemblyOutcome.Available(
                            artifact.Reference.Selection, group, group.Participants[index]));
                    RegisterArtifactSession(artifact.Session, artifact.Lease, [group]);
                    artifact.Transferred = true;
                }
            }

            return new PackageInspectionAssemblyContext(
                [.. groups], [.. selection.Assemblies.Select(entry => outcomes[entry])]);
        }
        catch (Exception failure)
        {
            try
            {
                new PackageInspectionAssemblyContext([.. groups], []).Dispose();
            }
            catch (Exception cleanupFailure)
            {
                failure.Data["DotnetInspector.Queries.GroupCleanupFailure"] = cleanupFailure;
            }
            foreach (InspectionArtifact artifact in acquired.Where(item => !item.Transferred))
                await CleanupFailedArtifactRealizationAsync(
                    null, artifact.Lease, artifact.Session, failure).ConfigureAwait(false);
            throw;
        }
    }

    sealed class InspectionArtifact(
        PackageInspectionAssemblyReference reference,
        ArtifactSetSession session,
        ArtifactQueryLease lease,
        long retainedBytes)
    {
        internal PackageInspectionAssemblyReference Reference { get; } = reference;
        internal ArtifactSetSession Session { get; } = session;
        internal ArtifactQueryLease Lease { get; } = lease;
        internal long RetainedBytes { get; } = retainedBytes;
        internal bool Transferred { get; set; }
    }
}
