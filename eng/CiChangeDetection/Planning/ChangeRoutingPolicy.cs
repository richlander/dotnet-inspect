using System.Collections.ObjectModel;

namespace CiChangeDetection.Planning;

/// <summary>
/// The planner's path and event routing policy. Every repository path rule
/// that decides whether a CI validation applies lives here, ported from
/// <c>eng/ci-detect-changes.sh</c> including its first-match <c>case</c>
/// semantics, in which <c>*</c> crosses <c>/</c>.
/// </summary>
internal sealed class ChangeRoutingPolicy
{
    private const string DetectionScript = "eng/ci-detect-changes.sh";

    private readonly ProjectInventory? webProjects;
    private readonly ProjectInventory? decompilerSkipProjects;

    private ChangeRoutingPolicy(
        ProjectInventory? webProjects,
        ProjectInventory? decompilerSkipProjects,
        IReadOnlyList<string> diagnostics)
    {
        this.webProjects = webProjects;
        this.decompilerSkipProjects = decompilerSkipProjects;
        Diagnostics = Array.AsReadOnly([.. diagnostics]);
    }

    /// <summary>
    /// Gets the bounded diagnostic codes recording deliberately conservative
    /// policy choices taken while loading policy data.
    /// </summary>
    internal ReadOnlyCollection<string> Diagnostics { get; }

    /// <summary>
    /// Loads routing policy data. Both conservative behaviors are policy, not
    /// an accidental parsing fallback: a missing or malformed inspect-web
    /// inventory broadens the Browser/Wasm lane to every <c>src</c> change,
    /// and a missing or malformed decompiler skip inventory exempts nothing.
    /// </summary>
    /// <param name="repository">The repository root directory.</param>
    /// <returns>The loaded policy.</returns>
    internal static ChangeRoutingPolicy Load(string repository)
    {
        List<string> diagnostics = [];
        ProjectInventory? web =
            ProjectInventory.TryLoad(
                repository,
                "eng/inspect-web-gate-projects.txt",
                ["src/"],
                requireNonEmpty: true,
                out ProjectInventory loadedWeb)
                ? loadedWeb
                : null;
        if (web is null)
        {
            diagnostics.Add(
                PlanDiagnosticCodes.InspectWebInventoryUnavailable);
        }

        ProjectInventory? decompiler =
            ProjectInventory.TryLoad(
                repository,
                "eng/decompiler-gate-skip-projects.txt",
                ["fixtures/", "src/", "tests/", "tools/"],
                requireNonEmpty: false,
                out ProjectInventory loadedDecompiler)
                ? loadedDecompiler
                : null;
        if (decompiler is null)
        {
            diagnostics.Add(
                PlanDiagnosticCodes.DecompilerSkipInventoryUnavailable);
        }

        diagnostics.Sort(StringComparer.Ordinal);
        return new ChangeRoutingPolicy(web, decompiler, diagnostics);
    }

    /// <summary>
    /// Routes an acquired change set to raw selections.
    /// </summary>
    /// <param name="evidence">The acquired change evidence.</param>
    /// <returns>The raw routing selections.</returns>
    internal RoutingSelections Route(ChangeEvidence evidence)
    {
        RoutingState state = default;
        foreach (ChangeRecord record in evidence.Records)
        {
            if (BytePattern.Matches(record.Path, DetectionScript))
            {
                return RoutingSelections.All;
            }

            RoutePath(record.Path, ref state);
        }

        // `ilroundtrip` has no job of its own: its steps live inside the test
        // job, which is gated on `code`. State the implication once rather
        // than duplicating `code` into every ilroundtrip rule.
        if (state.IlRoundtrip)
        {
            state.Code = true;
        }

        return new RoutingSelections(
            state.Code,
            state.CSharpDiff,
            state.Decompiler,
            state.Docs,
            state.IlDiff,
            state.IlRoundtrip,
            state.Packaging,
            state.Shipped,
            state.Web,
            state.Skills,
            state.Tla);
    }

