using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

public partial class DependsCommand
{
    private static async Task<DependsPruningProjectionResult>
        AcquirePruningProjectionAsync(
            DependsOptions options,
            PackageDependencyEvidenceOutcome evidence,
            IReadOnlyList<int> admittedIndexes,
            IReadOnlyList<LibraryAssetResult> libraries,
            DesktopPackageSourceComposition composition,
            NuGetOperationContext operationContext,
            CommandContext context,
            Func<string, InstalledPlatformPruneSource.Result> pruneSource,
            CancellationToken cancellationToken)
    {
        string requestedFramework = options.Tfm
            ?? throw new InvalidOperationException(
                "The Pruning section requires a validated target framework.");
        PackageHouseTargetContext unboundTarget =
            PackageHouseTargetContext.Exact(requestedFramework);
        var rows = ImmutableArray.CreateBuilder<DependencyInspectionPruning>();
        var failures = ImmutableArray.CreateBuilder<DependencyInspectionFailure>();
        var pending = new List<PendingPruningDeclaration>();
        int failedLibraryRoots = libraries.Count(
            static library => library.Document is null);
        int roots = evidence.Roots.Length
            + evidence.FailedRoots.Length
            + libraries.Count;
        int declarations = 0;
        int notEvaluated = evidence.FailedRoots.Length + libraries.Count;
        int sourceBounded = 0;
        int failed = evidence.FailedRoots.Length + failedLibraryRoots;
        int successful = libraries.Count - failedLibraryRoots;

        for (int inputIndex = 0;
             inputIndex < evidence.Roots.Length;
             inputIndex++)
        {
            PackageDependencyEvidenceRoot root = evidence.Roots[inputIndex];
            if (root.Declaration
                    is not PackageDependencyEvidenceDeclarationResult
                        .Available available)
            {
                notEvaluated++;
                if (root.Declaration
                        is PackageDependencyEvidenceDeclarationResult.Failed
                            or PackageDependencyEvidenceDeclarationResult
                                .Unavailable)
                {
                    failed++;
                    if (root.Declaration
                        is PackageDependencyEvidenceDeclarationResult
                            .Unavailable)
                    {
                        failures.Add(
                            new DependencyInspectionFailure.Pruning(
                                new DependencyInspectionPruningFailure.Prerequisite(
                                    admittedIndexes[inputIndex],
                                    root.Identity,
                                    root.Display,
                                    DependencyEvidenceDeclarationState
                                        .Unavailable,
                                    new InertString(
                                        TextPolicy.Prose,
                                        "Dependency declaration evidence is unavailable for pruning evaluation."))));
                    }
                }
                else
                {
                    successful++;
                }
                continue;
            }

            bool declarationsIncomplete = !available.IsComplete;
            if (declarationsIncomplete)
                failed++;

            bool hasSelectedGroup =
                root.Selection.Status
                    == PackageDependencyEvidenceSelectionStatus.Selected
                && root.Selection.SelectedGroup is not null;
            ImmutableArray<PackageDependencyEvidenceGroup> groups =
                hasSelectedGroup
                    ? [
                        available.Groups.Single(group =>
                            group.Identity
                            == root.Selection.SelectedGroup),
                    ]
                    : available.Groups;
            bool producedOutcome = false;
            foreach (PackageDependencyEvidenceDeclaration declaration
                     in groups.SelectMany(static group =>
                         group.Declarations))
            {
                PackageHouseDependencyPruningApplicability applicability =
                    PackageHouseDependencyPruningApplicabilityQuery.Execute(
                        root,
                        declaration,
                        unboundTarget);
                if (!hasSelectedGroup
                    && applicability.State
                        is PackageHouseDependencyPruningApplicabilityState
                            .CandidateRequired
                            or PackageHouseDependencyPruningApplicabilityState
                                .TargetUnavailable)
                {
                    continue;
                }

                producedOutcome = true;
                declarations++;
                if (applicability.State
                    != PackageHouseDependencyPruningApplicabilityState
                        .CandidateRequired)
                {
                    rows.Add(
                        CreateApplicabilityRow(
                            admittedIndexes[inputIndex],
                            root,
                            declaration,
                            applicability));
                    notEvaluated++;
                    if (applicability.State is
                        PackageHouseDependencyPruningApplicabilityState
                            .ProcessingIncomplete
                        or PackageHouseDependencyPruningApplicabilityState
                            .ProcessingUnavailable
                        or PackageHouseDependencyPruningApplicabilityState
                            .ProcessingFailed)
                    {
                        failed++;
                    }
                    else
                    {
                        successful++;
                    }
                    continue;
                }

                if (!AuthorizesCandidateResolution(options, root))
                {
                    rows.Add(
                        CreateUnavailableRow(
                            admittedIndexes[inputIndex],
                            root,
                            declaration,
                            applicability,
                            DependencyInspectionPruningDisposition.SourceBounded,
                            "The explicit root authorizes direct declarations but not package-source candidate resolution."));
                    sourceBounded++;
                    successful++;
                    continue;
                }

                pending.Add(
                    new PendingPruningDeclaration(
                        admittedIndexes[inputIndex],
                        root,
                        declaration,
                        applicability));
            }

            if (!producedOutcome)
            {
                notEvaluated++;
                if (!declarationsIncomplete)
                    successful++;
                continue;
            }
        }

        int evaluated = 0;
        int delegated = 0;
        int retained = 0;
        if (pending.Count > 0)
        {
            string family =
                (options.PruningPlatformFamily ?? "runtime")
                    .ToLowerInvariant();
            string frameworkSpec = CreateFrameworkSpec(
                family,
                requestedFramework);
            InstalledPlatformPruneSource.Result inventoryResult =
                pruneSource(frameworkSpec);
            if (inventoryResult.Inventory is not { } inventory)
            {
                string message = inventoryResult.Error
                    ?? $"The installed {family} pruning inventory is unavailable for '{requestedFramework}'.";
                ImmutableArray<int> affectedRoots =
                [
                    .. pending
                        .Select(static item => item.RootOccurrence)
                        .Distinct()
                        .Order(),
                ];
                failures.Add(
                    new DependencyInspectionFailure.Pruning(
                        new DependencyInspectionPruningFailure.Inventory(
                            family,
                            requestedFramework,
                            new InertString(TextPolicy.Prose, message),
                            affectedRoots,
                            pending.Count)));
                foreach (PendingPruningDeclaration item in pending)
                {
                    rows.Add(
                        CreateUnavailableRow(
                            item.RootOccurrence,
                            item.Root,
                            item.Declaration,
                            item.Applicability,
                            DependencyInspectionPruningDisposition.InventoryUnavailable,
                            message));
                }
                failed += pending.Count;
            }
            else if (!TryCreateTargetContext(
                         requestedFramework,
                         family,
                         inventory,
                         out PackageHouseTargetContext? target,
                         out string? targetError))
            {
                ImmutableArray<int> affectedRoots =
                [
                    .. pending
                        .Select(static item => item.RootOccurrence)
                        .Distinct()
                        .Order(),
                ];
                failures.Add(
                    new DependencyInspectionFailure.Pruning(
                        new DependencyInspectionPruningFailure.Inventory(
                            family,
                            requestedFramework,
                            new InertString(
                                TextPolicy.Prose,
                                targetError),
                            affectedRoots,
                            pending.Count)));
                foreach (PendingPruningDeclaration item in pending)
                {
                    rows.Add(
                        CreateUnavailableRow(
                            item.RootOccurrence,
                            item.Root,
                            item.Declaration,
                            item.Applicability,
                            DependencyInspectionPruningDisposition
                                .InventoryUnavailable,
                            targetError));
                }
                failed += pending.Count;
            }
            else
            {
                var candidateSource =
                    new DesktopPackageDependencyCandidateSource(
                        composition,
                        options.SourceOptions,
                        context.Logger.Log);
                var inspectionRequest =
                    new PackageDependencyPruningInspectionRequest(
                        pending.Select(
                            item =>
                                new PackageDependencyPruningInspectionSubject(
                                    item.Root,
                                    item.Declaration)),
                        target,
                        inventory);
                InspectionEnvelope<PackageDependencyPruningInspectionResult>
                    inspection =
                        await PackageDependencyPruningInspection.ExecuteAsync(
                            inspectionRequest,
                            candidateSource,
                            cancellationToken,
                            operationContext).ConfigureAwait(false);
                if (inspection.Content.Outcomes.Length != pending.Count)
                {
                    throw new InvalidOperationException(
                        "Package dependency pruning inspection did not preserve the requested subject count.");
                }

                for (int index = 0; index < pending.Count; index++)
                {
                    PendingPruningDeclaration item = pending[index];
                    PackageDependencyPruningInspectionOutcome outcome =
                        inspection.Content.Outcomes[index];
                    if (!ReferenceEquals(
                            item.Root,
                            outcome.Subject.Root)
                        || !ReferenceEquals(
                            item.Declaration,
                            outcome.Subject.Declaration))
                    {
                        throw new InvalidOperationException(
                            "Package dependency pruning inspection did not preserve subject order.");
                    }

                    if (outcome
                        is PackageDependencyPruningInspectionOutcome
                            .CandidateUnavailable candidateUnavailable)
                    {
                        rows.Add(
                            CreateCandidateUnavailableRow(
                                item,
                                candidateUnavailable,
                                target,
                                inventory));
                        failures.Add(
                            new DependencyInspectionFailure.Pruning(
                                new DependencyInspectionPruningFailure.Candidate(
                                    item.RootOccurrence,
                                    item.Root.Identity,
                                    item.Declaration.Identity,
                                    item.Declaration.CanonicalPackageId,
                                    item.Declaration
                                        .CanonicalVersionConstraint,
                                    DependencyInspectionPackageCandidateOutcome
                                        .Create(
                                            candidateUnavailable.Candidate))
                                {
                                    RuntimeOutcome =
                                        candidateUnavailable.Candidate,
                                }));
                        failed++;
                        continue;
                    }

                    PackageDependencyPruningInspectionOutcome.Evaluated
                        evaluatedOutcome =
                            outcome
                                as PackageDependencyPruningInspectionOutcome
                                    .Evaluated
                            ?? throw new InvalidOperationException(
                                "A pending package dependency pruning subject did not produce a candidate-bound outcome.");
                    rows.Add(
                        CreateEvaluatedRow(
                            item,
                            evaluatedOutcome));
                    evaluated++;
                    if (evaluatedOutcome.Result.Pruning.Supply
                        .DelegatesToPlatform)
                    {
                        delegated++;
                    }
                    else
                    {
                        retained++;
                    }
                    successful++;
                }
            }
        }

        DependencyInspectionPruningCompletion completion =
            failed > 0
                ? successful > 0
                    ? DependencyInspectionPruningCompletion.Partial
                    : DependencyInspectionPruningCompletion.Failed
                : sourceBounded > 0
                    ? DependencyInspectionPruningCompletion.SourceBounded
                    : DependencyInspectionPruningCompletion.Complete;
        return new DependsPruningProjectionResult(
            rows.ToImmutable(),
            failures.ToImmutable(),
            new DependencyInspectionPruningSummary(
                completion,
                roots,
                declarations,
                evaluated,
                delegated,
                retained,
                notEvaluated,
                sourceBounded,
                failed));
    }

