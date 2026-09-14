using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>
/// Retained Navigation copies participant failure evidence rather than retaining
/// producer registrations or exceptions. Stateless inventory classification keeps
/// its original owner-issued producer results.
/// </summary>
internal static class NavigationSnapshotDetachment
{
    internal static NavigationWorkspaceSnapshot Detach(NavigationWorkspaceSnapshot snapshot)
    {
        NavigationSubjectInventory? inventory = snapshot.Inventory;
        if (inventory is null || !inventory.Types.Evidence.Any(NeedsDetachment))
            return snapshot;
        ImmutableArray<NavigationLibraryInventory> libraries =
            [.. inventory.Libraries.Select(library =>
                new NavigationLibraryInventory(library.Subject, library.IsPrimary, Detach(library.Types)))];
        ImmutableArray<NavigationInventoryEvidence> evidence = [.. libraries.SelectMany(library => library.Types.Evidence)];
        NavigationTypeInventoryOutcome types = WithEvidence(inventory.Types, evidence);
        return new(
            snapshot.Scope, snapshot.Workspace, snapshot.ActiveOccurrence, snapshot.ActiveSubject,
            snapshot.RetainedContext, snapshot.TypeInventoryLibraryContext,
            snapshot.Packages, snapshot.Hierarchy, snapshot.Libraries, snapshot.Types, snapshot.Members,
            snapshot.Lenses, snapshot.LensOutcome,
            new(inventory.Package, libraries, types, inventory.InitialCandidates), snapshot.DescendantLenses);
    }

    internal static NavigationTypeInventoryOutcome Detach(NavigationTypeInventoryOutcome inventory) =>
        inventory.Evidence.Any(NeedsDetachment)
            ? WithEvidence(inventory, [.. inventory.Evidence.Select(Detach)])
            : inventory;

    static bool NeedsDetachment(NavigationInventoryEvidence evidence) =>
        evidence is NavigationInventoryEvidence.ParticipantFailed or NavigationInventoryEvidence.ParticipantRejected;

    static NavigationTypeInventoryOutcome WithEvidence(
        NavigationTypeInventoryOutcome inventory, ImmutableArray<NavigationInventoryEvidence> evidence) =>
        inventory switch
        {
            NavigationTypeInventoryOutcome.Available => new NavigationTypeInventoryOutcome.Available(inventory.Rows, evidence),
            NavigationTypeInventoryOutcome.Failed => new NavigationTypeInventoryOutcome.Failed(evidence),
            NavigationTypeInventoryOutcome.Unavailable => inventory,
            _ => throw new InvalidOperationException("Unknown Navigation inventory outcome."),
        };

    static NavigationInventoryEvidence Detach(NavigationInventoryEvidence evidence) =>
        evidence switch
        {
            NavigationInventoryEvidence.ParticipantRejected rejected =>
                new NavigationInventoryEvidence.DetachedParticipantRejected(
                    rejected.Library,
                    new(rejected.ProducerSubject.Registration, rejected.ProducerSubject.Identity, rejected.ProducerSubject.Provenance),
                    rejected.Failure),
            NavigationInventoryEvidence.ParticipantFailed failed =>
                new NavigationInventoryEvidence.DetachedParticipantFailed(
                    failed.Library,
                    new(failed.ProducerSubject.Registration, failed.ProducerSubject.Identity, failed.ProducerSubject.Provenance),
                    new(NavigationExceptionIdentity.From(failed.Error),
                        failed.Error.GetType().FullName ?? failed.Error.GetType().Name,
                        failed.Error.HResult, failed.Error.Message, failed.Error.ToString())),
            _ => evidence,
        };
}
