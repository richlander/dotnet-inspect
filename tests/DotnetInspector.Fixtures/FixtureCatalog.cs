namespace DotnetInspector.Fixtures;

public sealed record FixtureDefinition(
    string Id,
    string ProjectName,
    string AssemblyFileName,
    IReadOnlyList<string> Tags)
{
    public string AssemblyPath() => FixtureCatalog.AssemblyPath(Id);
}

public sealed record FixturePair(string Id, FixtureDefinition Old, FixtureDefinition New)
{
    public string OldAssemblyPath() => Old.AssemblyPath();
    public string NewAssemblyPath() => New.AssemblyPath();
}

public sealed record FixtureGroup(string Id, IReadOnlyList<FixtureDefinition> Fixtures);

public static class FixtureIds
{
    public const string DiffV1 = "diff.v1";
    public const string DiffV2 = "diff.v2";
    public const string DiffAsmCaller = "diff-asm.caller";
    public const string DiffAsmLibA = "diff-asm.lib-a";
    public const string DiffAsmLibB = "diff-asm.lib-b";
    public const string DiffAsmTarget = "diff-asm.target";

    public const string AnalysisCallerGraphCaller = "analysis.caller-graph.caller";
    public const string AnalysisCallerGraphCallerTwin = "analysis.caller-graph.caller-twin";
    public const string AnalysisCallerGraphLookalikeCaller = "analysis.caller-graph.lookalike-caller";
    public const string AnalysisCallerGraphTarget = "analysis.caller-graph.target";
    public const string AnalysisCrossAsmCollision = "analysis.cross-asm-collision";
    public const string AnalysisCrossAsmShape = "analysis.cross-asm-shape";
    public const string AnalysisExceptionBase = "analysis.exception-base";
    public const string AnalysisFacade = "analysis.facade";
    public const string AnalysisLookalike = "analysis.lookalike";
    public const string AnalysisProtobuf = "analysis.protobuf";
    public const string AnalysisRender = "analysis.render";
    public const string AnalysisSpoofSystemLinq = "analysis.spoof.system-linq";
    public const string AnalysisSpoofSystemRuntime = "analysis.spoof.system-runtime";

    public const string DecompilerCheckedArithmetic = "decompiler.checked-arithmetic";
    public const string DecompilerClassicAsync = "decompiler.classic-async";
    public const string DecompilerLadderIterator = "decompiler.ladder.iterator";
    public const string DecompilerLadderRung1 = "decompiler.ladder.rung1";
    public const string DecompilerLadderRung2 = "decompiler.ladder.rung2";
    public const string DecompilerLadderRung3 = "decompiler.ladder.rung3";
    public const string DecompilerLadderRung4 = "decompiler.ladder.rung4";
    public const string DecompilerLadderRung5 = "decompiler.ladder.rung5";
    public const string DecompilerLadderRung9 = "decompiler.ladder.rung9";
    public const string DecompilerUnsafeLegacy = "decompiler.unsafe.legacy";
    public const string DecompilerUnsafeNew = "decompiler.unsafe.new";
    public const string DecompilerUnsafeChainA = "decompiler.unsafe.chain-a";
    public const string DecompilerUnsafeChainB = "decompiler.unsafe.chain-b";
    public const string DecompilerUnsafeChainC = "decompiler.unsafe.chain-c";

    public const string RunFasterAllocation = "runfaster.allocation";
}

public static class FixtureCatalog
{
    public static readonly FixtureDefinition DiffV1 = Fixture(
        FixtureIds.DiffV1,
        "DiffFixtures.V1",
        "DiffFixtureSample.dll",
        "diff", "version-pair", "analysis", "decompiler", "rts-candidate");

    public static readonly FixtureDefinition DiffV2 = Fixture(
        FixtureIds.DiffV2,
        "DiffFixtures.V2",
        "DiffFixtureSample.dll",
        "diff", "version-pair", "analysis", "decompiler", "rts-candidate");

