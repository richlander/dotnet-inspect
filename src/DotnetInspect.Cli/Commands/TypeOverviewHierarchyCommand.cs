using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal static class TypeOverviewHierarchyCommand
{
    internal sealed record Execution(int? ExitCode, ExactTypeDefinitionSelection? Selection = null);

    internal static async Task<Execution> TryExecuteAsync(
        ApiSourceResult source,
        TypeOptions options,
        TypeOverviewHierarchyPresentationFormat format,
        CancellationToken cancellationToken)
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
        TypeInspectionPolicy policy =
            TypeInspectionPolicies.CompactOverview;
        TypeOverviewHierarchyInspectionExecution execution =
            await TypeOverviewHierarchyInspection.ExecuteAsync(
                ready.Reference,
                NoResolverAssemblyBindingPolicy.Instance,
                source.TypeName!,
                format,
                options.IncludeAll,
                policy.ExtractionBounds,
                policy.MaterializationLimits,
                cancellationToken)
            .ConfigureAwait(false);
        foreach (string cleanupFailure in execution.CleanupFailures)
            CommandError.Write(cleanupFailure);
        if (!execution.CleanupFailures.IsEmpty)
            return new(1);
        if (execution.Selection is { Outcome: not ExactTypeInspectionOutcome.Available } selection)
        {
            if (selection.Outcome == ExactTypeInspectionOutcome.NotFound
                && format == TypeOverviewHierarchyPresentationFormat.Tree)
                return new(null, selection);
            CommandError.Write(
                FailureMessage(format, selection.Failure ?? "The Type selection is unavailable."),
                [.. selection.Suggestions.Select(static name => $"  {name}")]);
            return new(1);
        }
        InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>? envelope =
            execution.Inspection;
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
                    _ => execution.Failure
                        ?? "The compact exact Type hierarchy is unavailable.",
                },
                terminal: execution.Selection is not null);
        }

        if (envelope.Diagnostics.Any(diagnostic =>
                diagnostic.Severity
                    == InspectionDiagnosticSeverity.Error))
        {
            if (format == TypeOverviewHierarchyPresentationFormat.Mermaid)
                TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
            return Unavailable(
                format,
                "The compact Type hierarchy inspection reported errors.",
                terminal: execution.Selection is not null);
        }

        try
        {
            TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
            TypeOverviewHierarchyPresentation.Write(
                available.Document,
                execution.Presentation,
                Console.Out);
            return new(0);
        }
        catch (InvalidOperationException failure)
        {
            return Unavailable(format, failure.Message,
                terminal: execution.Selection is not null);
        }
    }

    private static Execution Unavailable(
        TypeOverviewHierarchyPresentationFormat format,
        string message,
        bool terminal = false)
    {
        if (format == TypeOverviewHierarchyPresentationFormat.Tree && !terminal)
            return new(null);
        CommandError.Write(FailureMessage(format, message));
        return new(1);
    }

    private static string FailureMessage(
        TypeOverviewHierarchyPresentationFormat format,
        string message) =>
        format == TypeOverviewHierarchyPresentationFormat.Mermaid
            ? $"--mermaid could not produce the compact exact Type hierarchy. {message}"
            : message;
}
