using System.Xml.Linq;

namespace DotnetInspector.Fixtures;

public sealed record FixtureDefinition(
    string Id,
    string ProjectName,
    string RepositoryProjectDirectory,
    string AssemblyFileName,
    IReadOnlyList<string> Tags,
    IReadOnlyList<FixtureBoundary> Boundaries,
    IReadOnlyList<FixtureAsset> Assets)
{
    public string AssemblyPath() => FixtureCatalog.AssemblyPath(Id);
    public string ProjectDirectory() => FixtureCatalog.ProjectDirectory(Id);
    public IReadOnlyList<string> SourcePaths() => FixtureCatalog.SourcePaths(Id);
    public string AssetPath(string name) => FixtureCatalog.AssetPath(Id, name);
}

public enum FixtureBoundary
{
    AssemblyIdentity,
    AssemblyName,
    CompilerLowering,
    CrossAssemblyBoundary,
    ExternAlias,
    FrameworkReference,
    ModuleAttribute,
    OutputKind,
    PostBuildTransformation,
    SidecarAsset,
    TargetFramework,
    UntrustedText,
    VersionPair,
    SourceLinkMap,
}

public sealed record FixtureAsset(string Name, string ProjectName, string RelativePath);

public sealed record FixturePair(string Id, FixtureDefinition Old, FixtureDefinition New)
{
    public string OldAssemblyPath() => Old.AssemblyPath();
    public string NewAssemblyPath() => New.AssemblyPath();
}

public sealed record FixtureGroup(string Id, IReadOnlyList<FixtureDefinition> Fixtures);
public static class FixtureGroupExtensions
{
    public static IReadOnlyList<string> AssemblyPaths(this FixtureGroup group)
        => [.. group.Fixtures.Select(fixture => fixture.AssemblyPath())];
}

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
    public const string AnalysisCallerGraphIndirectCaller = "analysis.caller-graph.indirect-caller";
    public const string AnalysisCallerGraphLookalikeCaller = "analysis.caller-graph.lookalike-caller";
    public const string AnalysisCallerGraphTarget = "analysis.caller-graph.target";
    public const string AnalysisCallerGraphTargetV2 = "analysis.caller-graph.target-v2";
    public const string AnalysisAsyncSiblingFriend = "analysis.async-sibling.friend";
    public const string AnalysisCallerLoop = "analysis.caller-loop";
    public const string AnalysisCrossAsmCollision = "analysis.cross-asm-collision";
    public const string AnalysisCrossAsmShape = "analysis.cross-asm-shape";
    public const string AnalysisExceptionBase = "analysis.exception-base";
    public const string AnalysisFacade = "analysis.facade";
    public const string AnalysisLookalike = "analysis.lookalike";
    public const string AnalysisMethodCorrespondenceRuntime =
        "analysis.method-correspondence.runtime";
    public const string AnalysisMethodCorrespondenceSurface =
        "analysis.method-correspondence.surface";
    public const string AnalysisOwnershipFlow = "analysis.ownership-flow";
    public const string AnalysisTopLevelAsync = "analysis.top-level-async";
    public const string AnalysisTopLevelClassicAsync = "analysis.top-level-classic-async";
    public const string AnalysisProtobuf = "analysis.protobuf";
    public const string AnalysisRender = "analysis.render";
    public const string AnalysisSpoofSystemLinq = "analysis.spoof.system-linq";
    public const string AnalysisSpoofSystemRuntime = "analysis.spoof.system-runtime";

    public const string DecompilerCheckedArithmetic = "decompiler.checked-arithmetic";
    public const string DecompilerClassicAsync = "decompiler.classic-async";
    public const string DecompilerClassicAsyncArtifacts =
        "decompiler.classic-async-artifacts";
    public const string DecompilerRuntimeAsync = "decompiler.runtime-async";
    public const string DecompilerExpressionTreeSpoof = "decompiler.expression-tree-spoof";
    public const string DecompilerClassicStateMachines = "decompiler.classic-state-machines";
    public const string DecompilerLadderIterator = "decompiler.ladder.iterator";
    public const string DecompilerLadderRung1 = "decompiler.ladder.rung1";
    public const string DecompilerLadderRung2 = "decompiler.ladder.rung2";
    public const string DecompilerLadderRung3 = "decompiler.ladder.rung3";
    public const string DecompilerLadderRung4 = "decompiler.ladder.rung4";
    public const string DecompilerLadderRung5 = "decompiler.ladder.rung5";
    public const string DecompilerLadderRung9 = "decompiler.ladder.rung9";
    public const string DecompilerTypeIdentity = "decompiler.type-identity";
    public const string DecompilerUnsafeLegacy = "decompiler.unsafe.legacy";
    public const string DecompilerUnsafeNew = "decompiler.unsafe.new";
    public const string DecompilerUnsafeChainA = "decompiler.unsafe.chain-a";
    public const string DecompilerUnsafeChainB = "decompiler.unsafe.chain-b";
    public const string DecompilerUnsafeChainC = "decompiler.unsafe.chain-c";
    public const string DecompilerVbFinalizer = "decompiler.vb-finalizer";

    public const string HostileLiterals = "hostile.literals";
    public const string SourceLinkMalformed = "sourcelink.malformed";
    public const string SourceLinkNormalized = "sourcelink.normalized";

    public const string ResearchTargetSample = "research.target-sample";
    public const string ResearchTargetCorrespondenceV1 =
        "research.target-correspondence.v1";
    public const string ResearchTargetCorrespondenceV2 =
        "research.target-correspondence.v2";

    public const string RunFasterAllocation = "runfaster.allocation";

    public const string RestoredProjectDependencyFacts = "restored-project.dependency-facts";

    public const string ServicesRouteLearningBase =
        "services.route-learning.base";
    public const string ServicesRouteLearningContract =
        "services.route-learning.contract";
    public const string ServicesRouteLearningMiddle =
        "services.route-learning.middle";
    public const string ServicesRouteLearningConsumer =
        "services.route-learning.consumer";
    public const string ServicesRouteLearningUnrelated =
        "services.route-learning.unrelated";
}

