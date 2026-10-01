using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public abstract record ExactTypeRelationsInspectionOutcome
{
    private protected ExactTypeRelationsInspectionOutcome()
    {
    }

    public sealed record Available(
        InspectionEnvelope<SelectedContextExactTypeInspectionResult>?
            Inspection,
        WorkspaceTypeRelationsInspectionResult Relations)
        : ExactTypeRelationsInspectionOutcome;

    public sealed record Unavailable(string Detail)
        : ExactTypeRelationsInspectionOutcome;
}

public sealed record TypeRelationsInspectionRequest(
    WorkspaceContextInput Context,
    string Type,
    ExactTypeSelectionKind SelectionKind =
        ExactTypeSelectionKind.Query,
    string? FocusAssemblyName = null,
    ExactLibrarySourceCoordinate? FocusLibrary = null,
    bool IncludeTypeInspection = false);

/// <summary>
/// Executes exact Type inspection and QuerySpace Subject Relations in one
/// directly owned Workspace lifetime.
/// </summary>
public static class ExactTypeRelationsInspectionOperation
{
    public static async Task<ExactTypeRelationsInspectionOutcome> ExecuteAsync(
        ExactTypeInspectionRequest request,
        WorkspaceContextLoadOptions capabilities,
        SubjectRelationsQueryPlan plan,
        SubjectRelationPopulationCountRequest? count = null,
        SubjectRelationPopulationRowsRequest? rows = null,
        RowSelectionIntent<string>? rowSelection = null,
        bool includeNonPublic = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceContextInput input = new()
        {
            Framework = request.TargetFramework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    request.PackageId,
                    request.Version,
                    request.TargetFramework),
            ],
        };
        return await ExecuteAsync(
                new TypeRelationsInspectionRequest(
                    input,
                    request.Type,
                    request.SelectionKind,
                    IncludeTypeInspection: true),
                capabilities,
                plan,
                count,
                rows,
                rowSelection,
                includeNonPublic,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<ExactTypeRelationsInspectionOutcome> ExecuteAsync(
        TypeRelationsInspectionRequest request,
        WorkspaceContextLoadOptions capabilities,
        SubjectRelationsQueryPlan plan,
        SubjectRelationPopulationCountRequest? count = null,
        SubjectRelationPopulationRowsRequest? rows = null,
        RowSelectionIntent<string>? rowSelection = null,
        bool includeNonPublic = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(plan);
        WorkspaceContextInput input = request.Context;
        var workspace = new InspectionWorkspace(
            new WorkspacePlan([], [input]));
        ExactTypeRelationsInspectionOutcome outcome;
        try
        {
            WorkspaceDeclarationContext context =
                await WorkspaceContextLoader.LoadDeclarationContextAsync(
                    workspace,
                    input,
                    capabilities,
                    cancellationToken).ConfigureAwait(false);
            if (context.Group is null)
            {
                string detail = string.Join(
                    " ",
                    context.Receipt.Failures
                        .OfType<WorkspaceDeclarationFailure.ContextLoad>()
                        .Select(failure => failure.Failure.Message));
                outcome = new ExactTypeRelationsInspectionOutcome.Unavailable(
                    string.IsNullOrWhiteSpace(detail)
                        ? "The exact Type candidate context could not be loaded."
                        : detail);
            }
            else
            {
                outcome = Execute(
                    workspace,
                    context,
                    request,
                    plan,
                    count,
                    rows,
                    rowSelection,
                    includeNonPublic,
                    cancellationToken);
            }
        }
        catch (Exception failure)
        {
            await DirectWorkspaceOperationLifetime.CloseAfterFailureAsync(
                    workspace,
                    failure)
                .ConfigureAwait(false);
            throw;
        }

        await DirectWorkspaceOperationLifetime.CloseAsync(
                workspace,
                "Exact Type Subject Relations inspection")
            .ConfigureAwait(false);
        return outcome;
    }

    public static ExactTypeRelationsInspectionOutcome Execute(
        InspectionWorkspace workspace,
        WorkspaceDeclarationContext context,
        TypeRelationsInspectionRequest request,
        SubjectRelationsQueryPlan plan,
        SubjectRelationPopulationCountRequest? count = null,
        SubjectRelationPopulationRowsRequest? rows = null,
        RowSelectionIntent<string>? rowSelection = null,
        bool includeNonPublic = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        if (request.IncludeTypeInspection
            && context.Group is null)
        {
            string detail = string.Join(
                " ",
                context.Receipt.Failures
                    .OfType<WorkspaceDeclarationFailure.ContextLoad>()
                    .Select(failure => failure.Failure.Message));
            return new ExactTypeRelationsInspectionOutcome.Unavailable(
                string.IsNullOrWhiteSpace(detail)
                    ? "The exact Type candidate context could not be loaded."
                    : detail);
        }

        WorkspaceDeclarationPopulation population =
            workspace.CaptureDeclarationPopulation([context])
                is WorkspaceDeclarationPopulationCapture.Captured captured
                ? captured.Population
                : throw new InvalidOperationException(
                    "The loaded exact Type context could not be captured "
                        + "as a relation population.");
        WorkspaceExactTypeFocusOutcome focus =
            WorkspaceExactTypeFocusQuery.Execute(
                population,
                request.Type,
                request.SelectionKind,
                request.FocusAssemblyName,
                request.FocusLibrary,
                cancellationToken: cancellationToken);
        if (focus is not WorkspaceExactTypeFocusOutcome.Found found)
        {
            var unavailable =
                (WorkspaceExactTypeFocusOutcome.Unavailable)focus;
            string[] incomplete =
            [
                .. unavailable.Members
                    .Where(static member => !member.IsComplete)
                    .Select(static member =>
                        member.Member.AssemblyIdentity.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5),
            ];
            return new ExactTypeRelationsInspectionOutcome.Unavailable(
                unavailable.Detail
                    + (incomplete.Length == 0
                        ? ""
                        : $" Incomplete assemblies: "
                            + $"{string.Join(", ", incomplete)}."));
        }

        return Execute(
            workspace,
            context,
            request,
            population,
            found,
            plan,
            count,
            rows,
            rowSelection,
            includeNonPublic,
            cancellationToken);
    }

    public static ExactTypeRelationsInspectionOutcome Execute(
        InspectionWorkspace workspace,
        WorkspaceDeclarationContext context,
        TypeRelationsInspectionRequest request,
        WorkspaceDeclarationPopulation population,
        WorkspaceExactTypeFocusOutcome.Found focus,
        SubjectRelationsQueryPlan plan,
        SubjectRelationPopulationCountRequest? count = null,
        SubjectRelationPopulationRowsRequest? rows = null,
        RowSelectionIntent<string>? rowSelection = null,
        bool includeNonPublic = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(plan);
        InspectionEnvelope<
            SelectedContextExactTypeInspectionResult>? inspection =
            request.IncludeTypeInspection
                ? SelectedContextExactTypeInspectionOperation.Execute(
                        workspace,
                        context,
                        new(
                            request.Type,
                            request.SelectionKind))
                : null;
        WorkspaceTypeRelationsInspectionResult relations =
            WorkspaceTypeRelationsInspectionOperation.Execute(
                workspace,
                population,
                focus,
                plan,
                count,
                rows,
                rowSelection: rowSelection,
                includeNonPublic: includeNonPublic,
                cancellationToken: cancellationToken);
        return new ExactTypeRelationsInspectionOutcome.Available(
            inspection,
            relations);
    }
}
