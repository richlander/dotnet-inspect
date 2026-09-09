using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.PackageQueries;

/// <summary>Evaluates one frozen package selection and releases its candidate before returning.</summary>
public static class PackageAssemblyEvaluator
{
    public static async Task<PackageAssemblyEvaluationOutcome> EvaluateAsync(
        PackageRootBinding binding,
        PackageAssemblyPatternRequest pattern,
        PackageAssemblyEvaluationBudget budget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(budget);
        cancellationToken.ThrowIfCancellationRequested();

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(budget.MaximumDuration);
        long started = Stopwatch.GetTimestamp();
        var subject = new PackageAssemblyEvaluationSubject(
            binding.CreateReacquisitionRequest(),
            binding.ContentGenerationIdentity,
            binding.SelectionIdentity,
            pattern);
        PackageCompileAssetSelection selection = binding.Root.AssetSelection;
        PackageAssemblyEvaluationOutcome? selectionOutcome =
            ResolveSelection(subject, selection, out PackageCompileAsset? asset,
                out PackageAssemblySelectedAssetContext? context);
        if (selectionOutcome is not null)
        {
            ObserveCancellation();
            return selectionOutcome;
        }
        if (asset is null || context is null)
            throw new InvalidOperationException("A selected package evaluation requires one canonical asset.");

        var workspace = InspectionWorkspace.CreateAsynchronous();
        PackageAssemblyEvaluationOutcome? outcome = null;
        ExceptionDispatchInfo? primary = null;
        SparsePackageProjectionCleanupReceipt? projectionCleanup = null;
        InspectionWorkspaceCloseReport? closeReport = null;
        bool transferred = false;
        bool closeFaulted = false;
        try
        {
            ObserveCancellation();
            SparsePackageAssemblyProjectionOutcome projection =
                await workspace.ProjectSelectedPackageAssemblyAsync(
                    binding,
                    asset,
                    new SparsePackageAssemblyProjectionOptions
                    {
                        MaxSelectedEntryBytes = budget.MaximumEntryBytes,
                        MaxAggregateRetainedImageBytes = budget.MaximumRetainedImageBytes,
                    },
                    operation.Token).ConfigureAwait(false);
            if (projection is SparsePackageAssemblyProjectionOutcome.Available available)
            {
                transferred = true;
                ObserveCancellation();
                outcome = EvaluateAdmitted(available.Realization, context, budget, operation.Token);
            }
            else
            {
                (PackageAssemblyFailureReason reason, projectionCleanup) =
                    ProjectionFailure(projection, budget);
                outcome = new PackageAssemblyEvaluationOutcome.Failure(subject, context, reason);
            }
        }
        catch (Exception failure)
        {
            primary = ExceptionDispatchInfo.Capture(failure);
            projectionCleanup = SparsePackageProjectionCleanupReceipt.FromException(failure);
        }
        finally
        {
            // Results and exceptions remain provisional until the release owner has finished.
            try
            {
                closeReport = await workspace.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                primary ??= ExceptionDispatchInfo.Capture(failure);
                closeFaulted = true;
                closeReport = workspace.CloseReport;
            }
        }

        var cleanup = new PackageAssemblyEvaluationCleanupEvidence(
            projectionCleanup,
            DescribeClose(closeReport, transferred, closeFaulted));
        if (primary is not null)
        {
            PackageAssemblyEvaluationExceptionEvidence.Attach(primary.SourceException, cleanup);
            primary.Throw();
        }

        try
        {
            ObserveCancellation();
        }
        catch (OperationCanceledException canceled)
        {
            PackageAssemblyEvaluationExceptionEvidence.Attach(canceled, cleanup);
            throw;
        }

        if (outcome is null)
            throw new InvalidOperationException("Package evaluation completed without an outcome.");
        if (cleanup.IsEmpty)
            return outcome;

        return new PackageAssemblyEvaluationOutcome.Failure(
            subject,
            context,
            outcome is PackageAssemblyEvaluationOutcome.Failure failureOutcome
                ? failureOutcome.Reason
                : new PackageAssemblyFailureReason.CandidateCleanup(cleanup),
            cleanup);

        void ObserveCancellation()
        {
            if (Stopwatch.GetElapsedTime(started) >= budget.MaximumDuration)
                operation.Cancel();
            operation.Token.ThrowIfCancellationRequested();
        }
    }