public static class FixtureCatalog
{
    /// <summary>
    /// Attacker-controlled text inside C# string literals — a parameter default
    /// value, an [Obsolete] message, and a custom attribute argument, each
    /// carrying a bidi override and a vertical tab (issue #3319). Must be
    /// compiler-produced: an emitted attribute message blob does not decode back.
    /// </summary>
    /// <remarks>
    /// The project boundary is load-bearing, which is why this is not folded
    /// into a shared fixture project. Its assembly-level attributes carry bidi
    /// overrides, so every consumer of a shared project would inherit hostile
    /// Company/Product/Copyright text in its expected output; its build
    /// deliberately disables SourceLink and determinism to plant a hostile
    /// SourceLink map in the PDB; and MSBuild's default globs cannot even walk a
    /// directory whose name holds a control character. Hostile input has to be
    /// quarantined at the project boundary to stay hostile.
    /// </remarks>
    public static readonly FixtureDefinition HostileLiterals = Fixture(
        FixtureIds.HostileLiterals,
        "DotnetInspector.HostileNameFixtures",
        "DotnetInspector.HostileNameFixtures.dll",
        Boundaries(FixtureBoundary.UntrustedText),
        "presentation", "untrusted-input");

    public static readonly FixtureDefinition SourceLinkMalformed = Fixture(
        FixtureIds.SourceLinkMalformed,
        "DotnetInspector.SourceLinkMalformedFixtures",
        "DotnetInspector.SourceLinkMalformedFixtures.dll",
        Boundaries(FixtureBoundary.SourceLinkMap),
        "sourcelink", "malformed-map");

    public static readonly FixtureDefinition SourceLinkNormalized = Fixture(
        FixtureIds.SourceLinkNormalized,
        "DotnetInspector.SourceLinkNormalizedFixtures",
        "DotnetInspector.SourceLinkNormalizedFixtures.dll",
        Boundaries(FixtureBoundary.SourceLinkMap),
        "sourcelink", "normalized-map");

    public static readonly FixtureDefinition DiffV1 = Fixture(
        FixtureIds.DiffV1,
        "DiffFixtures.V1",
        "DiffFixtureSample.dll",
        Boundaries(FixtureBoundary.VersionPair),
        "diff", "version-pair", "analysis", "decompiler", "rts-candidate");

    public static readonly FixtureDefinition DiffV2 = Fixture(
        FixtureIds.DiffV2,
        "DiffFixtures.V2",
        "DiffFixtureSample.dll",
        Boundaries(FixtureBoundary.VersionPair),
        "diff", "version-pair", "analysis", "decompiler", "rts-candidate");

    /// <summary>
    /// Purpose-built member shapes for Research target requests (#5049):
    /// compiler-produced accessor tokens, an ambiguous overload pair, a
    /// bodyless field, a nested declaring type, and a real type-forwarder row.
    /// </summary>
    public static readonly FixtureDefinition ResearchTargetSample = Fixture(
        FixtureIds.ResearchTargetSample,
        "ILInspector.Research.TargetFixtures",
        "ILInspector.Research.TargetFixtures.dll",
        Boundaries(FixtureBoundary.CompilerLowering),
        "research", "target", "accessor-role", "type-forwarder");

    public static readonly FixtureDefinition ResearchTargetCorrespondenceV1 =
        Fixture(
            FixtureIds.ResearchTargetCorrespondenceV1,
            "ResearchTargetCorrespondenceFixtures.V1",
            "ResearchTargetCorrespondenceFixtures.dll",
            Boundaries(FixtureBoundary.VersionPair),
            "research", "target-correspondence", "version-pair");

    public static readonly FixtureDefinition ResearchTargetCorrespondenceV2 =
        Fixture(
            FixtureIds.ResearchTargetCorrespondenceV2,
            "ResearchTargetCorrespondenceFixtures.V2",
            "ResearchTargetCorrespondenceFixtures.dll",
            Boundaries(FixtureBoundary.VersionPair),
            "research", "target-correspondence", "version-pair");

    public static readonly FixtureDefinition DiffAsmCaller = Fixture(
        FixtureIds.DiffAsmCaller,
        "DiffAsmFixtures.Caller",
        "DiffAsmCaller.dll",
        Boundaries(FixtureBoundary.AssemblyIdentity),
        "diff", "assembly-identity", "caller");

    public static readonly FixtureDefinition DiffAsmLibA = Fixture(
        FixtureIds.DiffAsmLibA,
        "DiffAsmFixtures.LibA",
        "DiffAsmLibA.dll",
        Boundaries(FixtureBoundary.AssemblyIdentity),
        "diff", "assembly-identity", "same-fqn");

    public static readonly FixtureDefinition DiffAsmLibB = Fixture(
        FixtureIds.DiffAsmLibB,
        "DiffAsmFixtures.LibB",
        "DiffAsmLibB.dll",
        Boundaries(FixtureBoundary.AssemblyIdentity),
        "diff", "assembly-identity", "same-fqn");

    public static readonly FixtureDefinition DiffAsmTarget = Fixture(
        FixtureIds.DiffAsmTarget,
        "DiffAsmFixtures.Target",
        "DiffAsmTarget.dll",
        Boundaries(FixtureBoundary.AssemblyIdentity),
        "diff", "assembly-identity", "target");

    public static readonly FixtureDefinition AnalysisCallerGraphCaller = Fixture(
        FixtureIds.AnalysisCallerGraphCaller,
        "ILInspector.Analysis.CallerGraphCaller",
        "ILInspector.Analysis.CallerGraphCaller.dll",
        Boundaries(FixtureBoundary.CrossAssemblyBoundary),
        "analysis", "caller-graph", "caller");

    public static readonly FixtureDefinition AnalysisOwnershipFlow = Fixture(
        FixtureIds.AnalysisOwnershipFlow,
        "ILInspector.Analysis.OwnershipFlowFixtures",
        "ILInspector.Analysis.OwnershipFlowFixtures.dll",
        Boundaries(FixtureBoundary.CompilerLowering),
        "analysis", "ownership-flow");

