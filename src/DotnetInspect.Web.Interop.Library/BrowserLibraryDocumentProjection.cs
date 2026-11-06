using System.Runtime.Versioning;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Sections;

namespace DotnetInspect.Web.Interop.Library;

/// <summary>
/// Projects one host-neutral Library inspection run to the Browser wire.
/// Names use the shared Library hierarchy; Enablement labels come from
/// <see cref="LibraryEnablementFacts.Label"/>.
/// </summary>
[SupportedOSPlatform("browser")]
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
                diagnostics)
            {
                TypeNames = available.Document.Types is null
                    ? null
                    : ProjectNames(available.Document),
                Share = BrowserLibraryWireProjection.Project(envelope.Share),
            },
            LibraryInspectionOutcome.Rejected rejected => new(
                BrowserLibraryDocumentOutcome.Rejected,
                rejected.Reason.ToString(),
                null,
                null,
                diagnostics)
            {
                Share = BrowserLibraryWireProjection.Project(envelope.Share),
            },
            LibraryInspectionOutcome.Failed failed => new(
                BrowserLibraryDocumentOutcome.Failed,
                failed.Reason.ToString(),
                null,
                null,
                diagnostics)
            {
                Share = BrowserLibraryWireProjection.Project(envelope.Share),
            },
            _ => throw new InvalidOperationException("Unknown Library inspection outcome."),
        };
    }

    private static BrowserLibraryTypeNames ProjectNames(LibraryDocument document)
    {
        if (document.Types!.Rows is not LibraryTypePopulationRowsOutcome.Read
            {
                Continuation: null,
            })
        {
            return new(
                "Unavailable",
                document.Types.Rows switch
                {
                    LibraryTypePopulationRowsOutcome.Read =>
                        "The Library Type list exceeds one complete hierarchy.",
                    LibraryTypePopulationRowsOutcome.Incomplete incomplete =>
                        $"Type Names reached the {incomplete.Bound} bound "
                            + $"({incomplete.Measured}/{incomplete.Limit}).",
                    LibraryTypePopulationRowsOutcome.Unavailable unavailable =>
                        $"Type Names are unavailable: {unavailable.Reason}.",
                    LibraryTypePopulationRowsOutcome.Rejected rejected =>
                        $"Type Names were rejected: {rejected.Reason}.",
                    LibraryTypePopulationRowsOutcome.Failed failed =>
                        $"Type Names failed: {failed.Reason}.",
                    _ => "The Library returned no Type Names.",
                },
                []);
        }

        var sink = new NameHierarchySink();
        LibraryTypeHierarchyProjection.Write(
            document,
            BrowserLibraryDocumentOperation.NamePresentation().Hierarchy,
            sink);
        return new("Available", null, [.. sink.Namespaces]);
    }

    private sealed class NameHierarchySink :
        IInspectionHierarchySink<LibraryTypeHierarchyNode>
    {
        internal List<BrowserLibraryTypeNamespace> Namespaces { get; } = [];
        private List<BrowserLibraryTypeName>? _types;

        public void WriteNode(
            LibraryTypeHierarchyNode node,
            bool isLastSibling,
            Action<IInspectionHierarchySink<LibraryTypeHierarchyNode>>?
                writeChildren = null)
        {
            switch (node)
            {
                case LibraryTypeHierarchyNode.Namespace ns:
                    _types = [];
                    writeChildren?.Invoke(this);
                    Namespaces.Add(new(
                        ns.Name.ToString(),
                        ns.DeclarationCount,
                        [.. _types]));
                    _types = null;
                    break;
                case LibraryTypeHierarchyNode.Type type:
                    _types!.Add(new(
                        type.Value.Identity.ToEscapedFullName(),
                        type.Spelling.ToString(),
                        type.Value.DeclarationKind.ToString(),
                        type.Value.DefinitionKind?.ToString()));
                    writeChildren?.Invoke(this);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Library Type hierarchy node.");
            }
        }
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
