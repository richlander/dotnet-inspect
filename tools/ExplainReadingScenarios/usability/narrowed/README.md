# Landed narrowing trials

Six fresh GPT-6 Sol/high participants answered the mainline package/API
questions and a body-kind vocabulary question with a short experimental skill
prototype. Package explanation supplied usable query knowledge; vocabulary
selection supplied a small complete answer once its root was found. The largest
remaining costs in these traces are bootstrap discovery and rereading locally
available facts, rather than the selected vocabulary representation.

This is descriptive evidence, not a controlled format ranking. There is one
participant per task/assigned format. Package Query's command-local terminal
is HAL-only, so the direct guide uses root facet reading. The API participants
use neither reading format. The direct vocabulary participant makes one
out-of-format HAL request. Preserve that violation rather than treating this
as a clean three-pair comparison.

## Inputs and boundaries

The executable is NativeAOT `osx-arm64` from landed source
`e701f56d4e3964ca47ed50c424b166ae7b4c9670` (merged guidance PR #10091), built
with SDK `11.0.100-rc.1.26425.128`. The Release solution build passed with zero
warnings/errors. Native publish used `-p:UseLdClassicXCodeLinker=false` and
completed with linker debug-module-cache warnings; no runtime timing claim is
made. [Dispatch](dispatch.json) pins source, SDK, executable hash, platform
DLL inventory, guides, tasks, protocol, preflight and per-trial inputs before
participants start. [Launches](launches.json) records the fresh dispatches.

The primary scenario is skill-informed reading, modeled here with a
version-current **experimental skill prototype**, not the shipped skill. The
HAL guide is 3,175 bytes and the direct guide 3,111. They teach command
orientation, identities, help, ordinary `-Q`, explain entrances, HAL vocabulary,
selected-scope interpretation, jq, local reuse and the prohibition of
verification-only reads. Only the assigned structured-reading instruction
differs. Shipped skill corrections remain queued on release tracker #8151;
these observations do not establish production transfer.

The [package task](package-task.txt), [API task](api-task.txt) and
[neutral retrospective](post-task-question.txt) are unchanged from the
[command-bound study](../command-bound/README.md). The new
[vocabulary task](vocabulary-task.txt) supplies two native value IDs but no
canonical root path. It exercises root discovery as well as selection; it is
not an unfamiliar-domain discovery test. [Criteria](criteria.txt) were fixed
before dispatch. No explanation entrance is mandatory. Package searches are
bounded to 40 candidates; API inspections use the pinned installed platform.
Live NuGet populations and warm shared caches are not a stable package snapshot
or a network benchmark.

The cooperative [recorder](runner.py) reuses the shared command-bound runner
without modifying any product response. Every original answer, request, response,
jq view and event log is frozen before the separate retrospective. The
[manifest](manifest.json) preserves inventory and hashes; it includes both the
successful readings and failed attempts. The recorder is not a security sandbox. Guide copies are archived as `.txt`
with their original bytes; the runner expects `guide.md` in a fresh scratch trial.

## Results

Bytes below include the supplied guide. Retrieved counts product stdout and
stderr; rendered counts recorded views, including repeated views and jq errors.
Neither counts tool receipts, wrappers, reasoning, API transport or tokens.

| Task/assignment | Requests | Retrieved + guide | Rendered + guide | Actual structured reading |
| --- | ---: | ---: | ---: | --- |
| Package / HAL | 4 | 39,186 | 20,765 | Full command-local HAL, then five selected facets |
| Package / direct | 6 | 20,134 | 17,229 | Two root direct facet reads |
| API / HAL | 6 | 36,238 | 33,922 | None |
| API / direct | 3 | 24,072 | 22,761 | None |
| Vocabulary / HAL | 19 | 43,150 | 35,395 | One selected HAL read |
| Vocabulary / direct | 24 | 46,665 | 31,572 | Selected direct, then redundant HAL |

[Measurements](measurements.json) separate guidance from ordinary inspection,
record requested entrances and **successfully emitted** reading profiles, and
retain four failed requests, one jq error and one format violation. There are
no `.contract`, selected-response collection-href, or `-E` requests. The only
primary `--explain` use is the package HAL participant's two calls. Ordinary
query help supplies paths to the package direct reader; both vocabulary readers
use root explain. This establishes entrance use in these traces, not general
adoption rates.

The [author assessment](assessment.json) finds the six domain answers adequate
with explicit evidence limits. Both package readers establish one direct
manifest dependency: `Microsoft.Extensions.Http.Polly` version `10.0.13`, Polly
range text `7.2.4`, selected `.NETStandard2.0` group for `net10.0`, and
`CandidateLimitReached` at 40. Range text is a declaration, not a resolved Polly
version. Both API readers give inspected `GetFromJsonAsync`, `PostAsJsonAsync`
and `ReadFromJsonAsync` signatures with their actual `HttpClient`/`HttpContent`
receivers, declaring types/library and installed-runtime qualification. Neither
API trace tests explain-format usability. Vocabulary readers report 72 catalog
values, the exact IDs and selected-scope omission correctly; individual meanings
are qualified interpretations of the IDs because the catalog has no prose
value definitions.

## What the traces show

1. **Narrowing works after bootstrap.** The final two-value response is 656
   bytes in HAL and 540 in direct JSON. Both readers understand that omitting
   `BreakStatement` from an `ObjectCreationExpression` selection does not make it
   invalid. These small terminal answers do not account for the preceding
   discovery requests.
2. **The vocabulary route is hard to discover from this guide.** Searches for
   body-kind/value names return no matches. The HAL reader eventually infers
   `explain vocabularies --json`, copies the collection's root and narrows once.
   The direct reader infers an invalid vocabulary path and gets a useful exact
   suggestion. `explain --help` fails without an operand in both traces; help
   with an operand works. These are preserved bootstrap observations, not a
   claim that capability search promises all installed vocabulary values.
3. **Selection does not replace local reuse.** The package HAL reader first
   fetches all 19 facets, then fetches five already available records and the
   same binding rules. Its first jq projection asks for nonexistent properties
   such as `description` and `fields`. Correct local selection of the existing
   response supplies the needed rules without that 7,561-byte second request.
4. **A second format is not additional knowledge.** After reading all 540 direct
   bytes, the vocabulary direct reader requests HAL to look for descriptions
   or identity details. The retrospective acknowledges that HAL repeated the
   facts and provided no prose definitions. Both representations had already
   supplied the native identities, count and Break display name. This is an
   unnecessary read and an assigned-format violation.

A useful next experiment is a minimal vocabulary-collection bootstrap gesture
and a more concrete reuse rule: use `-Q` on a needed first request; after saving
a complete response, narrow it locally with jq. For equivalent reading
projections, changing representation does not reveal new domain facts. Keep
any guide revision and fresh follow-up trials separate from these frozen
outcomes. Nothing here supports abandoning HAL or retiring the broader skill.

## Worked local-reuse demo

The actual package participant already had the full HAL response saved. This
query preserves all five facet records and narrows the binding's exposed keys:

```bash
jq -c -f worked-cache-selection.jq package-hal/response-02.out
```

The [jq program](worked-cache-selection.jq) yields exactly the same
[reading facts](worked-cache-selection.json) from its later selected
`response-03.out`. The replay gate compares both outputs byte-for-byte. This
avoids another product request; it does not implement general required-context
closure or substitute for product selection when those facts are missing.

For a first vocabulary read, the installed collection supplies the root before
native-ID narrowing:

```bash
dotnet-inspect explain vocabularies --json
# Copy the returned path, then request only the known IDs:
dotnet-inspect explain vocabularies/csharp.body-kinds .hal --json \
  -Q ObjectCreationExpression,BreakStatement
```

The final command is retained in the actual HAL trace and preflight. A direct
first read uses `.data` instead. Both include the catalog count and complete
selected population without fetching their collection link.

## Replay

From the repository root:

```bash
python3 tools/ExplainReadingScenarios/usability/narrowed/verify.py
```

The offline gate validates pinned inputs, exact inventories, original freezes,
unmodified stdout, preflight exit/byte/hash receipts, replayed jq and diagnostics,
serial read/request ordering, format observations, separate retrospective
question identity, byte arithmetic and the cache-selection demo. Domain scoring
remains the explicit assessment against the original executed evidence.