    public static readonly FixtureDefinition AnalysisTopLevelAsync = Fixture(
        FixtureIds.AnalysisTopLevelAsync,
        "ILInspector.Analysis.TopLevelAsyncFixtures",
        "ILInspector.Analysis.TopLevelAsyncFixtures.dll",
        Boundaries(FixtureBoundary.CompilerLowering, FixtureBoundary.OutputKind),
        "analysis", "top-level", "async");

    public static readonly FixtureDefinition AnalysisTopLevelClassicAsync = Fixture(
        FixtureIds.AnalysisTopLevelClassicAsync,
        "ILInspector.Analysis.TopLevelClassicAsyncFixtures",
        "ILInspector.Analysis.TopLevelClassicAsyncFixtures.dll",
        Boundaries(FixtureBoundary.CompilerLowering, FixtureBoundary.OutputKind),
        "analysis", "top-level", "async", "classic-async");

    public static readonly FixtureDefinition AnalysisCallerGraphCallerTwin = Fixture(
        FixtureIds.AnalysisCallerGraphCallerTwin,
        "ILInspector.Analysis.CallerGraphCallerTwin",
        "ILInspector.Analysis.CallerGraphCallerTwin.dll",
        Boundaries(FixtureBoundary.CrossAssemblyBoundary),
        "analysis", "caller-graph", "caller", "twin");

    public static readonly FixtureDefinition AnalysisCallerGraphIndirectCaller = Fixture(
        FixtureIds.AnalysisCallerGraphIndirectCaller,
        "ILInspector.Analysis.CallerGraphIndirectCaller",
        "ILInspector.Analysis.CallerGraphIndirectCaller.dll",
        Boundaries(FixtureBoundary.CrossAssemblyBoundary),
        "analysis", "caller-graph", "indirect");

    public static readonly FixtureDefinition AnalysisCallerGraphLookalikeCaller = Fixture(
        FixtureIds.AnalysisCallerGraphLookalikeCaller,
        "ILInspector.Analysis.CallerGraphLookalikeCaller",
        "ILInspector.Analysis.CallerGraphLookalikeCaller.dll",
        Boundaries(FixtureBoundary.CrossAssemblyBoundary),
        "analysis", "caller-graph", "lookalike");

    public static readonly FixtureDefinition AnalysisCallerGraphTarget = Fixture(
        FixtureIds.AnalysisCallerGraphTarget,
        "ILInspector.Analysis.CallerGraphTarget",
        "ILInspector.Analysis.CallerGraphTarget.dll",
        Boundaries(FixtureBoundary.CrossAssemblyBoundary),
        "analysis", "caller-graph", "target");

    public static readonly FixtureDefinition AnalysisCallerGraphTargetV2 = Fixture(
        FixtureIds.AnalysisCallerGraphTargetV2,
        "ILInspector.Analysis.CallerGraphTargetV2",
        "ILInspector.Analysis.CallerGraphTarget.dll",
        Boundaries(FixtureBoundary.AssemblyIdentity, FixtureBoundary.CrossAssemblyBoundary),
        "analysis", "caller-graph", "target", "version-skew");

    public static readonly FixtureDefinition AnalysisCallerLoop = Fixture(
        FixtureIds.AnalysisCallerLoop,
        "ILInspector.Analysis.Fixtures",
        "ILInspector.Analysis.Fixtures.dll",
        "analysis", "caller-loop", "allocation");

    public static readonly FixtureDefinition
        AnalysisMethodCorrespondenceRuntime = Fixture(
            FixtureIds.AnalysisMethodCorrespondenceRuntime,
            "ILInspector.Analysis.MethodCorrespondenceRuntimeFixtures",
            "ILInspector.Analysis.MethodCorrespondenceFixture.dll",
            Boundaries(
                FixtureBoundary.AssemblyIdentity,
                FixtureBoundary.VersionPair),
            "analysis", "method-correspondence", "runtime");

    public static readonly FixtureDefinition
        AnalysisMethodCorrespondenceSurface = Fixture(
            FixtureIds.AnalysisMethodCorrespondenceSurface,
            "ILInspector.Analysis.MethodCorrespondenceSurfaceFixtures",
            "ILInspector.Analysis.MethodCorrespondenceFixture.dll",
            Boundaries(
                FixtureBoundary.AssemblyIdentity,
                FixtureBoundary.VersionPair),
            "analysis", "method-correspondence", "surface");

    public static readonly FixtureDefinition AnalysisAsyncSiblingFriend = Fixture(
        FixtureIds.AnalysisAsyncSiblingFriend,
        "ILInspector.Analysis.AsyncSiblingFriendFixtures",
        "ILInspector.Analysis.AsyncSiblingFriendFixtures.dll",
        Boundaries(
            FixtureBoundary.AssemblyIdentity,
            FixtureBoundary.CrossAssemblyBoundary),
        "analysis", "async-sibling", "friend-assembly");

    public static readonly FixtureDefinition AnalysisCrossAsmCollision = Fixture(
        FixtureIds.AnalysisCrossAsmCollision,
        "ILInspector.Analysis.CrossAsmCollisionFixtures",
        "ILInspector.Analysis.CrossAsmCollisionFixtures.dll",
        Boundaries(FixtureBoundary.AssemblyIdentity, FixtureBoundary.ExternAlias),
        "analysis", "cross-assembly", "collision");

    public static readonly FixtureDefinition AnalysisCrossAsmShape = Fixture(
        FixtureIds.AnalysisCrossAsmShape,
        "ILInspector.Analysis.Fixtures",
        "ILInspector.Analysis.Fixtures.dll",
        "analysis", "cross-assembly", "shape");

    public static readonly FixtureDefinition AnalysisExceptionBase = Fixture(
        FixtureIds.AnalysisExceptionBase,
        "ILInspector.Analysis.Fixtures",
        "ILInspector.Analysis.Fixtures.dll",
        "analysis", "exception");

