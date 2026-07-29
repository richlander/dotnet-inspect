# Inspection layers and consumer boundaries

How the inspection stack is split into layers so that more than one consumer can
sit on it, which layer owns which noun, and the seam rules that keep the split
from eroding. This is a design note about boundaries, ownership, and vocabulary —
not a tour of every type.

See [overview.md](../overview.md) for subsystem ownership,
[section-model.md](section-model.md) for section selection semantics, and
[output-shapes.md](output-shapes.md) for the shape ladder this note builds on.

## Purpose

`dotnet-inspect` grew as a single consumer, so inspection logic accumulated
wherever it was first needed — which was usually the CLI project. That was cheap
while the CLI was the only caller. It stopped being cheap when a second consumer
appeared: a browser/WebAssembly app whose engine could reference the
`ILInspector.*` libraries but not the CLI.

The result was measurable. The prototype engine re-derived package acquisition,
version resolution, TFM ranking, symbol acquisition, XML-doc lookup, and call
graph orchestration from scratch, and the re-derivations were not merely
duplicated — several were wrong in ways the shared code is not. A second
implementation of a rule is a second place for the rule to be wrong.

This note defines the layering that makes the shared code reachable, so a
consumer picks a depth instead of re-deriving a rule.

## The layers

```text
dotnet-inspect                      L3  argument parsing, console, formats
  |
  +-- DotnetInspector.Sections      L2  sections, categories, shape ladder
        |
        +-- DotnetInspector.Queries L1  typed inspection requests -> results
              |
              +-- ILInspector.*         metadata, analysis, decompiler, research
                  DotnetInspector.*     packages, services, core
```

Each layer is a separate component. A consumer decides how far up it comes:

- The CLI consumes all three.
- A browser engine consumes L1, and L2 as well when it wants section semantics
  and structured rows rather than its own bespoke payloads.
- A future non-interactive consumer may consume L1 alone.

A layer may be more than one project. The rule is the dependency direction and
the ownership boundaries below, not the project count.

### L1 — `DotnetInspector.Queries`

Owns typed inspection requests and their typed results, over the `ILInspector.*`
and `DotnetInspector.{Core,Packages,Services}` libraries.

L1 declares its own **cost** and **capabilities**. A query knows what it will
spend and what authorization it needs; a section must not declare that on a
query's behalf. This is what lets any consumer — not just one with a verbosity
flag — decide between eager and lazy acquisition.

