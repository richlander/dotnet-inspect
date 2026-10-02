using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.PerformanceOracles;
using DotnetInspector.ResearchQueries;
using DotnetInspector.ResearchSections;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Decompiler;
using ILInspector.Metadata;

using NLinq;

if (args.Length >= 4
    && string.Equals(args[0], "repeat-one", StringComparison.Ordinal))
{
    if (!int.TryParse(
            args[1],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out int rounds)
        || rounds <= 0
        || args[2] is not ("Old" or "Planner"))
    {
        Console.Error.WriteLine(
            "usage: repeat-one <positive-rounds> Old|Planner "
            + "<assembly>...");
        return 2;
    }

    string column = args[2];
    string[] paths = [.. args[3..].Select(Path.GetFullPath)];
    Console.WriteLine(
        "round\tcolumn\tmilliseconds\tallocated-bytes\tcount");
    for (int round = 0; round < rounds; round++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int total = column == "Old"
            ? paths.Sum(CountOld)
            : CountPlannerMany(paths);
        TimeSpan elapsed =
            System.Diagnostics.Stopwatch.GetElapsedTime(started);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread()
            - allocatedBefore;
        Console.WriteLine(
            $"{round + 1}\t{column}\t"
            + $"{elapsed.TotalMilliseconds:F3}\t"
            + $"{allocated}\t{total}");
    }
    return 0;
}

if (args.Length >= 3
    && string.Equals(args[0], "repeat", StringComparison.Ordinal))
{
    if (!int.TryParse(
            args[1],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out int rounds)
        || rounds <= 0)
    {
        Console.Error.WriteLine(
            "usage: repeat <positive-rounds> <assembly>...");
        return 2;
    }

    string[] paths =
    [
        .. args[2..].Select(Path.GetFullPath),
    ];
    Console.WriteLine(
        "round\tcolumn\tmilliseconds\tallocated-bytes\tcount");
    for (int round = 0; round < rounds; round++)
    {
        string[] order = (round & 1) == 0
            ? ["Old", "Planner"]
            : ["Planner", "Old"];
        foreach (string column in order)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int total = column == "Old"
                ? paths.Sum(CountOld)
                : CountPlannerMany(paths);
            TimeSpan elapsed =
                System.Diagnostics.Stopwatch.GetElapsedTime(started);
            long allocated =
                GC.GetAllocatedBytesForCurrentThread()
                - allocatedBefore;
            Console.WriteLine(
                $"{round + 1}\t{column}\t"
                + $"{elapsed.TotalMilliseconds:F3}\t"
                + $"{allocated}\t{total}");
        }
    }
    return 0;
}

if (args.Length >= 3
    && string.Equals(args[0], "once", StringComparison.Ordinal))
{
    string column = args[1];
    Func<string, int>? count = column switch
    {
        "Old" => CountOld,
        "LINQ" => CountLinq,
        "NLinq" => CountNLinq,
        "Planner" => null,
        _ => throw new ArgumentException(
            $"Unknown scorecard column '{column}'.",
            nameof(args)),
    };
    string[] paths = [.. args[2..].Select(Path.GetFullPath)];
    int total = count is null
        ? CountPlannerMany(paths)
        : paths.Sum(count);
    Console.WriteLine(
        $"{column} Count: {total}; assemblies: {args.Length - 2}");
    return 0;
}