    public static readonly FixtureDefinition AnalysisFacade = Fixture(
        FixtureIds.AnalysisFacade,
        "ILInspector.Analysis.FacadeFixtures",
        "ILInspector.Analysis.FacadeFixtures.dll",
        Boundaries(FixtureBoundary.TargetFramework),
        "analysis", "facade", "netstandard");

    public static readonly FixtureDefinition AnalysisLookalike = Fixture(
        FixtureIds.AnalysisLookalike,
        "ILInspector.Analysis.LookalikeFixtures",
        "ILInspector.Analysis.LookalikeFixtures.dll",
        Boundaries(FixtureBoundary.AssemblyIdentity),
        "analysis", "lookalike");

    public static readonly FixtureDefinition AnalysisProtobuf = Fixture(
        FixtureIds.AnalysisProtobuf,
        "ILInspector.Analysis.ProtobufFixtures",
        "Google.Protobuf.dll",
        Boundaries(FixtureBoundary.AssemblyName),
        "analysis", "protobuf", "assembly-name-axis");

    public static readonly FixtureDefinition AnalysisRender = Fixture(
        FixtureIds.AnalysisRender,
        "ILInspector.Analysis.RenderFixtures",
        "ILInspector.Analysis.RenderFixtures.dll",
        Boundaries(FixtureBoundary.FrameworkReference),
        "analysis", "render");

    public static readonly FixtureDefinition AnalysisSpoofSystemLinq = Fixture(
        FixtureIds.AnalysisSpoofSystemLinq,
        "ILInspector.Analysis.SpoofFixtures",
        "System.Linq.dll",
        Boundaries(FixtureBoundary.AssemblyName),
        "analysis", "spoof", "assembly-name-axis", "system-linq");

    public static readonly FixtureDefinition AnalysisSpoofSystemRuntime = Fixture(
        FixtureIds.AnalysisSpoofSystemRuntime,
        "ILInspector.Analysis.SpoofRuntimeFixtures",
        "System.Runtime.dll",
        Boundaries(FixtureBoundary.AssemblyName),
        "analysis", "spoof", "assembly-name-axis", "system-runtime");

    public static readonly FixtureDefinition DecompilerCheckedArithmetic = Fixture(
        FixtureIds.DecompilerCheckedArithmetic,
        "ILInspector.Decompiler.Fixtures.CheckedArithmetic",
        "ILInspector.Decompiler.Fixtures.CheckedArithmetic.dll",
        Boundaries(FixtureBoundary.CompilerLowering),
        "decompiler", "checked-arithmetic", "compiler-axis");

    public static readonly FixtureDefinition DecompilerTypeIdentity = Fixture(
        FixtureIds.DecompilerTypeIdentity,
        "ILInspector.Decompiler.Fixtures.TypeIdentity",
        "ILInspector.Decompiler.Fixtures.TypeIdentity.dll",
        Boundaries(FixtureBoundary.CompilerLowering),
        "decompiler", "type-identity", "compiler-axis");

    public static readonly FixtureDefinition DecompilerClassicAsync = Fixture(
        FixtureIds.DecompilerClassicAsync,
        "ILInspector.Decompiler.Fixtures.ClassicAsync",
        "ILInspector.Decompiler.Fixtures.ClassicAsync.dll",
        Boundaries(FixtureBoundary.CompilerLowering),
        "decompiler", "async", "classic-async", "compiler-axis", "rts-candidate");

    public static readonly FixtureDefinition DecompilerClassicAsyncArtifacts =
        Fixture(
            FixtureIds.DecompilerClassicAsyncArtifacts,
            "ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts",
            "ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts.dll",
            Boundaries(
                FixtureBoundary.CompilerLowering,
                FixtureBoundary.OutputKind,
                FixtureBoundary.PostBuildTransformation),
            "decompiler",
            "async",
            "classic-async",
            "artifact-matrix",
            "compiler-axis");

    public static readonly FixtureDefinition DecompilerRuntimeAsync = Fixture(
        FixtureIds.DecompilerRuntimeAsync,
        "ILInspector.Decompiler.Fixtures.RuntimeAsync",
        "ILInspector.Decompiler.Fixtures.RuntimeAsync.dll",
        Boundaries(FixtureBoundary.CompilerLowering),
        "decompiler", "async", "runtime-async", "compiler-axis");

    public static readonly FixtureDefinition DecompilerExpressionTreeSpoof = Fixture(
        FixtureIds.DecompilerExpressionTreeSpoof,
        "ILInspector.Decompiler.Fixtures.ExpressionTreeSpoof",
        "System.Linq.Expressions.dll",
        Boundaries(FixtureBoundary.AssemblyName),
        "decompiler", "expression-tree", "spoof", "assembly-name-axis", "system-linq-expressions");

    public static readonly FixtureDefinition DecompilerClassicStateMachines = Fixture(
        FixtureIds.DecompilerClassicStateMachines,
        "ILInspector.Decompiler.Fixtures.ClassicStateMachines",
        "ILInspector.Decompiler.Fixtures.ClassicStateMachines.dll",
        Boundaries(FixtureBoundary.CompilerLowering),
        "decompiler", "async", "iterator", "classic-state-machines", "compiler-axis");

    public static readonly FixtureDefinition DecompilerLadderIterator = Fixture(
        FixtureIds.DecompilerLadderIterator,
        "ILInspector.Decompiler.Fixtures.Ladder",
        "ILInspector.Decompiler.Fixtures.Ladder.dll",
        "decompiler", "ladder", "iterator");

    public static readonly FixtureDefinition DecompilerLadderRung1 = Fixture(
        FixtureIds.DecompilerLadderRung1,
        "ILInspector.Decompiler.Fixtures.Ladder",
        "ILInspector.Decompiler.Fixtures.Ladder.dll",
        "decompiler", "ladder", "rung1");