L1 takes **content**, not filesystem paths. A consumer without a filesystem must
be able to call it. See [Seam rules](#seam-rules).

L1 does not reference Markout.

### L2 — `DotnetInspector.Sections`

Owns the named, selectable unit (the **section**), the topical **categories**
that surface it, the disclosure ladder that decides when it appears, and the
**shape ladder** that narrows a result to what was asked for. L2 is where results
are integrated with Markout serialization.

Categories are consumer-neutral. `@Surface`, `@Performance`, `@Audit`,
`@Integrations`, and `@SourceLink` are topical groupings, not terminal
affordances. The browser prototype independently grew category-shaped UI — kind
pills, filter chips, scope selectors — which is evidence the concept belongs
below the CLI rather than in it.

### L3 — `dotnet-inspect`

Owns argument parsing, option objects, command routing, console writers, line
limiting, hints, and output **format** selection. L3 subscribes to sections and
categories; it does not compute facts and does not decide what a section costs.

## Vocabulary

Five nouns, five axes. They are not synonyms and must not be used
interchangeably.

| Noun | Meaning | Values | Owner |
| --- | --- | --- | --- |
| **Scanner** | a sequential pass over metadata | — | `ILInspector.Metadata` |
| **Query** | a typed inspection request producing a typed result | — | L1 |
| **Section** | the named, selectable unit | table \| fields \| list \| blob \| tree | L2 |
| **Shape** | the narrowing rung | Document \| Table \| Vector \| Scalar | L2 (Markout-defined) |
| **Format** | presentation of a selected payload | markdown \| json \| tsv \| jsonl \| plaintext | L3 |

A **query** may run one or more **scanners**. A **section** presents the result
of a query at some **shape**, rendered in some **format**.

### Why these words

Each was chosen against existing usage rather than invented.

**Section** is Markout's noun, not the CLI's. `MarkoutSection` is the most-used
Markout type in the repository, and `ISectionDescriptor.Name` is defined as
matching the `MarkoutSection` name. Since L2 is the layer integrated with Markout
serialization, it speaks Markout's vocabulary. The historical `Views/` directory
is the misnomer, and it is what made the layer look ambiguous.

**Shape** must not be reused as a layer or project name. It already carries two
established, unrelated meanings: the output ladder defined in
[output-shapes.md](output-shapes.md) ("Markout defines the shapes and produces
them"), and the metadata domain concept in `TypeShape`/`TypeShapeKind`/
`ArrayShape`. A third meaning would make the word useless.

**Section is not too narrow to cover trees and tables.**
[output-shapes.md](output-shapes.md) already settles this: a section may be a
table, a key-value field set, a list, a code/text blob, or a tree such as a call
graph, and all of them are still "one section". Trees and tables are siblings
*within* the section axis, which is why one noun covers both.

**JSON is a format, not a shape and not a section.** `--json`, `--tsv`, and
`--jsonl` are presentation modifiers: they change how a selected payload is
rendered without changing the shape.

**Query** has a typed precedent and two senses to keep clear of. The typed
precedent is small but exact:
[`SourceDocumentQuery`](../../src/ILInspector.Metadata/MetadataFindings.Source.cs)
and `MemberSourceQuery` are records in `ILInspector.Metadata` that carry a typed
request a producer runs — precisely the L1 shape. `MetadataDeclarationQuery` is
the same concept in utility form. The browser prototype independently named its
entire exported surface `Query*`.

Three other uses of the word are *not* precedent and must not be confused with
the L1 noun:

- **schema query** — `-D` catalog discovery, see [schema-query.md](schema-query.md). L2.
- **row query** — field predicates within a section, see
  [row-query-order.md](row-query-order.md). L2.
- **a user's search string** — CLI option names such as `OriginalTypeQuery` and
  `PlatformPrefixQuery` in `ApiOptions`, and the `ILOffsetQuery` helper. These
  are inputs typed by a user, not typed requests. L3.

Unqualified "query" means the L1 inspection query. The other three always keep
their qualifier, and a new L1 query type is named for what it returns, never for
the text a user typed.

**Scanner** stays with the passes that genuinely scan —
`MethodClassificationScanner`, `AssemblyDetailScanner`, `ResourceScanner`,
`ExtensionMethodScanner`, `EcosystemIntegrationScanner`,
`IntegrationOpportunityScanner` — all of which live in `ILInspector.Metadata`,
below L1. The orchestration layer that decides *which* scanners run is not
scanning and is not called a scanner.

**Result** names what a query returns (`XxxQuery` -> `XxxResult`).
"Inspection" stays reserved for composed aggregates and "Finding" for the
[`ILInspector.Findings`](../../src/ILInspector.Findings) spine, so the three
nouns remain distinguishable.

## Seam rules

These are the rules that keep the layering from eroding back into a single
consumer's convenience.

1. **Dependencies point down only.** L3 -> L2 -> L1 -> libraries. No layer
   references a layer above it, and nothing below L3 references the CLI.
2. **L1 does not reference Markout.** If a type needs a Markout attribute to be
   useful, it belongs in L2.
3. **L1 takes content, not paths.** A query accepts package or assembly content
   through an abstraction, never a `string` filesystem path. A consumer without a
   filesystem is a supported consumer.
4. **Cost and capabilities are declared by the query, not the section.** What
   work costs and what authorization it needs are properties of acquisition.
5. **The L1/L2 binding is typed.** A section names its query type. A section must
   not reach L1 through a string key, because a string key cannot be checked and
   silently degrades to "always collected".
6. **A second implementation of a shared rule is a defect.** TFM ranking, version
   resolution, moniker normalization, symbol acquisition, and checksum
   verification have one owner each. A consumer that cannot reach the owner is
   evidence of a seam bug — fix the seam, do not re-derive the rule.
7. **Presentation-free means presentation-free.** No layer below L3 writes to the
   console or decides an output format.

## What must change

The layering is closer to reality than it looks: the CLI's directories already
declare `DotnetInspector.*` namespaces, and Markout coupling is already
concentrated in the upper directories while the model and service directories are
essentially free of it. The boundary is largely drawn; what is missing is the
project split and one structural fix.

The structural fix is L1. Today it is neither typed nor demand-driven:

- Data collection **mutates a shared aggregate** rather than returning typed
  results, so a consumer cannot take one query without materializing everything.
- The binding to that collection is a **nullable string key** that is null for
  the large majority of sections, meaning "always collected" — so there is no
  demand-driven seam to consume.
- The collection context is **path-shaped**, so a consumer without a filesystem
  cannot call it at all.

Converting collection into typed, demand-driven, content-shaped queries is
therefore the prerequisite for the split, not a follow-up to it. L2 is close to a
project move once L1 exists; the descriptor contract is already Markout-free
apart from its name binding.

## Non-goals

- This note does not change any user-visible command, flag, section name, or
  category name. `-S`, `-D`, `@Category`, and the verbosity ladder keep their
  current meanings.
- It does not propose a new output format or a new shape rung.
- It does not require every consumer to adopt L2. Consuming L1 alone is a
  supported choice.
- It does not retire `ILInspector.*` ownership. Metadata still owns metadata
  facts, Analysis owns IL-body evidence, CSharp owns C# spelling, and Research
  composes evidence. L1 sits above them and composes them into typed results.
