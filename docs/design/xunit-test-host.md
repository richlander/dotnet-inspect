# Repository xUnit Test Host

## Status and owner

This document owns the repository xUnit executable host: the boundary between
the argument vector ready for xUnit and xUnit-owned runner dispatch.
Issue [#5379](https://github.com/richlander/dotnet-inspect/issues/5379) tracks
the first implementation.

The host owns explicit-selection non-vacuity. It does not own test discovery,
test-case identity, filtering semantics, execution, reporting, or Microsoft
Testing Platform protocol behavior; xUnit retains those responsibilities.
Suite-specific argument expansion, such as the decompiler's `--gate` presets,
also remains with that suite and runs before this boundary.

## Problem

The repository uses focused xUnit invocations as evidence for narrow changes.
With the pinned xUnit v3 console runner, an explicit include selector that
matches no tests can complete successfully after reporting `Total: 0`. A
renamed, deleted, misspelled, or wholly or partially disjoint selection can
therefore preserve a green command while removing evidence the caller intended
to run.

Post-run XML checks cover only the invocation paths that remember to request
and parse that report. They do not establish the executable's contract and
cannot distinguish all selection failures without reproducing xUnit's model.

## Boundary

The host consumes:

- one repository xUnit test assembly;
- the exact argument vector that will be delegated to xUnit, after any
  suite-owned expansion; and
- xUnit's discovery and selection model for that assembly.

It produces exactly one of these outcomes:

- **delegate** — transfer that vector to xUnit-owned runner dispatch without
  changing runner behavior; or
- **refuse** — return a nonzero result with a diagnostic identifying an
  explicit selection that cannot select the requested evidence.

xUnit remains the execution owner. Delegation must preserve its console-runner
and Microsoft Testing Platform dispatch behavior.

## Explicit-selection contract

The host first classifies whether xUnit will execute tests or perform a
non-execution operation such as help, listing, assembly information, or
Microsoft Testing Platform server dispatch. The contract applies only to a
test-execution invocation whose delegated console argument vector contains an
explicit inclusion by namespace, class, method, trait, query filter, test-case
identity, or serialized test case.

The **witness set** is the set of test cases xUnit would submit to its execution
path after filter, direct-selection, identity-resolution, and explicit-test
policy. Static or dynamic skips, cancellation, stop-on-failure, and test
outcomes do not change preflight membership in that set.

Before delegation:

1. Every named namespace, class, method, or trait inclusion, and every query
   selection, must retain at least one matching case in the witness set.
2. Every requested test-case identity must identify a discovered case selected
   by the active filters and admitted to the witness set, matching xUnit's
   filtered identity resolution.
3. Every serialized test-case selection must decode as a test case admitted to
   the witness set under xUnit's direct-selection explicit policy. Filters do
   not apply to a serialized selection when xUnit does not apply them.
4. The witness set must contain at least one test case.

These are semantic discovery obligations. The host must use xUnit's parser,
discovery, filter, identity, serialization, and explicit-test semantics rather
than infer selection from argument spelling, source text, test names, console
output, or result XML.

A query argument is one atomic explicit selection, including a query composed
only of negation. The host does not parse a query into positive and negative
subclaims or reproduce xUnit's query grammar. The exclusion-only category below
is limited to xUnit's simple negative namespace, class, method, and trait
switches.

## Delegation and failure

Help, discovery listings, assembly information, and Microsoft Testing Platform
server operation are non-execution modes and delegate before selection
preflight. Unfiltered test execution and runs containing only simple negative
namespace, class, method, or trait switches also delegate without acquiring a
minimum-test-count requirement from this design. Suite-owned argument expansion
occurs before this partition, so an expansion that produces an explicit xUnit
selection is subject to the same contract as a directly supplied selection.

Direct selection changes the set xUnit executes. Serialized `-run` cases bypass
the ordinary filters, while `-id` cases resolve through them. The host preserves
that asymmetry:

- without `-run` or `-id`, the witness set contains the filter-selected
  discovered cases admitted by xUnit's explicit-test policy;
- with either direct selector, the witness set contains only valid deserialized
  `-run` cases plus filter-selected discovered cases named by `-id`, after
  xUnit's direct-selection explicit policy; ordinary discovered cases are not
  added; and
- every separately named simple inclusion or query selection must match a case
  in that final witness set.

A direct case does not make an unrelated named inclusion non-vacuous merely
because some other test will run.

Selection preflight must not consume or mutate the runner's execution state.
When discovery can materialize disposable theory data, preflight uses an
isolated process and releases that data before the real runner starts.

For an applicable explicit selection:

- a semantic mismatch is a visible refusal;
- failure to start or complete the isolated preflight is a visible refusal;
  and
- an argument or discovery failure already owned and reported by xUnit may
  delegate only when xUnit's resulting runner path fails visibly.

An adoption must gate that delegated xUnit-owned failures return nonzero. The
host does not turn an indeterminate applicable preflight into successful
zero-test execution.

## Convention and divergence

xUnit v3 conventionally owns its command-line grammar, discovery, execution,
and console and Microsoft Testing Platform dispatch. The repository preserves
that owner and delegates successful selections without changing those paths.

The pinned xUnit v3.2.2 in-process console runner has no exposed option that
makes an unmatched explicit selector fail; the repository probe for #5379
observed `Total: 0` with exit code `0`. Microsoft Testing Platform has a
separate zero-test exit contract, but repository correctness suites use direct
`dotnet run` console execution, and server dispatch must remain transparent.

The repository deliberately adds a stricter pre-delegation rule only for
explicit inclusions because those commands claim named evidence. It does not
make the stronger and different claim that every unfiltered or
exclusion-filtered run must execute a test.

The repository also treats each query argument as one atomic selection, even
when the query is wholly negative. This is deliberately stricter than the
equivalent simple negative switches: the xUnit runner exposes the query as one
selection expression, and classifying its internal polarity would require the
host to reproduce query-language semantics it does not own.

The existing decompiler test host demonstrates the compatible process-isolated
preflight and xUnit-semantic approach. Its current selector coverage is only a
partial precedent; the repository contract also closes namespace, trait, and
partially disjoint inclusion paths.

## Evidence

The pathological fixture is an ordinary test executable invoked with a method
selector that names no test. Before adoption it reports zero tests and exits
successfully. An adoption must make that invocation fail and must also
demonstrate a neighboring valid selector that reaches normal xUnit execution.

The implementation gate for an adoption must cover:

- unmatched class and method inclusions and query selections;
- unmatched namespace and trait inclusions;
- a wholly negated query that contributes no case to the witness set;
- simple negative switches that intentionally select no test and still
  delegate;
- individually valid inclusions whose full intersection is empty;
- a nonempty combined selection in which any named inclusion has no final
  witness;
- missing and filter-disjoint test-case identities;
- invalid serialized test cases and serialized cases excluded by xUnit's
  direct-selection explicit policy;
- a serialized test case that xUnit runs without applying an otherwise active
  filter;
- a direct selection that leaves a separately named inclusion without a case
  in the witness set;
- explicit-only selection with an empty witness set;
- isolated-preflight startup or protocol failure;
- delegated xUnit-owned parse or discovery failure;
- a valid explicit selection;
- an exclusion-only run; and
- unchanged xUnit console and Microsoft Testing Platform dispatch.

The decompiler's current `ExplicitFilterGuardTests` enforce most semantic and
process-isolation cases. Until a repository-host adoption names its own
outcome-level integration gate, that adoption is **unverified**.

## Non-claims

This is an evidence-correctness boundary over trusted repository tests and
invocations, not a security boundary.

The host does not:

- validate that a test proves its stated property;
- infer which tests a workflow ought to select;
- own or define suite-specific argument expansion;
- require a minimum count for unfiltered or exclusion-only runs;
- parse workflow files, source text, prose, console formatting, or result XML;
- replace xUnit discovery, filtering, execution, or reporting;
- define Browser/Wasm product compatibility; or
- make `dotnet test` the repository's supported correctness command.
