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
    private const int MaxPlatformFocusExpansions = 16;

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
        for (int expansion = 0;
            expansion <= MaxPlatformFocusExpansions;
            expansion++)
        {
            var workspace = new InspectionWorkspace(
                new WorkspacePlan([], [input]));
            ExactTypeRelationsInspectionOutcome? outcome = null;
            WorkspaceContextInput? expandedInput = null;
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
                    outcome = new ExactTypeRelationsInspectionOutcome
                        .Unavailable(
                            string.IsNullOrWhiteSpace(detail)
                                ? "The exact Type candidate context could not "
                                    + "be loaded."
                                : detail);
                }
                else
                {
                    WorkspaceDeclarationPopulation population =
                        workspace.CaptureDeclarationPopulation([context])
                            is WorkspaceDeclarationPopulationCapture
                                .Captured captured
                            ? captured.Population
                            : throw new InvalidOperationException(
                                "The loaded exact Type context could not be "
                                    + "captured as a relation population.");
                    WorkspaceExactTypeFocusOutcome focus =
                        WorkspaceExactTypeFocusQuery.Execute(
                            population,
                            request.Type,
                            request.SelectionKind,
                            request.FocusAssemblyName,
                            request.FocusLibrary,
                            cancellationToken: cancellationToken);
                    if (focus
                        is WorkspaceExactTypeFocusOutcome
                            .PlatformAssemblyRequired required)
                    {
                        expandedInput =
                            expansion < MaxPlatformFocusExpansions
                                ? ExpandPlatformContext(
                                    input,
                                    required.Assembly)
                                : null;
                        if (expandedInput is null)
                        {
                            outcome =
                                new ExactTypeRelationsInspectionOutcome
                                    .Unavailable(
                                        "The exact Type focus requires "
                                            + $"platform assembly "
                                            + $"'{required.Assembly.Name}', "
                                            + "but the candidate context "
                                            + "cannot be expanded.");
                        }
                    }
                    else if (focus
                        is WorkspaceExactTypeFocusOutcome
                            .Unavailable unavailable)
                    {
                        outcome =
                            new ExactTypeRelationsInspectionOutcome.Unavailable(
                                unavailable.Detail);
                    }
                    else
                    {
                        var found =
                            (WorkspaceExactTypeFocusOutcome.Found)focus;
                        InspectionEnvelope<
                            SelectedContextExactTypeInspectionResult>?
                            inspection =
                                request.IncludeTypeInspection
                                    ? SelectedContextExactTypeInspectionOperation
                                        .Execute(
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
                                found,
                                plan,
                                count,
                                rows,
                                rowSelection: rowSelection,
                                includeNonPublic: includeNonPublic,
                                cancellationToken: cancellationToken);
                        outcome =
                            new ExactTypeRelationsInspectionOutcome.Available(
                                inspection,
                                relations);
                    }
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
            if (expandedInput is not null)
            {
                input = expandedInput;
                continue;
            }
            return outcome
                ?? new ExactTypeRelationsInspectionOutcome.Unavailable(
                    "The exact Type focus did not produce an outcome.");
        }

        return new ExactTypeRelationsInspectionOutcome.Unavailable(
            "The exact Type focus exceeded the platform expansion bound.");
    }

    private static WorkspaceContextInput? ExpandPlatformContext(
        WorkspaceContextInput input,
        AssemblyReferenceIdentity required)
    {
        WorkspaceMemberCoordinate.PlatformMember[] platformMembers =
        [
            .. input.Members.OfType<
                WorkspaceMemberCoordinate.PlatformMember>(),
        ];
        if (platformMembers.Length == 0
            || input.Members.Count != platformMembers.Length
            || platformMembers.Any(member =>
                member.Assembly?.Equals(
                    required.Name,
                    StringComparison.OrdinalIgnoreCase) is true))
        {
            return null;
        }

        WorkspaceMemberCoordinate.PlatformMember root =
            platformMembers[0];
        if (platformMembers.Any(member =>
            !member.Family.Equals(
                root.Family,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                member.Version,
                root.Version,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                member.Framework,
                root.Framework,
                StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return input with
        {
            Members =
            [
                .. input.Members,
                WorkspaceMemberCoordinate.Platform(
                    root.Family,
                    required.Name,
                    root.Version,
                    root.Framework),
            ],
        };
    }
}
