using System.Collections.Immutable;
using System.Diagnostics;

using DotnetInspector.PerformanceOracles;
using DotnetInspector.Queries;
using ILInspector.Metadata;

const string usage =
    "usage: check|time [--rounds N] [--budget-ms N] [--tsv <path>] "
    + "'<assembly>|<base|interface>|<target>'...";

if (!ScorecardCommandLine.TryParse(
        args,
        out ScorecardOptions? options,
        out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
var assets = new List<ScorecardAsset<HierarchyAsset>>();
var images =
    new Dictionary<string, HierarchyImage>(StringComparer.Ordinal);
try
{
    foreach (string specification in options!.Assets)
    {
        if (!HierarchyAsset.TryLoad(
                specification,
                images,
                out HierarchyAsset? asset,
                out error))
        {
            Console.Error.WriteLine($"{error} {usage}");
            return 2;
        }
        assets.Add(new(asset!.Name, asset));
    }

    ScorecardColumn<HierarchyAsset, HierarchyRelationOracleRow> oracle =
        HierarchyPopulation.NLinqColumn(shape);
    ScorecardColumn<HierarchyAsset, HierarchyRelationOracleRow>[] columns =
    [
        HierarchyPopulation.OldColumn(shape),
        HierarchyPopulation.LinqColumn(shape),
        oracle,
        HierarchyPopulation.PlannerColumn(shape),
        HierarchyPopulation.IndexedColumn(shape),
    ];
    foreach (HierarchyImage image in images.Values)
    {
        HierarchyIndexPreparationObservation preparation =
            image.IndexPreparation;
        Console.WriteLine(
            $"# index preparation: {image.Name}, "
            + $"{preparation.Elapsed.TotalMilliseconds:F3} ms, "
            + $"{preparation.AllocatedBytes} B, "
            + $"borrow "
            + $"{preparation.BorrowElapsed.TotalMilliseconds:F3} ms, "
            + $"{preparation.BorrowAllocatedBytes} B, "
            + $"{preparation.Receipt.PhysicalRelationCount} relations, "
            + $"{preparation.Receipt.IndexedTargetCount} targets, "
            + $"{preparation.Receipt.Disposition}");
    }
    ScorecardCheck check =
        Scorecard.Check(
            assets,
            oracle,
            columns,
            HierarchyRelationOracle.RowText,
            HierarchyRelationOracleRowComparer.Instance);
    foreach (ScorecardMismatch mismatch in check.Mismatches)
    {
        Console.WriteLine(
            $"mismatch\t{mismatch.Asset}\t"
            + $"{shape.Label(mismatch.Closing)}\t{mismatch.Column}\t"
            + $"{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    }
    Console.WriteLine(
        $"# answers: {check.Compared} compared, "
        + $"{check.Mismatches.Count} mismatches, "
        + $"{check.WindowFailures.Count} strict-window failures");
    if (!check.Agrees)
        return 1;

    HierarchyRelationSafetyCheck safety =
        HierarchyRelationOracle.CheckSafety();
    foreach (HierarchyRelationSafetyMismatch mismatch
        in safety.Mismatches)
    {
        Console.WriteLine(
            $"safety-mismatch\t{mismatch.Asset}\t"
            + $"{mismatch.Column}\t{mismatch.Actual}\t"
            + $"oracle={mismatch.Expected}");
    }
    Console.WriteLine(
        $"# safety answers: {safety.Compared} compared, "
        + $"{safety.Mismatches.Count} mismatches");
    if (!safety.Agrees)
        return 1;

    foreach (string asset in check.WindowFailures)
    {
        Console.WriteLine(
            $"window-failed\t{asset}\t"
            + $"{shape.Label(ScorecardClosing.Window)}\t"
            + "every column fails: the window does not exist");
    }
    if (options.Command == ScorecardCommand.Check)
        return 0;

    IReadOnlyList<ScorecardCell> cells =
        Scorecard.Measure(
            assets,
            columns,
            options.Timing,
            progress => Console.Error.WriteLine(progress));
    if (options.TsvPath is { } tsvPath)
    {
        using StreamWriter tsv = File.CreateText(tsvPath);
        Scorecard.WriteTsv(cells, tsv);
    }

    Console.Write(Scorecard.Report(cells, oracle.Name, shape));
    return 0;
}
finally
{
    foreach (HierarchyImage image in images.Values)
        image.Dispose();
}

sealed class HierarchyAsset
{
    HierarchyAsset(
        string path,
        MetadataHierarchyRelationKind kind,
        MetadataTypeDefinitionName target,
        HierarchyImage image)
    {
        Kind = kind;
        Target = target;
        Image = image;
        Name =
            $"{Path.GetFileNameWithoutExtension(path)}:"
            + $"{kind}:{target.ToEscapedFullName()}";
        OldRequest = new(
            [MetadataRelationFamily.Hierarchy],
            MetadataOperationPolicy.Unbounded)
        {
            IncludeHidden = false,
        };
    }

    public string Name { get; }

    public MetadataHierarchyRelationKind Kind { get; }

    public MetadataTypeDefinitionName Target { get; }

    public AssemblyInspectionSession Session => Image.Session;

    public Func<
        MetadataHierarchyRelationAnalysisRequest,
        MetadataHierarchyRelationAnalysisOutcome> AnalyzeIndex =>
            Image.AnalyzeIndex;

    HierarchyImage Image { get; }

    public MetadataRelationInspectionRequest OldRequest { get; }

    public static bool TryLoad(
        string specification,
        Dictionary<string, HierarchyImage> images,
        out HierarchyAsset? asset,
        out string? error)
    {
        asset = null;
        string[] parts = specification.Split('|');
        if (parts is not [string path, string form, string targetText]
            || !File.Exists(path))
        {
            error = $"Invalid asset '{specification}'.";
            return false;
        }
        MetadataHierarchyRelationKind? kind =
            form.ToLowerInvariant() switch
            {
                "base" => MetadataHierarchyRelationKind.BaseType,
                "interface" => MetadataHierarchyRelationKind.Interface,
                _ => null,
            };
        int separator = targetText.LastIndexOf('.');
        string targetNamespace =
            separator < 0 ? string.Empty : targetText[..separator];
        string targetName =
            separator < 0 ? targetText : targetText[(separator + 1)..];
        if (kind is null
            || MetadataTypeDefinitionName.Create(
                    targetNamespace,
                    [targetName])
                is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            error = $"Invalid asset '{specification}'.";
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        if (!images.TryGetValue(
                fullPath,
                out HierarchyImage? image))
        {
            image = new(fullPath);
            images.Add(fullPath, image);
        }

        asset = new(
            fullPath,
            kind.Value,
            valid.Name,
            image);
        error = null;
        return true;
    }
}

sealed class HierarchyImage : IDisposable
{
    private readonly InspectionWorkspace _workspace;
    private readonly AssemblyContextGroup _group;
    private readonly AssemblyContextHierarchyRelationIndexExecution
        _execution;

    internal HierarchyImage(string path)
    {
        AssemblyInspectionSession? oracleSession = null;
        InspectionWorkspace? workspace = null;
        AssemblyContextGroup? group = null;
        AssemblyContextHierarchyRelationIndexExecution? execution = null;
        try
        {
            oracleSession =
                AssemblyInspectionSession.OpenPrefetched(
                    new MemoryStream(
                        File.ReadAllBytes(path),
                        writable: false));
            workspace = new();
            var participant =
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "subject-relations scorecard")),
                    NoResolverAssemblyBindingPolicy.Instance);
            group =
                workspace.CreateAssemblyContextGroup([participant]);
            AssemblyImageAccessResult<int> image =
                group.UseAssemblyImage(
                    participant.Assembly,
                    static _ => 0);
            if (image is AssemblyImageAccessResult<int>.Rejected rejected)
            {
                throw new InvalidOperationException(
                    rejected.Failure.Detail);
            }

            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            AssemblyContextHierarchyRelationIndexPreparation preparation =
                AssemblyContextHierarchyRelationIndexQuery
                    .PrepareParticipant(
                        group,
                        participant,
                        MetadataOperationPolicy.Unbounded);
            TimeSpan elapsed =
                Stopwatch.GetElapsedTime(started);
            long allocated =
                GC.GetAllocatedBytesForCurrentThread()
                    - allocatedBefore;

            AssemblyContextHierarchyRelationIndexPreparation.Ready ready =
                RequireIndex(preparation);
            allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            execution = ready.OpenExecution();
            TimeSpan borrowElapsed =
                Stopwatch.GetElapsedTime(started);
            long borrowAllocated =
                GC.GetAllocatedBytesForCurrentThread()
                    - allocatedBefore;

            Session = oracleSession;
            _workspace = workspace;
            _group = group;
            _execution = execution;
            AnalyzeIndex =
                request => _execution.Analyze(request);
            IndexPreparation =
                new(
                    elapsed,
                    allocated,
                    borrowElapsed,
                    borrowAllocated,
                    ready.Receipt);
            Name = Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception failure)
        {
            var cleanupFailures = new List<Exception>();
            try
            {
                execution?.Dispose();
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
            }
            try
            {
                group?.Dispose();
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
            }
            if (workspace is not null)
            {
                try
                {
                    workspace.DisposeAsync()
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception ex)
                {
                    cleanupFailures.Add(ex);
                }
            }
            try
            {
                oracleSession?.Dispose();
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
            }
            if (cleanupFailures.Count != 0)
            {
                throw new AggregateException(
                    [failure, .. cleanupFailures]);
            }
            throw;
        }
    }

    internal string Name { get; }

    internal AssemblyInspectionSession Session { get; }

    internal Func<
        MetadataHierarchyRelationAnalysisRequest,
        MetadataHierarchyRelationAnalysisOutcome> AnalyzeIndex { get; }

    internal HierarchyIndexPreparationObservation IndexPreparation { get; }

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        {
            _execution.Dispose();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
        try
        {
            _group.Dispose();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
        try
        {
            _workspace.DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
        try
        {
            Session.Dispose();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
        if (failures.Count != 0)
            throw new AggregateException(failures);
    }

    static AssemblyContextHierarchyRelationIndexPreparation.Ready
        RequireIndex(
            AssemblyContextHierarchyRelationIndexPreparation
                preparation) =>
        preparation switch
        {
            AssemblyContextHierarchyRelationIndexPreparation.Ready
                ready =>
                ready,
            AssemblyContextHierarchyRelationIndexPreparation
                .ParticipantRejected rejected =>
                throw new InvalidOperationException(
                    rejected.Failure.Detail),
            AssemblyContextHierarchyRelationIndexPreparation
                .InspectionFailed failed =>
                throw new InvalidOperationException(failed.Detail),
            AssemblyContextHierarchyRelationIndexPreparation
                .IndexFailed failed =>
                throw new InvalidOperationException(
                    string.Join(
                        ", ",
                        failed.Receipt.Diagnostics.Select(
                            static diagnostic => diagnostic.Detail))),
            AssemblyContextHierarchyRelationIndexPreparation
                .ImageRejected rejected =>
                throw new InvalidOperationException(
                    rejected.Detail),
            _ => throw new InvalidOperationException(
                "Unknown hierarchy index preparation outcome."),
        };
}

readonly record struct HierarchyIndexPreparationObservation(
    TimeSpan Elapsed,
    long AllocatedBytes,
    TimeSpan BorrowElapsed,
    long BorrowAllocatedBytes,
    MetadataHierarchyRelationIndexReceipt Receipt);

static class HierarchyPopulation
{
    public static ScorecardColumn<
        HierarchyAsset,
        HierarchyRelationOracleRow> OldColumn(
        ScorecardShape shape) =>
        new(
            "Old",
            (closing, asset) =>
                OldAnswer(closing, asset, shape));

    public static ScorecardColumn<
        HierarchyAsset,
        HierarchyRelationOracleRow> LinqColumn(
        ScorecardShape shape) =>
        new(
            "LINQ",
            (closing, asset) =>
                HierarchyRelationOracle.LinqAnswer(
                    asset.Session,
                    asset.Kind,
                    asset.Target,
                    closing,
                    shape));

    public static ScorecardColumn<
        HierarchyAsset,
        HierarchyRelationOracleRow> NLinqColumn(
        ScorecardShape shape) =>
        new(
            "NLinq",
            (closing, asset) =>
                HierarchyRelationOracle.NLinqAnswer(
                    asset.Session,
                    asset.Kind,
                    asset.Target,
                    closing,
                    shape));

    public static ScorecardColumn<
        HierarchyAsset,
        HierarchyRelationOracleRow> PlannerColumn(
        ScorecardShape shape) =>
        new(
            "Planner analysis (shipping)",
            (closing, asset) =>
                HierarchyRelationOracle.PlannerAnswer(
                    asset.Session,
                    asset.Kind,
                    asset.Target,
                    closing,
                    shape));

    public static ScorecardColumn<
        HierarchyAsset,
        HierarchyRelationOracleRow> IndexedColumn(
        ScorecardShape shape) =>
        new(
            "Prepared reverse index",
            (closing, asset) =>
                HierarchyRelationOracle.IndexedAnswer(
                    asset.AnalyzeIndex,
                    asset.Kind,
                    asset.Target,
                    closing,
                    shape));

    static ScorecardAnswer<HierarchyRelationOracleRow> OldAnswer(
        ScorecardClosing closing,
        HierarchyAsset asset,
        ScorecardShape shape)
    {
        MetadataRelationInspectionResult result =
            RequireAvailable(
                asset.Session.Relations(asset.OldRequest));
        IEnumerable<HierarchyRelationOracleRow> rows =
            result.Hierarchy.Evidence
                .Where(evidence =>
                    evidence.Kind == asset.Kind
                    && Matches(evidence.Target, asset.Target))
                .GroupBy(static evidence => evidence.Source)
                .Select(static group =>
                    new HierarchyRelationOracleRow(
                        group.Key.Definition.Value,
                        group.First().SourceType,
                        [.. group.Select(static row =>
                            row.MetadataToken)]));
        return closing switch
        {
            ScorecardClosing.Exists =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfExists(
                    rows.Any()),
            ScorecardClosing.Count =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfCount(
                    rows.Count()),
            ScorecardClosing.Head =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                    rows.Take(shape.N).ToList()),
            ScorecardClosing.Tail =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                    rows.TakeLast(shape.N).ToList()),
            ScorecardClosing.Rows =>
                ScorecardAnswer<HierarchyRelationOracleRow>.OfRows(
                    rows.ToList()),
            ScorecardClosing.Window =>
                rows.Skip(shape.WindowSkip)
                    .Take(shape.WindowTake)
                    .ToList() is { } window
                && window.Count == shape.WindowTake
                    ? ScorecardAnswer<HierarchyRelationOracleRow>
                        .OfRows(window)
                    : ScorecardAnswer<HierarchyRelationOracleRow>
                        .OfWindowFailure(),
            _ => throw new ArgumentOutOfRangeException(nameof(closing)),
        };
    }

    static MetadataRelationInspectionResult RequireAvailable(
        MetadataRelationInspectionOutcome outcome) =>
        outcome switch
        {
            MetadataRelationInspectionOutcome.Available available =>
                available.Result,
            MetadataRelationInspectionOutcome.Rejected rejected =>
                throw new InvalidOperationException(rejected.Detail),
            _ => throw new InvalidOperationException(
                "Unknown relation inspection outcome."),
        };

    static bool Matches(
        MetadataTypeIdentity identity,
        MetadataTypeDefinitionName target)
    {
        MetadataNamedTypeIdentity? named = identity switch
        {
            MetadataTypeIdentity.Named value => value.Definition,
            MetadataTypeIdentity.GenericInstance value =>
                value.Definition,
            _ => null,
        };
        return named is not null
            && named.Namespace.ToString() == target.Namespace
            && named.Segments
                .Select(static segment => segment.ToString())
                .SequenceEqual(target.Segments);
    }
}
