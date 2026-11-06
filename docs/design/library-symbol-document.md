# Library symbol document

## Status and owner

Status: **proposed design** for
[#11028](https://github.com/richlander/dotnet-inspect/issues/11028). No
producer or host gesture is implemented.

The **Library Symbol Document** owner defines this claim:

> Given one exact Library and one resolved request, write one self-describing
> JSON file that holds every admitted Type and Member declaration of that
> Library as flat records, each carrying its identity, structured signature,
> name family, documentation summary, and source location with an explicit
> status, plus lookups derived only from those records. Every record occupies
> one line, so `grep` returns whole records and `jq` answers the
> [query catalog](models/library-symbol-document/README.md) with simple
> selects.

This owner defines the document's layout, record shapes, identity spelling,
explicit-value vocabulary, lookups, completion semantics, and the batch work
contract. Its implementation home is `DotnetInspector.Sections`, beside the
[Library inspection documents](library-inspection-document.md).

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Type and Member inspection documents](type-member-inspection-documents.md) | Member admission, ordering, display and canonical signatures, and documentation identity |
| [Library inspection documents](library-inspection-document.md) | The Library Type population, including forwarders as first-class declarations |
| [Library name-family summary](library-name-family-summary.md) | Each Type definition's one-word and two-word suffix families |
| [SourceLink exposure](../sourcelink-exposure.md) and [PDB acquisition](../pdb-acquisition.md) | Type source files, member source lines, and when a PDB may be acquired |
| [DocumentationHouse](documentation-house.md) | Documentation summaries |
| [Scope symmetry](scope-symmetry.md#no-rows-within-rows) | The persistent-artifact admission for a rows-within-rows answer |

The design follows the query-first schema method of the
[.NET CVE schema](https://github.com/dotnet/designs/blob/main/accepted/2025/cve-schema/cve_schema.md):
`jq` ease is the proxy for agent ease, and a bad query means a bad schema.

## Product question

> Give me this whole Library once, in a file I can search as often as I like,
> with whatever offline tools I have.

The consumer is an agent. It runs one command, keeps the file, and then asks
many questions: which methods take a `Stream`, which return one, which Types
live in the `Converter` name family, where a member's source is, and which
members require unreferenced code. Each answer is a `grep` or a `jq` select
over one file, with no further tool invocation.

This is **batch** work. Interactive surfaces serve a user waiting for one
slice, so latency rules and a whole-Library Rows-within-Rows answer is waste.
Batch serves a consumer that pays once and reads many times, so the measures
are total time, peak memory, completeness, and byte-identical output. Scope
symmetry admits a rows-within-rows answer exactly when a persistent consumer
justifies it; this document names that consumer, the agent's offline search,
and offers the whole-Library answer only as this file.

## Scenarios and claims

The document earns its place only if agents find real work easier with it and
choose it for that reason. The claims below name that work, and the
[workflow trials](#workflow-trials) test them. Each scenario is a question about
`System.Text.Json` 10.0.14.

Today an agent answers these with interactive commands. `find .Name` searches
member names across a package, and `type` and `member` show one subject at a
time. Nothing selects Members by parameter type, return type, attribute, or
name family, so a cross-cutting question becomes one `type` call per candidate
Type, chosen by the agent's guess about where to look.

| Scenario | Question | Claim with the document |
| --- | --- | --- |
| Signature search | Which public APIs accept a `Stream`? | A complete answer from one dump and one query. The metadata holds 30 such methods across four Types, including `JsonNode` and `Utf8JsonWriter`, which a name-guided search tends to miss. |
| Exact versus mentioned | What produces a `JsonDocument`, including asynchronously? | The agent separates `Parse` from `ParseAsync` with `return_type` and `return_type_mentions`, without parsing signature text. |
| Compatibility audit | Which `JsonSerializer` APIs require unreferenced code, and which same-named overloads do not? | One attribute lookup plus a group by name yields each unsafe overload beside its safe alternatives. |
| Vocabulary | Which converter Types exist, and what must a custom converter override? | Family and modifier selects answer it without opening each Type. |
| Source set | Where is the source of every `Stream`-taking `Deserialize` overload? | The agent fetches the listed raw URLs directly, with no further dotnet-inspect calls. |
| Follow-ups | A session of five related questions. | After the dump, follow-up questions need no dotnet-inspect calls, and the session costs fewer calls and fewer bytes in context than the interactive path from the second question on. |
| Single lookup (control) | What is `JsonDocument.ParseAsync`'s signature and summary? | The agent uses `member` and does not dump the Library; the document is no improvement here. |

The adoption claim spans the table: when guidance names the document without
recipes, agents choose it for the first six scenarios and not for the control.
The control guards against a format that wins only because it is new.

## Example

The vectors in [`models/library-symbol-document`](models/library-symbol-document/README.md)
are an excerpt of the document for `System.Text.Json` 10.0.14 from nuget.org,
built from that package's real metadata, documentation, and SourceLink. Two
member lines, abbreviated here only by `…`:

```json
{"documentation_id": "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)", "canonical_signature": "M:System.Text.Json.JsonDocument.ParseAsync(System.IO.Stream,System.Text.Json.JsonDocumentOptions,System.Threading.CancellationToken)", "signature": "System.Threading.Tasks.Task<System.Text.Json.JsonDocument> ParseAsync(System.IO.Stream utf8Json, …)", "declaring_type": "T:System.Text.Json.JsonDocument", "declaring_type_suffix_family": "Document", "member_kind": "method", "name": "ParseAsync", "modifiers": ["static"], "return_type": "System.Threading.Tasks.Task<System.Text.Json.JsonDocument>", "return_type_mentions": ["T:System.Text.Json.JsonDocument", "T:System.Threading.Tasks.Task`1"], "parameters": [{"name": "utf8Json", "type": "System.IO.Stream", "type_mentions": ["T:System.IO.Stream"], "modifier": "", "has_default_value": false}, …], "source_url": "https://raw.githubusercontent.com/dotnet/dotnet/0427b9a…/JsonDocument.Parse.cs", "source_line": 213, "source_status": "complete", "findings": [], …},
{"documentation_id": "M:System.Text.Json.Serialization.JsonConverter`1.Read(System.Text.Json.Utf8JsonReader@,System.Type,System.Text.Json.JsonSerializerOptions)", "canonical_signature": "M:System.Text.Json.Serialization.JsonConverter<T>.Read(ref System.Text.Json.Utf8JsonReader,System.Type,System.Text.Json.JsonSerializerOptions)", "declaring_type_suffix_family": "Converter", "modifiers": ["abstract"], "return_type": "T?", "return_type_mentions": [], "source_url": "", "source_line": 0, "source_status": "no_method_body", …}
```

Questions and their answers:

| Question | Query |
| --- | --- |
| Which members take a `Stream`? | `jq -r '.parameter_type_members["T:System.IO.Stream"][]'` |
| Which return exactly `JsonDocument`? | `jq -r '.members[] \| select(.return_type == "System.Text.Json.JsonDocument") \| .name'` |
| Which mention it in the return, including `Task<JsonDocument>`? | `jq -r '.return_type_members["T:System.Text.Json.JsonDocument"][]'` |
| Which members belong to `Converter`-family Types? | `jq -r '.members[] \| select(.declaring_type_suffix_family == "Converter") \| .signature'` |
| Where is `ParseAsync`? | `grep -F '"name": "ParseAsync"' stj.json` |

The catalog holds seventeen such questions with recorded answers, and
`check.sh` runs them. The command that writes the file belongs to its CLI
adoption; this design fixes only that the gesture is explicit and names a
destination.

## Layout

The file is one JSON object. Its members appear in this order: `schema_version`,
`generator`, `library`, `request`, `types`, `members`, the lookups, and then
`section_outcomes`, `counts`, and `outcome`. The trailing three come last
because the writer learns them last.

Every Type entry, every Member record, and every lookup entry occupies exactly
one line, and so does each top-level scalar or small object. The lines that
open and close `types`, `members`, and each lookup stand alone. A matching
line therefore holds one whole record; removing a trailing comma leaves valid
JSON. Strings are JSON-escaped, so a summary or hostile metadata name can
never break a record across lines.

Property names are snake_case and self-descriptive; space is not at a premium.
Keys and values appear with one space after `:` and `,`, so grep patterns are
predictable.

The same Library, request, and generator version produce byte-identical
files. The file carries no timestamp, machine name, or path outside the
package. Types are ordered by documentation ID with ordinal comparison;
Members by their declaring Type's position, then metadata order; lookup keys
and values by ordinal comparison.

## Identity

Every Type and Member is identified by its **documentation ID**, the ECMA-334
comment identifier used by XML documentation, docfx, and API catalogs:
`` T:System.Text.Json.Serialization.JsonConverter`1 ``,
``` M:System.Text.Json.JsonSerializer.Deserialize``1(System.IO.Stream,System.Text.Json.JsonSerializerOptions) ```.
It keys `types`, it is each Member's `documentation_id`, and every reference
to a Type anywhere in the file, including Types from other Libraries, is a
Type documentation ID. One spelling of identity lets every lookup and join
use plain string equality.

Display text is separate and never used for identity. `signature` and
`canonical_signature` are the owner-issued spellings from the Type document,
so the same Member reads the same in the file and in interactive output.
`metadata_token` is exact within the file's `module_version_id`.

Two declarations that share a documentation ID, which C# rarely permits but
metadata can express, both keep their records; each lookup lists that ID once,
and `section_outcomes` reports the declarations section as `partial` with the
colliding IDs in its detail.

## Records

All Type records share one shape and all Member records share one shape,
whatever their kind. A field that does not apply holds an explicit empty
value: `""`, `[]`, `0`, or `false`. No field is ever absent or `null`, so a
query never needs a null guard.

A Type record holds `name` (C# spelling, `JsonConverter<T>`),
`metadata_name`, `namespace`, `declaring_type` for a nested Type,
`declaration_kind` (`definition` or `forwarder`), `forwarded_to_assembly`,
`type_kind`, `accessibility`, `modifiers`, `generic_parameters`, `base_type`,
`interfaces`, `attribute_types`, `suffix_family`, `two_word_suffix_family`,
`summary`, `documentation_status`, `source_urls`, `source_status`, `findings`,
and `metadata_token`. A forwarder keeps its own record with its terminal
assembly; it never borrows the target's facts.

A Member record holds `documentation_id`, `canonical_signature`, `signature`,
`declaring_type`, `namespace`, `declaring_type_suffix_family`,
`declaring_type_two_word_suffix_family`, `member_kind`, `name`,
`accessibility`, `modifiers`, `generic_parameters`, `return_type`,
`return_type_mentions`, `parameters`, `attribute_types`, `summary`,
`documentation_status`, `source_url`, `source_line`, `source_status`,
`findings`, and `metadata_token`. It repeats its declaring Type's namespace
and families so a one-line match answers family questions without a join.

### Signatures

Each parameter holds `name`, `type`, `type_mentions`, `modifier` (`ref`,
`out`, `in`, `params`, `this`, or `""`), and `has_default_value`.

`type` and `return_type` are the C# display spelling, including nullable
annotations. `type_mentions` and `return_type_mentions` are the documentation
IDs of every Type definition the spelling names, at any depth, sorted:
`Task<JsonDocument>` mentions both `` Task`1 `` and `JsonDocument`. Generic
parameters mention nothing. Agents use the display spelling for an exact
question and mentions for an identity question.

`return_type` is what the Member yields: a method's return type, or a
property's, field's, or event's declared type. A constructor yields `""`.

### Name families

`suffix_family` and `two_word_suffix_family` are the Library name-family
summary's families for a Type definition, or `""` when it assigns none. A
forwarder has no families because the summary covers definitions only.

### Source and documentation

A Type lists every SourceLink file it spans in `source_urls`;
`JsonSerializer` spans twenty. A Member has one `source_url` and
`source_line`. URLs are published exactly as SourceLink maps them, which for
GitHub is the raw form an agent can fetch directly.

`source_status` and `documentation_status` say why a value is empty:

| Status | Meaning | Settled |
| --- | --- | --- |
| `complete` | The value is present | Yes |
| `no_method_body` | An abstract or extern Member has no sequence points | Yes |
| `no_pdb` | No PDB exists for this image in any configured location | Yes |
| `no_source_link` | The PDB carries no SourceLink map | Yes |
| `no_documentation` | No documentation file or entry exists | Yes |
| `forwarder` | A forwarder carries no source or documentation | Yes |
| `offline` | The request forbade network work | Yes |
| `not_requested` | The section was not requested | Yes |
| `failed` | Acquisition or decoding failed; detail is in `section_outcomes` | No |

### Findings

`findings` is present on every record and empty until a findings section is
requested. Which findings a record admits, and how they are keyed, belong to
a later slice that composes [Annotated Type Map](annotated-type-map.md)
findings by documentation ID.

## Lookups

Lookups are dictionaries from a known key to sorted documentation IDs. Each is
derived only from the records in the same file, so a consumer may ignore
them, recompute them, or trust them; the catalog checks that derivation.

| Lookup | From | To |
| --- | --- | --- |
| `parameter_type_members` | A Type mentioned by any parameter | Members |
| `return_type_members` | A Type mentioned by `return_type` | Members |
| `attribute_type_members` | An attribute Type on a Member | Members |
| `suffix_family_types` | A one-word suffix family | Types |
| `base_type_derived_types` | A direct base Type | Types in this Library |
| `interface_implementing_types` | A directly declared interface | Types in this Library |

Lookups cover declaration edges only. Edges that need method bodies, such as
calls, stay with [Annotated Type Relations](annotated-type-relations.md). A
new lookup is added when the catalog shows a question that a select answers
badly, and it changes `schema_version` only if it changes an existing one.

## Request and admission

A request names the admission and the sections. `public_api` admits exactly
what the Type document admits for public use; `all` admits every
accessibility. `declarations` is always present. The default request adds
`name_families`, `documentation`, and `source_link`, acquiring a PDB under the
network policy that PDB acquisition owns; an offline request settles
`source_link` as `offline`.

## Completion and failure

`section_outcomes` reports each section as `complete`, `partial`, `failed`,
`offline`, or `not_requested`, with a `detail` when not complete. `outcome` is
`complete` when every requested section settled and `partial` otherwise; a
partial document is still written whole, discloses the gap on standard error,
and exits nonzero. `counts` holds the number of Type and Member records.

A file destination is published atomically: the writer streams to a sibling
temporary file and renames it on completion, so an agent never reads a
half-written document. Budget exhaustion, cancellation, or an unreadable
Library aborts the write, leaves no file at the destination, and reports a
typed error.

## Work and bounds

The producer streams. It holds the Type dictionary and the lookups' ID lists,
never the Member records, so peak memory grows with identifiers rather than
with record text. It may decode Types in parallel when output order is
preserved.

Each record equals what the interactive owners report for the same
declaration: its signatures equal the Type document's, its families equal the
name-family summary's, and its source line equals the member Source Locations
section's. A batch implementation may use one sequential pass over metadata
instead of per-Type requests only when that equality holds.

## Relation to existing documents

This document joins the existing ones by identity; it does not replace them.

| Existing document | In this file |
| --- | --- |
| Library Type list | `types`, or `jq '.types \| keys[]'` |
| `TypeDocument` | `members` filtered by `declaring_type` |
| Annotated Type Map | `findings`, once findings compose by documentation ID |
| Annotated Type Relations | Declaration edges as lookups; body edges stay in Relations |

Interactive hosts keep answering one Type or one Member at a time.

## Real asset

`System.Text.Json` 10.0.14 on nuget.org, `lib/net10.0/System.Text.Json.dll`,
MVID `2ddc75da-7a95-4826-8729-839cc65f9eea`, observed with production
dotnet-inspect 0.27 on 2026-11-06:

- 85 Types, one of them the forwarder `IsExternalInit` to `System.Runtime`,
  which motivates forwarder records with explicit empty source and families;
- `JsonSerializer` declares 103 public methods, 24 of them taking a `Stream`,
  and spans twenty SourceLink files, which motivates parameter mentions, the
  parameter lookup, and `source_urls` as a list;
- across the whole Library, 30 public or protected methods take exactly a
  `Stream`, spread over `JsonDocument`, `JsonNode`, `JsonSerializer`, and
  `Utf8JsonWriter`; none returns one; and 54 carry
  `RequiresUnreferencedCode`, counted from metadata with
  `System.Reflection.Metadata`, which grounds the signature-search and audit
  scenarios;
- `JsonDocument.Parse` and `ParseAsync` separate exact-return from
  mention-return questions; and
- member Source Locations for `JsonDocument.ParseAsync` failed with
  `ExternalProviderFailed` while Type-level SourceLink succeeded
  ([#11031](https://github.com/richlander/dotnet-inspect/issues/11031)), which
  motivates per-record `source_status` and the settled distinction.

The excerpt in the vectors preserves these shapes. Slice 1 adds the package
to a pinned test population and proves the full file against the catalog.

## Production adoption

Both hosts consume the same host-neutral producer:

- the CLI writes the file through an explicit gesture with a destination; and
- Inspect Web offers the same bytes as a download for an opened Library.

The tracker counts the slices from this design to both hosts, then the
findings and portable-anchor slices that extend the record.

## Evidence

Each implementation slice reports, for `System.Text.Json` 10.0.14 and
`System.Private.CoreLib` under NativeAOT:

- the query catalog passing against the produced file;
- two runs producing byte-identical files;
- every record line parsing as JSON once its trailing comma is removed;
- record equality with the Type document, name-family summary, and Source
  Locations section for a sampled set of Members;
- total time, peak memory, and file size; and
- an offline run whose `source_link` settles as `offline` with outcome
  `complete`, and an injected PDB failure that yields `partial` and a nonzero
  exit.

### Workflow trials

The [scenario claims](#scenarios-and-claims) are tested with fresh agents,
reusing the fixed-design protocol, recorder, and freeze wrapper of the
[Explain reading trials](../../tools/ExplainReadingScenarios/usability/package-query-adoption/README.md).
Tasks, guidance, and criteria are fixed and published before dispatch, and
every original answer is frozen before a neutral post-task question.

Two arms receive the same tasks, executable, and pinned package. The
**interactive** arm has current guidance. The **document** arm has the same
guidance plus one task-neutral paragraph that says the document exists and
what it holds, with no task predicates or `jq` recipes. Each participant
answers every scenario, the control included, in a predetermined order.

Each trial records, per scenario and participant:

- correctness against the criteria, with recall reported separately for
  signature search;
- dotnet-inspect calls and local `jq`, `grep`, or fetch calls, separately;
- bytes retrieved into context, separating guidance from inspection;
- wall time, including the dump; and
- whether the agent chose the document.

A claim holds when the document arm answers at least as correctly as the
interactive arm and does better on the claimed measure, and the control shows
no unprompted dump. A failed claim is a schema or guidance question for this
design, decided before the next adoption slice, as the CVE method puts it: bad
queries mean a bad schema.

Two trial points are planned. A **format trial** hands the document arm a
complete `System.Text.Json` file before any producer exists, so the shape can
change cheaply. An **adoption trial** follows CLI adoption: the document arm
must discover and run the real gesture itself, which tests the choice and
the dump cost together. Small nonrandomized samples describe what agents do;
they do not establish causal effects or rankings.

## Non-claims

- Command names, flags, and Inspect Web placement.
- Package, Platform, or Workspace documents; a Package is a set of Library
  documents.
- Findings content, analysis selection, and finding keys.
- Body-derived relations such as calls, allocations, or field access.
- A structured portable anchor beyond the documentation ID and MVID-scoped
  token; the [API qualified anchor](api-qualified-anchor.md) joins when its
  persisted text form exists.
- Parameter attributes, constant values, and XML documentation beyond the
  summary.
- Version-to-version diff; two files can be compared line by line, but that
  comparison is not Finding Diff Any's equality.