    /// <summary>
    /// Reports whether a path is TLA+ model content, as distinct from the TLA+
    /// infrastructure paths that also select the lane. Only model content
    /// enters the scoped path corpus.
    /// </summary>
    /// <param name="path">The raw path bytes.</param>
    /// <returns>True when the path is a TLA+ module or configuration.</returns>
    internal static bool IsTlaModelContent(ReadOnlySpan<byte> path)
    {
        ReadOnlySpan<byte> folded = BytePattern.AsciiFold(path);
        return BytePattern.MatchesAny(
            folded,
            "docs/design/models/*.tla",
            "docs/design/models/*.cfg",
            "docs/models/*.tla",
            "docs/models/*.cfg");
    }

    /// <summary>
    /// Reports whether a path selects the TLA+ lane, through either the model
    /// content rules or the infrastructure trigger list.
    /// </summary>
    /// <param name="path">The raw path bytes.</param>
    /// <returns>True when the path selects TLA+ validation.</returns>
    internal static bool SelectsTla(ReadOnlySpan<byte> path) =>
        BytePattern.MatchesAny(
            path,
            ".github/workflows/ci.yml",
            DetectionScript,
            "eng/run-tla-checks.sh",
            "eng/test-tla-checks.sh",
            "eng/tla-module-overrides.txt",
            "eng/tla-expected-exit-codes.txt")
        || IsTlaModelContent(path);

    private void RoutePath(ReadOnlySpan<byte> path, ref RoutingState state)
    {
        if (IsWebProjectPath(path))
        {
            state.Code = true;
            state.Web = true;
        }

        RouteLanes(path, ref state);
        RouteCSharpDiff(path, ref state);
        RouteIlDiff(path, ref state);
        RouteDocs(path, ref state);
        RouteSkills(path, ref state);
        if (SelectsTla(path))
        {
            state.Tla = true;
        }

        RouteDecompiler(path, ref state);
        RouteIlRoundtrip(path, ref state);
        RoutePackaging(path, ref state);
        RouteShipped(path, ref state);
    }