    public static readonly FixtureDefinition DiffAsmCaller = Fixture(
        FixtureIds.DiffAsmCaller,
        "DiffAsmFixtures.Caller",
        "DiffAsmCaller.dll",
        "diff", "assembly-identity", "caller");

    public static readonly FixtureDefinition DiffAsmLibA = Fixture(
        FixtureIds.DiffAsmLibA,
        "DiffAsmFixtures.LibA",
        "DiffAsmLibA.dll",
        "diff", "assembly-identity", "same-fqn");

    public static readonly FixtureDefinition DiffAsmLibB = Fixture(
        FixtureIds.DiffAsmLibB,
        "DiffAsmFixtures.LibB",
        "DiffAsmLibB.dll",
        "diff", "assembly-identity", "same-fqn");

    public static readonly FixtureDefinition DiffAsmTarget = Fixture(
        FixtureIds.DiffAsmTarget,
        "DiffAsmFixtures.Target",
        "DiffAsmTarget.dll",
        "diff", "assembly-identity", "target");

    public static readonly FixtureDefinition AnalysisCallerGraphCaller = Fixture(
        FixtureIds.AnalysisCallerGraphCaller,
        "ILInspector.Analysis.CallerGraphCaller",
        "ILInspector.Analysis.CallerGraphCaller.dll",
        "analysis", "caller-graph", "caller");

    public static readonly FixtureDefinition AnalysisCallerGraphCallerTwin = Fixture(
        FixtureIds.AnalysisCallerGraphCallerTwin,
        "ILInspector.Analysis.CallerGraphCallerTwin",
        "ILInspector.Analysis.CallerGraphCallerTwin.dll",
        "analysis", "caller-graph", "caller", "twin");

    public static readonly FixtureDefinition AnalysisCallerGraphLookalikeCaller = Fixture(
        FixtureIds.AnalysisCallerGraphLookalikeCaller,
        "ILInspector.Analysis.CallerGraphLookalikeCaller",
        "ILInspector.Analysis.CallerGraphLookalikeCaller.dll",
        "analysis", "caller-graph", "lookalike");

    public static readonly FixtureDefinition AnalysisCallerGraphTarget = Fixture(
        FixtureIds.AnalysisCallerGraphTarget,
        "ILInspector.Analysis.CallerGraphTarget",
        "ILInspector.Analysis.CallerGraphTarget.dll",
        "analysis", "caller-graph", "target");

    public static readonly FixtureDefinition AnalysisCrossAsmCollision = Fixture(
        FixtureIds.AnalysisCrossAsmCollision,
        "ILInspector.Analysis.CrossAsmCollisionFixtures",
        "ILInspector.Analysis.CrossAsmCollisionFixtures.dll",
        "analysis", "cross-assembly", "collision");

    public static readonly FixtureDefinition AnalysisCrossAsmShape = Fixture(
        FixtureIds.AnalysisCrossAsmShape,
        "ILInspector.Analysis.CrossAsmShapeFixtures",
        "ILInspector.Analysis.CrossAsmShapeFixtures.dll",
        "analysis", "cross-assembly", "shape");

    public static readonly FixtureDefinition AnalysisExceptionBase = Fixture(
        FixtureIds.AnalysisExceptionBase,
        "ILInspector.Analysis.ExceptionBaseFixtures",
        "ILInspector.Analysis.ExceptionBaseFixtures.dll",
        "analysis", "exception");

    public static readonly FixtureDefinition AnalysisFacade = Fixture(
        FixtureIds.AnalysisFacade,
        "ILInspector.Analysis.FacadeFixtures",
        "ILInspector.Analysis.FacadeFixtures.dll",
        "analysis", "facade", "netstandard");

    public static readonly FixtureDefinition AnalysisLookalike = Fixture(
        FixtureIds.AnalysisLookalike,
        "ILInspector.Analysis.LookalikeFixtures",
        "ILInspector.Analysis.LookalikeFixtures.dll",
        "analysis", "lookalike");

