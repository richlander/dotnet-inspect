using System.Collections.Immutable;
using System.Runtime.InteropServices;
using DotnetInspector.Libraries;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
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
        PlatformLibraryRealizationSource? platformImplementationSource = null,
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
                platformImplementationSource,
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
        PlatformLibraryRealizationSource? platformImplementationSource = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(plan);
        WorkspaceContextInput input = request.Context;
        var workspace = new InspectionWorkspace(
            new WorkspacePlan([], [input]));
        var contexts = new List<WorkspaceDeclarationContext>();
        var selectedPlatformAssemblies =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WorkspaceMemberCoordinate.PlatformMember member
            in input.Members.OfType<
                WorkspaceMemberCoordinate.PlatformMember>())
        {
            if (member.Assembly is not null)
                selectedPlatformAssemblies.Add(member.Assembly);
        }
        string? focusAssemblyName = request.FocusAssemblyName;
        AssemblyReferenceIdentity? platformAssemblyDemand =
            PlatformAssemblyDemand(input);
        ExactTypeRelationsInspectionOutcome? outcome = null;
        try
        {
            for (int expansion = 0;
                expansion <= MaxPlatformFocusExpansions;
                expansion++)
            {
                WorkspaceDeclarationContext context =
                    platformImplementationSource is not null
                        && platformAssemblyDemand is not null
                            ? await LoadPlatformDeclarationContextAsync(
                                    workspace,
                                    input,
                                    platformAssemblyDemand,
                                    platformImplementationSource,
                                    cancellationToken)
                                .ConfigureAwait(false)
                            : await WorkspaceContextLoader
                                .LoadDeclarationContextAsync(
                                    workspace,
                                    input,
                                    capabilities,
                                    cancellationToken)
                                .ConfigureAwait(false);
                if (!context.Receipt.IsRealized)
                {
                    string detail = string.Join(
                        " ",
                        context.Receipt.Failures
                            .OfType<WorkspaceDeclarationFailure.ContextLoad>()
                            .Select(failure => failure.Failure.Message));
                    if (string.IsNullOrWhiteSpace(detail))
                    {
                        detail = string.Join(
                            " ",
                            context.Receipt.Failures.Select(
                                static failure => failure.ToString()));
                    }
                    outcome = new ExactTypeRelationsInspectionOutcome
                        .Unavailable(
                            string.IsNullOrWhiteSpace(detail)
                                ? "The exact Type candidate context could not "
                                    + "be loaded."
                                : detail);
                }
                else
                {
                    contexts.Add(context);
                    WorkspaceDeclarationPopulation population =
                        workspace.CaptureDeclarationPopulation([.. contexts])
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
                            focusAssemblyName,
                            request.FocusLibrary,
                            cancellationToken: cancellationToken);
                    if (focus
                        is WorkspaceExactTypeFocusOutcome
                            .PlatformAssemblyRequired required)
                    {
                        WorkspaceContextInput? expandedInput =
                            expansion < MaxPlatformFocusExpansions
                                ? CreatePlatformFocusContext(
                                    request.Context,
                                    required.Assembly,
                                    selectedPlatformAssemblies)
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
                            break;
                        }
                        selectedPlatformAssemblies.Add(
                            required.Assembly.Name);
                        input = expandedInput;
                        focusAssemblyName = required.Assembly.Name;
                        platformAssemblyDemand = required.Assembly;
                        continue;
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
                break;
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
        return outcome
            ?? new ExactTypeRelationsInspectionOutcome.Unavailable(
                "The exact Type focus exceeded the platform expansion bound.");
    }

    private static WorkspaceContextInput? CreatePlatformFocusContext(
        WorkspaceContextInput rootInput,
        AssemblyReferenceIdentity required,
        IReadOnlySet<string> selectedAssemblies)
    {
        WorkspaceMemberCoordinate.PlatformMember[] platformMembers =
        [
            .. rootInput.Members.OfType<
                WorkspaceMemberCoordinate.PlatformMember>(),
        ];
        if (platformMembers.Length == 0
            || rootInput.Members.Count != platformMembers.Length
            || platformMembers.Any(member => member.Assembly is null)
            || selectedAssemblies.Contains(required.Name))
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

        return rootInput with
        {
            Members =
            [
                WorkspaceMemberCoordinate.Platform(
                    root.Family,
                    required.Name,
                    root.Version,
                    root.Framework),
            ],
        };
    }

    private static AssemblyReferenceIdentity? PlatformAssemblyDemand(
        WorkspaceContextInput input)
    {
        WorkspaceMemberCoordinate.PlatformMember[] members =
        [
            .. input.Members.OfType<
                WorkspaceMemberCoordinate.PlatformMember>(),
        ];
        return members.Length == 1
            && input.Members.Count == 1
            && members[0].Assembly is { } assembly
                ? new AssemblyReferenceIdentity(
                    assembly,
                    Version: null,
                    Culture: null,
                    PublicKeyToken: null)
                : null;
    }

    private static async Task<WorkspaceDeclarationContext>
        LoadPlatformDeclarationContextAsync(
        InspectionWorkspace workspace,
        WorkspaceContextInput input,
        AssemblyReferenceIdentity assembly,
        PlatformLibraryRealizationSource source,
        CancellationToken cancellationToken)
    {
        WorkspaceMemberCoordinate.PlatformMember member =
            input.Members.OfType<
                WorkspaceMemberCoordinate.PlatformMember>().Single();
        PlatformFamily family =
            member.Family.ToLowerInvariant() switch
            {
                "runtime" => PlatformFamily.DotNetRuntime,
                "aspnetcore" => PlatformFamily.AspNetCore,
                _ => throw new InvalidOperationException(
                    $"Platform family '{member.Family}' is unavailable."),
            };
        if (string.IsNullOrWhiteSpace(member.Version)
            || string.IsNullOrWhiteSpace(member.Framework))
        {
            throw new InvalidOperationException(
                "PlatformHouse exact Library realization requires a version "
                    + "and target framework.");
        }
        var target = new PlatformFamilyTarget(
            family,
            PlatformTargetFramework.Parse(member.Framework),
            PlatformVersion.Parse(member.Version));
        PlatformHouseRequest houseRequest = new(
            PlatformHouseRequestIdentity.Create(
                "exact-type-relations-platform-library"),
            new PlatformTargetDemand.Exact(target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "exact-type-relations")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.AssemblyReferenceBinding(
                        assembly)),
                PlatformViewDemand.Implementation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "exact-type-relations-platform-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [source.Capability]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 1,
                maxTargetCandidates: 0,
                maxAssemblies: 1,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 512L * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromMinutes(2)),
            cancellationToken);
        PlatformLibraryArtifactMaterializationOutcome realization =
            await PlatformHouseExactImplementationLibraryExecutor
                .ExecuteAsync(
                    houseRequest,
                    source,
                    "exact-type-relations-platform-library")
                .ConfigureAwait(false);
        if (realization
            is PlatformLibraryArtifactMaterializationOutcome.Terminal terminal)
        {
            throw new InvalidOperationException(
                "PlatformHouse could not realize the exact implementation "
                    + $"Library "
                    + $"({terminal.TerminalRealization.Outcome.GetType().Name}).");
        }

        var completed =
            (PlatformLibraryArtifactMaterializationOutcome.Completed)
                realization;
        LibraryContentOwner owner = completed.Library.Owner;
        LibraryReference library = completed.Library.Value.Library;
        ExactLibrarySourceCoordinate.Platform coordinate =
            library.SourceCoordinate
                as ExactLibrarySourceCoordinate.Platform
                ?? throw new InvalidOperationException(
                    "The exact Platform Library has no Platform coordinate.");
        LibraryContentReference content =
            library.ImplementationAssembly
                ?? throw new InvalidOperationException(
                    "The exact Platform implementation Library has no "
                        + "implementation assembly.");
        PlatformLibraryArtifactProvenance artifactProvenance =
            content.Provenance
                as PlatformLibraryArtifactProvenance
                ?? throw new InvalidOperationException(
                    "The exact Platform implementation Library has no "
                        + "PlatformHouse provenance.");
        ResolvedAssemblyReference assemblyReference;
        Exception? primaryFailure = null;
        try
        {
            if (owner.IssueOperationLease(library)
                is not LibraryOperationLeaseIssueOutcome.Issued issued)
            {
                throw new InvalidOperationException(
                    "The exact Platform Library operation could not be "
                        + "issued.");
            }
            AssemblyResolutionProvenance selection =
                AssemblyResolutionProvenance.Platform(
                    target.Family switch
                    {
                        PlatformFamily.DotNetRuntime =>
                            "Microsoft.NETCore.App",
                        PlatformFamily.AspNetCore =>
                            "Microsoft.AspNetCore.App",
                        _ => throw new InvalidOperationException(
                            "Unknown Platform family."),
                    },
                    target.Version.Value,
                    artifactProvenance.Contribution.Capability.Name);
            using (LibraryOperationLease lease = issued.Lease)
            {
                assemblyReference = lease.Snapshot(
                    content,
                    selection,
                    static (view, state, _) =>
                        SnapshotAssembly(view, state),
                    cancellationToken);
            }
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            await RetirePlatformLibraryAsync(
                    completed,
                    primaryFailure)
                .ConfigureAwait(false);
        }

        IAcquisitionFreeAssemblyBindingPolicy bindingPolicy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
                [
                    (
                        assemblyReference,
                        (IAcquisitionFreeAssemblyBindingPolicy)
                            NoResolverAssemblyBindingPolicy.Instance
                    ),
                ]);
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [new AssemblyContextParticipant(
                    assemblyReference,
                    bindingPolicy)]);
        int order = workspace.BeginDeclarationContext();
        var context = new WorkspaceDeclarationContext(
            new WorkspaceDeclarationContextReceipt(
                workspace.Identity,
                order,
                new WorkspaceDeclarationRequest.PlatformPopulation(target),
                isRealized: true,
                [
                    new WorkspaceDeclarationMember(
                        new(workspace.Identity, order, 0),
                        coordinate,
                        assemblyReference.Identity,
                        new WorkspaceDeclarationOrigin.PlatformPopulation(
                            target,
                            WorkspacePlatformPopulationMemberRole.Focus,
                            artifactProvenance.Contribution.Capability.Name,
                            assemblyReference.Identity.Name),
                        assemblyReference.Provenance),
                ],
                []),
            group);
        return workspace.PublishDeclarationContext(context);
    }

    private static ResolvedAssemblyReference SnapshotAssembly(
        scoped LibraryContentView view,
        AssemblyResolutionProvenance provenance)
    {
        ImmutableArray<byte> content = view.Snapshot;
        byte[] bytes =
            ImmutableCollectionsMarshal.AsArray(content)!;
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                view.Reference.Registration,
                () => new MemoryStream(bytes, writable: false),
                provenance)
            ?? throw new BadImageFormatException(
                "The exact Platform implementation Library is not a "
                    + "managed assembly.");
        if (view.Reference.AssemblyIdentity is not { } expected
            || !assembly.Identity.IsEquivalentTo(expected.Identity))
        {
            throw new BadImageFormatException(
                "The exact Platform implementation Library changed assembly "
                    + "identity during Workspace projection.");
        }
        return assembly;
    }

    private static async ValueTask RetirePlatformLibraryAsync(
        PlatformLibraryArtifactMaterializationOutcome.Completed completed,
        Exception? primaryFailure)
    {
        var cleanupFailures = new List<Exception>();
        try
        {
            await completed.Library.Owner.DisposeAsync()
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            cleanupFailures.Add(failure);
        }
        cleanupFailures.AddRange(
            completed.Library.Owner.CleanupFailures);
        cleanupFailures.AddRange(
            completed.Library.Owner.ReleaseFailures.Select(
                static failure => failure.Failure));

        try
        {
            await completed.Artifacts.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            cleanupFailures.Add(failure);
        }
        cleanupFailures.AddRange(completed.Artifacts.CleanupFailures);
        if (cleanupFailures.Count == 0)
            return;
        if (primaryFailure is not null)
        {
            ArtifactSetSession.AttachCleanupFailures(
                primaryFailure,
                cleanupFailures);
            return;
        }
        throw new AggregateException(
            "The exact Platform Library could not be retired.",
            cleanupFailures);
    }
}