    public static readonly FixtureDefinition DecompilerLadderRung2 = Fixture(
        FixtureIds.DecompilerLadderRung2,
        "ILInspector.Decompiler.Fixtures.Ladder",
        "ILInspector.Decompiler.Fixtures.Ladder.dll",
        "decompiler", "ladder", "rung2");

    public static readonly FixtureDefinition DecompilerLadderRung3 = Fixture(
        FixtureIds.DecompilerLadderRung3,
        "ILInspector.Decompiler.Fixtures.Ladder",
        "ILInspector.Decompiler.Fixtures.Ladder.dll",
        "decompiler", "ladder", "rung3");

    public static readonly FixtureDefinition DecompilerLadderRung4 = Fixture(
        FixtureIds.DecompilerLadderRung4,
        "ILInspector.Decompiler.Fixtures.Ladder",
        "ILInspector.Decompiler.Fixtures.Ladder.dll",
        "decompiler", "ladder", "rung4");

    public static readonly FixtureDefinition DecompilerLadderRung5 = Fixture(
        FixtureIds.DecompilerLadderRung5,
        "ILInspector.Decompiler.Fixtures.Ladder",
        "ILInspector.Decompiler.Fixtures.Ladder.dll",
        "decompiler", "ladder", "rung5");

    public static readonly FixtureDefinition DecompilerLadderRung9 = Fixture(
        FixtureIds.DecompilerLadderRung9,
        "ILInspector.Decompiler.Fixtures.Ladder",
        "ILInspector.Decompiler.Fixtures.Ladder.dll",
        "decompiler", "ladder", "rung9");

    public static readonly FixtureDefinition DecompilerUnsafeLegacy = Fixture(
        FixtureIds.DecompilerUnsafeLegacy,
        "ILInspector.Decompiler.Fixtures.LegacyUnsafe",
        "ILInspector.Decompiler.Fixtures.LegacyUnsafe.dll",
        Boundaries(FixtureBoundary.ModuleAttribute),
        "decompiler", "unsafe", "legacy-memory-safety");

    public static readonly FixtureDefinition DecompilerUnsafeNew = Fixture(
        FixtureIds.DecompilerUnsafeNew,
        "ILInspector.Decompiler.Fixtures.NewUnsafe",
        "ILInspector.Decompiler.Fixtures.NewUnsafe.dll",
        Boundaries(FixtureBoundary.ModuleAttribute),
        "decompiler", "unsafe", "updated-memory-safety");

    public static readonly FixtureDefinition DecompilerUnsafeChainA = Fixture(
        FixtureIds.DecompilerUnsafeChainA,
        "ILInspector.Decompiler.Fixtures.UnsafeChainA",
        "ILInspector.Decompiler.Fixtures.UnsafeChainA.dll",
        Boundaries(FixtureBoundary.CrossAssemblyBoundary, FixtureBoundary.ModuleAttribute),
        "decompiler", "unsafe", "chain", "updated-memory-safety");

    public static readonly FixtureDefinition DecompilerUnsafeChainB = Fixture(
        FixtureIds.DecompilerUnsafeChainB,
        "ILInspector.Decompiler.Fixtures.UnsafeChainB",
        "ILInspector.Decompiler.Fixtures.UnsafeChainB.dll",
        Boundaries(FixtureBoundary.CrossAssemblyBoundary, FixtureBoundary.ModuleAttribute),
        "decompiler", "unsafe", "chain", "updated-memory-safety");

    public static readonly FixtureDefinition DecompilerUnsafeChainC = Fixture(
        FixtureIds.DecompilerUnsafeChainC,
        "ILInspector.Decompiler.Fixtures.UnsafeChainC",
        "ILInspector.Decompiler.Fixtures.UnsafeChainC.dll",
        Boundaries(FixtureBoundary.CrossAssemblyBoundary, FixtureBoundary.ModuleAttribute, FixtureBoundary.OutputKind),
        "decompiler", "unsafe", "chain", "legacy-memory-safety", "executable");

    public static readonly FixtureDefinition DecompilerVbFinalizer = Fixture(
        FixtureIds.DecompilerVbFinalizer,
        "ILInspector.Decompiler.Fixtures.VbFinalizer",
        "ILInspector.Decompiler.Fixtures.VbFinalizer.dll",
        Boundaries(FixtureBoundary.CompilerLowering),
        "decompiler", "vb", "finalizer");

    public static readonly FixtureDefinition RunFasterAllocation = Fixture(
        FixtureIds.RunFasterAllocation,
        "RunFaster.AllocationFixture",
        "RunFaster.AllocationFixture.dll",
        ["runfaster", "allocation", "trace-coupled"],
        Boundaries(FixtureBoundary.SidecarAsset),
        Asset("fixture.nettrace", "runfaster.Tests", "Fixtures/RunFaster.AllocationFixture/fixture.nettrace"));

    public static readonly FixtureDefinition RestoredProjectDependencyFacts = Fixture(
        FixtureIds.RestoredProjectDependencyFacts,
        "DotnetInspector.RestoredProjectFixtures",
        "DotnetInspector.RestoredProjectFixtures.dll",
        ["restored-project", "dependency-facts", "multi-target"],
        Boundaries(FixtureBoundary.TargetFramework, FixtureBoundary.SidecarAsset, FixtureBoundary.PostBuildTransformation),
        Asset("project.assets.json", "DotnetInspector.RestoredProjectFixtures", "project.assets.json"),
        Asset("manifest.nuspec", "DotnetInspector.RestoredProjectFixtures", "RestoredProjectFixture.nuspec"));

    public static readonly FixtureDefinition ServicesRouteLearningBase =
        Fixture(
            FixtureIds.ServicesRouteLearningBase,
            "DotnetInspector.Services.RouteLearning.Base",
            "DotnetInspector.Services.RouteLearning.Base.dll",
            Boundaries(FixtureBoundary.CrossAssemblyBoundary),
            "services", "binding", "route-learning", "base");

