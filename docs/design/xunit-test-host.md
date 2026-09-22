# Repository xUnit Test Host

## Status and owner

This document owns the repository choice of command-line host for xUnit test
executables and the aggregate non-vacuity contract for test execution.
Issue [#5379](https://github.com/richlander/dotnet-inspect/issues/5379) tracks
adoption.

Microsoft Testing Platform (MTP) owns command-line parsing, filtering,
discovery, execution, reporting, and the zero-test exit result. xUnit owns its
adapter from MTP filters to xUnit test cases. Suite-specific argument expansion,
such as the decompiler's `--gate` presets, remains with that suite and runs
before the MTP boundary.

## Problem

The repository uses focused xUnit invocations as evidence for narrow changes.
With the xUnit v3 native console runner, a selector that matches no tests can
complete successfully after reporting `Total: 0`. A renamed, deleted, or
misspelled selection can therefore preserve a green command while removing all
of the intended evidence.

A repository-owned semantic preflight would need to consume xUnit's parser,
discovery, filter, identity, serialization, and explicit-test behavior. That
duplicates runner policy and couples repository infrastructure to libraries
whose semantics xUnit already adapts for MTP.

The `xunit.v3` package already includes MTP support. MTP's conventional
execution contract expects at least one test and returns exit code `8` when a
run executes none. The repository uses that existing contract instead of
building another test host.

## Boundary

The repository host consumes:

- one xUnit test executable configured to use MTP;
- an MTP argument vector in the repository evidence profile, after any
  suite-owned expansion; and
- the MTP integration supplied by the repository's pinned `xunit.v3` package.

It produces MTP's execution or non-execution result without a repository
preflight. MTP and xUnit remain responsible for interpreting selectors and
deciding which test cases run.

Repository call sites use the MTP command-line grammar directly. The host does
not carry a compatibility layer that translates native xUnit options at
runtime.

## Execution contract

Every repository xUnit executable uses the MTP command-line host for direct
execution through `dotnet run`. For a test-execution invocation:

1. The effective minimum expected test count is at least one.
2. Exit code `8` is not ignored.
3. Focused invocations use xUnit's MTP filter options rather than the native
   xUnit console options.
4. Suite-owned presets expand to MTP arguments before runner dispatch.
5. Parse, discovery, filter, execution, and reporting failures remain visible
   through MTP's exit result.

The repository evidence profile excludes an explicit MTP opt-out that ignores
exit code `8` or lowers the minimum expected test count below one. Such an
invocation deliberately declines the non-vacuity contract and is not supported
as repository evidence. The host does not add an argument parser to police
trusted callers; reviewed call sites and suite-owned expansion are responsible
for selecting the evidence profile.

The minimum applies to every test execution, including unfiltered and
exclusion-filtered runs. This is MTP's conventional behavior and is useful for
repository test executables: a supported test suite that executes nothing is
not successful evidence.

Help, test listing, runner information, and server operation are non-execution
modes. MTP owns their result as well; the repository does not weaken a nonzero
result when an empty filtered listing is rejected by the packaged MTP version.

## Aggregate and per-selection evidence

MTP's minimum count establishes **aggregate non-vacuity**: at least one test
ran. It does not establish that every value in a multi-value filter contributed
a test. A command combining one valid class with one stale class can still run
the valid class and satisfy the aggregate minimum.

The meaning of a multi-selection command follows the evidence claim made by
its owner:

- If the values describe one aggregate selection, MTP's nonzero execution is
  the complete host-level contract.
- If the command claims that each named class, method, or other selection
  contributed evidence, the owning suite or workflow must provide a
  domain-level receipt for each named claim.

That stronger receipt remains local to the scenario that needs it. The
repository host does not reproduce xUnit filtering to infer contribution.
Existing result checks may remain where they prove per-selection execution,
not merely that the overall run was nonzero.

The decompiler pre-merge gate is such a scenario. Its preset names independent
correctness classes, and its report checker requires execution evidence for
every expected class. It also compares an independent pre-enumerated discovery
reference with execution identities so every discovered case executes exactly
once. The decompiler suite owns that inventory, preset expansion, and complete
discovery-to-execution receipt.

## Convention and dependencies

xUnit v3 conventionally supports MTP as a built-in command-line host. With the
pinned `xunit.v3` 4.0.1 package, MTP 2.4.0 is already present; selecting it does
not require a new direct package reference or repository test-host library.
`dotnet run` remains the canonical repository command because the test project
is still an executable.

The repository does not depend directly on xUnit runner implementation
libraries to enforce selection non-vacuity. A future xUnit package update may
change the packaged MTP major version, but it must preserve this document's
minimum-count and visible-failure contract before adoption.

## Evidence

The pathological fixture is an ordinary test executable invoked through MTP
with a method filter that names no test. With the pinned `xunit.v3` 4.0.1 package,
it reports `Zero tests ran` and exits `8`. A neighboring valid fully qualified
method filter runs one test and exits `0`.

The implementation gate must cover:

- an unmatched focused filter returning exit code `8`;
- a valid focused filter reaching normal xUnit execution;
- an unfiltered suite reaching normal xUnit execution;
- an exclusion filter that leaves tests reaching normal execution;
- suite-owned argument expansion producing valid MTP filters;
- the decompiler's custom entry point dispatching ordinary execution to MTP;
- report production needed by domain-level per-selection receipts;
- removal of the decompiler's repository-owned semantic preflight;
- preservation of the decompiler's per-class and per-case completeness
  receipts, including exactly-once execution; and
- migration of supported repository invocations and examples to MTP syntax.

The same probe must show that one valid and one stale filter value can still
produce a successful aggregate run. This negative control pins the boundary:
MTP enforces the aggregate minimum, not per-selection contribution.

Another boundary probe must show that explicitly ignoring exit code `8` can
turn zero execution into success. This pins why that opt-out is outside the
repository evidence profile rather than motivating a second command-line
parser.

`MtpTestHostTests` is the outcome-level gate for the first adopter,
`DotnetInspect.Cli.Tests`. It starts the built test apphost and covers the
unmatched, valid, and mixed valid/stale filter outcomes. The suite's workflow
contract tests pin its MTP call sites and preserve the authenticated package
fixture's stronger not-skipped receipt.

`ILInspector.Analysis.Tests` is the second adopter. Its required Linux and
Windows lanes exercise exclusion-filtered execution through MTP, while Deep
Inspect exercises the unfiltered suite. The Windows workflow contract test pins
the migrated filter syntax. These paths use the same pinned xUnit integration
as the outcome-level host gate rather than duplicating that self-spawn harness
in every adopting suite.

`NuGetFetch.Tests` is the third adopter. Its CI and Deep Inspect lanes exercise
the offline `Network=Live` exclusion through MTP, while the NuGet authentication
test contract records the corresponding explicit live selection. These paths
reuse the pinned outcome-level host gate and preserve the test partition owned
by `docs/design/nuget-authentication.md`.

`DotnetInspector.ILRoundtrip.Tests` is the fourth adopter. Its required PR lane
exercises the fast `Speed=Slow` exclusion through MTP, while Deep Inspect and
the suite's focused README preserve the unfiltered vendored-assembler sweep.
These paths reuse the pinned outcome-level host gate without weakening the
round-trip oracle or moving broad sweep work into PR CI.

`ILInspector.JsExportSurface.Tests` is the fifth adopter. Its required focused
CI commands use MTP method and class filters plus the MTP xUnit report
extension. Existing report checks remain the stronger evidence that every
named method or class contributed execution; MTP supplies only aggregate
non-vacuity.

`ILInspector.Metadata.Tests` is the sixth adopter. Its ordinary Linux and
Windows PR commands remain unfiltered, while Deep Inspect preserves the
60-second long-running diagnostic threshold through MTP's `--long-running`
option with xUnit diagnostics enabled. Its process-isolated containment tests
also select their child workers through MTP method filters. These paths reuse
the pinned outcome-level host gate without changing the suite's metadata or
oracle evidence.

`ILInspector.Research.Tests` is the seventh adopter. Its ordinary PR command
remains unfiltered, while Deep Inspect preserves the suite's skipped-test
failure policy through MTP's `--fail-skips on` option. These paths reuse the
pinned outcome-level host gate without changing the Research evidence composed
from metadata, analysis, decompilation, source, or Finding contracts.

`DotnetInspector.Services.Tests` is the eighth migrated adopter. Its required
Linux and Windows PR commands and all three Deep Inspect platform lanes remain
unfiltered, while its signed-package fixture instructions select the focused
verification class through an MTP class filter. These paths reuse the pinned
outcome-level host gate without changing the suite's service, package, source,
cache, or resolution evidence.

`DotnetInspector.Queries.Tests` is the ninth migrated adopter. Its required
Linux and Windows PR commands and all three Deep Inspect platform lanes remain
unfiltered. Supported package-manifest and source-query instructions use MTP
method or class filters, while the workspace-scope instructions run each named
regression class separately so every selection receives its own aggregate
non-vacuity result. These paths reuse the pinned outcome-level host gate without
changing query, workspace, package, source, or acquisition evidence.

`DotnetInspector.Ecosystems.Tests` is the tenth migrated adopter. Its required
PR, Windows, Deep Inspect platform, and developer commands remain unfiltered.
These paths reuse the pinned outcome-level host gate without changing the
suite's friend-only registry and catalog evidence or the separately compiled
public-consumer evidence in `DotnetInspector.Ecosystems.Consumer.Tests`.

`DotnetInspector.Ecosystems.Consumer.Tests` is the eleventh migrated adopter.
Its required PR, Windows, Deep Inspect platform, and developer commands remain
unfiltered. These paths reuse the pinned outcome-level host gate without
changing the suite's separately compiled non-friend evidence or combining it
with the dedicated catalog suite.

`Inspector.Artifacts.Tests` is the twelfth migrated adopter. Its required PR,
Deep Inspect platform, and developer commands remain unfiltered. These paths
reuse the pinned outcome-level host gate without changing the suite's artifact
access, local admission, workspace-session, digest, or cleanup evidence.

`CSharpText.Tests` is the thirteenth migrated adopter. Its required PR, Deep
Inspect platform, and developer commands remain unfiltered. These paths reuse
the pinned outcome-level host gate without changing the suite's identifier,
signature, declaration, conditional-recovery, or layout evidence. Manual
decompiler fixture probes continue to inspect the built test assembly rather
than invoke its test host.

`DotnetInspector.RowSelection.Tests` is the fourteenth migrated adopter. Its
required PR and developer commands remain unfiltered. These paths reuse the
pinned outcome-level host gate without changing the suite's typed-language,
reference-evaluator, failure, or separately compiled non-friend consumer
evidence.

`DotnetInspector.Sections.Tests` is the fifteenth migrated adopter. Its
required PR and developer commands remain unfiltered. These paths reuse the
pinned outcome-level host gate without changing the suite's unresolved-intent,
cohort-binding, owner-identity, selection, or structured-failure evidence.

`ILInspector.Instructions.Tests` is the sixteenth migrated adopter. Its
required PR and developer commands remain unfiltered. These paths reuse the
pinned outcome-level host gate without changing the suite's instruction
decoding, block graph, typed-stack, metadata resolution, fidelity, comparison,
analysis-diff, Finding value-equality, or review-fix evidence.

`ILInspector.ILDiff.Tests` is the seventeenth migrated adopter. Its required PR
and developer commands remain unfiltered, while process-isolated signature and
metadata-graph safety workers select their child methods through MTP filters.
These paths reuse the pinned outcome-level host gate without changing the
suite's IL body and assembly comparison, normalization, member alignment,
Finding census and matching, compiler-generated ordinal, metadata-graph
safety, or diff presentation evidence.

`DotnetInspector.FixtureInfrastructure.Tests` is the eighteenth migrated
adopter. Its required PR command remains unfiltered. This path reuses the
pinned outcome-level host gate without changing the suite's fixture identity,
registration, grouping, artifact resolution, source and sidecar paths,
boundary-axis, or cross-assembly relationship evidence.

`InertText.Tests` is the nineteenth migrated adopter. Its required PR and
developer commands remain unfiltered. These paths reuse the pinned
outcome-level host gate without changing the suite's text-policy, containment,
lossless and injective encoding, visual-form, truncation, composition, URL
redaction, or public-surface evidence.

`runfaster.Tests` is the twentieth migrated adopter. Its required PR and
developer commands remain unfiltered. These paths reuse the pinned
outcome-level host gate without changing the suite's candidate lookup, byte
attribution, multiplicity, type confirmation, trace correlation, triage
validation, leak-watch, or end-to-end CLI evidence.

`DependencyPolicy.Tests` is the twenty-first migrated adopter. Its required PR
and developer commands remain unfiltered. These paths reuse the pinned
outcome-level host gate without changing the suite's strict schema, graph
classification, rule evaluation, non-vacuity, deterministic diagnostic,
MSBuild graph, assembly-closure, or fail-closed boundary evidence.

`DotnetInspector.MetadataRendering.Tests` is the twenty-second migrated
adopter. Its required PR and developer commands remain unfiltered. These paths
reuse the pinned outcome-level host gate without changing the suite's metadata
projection, structured rendering, `mdi`, containment, untrusted-text, reference,
or visible-failure evidence. The migration also retires the oversized-version
fixture harness: metadata admission now rejects its invalid version length
before rendering, and the Metadata owner separately gates that typed rejection.

`DotnetInspector.Presentation.Tests` is the twenty-third migrated adopter. Its
required PR, Windows, Deep Inspect platform, and developer commands remain
unfiltered. These paths reuse the pinned outcome-level host gate without
changing the suite's ecosystem-change report, clone-candidate, Library API
diff, Member source-diff, or detached inspection-envelope presentation
evidence.

`CSharpText.MemberSlicing.Tests` is the twenty-fourth migrated adopter. The
tracker called this project `DotnetInspector.CSharpBodySlicer.Tests` before its
rename in [#6340](https://github.com/richlander/dotnet-inspect/pull/6340). Its
developer command remains unfiltered. This path reuses the pinned outcome-level
host gate without changing the suite's declaration slicing, conditional
recovery, exact metadata-name correspondence, corpus breadth, or Roslyn
parse-validity evidence.

`ILInspector.CSharp.Tests` is the twenty-fifth migrated adopter. Its required PR
and developer commands remain unfiltered, while the documented hostile
metadata self-name probe uses the supported MTP method filter. These paths reuse
the pinned outcome-level host gate without changing the suite's model-bound C#
formatting, declaration, type-shell, memory-safety spelling, declared-self-name,
or visible-refusal evidence.

`Inspector.Text.Tests` is the twenty-sixth migrated adopter. The tracker called
this project `ILInspector.Text.Tests` before its rename in
[#6387](https://github.com/richlander/dotnet-inspect/pull/6387). Its developer
command remains unfiltered. This path reuses the pinned outcome-level host gate
without changing the suite's exact line census, bounded projection, text
comparison, movement, line-ending, final-terminator, or implementation-diff
relation evidence.

`HarnessReportDiff.Tests` is the twenty-seventh migrated adopter. Its direct
execution remains unfiltered. This path reuses the pinned outcome-level host
gate without changing the suite's stored-report identity, metric comparability,
residue endpoint, schema incompatibility, corpus-population, or Markdown, TSV,
and JSONL rendering evidence. The migration also retires a hard-coded corpus
snapshot schema assertion: input-derived corpus-path evidence gates schema
preservation and incompatibility, while separate corpus evidence gates
unknown-population refusal without coupling the suite to a mutable baseline
version.

`DotnetInspect.Web.Tests` is the twenty-eighth migrated adopter. The tracker
called this project `InspectWeb.Engine.Tests` before its move and rename in
[#6507](https://github.com/richlander/dotnet-inspect/pull/6507). Its required
browser-engine CI command remains unfiltered, while the documented Source
comparison procedure selects its managed operation class through an MTP class
filter. These paths reuse the pinned outcome-level host gate without changing
the suite's browser-host budgets, workspace and operation lifetimes, layering,
package and platform queries, source and implementation comparisons, managed
operation bridge, home demos, static-site publication, or structured wire
evidence.

`MsdlProxy.Tests` is the twenty-ninth migrated adopter. Its required managed-API
CI and developer commands remain unfiltered, while focused development uses MTP
class and method filters. These paths reuse the pinned outcome-level host gate
without changing the suite's MSDL symbol request validation, fixed upstream
authority, response bounds, failure mapping, package-change evidence routing,
or Function response-security evidence.

`DecompilerHarness.Tests` is the thirtieth migrated adopter. Its required
decompiler CI and developer commands remain unfiltered, while focused
development uses MTP class and method filters. These paths reuse the pinned
outcome-level host gate without changing the harness-owned ReturnToSender
compilation-closure evidence or the decompiler suite's separate gate presets
and completeness receipts.

`Inspector.Resources.Tests` is the thirty-first migrated adopter. Its required
resource-contract CI command remains unfiltered. This path reuses the pinned
outcome-level host gate without changing the suite's ownership-role attribute,
stack-only snapshot view, borrow lifetime, callback, failure-propagation, or
owner-release evidence.

`NetworkAccess.Tests` is the thirty-second migrated adopter. Its required
network-policy CI and developer commands remain unfiltered. This path reuses
the pinned outcome-level host gate without changing the suite-owned IPv4 and
IPv6 non-public destination-classification evidence used by independent
desktop transports.

`UntrustedDocuments.Tests` is the thirty-third migrated adopter. Its required
untrusted-document CI and developer commands remain unfiltered. This path
reuses the pinned outcome-level host gate without changing the suite-owned
duplicate-property, malformed-JSON, DTD, external-entity, decoded-character
budget, or ordinary parsing evidence.

`ILInspector.Decompiler.Tests` is the thirty-fourth and final migrated adopter.
Its custom entry point retains `--gate` preset expansion but sends every
ordinary execution through MTP. The suite's independent completeness receipt
uses MTP's JSON-RPC `testing/discoverTests` protocol for the discovery
reference, because MTP 1.9 deliberately disables user data consumers during a
discovery request, and an MTP `IDataConsumer` for execution lifecycle events.
Both surfaces carry the same `TestNodeUid`; the suite checker still requires
every discovered case to start exactly once, every expected class to execute,
and receipt outcomes to agree with the MTP xUnit report. The migration removes
`ExplicitFilterGuard` rather than reimplementing MTP selector semantics. All 47
repository xUnit executables now use MTP for direct execution.

The migrated executables run these outcome-level gates in their ordinary
Release test paths; suite-owned evidence remains additive where aggregate
non-vacuity is not the complete claim.

## Non-claims

This is an evidence-correctness boundary over trusted repository tests and
invocations, not a security boundary.

The host does not:

- validate that a test proves its stated property;
- infer which tests a workflow ought to select;
- own or define suite-specific argument expansion or execution receipts;
- promise that every value in a multi-value filter contributes a test;
- police trusted callers that deliberately opt out of MTP's zero-test result;
- parse workflow files, source text, prose, console output, or result XML;
- replace MTP or xUnit discovery, filtering, execution, or reporting;
- require `dotnet test` as the repository's supported correctness command; or
- add product dependencies or alter product platform compatibility.
