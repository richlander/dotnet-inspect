# Symbol document format pilot

A single author-run pass over the
[scenario claims](../../../../../docs/design/library-symbol-document.md#scenarios-and-claims),
run before any fresh-agent trial. One participant played both arms on
`System.Text.Json` 10.0.14: the **interactive** arm used production
dotnet-inspect `0.27.0+8ad7cfc` with the package cached, and the **document**
arm used `jq` over the [prototype](../prototype/README.md) output (SHA-256
`f0ee4d5a…`). Every call is in [`calls.jsonl`](calls.jsonl) with its wall time
and the bytes it returned after local filtering.

**This is not trial evidence.** The participant designed the schema and knew
several answers in advance, so the pilot measures what each path costs and
covers. It does not measure whether a fresh agent finds or chooses the
document. Its job is to find schema and claim problems before a trial spends
agent runs on them.

## First run

| Scenario | Interactive | Document | Reading |
| --- | --- | --- | --- |
| S1 Which APIs accept a `Stream`? | Name search finds nothing. Opening the likely Type gives 24 of 30 in 3 calls. Completeness takes one call per Type: 84 calls, 43 s, after 84 wasted calls on default output, which lists names without signatures. | 30 of 30 in 1 query, 0.03 s | Claim holds strongly |
| S2 What produces a `JsonDocument`? | Opening `JsonDocument` gives 8 of 13 in 1 call and misses `JsonSerializer.SerializeToDocument` ×5. A text grep over all 84 Types gives 12 of 13 with false positives and misses the `out` producer. | 13 of 13 in 3 queries, plus one false positive to judge (see F1) | Holds, with a schema fix |
| S3 Which `JsonSerializer` APIs require unreferenced code, beside safe overloads? | 1 call with `--json` and `jq`; identical answer | 1 query; identical answer | Tie as stated; see F5 |
| S4 Which converter Types exist, and what must a subclass override? | Type list grep finds all 7 converter-named Types; 2 more calls show the abstract members | The `Converter` suffix family finds 5 and misses `JsonConverterFactory` and `JsonConverterAttribute` | Claim fails as designed; see F3 and F4 |
| S5 Where is each `Stream`-taking `Deserialize` overload in source? | Member source fails with `ExternalProviderFailed` and exit 0 ([#11031](https://github.com/richlander/dotnet-inspect/issues/11031)). The fallback, Type-level SourceLink, a raw-file fetch, and reading, takes 4 calls and yields declaration lines, not body lines. | 5 of 5 file and line pairs in 1 query | Holds, confounded by #11031 |
| S6 All of the above as one session | Targeted paths: 12 calls, about 7 s, incomplete for S1 and S2. Complete paths: about 175 calls and 90 s. | Dump plus 10 queries: 0.4 s and about 11 KB into context, complete except S4 | Holds from the first cross-cutting question |
| S7 Control: `JsonDocument.ParseAsync` signature and summary | 1 call | 1 query, but only after a dump | Tie, as intended |

The prototype dump took 0.32 s to extract and 0.07 s to lay out, warm and with
local inputs; it is 1.7 MB in 1,312 lines. Each query read the whole file in
about 0.03 s. The product producer's cost is slice 1's to measure.

### Findings from the first run

- **F1. Mentions are too broad for "produces".** `return_type_members` lists
  `JsonMetadataServices.JsonDocumentConverter`, which returns
  `JsonConverter<JsonDocument>` and produces no `JsonDocument`. Mentions at any
  depth cannot tell a result (`Task<JsonDocument>`) from a wrapper. A field for
  the produced type, unwrapping `Task<T>` and `ValueTask<T>`, would answer S2
  in one query.
- **F2. `out` parameters produce values too.** `TryParseValue(…, out
  JsonDocument)` is a producer that `return_type` cannot see, and
  `parameter_type_members` mixes inputs with outputs.
- **F3. Suffix families do not answer "the family a Type lives in".** For
  compound names, the family is the last word only: `JsonConverterFactory` is
  in `Factory`. Records need the Type's full word list, which the name-family
  summary already produces, so that "contains the word `Converter`" is one
  select.
- **F4. Override questions need inheritance.** `JsonConverter`'s abstract
  `Type` and `CanConvert` are already implemented by `JsonConverter<T>`, so a
  subclass of `JsonConverter<T>` overrides only `Read` and `Write`. Neither
  arm answers that without reasoning; an `overrides` link from a Member to the
  base Member it overrides would.
- **F5. S3's advantage is scope, not the question.** Scoped to one Type, the
  interactive path ties. The claim should be the library-wide audit: 54
  Members across `JsonSerializer`, `JsonSerializerOptions`, and other Types.
- **F6. Part of S5's gap is a product bug.** The document answers because the
  prototype fetched the PDB directly. Once #11031 is fixed, the interactive
  path costs one call per Member group. The S5 claim should not rest on the
  failure.
- **F7. Production hides an acquisition failure behind exit 0.** The member
  source commands print a warning and succeed with no rows, so an agent could
  read the empty answer as "no source".

## Re-run after the fixes

F1 through F5 went into the design: `produced_types` and its lookup, which
also take `out` parameters; `name_words` and `name_word_types`; `overrides`
and `required_overrides`; and the library-wide audit claim. The prototype was
regenerated (SHA-256 `f2bc17ce…`, 2,009,522 bytes) and the document arm re-run
with fresh questions where the claims changed. Production dotnet-inspect did
not change, so the first run's interactive measurements stand, except S3,
whose library-wide wording was measured again. The calls are in
[`calls-rerun.jsonl`](calls-rerun.jsonl).

| Scenario | Interactive | Document | Reading |
| --- | --- | --- | --- |
| S1 `Stream` parameters | 84 calls for 30 of 30 (unchanged) | 30 of 30, 1 query | Holds |
| S2 Producers of `JsonDocument` | 12 of 13 at best, with false positives (unchanged) | 13 of 13, 1 query, no false positive | Holds |
| S3 Library-wide unreferenced-code audit | 84 calls, 43 s, 51 of 54: production omits accessor attributes, so `JsonSerializerOptions.Default`, `Web`, and `Strict` are missed | 54 of 54 in 20 Member groups across 7 Types, 1 query | Holds |
| S4 Converter Types and required overrides | 3 calls, then reasoning about which base members `JsonConverter<T>` already overrides | All 7 converter-named Types and each one's required overrides, 1 query | Holds |
| S5 Source set | 4 calls with the #11031 fallback (unchanged) | 5 of 5, 1 query | Holds; independent of #11031 by wording |
| S6 Session | About 260 calls and 135 s for complete answers | Dump plus 6 queries, under 0.6 s and about 8 KB into context | Holds |
| S7 Control | 1 call | 1 query after a dump | Tie, as intended |

### Findings from the re-run

- **F8. Documentation IDs are awkward to shorten.** Splitting an ID on `.`
  breaks inside parameter lists, so a first attempt at a readable S4 answer
  printed fragments. Joining back to the Member records for
  `display_signature` works in the same query. A trial should show whether
  agents find the join unprompted.
- **F9. `overrides` is empty when the base Member is outside the Library.** 8
  of 20 overriding Members, such as `ToString` overrides, show `is_override`
  with an empty `overrides`, as the design states. An agent could misread
  that as "overrides nothing".

The open question for a fresh-agent trial is unchanged: whether agents choose
the document unprompted for S1 through S6 and not for S7, and whether they
write these queries without the author's knowledge of the schema.