    private static void RouteLanes(
        ReadOnlySpan<byte> path,
        ref RoutingState state)
    {
        if (BytePattern.MatchesAny(
            path,
            "src/NetworkDestinationPolicy.cs",
            "src/UnionPolyfill.cs"))
        {
            state.Code = true;
            state.Web = true;
        }
        else if (BytePattern.Matches(path, "src/*"))
        {
            state.Code = true;
        }
        else if (BytePattern.Matches(path, "fixtures/*"))
        {
            state.Code = true;
        }
        else if (BytePattern.MatchesAny(
            path,
            "tests/ILInspector.MetadataPrimitives.PlatformProbe/*",
            "tests/DotnetInspector.Artifacts.Local.PlatformProbe/*",
            "tests/ILInspector.JsExportSurface.TypeScriptFixtures/*",
            "tests/ILInspector.JsExportSurface.Tests/Fixtures/ts-jsexport-runtime/*"))
        {
            state.Code = true;
            state.Web = true;
        }
        else if (BytePattern.Matches(path, "tests/*"))
        {
            state.Code = true;
        }
        else if (BytePattern.MatchesAny(
            path,
            "tools/DecompilerHarness/*.md",
            "tools/DecompilerHarness/*.txt"))
        {
            // Documentation and text fixtures under the harness stay off the
            // code lane.
        }
        else if (BytePattern.Matches(path, "tools/DecompilerHarness/*"))
        {
            state.Code = true;
        }
        else if (BytePattern.Matches(path, "eng/test-ci-change-detection.cs"))
        {
            state.Code = true;
        }
        else if (BytePattern.Matches(path, "eng/inspect-web-gate-projects.txt"))
        {
            state.Code = true;
            state.Web = true;
        }
        else if (BytePattern.Matches(
            path,
            "eng/CiChangeDetection/PromotionWorkflowContract.cs"))
        {
            state.Code = true;
            state.Web = true;
        }
        else if (BytePattern.MatchesAny(
            path,
            "eng/CiChangeDetection/*",
            "eng/DependencyPolicy/*",
            "eng/DependencyPolicy.Tests/*",
            "eng/dependency-policy.json",
            "eng/package-fixtures/*",
            "eng/package-manifest-corpus.json",
            "eng/verify-package-manifest-corpus.cs",
            "eng/prepare-decompiler-assertion-corpus.sh",
            "eng/prepare-decompiler-corpus.sh",
            "eng/prepare-decompiler-opt-in-corpus.sh",
            "eng/prepare-decompiler-pr-corpus.sh",
            "eng/prepare-authored-source-oracles.sh",
            "eng/report-decompiler-opt-in-corpus-drift.sh",
            "eng/prepare-decompiler-package-sweep.cs",
            "eng/prepare-evil-corpus.sh",
            "docs/data/nuget-top-packages.lock.json",
            "docs/data/nuget-top-packages.json",
            "eng/restore-iltools.sh",
            "eng/activate-iltools.sh",
            "eng/test-ts-jsexport-context-aot.sh"))
        {
            state.Code = true;
        }
        else if (BytePattern.MatchesAny(
            path,
            "eng/run-method-semantics-platform-probe.sh",
            "eng/run-local-path-admission-platform-probe.sh"))
        {
            state.Code = true;
            state.Web = true;
        }
        else if (BytePattern.MatchesAny(
            path,
            "eng/test-ts-jsexport-typescript.sh",
            "eng/generate-inspect-web-multi-facade-canary.sh",
            "eng/test-inspect-web-multi-facade-canary.sh",
            "eng/validate-inspect-web-promotion.cs",
            "eng/validate-inspect-web-promotion.sh",
            "eng/generate-inspect-web-engine-facade.sh",
            "eng/InspectWebAsyncLoweringReceipt.targets",
            "eng/verify-inspect-web-async-deployment.sh"))
        {
            state.Web = true;
        }
        else if (BytePattern.Matches(path, "eng/BannedSymbols.txt"))
        {
            state.Code = true;
            state.Web = true;
        }
        else if (BytePattern.MatchesAny(
            path,
            ".gitattributes",
            "install.ps1",
            "eng/decompiler-gate-expected-classes.txt"))
        {
            state.Code = true;
        }
        else if (BytePattern.Matches(path, "prototypes/inspect-web/*.md"))
        {
            // Markdown under the browser prototype is documentation, not a
            // browser build input.
        }
        else if (BytePattern.MatchesAny(
            path,
            "prototypes/inspect-web/*",
            "prototypes/annotated-source-viewer/*"))
        {
            state.Web = true;
        }
        else if (BytePattern.MatchesAny(
            path,
            "*.props",
            "*.targets",
            "*.sln",
            "*.slnx"))
        {
            state.Code = true;
            state.Web = true;
        }
        else if (BytePattern.Matches(path, ".github/workflows/ci.yml"))
        {
            state.Code = true;
            state.Web = true;
        }
        else if (BytePattern.MatchesAny(
            path,
            ".github/workflows/deploy-inspect-web.yml",
            ".github/workflows/deploy-inspect-web-coreclr.yml",
            ".github/workflows/promote-inspect-web.yml"))
        {
            state.Web = true;
        }
        else if (BytePattern.Matches(path, ".github/workflows/*"))
        {
            state.Code = true;
        }
    }

    private static void RouteCSharpDiff(
        ReadOnlySpan<byte> path,
        ref RoutingState state)
    {
        if (BytePattern.MatchesAny(
            path,
            "tools/CSharpDiffHarness/*",
            "src/ILInspector.Decompiler/*",
            "src/ILInspector.ILDiff/*",
            "src/ILInspector.Instructions/*",
            "src/ILInspector.ControlFlow/*",
            "fixtures/diff/DiffFixtures.V1/*",
            "fixtures/diff/DiffFixtures.V2/*",
            "tools/DiffHarnessCommon/*",
            "Directory.Packages.props",
            "*.props",
            "*.targets",
            "*.slnx",
            ".github/workflows/ci.yml"))
        {
            state.CSharpDiff = true;
        }
    }

    private static void RouteIlDiff(
        ReadOnlySpan<byte> path,
        ref RoutingState state)
    {
        if (BytePattern.MatchesAny(
            path,
            "tools/IlDiffHarness/*",
            "src/ILInspector.ILDiff/*",
            "src/ILInspector.Instructions/*",
            "src/ILInspector.ControlFlow/*",
            "fixtures/diff/DiffFixtures.V1/*",
            "fixtures/diff/DiffFixtures.V2/*",
            "tools/DiffHarnessCommon/*",
            "Directory.Packages.props",
            "*.props",
            "*.targets",
            "*.slnx",
            ".github/workflows/ci.yml"))
        {
            state.IlDiff = true;
        }
    }

