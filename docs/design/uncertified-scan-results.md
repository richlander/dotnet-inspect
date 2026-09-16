# Uncertified scan results

**Owner:** the CLI command layer. **Consumer:** `depends`.

This document owns one contract: how a command reports a scan that lost a
candidate. It defines the pattern only. It does not define which candidates get
excluded, or why — that belongs to the owner that refused them, currently the
metadata format admission contract in
[`metadata-primitives.md`](../metadata-primitives.md).

## The problem

A command that scans several candidate assemblies can lose one of them while
retaining an outcome from the others. The excluded candidate might have
contributed more, so the command must qualify that outcome rather than present
it as complete.

Here, certification refers only to reported candidate coverage. The CLI does
not establish metadata validity or independently prove the soundness of
retained edges. The producing owner's decoder and resolver limits still apply,
including the unreported generic-context limitation tracked by #5856.

Two opposite failures are available here, and both are wrong:

- **Success-shaped output.** Present the partial result as a complete one. The
  caller consumes a graph or an absence that quietly omits a participant. This
  is the failure the repository-wide "keep failure visible" constraint forbids.
- **Withheld output.** Treat the uncertainty as a failure and emit nothing. The
  caller loses evidence the command actually has, including answers that never
  depended on the excluded candidate at all.

The second failure is the less obvious one, and it is easy to introduce while
fixing the first.

## Contract

1. **Name every exclusion before the result.** Each excluded candidate and the
   mechanism that excluded it is written to standard error, ahead of any result
   or absence claim, so the qualification is never separated from what it
   qualifies.

2. **Uncertainty travels beside the outcome, never replaces it.** A scan
   reports what it found and whether a candidate was excluded as two separate
   values. Folding the second into the first destroys the outcome the caller
   dispatches on.

3. **Exit code `3` marks a qualified scan outcome.** The scan returned a result
   or an absence with at least one reported exclusion. It does not certify the
   validity of retained evidence. A scan without reported exclusions keeps its
   ordinary result status; an operation that fails to produce an outcome keeps
   its error status.

4. **An independent answer keeps its own status.** An answer that did not
   consult the excluded candidate — for example one resolved by name through a
   path that never reads the candidate list — keeps its own exit code. Only a
   result from the affected scan carries that scan's qualification.

5. **An absence claim is never certified once a candidate was excluded.**
   "Not found" asserts completeness by construction, so an exclusion always
   qualifies it.

6. **Qualification does not depend on output format.** Exclusion warnings stay
   on standard error and the exit status qualifies the scan outcome regardless
   of the selected result format. This contract does not add metadata-validity
   evidence or a new structured-output schema.

Rule 2 is the load-bearing one. Rules 4 and 5 are its two consequences, and
they are the ones an implementation gets wrong: collapsing uncertainty into the
exit code can suppress either the caller's absence diagnosis or an otherwise
eligible fallback.

An explicit source option, such as `--library`, makes the positional argument
unambiguously a type. It does not permit the library-name fallback. Examples
and gates must preserve that search-scope contract rather than rely on the
earlier fallback behavior.

## Why a distinct exit code

An uncertified result is not a success and not an error, and a caller
scripting against the command needs to tell the difference without parsing
standard error. Reusing `0` hides the qualification; reusing an error code
conflates an incomplete outcome with failure to produce one. The cost is that
`3` is a new value callers must learn, which is why the pattern is adopted one
command at a time rather than applied everywhere at once.

## Adoption

`depends` is the only adopter. Other commands that scan multiple candidates
have not adopted the pattern and continue to behave as they did; this document
does not claim otherwise, and no gate requires universal adoption. A command
adopts it when a concrete report or defect shows that its scan can lose a
candidate silently.

Delivery is step 2 of 2 in #4877: #5631 supplies shared Metadata admission and
direct-consumer transport, and #5632 supplies this CLI-owned presentation.
The shared admission step also serves the existing Browser/Wasm query paths;
this step does not add a browser dependency-scan presentation.

## Gates

- `CommandExecutionTests.Depends_NamesAnExcludedUnsupportedAssemblyInsteadOfReportingACleanPartialGraph`
  — rules 1–3: the exclusion is named, the healthy neighbor still resolves,
  and the exit code is `3`.
- `CommandExecutionTests.Depends_DoesNotCertifyAbsenceWhenACandidateWasExcluded`
  — rule 5: a not-found answer is delivered *and* qualified, and its diagnosis
  still reaches standard error.

The former `Depends_ExcludedCandidateDoesNotWithholdAnIndependentLibraryAnswer`
gate was removed because its explicit `--library` input no longer permits
fallback. Rule 4 with a preceding exclusion, warning-before-result ordering,
and rule 6's cross-format parity are `unverified` by the focused gates above.
The library-fallback path also emits a tree under `--json`; that pre-existing
formatting limitation is not fixed by this exclusion-reporting contract.