    private static bool AuthorizesCandidateResolution(
        DependsOptions options,
        PackageDependencyEvidenceRoot root) =>
        options.PackagePrefix is null
        && root.InputKind == PackageDependencyEvidenceInputKind.PackageManifest
        && root.Provenance.AcquisitionForm
            != PackageDependencyEvidenceAcquisitionForm.DirectNuspec;

    private static string CreateFrameworkSpec(
        string family,
        string requestedFramework)
    {
        PlatformTargetFramework target =
            PlatformTargetFramework.Parse(requestedFramework.ToLowerInvariant());
        return $"{family}@{target.Major}.{target.Minor}";
    }

    private static bool TryCreateTargetContext(
        string requestedFramework,
        string requestedFamily,
        PlatformPruneInventory inventory,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out PackageHouseTargetContext? target,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)]
        out string? error)
    {
        target = null;
        error = null;
        if (!string.Equals(
                inventory.TargetFramework,
                requestedFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            error =
                $"The installed {requestedFamily} inventory describes '{inventory.TargetFramework}', not requested target '{requestedFramework}'.";
            return false;
        }

        string sharedFramework = requestedFamily switch
        {
            "runtime" => "Microsoft.NETCore.App",
            "aspnetcore" => "Microsoft.AspNetCore.App",
            _ => throw new InvalidOperationException(
                $"Unsupported pruning platform family '{requestedFamily}'."),
        };
        PlatformPruneFamily? family =
            inventory.Families.SingleOrDefault(candidate =>
            candidate.Name.Equals(
                sharedFramework,
                StringComparison.OrdinalIgnoreCase));
        if (family is null)
        {
            error =
                $"The installed {requestedFamily} inventory does not contain '{sharedFramework}'.";
            return false;
        }

        var platformTarget = new PlatformFamilyTarget(
            requestedFamily == "runtime"
                ? PlatformFamily.DotNetRuntime
                : PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse(
                inventory.TargetFramework.ToLowerInvariant()),
            PlatformVersion.Parse(
                family.TargetPackVersion.ToNormalizedString()));
        target = PackageHouseTargetContext.Exact(
            requestedFramework,
            platformTarget: platformTarget);
        return true;
    }

    private static DependencyInspectionPruning CreateApplicabilityRow(
        int rootOccurrence,
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration declaration,
        PackageHouseDependencyPruningApplicability applicability) =>
        CreateRow(
            rootOccurrence,
            root,
            declaration,
            applicability,
            CandidateVersion: null,
            PlatformFamily: null,
            PlatformTargetFramework: null,
            PlatformVersion: null,
            PlatformProvidedVersion: null,
            DependencyInspectionPruningDisposition.NotEvaluated,
            applicability.State.ToString(),
            CandidateOutcome: null,
            Result: null);

    private static DependencyInspectionPruning CreateUnavailableRow(
        int rootOccurrence,
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration declaration,
        PackageHouseDependencyPruningApplicability applicability,
        DependencyInspectionPruningDisposition disposition,
        string reason) =>
        CreateRow(
            rootOccurrence,
            root,
            declaration,
            applicability,
            CandidateVersion: null,
            PlatformFamily: null,
            PlatformTargetFramework: null,
            PlatformVersion: null,
            PlatformProvidedVersion: null,
            disposition,
            reason,
            CandidateOutcome: null,
            Result: null);

    private static DependencyInspectionPruning CreateCandidateUnavailableRow(
        PendingPruningDeclaration item,
        PackageDependencyPruningInspectionOutcome.CandidateUnavailable
            outcome,
        PackageHouseTargetContext target,
        PlatformPruneInventory inventory) =>
        CreateRow(
            item.RootOccurrence,
            item.Root,
            item.Declaration,
            item.Applicability,
            CandidateVersion: null,
            PlatformFamily: target.PlatformTarget!.Family.ToString(),
            PlatformTargetFramework:
                target.PlatformTarget.TargetFramework.ToString(),
            PlatformVersion: target.PlatformTarget.Version.Value,
            PlatformProvidedVersion: PlatformProvidedVersion(
                inventory,
                item.Declaration.CanonicalPackageId),
            DependencyInspectionPruningDisposition.CandidateUnavailable,
            outcome.Candidate switch
            {
                PackageDependencyCandidateResult.Failed failed =>
                    failed.Failure.GetType().Name,
                PackageDependencyCandidateResult.Incomplete incomplete =>
                    incomplete.Evidence.GetType().Name,
                _ => throw new InvalidOperationException(
                    "A resolved candidate is not unavailable."),
            },
            outcome.Candidate,
            Result: null);

    private static string? PlatformProvidedVersion(
        PlatformPruneInventory inventory,
        string packageId) =>
        inventory.TryGetEntry(packageId, out PlatformPruneEntry entry)
            ? entry.SuppliedVersion.ToNormalizedString()
            : null;

    private static DependencyInspectionPruning CreateEvaluatedRow(
        PendingPruningDeclaration item,
        PackageDependencyPruningInspectionOutcome.Evaluated outcome)
    {
        PackageHousePruningReceipt pruning = outcome.Result.Pruning;
        bool delegates = pruning.Supply.DelegatesToPlatform;
        return CreateRow(
            item.RootOccurrence,
            item.Root,
            item.Declaration,
            outcome.Applicability,
            outcome.Candidate.Candidate.Coordinate.Version,
            pruning.Target.Family.ToString(),
            pruning.Target.TargetFramework.ToString(),
            pruning.Target.Version.Value,
            pruning.Supply.SuppliedVersion?.ToNormalizedString(),
            delegates
                ? DependencyInspectionPruningDisposition.PlatformDelegation
                : DependencyInspectionPruningDisposition.PackageRetained,
            pruning.Supply.Subsumption.ToString(),
            outcome.Candidate,
            outcome.Result);
    }

    private static DependencyInspectionPruning CreateRow(
        int rootOccurrence,
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration declaration,
        PackageHouseDependencyPruningApplicability applicability,
        string? CandidateVersion,
        string? PlatformFamily,
        string? PlatformTargetFramework,
        string? PlatformVersion,
        string? PlatformProvidedVersion,
        DependencyInspectionPruningDisposition disposition,
        string reason,
        PackageDependencyCandidateResult? CandidateOutcome,
        PackageHouseDependencyPruningResult? Result)
    {
        return new DependencyInspectionPruning(
            rootOccurrence,
            root.Identity,
            root.Display,
            declaration.Identity,
            root.Selection.RequestedFramework
                ?? new InertString(TextPolicy.Field, string.Empty),
            root.Selection.SelectedFramework
                ?? new InertString(TextPolicy.Field, string.Empty),
            declaration.CanonicalPackageId,
            declaration.SourcePackageIdSpelling,
            declaration.CanonicalVersionConstraint,
            declaration.SourceVersionConstraintSpelling,
            CandidateVersion,
            PlatformFamily,
            PlatformTargetFramework,
            PlatformVersion,
            PlatformProvidedVersion,
            disposition,
            reason,
            applicability,
            CandidateOutcome is { } candidateOutcome
                ? DependencyInspectionPackageCandidateOutcome.Create(
                    candidateOutcome)
                : null,
            Result is { } result
                ? DependencyInspectionPruningResult.Create(result)
                : null)
        {
            RuntimeCandidateOutcome = CandidateOutcome,
            RuntimeResult = Result,
        };
    }

    private sealed record PendingPruningDeclaration(
        int RootOccurrence,
        PackageDependencyEvidenceRoot Root,
        PackageDependencyEvidenceDeclaration Declaration,
        PackageHouseDependencyPruningApplicability Applicability);

    private sealed record DependsPruningProjectionResult(
        ImmutableArray<DependencyInspectionPruning> Rows,
        ImmutableArray<DependencyInspectionFailure> Failures,
        DependencyInspectionPruningSummary Summary);
}
