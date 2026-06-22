# Build Event Views Feature

This work uses `dotnet-inspect` to design useful offline views over managed
`dotnet build` event logs. The goal is to let `dotnet build` create a compact
baseline view plus a durable JSONL event log, then let `dotnet-inspect build`
iterate quickly on richer views that drive the SDK/MSBuild implementation.

## North star

`dotnet build` should capture facts once. `dotnet-inspect build` should query,
slice, summarize, and explain those facts repeatedly without rebuilding.

```bash
dotnet build --view summary --event-log
dotnet-inspect build <EventLogId> -S Summary --tsv
dotnet-inspect build <EventLogId> -S Types --tsv
dotnet-inspect build <EventLogId> -S Details --markdown
```

The SDK-owned baseline should stay small and stable. The richer agent workflow
belongs in `dotnet-inspect` first because it can move faster and support the
full renderer surface: Markdown, table, TSV, JSONL, and JSON.

## Related SDK issues

This feature is part of a larger SDK/agentic-CLI thread:

| Issue | Relevance |
| --- | --- |
| [dotnet/sdk#54417](https://github.com/dotnet/sdk/issues/54417) | Frames the success metric: reduce reasoning spent on noisy CLI output, not merely visible input tokens. Build views should make diagnostic grouping and next actions obvious enough to lower turn count, reasoning tokens, and cap-hit rate. |
| [dotnet/sdk#54333](https://github.com/dotnet/sdk/issues/54333) | Frames the distribution model: fast-moving AI-adjacent tools such as `dotnet-inspect` should ship out-of-band via NuGet, potentially as part of a tools metapackage, while still feeling like part of the official .NET toolchain. |
| [dotnet/sdk#54243](https://github.com/dotnet/sdk/issues/54243) | Frames discovery and agent guidance: `dotnet skill` should point agents at curated .NET skills/tools. The eventual build workflow should be easy for agents to discover and should have concise trigger text. |

These issues reinforce the split used here: `dotnet build` creates the stable,
low-friction substrate; `dotnet-inspect build` iterates on the high-value,
agent-efficient query surface; skills/tool discovery tells agents when to use
that surface.

## Current worktree

The active prototype is in this worktree:

```text
/home/rich/git/dotnet-inspect-build-event-query
branch: feature/build-event-query
```

Important files:

| Path | Purpose |
| --- | --- |
| `docs/design/build-event-log.md` | Detailed design/spec and view mockups. |
| `skills/dotnet-build-events/SKILL.md` | Provisional agent skill for validating the workflow. |
| `src/DotnetInspector.BuildEvents/` | JSONL reader, event DTOs, projections. |
| `src/dotnet-inspect/Commands/BuildCommand.cs` | Current command/view prototype. |
| `samples/build-event-logs/` | Static JSONL logs generated from `~/git/bad-code`. |
| `samples/build-event-view-snapshots/` | Current view outputs over those logs. |

## Local VMR test environment

Use `scripts/setup-build-event-env.sh` to create a local, ignored environment
that points agents at the right implementations:

```bash
./scripts/setup-build-event-env.sh
source .env.build-events
export PATH="$BUILD_EVENT_TOOLS_BIN:$PATH"
```

The setup creates wrapper commands under `.build-event-tools/bin/`:

| Command | Implementation |
| --- | --- |
| `dotnet` | Test shim over the VMR SDK `dotnet`; auto-writes event logs when `BUILD_EVENT_ALWAYS_EVENT_LOG=1`. |
| `dotnet-vmr` | Symlink to the raw VMR SDK `dotnet`. |
| `dotnet-build-events` | Compatibility alias for the test shim. |
| `dotnet-inspect-dev` | This worktree's `src/dotnet-inspect` via `dotnet run --no-build`. |
| `capture-build-event-log` | Harness-only helper that captures VMR build JSONL, stdout/stderr, exit code, and `manifest.json` into `artifacts/build-event-runs/`. |

Example:

```bash
dotnet build --view summary "$BUILD_EVENT_BAD_CODE_ROOT/ZeroDaySearch/ZeroDaySearch.csproj"
dotnet build --view types "$BUILD_EVENT_BAD_CODE_ROOT/ZeroDaySearch/ZeroDaySearch.csproj"
dotnet-inspect-dev build <printed-jsonl-path> -S Types --tsv
dotnet-inspect-dev build <printed-jsonl-path> -S Details --code CS1061 --markdown
```

The `dotnet` shim injects `--event-log-file <run>/build.jsonl` and
`--event-log-stderr` for `dotnet build` when the user did not already specify an
event-log option. This means the agent must still choose the build view
(`summary`, `types`, etc.), while the test harness always gets an event log. The
sidecar `manifest.json` records the exact VMR `dotnet` command, working
directory, event log path, exit code, and VMR SDK version. The JSONL stream
itself does not yet carry command-line metadata.

## Static sample logs

Fresh logs were generated from `~/git/bad-code` with the VMR dogfood SDK:

```bash
/home/rich/git/dotnet-build-events-vmr-e2e/src/sdk/artifacts/bin/redist/Debug/dotnet/dotnet \
  build --no-restore --view summary --event-log-file <log.jsonl> <project.csproj>
```

Samples now present:

| Sample | Source project | Lines | Size | Exit |
| --- | --- | ---: | ---: | ---: |
| `badwolf.jsonl` | `~/git/bad-code/BadWolf/BadWolf.csproj` | 1,961 | 977 KB | 1 |
| `enter-the-ring.jsonl` | `~/git/bad-code/EnterTheRing/EnterTheRing.csproj` | 1,978 | 996 KB | 1 |
| `zerodaysearch.jsonl` | `~/git/bad-code/ZeroDaySearch/ZeroDaySearch.csproj` | 2,030 | 1,030 KB | 1 |

The logs are useful for offline view development because they represent three
different error shapes:

| Sample | Summary | Top diagnostic types |
| --- | --- | --- |
| `badwolf` | 1 failed project, 2 errors | `CS1003`, `CS1013` |
| `enter-the-ring` | 1 failed project, 29 errors | `CS4014`, `CS0029`, `CS0019`, `CS1929`, `CS1503`, `CS4016` |
| `zerodaysearch` | 1 failed project, 51 errors | `CS1061`, `CS0103`, `CS1739`, `CS1729` |

For now these logs live directly in the worktree. If the sample set grows, move
large or numerous logs to an orphan branch such as `build-event-log-samples` and
keep a small manifest on the feature branch that records sample name, source
project, SDK build, command line, event schema version, and expected summaries.

## Larger scenario: Jellyfin warning cleanup

`~/git/jellyfin/Jellyfin.sln` is the next qualitative test. The prompt should be:

```text
Fix all warnings in jellyfin/jellyfin. Skip warnings where the fix would affect
compatibility, and do not suppress skipped warnings. Report before/after warning
counts by code and explain skipped codes.
```

This scenario tests a different path than compile-error repair:

| Capability | What it tests |
| --- | --- |
| `dotnet build --view types` | Whether the agent can start from an SDK-owned warning rollup. |
| `Warnings --tsv` | Whether compact warning rows are enough as the work queue. |
| `Projects --tsv` | Whether the agent scopes warning clusters by project before editing. |
| `Details --code <code>` | Whether source context is used only when needed. |
| `Explain` | Needs analyzer/compatibility clusters such as `CA1819`, `CA2227`, `CA1002`, `CA2100`. |
| Before/after logs | Enables warning-count metrics without scraping raw output. |

This is also the first scenario where the correct answer may be "do not fix
this warning because it changes public API compatibility."

Current blocker for this scenario:

- Jellyfin pins SDK `10.0.0` with `rollForward: latestMinor`; the VMR test SDK is
  `11.0.100-dev`. Use an isolated Jellyfin worktree and test-only `global.json`
  override before running VMR builds.
- With that override, the current VMR build reaches MSBuild but fails before the
  intended warning-cleanup shape: `Types` reports 172 code-less errors and 16
  `NU1903` warnings. The error rows are Roslyn/analyzer assertion output from
  `Microsoft.CSharp.Core.targets` (`Unknown pattern kind 'TypePattern'` in
  `Microsoft.CodeAnalysis.NetAnalyzers` flow analysis), not ordinary Jellyfin
  warning debt.
- This is not an event-logging root-cause bug: the event stream is preserving
  diagnostics emitted by the crashed toolchain/analyzer. The `Explain` view has
  a `toolchain-analyzer-crash` cluster so agents do not treat this as an
  application-code warning cleanup.
- The same crash does not reproduce with the local .NET 10 SDK selected by the
  Jellyfin repo (`10.0.301`). `dotnet build Jellyfin.sln` completes with
  `0 Error(s)` and `231 Warning(s)`; no `Unknown pattern kind`, `Assertion
  failed`, `Process terminated`, or NetAnalyzers stack trace appears. The raw
  .NET 10 console output repeats warning lines, so grep-based counts can double
  count; the build summary is the authoritative total.
- The same crash also does not reproduce with an isolated .NET 11 daily SDK
  (`11.0.100-preview.6.26319.103`, installed under `/tmp/dotnet-nightly-11` for
  the probe). With a test-only `global.json` override, `dotnet build
  Jellyfin.sln` completes with `0 Error(s)` and `229 Warning(s)` and no analyzer
  assertion/crash pattern. This suggests the VMR failure is specific to that VMR
  build or source state, not a general .NET 11 daily behavior.
- Do not use this scenario for agent evaluation until the VMR analyzer assertion
  is resolved or we pick a Jellyfin commit/configuration that builds far enough
  to expose the warning inventory.
- For a safe latest-upstream test, use the fresh VMR worktree at
  `/home/rich/git/dotnet-build-events-vmr-latest-test` on branch
  `build-event-stream-vmr-latest-test` (`origin/main` at `b756a8d8a0e`). Keep
  `/home/rich/git/dotnet-build-events-vmr-e2e` unchanged as the repro/prototype
  worktree. Reapply the MSBuild/SDK event-stream changes into the fresh worktree,
  rebuild the SDK redist, then rerun Jellyfin.
- Current latest-upstream port status:
  - The event-stream patch applies to `/home/rich/git/dotnet-build-events-vmr-latest-test`
    with one trivial `Parser.cs` conflict already resolved.
  - Building only patched MSBuild succeeds.
  - Building only patched SDK CLI succeeds.
  - Full SDK/redist build is blocked by restore/package infrastructure, not by
    the event-stream code: `NU1903` warnings-as-errors in test projects and
    `NU1102` for `aspnetcoretools.linux-x64` version
    `11.0.0-preview.6.26277.111`.
  - An ad-hoc overlay of the patched CLI/MSBuild assemblies into a public
    nightly SDK is not reliable; MSBuild internal type allowlist/dependency
    closure expects a fully coherent SDK layout.
- A nightly-matched VMR worktree also exists at
  `/home/rich/git/dotnet-build-events-vmr-nightly-test` for commit
  `93304cdf8a8` (the public nightly SDK commit). The same event-stream patch
  applies and the targeted MSBuild/CLI builds succeed, but a full redist build
  is blocked by the same restore/package issues. This confirms the next useful
  step is to get a coherent SDK/redist build, not ad-hoc assembly overlay.
- A coherent-enough event-stream redist was produced from the nightly-matched
  worktree by building `redist.csproj` with a local ASP.NET version override:
  `MicrosoftAspNetCoreAppRefPackageVersion`,
  `MicrosoftAspNetCoreAppRefInternalPackageVersion`, and
  `MicrosoftAspNetCoreAppRuntimePackageVersion` set to
  `11.0.0-preview.6.26316.109`, then overlaying the patched MSBuild closure and
  `NuGet.Frameworks` `7.9.0.0`. Missing Razor/StaticWebAssets SDK props/targets
  were manually copied into the redist layout for the Jellyfin probe.
- With that redist, event logging works and the Razor SDK resolution errors are
  gone, but Jellyfin still hits the analyzer crash. Running the same redist
  without `--view`/event logging also crashes (`MSB4236` count is 0; analyzer
  crash count is 38), confirming again that the crash is not caused by event
  logging.
- The public binary nightly SDK `11.0.100-preview.6.26319.103` still builds
  Jellyfin successfully. The locally rebuilt redist uses an older/mixed SDK
  dependency closure (`11.0.100-dev`, preview.6.26277-era components), so it is
  not equivalent to the public nightly despite being ported to the same VMR
  commit.
- Binlog comparison confirms the crash is in the SDK analyzer pipeline:
  - Failing binlog:
    `files/binlogs/jellyfin-sln-redist-crash.binlog` in the session state.
    The first crash is the `Csc` task for `MediaBrowser.Model.csproj` under
    `CoreCompile`.
  - Good binlog:
    `files/binlogs/jellyfin-sln-public-nightly-good.binlog` in the session
    state. The same `MediaBrowser.Model` compiler task uses the same
    `analysislevel_10_all.globalconfig` analyzer config and succeeds.
  - The failing task loads implicit SDK analyzers from the local Debug redist:
    `Sdks/Microsoft.NET.Sdk/analyzers/Microsoft.CodeAnalysis.NetAnalyzers.dll`
    and `Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll`.
  - The first failing stack is
    `Microsoft.CodeQuality.Analyzers.Maintainability.AvoidDeadConditionalCode`
    (CA1508) calling flow analysis, which hits
    `Debug.Fail("Unknown pattern kind 'TypePattern'")` in
    `DataFlowOperationVisitor`.
  - A second probe with `AnalysisLevel=10-recommended` still crashes, this time
    in `Microsoft.CodeQuality.Analyzers.QualityGuidelines.AvoidMultipleEnumerations`
    (CA1851) on Jellyfin source `child is not IItemByName`, hitting another
    `Debug.Fail` in the same flow-analysis helper. This means the issue is not
    a single rule; it is a Debug/dev NetAnalyzers flow-analysis assertion path.
  - A temporary binlog build with `RunAnalyzersDuringBuild=false` has zero
    analyzer-crash lines and then reaches unrelated incomplete-redist
    StaticWebAssets task errors, confirming analyzers are the crash source.
- Version evidence from the binlogs and DLL metadata:
  - Local Debug redist compiler/analyzer closure:
    `csc.dll` `5.8.0-1.26277.111`,
    `Microsoft.Build.Tasks.CodeAnalysis.dll` `5.8.14.27811`, and
    NetAnalyzers stamped `assembly=42.42.42.42`, `product=11.0.100-dev`.
  - Public nightly compiler/analyzer closure:
    `csc.dll` `5.9.0-1.26319.103`,
    `Microsoft.Build.Tasks.CodeAnalysis.dll` `5.9.14.32003`, and
    NetAnalyzers `file=11.1.26.32003`,
    `product=11.0.100-preview.6.26319.103`.
  - Conclusion: the Jellyfin crash is caused by the locally rebuilt
    Debug/dev/mixed analyzer closure asserting on valid pattern operations, not
    by the event logger.
- Analyzer-disable proof:
  - `dotnet build Jellyfin.sln --no-restore --no-incremental --view summary
    --event-log-stderr /p:UseSharedCompilation=false
    /p:EnableNETAnalyzers=false` succeeds with the local event-stream redist.
  - Final proof artifacts are in session state under
    `files/analyzer-disable/jellyfin-sln-redist-netanalyzers-false-final.*`.
    The emitted event log summary is `2318` projects, `0` failed, `0` errors,
    `7` warnings.
  - The remaining warnings are all `NU1903` vulnerability-audit warnings for
    `SQLitePCLRaw.lib.e_sqlite3`; there are no analyzer diagnostics and no
    analyzer crash/assertion lines.
  - This required restoring the local Web SDK layout coherently in the redist:
    copy `Microsoft.NET.Sdk.Razor` and `Microsoft.NET.Sdk.StaticWebAssets` from
    `src/sdk/.dotnet/sdk/11.0.100-preview.5.26227.104/Sdks/` into the final
    redist `Sdks/` folder. Copying the public nightly Web SDK tasks caused
    `CS9057` because those Razor source generators reference Roslyn `5.9.0.0`
    while the local redist compiler is Roslyn `5.8.0.0`.
  - This proves the local SDK can build Jellyfin once implicit SDK NetAnalyzers
    are removed from the compilation, which reinforces that the crash source is
    the SDK NetAnalyzers closure.
- Pure VMR result:
  - Clean worktree: `/home/rich/git/dotnet-build-events-vmr-pure`, branch
    `build-event-stream-vmr-pure`, based on `origin/main` at `b756a8d8a0e`.
  - Imported the local SDK/MSBuild event-stream source changes from the existing
    latest VMR patch. No binary overlays or public SDK task copies are used.
  - Build command:
    `./build.sh --clean-while-building --warnAsError false --nodeReuse false`.
    The build succeeded and produced
    `artifacts/assets/Release/Sdk/11.0.100-preview.6/dotnet-sdk-11.0.100-dev-linux-x64.tar.gz`.
  - One source workaround was needed for this machine/build graph: in VMR builds
    only, `src/sdk/src/Tasks/sdk-tasks/sdk-tasks.InTree.targets` runs in-tree SDK
    tasks in-process instead of through `TaskHostFactory`. Otherwise the SDK
    redist build tries to launch .NET task hosts from the bootstrap SDK
    (`11.0.100-preview.5.26227.104`) and fails with `MSB4216`. Non-VMR builds
    keep the existing `TaskHostFactory` declarations.
  - Extracted SDK:
    `/home/rich/git/dotnet-build-events-vmr-pure/artifacts/pure-sdk-test/dotnet`
    reports `11.0.100-dev`, host/runtime `11.0.0-dev`, MSBuild `18.9.0-dev`,
    and includes the event-log/build-view switches.
  - Jellyfin verification with analyzers enabled:
    `dotnet build Jellyfin.sln --no-restore --no-incremental --view summary
    --event-log-stderr /p:UseSharedCompilation=false`.
    Result: exit `0`, `2318` projects, `0` failed, `0` errors, `221` warnings,
    no analyzer crash/assertion lines. Artifacts are in session state under
    `files/pure-vmr/jellyfin-pure-vmr.*`; emitted event log:
    `/home/rich/.dotnet/build-events/2026-06-20/20260620T230859.1653827Z-4029312-build-04872f02.jsonl`.
  - `Types` for that event log:
    `CA1819` 133, `CA1002` 24, `CA2227` 21, `CA1721` 18, `NU1903` 8,
    `CA1008` 5. `Explain` currently has no matching clusters for these warning
    families.
  - This supersedes the Frankenstein layout evidence for Jellyfin: a coherent
    source-built VMR SDK can build Jellyfin with the event logger and analyzers
    enabled.

## Current output analysis

The command accepts an EventLogId or JSONL path:

```bash
dotnet-inspect build samples/build-event-logs/zerodaysearch.jsonl -S Types --tsv
```

The current snapshots show which views are worth keeping:

| View | Snapshot finding | Decision |
| --- | --- | --- |
| `Summary` | Dense one-row schema; useful status and EventLogId handoff. | Keep as the first view. |
| `Types` / `DiagnosticTypes` | Dense aggregate schema; immediately identifies repeated failure classes. | Keep as the main triage view after `Summary`. |
| `Diagnostics` | Dense detail schema: severity, code, project, file, line, column, message. | Keep as the canonical diagnostic row view. |
| `Errors` | Dense today, but schema differs from `Diagnostics` and includes `Context`. | Keep as a filtered `Diagnostics` projection; omit only the constant severity column. Move source context to `Details`. |
| `Warnings` | Not implemented as its own view. | Add as a filtered `Diagnostics` projection; same columns as `Errors`. |
| `Projects` | Dense except `RuntimeIdentifier`, which is empty for all current samples. | Keep, but hide RID by default; expose dimensions in detailed output. |
| `Graph` | Trivial for single-project error samples. | Keep as a secondary build-shape view, not core skill content. |
| `Targets` / `Tasks` | Dense but raw and noisy: about 80 tasks and 125+ targets per tiny failing project. | Do not promote as skill views. Replace with `Stages`/`Timeline` later. |
| `Artifacts` | Payload type exists; no projection/view yet. | Later investigation view. |
| `Details` | Partially implemented. | Rich Markdown diagnostics with source context. |
| `Explain` | Implemented as first cut. | Cluster-oriented diagnostic docs and first-fix guidance. |

The current implementation is intentionally prototype-level: it builds rows as
dictionaries inside `BuildCommand.cs` and writes table/TSV/JSON directly. The
next durable design should split this into semantic view models plus shared
rendering definitions so the SDK baseline views and `dotnet-inspect` views do
not drift.

## Table structure analysis

The current snapshots expose three schema problems that should be fixed before
skill content is written.

### 1. `Diagnostics` and `Errors` currently have different schemas

Current `Diagnostics`:

```text
Severity Code Project File Line Column Message
```

Current `Errors`:

```text
File Line Column Code Message Context
```

That difference is larger than just omitting the severity column:

- `Errors` drops `Project`, which is important in multi-project builds.
- `Errors` moves `Code` after the location, while `Types` uses `Severity` +
  `Code` as the grouping key.
- `Errors` adds `Context`, which is not raw event-log data and can become stale.

Decision: `Diagnostics`, `Errors`, and `Warnings` must share one semantic model.
`Diagnostics` renders the mixed-severity table and keeps `Severity` as the first
column. `Errors` means `Diagnostics --severity error`; `Warnings` means
`Diagnostics --severity warning`. Because severity is then constant and implied
by the view name, the default filtered projection should drop only the first
column.

### 2. `Types` should align with diagnostics

Current `Types`:

```text
Kind Severity Code Count
```

The useful join key is `Severity` + `Code`, which should appear first and in the
same order as `Diagnostics`. `Kind=diagnostic-type` is redundant when the user
explicitly selected the `Types` view, and it pushes the actionable columns right.

Recommended `Types` schema:

```text
Severity Code Count Description
```

`Description` should be optional but valuable for skill content because it lets
an agent map codes to likely fix strategy without another lookup.

### 3. `Projects` should prioritize ownership, not dimensions

Current `Projects`:

```text
Project TargetFramework RuntimeIdentifier Errors Warnings Succeeded
```

`RuntimeIdentifier` is empty in all three current samples and will usually be
irrelevant for normal compile failure triage. The view should answer "which
project owns the problem?" first.

Recommended default `Projects` schema:

```text
Project Errors Warnings Succeeded TargetFramework
```

Detailed project/dimension output can add:

```text
RuntimeIdentifier Configuration Platform SelfContained ProjectExecutions
```

## Recommended skill-worthy views

The skill surface should stay small. Each view needs a clear question it
answers and a clear next action.

| View | Question it answers | When to use it | Output contract |
| --- | --- | --- | --- |
| `Summary` | Did the build succeed, and where is the durable log? | First view after a build, or when checking a remembered EventLogId. | One row: status/succeeded, logical projects, failed projects, errors, warnings, EventLogId. |
| `Types` | Which diagnostic classes dominate this build? | Immediately after `Summary` when there are errors or warnings. Use it to choose a code to investigate first. | Aggregate rows: severity, code, count, optional description. |
| `Diagnostics` | What are the concrete diagnostics? | When scanning all failures/warnings or applying filters such as `--code`, `--project`, or `--file`. | Detail rows: severity, code, project, file, line, column, message. |
| `Errors` | What failed the build? | Shortcut for `Diagnostics --severity error`; use when fixing compile/build failures. | Same model as `Diagnostics`; default projection omits `Severity`. |
| `Warnings` | What warning debt exists? | Shortcut for `Diagnostics --severity warning`; use for cleanup tasks and warning-count before/after reports. | Same model as `Diagnostics`; default projection omits `Severity`. |
| `Projects` | Which project owns the problem? | Multi-project builds, project-scoped warning cleanup, or checking if failures are localized. | Project, target framework, errors, warnings, succeeded. RID/configuration only in detailed/dimensions mode. |
| `Details` | What evidence should an agent read before editing? | Before making fixes, especially after selecting one diagnostic code or project. | Rich Markdown diagnostic cards with source windows, spans, notes, selectors, and digests. |
| `Explain` | What does this diagnostic cluster mean and how is it usually fixed? | After `Types`, when a repeated diagnostic class suggests a known pattern. | Cluster docs with applies-to codes, likely cause, first fixes, and useful follow-up commands. |

Secondary/developer views:

| View | Use |
| --- | --- |
| `Graph` | Explain project/dimension structure or container/multi-targeting build shape. Not useful for small single-project compile failures. |
| `Stages` / `Timeline` | Future replacement for raw target/task dumps; use for "why did the build do this?" and performance/stage analysis. |
| `Artifacts` | Future view for outputs, packages, containers, and other produced artifacts. |

Views to hide from skill content:

| View | Reason |
| --- | --- |
| `Targets` | Raw MSBuild detail; too noisy for normal agent workflows. |
| `Tasks` | Raw MSBuild detail; too noisy for normal agent workflows. |

## Diagnostic view model

`Diagnostics`, `Errors`, and `Warnings` should share the same semantic model with
different filters and default projections.

Canonical diagnostic model:

| Column | Meaning |
| --- | --- |
| `Severity` | `error`, `warning`, `message`, etc. |
| `Code` | Diagnostic code such as `CS1061` or `CA1819`. |
| `Project` | Owning project, rendered repo-relative when possible. |
| `File` | Source or project file, rendered repo-relative when possible. |
| `Line` | 1-based diagnostic line. |
| `Column` | 1-based diagnostic column. |
| `Message` | Raw diagnostic message. |

Default `Diagnostics` projection:

```text
Severity Code Project File Line Column Message
```

Default `Errors` and `Warnings` projection:

```text
Code Project File Line Column Message
```

The filtered projections omit `Severity` because it is implied by the selected
view. The order is still intentional:

1. `Severity`, `Code`: `Diagnostics` matches `Types` grouping and makes
   filtering/sorting cheap.
2. `Code`: filtered views start with the actionable diagnostic class.
3. `Project`: preserve ownership before location, especially for multi-project
   builds and warning-cleanup work.
4. `File`, `Line`, `Column`: give the editor jump target.
5. `Message`: keep the long text at the end so TSV/table scans stay aligned.

If a workflow needs the severity column in a filtered result, use
`Diagnostics --severity error` or an explicit column projection. `Errors` and
`Warnings` are convenience projections, not separate data models.

`Types` is the aggregate over the same diagnostic stream:

| Column | Meaning |
| --- | --- |
| `Severity` | Same severity vocabulary as `Diagnostics`. |
| `Code` | Same diagnostic code as `Diagnostics`. |
| `Count` | Number of diagnostics with that severity + code. |
| `Description` | Optional short description for known codes. |

Source context does not belong in the detail schema because it makes `Errors`
and `Diagnostics` diverge and can become stale. Put context in `Details` or a
future `DiagnosticDetails` view, where it can be clearly labeled as an
enrichment derived from current source files.

## Rich diagnostic view structure

`Details`/rich diagnostic output is intentionally bulky. It should be powerful
when an agent needs source context, but it must not force the agent to read every
diagnostic card.

The rich diagnostic system should have three controls.

### 1. Filter before rendering

The preferred path is to select a diagnostic class or cluster first:

```bash
dotnet-inspect build <log> -S Types --tsv
dotnet-inspect build <log> -S Details --code CS1061 --markdown
```

All rich views should honor at least:

```text
--code <CODE>
--severity <LEVEL>
--project <PATTERN>
--file <PATTERN>
```

### 2. Limit the diagnostic cards

Rich output should default to one card. That makes a rich query safe by default
while still showing the compact index and matched/rendered counts. It should
support both head and tail style limits:

```bash
dotnet-inspect build <log> -S Details --code CS1061 --markdown
dotnet-inspect build <log> -S Details --code CS1061 --markdown --cards 5
dotnet-inspect build <log> -S Details --code CS1061 --markdown --tail-cards 5
```

Use cases:

| Limit | Use |
| --- | --- |
| Default `1` | Avoid the rich-card footgun; show one representative card. |
| First `n` | Normal fix loop; inspect the first representative diagnostics. |
| Last `n` | Check whether later diagnostics differ or are cascades. |
| No card expansion | Use TSV summaries when locations are enough. |

The report should always say how many diagnostics matched and how many cards
were rendered.

### 3. Assign stable diagnostic selectors

Each diagnostic row should get a short stable selector, similar to
`dotnet-inspect member --show-index` overload selectors. Use it to request one
rich card without re-rendering the whole report.

Candidate selector shape:

```text
E1, E2, E3 ... for errors
W1, W2, W3 ... for warnings
D1, D2, D3 ... for mixed Diagnostics rows
```

Candidate digest shape:

```text
CS1061:7f3a2c
```

Where the digest is computed from stable diagnostic identity:

```text
severity + code + project path + file path + line + column + message
```

Selectors are human-friendly within one view. Digests are more stable across
filters, ordering changes, and repeated queries. The row views can expose both:

```text
Id Digest Code Project File Line Column Message
E7 CS1061:7f3a2c CS1061 src/App.csproj src/SearchService.cs 42 17 ...
```

Then rich detail can be queried directly:

```bash
dotnet-inspect build <log> -S Details --diagnostic E7 --markdown
dotnet-inspect build <log> -S Details --diagnostic CS1061:7f3a2c --markdown
```

Rules:

- The selector must be printed in `Errors`, `Warnings`, and `Diagnostics` TSV
  when `--show-index` or equivalent is requested.
- The digest should not depend on absolute machine paths once repo root/build
  root metadata is available; prefer normalized repo-relative paths.
- If a source file changes, the diagnostic digest can remain the diagnostic
  identity, while the report separately marks source freshness.
- Do not use random IDs. Selectors and digests must be deterministic for the
  same event log.

## Comparison with Clang, Go, and Swift

The proposed rich diagnostic view is trying to combine the strongest pieces of
three existing ecosystems while avoiding their gaps for agent workflows.

### Clang

Representative Clang diagnostics are source-rich:

```text
/tmp/diag.c:5:20: error: use of undeclared identifier 'counnt'; did you mean 'count'?
    5 |     printf("%d\n", counnt);
      |                    ^~~~~~
      |                    count
/tmp/diag.c:4:9: note: 'count' declared here
    4 |     int count = "not an int";
      |         ^
```

Clang also has machine-ish fix-it output:

```text
fix-it:"/tmp/diag.c":{5:20-5:26}:"count"
```

What to copy:

- caret/range source presentation
- related notes attached to a primary diagnostic
- fix-it/suggestion representation when available
- controls such as maximum caret diagnostic lines

What not to copy:

- printing every rich diagnostic by default
- relying on text output as the only query surface

### Go

Go 1.26 `go build -json` is event-stream oriented:

```json
{"ImportPath":"example.com/zerodaysearch","Action":"build-output","Output":"./search.go:8:19: query.Tokens undefined (type Query has no field or method Tokens)\n"}
{"ImportPath":"example.com/zerodaysearch","Action":"build-fail"}
```

What to copy:

- newline-delimited events
- package/project identity on events
- terminal build-fail/build-finished style events

What not to copy:

- diagnostics as opaque output strings
- no source spans beyond parsed text
- no rich source context or fix detail

### Swift

Swift diagnostics are rich and readable:

```text
main.swift:13:18: error: incorrect argument label in call (have 'value:', expected 'text:')
13 | let query = Query(value: "zero day")
   |                  `- error: incorrect argument label in call (have 'value:', expected 'text:')
```

The captured Swift build also repeated some diagnostics across emit-module and
compile phases, which is useful evidence for why `Types`, digesting, and
deduplication matter.

What to copy:

- inline source snippets with precise caret markers
- diagnostic text that names expected vs actual values
- compiler support for fix-its/serialized diagnostics where available

What not to copy:

- repeated rich diagnostics without a compact index
- no obvious default query layer over the build output

### Net result for dotnet-inspect

`dotnet-inspect build -S Details` should be:

| Ecosystem lesson | dotnet-inspect design |
| --- | --- |
| Clang/Swift rich context is useful. | Use Markdown cards with source windows, spans, notes, hints, and optional symbols. |
| Clang/Swift output is bulky. | Default to limited cards; support `--cards`, `--tail-cards`, `--code`, and `--diagnostic`. |
| Go events are queryable. | Keep JSONL as the durable source and derive views repeatedly. |
| Go diagnostics are too stringly. | Preserve structured severity/code/project/file/line/column/message rows. |
| Swift can repeat diagnostics across phases. | Assign selectors/digests and report matched/rendered counts. |
| None provide agent-specific random access. | Add `Id`, `Digest`, `Section`, and `Lines` for direct follow-up queries. |

## Other findings from the samples

1. Paths are absolute because that is what the event stream currently contains.
   Views should render repo-relative paths when a build manifest/root is
   available.
2. Source context works only when the original source paths are available and
   unchanged. Offline logs need source freshness metadata or a manifest to avoid
   stale explanations.
3. The event log has no sidecar manifest yet. Future samples should capture
   command line, working directory, repo root, git commit/dirty state, SDK
   identity, schema version, and output log id separately from the event stream.
4. `RuntimeIdentifier` should be hidden from default `Projects` output. It is
   valuable for publish/container/multi-RID scenarios, but not for common build
   failure triage.

## MarkdownTableLogger inspiration

`~/git/markdown-table-logger` is the clearest prior-art prototype. The parts to
carry forward are architectural, not the logger-specific implementation:

```text
MSBuild events -> semantic schemas -> selectable views -> multiple renderers
```

Useful patterns:

| Pattern | What to adopt |
| --- | --- |
| Semantic row models | Keep stable models such as `ProjectResult`, `ErrorDiagnostic`, `ErrorTypeSummary`, and richer diagnostics before choosing Markdown/TSV/JSON. |
| Small first tables | Start prompt documents with `Projects` and `Build Errors`; let agents decide whether to drill into details. |
| Random access | Add `Section` and `Lines` columns so an agent can jump directly to detail blocks without rereading the whole report. |
| Prompt/report document | Compose a rich Markdown report from smaller views instead of inventing a separate data path. |
| Source windows | Include bounded code context for fix-oriented error views, with line ranges and diagnostic annotation. |
| Optional symbols | Symbol classification is valuable, but it must be an optional enrichment that degrades cleanly. |
| Persistent index | A manifest/index over generated views makes "latest build" and offline reuse natural. |

Concrete examples from that repo:

- `PROMPT-EXAMPLE.md` shows the desired report shape: Projects, Build Errors,
  Error Details, source context, and referenced symbols.
- `src/MarkdownTableLogger/Models/SchemaModels.cs` shows the minimal semantic
  row types.
- `src/MarkdownTableLogger/Output/OutputGenerator.cs` shows two useful ideas:
  a two-pass report writer that adds `Section`/`Lines`, and a build-log index.
- `src/DotnetLogs/Program.cs` shows a small CLI over persisted log artifacts.

## Proposed next implementation order

1. **Stabilize sample handling**
   - Keep the three current JSONL logs as the first fixture set.
   - Add a manifest for each log with source repo, project path, command line,
     SDK, schema version, exit code, and expected summaries.
   - Decide whether samples stay in this branch or move to an orphan branch.

2. **Extract view models**
   - Move `Summary`, `Types`, `Projects`, and `Diagnostics` row shapes out of
     `BuildCommand.cs`.
   - Keep them renderer-independent and test them directly against sample logs.
   - Implement `Errors` and `Warnings` as filtered `Diagnostics` views with
     default projections that omit the constant `Severity` column.

3. **Make baseline views drift-proof**
   - Treat `Summary` and `Types` as the shared SDK/dotnet-inspect baseline set.
   - Add golden TSV tests from `samples/build-event-view-snapshots/`.
   - Later add Markdown/table/JSON/JSONL golden coverage through the normal
     dotnet-inspect renderers.

4. **Build the first rich details view**
   - Implement `Details` as a composed Markdown view:
     `Summary` -> `Projects` -> `Types` -> filtered `Diagnostics` -> `Error Details`.
   - Add `Section` and `Lines` columns inspired by MarkdownTableLogger.
   - Use bounded source context and clearly mark when source is unavailable or
     potentially stale.

5. **Tame target/task noise**
   - Use the MSBuild stage/category projection work to group targets/tasks.
   - Prefer a small `Stages`/`Timeline` view over raw task dumps by default.
   - Keep raw `Targets` and `Tasks` available for detailed debugging.

6. **Add query filters**
   - `--code`, `--severity`, `--project`, `--file`, and `--stage` should work
     across relevant views.
   - The common agent loop should be:

     ```bash
     dotnet-inspect build <log> -S Types --tsv
     dotnet-inspect build <log> -S Errors --code CS1061 --markdown
     ```

## Open decisions

| Decision | Current leaning |
| --- | --- |
| Where to store static logs? | Keep the first three in-tree; move larger corpora to an orphan branch. |
| What is the build-log manifest shape? | Sidecar JSON, not embedded in event stream. |
| Are baseline SDK views TSV or Markout? | Semantic views are the contract; TSV is only the current SDK bootstrap. |
| Does `Summary.Projects` mean logical projects or executions? | Logical projects in user-facing views; expose executions separately. |
| How much source goes into reports? | Bounded context only; no source text in raw event logs by default. |
| How does symbol enrichment work? | Optional dotnet-inspect-side enrichment, not an SDK build dependency. |