    static PackageAssemblyEvaluationOutcome? ResolveSelection(
        PackageAssemblyEvaluationSubject subject,
        PackageCompileAssetSelection selection,
        out PackageCompileAsset? asset,
        out PackageAssemblySelectedAssetContext? context)
    {
        asset = null;
        context = null;
        switch (selection.Status)
        {
            case PackageCompileAssetSelectionStatus.NoCompileAssets:
                return NotApplicable(PackageAssemblyNotApplicableReason.NoCompileAssets);
            case PackageCompileAssetSelectionStatus.NoMatchingTargetFramework:
                return NotApplicable(PackageAssemblyNotApplicableReason.NoMatchingTargetFramework);
            case PackageCompileAssetSelectionStatus.EmptyCompileGroup:
                return NotApplicable(PackageAssemblyNotApplicableReason.EmptyCompileGroup);
            case PackageCompileAssetSelectionStatus.InvalidImplementationAssets:
                return new PackageAssemblyEvaluationOutcome.Failure(
                    subject, null, new PackageAssemblyFailureReason.InvalidSelection(selection.Status));
            case PackageCompileAssetSelectionStatus.Selected:
                break;
            default:
                throw new InvalidOperationException("Unknown package compile selection status.");
        }

        PackageCompileAsset primary = selection.DefaultAsset
            ?? throw new InvalidOperationException("A selected package has no default compile asset.");
        if (subject.Pattern.Pattern.Role == PackageAssemblyPatternRole.CompileSurface)
        {
            asset = primary;
            context = SelectedContext(subject, asset, selection.Assets,
                PackageAssemblyAssetSequence.Compile, selection.Assets.Count - 1);
            return null;
        }
        if (subject.Pattern.Pattern.Role != PackageAssemblyPatternRole.ImplementationBody)
            throw new InvalidOperationException("Unknown package assembly pattern role.");

        asset = selection.FindImplementationAsset(primary);
        if (asset is null)
        {
            context = SelectedContext(subject, primary, selection.Assets,
                PackageAssemblyAssetSequence.Compile, selection.ImplementationAssets.Count);
            return new PackageAssemblyEvaluationOutcome.NotApplicable(
                subject, PackageAssemblyNotApplicableReason.NoImplementationCounterpart, context);
        }

        context = SelectedContext(subject, asset, selection.ImplementationAssets,
            PackageAssemblyAssetSequence.Implementation, selection.ImplementationAssets.Count - 1);
        return null;

        PackageAssemblyEvaluationOutcome NotApplicable(PackageAssemblyNotApplicableReason reason) =>
            new PackageAssemblyEvaluationOutcome.NotApplicable(subject, reason);
    }

    static PackageAssemblySelectedAssetContext SelectedContext(
        PackageAssemblyEvaluationSubject subject,
        PackageCompileAsset asset,
        IReadOnlyList<PackageCompileAsset> sequence,
        PackageAssemblyAssetSequence sequenceKind,
        int siblings)
    {
        for (int index = 0; index < sequence.Count; index++)
        {
            if (ReferenceEquals(sequence[index], asset))
            {
                return new(
                    subject,
                    new(sequenceKind, index),
                    new(
                        asset.Kind,
                        new InertString(TextPolicy.Field, asset.AssemblyName),
                        new InertString(TextPolicy.Field, asset.TargetFramework),
                        new InertString(TextPolicy.Field, asset.Path)),
                    siblings);
            }
        }
        throw new InvalidOperationException("The selected package asset is not in its frozen sequence.");
    }

    static PackageAssemblyEvaluationOutcome EvaluateAdmitted(
        SparsePackageAssemblyRealization realization,
        PackageAssemblySelectedAssetContext context,
        PackageAssemblyEvaluationBudget budget,
        CancellationToken cancellationToken)
    {
        switch (realization.Admission)
        {
            case ArtifactAssemblyProjectionOutcome.NotAssembly notAssembly:
                return Failure(new PackageAssemblyFailureReason.NotAssembly(
                    PackageAssemblyImageAdmissionStage.Projection, notAssembly.Kind));
            case ArtifactAssemblyProjectionOutcome.Rejected rejected:
                return Failure(new PackageAssemblyFailureReason.ProjectionRejected(rejected.Failure));
            case ArtifactAssemblyProjectionOutcome.Projected projected:
                cancellationToken.ThrowIfCancellationRequested();
                ArtifactAssemblyQueryOutcome<StringLiteralUsePatternResult> query =
                    realization.ExecuteAssemblyQuery(
                        (session, token) => StringLiteralUsePatternAnalysis.Inspect(
                            session, context.Subject.Pattern.Operand, budget.SemanticBudget, token),
                        cancellationToken);
                return query switch
                {
                    ArtifactAssemblyQueryOutcome<StringLiteralUsePatternResult>.NotAssembly value =>
                        Failure(new PackageAssemblyFailureReason.NotAssembly(
                            PackageAssemblyImageAdmissionStage.Query, value.Kind)),
                    ArtifactAssemblyQueryOutcome<StringLiteralUsePatternResult>.Rejected value =>
                        Failure(new PackageAssemblyFailureReason.QueryRejected(value.Failure)),
                    ArtifactAssemblyQueryOutcome<StringLiteralUsePatternResult>.Validated value =>
                        SemanticOutcome(value.Value, projected.Value.Registration.ModuleVersionId),
                    _ => throw new InvalidOperationException("Unknown Metadata query outcome."),
                };
            default:
                return Failure(new PackageAssemblyFailureReason.Contract(
                    PackageAssemblyFailureStage.ProjectionContractViolation));
        }

        PackageAssemblyEvaluationOutcome SemanticOutcome(
            StringLiteralUsePatternResult result, Guid mvid) =>
            result switch
            {
                StringLiteralUsePatternResult.Match match
                    when !match.Occurrences.IsDefaultOrEmpty
                        && match.Occurrences.All(value => value.Address.ModuleVersionId == mvid) =>
                    new PackageAssemblyEvaluationOutcome.Matched(context, match),
                StringLiteralUsePatternResult.Match =>
                    Failure(new PackageAssemblyFailureReason.Contract(
                        PackageAssemblyFailureStage.SemanticProducerContractViolation)),
                StringLiteralUsePatternResult.NoMatch noMatch =>
                    new PackageAssemblyEvaluationOutcome.NoMatch(context, noMatch.Receipt),
                StringLiteralUsePatternResult.Rejected rejected =>
                    Failure(new PackageAssemblyFailureReason.SemanticRejection(
                        rejected.Rejection, rejected.Receipt)),
                StringLiteralUsePatternResult.WorkLimitExceeded limited =>
                    Failure(new PackageAssemblyFailureReason.SemanticWorkLimit(
                        limited.Limit, budget.SemanticBudget, limited.Receipt)),
                _ => throw new InvalidOperationException("Unknown literal producer outcome."),
            };

        PackageAssemblyEvaluationOutcome Failure(PackageAssemblyFailureReason reason) =>
            new PackageAssemblyEvaluationOutcome.Failure(context.Subject, context, reason);
    }

