# Encyclopedia-shaped run, 2026-11-06

Four fresh GPT-6 Sol (high) participants answered the six
[encyclopedia-shaped questions](../task.txt) under the [fixed design](../README.md):
two with dotnet-inspect 0.27 alone and two that also had the prototype
document. Every original answer, receipt, response, view, and event log was
frozen before the [neutral question](../post-task-question.txt); each
participant's reply is its `post-task.md`.

The document participants made no product requests, so `calls.jsonl` did not
exist for them; an empty one was created before freezing because `freeze.py`
requires it. Three raw responses over 256 KB are omitted from the repository
and pinned in [`omitted.json`](omitted.json); every view read from them is
kept, so `freeze.py --check` passes only on the original directories. The
participants ran from the session scratch directory rather than `/tmp`; their
transcripts contain no read outside the recorder.

## Results

| | interactive-1 | interactive-2 | document-1 | document-2 |
| --- | --- | --- | --- | --- |
| Q1 `Stream` parameters (30) | 30 | 30 | 30 | 30 |
| Q2 `JsonDocument` producers (13) | 13 | 13 | 13 | 13 |
| Q3 `RequiresUnreferencedCode` groups (20) | 17, plus 1 false positive | 17, plus 1 false positive | 20 | 20 |
| Q4 `ParseAsync` signature and summary | correct | correct | correct | correct |
| Q5 converter derivability and overrides | **correct** | `JsonConverter` wrongly derivable | `JsonConverter` wrongly derivable | `JsonConverter` wrongly derivable |
| Q6 `Stream` `Deserialize` source (5) | 5, declaration lines | 5, declaration lines | 5, first-statement lines | 5, first-statement lines |
| Product requests (failed) | 39 (3) | 44 (8) | 0 | 0 |
| Views | 56 | 57 | 16 | 20 |
| Bytes read into context | 139,471 | 140,336 | 90,767 | 67,106 |
| Raw product output retrieved | 12.5 MB | 2.3 MB | none | none |
| Wall time, launch to exit | 312 s | 246 s | 100 s | 108 s |
| Claimed complete | all six | all six | all six | all six |

Every participant claimed all six answers complete. Q3's interactive answers
and three Q5 answers were not.

## What happened

**The interactive arm found its own dump.** Neither interactive participant
opened Types one by one, as the pilot assumed. Both discovered that
`type * --package System.Text.Json@10.0.14 -S Classes,Structs,Interfaces,Delegates --json`
returns the whole public surface as one 2.1 MB JSON document, and answered Q1
through Q3 with `jq` over it; interactive-1 later fetched 9.6 MB with `--all`.
Both also found `member … --source-parts` for Q6, which avoids
[#11031](https://github.com/richlander/dotnet-inspect/issues/11031). The real
interactive baseline is therefore a production batch path, not 84 calls per
question, and the pilot's S1 and S6 costs overstate the interactive arm.

**The document arm was faster, cheaper, and more correct on Q3.** With no
product requests, the document participants finished 2.3 to 3.1 times faster in wall
time and read 35 to 52 percent fewer bytes. Their Q3 answers were complete; both
interactive participants missed `JsonSerializerOptions.Default`, `Web`, and
`Strict`, because production reports a property's own attributes but not its
accessors', and both counted `DefaultJsonTypeInfoResolver.GetTypeInfo`, whose
only attribute is `UnconditionalSuppressMessage("…IL2026:RequiresUnreferencedCode…")`,
because production attributes are text and a substring matched. Typed
`attribute_types` avoided both errors.

**The document misled on Q5.** `JsonConverter` has only an internal
constructor, so no one outside the library can derive from it. The document
lists `required_overrides` for it and, under `public_api` admission, simply
has no constructor records, so both document participants, one after
explicitly querying constructors, called it derivable. interactive-1 asked
`member JsonConverter --all --ctor`, saw the assembly-only constructor, and
answered correctly. interactive-2 made the same mistake from the public
inventory.

**Schema discovery worked without recipes.** Both document participants read
`keys` and a sample record, then used `parameter_type_members`,
`produced_type_members`, `attribute_type_members`, and `name_word_types` by
name from their third or fourth read. document-2 first spelled lookup keys
without the `T:` prefix, got empty results, and corrected. No participant
tried to shorten documentation IDs, so pilot finding F8 did not recur.

## Findings for the design

- **R1. State derivability.** Absence of a non-admitted constructor is
  indistinguishable from absence of any constructor. The Type record needs an
  explicit fact, such as whether a class can be derived from outside the
  Library, and `required_overrides` should be read only when it is true.
- **R2. The comparison is batch against batch.** Production already offers a
  whole-library JSON through `type *`. The document's advantages in this run
  were typed attributes, accessor-aware audits, produced types, and lookups,
  plus speed and size; the claims and the adoption trial should be stated
  against that path, not against per-Type calls.
- **R3. Production's `type *` path has the Q3 defects.** Accessor attributes
  are missing and attributes are untyped text. Both deserve issues against
  the Type document owner regardless of the symbol document.
- **R4. Completeness claims are unreliable in both arms.** Every participant
  claimed every answer complete, including the wrong ones. The document's
  `section_outcomes` did not prevent overclaiming on a question its records
  could not decide.
