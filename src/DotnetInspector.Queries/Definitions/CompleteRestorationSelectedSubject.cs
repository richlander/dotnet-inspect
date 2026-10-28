using System.Collections.Immutable;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Exact saved subject borrowed only during the receiving operation. Return
/// detached content, not this target or its Root/Library participants.
/// </summary>
public abstract class CompleteRestorationSelectedSubject
{
    private protected CompleteRestorationSelectedSubject(
        PackageRootBinding root,
        WorkspacePackageOccurrenceDescriptor occurrence,
        NavigationLensIdentity lens,
        ImmutableArray<BoundCommittedQuery> queries,
        ImmutableArray<NavigationLibraryEvaluation> queryLibraries)
    {
        Root = root;
        Occurrence = occurrence;
        Lens = lens;
        Queries = queries;
        QueryLibraries = queryLibraries;
    }

    public abstract StructuralSubjectIdentity Subject { get; }
    public PackageRootBinding Root { get; }
    public WorkspacePackageOccurrenceDescriptor Occurrence { get; }
    public NavigationLensIdentity Lens { get; }
    public ImmutableArray<BoundCommittedQuery> Queries { get; }
    public ImmutableArray<NavigationLibraryEvaluation> QueryLibraries { get; }

    public sealed class Package : CompleteRestorationSelectedSubject
    {
        internal Package(StructuralSubjectIdentity.PackageSubject subject,
            PackageRootBinding root, WorkspacePackageOccurrenceDescriptor occurrence,
            NavigationLensIdentity lens, ImmutableArray<BoundCommittedQuery> queries,
            ImmutableArray<NavigationLibraryEvaluation> queryLibraries)
            : base(root, occurrence, lens, queries, queryLibraries) => Subject = subject;

        public override StructuralSubjectIdentity.PackageSubject Subject { get; }
    }

    public sealed class AllLibraries : CompleteRestorationSelectedSubject
    {
        internal AllLibraries(StructuralSubjectIdentity.AllLibrariesSubject subject,
            PackageRootBinding root, NavigationPackageEvaluation package,
            NavigationLensIdentity lens, ImmutableArray<BoundCommittedQuery> queries,
            ImmutableArray<NavigationLibraryEvaluation> queryLibraries)
            : base(root, package.Occurrence, lens, queries, queryLibraries)
        {
            Subject = subject;
            Libraries = package.Libraries;
        }

        public override StructuralSubjectIdentity.AllLibrariesSubject Subject { get; }
        public ImmutableArray<NavigationLibraryEvaluation> Libraries { get; }
    }

    public sealed class Library : CompleteRestorationSelectedSubject
    {
        internal Library(StructuralSubjectIdentity.LibrarySubject subject,
            PackageRootBinding root, WorkspacePackageOccurrenceDescriptor occurrence,
            NavigationLibraryEvaluation library, NavigationLensIdentity lens,
            ImmutableArray<BoundCommittedQuery> queries,
            ImmutableArray<NavigationLibraryEvaluation> queryLibraries)
            : base(root, occurrence, lens, queries, queryLibraries)
        {
            Subject = subject;
            DefiningLibrary = library;
        }

        public override StructuralSubjectIdentity.LibrarySubject Subject { get; }
        public NavigationLibraryEvaluation DefiningLibrary { get; }
    }

    public sealed class Type : CompleteRestorationSelectedSubject
    {
        internal Type(StructuralSubjectIdentity.TypeSubject subject,
            PackageRootBinding root, WorkspacePackageOccurrenceDescriptor occurrence,
            NavigationLibraryEvaluation library, NavigationLensIdentity lens,
            ImmutableArray<BoundCommittedQuery> queries,
            ImmutableArray<NavigationLibraryEvaluation> queryLibraries)
            : base(root, occurrence, lens, queries, queryLibraries)
        {
            Subject = subject;
            DefiningLibrary = library;
        }

        public override StructuralSubjectIdentity.TypeSubject Subject { get; }
        public NavigationLibraryEvaluation DefiningLibrary { get; }
    }

    public sealed class Member : CompleteRestorationSelectedSubject
    {
        internal Member(StructuralSubjectIdentity.MemberSubject subject,
            PackageRootBinding root, WorkspacePackageOccurrenceDescriptor occurrence,
            NavigationLibraryEvaluation library, NavigationLensIdentity lens,
            ImmutableArray<BoundCommittedQuery> queries,
            ImmutableArray<NavigationLibraryEvaluation> queryLibraries)
            : base(root, occurrence, lens, queries, queryLibraries)
        {
            Subject = subject;
            DefiningLibrary = library;
        }

        public override StructuralSubjectIdentity.MemberSubject Subject { get; }
        public NavigationLibraryEvaluation DefiningLibrary { get; }
    }
}