    public static readonly FixtureDefinition ServicesRouteLearningContract =
        Fixture(
            FixtureIds.ServicesRouteLearningContract,
            "DotnetInspector.Services.RouteLearning.Contract",
            "DotnetInspector.Services.RouteLearning.Middle.dll",
            Boundaries(FixtureBoundary.CrossAssemblyBoundary),
            "services", "binding", "route-learning", "compile-contract");

    public static readonly FixtureDefinition ServicesRouteLearningMiddle =
        Fixture(
            FixtureIds.ServicesRouteLearningMiddle,
            "DotnetInspector.Services.RouteLearning.Middle",
            "DotnetInspector.Services.RouteLearning.Middle.dll",
            Boundaries(FixtureBoundary.CrossAssemblyBoundary),
            "services", "binding", "route-learning", "middle");

    public static readonly FixtureDefinition ServicesRouteLearningConsumer =
        Fixture(
            FixtureIds.ServicesRouteLearningConsumer,
            "DotnetInspector.Services.RouteLearning.Consumer",
            "DotnetInspector.Services.RouteLearning.Consumer.dll",
            ["services", "binding", "route-learning", "consumer"],
            Boundaries(FixtureBoundary.CrossAssemblyBoundary),
            Asset(
                "middle",
                "DotnetInspector.Services.RouteLearning.Consumer",
                "DotnetInspector.Services.RouteLearning.Middle.dll"),
            Asset(
                "base",
                "DotnetInspector.Services.RouteLearning.Consumer",
                "DotnetInspector.Services.RouteLearning.Base.dll"));

    public static readonly FixtureDefinition ServicesRouteLearningUnrelated =
        Fixture(
            FixtureIds.ServicesRouteLearningUnrelated,
            "DotnetInspector.Services.RouteLearning.Unrelated",
            "DotnetInspector.Services.RouteLearning.Unrelated.dll",
            Boundaries(FixtureBoundary.CrossAssemblyBoundary),
            "services", "binding", "route-learning", "unrelated");

    public static readonly IReadOnlyList<FixtureDefinition> All =
    [
        HostileLiterals,
        SourceLinkMalformed,
        SourceLinkNormalized,
        DiffV1,
        DiffV2,
        DiffAsmCaller,
        DiffAsmLibA,
        DiffAsmLibB,
        DiffAsmTarget,
        AnalysisCallerGraphCaller,
        AnalysisOwnershipFlow,
        AnalysisTopLevelAsync,
        AnalysisTopLevelClassicAsync,
        AnalysisCallerGraphCallerTwin,
        AnalysisCallerGraphIndirectCaller,
        AnalysisCallerGraphLookalikeCaller,
        AnalysisCallerGraphTarget,
        AnalysisCallerGraphTargetV2,
        AnalysisAsyncSiblingFriend,
        AnalysisCallerLoop,
        AnalysisCrossAsmCollision,
        AnalysisCrossAsmShape,
        AnalysisExceptionBase,
        AnalysisFacade,
        AnalysisLookalike,
        AnalysisMethodCorrespondenceRuntime,
        AnalysisMethodCorrespondenceSurface,
        AnalysisProtobuf,
        AnalysisRender,
        AnalysisSpoofSystemLinq,
        AnalysisSpoofSystemRuntime,
        DecompilerCheckedArithmetic,
        DecompilerTypeIdentity,
        DecompilerClassicAsync,
        DecompilerClassicAsyncArtifacts,
        DecompilerRuntimeAsync,
        DecompilerExpressionTreeSpoof,
        DecompilerClassicStateMachines,
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
        DecompilerVbFinalizer,
        RunFasterAllocation,
        RestoredProjectDependencyFacts,
        ServicesRouteLearningBase,
        ServicesRouteLearningContract,
        ServicesRouteLearningMiddle,
        ServicesRouteLearningConsumer,
        ServicesRouteLearningUnrelated,
        ResearchTargetSample,
        ResearchTargetCorrespondenceV1,
        ResearchTargetCorrespondenceV2,
    ];

    public static readonly FixturePair DiffPair = new("diff", DiffV1, DiffV2);

    public static readonly FixtureGroup DiffAssemblyFixtures = new(
        "diff-asm",
        [DiffAsmTarget, DiffAsmCaller, DiffAsmLibA, DiffAsmLibB]);