    private static void RouteDocs(
        ReadOnlySpan<byte> path,
        ref RoutingState state)
    {
        if (BytePattern.MatchesAny(
            path,
            ".markdownlint.*",
            ".markdownlint-cli2.*",
            "*/.markdownlint.*",
            "*/.markdownlint-cli2.*",
            "*.md",
            "*.txt",
            "docs/*",
            "skills/*"))
        {
            state.Docs = true;
        }
    }

    private static void RouteSkills(
        ReadOnlySpan<byte> path,
        ref RoutingState state)
    {
        if (BytePattern.Matches(path, ".github/workflows/ci.yml"))
        {
            state.Skills = true;
        }
        else if (BytePattern.Matches(path, "skills/*/*/SKILL.md"))
        {
            // A nested support document is linted, not gated.
        }
        else if (BytePattern.Matches(path, "skills/*/SKILL.md"))
        {
            state.Skills = true;
        }
    }

    private void RouteDecompiler(
        ReadOnlySpan<byte> path,
        ref RoutingState state)
    {
        if (BytePattern.MatchesAny(
            path,
            "eng/check-decompiler-gate.cs",
            "eng/decompiler-gate-known-red.txt",
            "eng/decompiler-gate-expected-classes.txt",
            "eng/decompiler-gate-skip-projects.txt"))
        {
            state.Decompiler = true;
        }
        else if (BytePattern.Matches(path, "*.md"))
        {
            // Documentation under a selected project does not run the gates.
        }
        else if (BytePattern.MatchesAny(
            path,
            "global.json",
            "*.props",
            "*.targets",
            "*.slnx",
            ".github/workflows/ci.yml"))
        {
            state.Decompiler = true;
        }
        else if (BytePattern.MatchesAny(
                path,
                "fixtures/*",
                "src/*",
                "tests/*",
                "tools/*")
            && !SkipsDecompilerProject(path))
        {
            state.Decompiler = true;
        }
    }

    private static void RouteIlRoundtrip(
        ReadOnlySpan<byte> path,
        ref RoutingState state)
    {
        if (BytePattern.MatchesAny(
            path,
            "tests/DotnetInspector.ILRoundtrip.Tests/*",
            "eng/restore-ilassembler.sh",
            "src/ILInspector.Metadata*",
            "src/DotnetInspector.Core/*",
            "*.props",
            "*.targets",
            "*.sln",
            "*.slnx"))
        {
            state.IlRoundtrip = true;
        }
    }

    private static void RoutePackaging(
        ReadOnlySpan<byte> path,
        ref RoutingState state)
    {
        if (BytePattern.MatchesAny(
            path,
            "src/dotnet-inspect/dotnet-inspect.csproj",
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "src/Directory.Build.props",
            "global.json",
            ".github/workflows/ci.yml",
            ".github/workflows/release.yml"))
        {
            state.Packaging = true;
        }
    }

    private static void RouteShipped(
        ReadOnlySpan<byte> path,
        ref RoutingState state)
    {
        if (BytePattern.Matches(path, "src/*Tests/*"))
        {
            // Test projects cannot introduce a net11-only API into the shipped
            // tool.
        }
        else if (BytePattern.MatchesAny(
            path,
            "src/*Fixtures*/*",
            "src/DiffFixtures*/*"))
        {
            // Neither can fixtures.
        }
        else if (BytePattern.MatchesAny(
            path,
            "src/*",
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "src/Directory.Build.props",
            "global.json",
            ".github/workflows/ci.yml"))
        {
            state.Shipped = true;
        }
    }

    private bool IsWebProjectPath(ReadOnlySpan<byte> path) =>
        webProjects is null
            ? BytePattern.Matches(path, "src/*")
            : webProjects.Covers(path);

    private bool SkipsDecompilerProject(ReadOnlySpan<byte> path) =>
        decompilerSkipProjects is not null
        && decompilerSkipProjects.Covers(path);

    private struct RoutingState
    {
        internal bool Code;
        internal bool CSharpDiff;
        internal bool Decompiler;
        internal bool Docs;
        internal bool IlDiff;
        internal bool IlRoundtrip;
        internal bool Packaging;
        internal bool Shipped;
        internal bool Web;
        internal bool Skills;
        internal bool Tla;
    }
}