    static (PackageAssemblyFailureReason, SparsePackageProjectionCleanupReceipt?)
        ProjectionFailure(
            SparsePackageAssemblyProjectionOutcome projection,
            PackageAssemblyEvaluationBudget budget) =>
        projection switch
        {
            SparsePackageAssemblyProjectionOutcome.InvalidBinding =>
                (new PackageAssemblyFailureReason.Contract(
                    PackageAssemblyFailureStage.InvalidBinding), null),
            SparsePackageAssemblyProjectionOutcome.InvalidSelectedAsset =>
                (new PackageAssemblyFailureReason.Contract(
                    PackageAssemblyFailureStage.ProjectionContractViolation), null),
            SparsePackageAssemblyProjectionOutcome.SelectedEntryUnavailable unavailable =>
                (new PackageAssemblyFailureReason.EntryUnavailable(), unavailable.Cleanup),
            SparsePackageAssemblyProjectionOutcome.EntryByteLimitExceeded limited =>
                (new PackageAssemblyFailureReason.EntryByteLimit(
                    budget.MaximumEntryBytes, budget.MaximumRetainedImageBytes), limited.Cleanup),
            SparsePackageAssemblyProjectionOutcome.ArtifactPublicationFailed failed =>
                (new PackageAssemblyFailureReason.ArtifactPublication(
                    [.. failed.Failures.Select(failure =>
                        new PackageAssemblyArtifactFailure(failure.Kind, failure.Diagnostic.Code))]),
                    failed.Cleanup),
            _ => (new PackageAssemblyFailureReason.Contract(
                PackageAssemblyFailureStage.ProjectionContractViolation), null),
        };

    internal static ImmutableArray<PackageAssemblyCandidateCleanupFailure> DescribeClose(
        InspectionWorkspaceCloseReport? report,
        bool transferred,
        bool closeFaulted)
    {
        var failures = ImmutableArray.CreateBuilder<PackageAssemblyCandidateCleanupFailure>();
        if (report is not null)
        {
            if (!transferred)
            {
                Add(PackageAssemblyCandidateCleanupStage.CloseReportContract,
                    report.Groups.Length + report.ArtifactSessionCleanupFailures.Length);
            }
            else
            {
                if (report.Groups is not [InspectionWorkspaceDirectGroupCloseResult])
                    Add(PackageAssemblyCandidateCleanupStage.CloseReportContract, 1);
                Add(PackageAssemblyCandidateCleanupStage.GroupRelease,
                    report.Groups.OfType<InspectionWorkspaceDirectGroupCloseResult>()
                        .Count(group => !group.Succeeded));
                Add(PackageAssemblyCandidateCleanupStage.ArtifactSessionRelease,
                    report.ArtifactSessionCleanupFailures.Length);
            }
        }
        if (closeFaulted)
            Add(PackageAssemblyCandidateCleanupStage.CloseOrchestration, 1);
        return failures.ToImmutable();

        void Add(PackageAssemblyCandidateCleanupStage stage, int count)
        {
            if (count > 0)
                failures.Add(new(stage, count));
        }
    }
}
