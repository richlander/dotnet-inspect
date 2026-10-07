using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal static class TypeOverviewHierarchyCommand
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 250_000,
            maxMembers: 2_000_000,
            maxInspectionFailures: 4_096,
            maxTypeForwarders: 250_000,
            maxMetadataRows: 5_000_000,
            maxRetainedTextCharacters: 20_000_000);

    internal static async Task<int?> TryExecuteAsync(
        ApiSourceResult source,
        TypeOptions options,
        TypeOverviewHierarchyPresentationFormat format,
        CancellationToken cancellationToken,
        int maximumInventoryRows = 5_000)
    {
        string? path =
            ApiServices.FindApiDll(
                source.SearchPath,
                source.Context.Logger);
        if (path is null)
            return Unavailable(format, "Could not find the exact Type library.");

        AssemblyDescriptorSelectionResult descriptor;
        try
        {
            descriptor = ResolvedAssemblyReference.SelectFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "Type overview hierarchy"));
        }
        catch (Exception failure)
            when (failure is IOException or UnauthorizedAccessException)
        {
            return Unavailable(format, "Could not read the exact Type library.");
        }
        if (descriptor is not AssemblyDescriptorSelectionResult.Ready ready)
            return Unavailable(
                format,
                "A compact Type hierarchy requires a managed assembly descriptor.");
        TypeOverviewHierarchyPresentationPlan presentation =
            TypeOverviewHierarchyPresentation.CreateCompactPlan(
                format,
                options.IncludeAll);
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>? envelope =
            await ExactLibraryInspectionExecutor.ExecuteAsync(
                ready.Reference,
                session =>
                {
                    LibraryTypeListingResult? listing =
                        LibraryTypeListingCommand.ReadRows(
                            session,
                            new(
                                LibraryTypeDeclarationSelection.Definitions,
                                ApiTypeInventoryKinds.All),
                            cancellationToken,
                            maximumRows: maximumInventoryRows,
                            writeFailures: false);
                    if (listing is null)
                        return null;

                    var identities = listing.Rows.ToDictionary(
                        static row => row.Identity.ToEscapedFullName(),
                        static row => row.Identity,
                        StringComparer.Ordinal);
                    LookupResult lookup =
                        TypeMatcher.Lookup(
                            identities.Keys,
                            source.TypeName!);
                    if (lookup.Match is not { } match)
                        return null;

                    return session.ExecuteTypeOverviewDocument(
                        new(
                            identities[match],
                            presentation.Members.Rows!,
                            s_bounds,
                            presentation.Members.Spelling,
                            presentation.Members.Accessibility,
                            presentation.Members.Receiver,
                            presentation.Members.IncludeHidden,
                            presentation.Hierarchy),
                        cancellationToken);
                },
                cancellationToken);
        if (envelope?.Content
            is not TypeOverviewDocumentInspectionOutcome.Available available)
        {
            if (format == TypeOverviewHierarchyPresentationFormat.Mermaid
                && envelope is not null)
                TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
            return Unavailable(
                format,
                envelope?.Content switch
                {
                    TypeOverviewDocumentInspectionOutcome.Rejected rejected =>
                        $"Type overview inspection was rejected: {rejected.Reason}.",
                    TypeOverviewDocumentInspectionOutcome.Incomplete incomplete =>
                        $"Type overview inspection reached the {incomplete.Bound} bound ({incomplete.Measured}/{incomplete.Limit}).",
                    TypeOverviewDocumentInspectionOutcome.Failed failed =>
                        $"Type overview inspection failed: {failed.Reason}.",
                    _ => "The compact exact Type hierarchy is unavailable.",
                });
        }

        if (envelope.Diagnostics.Any(diagnostic =>
                diagnostic.Severity
                    == InspectionDiagnosticSeverity.Error))
        {
            if (format == TypeOverviewHierarchyPresentationFormat.Mermaid)
                TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
            return Unavailable(
                format,
                "The compact Type hierarchy inspection reported errors.");
        }

        try
        {
            TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
            TypeOverviewHierarchyPresentation.Write(
                available.Document,
                presentation,
                Console.Out);
            return 0;
        }
        catch (InvalidOperationException failure)
        {
            return Unavailable(format, failure.Message);
        }
    }

    private static int? Unavailable(
        TypeOverviewHierarchyPresentationFormat format,
        string message)
    {
        if (format == TypeOverviewHierarchyPresentationFormat.Tree)
            return null;
        CommandError.Write(
            $"--mermaid could not produce the compact exact Type hierarchy. {message}");
        return 1;
    }

}
