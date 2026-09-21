using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

public static partial class WorkspaceCommand
{
    static async Task<int> ExecuteReplacementAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken)
    {
        CommittedScenarioDefinitionSet definitions;
        WorkspacePackageCoordinateReplacementRequest request;
        try
        {
            WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
                WorkspacePacketRestoration.GetPacketInput(
                    options.Packet!,
                    "--packet"),
                cancellationToken);
            definitions = WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet, cancellationToken);
            request = new(
                options.UpdatePackage!,
                options.ReplacementVersion,
                options.ReplacementTfm);
        }
        catch (Exception error) when (error is WorkspaceSharePacketException
            or InspectionDefinitionException or InvalidDataException or ArgumentException)
        {
            CommandError.Write(
                "The Package update input is invalid.",
                [error.Message]);
            return 1;
        }

        ViewFacetRegistry facets = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot entries = CurrentCatalogEntriesExecutable(facets);
        InspectionEnvelope<WorkspacePortableCoordinateReplacementOutcome> inspection =
            await WorkspacePortableCoordinateReplacementOperation.ExecuteAsync(
                definitions,
                request,
                new CompleteRestorationExecutionOptions
                {
                    ContextLoad = loadOptions,
                    ScopeDeadline = DateTimeOffset.UtcNow.AddMinutes(5),
                    Facets = facets,
                    FacetAvailability = (_, _) => entries,
                },
                cancellationToken).ConfigureAwait(false);

        if (options.EnvelopeOutput)
        {
            if (!InspectionEnvelopeOutput.TryWrite(
                inspection,
                new InspectionEnvelopeJsonContract<WorkspacePortableCoordinateReplacementOutcome>(
                    "workspace-package-update",
                    1,
                    WorkspacePortableCoordinateReplacementJsonContext.Default
                        .WorkspacePortableCoordinateReplacementOutcome),
                includeEnvelope: true))
            {
                return 1;
            }
        }
        foreach (InspectionDiagnostic diagnostic in inspection.Diagnostics)
        {
            if (diagnostic.Severity == InspectionDiagnosticSeverity.Error)
                CommandError.Write($"{diagnostic.Code}: {diagnostic.Summary}");
            else
                CommandError.WriteNote($"{diagnostic.Code}: {diagnostic.Summary}");
        }
        if (inspection.Share is InspectionShare.NonProjectable refusal)
        {
            CommandError.Write(
                $"The derived Workspace is not projectable at {refusal.Path}: {refusal.Reason}");
            return 1;
        }
        if (!inspection.Content.Succeeded)
            return 1;
        if (!options.EnvelopeOutput)
        {
            var available = (InspectionShare.Available)inspection.Share;
            Console.WriteLine(
                options.ShareFormat == WorkspaceShareFormat.Packet
                    ? available.Packet
                    : available.FullUrl);
        }
        return 0;
    }

    static string? ReplacementOptionError(WorkspaceOptions options)
    {
        if (options.MakePackageDependenciesExplicit)
            return "Choose Package update or dependency enrichment, not both.";
        if (options.UpdatePackage is null)
            return "Package update requires a Workspace Package component path.";
        if (options.Packet is null)
            return "Package update requires --packet with a complete input scenario.";
        if (options.ReplacementVersion is null && options.ReplacementTfm is null)
            return "Package update requires --version, --tfm, or both.";
        if (options.Packages.Length != 0
            || options.Tfm is not null
            || options.RootRequest is not null
            || options.OrderedRegistrations.Length != 0
            || options.RegisteredLibraries.Length != 0
            || options.RegisteredPackagePrefixes.Length != 0
            || options.RegisteredEcosystems.Length != 0)
        {
            return "Package update uses the complete --packet definition "
                + "and cannot be combined with direct construction options.";
        }
        if (options.ActivePackage is not null
            || options.Library is not null || options.AllLibraries
            || options.Type is not null || options.Member is not null
            || options.Lens is not null)
        {
            return "Package update retains the packet's committed view; "
                + "do not restate Navigation selectors.";
        }
        if (options.InventoryKinds.Length != 0 || options.Count
            || options.Rows is not null || options.NoHeader)
        {
            return "Package update emits a complete scenario and "
                + "cannot be combined with inventory filters or row controls.";
        }
        if (options.IncludePrerelease)
            return "Package update requires exact Version pins, not --preview.";
        if (options.EnvelopeOutput && options.Format != OutputFormat.Json)
            return "--envelope requires --json.";
        if (options.EnvelopeOutput && options.ShareFormat is not null)
            return "Choose --json --envelope or --share; each emits the complete derived Share.";
        if (!options.EnvelopeOutput && options.ShareFormat is null)
        {
            return "Package update requires --share packet|url or "
                + "--json --envelope to emit the complete derived scenario.";
        }
        return null;
    }
}