    public static readonly FixtureDefinition AnalysisProtobuf = Fixture(
        FixtureIds.AnalysisProtobuf,
        "ILInspector.Analysis.ProtobufFixtures",
        "Google.Protobuf.dll",
        "analysis", "protobuf", "assembly-name-axis");

    public static readonly FixtureDefinition AnalysisRender = Fixture(
        FixtureIds.AnalysisRender,
        "ILInspector.Analysis.RenderFixtures",
        "ILInspector.Analysis.RenderFixtures.dll",
        "analysis", "render");

    public static readonly FixtureDefinition AnalysisSpoofSystemLinq = Fixture(
        FixtureIds.AnalysisSpoofSystemLinq,
        "ILInspector.Analysis.SpoofFixtures",
        "System.Linq.dll",
        "analysis", "spoof", "assembly-name-axis", "system-linq");

    public static readonly FixtureDefinition AnalysisSpoofSystemRuntime = Fixture(
        FixtureIds.AnalysisSpoofSystemRuntime,
        "ILInspector.Analysis.SpoofRuntimeFixtures",
        "System.Runtime.dll",
        "analysis", "spoof", "assembly-name-axis", "system-runtime");

    public static readonly FixtureDefinition DecompilerCheckedArithmetic = Fixture(
        FixtureIds.DecompilerCheckedArithmetic,
        "ILInspector.Decompiler.Fixtures.CheckedArithmetic",
        "ILInspector.Decompiler.Fixtures.CheckedArithmetic.dll",
        "decompiler", "checked-arithmetic", "compiler-axis");

    public static readonly FixtureDefinition DecompilerClassicAsync = Fixture(
        FixtureIds.DecompilerClassicAsync,
        "ILInspector.Decompiler.Fixtures.ClassicAsync",
        "ILInspector.Decompiler.Fixtures.ClassicAsync.dll",
        "decompiler", "async", "classic-async", "compiler-axis", "rts-candidate");

    public static readonly FixtureDefinition DecompilerLadderIterator = Fixture(
        FixtureIds.DecompilerLadderIterator,
        "ILInspector.Decompiler.Fixtures.LadderIterator",
        "ILInspector.Decompiler.Fixtures.LadderIterator.dll",
        "decompiler", "ladder", "iterator");

    public static readonly FixtureDefinition DecompilerLadderRung1 = Fixture(
        FixtureIds.DecompilerLadderRung1,
        "ILInspector.Decompiler.Fixtures.LadderRung1",
        "ILInspector.Decompiler.Fixtures.LadderRung1.dll",
        "decompiler", "ladder", "rung1");

    public static readonly FixtureDefinition DecompilerLadderRung2 = Fixture(
        FixtureIds.DecompilerLadderRung2,
        "ILInspector.Decompiler.Fixtures.LadderRung2",
        "ILInspector.Decompiler.Fixtures.LadderRung2.dll",
        "decompiler", "ladder", "rung2");

    public static readonly FixtureDefinition DecompilerLadderRung3 = Fixture(
        FixtureIds.DecompilerLadderRung3,
        "ILInspector.Decompiler.Fixtures.LadderRung3",
        "ILInspector.Decompiler.Fixtures.LadderRung3.dll",
        "decompiler", "ladder", "rung3");

    public static readonly FixtureDefinition DecompilerLadderRung4 = Fixture(
        FixtureIds.DecompilerLadderRung4,
        "ILInspector.Decompiler.Fixtures.LadderRung4",
        "ILInspector.Decompiler.Fixtures.LadderRung4.dll",
        "decompiler", "ladder", "rung4");

    public static readonly FixtureDefinition DecompilerLadderRung5 = Fixture(
        FixtureIds.DecompilerLadderRung5,
        "ILInspector.Decompiler.Fixtures.LadderRung5",
        "ILInspector.Decompiler.Fixtures.LadderRung5.dll",
        "decompiler", "ladder", "rung5");

