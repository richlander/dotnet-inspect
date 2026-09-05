# Source-oracle candidate ledger

## Status

Current design for the source-oracle candidate ledger tracked by
[#5841](https://github.com/richlander/dotnet-inspect/issues/5841).
`SourceOracleCandidateLedger` implements the contract. The production consumer
is the operator running `--source-oracle-candidates` and reading its card or
JSON report.

## Decision

`SourceOracleCandidateLedger` owns one decision: whether one
candidate-discovery run has enough evidence to publish denominator-complete
file verdicts and a deterministic next-enrollment ranking for every file it
conclusively qualifies against one accepted baseline.

Denominator-complete does not mean fully measured. A successful run may retain
unmapped targets and files whose source could not be acquired. It is complete
because every eligible target in the exact scanned assembly set remains
accounted for as attributed, unattributed, measured, or unmeasured; only
currently qualified files enter the ranking.

## Change classification and complexity

This design specifies an existing correctness-harness component. It adds no
product command, shared substrate, browser/Wasm path, or rendering mechanism.
The existing consumer is a deliberate operator mode in
`tools/DecompilerHarness`; no automated consumer of its JSON report exists.
The harness remains a tools host, so this design requires no browser enablement
plan or single-host substrate exception.

The ledger is a finite, deterministic transformation over one run's evidence.
It has no concurrent writer, retry protocol, distributed state, or scheduling
lifecycle, so a TLA+ model would add no useful contract evidence.

## Owner and boundaries

The owner is `SourceOracleCandidateLedger` in
`tools/DecompilerHarness/SourceOracleCandidateLedger.cs`.

It consumes:

- one accepted `AuthoredCorpusBenchmark` source-oracle report as the enrolled
  feature baseline;
- the exact identities of all assemblies supplied to the run;
- a complete pre-acquisition primary-document census for those assemblies;
- per-target acquisition and source-oracle evaluation outcomes; and
- current run provenance.

It returns either:

- one typed report with complete target accounting, file verdicts, and a
  deterministic ranking;
- the card or JSON projection of that same report; or
- a visible refusal without a partial ranking.

It does not own:

- Portable PDB mapping or primary-document selection;
- PDB, SourceLink, repository, or source-body acquisition;
- the meaning of real-method eligibility or authored-body extraction;
- Valid, Correct, or Printer-exact evaluation;
- syntax-feature vocabulary or inventory parsing;
- source-oracle manifest enrollment or perfection-gate policy;
- command-line argument policy; or
- card and JSON presentation conventions.

`ILInspector.Metadata` and `PdbSourceAcquisition` own mapping and acquisition.
`AuthoredSourceHarvest` owns authored-source harvesting.
`AuthoredCorpusSourceEvaluator` owns source-oracle evaluation.
`PrinterSyntaxInventory` owns feature extraction.
`AuthoredSourceOracleManifest` owns enrollment and its perfection gate.
`AuthoredCorpusHistoryStore` supplies provenance facts but does not impose its
committed-history admission contract on this measurement.

## Evidence currencies

The owner relates four currencies:

```text
AcceptedBaseline
  measured enrolled feature set
  enrolled source URL projection
  producer-reported provenance
  baseline text digest

CandidateDenominator
  exact scanned assembly identities
  complete primary-document census
  eligible real-method subset
  checksum-pinned per-run file identities

CandidateVerdicts
  every attributed file status
  every unattributed eligible target
  complete measured and unmeasured counts

RankedCandidates
  qualified, not-recognizably-enrolled files
  deterministic marginal feature gains
  complete deterministic order
```

The accepted baseline establishes measured enrolled features, not clean
repository history. The denominator establishes what this run attempted, not
every declaration physically present in a source file. A ranked report is an
operator observation, not enrollment authorization.

## Run admission and assembly-set integrity

The run is admitted only after current provenance is captured, the baseline is
accepted, and every supplied assembly contributes one usable identity and a
complete census. The assembly identity is name, version, MVID, and SHA-256;
local paths are execution inputs, not durable report identity.

The supplied assembly set is all-or-nothing. An unreadable or invalid assembly,
duplicate MVID, assembly with no real-method target, or failed PDB census
refuses the run rather than publishing a ranking for the remaining inputs.
Acquisition may then fail for individual target sources without shortening the
assembly set or denominator.

After acquisition and evaluation, at least one eligible target with a
checksum-pinned file identity must have reached evaluation. This distinguishes
a partial but informative measurement from a run that decided nothing.
`MeasurementIntegrity_FailsOnlyWhenNothingWasDecided` gates that lower bound.
The execution-level all-or-nothing composition is unverified; the implementation
returns before report construction when any per-assembly scan refuses.

## Denominator and file membership

The ledger consumes the complete Portable PDB MethodDef-to-primary-document
mapping before source acquisition. Primary-document selection is owner-issued:
`IsPrimaryDocument` descending, then `DocumentRowId` ascending. The ledger
intersects that census with the real-method targets without deriving membership
from successful harvest records.

Each mapped document with a resolved URL, recognized checksum algorithm, and
checksum contributes the per-run file identity:

```text
(sourceUrl, uppercase checksumAlgorithm, uppercase checksum)
```

That triple pins the source bytes used in this run and is the same identity
shape the enrollment manifest uses. Members sharing the triple are one
candidate file even when they come from multiple assemblies.

An eligible member with no primary-document mapping remains an unattributed
`no-pdb-source-mapping` row. A mapping without a complete checksum-pinned
identity remains an unattributed `no-immutable-source-identity` row. Neither
case invents file membership.

For an attributed file:

- `mappedMembers` counts every primary-document-mapped MethodDef;
- `eligibleTargets` counts the real-method subset;
- `evaluatedTargets` counts eligible targets that reached evaluation; and
- `unevaluatedMappedMembers` is `mappedMembers - evaluatedTargets`.

The final value deliberately includes mapped members outside the eligible
subset; it is scope disclosure, not a count of acquisition outages.
`QualifiedFile_PublishesItsUnevaluatedMappedMembers` gates this distinction.

Missing acquisition or evaluation remains inside the denominator.
`AcquisitionFailure_CannotLeaveAFileQualified` and
`MissingEvaluation_IsUnevaluableRatherThanAShorterDenominator` gate that
property. `UnmappedEligibleTarget_StaysExplicitAndInventsNoFileMembership`
gates unattributed accounting.

## Accepted baseline

The baseline is a current `AuthoredCorpusBenchmark` report, not a source-oracle
manifest. A manifest declares intended enrollment; only a completed benchmark
report can establish the syntax features the enrolled set actually observed.

The ledger accepts the report only when:

1. strict current-schema deserialization succeeds;
2. the benchmark's complete-input predicate recomputes successfully;
3. a source-oracle report is present, passed, non-vacuous, and retains no
   failures;
4. every registered file is Valid, Correct, inventory-tracked, and Printer
   exact at the supported versions;
5. every enrolled URL has supporting Correct and Printer-exact row evidence;
6. aggregate and per-file feature sets are nonempty where required, unique,
   ordinal-sorted, and have an exact union; and
7. producer counts do not contradict the row evidence.

These checks establish a measured enrolled feature set. They do not establish
that the baseline was built from a clean checkout, that its build revision
matched the then-current `HEAD`, or that its commit is on a particular branch.
Those producer-reported facts remain disclosure. The candidate report preserves
the baseline's date, commit, build source state, revision-match verdict, dirty
state, input digests, and enrolled URLs without promoting them into admission
requirements.

The baseline digest is SHA-256 over the decoded baseline text encoded as UTF-8.
It identifies the consumed text, not the original file encoding or a
repository-verified artifact. The local baseline path is not report data.

The `Baseline_*` tests gate the accepted measurement invariants.
`Provenance_IsDisclosedRatherThanAnAdmissionGate` gates the provenance
boundary.

## Enrollment correspondence

Candidate grouping uses the complete URL, checksum-algorithm, and checksum
triple. The accepted baseline's current per-file projection retains only source
URLs, so the ledger must not claim general full-triple correspondence across
runs.

A currently qualified file becomes `Enrolled` only when:

1. its source URL is present in the accepted baseline; and
2. `SourceLinkUrls` recognizes that exact URL as an immutable, commit-pinned
   GitHub or Azure DevOps content URL.

The recognized provenance grammar supplies the missing cross-run immutability
claim. A moving selector or unknown-host URL is insufficient even when its
current bytes match its PDB checksum; that file remains `Qualified` and is
ranked. This conservative duplicate may have zero incremental gain, but it
cannot hide changed content behind an unproved URL identity.

Baseline membership never hides current evidence. A baseline URL whose current
file is rejected or unevaluable retains that current verdict; `Enrolled` means
recognizably enrolled and qualified now.
`AlreadyEnrolledFile_IsReportedButNotRankedAsANextCandidate`,
`MutableBaselineUrl_DoesNotSuppressAQualifiedCandidate`, and
`BaselineMembership_DoesNotHideACurrentRejection` gate these properties.

## File verdicts

Each attributed file has exactly one status:

- `Enrolled`: currently qualified with recognized immutable baseline
  correspondence;
- `Qualified`: every eligible target was evaluated, Correct, Printer exact at
  the supported version, and inventory-parseable;
- `Rejected`: measurement completed and at least one structural, quality, or
  inventory requirement failed; or
- `Unevaluable`: at least one eligible target was not measured.

A file with no eligible target is structurally rejected rather than vacuously
qualified. An acquisition-family reason dominates the status because any
unmeasured eligible member prevents a conclusive file verdict. Rejected and
unevaluable files publish no rankable feature set.

Stable serialized reason codes retain all observed reason classes. The one
reported rejection family is a deterministic summary; it does not replace the
reason set. `EveryCandidateReason_HasAFamilyAndAStableCode` and the
classification tests gate those distinctions.

## Ranking

Only `Qualified` files enter the next-enrollment ranking. `Enrolled`,
`Rejected`, and `Unevaluable` files remain visible but unranked.

Ranking uses the conventional greedy maximum-coverage step:

1. seed covered features with the accepted baseline;
2. select the remaining file with the most currently uncovered features;
3. record that marginal feature set and add all selected features to coverage;
4. recompute every remaining marginal gain; and
5. continue through all qualified files, placing zero-gain files after every
   positive-gain file.

Equal marginal gain breaks by total feature count descending, eligible-target
count descending, source URL ordinal, checksum algorithm ordinal, then checksum
ordinal. Unranked report rows also use the complete file identity as their final
ordering keys. Input, dictionary, acquisition, and evaluation order therefore
cannot select a different report order.

Greedy maximum coverage supplies the useful marginal-gain convention. Its
fixed-budget approximation guarantee does not transfer: this ledger accepts no
selection budget, emits the full order, and makes no claim that a prefix is the
globally optimal enrollment set.

`GreedyRanking_IsDeterministicAndUpdatesIncrementalGain`,
`GreedyRanking_BreaksTiesDeterministically`,
`GreedyRanking_UsesTheCompleteFileIdentityAsTheFinalTieBreak`, and
`UnrankedFiles_AreOrderedByTheCompleteFileIdentity` gate the ranking and report
order.

## Provenance and archive semantics

The report discloses current and baseline provenance separately. For each run
it carries the UTC date, build commit, build source state, whether the build
commit matched the observed checkout, and whether that checkout was dirty.
Dirty or mismatched state does not refuse this measurement mode.

The version-2 JSON report is archiveable operator output. Its identities,
counts, statuses, reason codes, feature names, ranking, and disclosed provenance
remain readable without local paths. It deliberately omits authored source,
Printer source, diffs, PDB content, baseline text, and assembly bytes.

No report parser, complete-report verifier, or automated downstream consumer
exists. An archived report cannot replay its classifications, authenticate its
historical checkout, become a new source-oracle baseline, or authorize manifest
enrollment. Calling it durable evidence would overstate what survives archival;
the accepted benchmark report and source-oracle manifest remain separate owner
currencies.

`Json_ExcludesAuthoredAndPrinterSourceTextAndLocalPaths` gates the omission
boundary. The ledger schema version changes when the typed serialized shape
changes; version 2 adds the baseline revision-match disclosure.

## Failure and partial measurement

Ordinary findings are successful measurement data:

- no file qualifies;
- measured files are rejected;
- source acquisition leaves some attributed files unevaluable;
- eligible targets remain unattributed; or
- only a subset of checksum-identified files reaches evaluation.

The command refuses when it cannot preserve measurement integrity:

- current provenance cannot be captured;
- the baseline cannot be read or accepted;
- any supplied assembly cannot contribute its complete census;
- assembly, census, acquired identity, or evaluator correlation contradicts
  itself;
- the exact assembly set has no usable real-method denominator; or
- no checksum-identified target reaches evaluation.

Refusal publishes no partial ranking. A successful partial measurement
publishes every missing outcome explicitly rather than presenting absence as a
qualified or rejected result.

## Untrusted input and non-claims

Assemblies, PDBs, SourceLink maps, network source, and baseline JSON may be
untrusted. Their owning readers retain bounding, parsing, SSRF, attribution,
and checksum controls. The ledger consumes only checksum-verified source
outcomes and serializes no source body, preventing internet-origin text from
entering its report body.

The pure `Build` seam consumes cooperating typed census and evaluation values
constructed by the supported execution path. This contract does not add
defenses against deliberately contradictory in-process callers; command input
and producer correlation are the real boundaries.

The ledger does not claim:

- that every C# declaration in a candidate file was evaluated;
- that every target was measured in a successful run;
- that an attributed checksum proves repository provenance;
- that URL overlap proves enrollment outside the recognized immutable grammar;
- that the baseline or current checkout was clean;
- that a ranked file is authorized for enrollment;
- that a ranking prefix is globally optimal; or
- that archived JSON is replay-verifiable evidence.
