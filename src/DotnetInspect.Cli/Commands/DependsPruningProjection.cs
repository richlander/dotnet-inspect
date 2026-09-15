using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
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
        var rows = ImmutableArray.CreateBuilder<DependsPruningRow>();
        var failures = ImmutableArray.CreateBuilder<DependsFailureRow>();
        var pending = new List<PendingPruningDeclaration>();
        int roots = 0;
        int declarations = 0;
        int notEvaluated = 0;
        int sourceBounded = 0;
        int failed = 0;
        int successful = 0;

        for (int inputIndex = 0;
             inputIndex < evidence.Roots.Length;
             inputIndex++)
        {
            PackageDependencyEvidenceRoot root = evidence.Roots[inputIndex];
            roots++;
            if (root.Declaration
                    is not PackageDependencyEvidenceDeclarationResult
                        .Available available)
            {
                notEvaluated++;
                if (root.Declaration
                        is PackageDependencyEvidenceDeclarationResult.Failed
                    || root.Declaration
                        is PackageDependencyEvidenceDeclarationResult.Available
                        {
                            IsComplete: false,
                        })
                {
                    failed++;
                }
                else
                {
                    successful++;
                }
                continue;
            }

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
                            DependsPruningDisposition.SourceBounded,
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
                    new DependsFailureRow.Pruning(
                        new DependsPruningFailure.Inventory(
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
                            DependsPruningDisposition.InventoryUnavailable,
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
                    new DependsFailureRow.Pruning(
                        new DependsPruningFailure.Inventory(
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
                            DependsPruningDisposition
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
                foreach (PendingPruningDeclaration item in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PackageDependencyCandidateResult candidate =
                        await PackageDependencyCandidateQuery.ExecuteAsync(
                            new PackageDependencyCandidateRequest.Declared(
                                item.Declaration),
                            candidateSource,
                            cancellationToken,
                            operationContext).ConfigureAwait(false);
                    if (candidate
                        is not PackageDependencyCandidateResult.Resolved
                            resolved)
                    {
                        rows.Add(
                            CreateCandidateUnavailableRow(
                                item,
                                candidate,
                                target,
                                inventory));
                        failures.Add(
                            new DependsFailureRow.Pruning(
                                new DependsPruningFailure.Candidate(
                                    item.RootOccurrence,
                                    item.Root.Identity,
                                    item.Declaration.Identity,
                                    item.Declaration.CanonicalPackageId,
                                    item.Declaration
                                        .CanonicalVersionConstraint,
                                    candidate)));
                        failed++;
                        continue;
                    }

                    PackageHouseDependencyInput input =
                        PackageHouseDependencyInputAdapter.Create(
                            item.Root,
                            resolved,
                            PackageHouseOperation.Create(
                                PackageHouseOperationProfile.Settle),
                            target);
                    PackageHouseDependencyPruningResult result =
                        PackageHouseDependencyPruningQuery.Execute(
                            input,
                            inventory);
                    rows.Add(
                        CreateEvaluatedRow(
                            item,
                            resolved,
                            result));
                    evaluated++;
                    if (result
                        is PackageHouseDependencyPruningResult.Evaluated
                        {
                            Pruning.Supply.DelegatesToPlatform: true,
                        })
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

        DependsPruningCompletion completion =
            failed > 0
                ? successful > 0
                    ? DependsPruningCompletion.Partial
                    : DependsPruningCompletion.Failed
                : sourceBounded > 0
                    ? DependsPruningCompletion.SourceBounded
                    : DependsPruningCompletion.Complete;
        return new DependsPruningProjectionResult(
            rows.ToImmutable(),
            failures.ToImmutable(),
            new DependsPruningSummary(
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

    private static DependsPruningRow CreateApplicabilityRow(
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
            DependsPruningDisposition.NotEvaluated,
            applicability.State.ToString(),
            CandidateOutcome: null,
            Result: null);

    private static DependsPruningRow CreateUnavailableRow(
        int rootOccurrence,
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration declaration,
        PackageHouseDependencyPruningApplicability applicability,
        DependsPruningDisposition disposition,
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

    private static DependsPruningRow CreateCandidateUnavailableRow(
        PendingPruningDeclaration item,
        PackageDependencyCandidateResult candidate,
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
            DependsPruningDisposition.CandidateUnavailable,
            candidate switch
            {
                PackageDependencyCandidateResult.Failed failed =>
                    failed.Failure.GetType().Name,
                PackageDependencyCandidateResult.Incomplete incomplete =>
                    incomplete.Evidence.GetType().Name,
                _ => throw new InvalidOperationException(
                    "A resolved candidate is not unavailable."),
            },
            candidate,
            Result: null);

    private static string? PlatformProvidedVersion(
        PlatformPruneInventory inventory,
        string packageId) =>
        inventory.TryGetEntry(packageId, out PlatformPruneEntry entry)
            ? entry.SuppliedVersion.ToNormalizedString()
            : null;

    private static DependsPruningRow CreateEvaluatedRow(
        PendingPruningDeclaration item,
        PackageDependencyCandidateResult.Resolved candidate,
        PackageHouseDependencyPruningResult result)
    {
        PackageHouseDependencyPruningResult.Evaluated evaluated =
            result as PackageHouseDependencyPruningResult.Evaluated
            ?? throw new InvalidOperationException(
                "A candidate-required pruning input must produce an evaluated result.");
        PackageHousePruningReceipt pruning = evaluated.Pruning;
        bool delegates = pruning.Supply.DelegatesToPlatform;
        return CreateRow(
            item.RootOccurrence,
            item.Root,
            item.Declaration,
            item.Applicability,
            candidate.Candidate.Coordinate.Version,
            pruning.Target.Family.ToString(),
            pruning.Target.TargetFramework.ToString(),
            pruning.Target.Version.Value,
            pruning.Supply.SuppliedVersion?.ToNormalizedString(),
            delegates
                ? DependsPruningDisposition.PlatformDelegation
                : DependsPruningDisposition.PackageRetained,
            pruning.Supply.Subsumption.ToString(),
            candidate,
            result);
    }

    private static DependsPruningRow CreateRow(
        int rootOccurrence,
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration declaration,
        PackageHouseDependencyPruningApplicability applicability,
        string? CandidateVersion,
        string? PlatformFamily,
        string? PlatformTargetFramework,
        string? PlatformVersion,
        string? PlatformProvidedVersion,
        DependsPruningDisposition disposition,
        string reason,
        PackageDependencyCandidateResult? CandidateOutcome,
        PackageHouseDependencyPruningResult? Result) =>
        new(
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
            CandidateOutcome,
            Result);

    private sealed record PendingPruningDeclaration(
        int RootOccurrence,
        PackageDependencyEvidenceRoot Root,
        PackageDependencyEvidenceDeclaration Declaration,
        PackageHouseDependencyPruningApplicability Applicability);

    private sealed record DependsPruningProjectionResult(
        ImmutableArray<DependsPruningRow> Rows,
        ImmutableArray<DependsFailureRow> Failures,
        DependsPruningSummary Summary);
}