    public static readonly FixtureDefinition DecompilerLadderRung9 = Fixture(
        FixtureIds.DecompilerLadderRung9,
        "ILInspector.Decompiler.Fixtures.LadderRung9",
        "ILInspector.Decompiler.Fixtures.LadderRung9.dll",
        "decompiler", "ladder", "rung9");

    public static readonly FixtureDefinition DecompilerUnsafeLegacy = Fixture(
        FixtureIds.DecompilerUnsafeLegacy,
        "ILInspector.Decompiler.Fixtures.LegacyUnsafe",
        "ILInspector.Decompiler.Fixtures.LegacyUnsafe.dll",
        "decompiler", "unsafe", "legacy-memory-safety");

    public static readonly FixtureDefinition DecompilerUnsafeNew = Fixture(
        FixtureIds.DecompilerUnsafeNew,
        "ILInspector.Decompiler.Fixtures.NewUnsafe",
        "ILInspector.Decompiler.Fixtures.NewUnsafe.dll",
        "decompiler", "unsafe", "updated-memory-safety");

    public static readonly FixtureDefinition DecompilerUnsafeChainA = Fixture(
        FixtureIds.DecompilerUnsafeChainA,
        "ILInspector.Decompiler.Fixtures.UnsafeChainA",
        "ILInspector.Decompiler.Fixtures.UnsafeChainA.dll",
        "decompiler", "unsafe", "chain", "updated-memory-safety");

    public static readonly FixtureDefinition DecompilerUnsafeChainB = Fixture(
        FixtureIds.DecompilerUnsafeChainB,
        "ILInspector.Decompiler.Fixtures.UnsafeChainB",
        "ILInspector.Decompiler.Fixtures.UnsafeChainB.dll",
        "decompiler", "unsafe", "chain", "updated-memory-safety");

    public static readonly FixtureDefinition DecompilerUnsafeChainC = Fixture(
        FixtureIds.DecompilerUnsafeChainC,
        "ILInspector.Decompiler.Fixtures.UnsafeChainC",
        "ILInspector.Decompiler.Fixtures.UnsafeChainC.dll",
        "decompiler", "unsafe", "chain", "legacy-memory-safety", "executable");

    public static readonly FixtureDefinition RunFasterAllocation = Fixture(
        FixtureIds.RunFasterAllocation,
        "RunFaster.AllocationFixture",
        "RunFaster.AllocationFixture.dll",
        "runfaster", "allocation", "trace-coupled");

    public static readonly IReadOnlyList<FixtureDefinition> All =
    [
        DiffV1,
        DiffV2,
        DiffAsmCaller,
        DiffAsmLibA,
        DiffAsmLibB,
        DiffAsmTarget,
        AnalysisCallerGraphCaller,
        AnalysisCallerGraphCallerTwin,
        AnalysisCallerGraphLookalikeCaller,
        AnalysisCallerGraphTarget,
        AnalysisCrossAsmCollision,
        AnalysisCrossAsmShape,
        AnalysisExceptionBase,
        AnalysisFacade,
        AnalysisLookalike,
        AnalysisProtobuf,
        AnalysisRender,
        AnalysisSpoofSystemLinq,
        AnalysisSpoofSystemRuntime,
        DecompilerCheckedArithmetic,
        DecompilerClassicAsync,
        DecompilerLadderIterator,
        DecompilerLadderRung1,
        DecompilerLadderRung2,
        DecompilerLadderRung3,
        DecompilerLadderRung4,
        DecompilerLadderRung5,
        DecompilerLadderRung9,
        DecompilerUnsafeLegacy,
        DecompilerUnsafeNew,
        DecompilerUnsafeChainA,
        DecompilerUnsafeChainB,
        DecompilerUnsafeChainC,
        RunFasterAllocation,
    ];

    public static readonly FixturePair DiffPair = new("diff", DiffV1, DiffV2);

    public static readonly FixtureGroup DiffAssemblyFixtures = new(
        "diff-asm",
        [DiffAsmTarget, DiffAsmCaller, DiffAsmLibA, DiffAsmLibB]);

