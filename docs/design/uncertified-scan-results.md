# Uncertified scan results

**Owner:** the CLI command layer. **Consumer:** `depends`.

This document owns one contract: how a command reports a scan that lost a
candidate. It defines the pattern only. It does not define which candidates get
excluded, or why — that belongs to the owner that refused them, currently the
metadata format admission contract in
[`metadata-primitives.md`](../metadata-primitives.md).

## The problem

A command that scans several candidate assemblies can lose one of them. The
surviving evidence is still sound: every edge, row, or match it produced is
real. What is unknown is whether the answer is *complete*, because the excluded
candidate might have contributed more.

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

3. **Exit code `3` marks an uncertified result.** It says: output was produced
   and is sound, but completeness is unknown. It is distinct from success (`0`),
   from a certified not-found (`1`/`2`), and from an error.

4. **An independent answer stays certified.** An answer that provably did not
   consult the excluded candidate — for example one resolved by name through a
   path that never reads the candidate list — keeps its own exit code. Only a
   claim whose truth depends on having seen every candidate is uncertified.

5. **An absence claim is never certified once a candidate was excluded.**
   "Not found" asserts completeness by construction, so an exclusion always
   qualifies it.

6. **No stream disagrees with another.** Structured output presents an
   uncertified scan exactly as the human-readable output does, and no stream
   promises output that another does not deliver.

Rule 2 is the load-bearing one. Rules 4 and 5 are its two consequences, and
they are the ones an implementation gets wrong: collapsing uncertainty into the
exit code suppressed a caller's fallback dispatch, withholding an answer that
rule 4 says is certified.

## Why a distinct exit code

An uncertified result is not a success and not an error, and a caller
scripting against the command needs to tell the difference without parsing
standard error. Reusing `0` re-creates success-shaped output; reusing an error
code discards a sound result. The cost is that `3` is a new value callers must
learn, which is why the pattern is adopted one command at a time rather than
applied everywhere at once.

## Adoption

`depends` is the only adopter. Other commands that scan multiple candidates
have not adopted the pattern and continue to behave as they did; this document
does not claim otherwise, and no gate requires universal adoption. A command
adopts it when a concrete report or defect shows that its scan can lose a
candidate silently.

## Gates

- `CommandExecutionTests.Depends_NamesAnExcludedUnsupportedAssemblyInsteadOfReportingACleanPartialGraph`
  — rules 1 and 3: the exclusion is named, the healthy neighbor still resolves,
  and the exit code is `3`.
- `CommandExecutionTests.Depends_DoesNotCertifyAbsenceWhenACandidateWasExcluded`
  — rule 5: a not-found answer is delivered *and* qualified, and its diagnosis
  still reaches standard error.
- `CommandExecutionTests.Depends_ExcludedCandidateDoesNotWithholdAnIndependentLibraryAnswer`
  — rules 2 and 4: an answer that did not consult the excluded candidate is
  delivered with its own exit code.

Rule 6 is `unverified` for structured output: `--json` on the library-fallback
path emits a tree rather than JSON, which is pre-existing behavior on `main`
and is not addressed here.
