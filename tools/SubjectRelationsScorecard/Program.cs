using System.Collections.Immutable;
using System.Diagnostics;

using DotnetInspector.PerformanceOracles;
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

    public MetadataHierarchyRelationIndex Index => Image.Index;

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
    internal HierarchyImage(string path)
    {
        AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(
                File.ReadAllBytes(path),
                writable: false));
        try
        {
            Session = session;
            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            MetadataHierarchyRelationIndexPreparation preparation =
                Session.PrepareHierarchyRelationIndex(
                    MetadataOperationPolicy.Unbounded);
            TimeSpan elapsed =
                Stopwatch.GetElapsedTime(started);
            long allocated =
                GC.GetAllocatedBytesForCurrentThread()
                    - allocatedBefore;
            Index = RequireIndex(preparation);
            IndexPreparation =
                new(elapsed, allocated, Index.Receipt);
            Name = Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    internal string Name { get; }

    internal AssemblyInspectionSession Session { get; }

    internal MetadataHierarchyRelationIndex Index { get; }

    internal HierarchyIndexPreparationObservation IndexPreparation { get; }

    public void Dispose() => Session.Dispose();

    static MetadataHierarchyRelationIndex RequireIndex(
        MetadataHierarchyRelationIndexPreparation preparation) =>
        preparation switch
        {
            MetadataHierarchyRelationIndexPreparation.Ready ready =>
                ready.Index,
            MetadataHierarchyRelationIndexPreparation.Failed failed =>
                throw new InvalidOperationException(
                    string.Join(
                        ", ",
                        failed.Receipt.Diagnostics.Select(
                            static diagnostic => diagnostic.Detail))),
            MetadataHierarchyRelationIndexPreparation.Rejected rejected =>
                throw new InvalidOperationException(rejected.Detail),
            _ => throw new InvalidOperationException(
                "Unknown hierarchy index preparation outcome."),
        };
}

readonly record struct HierarchyIndexPreparationObservation(
    TimeSpan Elapsed,
    long AllocatedBytes,
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
                    asset.Index,
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
