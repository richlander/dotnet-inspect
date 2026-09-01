# Repository xUnit Test Host

## Status and owner

This document owns the repository xUnit executable host: the boundary between
an operator's test-run arguments and xUnit's generated runner entry point.
Issue [#5379](https://github.com/richlander/dotnet-inspect/issues/5379) tracks
the first implementation.

The host owns explicit-selection non-vacuity. It does not own test discovery,
test-case identity, filtering semantics, execution, reporting, or Microsoft
Testing Platform protocol behavior; xUnit retains those responsibilities.

## Problem

The repository uses focused xUnit invocations as evidence for narrow changes.
With the pinned xUnit v3 console runner, an explicit include selector that
matches no tests can complete successfully after reporting `Total: 0`. A
renamed, deleted, misspelled, or mutually disjoint selection can therefore
preserve a green command while removing the evidence the caller intended to
run.

Post-run XML checks cover only the invocation paths that remember to request
and parse that report. They do not establish the executable's contract and
cannot distinguish all selection failures without reproducing xUnit's model.

## Boundary

The host consumes:

- one repository xUnit test assembly;
- the exact argument vector supplied to that executable; and
- xUnit's discovery and selection model for that assembly.

It produces exactly one of these outcomes:

- **delegate** — transfer the original arguments to xUnit's generated entry
  point without changing runner behavior; or
- **refuse** — return a nonzero result with a diagnostic identifying an
  explicit selection that cannot produce the requested evidence.

The generated xUnit entry point remains the execution owner. Delegation must
preserve its console-runner and Microsoft Testing Platform dispatch behavior.

## Explicit-selection contract

The contract applies when the console invocation contains an explicit
inclusion by class, method, query filter, test-case identity, or serialized
test case.

Before delegation:

1. Every named class, method, or query inclusion must match at least one
   discovered test.
2. Every requested test-case identity must identify a discovered test selected
   by the other active filters.
3. Every serialized test-case selection must decode as a test case accepted by
   xUnit.
4. The combined selection must contain at least one runnable test after xUnit's
   explicit-test policy is applied.

These are semantic discovery obligations. The host must use xUnit's parser,
discovery, filter, identity, serialization, and explicit-test semantics rather
than infer selection from argument spelling, source text, test names, console
output, or result XML.

## Delegation and failure

Unfiltered runs, exclusion-only filters, help, discovery listings, assembly
information, and Microsoft Testing Platform server operation delegate without
acquiring a minimum-test-count requirement from this design.

Selection preflight must not consume or mutate the runner's execution state.
When discovery can materialize disposable theory data, preflight uses an
isolated process and releases that data before the real runner starts.

For an applicable explicit selection:

- a semantic mismatch is a visible refusal;
- failure to start or complete the isolated preflight is a visible refusal;
  and
- an argument or discovery failure already owned and reported by xUnit
  delegates so xUnit preserves its diagnostic and exit behavior.

The host does not turn an indeterminate applicable preflight into successful
zero-test execution.

## Convention and divergence

xUnit v3 conventionally owns its generated executable entry point, command-line
grammar, discovery, and execution. The repository preserves that owner and
delegates successful selections back to the generated entry point.

The pinned xUnit v3.2.2 in-process console runner has no exposed option that
makes an unmatched explicit selector fail; the repository probe for #5379
observed `Total: 0` with exit code `0`. Microsoft Testing Platform has a
separate zero-test exit contract, but repository correctness suites use direct
`dotnet run` console execution, and server dispatch must remain transparent.

The repository deliberately adds a stricter pre-delegation rule only for
explicit inclusions because those commands claim named evidence. It does not
make the stronger and different claim that every unfiltered or
exclusion-filtered run must execute a test.

The existing decompiler test host demonstrates the compatible precedent: it
uses xUnit discovery semantics in an isolated preflight, rejects unmatched or
empty combined selections, and otherwise retains xUnit runner behavior.

## Evidence

The pathological fixture is an ordinary test executable invoked with a method
selector that names no test. Before adoption it reports zero tests and exits
successfully. An adoption must make that invocation fail and must also
demonstrate a neighboring valid selector that executes normally.

The implementation gate for an adoption must cover:

- unmatched class, method, and query inclusions;
- an individually valid set of inclusions whose intersection is empty;
- missing and filter-disjoint test-case identities;
- invalid serialized test cases;
- explicit-only selection with no runnable explicit test;
- isolated-preflight startup or protocol failure;
- a valid explicit selection;
- an exclusion-only run; and
- delegation to the generated xUnit entry point.

The decompiler's current `ExplicitFilterGuardTests` enforce most semantic and
process-isolation cases. Until a repository-host adoption names its own
outcome-level integration gate, that adoption is **unverified**.

## Non-claims

This is an evidence-correctness boundary over trusted repository tests and
invocations, not a security boundary.

The host does not:

- validate that a test proves its stated property;
- infer which tests a workflow ought to select;
- require a minimum count for unfiltered or exclusion-only runs;
- parse workflow files, source text, prose, console formatting, or result XML;
- replace xUnit discovery, filtering, execution, or reporting;
- define Browser/Wasm product compatibility; or
- make `dotnet test` the repository's supported correctness command.