if (!ScorecardCommandLine.TryParse(
        args,
        out ScorecardOptions? options,
        out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

IReadOnlyList<ScorecardAsset<string>> assets =
    ScorecardAssetNames.FromPaths(options!.Assets)
        .Select(
            (name, index) =>
                new ScorecardAsset<string>(
                    name,
                    Path.GetFullPath(options.Assets[index])))
        .ToArray();
ScorecardColumn<string, byte> oracle =
    new("NLinq", static (_, path) => CountAnswer(CountNLinq(path)));
ScorecardColumn<string, byte>[] columns =
[
    new("Old", static (_, path) => CountAnswer(CountOld(path))),
    new("LINQ", static (_, path) => CountAnswer(CountLinq(path))),
    oracle,
    new("Planner", static (_, path) => CountAnswer(CountPlanner(path))),
];
ScorecardClosing[] closings = [ScorecardClosing.Count];

ScorecardCheck check = Scorecard.Check(
    assets,
    oracle,
    columns,
    static row => row.ToString(
        System.Globalization.CultureInfo.InvariantCulture),
    closings: closings);
foreach (ScorecardMismatch mismatch in check.Mismatches)
{
    Console.WriteLine(
        $"mismatch\t{mismatch.Asset}\tCount\t"
        + $"{mismatch.Column}\t{mismatch.Answer}\t"
        + $"oracle={mismatch.OracleAnswer}");
}
Console.WriteLine(
    $"# answers: {check.Compared} compared, "
    + $"{check.Mismatches.Count} mismatches");
if (!check.Agrees)
    return 1;

foreach (ScorecardAsset<string> asset in assets)
{
    CountEvidence evidence = ReadEvidence(asset.Asset);
    Console.WriteLine(
        $"# evidence: {asset.Name}; count={evidence.Count}; "
        + $"scanned={evidence.ScannedBodyCount}; "
        + $"candidates={evidence.DeclarationCandidateCount}; "
        + $"old-rows={evidence.EagerRowCount}; "
        + $"planner-rows={evidence.PlannerMaterializedRowCount}");
}

if (options.Command == ScorecardCommand.Check)
    return 0;

IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
    assets,
    columns,
    options.Timing,
    progress => Console.Error.WriteLine(progress),
    closings);
if (options.TsvPath is { } tsvPath)
{
    using StreamWriter tsv = File.CreateText(tsvPath);
    Scorecard.WriteTsv(cells, tsv);
}

Console.Write(Scorecard.Report(cells, oracle.Name, new ScorecardShape()));
return 0;

static ScorecardAnswer<byte> CountAnswer(int count) =>
    ScorecardAnswer<byte>.OfCount(count);

static int CountOld(string path)
{
    return ReadOld(path).Count;
}

static int CountLinq(string path) =>
    ReadPopulation(path).Rows.Count(static row => row.Eligible);

static int CountNLinq(string path)
{
    CandidateRow[] rows = ReadPopulation(path).Rows;
    var eligible = rows
        .AsNLinq()
        .Where<
            ArrayEnumerator<CandidateRow>,
            CandidateRow,
            IsEligible>(default);
    return eligible.CountFold<
        Filter<
            CandidateRow,
            ArrayEnumerator<CandidateRow>,
            IsEligible>,
        CandidateRow>();
}

static int CountPlanner(string path) =>
    CountPlannerMany([path]);

static int CountPlannerMany(IReadOnlyList<string> paths)
{
    var sessions = new AssemblyInspectionSession[paths.Count];
    try
    {
        var subjects =
            new ReturnToSenderTargetSubject[paths.Count];
        for (int index = 0; index < paths.Count; index++)
        {
            sessions[index] =
                AssemblyInspectionSession.Open(paths[index]);
            subjects[index] =
                new(paths[index], sessions[index]);
        }

        ReturnToSenderTargetCountOutcome outcome =
            ReturnToSenderTargetInspection.Count(
                subjects,
                ReturnToSenderTargetQuery.CreateCountRequest());
        var counted =
            outcome as ReturnToSenderTargetCountOutcome.Counted
            ?? throw new InvalidOperationException(
                "The shipping RTS target Count request was rejected.");
        if (counted.Receipt.MaterializedRowCount != 0)
        {
            throw new InvalidOperationException(
                "The shipping RTS target Count materialized target rows.");
        }
        return counted.Count;
    }
    finally
    {
        for (int index = sessions.Length - 1; index >= 0; index--)
            sessions[index]?.Dispose();
    }
}

static CountEvidence ReadEvidence(string path)
{
    OldCount old = ReadOld(path);
    PlannerCount planner = ReadPlanner(path);
    if (old.Count != planner.Count
        || old.ScannedBodyCount != planner.ScannedBodyCount
        || old.DeclarationCandidateCount
            != planner.DeclarationCandidateCount)
    {
        throw new InvalidOperationException(
            "The old eager target source and shipping Planner did not "
            + "observe the same completed population.");
    }
    return new(
        planner.Count,
        planner.ScannedBodyCount,
        planner.DeclarationCandidateCount,
        old.MaterializedRowCount,
        planner.MaterializedRowCount);
}

static OldCount ReadOld(string path)
{
    using var metadata = MetadataSource.Open(path);
    using AssemblyInspectionSession assembly =
        AssemblyInspectionSession.Open(path);
    using var source =
        new ReturnToSenderTargetSourceSession(path, assembly);
    var decisions =
        new Dictionary<int, ReturnToSenderTargetDecision>();
    var targets = new List<ReturnToSenderTarget>();
    var exclusions = new List<ReturnToSenderTargetExclusion>();
    int scannedBodyCount = 0;
    int declarationCandidateCount = 0;
    int eligibleCount = 0;
    foreach (IrImporter.StableSampleCandidate candidate
        in IrImporter.GetStableSampleCandidates(
            metadata,
            int.MaxValue,
            candidate =>
            {
                scannedBodyCount++;
                ReturnToSenderTargetDecision? decision =
                    source.Decide(
                        candidate,
                        typeFilter: null,
                        out bool declarationCandidate);
                if (declarationCandidate)
                    declarationCandidateCount++;
                if (decision is null)
                    return false;

                int token = System.Reflection.Metadata.Ecma335
                    .MetadataTokens.GetToken(
                        candidate.MethodHandle);
                decisions.Add(token, decision);
                if (decision.Exclusion is { } exclusion)
                {
                    exclusions.Add(exclusion);
                    return false;
                }

                eligibleCount++;
                return true;
            },
            candidate =>
            {
                int token = System.Reflection.Metadata.Ecma335
                    .MetadataTokens.GetToken(
                        candidate.MethodHandle);
                return decisions[token].StableIdentitySuffix;
            }))
    {
        int token = System.Reflection.Metadata.Ecma335.MetadataTokens
            .GetToken(candidate.MethodHandle);
        targets.Add(
            decisions[token].Target
            ?? throw new InvalidOperationException(
                "An eligible RTS candidate lost its target."));
    }

    return new(
        eligibleCount,
        scannedBodyCount,
        declarationCandidateCount,
        checked(targets.Count + exclusions.Count));
}

static CandidatePopulation ReadPopulation(string path)
{
    using FileStream stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    MetadataReader reader = pe.GetMetadataReader();
    using AssemblyInspectionSession assembly =
        AssemblyInspectionSession.Open(path);
    using var source =
        new ReturnToSenderTargetSourceSession(path, assembly);
    var rows = new List<CandidateRow>();
    int scannedBodyCount = 0;
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        string typeName = reader.GetFullTypeName(type);
        var overloads = new Dictionary<string, int>();
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            MethodDefinition method =
                reader.GetMethodDefinition(methodHandle);
            string methodName = reader.GetString(method.Name);
            int overload = overloads.GetValueOrDefault(methodName);
            overloads[methodName] = overload + 1;
            if (method.RelativeVirtualAddress == 0)
                continue;

            scannedBodyCount++;
            ReturnToSenderTargetDecision? decision =
                source.Decide(
                    new(
                        typeName,
                        methodName,
                        overload,
                        typeHandle,
                        methodHandle),
                    typeFilter: null,
                    out bool declarationCandidate);
            if (!declarationCandidate)
                continue;
            if (decision is null
                || (decision.Target is null)
                    == (decision.Exclusion is null))
            {
                throw new InvalidOperationException(
                    "An RTS declaration candidate must produce exactly "
                    + "one target or exclusion.");
            }

            rows.Add(
                new(
                    decision.Target,
                    decision.Exclusion));
        }
    }

    return new([.. rows], scannedBodyCount);
}

