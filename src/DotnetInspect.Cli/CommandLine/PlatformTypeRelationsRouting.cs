using DotnetInspect.Cli.Commands;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using QuerySpace.Rows;

namespace DotnetInspect.Cli.CommandLine;

internal static class PlatformTypeRelationsRouting
{
    internal static async ValueTask<ExactTypeRelationsInspectionOutcome>
        ExecuteAsync(
            PlatformFamilyTarget target,
            TypeRelationsInspectionRequest request,
            SubjectRelationsQueryPlan plan,
            SubjectRelationPopulationCountRequest? count,
            SubjectRelationPopulationRowsRequest? rows,
            RowSelectionIntent<string>? rowSelection,
            bool includeNonPublic,
            CommandContext context,
            NuGetSourceOptions sourceOptions,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sourceOptions);

        PlatformPopulationArtifactMaterializationOutcome realization =
            await PlatformTypeCatalogRouting.RealizePopulationAsync(
                    PlatformTypeCatalogRouting.FindActiveDotnetRoot(),
                    target,
                    context,
                    sourceOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        if (realization
            is PlatformPopulationArtifactMaterializationOutcome.Terminal
                terminal)
        {
            CliPlatformTypeCatalogOutcome.NotCompleted failure =
                PlatformTypeCatalogRouting.HouseFailure(terminal);
            return new ExactTypeRelationsInspectionOutcome.Unavailable(
                "The exact Platform relation population could not be "
                    + $"realized ({Describe(failure)}).");
        }

        var completed =
            (PlatformPopulationArtifactMaterializationOutcome.Completed)
                realization;
        var workspace = new InspectionWorkspace();
        bool resourcesSettled = false;
        bool workspaceClosed = false;
        try
        {
            WorkspaceRegistrationRevision registrations =
                RegistrationSnapshot(workspace);
            WorkspaceLibraryAdmissionOutcome libraryAdmission =
                await workspace.AdmitLibraryBatchAsync(
                        registrations,
                        completed.Artifacts,
                        completed.Population.Owners)
                    .ConfigureAwait(false);
            resourcesSettled = true;
            if (libraryAdmission
                is not WorkspaceLibraryAdmissionOutcome.Accepted accepted)
            {
                InspectionWorkspaceCloseReport closeReport =
                    await workspace.CloseAsync().ConfigureAwait(false);
                workspaceClosed = true;
                return new ExactTypeRelationsInspectionOutcome.Unavailable(
                    closeReport.Succeeded
                        ? "The exact Platform relation population could not "
                            + "be admitted to the Workspace."
                        : "The exact Platform relation population could not "
                            + "be admitted or retired.");
            }

            WorkspacePlatformPopulationDeclarationAdmissionOutcome
                declarationAdmission =
                    WorkspacePlatformPopulationDeclarationAdmission.Admit(
                        workspace,
                        accepted.Receipt,
                        completed.Population.Value,
                        completed.Population.Receipt,
                        PlatformTypeLocatorRouting.InventoryBounds);
            if (declarationAdmission
                is not WorkspacePlatformPopulationDeclarationAdmissionOutcome
                    .Admitted admitted)
            {
                InspectionWorkspaceCloseReport closeReport =
                    await workspace.CloseAsync().ConfigureAwait(false);
                workspaceClosed = true;
                return new ExactTypeRelationsInspectionOutcome.Unavailable(
                    closeReport.Succeeded
                        ? "The exact Platform relation declarations could "
                            + "not be admitted to the Workspace."
                        : "The exact Platform relation declarations could "
                            + "not be admitted or retired.");
            }

            ExactTypeRelationsInspectionOutcome outcome =
                ExactTypeRelationsInspectionOperation.Execute(
                    workspace,
                    admitted.Context,
                    request,
                    plan,
                    count,
                    rows,
                    rowSelection,
                    includeNonPublic,
                    cancellationToken);
            InspectionWorkspaceCloseReport report =
                await workspace.CloseAsync().ConfigureAwait(false);
            workspaceClosed = true;
            return report.Succeeded
                ? outcome
                : new ExactTypeRelationsInspectionOutcome.Unavailable(
                    "The exact Platform relation Workspace could not be "
                        + "retired.");
        }
        finally
        {
            if (!resourcesSettled)
            {
                await PlatformTypeCatalogRouting.RetireAsync(completed)
                    .ConfigureAwait(false);
            }
            else if (!workspaceClosed)
            {
                await workspace.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static WorkspaceRegistrationRevision RegistrationSnapshot(
        InspectionWorkspace workspace) =>
        workspace.GetRegistrationSnapshot() switch
        {
            WorkspaceRegistrationReadResult.Available available =>
                available.Revision,
            _ => throw new InvalidOperationException(
                "A new Workspace must expose its initial registration "
                    + "revision."),
        };

    private static string Describe(
        CliPlatformTypeCatalogOutcome.NotCompleted failure) =>
        failure.HouseReceipt?.Termination
            is PlatformHouseTermination.Rejected rejected
                ? rejected.Rejection switch
                {
                    PlatformHouseRejection.OwnerEvidence owner =>
                        $"{failure.Kind}: {owner.Kind}, "
                            + $"{owner.Evidence.Name}",
                    _ => $"{failure.Kind}: "
                        + rejected.Rejection.GetType().Name,
                }
                : failure.Kind.ToString();
}
