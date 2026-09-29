# Library name-family summary

## Status, owner, and claim

Status: **design contract** for
[#8698](https://github.com/richlander/dotnet-inspect/issues/8698), a focused
evidence producer for the architecture narrative in
[#8634](https://github.com/richlander/dotnet-inspect/issues/8634) and the
learn-the-codebase scenario in
[#8744](https://github.com/richlander/dotnet-inspect/issues/8744).

The **Library Name-Family Summary** owner defines this claim:

> Given one exact Library's complete Type-definition inventory and one
> versioned identifier-word grammar, issue every Type's qualified word
> sequence and deterministic one-word and two-word suffix-family populations,
> preserving exact Type identity, source provenance when supplied, and every
> unresolved word run.

Metadata continues to own Type-definition identity, names, accessibility,
kind, image identity, and inventory completion.
[CSharpText identifier word breaking](https://github.com/richlander/dotnet-inspect/issues/8897)
owns model-free segmentation and its rule evidence.
[Typed authored/generated source provenance](https://github.com/richlander/dotnet-inspect/issues/8643)
owns source classification.
Research owns only the Library population, family aggregation, qualification,
and portable summary defined here. Queries and hosts retain selection,
acquisition, execution, and presentation.

The contract is **unverified** until the Release gates under
[Required evidence](#required-evidence) land.

## User question

> Which recurring Type-name suffixes describe this Library's vocabulary, how
> widespread is each family, and which exact Types support it?

This is the vocabulary pass of the architecture narrative. It reduces
thousands of Type names to tens or hundreds of evidence-backed families before
an agent decides that `*Validator`, `*Options`, or `*Command` has domain
meaning.

The summary does not decide that meaning. A suffix is a textual population,
not proof that a Type performs the role suggested by an English word.

## Basis and motivating evidence

The survey on #8634 examined 27,108 Type names from the repository's pinned
top-100 NuGet corpus:

- 94.0% split by ordinary case boundaries;
- a runtime-oracle prototype reached about 97% on a fresh hand-graded sample;
- common suffixes such as `Extensions`, `Factory`, `Provider`, `Options`,
  `Exception`, `Attribute`, `Context`, `Builder`, and `Handler` recur across
  many packages; and
- the long tail contains domain vocabulary such as `Unmarshaller`,
  `Paginator`, `Mapper`, `Sink`, and `Runner`.

FluentValidation 12.1.1 is the positive control: `Validator` occurs in 55 of
111 public Types and makes the Library's purpose visible from names alone.
The repository is the pathological provenance case. A naive CLI count found
226 `*Info` Types, but 214 were generated `*MarkoutTypeInfo` Types. The same
name population must therefore support an all-Type view and a separately
qualified authored-Type view; it may not guess authorship from a suffix,
namespace, or generated-looking spelling.

This design deliberately makes fewer claims than the survey prototype.
It issues measured populations and imported rule evidence, not a semantic
category, quality judgment, or opaque distinctiveness score.

## Imported inputs

One execution binds three independently owned inputs. Its exact Library image
binding is:

```text
LibraryImageBinding
  ArtifactIdentity
  ImmutableSourceCoordinate?   // when the acquisition owner supplies one
  AssemblyReferenceIdentity
  NonEmptyModuleVersionId
```

This is a conceptual contract shape, not a new frozen CLR type.
[Assembly image lifetime](assembly-image-lifetime.md#identity-vocabulary)
owns the distinction: `ArtifactIdentity` supplies the run-local outer artifact
scope, an immutable source coordinate may supply a portable reacquisition
scope, and MVID plus metadata token addresses a row only inside that module
generation. `AssemblyReferenceIdentity + MVID` is never treated as global
artifact identity.

Metadata issues the assembly identity and MVID from one open image. The query
binding retains the acquisition-issued `ArtifactIdentity` for that exact image
and admits optional provenance only from the same artifact binding. It does
not accept an independently supplied provenance collection whose only
association is MVID or assembly name. A duplicate Type address or provenance
row outside the bound inventory rejects the optional provenance binding
visibly; it never joins by name. The complete all-Type summary remains
available when only the optional provenance binding is rejected.

The detached document retains the owner-issued immutable source coordinate
when one exists. A local or otherwise mutable artifact retains an explicitly
run-local binding receipt and non-projectable Share outcome; the document does
not manufacture portability from its path, assembly identity, or MVID.

### Complete Type-definition inventory

The required Metadata input is the existing
`AssemblyInspectionSession.TypeDeclarations()` result together with
`AssemblyInspectionSession.ModuleVersionId()` from the same live session.
`AssemblyTypeDeclarationInventoryOutcome.Read` establishes complete bounded
enumeration of that image. Research selects only declarations whose kind is
`Definition`; forwarders and module exports remain outside this population.

The selected rows contain every Type definition in the Library, including
non-public and nested Types, with:

- exact definition identity;
- exact namespace and nesting identity;
- the metadata name and trusted generic arity;
- accessibility and Type kind; and
- the owner-issued assembly identity and complete inventory outcome.

Research combines the non-empty MVID with each definition token to form the
existing `MetadataTypeDefinitionAddress`. It does not derive an address from a
structured or displayed name. A Type row's exact join currency is the bound
artifact scope plus that address; the address alone is not cross-artifact
identity.

Type forwarders and referenced Type definitions are not members of this
population. An empty, complete inventory is valid. A missing, failed, scoped,
or otherwise incomplete inventory produces a typed unavailable summary rather
than an available empty document.

The row retains two distinct strings:

- **metadata simple name:** the exact innermost definition segment; and
- **name stem:** that segment after removing only the canonical generic-arity
  suffix justified by the Metadata-issued arity.

A malformed or noncanonical backtick sequence remains in the name stem and
reaches the word grammar. Word and separator spans index the name stem.
Namespace segments and enclosing-Type names do not enter the suffix family of
a nested Type.

### Identifier-word grammar

The required CSharpText input supplies:

- a grammar and oracle version;
- immutable recognized atoms and compounds;
- any explicit Library-local numbered-family evidence;
- an ordinal, culture-independent break operation; and
- source spans classified as admitted words, separators, or unresolved runs,
  with owner-issued rule evidence.

Research neither rebuilds the runtime oracle nor repairs a break result.
The returned spans cover the name stem exactly once.
Unknown uppercase runs, mixed-case proper nouns, digit compounds, and
non-C# metadata names remain visible evidence; uncertainty never drops text.

The same grammar and oracle version apply to every Type in one document.
Changing either changes the summary methodology version and invalidates a
baseline comparison.

Library-local numbered-family evidence is derived once from the exact ordered
name-stem population and passed to CSharpText as explicit textual context.
Research associates that context with the `LibraryImageBinding`, exact Type
count, and grammar version in its own receipt. CSharpText neither references
Metadata identity nor learns what a Library is. Research rejects results from
another grammar version.

### Source provenance

Typed source provenance is optional for constructing the all-Type population.
When supplied, it must come through the same `ArtifactIdentity` and open-image
binding and classify each Type address as:

- authored;
- generated, retaining the owner-issued generator identity; or
- unknown, retaining the reason.

Research carries this classification unchanged. It does not parse PDB paths,
inspect attributes, infer from angle brackets, or treat missing source as
authored.

Complete provenance means that every exact Type address has exactly one of the
three classifications, including `unknown`; it does not mean that every Type
is known to be authored or generated. Positive authored and generated
populations are available under a complete classification. An authored
population with a non-zero unknown count is visibly qualified: it is the known
authored population, not proof of the complete authored vocabulary. Generated
and unknown partitions remain independently selectable evidence rather than
disappearing from the document.

## Family model

### Type word rows

The document retains one row for every Type definition. A row contains:

- exact Type identity;
- metadata simple name and name stem;
- ordered word and separator spans;
- the rule evidence for each span;
- the one-word suffix family, when the final span is an admitted word;
- the two-word suffix family, when the final span and preceding nonseparator
  span are admitted words and every span between them is a separator;
- namespace, accessibility, and Type kind; and
- source provenance when supplied.

A Type with no admitted suffix word still has a row. Its exact names and
unresolved or separator spans remain inspectable, and it contributes to the
applicable partition's residual count. An unresolved final run is never
promoted to a word or family identity. A trailing separator likewise prevents
a suffix-family assignment. Earlier admitted words do not replace the actual
unresolved or separator-terminated suffix.

The document does not remove a leading `I` from interfaces, singularize or
pluralize words, merge synonyms, translate words, or normalize acronyms.
Those transformations would replace textual evidence with a semantic guess.

### Suffix-family identity

A suffix family is identified by:

```text
NameFamilyIdentity
  MethodologyVersion
  WordGrammarVersion
  Kind                 OneWordSuffix | TwoWordSuffix
  Words[]              exact ordinal word spellings
  Separator?           exact intervening separator spelling for two words
```

`JsonContext`, `JSONContext`, and `JsonContexts` therefore remain distinct
families. A consumer may interpret them as related, but the tool does not
silently merge them. `FooBar` and `Foo_Bar` also remain distinct two-word
families; both belong to the exact one-word `Bar` family.

Every Type whose final span is an admitted word belongs to exactly one
one-word suffix family. Every Type whose final two nonseparator spans are
admitted words, with only separators between them, belongs to exactly one
two-word suffix family. These two partitions are independent; membership in
one never replaces membership in the other.

### Family rows

Each family row contains deterministic facts over one named population:

- exact family identity;
- Type count;
- public-Type count;
- distinct namespace count;
- counts by Type kind;
- counts by source-provenance class when provenance is present; and
- references to the complete supporting Type-word rows.

Each named population carries separate one-word and two-word partition
receipts. A receipt records the total Type denominator, eligible Type count,
residual Type count, and residual counts by a closed reason:

- one-word residuals: empty stem, final separator, or final unresolved run;
- two-word residuals: fewer than two suffix words, final separator, final
  unresolved run, or preceding unresolved run.

Family Type counts sum exactly to that partition's eligible count. A one-word
Type is therefore eligible for the one-word partition and a stated
fewer-than-two residual in the two-word partition, not globally
"unclassified."

Rows sort by descending Type count, then descending distinct namespace count,
then exact ordinal family identity. Type references sort by exact Type
identity. Display text never establishes identity or order.

The complete supporting population remains in the document. A host may bound
display rows, but every bound carries the exact omitted-family count and never
changes a family count.

## Population views and qualification

The document always defines the complete **All Types** population.

When complete typed source classification is available, it additionally
defines:

- **Authored Types**;
- **Generated Types**; and
- **Unknown Provenance Types**.

Each view has its own Type denominator, family rows, per-partition receipts,
and qualification. A family count never silently mixes denominators. A host
that shows an authored view labels it as known-authored, reports the exact
unknown count, and retains access to the all-Type view.

Generated code can be architecturally meaningful, especially for serializer
contexts and generated clients. The separate generated population is not a
discard pile or a quality classification. It exists so generated volume does
not masquerade as authored vocabulary and so a consumer can inspect generator
effects explicitly.

An available summary may contain unresolved word runs. Word-breaking
uncertainty qualifies the affected Type rows but does not make the complete
inventory unavailable. The document reports exact counts of Types with and
without unresolved runs and preserves the grammar's typed rule evidence on
every span. It does not copy a closed rule-kind taxonomy from CSharpText.

These are methodology facts, not confidence percentages.

## Corpus comparison

The first summary does not issue a distinctiveness score. The survey's score
was useful exploration, but no score is required to answer which suffixes are
common in one Library, and an opaque score would combine choices about package
selection, package weighting, version weighting, and normalization.

A focused successor may bind a pinned corpus baseline to the exact same
methodology and issue direct comparison facts such as:

- baseline package count and exact package-set receipt;
- packages containing the family;
- baseline Type count and denominator; and
- local versus baseline population shares.

Interpretation such as "distinctive" remains with the narrative consumer
unless that successor defines and names a transparent statistic.

## Graph and amplitude composition

This document is not graph-shaped. Name-family membership is a caller-owned
per-Type value that joins to graph results through exact bound Type identity:
artifact scope plus `MetadataTypeDefinitionAddress`.

It does not:

- build Type or namespace edges;
- sum dependency edges by family;
- compute distinct-neighbor degree or structural role;
- aggregate implementation volume or complexity; or
- define communities.

A focused Research composition may later group Graph-issued topology or join
Library Metrics amplitude by exact bound Type identity. That owner must preserve the
Graph document receipt, relationship selection, direction, completion, and
the name-family methodology receipt. It may not reconstruct either input from
display names.

This separation follows
[Learn the codebase](https://github.com/richlander/dotnet-inspect/issues/8744):
shape belongs to Inspector.Graph, while naming role is a per-subject signal
joined onto shape.

## Interpretation boundary

The summary can state:

- which words and rules were issued for an exact Type name;
- how many Types share an exact suffix family;
- how broadly the family occurs across namespaces and Type kinds;
- which exact Types support the count; and
- whether rows are authored, generated, unknown, or unclassified.

It cannot state:

- that `*Service` implements a service pattern;
- that `*Manager`, `*Helper`, or any other family is good or bad;
- that two differently spelled families are synonyms;
- that a common family is architecturally important;
- that an uncommon family is accidental; or
- that unresolved word runs are malformed identifiers.

Those are interpretations or hypotheses under #8516. An agent may make them
only with that label and with links back to these rows and supporting
structural evidence.

## Analogous implementations

Analogous implementations inform boundaries but transfer no code.

| Implementation | Relevant evidence | Deliberate difference |
| --- | --- | --- |
| Roslyn `StringBreaker` | Deterministic case, acronym, digit, and punctuation parts support editor search and naming styles | Roslyn is prohibited on product paths; its parts carry no runtime-oracle or uncertainty receipt |
| Unicode Standard Annex #29 | Unicode-aware word boundaries distinguish text segmentation from byte or UTF-16 slicing | Natural-language boundaries do not define Pascal/camel-case or runtime naming compounds |
| Humanizer | Demonstrates user-facing word and casing transforms | Transforming or humanizing names loses exact spelling and is not evidence-preserving analysis |
| .NET API naming guidelines and reference packs | Reviewed runtime identifiers provide representative atoms and compounds | The runtime is evidence for a versioned oracle, not authority over third-party domain vocabulary |

## Required evidence

Release gates belong in `ILInspector.Research.Tests` and exercise
product-owned Metadata inventory and CSharpText word results:

- `LibraryNameFamilies_PartitionsCompleteTypeInventory`: every exact Type row
  occurs once; one-word and two-word memberships agree with the issued words;
  empty and one-word names follow the declared partition rules.
- `LibraryNameFamilies_PreservesExactMetadataIdentity`: nested Types, generic
  definitions, and same-display names retain distinct exact identities;
  canonical arity is excluded without stripping noncanonical backtick text.
- `LibraryNameFamilies_PreservesWordRuleEvidence`: every owner-issued word,
  separator, unresolved-run disposition, and detailed rule value survives
  Research aggregation unchanged.
- `LibraryNameFamilies_SeparatesExactSpellings`: acronym, casing, and plural
  variants remain separate ordinal families.
- `LibraryNameFamilies_RejectsIncompleteOrMismatchedInventory`: scoped,
  incomplete, failed, wrong-artifact, and wrong-module inputs fail visibly
  rather than issuing success-shaped partial rows.
- `LibraryNameFamilies_SeparatesSourcePopulations`: all, known-authored,
  generated, and unknown denominators and family counts remain distinct;
  unknown rows qualify rather than suppress the positive authored population.
- `LibraryNameFamilies_BoundsDisplayWithoutChangingCounts`: a bounded
  projection retains exact omitted counts and references the complete
  document.
- `LibraryNameFamilies_IsDeterministic`: input enumeration and hash order do
  not change rows, counts, identities, or tie-breaking.

The PR-fast fixture lives under `fixtures/research/` following
[Fixture governance](../fixture-governance.md). It contains scenario-adjacent
families such as `Validator`,
`ValidatorOptions`, `ValidationContext`, and generated
`ValidatorJsonContext`, plus nested, generic, acronym, digit, unknown-run, and
hostile metadata names.

Real-asset evidence records:

- FluentValidation 12.1.1, including the `Validator` family;
- `dotnet-inspect.dll`, including the authored/generated `Info` distortion;
  and
- one runtime Library containing acronym and digit compounds.

The real-asset probes are reproducible design evidence, not corpus-wide CI
gates.

## Production adoption

1. **Identifier words:** #8897 defines and implements the CSharpText grammar,
   versioned oracle, and rule evidence.
2. **Metadata binding:** Research consumes the existing
   `AssemblyInspectionSession.ModuleVersionId()` and complete
   `TypeDeclarations()` inventory under the acquisition-issued
   `ArtifactIdentity`; no signature-use scan or new Metadata population is
   required.
3. **Source provenance:** #8643 issues typed authored/generated/unknown
   classifications from the Metadata/PDB source owner.
4. **Research:** this owner consumes the complete Metadata Type inventory and
   identifier words, issuing the resource-free summary and optional provenance
   populations.
5. **Query and CLI:** a host-neutral query exposes Type-word and family row
   vocabularies through QuerySpace. The CLI adds an exact-name-only,
   non-default `Name Families` Library section through Markout, with complete
   JSON and an `InspectionEnvelope<TContent>`.
6. **Browser/Wasm:** the Library experience consumes the same managed query and
   presents family filtering and exact-Type drill-down without tokenizing in
   TypeScript.
7. **Learning composition:** a focused Research composition joins family
   membership with Inspector.Graph structure and Library Metrics amplitude by
   exact bound Type identity. The `project-analysis` workflow consumes those
   typed results instead of parsing names or rebuilding counts.

Steps 1-4 establish the shared owner path. Step 5 is the first production host;
step 6 completes shared-host adoption. Step 7 is a separate owner because it
combines independently qualified evidence rather than changing this summary.

## Non-claims

- No semantic role taxonomy, family synonym table, or quality score.
- No corpus baseline or distinctiveness score in the first methodology.
- No first-word or arbitrary n-gram family in the first methodology; that is a
  focused successor if suffix families and per-Type words prove insufficient.
- No namespace, assembly, Workspace, or repository-wide aggregation.
- No graph construction, graph algorithm, or family-level edge aggregation.
- No in-owner amplitude join; both amplitude and family-level edge sums remain
  a separately qualified learning composition.
- No source-provenance inference from names or paths in Research.
- No guarantee that every identifier has one natural-language segmentation.
- No Roslyn dependency, inspected-assembly loading, or culture-sensitive
  casing.
- No default CLI or Browser presentation change in the design slice.