    public static readonly FixtureGroup AnalysisFixtures = new(
        "analysis",
        [
            AnalysisCallerGraphTarget,
            AnalysisCallerGraphTargetV2,
            AnalysisCallerGraphCaller,
            AnalysisOwnershipFlow,
            AnalysisTopLevelAsync,
            AnalysisTopLevelClassicAsync,
            AnalysisCallerGraphCallerTwin,
            AnalysisCallerGraphIndirectCaller,
            AnalysisCallerGraphLookalikeCaller,
            AnalysisCallerLoop,
            AnalysisCrossAsmCollision,
            AnalysisCrossAsmShape,
            AnalysisExceptionBase,
            AnalysisFacade,
            AnalysisLookalike,
            AnalysisMethodCorrespondenceRuntime,
            AnalysisMethodCorrespondenceSurface,
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
            DecompilerClassicAsyncArtifacts,
            DecompilerRuntimeAsync,
            DecompilerClassicStateMachines,
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

    public static readonly FixtureGroup DecompilerAsyncLoweringFixtures = new(
        "decompiler.async-lowering",
        [DecompilerClassicAsync, DecompilerRuntimeAsync]);

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

    public static readonly IReadOnlyList<FixtureGroup> Groups =
    [
        DiffAssemblyFixtures,
        AnalysisFixtures,
        DecompilerFixtures,
        DecompilerAsyncLoweringFixtures,
        DecompilerLadderFixtures,
        DecompilerUnsafeFixtures,
        RunFasterFixtures,
        ReturnToSenderCandidates,
    ];

    static readonly Dictionary<string, FixtureGroup> s_groupsById =
        Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);

    public static FixtureDefinition Get(string id)
        => s_byId.TryGetValue(id, out var fixture)
            ? fixture
            : throw new ArgumentException($"Unknown fixture id '{id}'.", nameof(id));

    public static FixtureGroup Group(string id)
        => s_groupsById.TryGetValue(id, out var group)
            ? group
            : throw new ArgumentException($"Unknown fixture group id '{id}'.", nameof(id));

    public static IReadOnlyList<FixtureDefinition> SelectByTag(string tag)
    {
        var matches = All
            .Where(fixture => fixture.Tags.Contains(tag, StringComparer.Ordinal))
            .ToArray();
        return matches.Length > 0
            ? matches
            : throw new ArgumentException($"Unknown fixture tag '{tag}'.", nameof(tag));
    }

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

    public static string ProjectDirectory(string id)
    {
        var fixture = Get(id);
        string root = RepositoryRoot();
        string path = Path.Combine(
            root,
            fixture.RepositoryProjectDirectory.Replace(
                '/',
                Path.DirectorySeparatorChar));
        if (Directory.Exists(path))
            return path;

        throw new DirectoryNotFoundException(
            $"Expected fixture source project '{fixture.Id}' at {path}.");
    }

    public static IReadOnlyList<string> SourcePaths(string id)
    {
        string projectDirectory = ProjectDirectory(id);
        return [.. Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(ProjectCompileSourcePaths(projectDirectory))
            .Select(Path.GetFullPath)
            .Where(path => !HasPathSegment(path, "bin") && !HasPathSegment(path, "obj"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    static IEnumerable<string> ProjectCompileSourcePaths(string projectDirectory)
    {
        foreach (var projectFile in Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            XDocument project;
            try
            {
                project = XDocument.Load(projectFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                continue;
            }

            foreach (var include in project
                .Descendants()
                .Where(element => element.Name.LocalName == "Compile")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                foreach (var path in ExpandCompileInclude(projectDirectory, include!))
                    yield return path;
            }
        }
    }

    static IEnumerable<string> ExpandCompileInclude(string projectDirectory, string include)
    {
        string normalized = include
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        string fullPattern = Path.GetFullPath(Path.Combine(projectDirectory, normalized));
        string? directory = Path.GetDirectoryName(fullPattern);
        if (directory is null || !Directory.Exists(directory))
            yield break;

        string pattern = Path.GetFileName(fullPattern);
        if (pattern.Contains('*', StringComparison.Ordinal) || pattern.Contains('?', StringComparison.Ordinal))
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                yield return path;
            yield break;
        }

        if (File.Exists(fullPattern))
            yield return fullPattern;
    }

    public static string AssetPath(string id, string assetName)
    {
        var fixture = Get(id);
        var asset = fixture.Assets.FirstOrDefault(asset => asset.Name == assetName);
        if (asset is null)
            throw new ArgumentException($"Fixture '{id}' has no asset named '{assetName}'.", nameof(assetName));

        string configuration = CurrentConfiguration();
        string root = RepositoryRoot();
        string path = Path.Combine(
            root,
            "artifacts",
            "bin",
            asset.ProjectName,
            configuration,
            asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(path))
            return path;

        throw new FileNotFoundException(
            $"Expected built fixture asset '{fixture.Id}/{asset.Name}' at {path}. Run 'dotnet build dotnet-inspect.slnx -c Release' before running harnesses or tests that consume fixture assets.",
            path);
    }

    static FixtureDefinition Fixture(string id, string projectName, string assemblyFileName, params string[] tags)
        => new(
            id,
            projectName,
            RepositoryProjectDirectory(projectName),
            assemblyFileName,
            tags,
            [],
            []);

    static FixtureDefinition Fixture(string id, string projectName, string assemblyFileName, FixtureBoundary[] boundaries, params string[] tags)
        => new(
            id,
            projectName,
            RepositoryProjectDirectory(projectName),
            assemblyFileName,
            tags,
            boundaries,
            []);

    static FixtureDefinition Fixture(string id, string projectName, string assemblyFileName, string[] tags, FixtureBoundary[] boundaries, params FixtureAsset[] assets)
        => new(
            id,
            projectName,
            RepositoryProjectDirectory(projectName),
            assemblyFileName,
            tags,
            boundaries,
            assets);

    static string RepositoryProjectDirectory(string projectName)
        => projectName switch
        {
            "DiffAsmFixtures.Caller" => "fixtures/diff/DiffAsmFixtures.Caller",
            "DiffAsmFixtures.LibA" => "fixtures/diff/DiffAsmFixtures.LibA",
            "DiffAsmFixtures.LibB" => "fixtures/diff/DiffAsmFixtures.LibB",
            "DiffAsmFixtures.Target" => "fixtures/diff/DiffAsmFixtures.Target",
            "DiffFixtures.V1" => "fixtures/diff/DiffFixtures.V1",
            "DiffFixtures.V2" => "fixtures/diff/DiffFixtures.V2",
            "DotnetInspector.HostileNameFixtures" => "fixtures/cli/DotnetInspector.HostileNameFixtures",
            "DotnetInspector.RestoredProjectFixtures" => "fixtures/queries/DotnetInspector.RestoredProjectFixtures",
            "DotnetInspector.SourceLinkMalformedFixtures" => "fixtures/sourcelink/DotnetInspector.SourceLinkMalformedFixtures",
            "DotnetInspector.SourceLinkNormalizedFixtures" => "fixtures/sourcelink/DotnetInspector.SourceLinkNormalizedFixtures",
            "DotnetInspector.Services.RouteLearning.Base" => "fixtures/services/DotnetInspector.Services.RouteLearning.Base",
            "DotnetInspector.Services.RouteLearning.Consumer" => "fixtures/services/DotnetInspector.Services.RouteLearning.Consumer",
            "DotnetInspector.Services.RouteLearning.Contract" => "fixtures/services/DotnetInspector.Services.RouteLearning.Contract",
            "DotnetInspector.Services.RouteLearning.Middle" => "fixtures/services/DotnetInspector.Services.RouteLearning.Middle",
            "DotnetInspector.Services.RouteLearning.Unrelated" => "fixtures/services/DotnetInspector.Services.RouteLearning.Unrelated",
            "ILInspector.Analysis.AsyncSiblingFriendFixtures" => "fixtures/analysis/ILInspector.Analysis.AsyncSiblingFriendFixtures",
            "ILInspector.Analysis.CallerGraphCaller" => "fixtures/analysis/ILInspector.Analysis.CallerGraphCaller",
            "ILInspector.Analysis.CallerGraphCallerTwin" => "fixtures/analysis/ILInspector.Analysis.CallerGraphCallerTwin",
            "ILInspector.Analysis.CallerGraphIndirectCaller" => "fixtures/analysis/ILInspector.Analysis.CallerGraphIndirectCaller",
            "ILInspector.Analysis.CallerGraphLookalikeCaller" => "fixtures/analysis/ILInspector.Analysis.CallerGraphLookalikeCaller",
            "ILInspector.Analysis.CallerGraphTarget" => "fixtures/analysis/ILInspector.Analysis.CallerGraphTarget",
            "ILInspector.Analysis.CallerGraphTargetV2" => "fixtures/analysis/ILInspector.Analysis.CallerGraphTargetV2",
            "ILInspector.Analysis.CrossAsmCollisionFixtures" => "fixtures/analysis/ILInspector.Analysis.CrossAsmCollisionFixtures",
            "ILInspector.Analysis.FacadeFixtures" => "fixtures/analysis/ILInspector.Analysis.FacadeFixtures",
            "ILInspector.Analysis.Fixtures" => "fixtures/analysis/ILInspector.Analysis.Fixtures",
            "ILInspector.Analysis.LookalikeFixtures" => "fixtures/analysis/ILInspector.Analysis.LookalikeFixtures",
            "ILInspector.Analysis.MethodCorrespondenceRuntimeFixtures" => "fixtures/analysis/ILInspector.Analysis.MethodCorrespondenceRuntimeFixtures",
            "ILInspector.Analysis.MethodCorrespondenceSurfaceFixtures" => "fixtures/analysis/ILInspector.Analysis.MethodCorrespondenceSurfaceFixtures",
            "ILInspector.Analysis.OwnershipFlowFixtures" => "fixtures/analysis/ILInspector.Analysis.OwnershipFlowFixtures",
            "ILInspector.Analysis.ProtobufFixtures" => "fixtures/analysis/ILInspector.Analysis.ProtobufFixtures",
            "ILInspector.Analysis.RenderFixtures" => "fixtures/analysis/ILInspector.Analysis.RenderFixtures",
            "ILInspector.Analysis.SpoofFixtures" => "fixtures/analysis/ILInspector.Analysis.SpoofFixtures",
            "ILInspector.Analysis.SpoofRuntimeFixtures" => "fixtures/analysis/ILInspector.Analysis.SpoofRuntimeFixtures",
            "ILInspector.Analysis.TopLevelAsyncFixtures" => "fixtures/analysis/ILInspector.Analysis.TopLevelAsyncFixtures",
            "ILInspector.Analysis.TopLevelClassicAsyncFixtures" => "fixtures/analysis/ILInspector.Analysis.TopLevelClassicAsyncFixtures",
            "ILInspector.Decompiler.Fixtures.CheckedArithmetic" => "src/ILInspector.Decompiler.Fixtures.CheckedArithmetic",
            "ILInspector.Decompiler.Fixtures.ClassicAsync" => "src/ILInspector.Decompiler.Fixtures.ClassicAsync",
            "ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts" => "src/ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts",
            "ILInspector.Decompiler.Fixtures.ClassicStateMachines" => "src/ILInspector.Decompiler.Fixtures.ClassicStateMachines",
            "ILInspector.Decompiler.Fixtures.ExpressionTreeSpoof" => "src/ILInspector.Decompiler.Fixtures.ExpressionTreeSpoof",
            "ILInspector.Decompiler.Fixtures.Ladder" => "src/ILInspector.Decompiler.Fixtures.Ladder",
            "ILInspector.Decompiler.Fixtures.LegacyUnsafe" => "src/ILInspector.Decompiler.Fixtures.LegacyUnsafe",
            "ILInspector.Decompiler.Fixtures.NewUnsafe" => "src/ILInspector.Decompiler.Fixtures.NewUnsafe",
            "ILInspector.Decompiler.Fixtures.RuntimeAsync" => "src/ILInspector.Decompiler.Fixtures.RuntimeAsync",
            "ILInspector.Decompiler.Fixtures.TypeIdentity" => "src/ILInspector.Decompiler.Fixtures.TypeIdentity",
            "ILInspector.Decompiler.Fixtures.UnsafeChainA" => "src/ILInspector.Decompiler.Fixtures.UnsafeChainA",
            "ILInspector.Decompiler.Fixtures.UnsafeChainB" => "src/ILInspector.Decompiler.Fixtures.UnsafeChainB",
            "ILInspector.Decompiler.Fixtures.UnsafeChainC" => "src/ILInspector.Decompiler.Fixtures.UnsafeChainC",
            "ILInspector.Decompiler.Fixtures.VbFinalizer" => "src/ILInspector.Decompiler.Fixtures.VbFinalizer",
            "ILInspector.Research.TargetFixtures" => "src/ILInspector.Research.TargetFixtures",
            "ResearchTargetCorrespondenceFixtures.V1" => "src/ResearchTargetCorrespondenceFixtures.V1",
            "ResearchTargetCorrespondenceFixtures.V2" => "src/ResearchTargetCorrespondenceFixtures.V2",
            "RunFaster.AllocationFixture" => "src/runfaster.Tests/Fixtures/RunFaster.AllocationFixture",
            _ => throw new ArgumentException(
                $"Unknown fixture project '{projectName}'.",
                nameof(projectName)),
        };

    static FixtureAsset Asset(string name, string projectName, string relativePath)
        => new(name, projectName, relativePath);

    static FixtureBoundary[] Boundaries(params FixtureBoundary[] boundaries)
        => boundaries;

    static bool HasPathSegment(string path, string segment)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));

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
