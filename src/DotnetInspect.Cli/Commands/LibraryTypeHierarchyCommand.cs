using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Renders one exact Library's native Tree. The Library owner issues the
/// Type declaration population and hierarchy; shared presentation lowers it.
/// </summary>
internal static class LibraryTypeHierarchyCommand
{
    private static readonly LibraryInspectionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    internal static LibraryInspectionPlan CreateInspectionPlan(
        LibraryTypeHierarchyPresentationPlan presentation) =>
        new(presentation.Types, s_bounds);

    internal static async Task<int> ExecuteAsync(
        string assemblyPath,
        LibraryCommandPlan.TypeHierarchy plan,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(plan);

        LibraryTypeHierarchyPresentationPlan presentation =
            LibraryTypeHierarchyPresentation.CreateDefaultPlan(plan.Format);
        LibraryInspectionPlan inspection =
            CreateInspectionPlan(presentation);
        InspectionEnvelope<LibraryInspectionOutcome>? envelope =
            await ExactLibraryInspectionExecutor.ExecuteAsync(
                    assemblyPath,
                    "Library Type hierarchy",
                    session => session.Execute(
                        inspection,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        if (envelope is null)
            return 1;

        TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
        if (envelope.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity == InspectionDiagnosticSeverity.Error))
        {
            return Unavailable(
                "The Library Type inspection reported errors.");
        }
        if (envelope.Content
            is not LibraryInspectionOutcome.Available available)
        {
            return Unavailable(
                envelope.Content switch
                {
                    LibraryInspectionOutcome.Rejected rejected =>
                        $"The Library inspection was rejected: {rejected.Reason}.",
                    LibraryInspectionOutcome.Failed failed =>
                        $"The Library inspection failed: {failed.Reason}.",
                    _ => "The Library inspection returned an unknown outcome.",
                });
        }
        if (DescribeRowsFailure(available.Document.Types?.Rows)
            is { } rowsFailure)
            return Unavailable(rowsFailure);

        try
        {
            LibraryTypeHierarchyPresentation.Write(
                available.Document,
                presentation,
                Console.Out);
            return 0;
        }
        catch (InvalidOperationException failure)
        {
            return Unavailable(failure.Message);
        }
    }

    internal static string? DescribeRowsFailure(
        LibraryTypePopulationRowsOutcome? rows) =>
        rows switch
        {
            LibraryTypePopulationRowsOutcome.Read { Continuation: null } =>
                null,
            LibraryTypePopulationRowsOutcome.Read =>
                "The Library declares more public Types than one hierarchy "
                    + "presents. Use 'library <Library> --namespace <Namespace>'.",
            LibraryTypePopulationRowsOutcome.Incomplete incomplete =>
                $"The Type declaration Rows reached the {incomplete.Bound} "
                    + $"bound ({incomplete.Measured}/{incomplete.Limit}).",
            LibraryTypePopulationRowsOutcome.Unavailable unavailable =>
                $"The Type declaration Rows are unavailable: {unavailable.Reason}.",
            LibraryTypePopulationRowsOutcome.Rejected rejected =>
                $"The Type declaration Rows were rejected: {rejected.Reason}.",
            LibraryTypePopulationRowsOutcome.Failed failed =>
                $"The Type declaration Rows failed: {failed.Reason}.",
            null => "The Library inspection returned no Type declaration Rows.",
            _ => "The Type declaration Rows returned an unknown outcome.",
        };

    private static int Unavailable(string message)
    {
        CommandError.Write(
            $"The Library Type hierarchy is unavailable. {message}");
        return 1;
    }
}
