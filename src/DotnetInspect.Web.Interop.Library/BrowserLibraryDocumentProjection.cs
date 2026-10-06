using DotnetInspector.LibraryMetadata;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web.Interop.Library;

/// <summary>
/// Projects one host-neutral Library inspection run to the Browser wire.
/// Enablement labels come from <see cref="LibraryEnablementFacts.Label"/>, so
/// hosts share one vocabulary.
/// </summary>
internal static class BrowserLibraryDocumentProjection
{
    internal static BrowserLibraryDocumentInspection Project(
        AssemblyContextLibraryInspectionRun<InspectionEnvelope<LibraryInspectionOutcome>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        BrowserLibraryInspectionDiagnostic[] cleanup =
        [
            .. run.CleanupFailures.Select(static failure =>
                new BrowserLibraryInspectionDiagnostic(
                    "library-retirement",
                    "Warning",
                    failure,
                    null)),
        ];
        if (run.Failure is { } failure)
        {
            return new(BrowserLibraryDocumentOutcome.Unavailable, failure, null, null, cleanup);
        }

        if (run.Result is not { } envelope)
        {
            return new(
                BrowserLibraryDocumentOutcome.Failed,
                "The exact Library owner could not issue the inspection operation lease.",
                null,
                null,
                cleanup);
        }

        BrowserLibraryInspectionDiagnostic[] diagnostics =
        [
            .. envelope.Diagnostics.Select(static diagnostic =>
                new BrowserLibraryInspectionDiagnostic(
                    diagnostic.Code,
                    diagnostic.Severity.ToString(),
                    diagnostic.Summary.ToString(),
                    diagnostic.Correspondence?.ToString())),
            .. cleanup,
        ];
        return envelope.Content switch
        {
            LibraryInspectionOutcome.Available available => new(
                BrowserLibraryDocumentOutcome.Available,
                null,
                Project(available.Document.Assembly),
                available.Document.Enablements is { } enablements
                    ? Project(enablements)
                    : null,
                diagnostics),
            LibraryInspectionOutcome.Rejected rejected => new(
                BrowserLibraryDocumentOutcome.Rejected,
                rejected.Reason.ToString(),
                null,
                null,
                diagnostics),
            LibraryInspectionOutcome.Failed failed => new(
                BrowserLibraryDocumentOutcome.Failed,
                failed.Reason.ToString(),
                null,
                null,
                diagnostics),
            _ => throw new InvalidOperationException("Unknown Library inspection outcome."),
        };
    }

    internal static BrowserLibraryEnablements Project(LibraryEnablementsOutcome outcome) =>
        outcome switch
        {
            LibraryEnablementsOutcome.Available available => new(
                BrowserLibraryEnablementsOutcome.Available,
                available.Role switch
                {
                    LibraryEnablementsRole.ImplementationAssembly =>
                        BrowserLibraryEnablementsRole.ImplementationAssembly,
                    LibraryEnablementsRole.ApiAssembly =>
                        BrowserLibraryEnablementsRole.ApiAssembly,
                    _ => throw new InvalidOperationException("Unknown enablements role."),
                },
                null,
                [.. available.Facts.Items.Select(Project)]),
            LibraryEnablementsOutcome.Failed failed => new(
                BrowserLibraryEnablementsOutcome.Failed,
                null,
                failed.Reason.ToString(),
                []),
            _ => throw new InvalidOperationException("Unknown enablements outcome."),
        };

    private static BrowserLibraryEnablement Project(LibraryEnablement item) =>
        new(
            Project(item.Id),
            item switch
            {
                LibraryEnablement.Enabled => BrowserLibraryEnablementKind.Enabled,
                LibraryEnablement.NotEnabled => BrowserLibraryEnablementKind.NotEnabled,
                LibraryEnablement.Unavailable => BrowserLibraryEnablementKind.Unavailable,
                _ => throw new InvalidOperationException("Unknown enablement case."),
            },
            LibraryEnablementFacts.Label(item.Id),
            item is LibraryEnablement.Unavailable unavailable
                ? Project(unavailable.Reason)
                : null);

    internal static BrowserLibraryEnablementId Project(LibraryEnablementId id) =>
        id switch
        {
            LibraryEnablementId.AotCompatible => BrowserLibraryEnablementId.AotCompatible,
            LibraryEnablementId.RuntimeAsync => BrowserLibraryEnablementId.RuntimeAsync,
            LibraryEnablementId.MemorySafetyV2 => BrowserLibraryEnablementId.MemorySafetyV2,
            _ => throw new InvalidOperationException("Unknown enablement id."),
        };

    internal static BrowserLibraryEnablementUnavailableReason Project(
        LibraryEnablementUnavailableReason reason) =>
        reason switch
        {
            LibraryEnablementUnavailableReason.ReferenceAssembly =>
                BrowserLibraryEnablementUnavailableReason.ReferenceAssembly,
            LibraryEnablementUnavailableReason.UndecodableMetadata =>
                BrowserLibraryEnablementUnavailableReason.UndecodableMetadata,
            LibraryEnablementUnavailableReason.UnrecognizedValue =>
                BrowserLibraryEnablementUnavailableReason.UnrecognizedValue,
            LibraryEnablementUnavailableReason.ConflictingValues =>
                BrowserLibraryEnablementUnavailableReason.ConflictingValues,
            LibraryEnablementUnavailableReason.UnsupportedMemorySafetyRules =>
                BrowserLibraryEnablementUnavailableReason.UnsupportedMemorySafetyRules,
            LibraryEnablementUnavailableReason.MalformedMemorySafetyRules =>
                BrowserLibraryEnablementUnavailableReason.MalformedMemorySafetyRules,
            LibraryEnablementUnavailableReason.ConflictingMemorySafetyRules =>
                BrowserLibraryEnablementUnavailableReason.ConflictingMemorySafetyRules,
            LibraryEnablementUnavailableReason.MemorySafetyMetadataUnavailable =>
                BrowserLibraryEnablementUnavailableReason.MemorySafetyMetadataUnavailable,
            _ => throw new InvalidOperationException("Unknown enablement reason."),
        };

    private static BrowserLibraryAssemblyReference Project(LibraryAssemblyIdentity identity) =>
        new(
            identity.Name.ToString(),
            identity.Version.ToString(),
            identity.Culture?.ToString(),
            identity.PublicKeyToken?.ToString());
}