public enum CompleteRestorationSubjectUnavailableReason
{
    NotCaptured,
    UnsupportedSubject,
    LensUnavailable,
    ForeignWorkspace,
    ScopeChanged,
    ArtifactUnavailable,
}

public sealed record CompleteRestorationSubjectFailure(
    CompleteRestorationSubjectUnavailableReason Reason,
    ArtifactRootFailure? ArtifactFailure = null);

public abstract record CompleteRestorationSubjectResult<T>
{
    private protected CompleteRestorationSubjectResult() { }
    public sealed record Completed(T Content) : CompleteRestorationSubjectResult<T>;
    public sealed record Unavailable(CompleteRestorationSubjectFailure Failure)
        : CompleteRestorationSubjectResult<T>;
}

internal abstract record CompleteRestorationSubjectPreparation
{
    private protected CompleteRestorationSubjectPreparation() { }
    internal sealed record Ready(CompleteRestorationSelectedSubject Target)
        : CompleteRestorationSubjectPreparation;
    internal sealed record Unavailable(CompleteRestorationSubjectFailure Failure)
        : CompleteRestorationSubjectPreparation;
}

/// <summary>
/// Borrows the exact saved subject, without selecting by name, acquiring
/// content, translating registrations, or executing any attached query.
/// </summary>
public static class CompleteRestorationSelectedSubjectOperation
{
    public static async ValueTask<CompleteRestorationSubjectResult<T>> ExecuteAsync<T>(
        WorkspaceRealizationOperationLease authority,
        CompleteWorkspaceActivation activation,
        Func<CompleteRestorationSelectedSubject, CancellationToken, ValueTask<T>> inspect,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        using WorkspaceRealizationOperationUse operation = authority.EnterUse();
        return await ExecuteAsync(operation.Workspace, activation, inspect,
            cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<CompleteRestorationSubjectResult<T>> ExecuteAsync<T>(
        InspectionWorkspace workspace,
        CompleteWorkspaceActivation activation,
        Func<CompleteRestorationSelectedSubject, CancellationToken, ValueTask<T>> inspect,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        return await ExecuteAsync(workspace, activation,
            (target, _, token) => inspect(target, token), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Uses the same borrow for the subject and its assembly roles.</summary>
    public static async ValueTask<CompleteRestorationSubjectResult<T>> ExecuteAsync<T>(
        InspectionWorkspace workspace,
        CompleteWorkspaceActivation activation,
        Func<CompleteRestorationSelectedSubject, PackageAssemblyContextRealization,
            CancellationToken, ValueTask<T>> inspect,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(inspect);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(workspace.Identity, activation.Workspace))
            return Unavailable<T>(CompleteRestorationSubjectUnavailableReason.ForeignWorkspace);
        if (activation.SelectedSubject is not { } prepared)
            return Unavailable<T>(CompleteRestorationSubjectUnavailableReason.NotCaptured);
        if (prepared is CompleteRestorationSubjectPreparation.Unavailable unavailableTarget)
            return new CompleteRestorationSubjectResult<T>.Unavailable(unavailableTarget.Failure);

        CompleteRestorationSelectedSubject target =
            ((CompleteRestorationSubjectPreparation.Ready)prepared).Target;
        WorkspaceScopeReadResult scope = await workspace.GetScopeSnapshotAsync()
            .ConfigureAwait(false);
        if (scope is WorkspaceScopeReadResult.Unavailable unavailable)
            return Unavailable<T>(CompleteRestorationSubjectUnavailableReason.ArtifactUnavailable,
                unavailable.RuntimeFailure);
        if (!ReferenceEquals(((WorkspaceScopeReadResult.Available)scope).Snapshot.Revision,
            activation.Snapshot.Scope.Revision))
            return Unavailable<T>(CompleteRestorationSubjectUnavailableReason.ScopeChanged);

        ArtifactRootResult<T> result = await workspace.ExecutePackageRootQueryAsync(
            (PackageArtifactRootCorrespondence)target.Occurrence.Occurrence.Correspondence,
            ((ArtifactRootRealizationStatus.Ready)target.Occurrence.Realization.Status).Generation,
            (roles, token) => inspect(target, roles, token),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result switch
        {
            ArtifactRootResult<T>.Available available =>
                new CompleteRestorationSubjectResult<T>.Completed(available.Value),
            ArtifactRootResult<T>.Rejected rejected =>
                Unavailable<T>(CompleteRestorationSubjectUnavailableReason.ArtifactUnavailable,
                    rejected.Failure),
            _ => throw new InvalidOperationException("Unknown Root query result."),
        };
    }

    static CompleteRestorationSubjectResult<T> Unavailable<T>(
        CompleteRestorationSubjectUnavailableReason reason,
        ArtifactRootFailure? failure = null) =>
        new CompleteRestorationSubjectResult<T>.Unavailable(new(reason, failure));

    internal static CompleteRestorationSubjectPreparation Prepare(
        CompleteWorkspaceSnapshot snapshot,
        ResolvedCommittedPackageViewState? selected,
        PackageRootBinding? root)
    {
        NavigationWorkspaceSnapshot navigation = snapshot.Navigation.State.CurrentSnapshot;
        if (selected is null)
            return new CompleteRestorationSubjectPreparation.Unavailable(
                new(CompleteRestorationSubjectUnavailableReason.UnsupportedSubject));
        if (navigation.LensOutcome.EffectiveLens is not { } lens)
            return new CompleteRestorationSubjectPreparation.Unavailable(
                new(CompleteRestorationSubjectUnavailableReason.LensUnavailable));

        CommittedScenarioDefinitionSet definitions = snapshot.Resolved switch
        {
            CompleteRestorationResolvedState.Version2 v => v.Definitions,
            CompleteRestorationResolvedState.Version3 v => v.Definitions,
            CompleteRestorationResolvedState.Version4 v => v.Definitions,
            CompleteRestorationResolvedState.Version5 v => v.Definitions,
            _ => throw new InvalidOperationException("Unknown committed scenario."),
        };
        // Registry binding order follows view-state order and each state's
        // query order, including repeated query definitions on distinct states.
        int queryOffset = 0;
        foreach (CommittedViewStateDefinition state in definitions.View!.States)
        {
            if (ReferenceEquals(state, selected.Definition))
                break;
            queryOffset += state.Queries.Count;
        }
        ImmutableArray<BoundCommittedQuery> queries =
            [.. definitions.QueryBindings.Skip(queryOffset).Take(selected.Definition.Queries.Count)];
        NavigationPackageEvaluation package = selected.Package;
        WorkspacePackageOccurrenceDescriptor occurrence = package.Occurrence;
        NavigationLibraryEvaluation Library(StructuralSubjectIdentity.LibrarySubject subject) =>
            package.Libraries.Single(candidate => ReferenceEquals(
                NavigationRegistrationIdentity.From(candidate.Library.Participant.Assembly.Registration),
                subject.Identity.Registration));
        CompleteRestorationSelectedSubject target = navigation.ActiveSubject switch
        {
            StructuralSubjectIdentity.PackageSubject subject =>
                new CompleteRestorationSelectedSubject.Package(subject, root!, occurrence,
                    lens, queries, selected.Libraries),
            StructuralSubjectIdentity.AllLibrariesSubject subject =>
                new CompleteRestorationSelectedSubject.AllLibraries(subject, root!, package,
                    lens, queries, selected.Libraries),
            StructuralSubjectIdentity.LibrarySubject subject =>
                new CompleteRestorationSelectedSubject.Library(subject, root!, occurrence,
                    Library(subject), lens, queries, selected.Libraries),
            StructuralSubjectIdentity.TypeSubject subject =>
                new CompleteRestorationSelectedSubject.Type(subject, root!, occurrence,
                    Library(subject.Library), lens, queries, selected.Libraries),
            StructuralSubjectIdentity.MemberSubject subject =>
                new CompleteRestorationSelectedSubject.Member(subject, root!, occurrence,
                    Library(subject.DeclaringType.Library), lens, queries, selected.Libraries),
            _ => throw new InvalidOperationException("A selected Package state requires its subject."),
        };
        return new CompleteRestorationSubjectPreparation.Ready(target);
    }

    internal static CompleteRestorationSubjectPreparation PreparePackageType(
        CompleteWorkspaceSnapshot snapshot,
        NavigationPackageEvaluation package,
        PackageRootBinding root,
        NavigationLibraryEvaluation library)
    {
        NavigationWorkspaceSnapshot navigation =
            snapshot.Navigation.State.CurrentSnapshot;
        if (navigation.LensOutcome.EffectiveLens is not { } lens)
        {
            return new CompleteRestorationSubjectPreparation.Unavailable(
                new(
                    CompleteRestorationSubjectUnavailableReason
                        .LensUnavailable));
        }
        if (navigation.ActiveSubject
            is not StructuralSubjectIdentity.TypeSubject subject
            || !ReferenceEquals(
                NavigationRegistrationIdentity.From(
                    library.Library.Participant.Assembly.Registration),
                subject.Library.Identity.Registration))
        {
            return new CompleteRestorationSubjectPreparation.Unavailable(
                new(
                    CompleteRestorationSubjectUnavailableReason
                        .UnsupportedSubject));
        }

        var target = new CompleteRestorationSelectedSubject.Type(
            subject,
            root,
            package.Occurrence,
            library,
            lens,
            [],
            []);
        return new CompleteRestorationSubjectPreparation.Ready(target);
    }
}
