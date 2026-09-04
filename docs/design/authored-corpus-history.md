# Committed authored-corpus history

## Status

Current design for the committed EVIL authored-corpus history tracked by
[#5788](https://github.com/richlander/dotnet-inspect/issues/5788).
`AuthoredCorpusHistoryStore` implements the admission, append, and complete-store
verification contract. The contract is exercised by the Decompiler test
executable and by the `Check EVIL history provenance` CI gate.

## Decision

Authored-corpus history owns one decision: whether one complete benchmark
artifact may become one durable EVIL history observation and whether the
ordered committed observation sequence is valid evidence for downstream
consumers.

Admission derives a compact row from the artifact's complete per-target
evidence. The append command accepts no operator-supplied metric, identity,
methodology, or provenance arguments. Complete-store verification later proves
the persisted shape, measurement invariants, and repository provenance that
remain reproducible from the row and repository. It cannot replay the
artifact-to-row projection or independently reproduce the report's date and
input digests or its clean build-and-run state after the full artifact and
inputs are unavailable.

## Change classification and complexity

This design specifies an existing correctness-harness component. Its named
consumers are:

- the CI provenance gate, which verifies the tracked store;
- the Deep Inspect EVIL benchmark, which uses the tracked store as its ratchet
  baseline; and
- the history card, which renders the observations and recent movement.

It adds no product capability, shared substrate, browser/Wasm path, command,
format, or rendering behavior. `tools/DecompilerHarness` is an existing focused
tools host, so this design creates no browser enablement plan or single-host
substrate exception. The history card continues to use Markout; this owner
neither defines nor bypasses that rendering path.

The design deliberately remains a small repository-backed observation log. It
does not add an event-sourced database, global event identifiers, optimistic
concurrency, upcasting, or a replay service. Those mechanisms solve concurrent
or distributed mutation and domain-state reconstruction, neither of which this
single-writer, Git-reviewed history performs.

## Owner and boundaries

The owner is `AuthoredCorpusHistoryStore` in
`tools/DecompilerHarness/AuthoredCorpusHistoryStore.cs`.

It consumes:

- one complete `AuthoredCorpusBenchmark` report containing the per-target
  evidence needed to derive the compact measurement;
- build and execution provenance for the harness that produced the report;
- the methodology version stamped by the benchmark owner;
- repository evidence resolving the recorded commit against `origin/main` and
  the methodology source at that commit; and
- the complete current history text after repository decoding.

It returns either:

- a verified ordered sequence of durable `HistoryRun` observations;
- that sequence plus one admitted canonical row appended to its store; or
- a visible refusal without returning a success-shaped sequence.

It does not own:

- corpus selection, benchmark execution, per-target classification, or report
  production;
- the meaning or lineage of an authored-corpus methodology;
- run comparability, baseline selection, metric direction, or regression
  policy;
- history-card tables, movement windows, or presentation;
- Git branch policy or CI workflow scheduling; or
- archival retention for full benchmark reports.

`AuthoredCorpusBenchmark` owns report production.
`AuthoredCorpusMethodology` owns attribution meaning and lineage.
`AuthoredCorpusRatchet` owns comparability and regression verdicts.
`AuthoredCorpusHistoryCard` owns rendering.
[Decompiler correctness](../decompiler-correctness-pipeline.md) owns the
surrounding correctness-gate role.

The current implementation shares identity-shape, known-methodology,
attribution-availability, and measurement-soundness predicates with the
ratchet path. History consumes those predicates as admission requirements; it
does not take ownership of the ratchet's baseline walk, metric selection, or
verdict policy. A change to a shared predicate must preserve both owners'
contracts rather than treating its current code location as sole authority.

## Durable currencies

The owner relates three currencies:

```text
CompleteBenchmarkArtifact
  producer-issued report schema
  complete per-target evidence
  build and run provenance
  corpus and selected-pool identities

AdmittedHistoryObservation
  artifact-derived compact measurement
  exact source commit
  methodology stamp
  corpus and selected-pool identities

VerifiedCommittedHistory
  ordered HistoryObservation sequence
  closed persistence schema
  repository provenance verdict
```

An admitted row is a projection, not a second benchmark report. It preserves
the stable aggregate evidence needed by its consumers and deliberately omits
the multi-megabyte per-target payload.

The committed sequence is the downstream trust currency. A list of
deserialized `HistoryRun` values is not equivalent: without complete-store
verification it carries neither the closed-schema verdict nor Git and
methodology provenance.

## Artifact admission

A candidate artifact is admissible only when all of the following hold:

1. The artifact has the benchmark owner's complete current report shape.
   Missing, duplicate, unknown, malformed, or null required members refuse
   admission.
2. The build revision equals the repository `HEAD` observed by the run, the
   build-time source state was clean, and the execution-time source tree
   remained clean.
3. The benchmark inputs are complete and the corpus and selected assembly pool
   carry well-formed full identities.
4. Every per-target classification is recognized and can be re-derived from
   its lower-level outcome, reason, compile-back, and fault-isolation evidence.
5. The recomputed census agrees with every producer summary and closes every
   required partition without negative counts or arithmetic overflow.
6. The recorded commit is a full immutable commit on `origin/main`, contains
   the benchmark, and implements the artifact's recorded methodology.
7. The complete existing store verifies, and the candidate row verifies as its
   final observation.

The owner recomputes the compact measurement from per-target evidence rather
than copying aggregate counts. A self-consistent producer summary cannot
override a contradictory row census. A partial or indeterminate report remains
available as benchmark output but cannot become committed history.

The artifact's date and corpus and pool digests are producer-reported facts.
Admission checks their required shapes and preserves them but does not re-read
the corpus or pool to reproduce them. Commit and methodology are also reported
by the artifact but gain the independent repository cross-check in step 6.

`ProducerReport_ProjectsToOneDeterministicCanonicalRow`,
`AppendProjection_RejectsRepresentativeArtifactTampering`,
`AppendProjection_RejectsUnknownEnumShapedFactsWithMatchingSummaries`, and
`GitRepository_RejectsBenchmarklessNonMainAndDuplicateMethodologyCommits` gate
these admission properties.

## Sequence identity and order

One physical line is one recorded observation. Its zero-based ordinal within
one exact committed history version is its observation address and ordering
currency. The format assigns no standalone global observation ID. Ordinary
append preserves every existing ordinal.

The date and commit are provenance, not uniqueness keys. Re-running one commit
on the same UTC date is a legitimate repeat observation, so equal date, commit,
input identity, and measurement values do not authorize deduplication. The
tracked store contains such repeated observations, and
`TrackedHistory_VerifiesUnchangedWithoutRequiringADeepCheckout` exercises the
complete sequence.

Append order is authoritative. "Last" means later in the file, not the greatest
date or commit. A consumer that needs a comparable baseline traverses that
sequence under its own comparison contract; this owner does not reorder rows or
select a baseline.

The owner is append-only for ordinary observations. Historical correction is a
reviewed repository migration, not an append operation pretending to reverse
or replace an earlier measurement. Any such migration must preserve an
explicit compatibility account and pass complete-store verification.

## Persistent representation

After repository text decoding, the store uses a strict JSON Lines profile:

- exactly one JSON object per non-empty line;
- LF line endings;
- exactly one final LF and no blank records; and
- a closed property inventory at every object level.

The general JSON Lines convention supplies UTF-8 text and permits any JSON
value, optional final line termination, and CRLF. The typed verifier receives
decoded text and does not independently prove byte encoding or byte-order-mark
absence. Its physical profile is intentionally stricter after decoding so
concatenation, review, and truncation have one canonical form.
`StoreVerifier_RejectsMalformedPhysicalFraming` gates that profile.

New rows use deterministic compact serialization and contain no derived helper
properties. The decoder rejects unknown and duplicate properties rather than
silently ignoring a measurement a newer writer may require.
`ProducerReport_ProjectsToOneDeterministicCanonicalRow`,
`StoreVerifier_RejectsDuplicateAndUnknownSchemaMembers`, and
`StoreVerifier_RejectsIgnoredComputedPropertyNames` gate those properties.

The field inventory and its measurement interpretation remain in the
[operational history guide](../../tools/DecompilerHarness/corpus/evil-runs/README.md#schema).
For optional members, absent and JSON `null` both mean **not recorded** and
canonical writing omits null. Neither state may be repaired to zero or a
current identity. `commit` and `invalidBreakdown` are physically required even
when their legal historical value is null. Derived sums and percentages must
reproduce the persisted base counts.

## Schema and methodology evolution

Persistence schema and benchmark methodology are independent:

- the persistence schema determines which facts a row carries and how they are
  encoded; and
- methodology determines what benchmark attribution evidence means.

`methodologyVersion` cannot stand in for a persistence-schema version. The
current repository-local format has no serialized schema discriminator.
Writer, verifier, and consumers evolve in one repository, while the verifier
recognizes the finite historical shapes that remain in the tracked file.
Future incompatible format changes must define a successor decoding contract
or a reviewed migration; they may not reinterpret old bytes through a new
methodology stamp.

Exactly one original observation is grandfathered by its complete expected
value. It may omit immutable commit provenance and partitions not retained in
its discarded artifact. Other historical rows may omit identities or
methodology fields that their generation did not record; absence stays unknown
and cannot be repaired to zero or a current identity. Once an identified row
records current corpus or pool identity, it must state its methodology.

Typed admission requires a full 40-hex commit. Complete-store verification also
accepts the 8- through 39-hex commit prefixes already used by historical
observations, resolves each prefix to a commit, and verifies that resolved
commit. An unresolvable or newly ambiguous prefix refuses the whole store; it
does not fall through to another commit or row.

Unknown methodology versions and fields claimed outside the methodology that
produced them refuse verification.
`StoreVerifier_RejectsMissingRequiredCountsAndGrandfatherTampering`,
`TrackedHistory_OnlyGrandfatheredRowsOmitThePartition`,
`TrackedHistory_OnlyTheOriginalRowOmitsACommit`,
`ParseHistory_RejectsUnknownMethodologyVersion`, and
`ParseHistory_RejectsFrontierAttributionBeforeMethodologyV3` gate the current
compatibility boundary.

## Append publication and failure

Append first verifies the current bytes, derives and verifies one candidate
row, and verifies the proposed combined sequence in memory. Only then may it
append the canonical row.

The owner recognizes one singleton history rooted at the exact original
2026-07-20 observation. `--history-path` relocates that history for append or
verification; it does not create a second history, bootstrap an empty store, or
select a different root.

The local append is not a transactional or concurrent-writer protocol. The
workflow admits one repository writer, and Git review exposes the resulting
diff. This owner makes no claim about process concurrency, same-machine actors,
filesystem mutation during the operation, or crash-safe atomic append.

Failure remains visible. An exception is not converted to success, and a torn
trailing write cannot be interpreted as the previously valid prefix because
strict final-LF and one-object-per-line verification rejects the complete file.
The operator repairs the working-tree file from the retained artifact or Git;
the owner does not silently truncate to the last valid row.

`AppendedProjection_PassesTheSameCompleteStoreVerifier` and
`StoreVerifier_RejectsMalformedPhysicalFraming` gate prepublication equivalence
and fail-closed subsequent verification. Crash atomicity itself is not claimed.

## Durable verification and trust

Complete-store verification proves, for every accepted row:

- strict physical framing and closed decoding;
- required field presence and legal historical absence;
- well-formed identities and known methodology;
- complete, nonnegative, closing measurement partitions;
- internally derived percentage agreement;
- commit or historical-prefix resolution, `origin/main` ancestry, and
  methodology agreement at the resolved commit; and
- exact preservation of the sole grandfathered observation.

`TrackedHistory_VerifiesUnchangedWithoutRequiringADeepCheckout` runs the typed
verifier over the tracked bytes with repository provenance supplied by a
pinned test double. The `Check EVIL history provenance` workflow step supplies
full Git history and is the gate for real commit resolution, `origin/main`
ancestry, and methodology-source agreement.
`EVIL_PROVENANCE_RUN_SHA256` makes a workflow edit visibly refresh its
change-detection acknowledgement.

Verification cannot prove that a stored aggregate was originally projected
from the discarded full report. That claim exists only during typed append,
where the artifact is present, and in the archived run evidence retained
outside the repository. An internally consistent hand-authored forgery could
therefore pass later structural and provenance verification. Repository review
and the append-only operational workflow are the residual control; durable
artifact-to-row authenticity is **not replay-verifiable**.

The verifier likewise cannot reproduce the report's date, clean build-and-run
state, or corpus and selected-pool digests from the unavailable original
artifact and inputs. Their producer construction and archived run evidence
remain outside durable store verification. This matters to consumers: the
ratchet owns whether persisted input identities make two observations
comparable.

## Consumer boundary

CI establishes the tracked file as a `VerifiedCommittedHistory` before it may
land. The history card and Deep Inspect ratchet consume that committed
repository artifact without rerunning Git provenance checks.

Their parsers and checks are purpose-specific:

- the history card preserves historical absence and rejects values that would
  fabricate rendered measurements; and
- the ratchet rejects malformed identity or methodology, skips unsound or
  incomparable observations under its own policy, and fails when a requested
  comparison reaches no verdict.

Neither path is an alternative complete-store verifier, and this owner does not
transfer its provenance claim to an arbitrary file supplied through
`--history-path`. A caller that needs the full trust currency must invoke the
complete verifier over the singleton history or consume the tracked file after
its CI gate.

## Conventional basis

[JSON Lines](https://jsonlines.org/) supplies the one-record-per-line,
UTF-8, and final-line-terminator conventions. The stricter physical profile is
owned above.

The
[Event Sourcing pattern](https://learn.microsoft.com/azure/architecture/patterns/event-sourcing)
supports immutable ordered observations but also warns that event sourcing
adds substantial concurrency, replay, and schema-evolution complexity. This
history is not a domain system of record and does not reconstruct state.
Sequence position is sufficient identity for its append-only measurement
observations, including legitimate repeats; distributed event IDs,
idempotent-delivery machinery, and optimistic concurrency are deliberately
omitted.

No TLA+ model is required. The owner has no autonomous coordination or
scheduling protocol: one local writer verifies one immutable input and one
existing finite sequence before appending, while Git and CI serialize
publication.