    public static readonly FixtureGroup AnalysisFixtures = new(
        "analysis",
        [
            AnalysisCallerGraphTarget,
            AnalysisCallerGraphCaller,
            AnalysisCallerGraphCallerTwin,
            AnalysisCallerGraphLookalikeCaller,
            AnalysisCrossAsmCollision,
            AnalysisCrossAsmShape,
            AnalysisExceptionBase,
            AnalysisFacade,
            AnalysisLookalike,
            AnalysisProtobuf,
            AnalysisRender,
            AnalysisSpoofSystemLinq,
            AnalysisSpoofSystemRuntime,
        ]);

    public static readonly FixtureGroup DecompilerFixtures = new(
        "decompiler",
        [
            DecompilerCheckedArithmetic,
            DecompilerClassicAsync,
            DecompilerLadderIterator,
            DecompilerLadderRung1,
            DecompilerLadderRung2,
            DecompilerLadderRung3,
            DecompilerLadderRung4,
            DecompilerLadderRung5,
            DecompilerLadderRung9,
            DecompilerUnsafeLegacy,
            DecompilerUnsafeNew,
            DecompilerUnsafeChainA,
            DecompilerUnsafeChainB,
            DecompilerUnsafeChainC,
        ]);

    public static readonly FixtureGroup DecompilerLadderFixtures = new(
        "decompiler.ladder",
        [
            DecompilerLadderRung1,
            DecompilerLadderRung2,
            DecompilerLadderRung3,
            DecompilerLadderRung4,
            DecompilerLadderRung5,
            DecompilerLadderRung9,
            DecompilerLadderIterator,
        ]);

    public static readonly FixtureGroup DecompilerUnsafeFixtures = new(
        "decompiler.unsafe",
        [
            DecompilerUnsafeLegacy,
            DecompilerUnsafeNew,
            DecompilerUnsafeChainA,
            DecompilerUnsafeChainB,
            DecompilerUnsafeChainC,
        ]);

    public static readonly FixtureGroup RunFasterFixtures = new(
        "runfaster",
        [RunFasterAllocation]);

    public static readonly FixtureGroup ReturnToSenderCandidates = new(
        "rts.candidates",
        [DiffV1, DiffV2, DecompilerClassicAsync]);

    static readonly Dictionary<string, FixtureDefinition> s_byId =
        All.ToDictionary(fixture => fixture.Id, StringComparer.Ordinal);

    public static FixtureDefinition Get(string id)
        => s_byId.TryGetValue(id, out var fixture)
            ? fixture
            : throw new ArgumentException($"Unknown fixture id '{id}'.", nameof(id));

    public static string AssemblyPath(string id)
    {
        var fixture = Get(id);
        string configuration = CurrentConfiguration();
        string root = RepositoryRoot();
        string path = Path.Combine(
            root,
            "artifacts",
            "bin",
            fixture.ProjectName,
            configuration,
            fixture.AssemblyFileName);

        if (File.Exists(path))
            return path;

        throw new FileNotFoundException(
            $"Expected built fixture '{fixture.Id}' at {path}. Run 'dotnet build dotnet-inspect.slnx -c Release' before running harnesses or tests that consume fixture binaries.",
            path);
    }

    static FixtureDefinition Fixture(string id, string projectName, string assemblyFileName, params string[] tags)
        => new(id, projectName, assemblyFileName, tags);

    static string CurrentConfiguration()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (IsConfigurationDirectory(directory.Name))
                return directory.Name.ToLowerInvariant();
        }

        throw new InvalidOperationException(
            $"Could not infer build configuration from '{AppContext.BaseDirectory}'. Run tests from a built dotnet-inspect checkout.");
    }

    static bool IsConfigurationDirectory(string name)
        => string.Equals(name, "debug", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "release", StringComparison.OrdinalIgnoreCase);

    static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'. Run tests from a dotnet-inspect checkout after building 'dotnet-inspect.slnx'.");
    }
}
