using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>Erasing identity for one exact Scope current-snapshot issuance.</summary>
public sealed class WorkspaceScopePublicationBaseIdentity
{
    internal WorkspaceScopePublicationBaseIdentity() { }
}

/// <summary>Erasing identity for one Scope operation.</summary>
public sealed class WorkspaceScopePublicationOperationIdentity
{
    internal WorkspaceScopePublicationOperationIdentity() { }
}

// The participant seam remains internal; hosts cannot supply callbacks.
internal sealed class WorkspaceScopePublicationCandidateIdentity;

internal abstract class WorkspaceScopePublicationResult;

internal interface IWorkspaceScopePublicationCandidate
{
    InspectionWorkspaceIdentity Workspace { get; }
    WorkspaceScopePublicationBaseIdentity ExpectedBase { get; }
    WorkspaceScopePublicationOperationIdentity Operation { get; }
    WorkspaceScopePublicationCandidateIdentity CandidateSet { get; }

    ArtifactRootResult<WorkspaceScopePreparedCommit> PrepareCommit(
        ArtifactRootCompositionGenerationIdentity currentComposition,
        ArtifactRootCompositionGenerationIdentity candidateComposition,
        ImmutableArray<ArtifactRootScopeProjection> roots);
}

internal abstract class WorkspaceScopePreparedCommit(
    WorkspaceScopePublicationResult result)
{
    internal WorkspaceScopePublicationResult Result { get; } = result;

    // Only Scope's preconstructed pointer assignment is permitted here.
    internal abstract void Commit();
}

internal sealed class ArtifactRootScopePublicationParticipant
{
    IWorkspaceScopePublicationCandidate? _candidate;

    internal ArtifactRootScopePublicationParticipant(
        IWorkspaceScopePublicationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Workspace = candidate.Workspace;
        ExpectedBase = candidate.ExpectedBase;
        Operation = candidate.Operation;
        CandidateSet = candidate.CandidateSet;
        _candidate = candidate;
    }

    internal InspectionWorkspaceIdentity Workspace { get; }
    internal WorkspaceScopePublicationBaseIdentity ExpectedBase { get; }
    internal WorkspaceScopePublicationOperationIdentity Operation { get; }
    internal WorkspaceScopePublicationCandidateIdentity CandidateSet { get; }

    internal ArtifactRootResult<WorkspaceScopePreparedCommit> PrepareCommit(
        ArtifactRootCompositionGenerationIdentity currentComposition,
        ArtifactRootCompositionGenerationIdentity candidateComposition,
        ImmutableArray<ArtifactRootScopeProjection> roots)
    {
        IWorkspaceScopePublicationCandidate? candidate =
            Interlocked.Exchange(ref _candidate, null);
        return candidate is null
            ? new ArtifactRootResult<WorkspaceScopePreparedCommit>.Rejected(
                ArtifactRootFailure.ParticipantAlreadyConsumed)
            : candidate.PrepareCommit(
                currentComposition, candidateComposition, roots);
    }
}
