using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

internal static class BrowserIndexedPackageTypeProjection
{
    internal static BrowserIndexedPackageTypeProjectionResult Project(
        TypeFindSemanticMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        string? identity = BrowserSpotlightTypeIdentity.TryCreate(
            match.Declaration.Source.Coordinate,
            match.Declaration.Name,
            match.Declaration.ModuleVersionId);
        if (match.DeclarationKind
                is not AssemblyTypeDeclarationKind.Definition
            || match.Declaration.Source.Coordinate
                is not ExactLibrarySourceCoordinate.Package library)
        {
            return new(
                identity,
                Intent: null,
                "Only exact package Type definitions can be activated.");
        }

        return new(
            identity,
            new BrowserSpotlightPackageTypeIntent(
                library,
                match.Declaration.Name),
            RefusalReason: null);
    }
}

internal sealed record BrowserIndexedPackageTypeProjectionResult(
    string? Identity,
    BrowserSpotlightPackageTypeIntent? Intent,
    string? RefusalReason);

[SupportedOSPlatform("browser")]
internal sealed partial class BrowserRetainedWorkspaceActivationOwner
{
    internal async ValueTask<BrowserTypeFindActivationResult>
        ActivateIndexedPackageTypeAsync(
            string retainedDefinitionId,
            string realizationId,
            PackageQueryEcosystemAdmission admission,
            BrowserSpotlightPackageTypeIntent intent,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(intent);

        BrowserEcosystemPackageAdmissionResult package =
            await AdmitEcosystemPackageAsync(
                    retainedDefinitionId,
                    realizationId,
                    intent.Package.PackageId,
                    intent.Package.Version,
                    admission,
                    cancellationToken)
                .ConfigureAwait(false);
        BrowserRetainedWorkspacePosting posting;
        switch (package)
        {
            case BrowserEcosystemPackageAdmissionResult.Admitted
                    packageAdmitted:
                posting = packageAdmitted.Posting;
                break;
            case BrowserEcosystemPackageAdmissionResult.NoEffect noEffect:
                posting = noEffect.Posting;
                break;
            case BrowserEcosystemPackageAdmissionResult.Failed failed:
                return new BrowserTypeFindActivationResult.Blocked(
                    BrowserTypeFindActivationStatus.Failed,
                    failed.Message);
            case BrowserEcosystemPackageAdmissionResult.Superseded:
                return new BrowserTypeFindActivationResult.Blocked(
                    BrowserTypeFindActivationStatus.Stale,
                    "The indexed Type Find Workspace is no longer current.");
            default:
                throw new InvalidOperationException(
                    "Indexed Type package admission returned an unsupported result.");
        }

        WorkspaceRealizationOperationAdmission entered =
            await EnterOperationAsync(
                    retainedDefinitionId,
                    realizationId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (entered
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            return new BrowserTypeFindActivationResult.Blocked(
                BrowserTypeFindActivationStatus.Stale,
                "The indexed Type Find Workspace realization is unavailable.");
        }

        using WorkspaceRealizationOperationLease operation = admitted.Lease;
        lock (_gate)
        {
            if (!ReferenceEquals(_active, posting))
            {
                return new BrowserTypeFindActivationResult.Blocked(
                    BrowserTypeFindActivationStatus.Stale,
                    "The indexed Type Find Workspace changed during activation.");
            }
        }

        using var typeFind = new BrowserTypeFindOperation(_host);
        BrowserTypeFindExecutionResult result =
            await typeFind.ExecuteAsync(
                    operation,
                    posting.NavigationState,
                    posting.NavigationPreparation,
                    retainedDefinitionId,
                    intent.Type.ToMetadataFullName(),
                    resultGeneration: 0,
                    cancellationToken)
                .ConfigureAwait(false);
        if (result is not BrowserTypeFindExecutionResult.Completed completed
            || completed.Result.Find.Content
                is not TypeDeclarationLocatorSectionResult.Evaluated evaluated)
        {
            return new BrowserTypeFindActivationResult.Blocked(
                BrowserTypeFindActivationStatus.Unavailable,
                result switch
                {
                    BrowserTypeFindExecutionResult.Rejected rejected =>
                        rejected.Reason,
                    BrowserTypeFindExecutionResult.Unavailable unavailable =>
                        unavailable.Reason,
                    BrowserTypeFindExecutionResult.Stale stale =>
                        stale.Reason,
                    _ => "The exact indexed Type declaration is unavailable.",
                });
        }

        BrowserTypeFindCandidateActivation[] matches =
        [
            .. evaluated.Answers.SelectMany(answer =>
                answer.Candidates.Select((candidate, candidateIndex) =>
                    (answer, candidate, candidateIndex)))
                .Where(candidate =>
                    candidate.candidate.Coordinate
                        is TypeDeclarationLocatorSectionCoordinate
                            .PackageCoordinate packageCoordinate
                    && packageCoordinate.Package == intent.Package
                    && AssemblyReferenceIdentity.EquivalentComparer.Equals(
                        packageCoordinate.LibraryIdentity,
                        intent.Library.LibraryIdentity.Identity)
                    && candidate.candidate.Name == intent.Type)
                .Select(candidate =>
                    completed.Result.Activations.Single(activation =>
                        activation.Candidate.AnswerOrdinal
                            == candidate.answer.Identity.Ordinal
                        && activation.Candidate.CandidateOrdinal
                            == candidate.candidateIndex + 1)),
        ];
        if (matches.Length != 1)
        {
            return new BrowserTypeFindActivationResult.Blocked(
                matches.Length == 0
                    ? BrowserTypeFindActivationStatus.Unavailable
                    : BrowserTypeFindActivationStatus.Ambiguous,
                matches.Length == 0
                    ? "The exact indexed package Type declaration is unavailable."
                    : "The exact indexed package Type declaration is ambiguous.");
        }

        BrowserTypeFindCandidateActivation match = matches[0];
        if (match.Status is not BrowserTypeFindActivationStatus.Available
            || match.Action is null)
        {
            return new BrowserTypeFindActivationResult.Blocked(
                match.Status,
                match.Reason
                    ?? "The exact indexed package Type cannot be activated.");
        }

        return await typeFind.ActivateAsync(
                this,
                match.Action,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
