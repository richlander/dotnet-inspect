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
pinned xUnit v3.2.2 package, MTP v1 is already present; selecting it does not
require a new direct package reference or repository test-host library.
`dotnet run` remains the canonical repository command because the test project
is still an executable.

The repository does not depend directly on xUnit runner implementation
libraries to enforce selection non-vacuity. A future xUnit package update may
change the packaged MTP major version, but it must preserve this document's
minimum-count and visible-failure contract before adoption.

## Evidence

The pathological fixture is an ordinary test executable invoked through MTP
with a method filter that names no test. With the pinned xUnit v3.2.2 package,
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
`dotnet-inspect.Tests`. It starts the built test apphost and covers the
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

If the selected MTP version cannot produce the independent discovery and
execution identities required by the decompiler completeness receipt, that
suite remains on its transitional host until its owner has an equally strong
MTP-backed receipt. Aggregate MTP adoption elsewhere does not authorize
weakening or removing that gate.

Until a repository adoption names and runs these outcome-level gates, the MTP
host contract is **unverified**.

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