static PlannerCount ReadPlanner(string path)
{
    using AssemblyInspectionSession assembly =
        AssemblyInspectionSession.Open(path);
    ReturnToSenderTargetCountOutcome outcome =
        ReturnToSenderTargetInspection.Count(
            [new(path, assembly)],
            ReturnToSenderTargetQuery.CreateCountRequest());
    var counted =
        outcome as ReturnToSenderTargetCountOutcome.Counted
        ?? throw new InvalidOperationException(
            "The shipping RTS target Count request was rejected.");
    return new(
        counted.Count,
        counted.Receipt.ScannedBodyCount,
        counted.Receipt.DeclarationCandidateCount,
        counted.Receipt.MaterializedRowCount);
}

readonly record struct CandidatePopulation(
    CandidateRow[] Rows,
    int ScannedBodyCount);

readonly record struct CandidateRow(
    ReturnToSenderTarget? Target,
    ReturnToSenderTargetExclusion? Exclusion)
{
    public bool Eligible => Target is not null;
}

readonly record struct PlannerCount(
    int Count,
    int ScannedBodyCount,
    int DeclarationCandidateCount,
    int MaterializedRowCount);

readonly record struct OldCount(
    int Count,
    int ScannedBodyCount,
    int DeclarationCandidateCount,
    int MaterializedRowCount);

readonly record struct CountEvidence(
    int Count,
    int ScannedBodyCount,
    int DeclarationCandidateCount,
    int EagerRowCount,
    int PlannerMaterializedRowCount);

readonly struct IsEligible : IFunc<CandidateRow, bool>
{
    public bool Invoke(CandidateRow row) => row.Eligible;
}
