using System.Reflection.Metadata;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
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
            WorkspaceContextLoadOptions capabilities,
            CommandContext context,
            NuGetSourceOptions sourceOptions,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sourceOptions);

        var workspace = new InspectionWorkspace();
        Task<PlatformPopulationArtifactMaterializationOutcome>
            realizationTask =
                PlatformTypeCatalogRouting.RealizePopulationAsync(
                        PlatformTypeCatalogRouting.FindActiveDotnetRoot(),
                        target,
                        context,
                        sourceOptions,
                        cancellationToken)
                    .AsTask();
        WorkspaceMemberCoordinate.PlatformMember requestedLibrary =
            RequestedLibrary(request);
        Task<(
            string? AssemblyPath,
            string? Framework,
            string? Version,
            string? Error)> focusTask =
                PlatformResolver.ResolveAssemblyAsync(
                    requestedLibrary.Assembly!,
                    capabilities.HttpClient,
                    capabilities.Log,
                    requestedLibrary.Family,
                    useRuntimeAssemblies: true,
                    platformVersion: requestedLibrary.Version,
                    sourceOptions: sourceOptions);
        PlatformPopulationArtifactMaterializationOutcome.Completed?
            completed = null;
        bool resourcesSettled = false;
        bool workspaceClosed = false;
        try
        {
            try
            {
                await Task.WhenAll(realizationTask, focusTask)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (realizationTask.IsCompletedSuccessfully
                    && realizationTask.Result
                        is PlatformPopulationArtifactMaterializationOutcome
                            .Completed realized)
                {
                    completed = realized;
                }
                throw;
            }

            PlatformPopulationArtifactMaterializationOutcome realization =
                await realizationTask.ConfigureAwait(false);
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

            completed =
                (PlatformPopulationArtifactMaterializationOutcome.Completed)
                    realization;
            var (focusPath, resolvedFamily, resolvedVersion, focusError) =
                await focusTask.ConfigureAwait(false);
            if (focusPath is null
                || focusError is not null
                || !string.Equals(
                    resolvedFamily,
                    requestedLibrary.Family,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    resolvedVersion,
                    requestedLibrary.Version,
                    StringComparison.OrdinalIgnoreCase))
            {
                ExactTypeRelationsInspectionOutcome.Unavailable unavailable =
                    await CloseUnavailableAsync(
                        workspace,
                        focusError
                            ?? "The requested Platform Library could not be "
                                + "resolved at its exact coordinate.")
                    .ConfigureAwait(false);
                workspaceClosed = true;
                return unavailable;
            }

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

            WorkspaceDeclarationPopulation population =
                workspace.CaptureDeclarationPopulation([admitted.Context])
                            is WorkspaceDeclarationPopulationCapture.Captured captured
                                ? captured.Population
                                : throw new InvalidOperationException(
                                    "The exact Platform relation population could not "
                                        + "be captured.");
            WorkspaceExactTypeFocusOutcome focus =
                WorkspaceExactTypeFocusQuery.Execute(
                            population,
                            request.Type,
                            request.SelectionKind,
                            cancellationToken: cancellationToken);
            if (focus is not WorkspaceExactTypeFocusOutcome.Found found)
            {
                var focusUnavailable =
                            (WorkspaceExactTypeFocusOutcome.Unavailable)focus;
                ExactTypeRelationsInspectionOutcome.Unavailable unavailable =
                            await CloseUnavailableAsync(
                                workspace,
                                focusUnavailable.Detail)
                            .ConfigureAwait(false);
                workspaceClosed = true;
                return unavailable;
            }

            using (AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(focusPath))
            {
                AssemblyReferenceIdentity focusAssembly =
                            session.AssemblyIdentity();
                TypeDeclarationResult declaration =
                            session.InspectImage(
                                reader =>
                                    MetadataTypeDeclarationProbe.ProbeDefinition(
                                        reader.GetMetadataReader(),
                                        found.Type));
                if (!focusAssembly.Name.Equals(
                                requestedLibrary.Assembly,
                                StringComparison.OrdinalIgnoreCase)
                            || declaration
                                is not (TypeDeclarationResult.Defined
                                    or TypeDeclarationResult
                                        .DefinitionKindUnavailable))
                {
                            ExactTypeRelationsInspectionOutcome.Unavailable
                                unavailable =
                                    await CloseUnavailableAsync(
                                        workspace,
                                        "The requested Platform Library does not "
                                            + "declare the selected exact Type.")
                                    .ConfigureAwait(false);
                            workspaceClosed = true;
                            return unavailable;
                }
            }

            ExactTypeRelationsInspectionOutcome outcome =
                ExactTypeRelationsInspectionOperation.Execute(
                            workspace,
                    admitted.Context,
                    request with
                    {
                        Type = found.Type.ToEscapedFullName(),
                        SelectionKind =
                            ExactTypeSelectionKind.DefinitionIdentity,
                        FocusAssemblyName = null,
                        FocusLibrary = null,
                    },
                    population,
                    found,
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
            try
            {
                if (completed is not null && !resourcesSettled)
                {
                    await PlatformTypeCatalogRouting.RetireAsync(completed)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                if (!workspaceClosed)
                {
                    await workspace.CloseAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private static WorkspaceMemberCoordinate.PlatformMember
        RequestedLibrary(TypeRelationsInspectionRequest request)
    {
        WorkspaceMemberCoordinate.PlatformMember[] members =
        [
            .. request.Context.Members.OfType<
                WorkspaceMemberCoordinate.PlatformMember>(),
        ];
        if (members is not
            [
                {
                    Assembly: not null,
                    Version: not null,
                } member,
            ])
        {
            throw new InvalidOperationException(
                "An exact Platform relation request must contain one pinned "
                    + "Library coordinate.");
        }
        return member;
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

    private static async ValueTask<
        ExactTypeRelationsInspectionOutcome.Unavailable>
        CloseUnavailableAsync(
            InspectionWorkspace workspace,
            string detail)
    {
        InspectionWorkspaceCloseReport report =
            await workspace.CloseAsync().ConfigureAwait(false);
        return new(
            report.Succeeded
                ? detail
                : $"{detail} The Platform focus Workspace could not be "
                    + "retired.");
    }

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
