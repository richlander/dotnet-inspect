using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems;

/// <summary>
/// Produces detached Library ecosystem-dependency recognition from one exact
/// Library source coordinate and its owner-issued direct references.
/// </summary>
public static class LibraryEcosystemDependencyRecognitionInspection
{
    public static InspectionEnvelope<EcosystemDependencyRecognitionOutcome>
        Execute(
            ExactLibrarySourceCoordinate source,
            AssemblyReferencesResult references)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(references);

        PortableLibraryIdentity identity =
            CreatePortableIdentity(source.LibraryIdentity.Identity);
        var subject = new EcosystemDependencySubject.Library(
            source,
            identity);
        EcosystemDependencyObservationBatch batch;
        ImmutableArray<InspectionDiagnostic> diagnostics;

        switch (references)
        {
            case AssemblyReferencesResult.Available available:
                batch = new EcosystemDependencyObservationBatch.Available(
                    subject,
                    new EcosystemDependencyInputContext.Library(
                        new EcosystemDependencyReferenceInput.Available()),
                    available.Identities.Select(
                        (reference, index) =>
                            new EcosystemDependencyObservation
                                .AssemblyReference(
                                    new(index + 1),
                                    index + 1,
                                    reference,
                                    identity)));
                diagnostics = [];
                break;

            case AssemblyReferencesResult.Failed:
            {
                var diagnostic = new InspectionDiagnostic(
                    "ecosystem-dependency-recognition.library-references-unavailable",
                    InspectionDiagnosticSeverity.Warning,
                    "The Library's direct assembly references could not be projected.");
                var issueIdentity =
                    new EcosystemDependencyInputIssueIdentity(1);
                var issue = new EcosystemDependencyInputIssue(
                    issueIdentity,
                    EcosystemDependencyInputRole.AssemblyReferenceProjection,
                    diagnostic,
                    new EcosystemDependencyInputIssueSource.Library(identity));
                batch = new EcosystemDependencyObservationBatch.Unavailable(
                    subject,
                    new EcosystemDependencyInputContext.Library(
                        new EcosystemDependencyReferenceInput.Unavailable(
                            issueIdentity)),
                    [issue]);
                diagnostics = [diagnostic];
                break;
            }

            default:
                throw new InvalidOperationException(
                    $"Unknown assembly-reference result '{references.GetType().Name}'.");
        }

        return EcosystemDependencyRecognizer.Recognize(
            ProductEcosystemPacks.DependencyRecognitionProfile,
            batch,
            new EcosystemDependencyRecognitionPortableProjection(
                subject,
                new InspectionPortableProjection.NonProjectable(
                    "ecosystem-dependency-recognition/library-share",
                    InspectionPortableProjectionFailureReason.NotSupported)),
            diagnostics);
    }

    private static PortableLibraryIdentity CreatePortableIdentity(
        AssemblyReferenceIdentity identity) =>
        new(
            identity.Name,
            identity.Version?.ToString(4)
                ?? throw new ArgumentException(
                    "Library ecosystem recognition requires an exact assembly version.",
                    nameof(identity)),
            string.IsNullOrWhiteSpace(identity.Culture)
                || identity.Culture.Equals(
                    "neutral",
                    StringComparison.OrdinalIgnoreCase)
                    ? null
                    : identity.Culture,
            identity.PublicKeyToken?.ToLowerInvariant());
}
