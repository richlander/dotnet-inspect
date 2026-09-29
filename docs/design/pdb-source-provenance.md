# PDB source provenance

## Status, owner, and claim

Status: **design contract** for
[#8643](https://github.com/richlander/dotnet-inspect/issues/8643).

The **PDB Source Provenance** owner in `ILInspector.Metadata` defines this
claim:

> Given one exact managed image and its matching loaded Portable PDB, issue
> every PDB document's qualified ordinary, generated, or unknown evidence and
> every Type definition's complete ordinary-only, generated-only, mixed, or
> unknown aggregate without inferring provenance from symbol names.

Metadata owns PE/PDB identity, document and sequence-point facts, generation
attributes, Type-definition identity, and this evidence classification.
Artifact acquisition retains outer artifact scope. SourceLink retains URL-map,
checksum, source-content, and repository provenance. Research and Graph
consumers own their population lenses and never parse paths or attributes.

This contract uses **ordinary source**, not unqualified "authored source," for
its positive class. Ordinary means a compiler document for which this
methodology found no source-generator evidence. It does not prove human
authorship or exclude files generated before compiler invocation.

The contract is **unverified** until the Release gates under
[Required evidence](#required-evidence) land.

## User question

> Which exact Types are supported only by ordinary source documents, which
> have source-generator or compiler-synthesis evidence, which contain both,
> and where is the evidence insufficient?

This classification enables an explicit ordinary-code lens for architecture
vocabulary and structure. It does not remove generated code from the physical
Library or reinterpret generated code as unimportant.

## Basis and motivating evidence

The #8634 architecture probe over this repository's 65 product assemblies
found 11,821 Type-to-document rows:

- 8,506 ordinary rows;
- 3,063 System.Text.Json source-generator rows;
- 249 Markout source-generator rows; and
- 2 regex-generator rows.

A naive suffix count reported 226 CLI `*Info` Types, but 214 were generated
`*MarkoutTypeInfo` Types. Generated `JsonContext` callers also accounted for
about half of the CLI's call sites into `ILInspector.Metadata`. The
classification therefore materially changes the vocabulary and structure an
agent interprets.

A fresh Release build of `dotnet-inspect.dll` at
`b8dafb797636da14319e5896052c40ba9e0c1370` is the motivating real asset:

- its matching Portable PDB contains 3,272 documents;
- 2,872 documents are embedded and recorded under the intermediate output
  tree;
- 400 documents are non-embedded ordinary inputs;
- 248 Types have method-to-document rows under recognized generated paths;
- those Types contribute 1,246 generated Type-to-document rows; and
- only 53 Type definitions carry `GeneratedCodeAttribute`.

Attribute-only classification would therefore miss most of the observed
distortion. Path-only classification would lose positive framework marker
evidence and could not classify Types without sequence points. The owner
preserves both.

Collecting Type documents for the 65-assembly probe took approximately 43
seconds on a warm cache. This design-only candidate makes no implementation
cost claim. The implementation slice must measure PE/PDB opening, document
classification, association, and aggregate construction separately on the
pinned asset.

## Conventional basis and deliberate boundary

At Roslyn commit
`644b6341ab31edd8528721aee7f31e4c6bb57f37`:

- `GeneratorDriver.GetFilePathPrefixForGenerator` forms generated paths from
  the compiler-selected base directory, generator assembly simple name, and
  generator full type name;
- the normalized hint name follows that prefix; and
- `CommonCompiler` embeds every generated syntax tree in the Portable PDB.

Roslyn source-generator paths and embedded-source records are therefore
relevant evidence. They are not authenticated producer identities: a PDB
publisher controls both. This owner reports a **path-declared generator
identity**, never a claim that the named assembly or type actually executed.
The tool does not load or execute it.

`GeneratedCodeAttribute(string tool, string version)` is separate positive
declaration evidence. Its decoded strings are likewise declarations from the
inspected artifact, not authenticated package or assembly identities.
`CompilerGeneratedAttribute` is evidence of compiler synthesis but names no
source generator.

The deliberate conservative boundary is embedded ordinary source.
`/embed` can place authored documents in the PDB, while explicit generated
output roots can differ from ordinary SDK layouts. An embedded document that
does not satisfy a supported generated-path profile and has no associated
generation marker remains unknown. It is never classified ordinary merely
because path recognition failed.

## Exact input binding

One execution binds:

```text
PdbSourceProvenanceBinding
  ArtifactIdentity
  AssemblyReferenceIdentity
  NonEmptyModuleVersionId
  PortablePdbContentId
  PdbGeneration
  PathProfileVersion
```

This is a conceptual contract shape, not a frozen CLR type.
[Assembly image lifetime](assembly-image-lifetime.md#identity-vocabulary)
owns `ArtifactIdentity`, image generation, and module-local row addresses.
Metadata's existing Portable PDB identity check owns image/PDB correspondence.

The producer consumes the PE metadata, the matching loaded Portable PDB, and
one explicit path-profile version from the same live context. It does not
accept a detached document list associated only by assembly name, path, or
MVID. The detached result retains the complete binding receipt.

An absent, rejected, unsupported, identity-mismatched, malformed, bounded, or
failed PDB produces a typed unavailable or failed outcome. It does not produce
an available document whose Types are all unknown. A matching loaded PDB may
still issue unknown rows for Types or documents whose own evidence is
insufficient.

The result covers every Metadata Type definition except `<Module>`.
The exact Type address is the bound artifact scope plus
`MetadataTypeDefinitionAddress`; display name never establishes identity.

## Document evidence

The result retains one row for every Portable PDB Document row, ordered by
document row ID. A row contains:

- document row ID and exact inert path text;
- checksum algorithm and checksum bytes when present;
- whether the document carries Embedded Source custom debug information;
- every exact associated method and Type-definition address;
- its disposition and owner-issued evidence; and
- every path-declared producer identity.

Paths remain evidence, not filesystem authority. Classification performs
bounded lexical inspection only; it never opens the recorded path.

### Ordinary document

A non-embedded document is `Ordinary`.

This means the document reached the compiler as an ordinary input under this
methodology. MSBuild-generated `.g.cs` files supplied as ordinary compiler
inputs are indistinguishable from human-authored files and remain ordinary.
Filename suffixes such as `.g.cs`, directory names, and symbol names do not
change the disposition.

### Generated document

A document is `Generated` when this evidence path succeeds:

1. **Path-declared source generator.** The document is embedded and its exact
   path satisfies the selected generated-path profile. The evidence retains
   the profile version, generator assembly spelling, generator full-type
   spelling, hint-name suffix, and exact matched path spans.

Generation attributes classify exact Type contributions, not a shared
document. A document can contain sequence points for unrelated Types and
methods; one attributed method or Type must not reclassify their common
document.

The first generated-path profile is
`RoslynSourceGeneratorPathV1`. It recognizes a path-declared generator only
when:

- the document is embedded;
- one adjacent segment pair has a nonempty first spelling other than `.` or
  `..`, followed by a second spelling that begins with the exact first
  spelling plus `.`, and whose leaf after the last `.` or `+` ends with the
  ordinal text `Generator`;
- at least one valid Roslyn hint-name segment follows;
- the pair occurs below an `obj` or `artifacts/obj` intermediate-output
  segment; and
- exactly one eligible decomposition exists across the whole path.

Both `/` and `\` are separators for lexical matching; the source path remains
unchanged. Dot segments, empty segments, traversal-like hint segments, an
ambiguous pair, or a path beyond the configured scan and segment bounds do not
match.

This profile intentionally recognizes the observed
`System.Text.Json.SourceGeneration.JsonSourceGenerator` and
`Markout.SourceGeneration.MarkoutSourceGenerator` paths. A generator whose
assembly/type spelling does not follow this conservative shape remains
unknown unless another evidence path identifies it. The profile is a
versioned .NET SDK/Roslyn convention, not a Portable PDB standard.

### Unknown document

A document is `Unknown` when no generated evidence succeeds and it is:

- embedded;
- ambiguous under the selected generated-path profile; or
- beyond a document or path bound.

Unknown retains every reason and any partial known evidence. It is not
rewritten to ordinary or generated by a consumer.

An unassociated non-embedded document remains ordinary at document grain but
contributes to no Type aggregate.

## Generation-marker evidence

The attribute reader authenticates the framework attribute Type identity and
constructor shape before decoding:

- `System.CodeDom.Compiler.GeneratedCodeAttribute(string, string)` supplies
  exact declared tool and version text; and
- `System.Runtime.CompilerServices.CompilerGeneratedAttribute()` supplies the
  compiler-synthesis marker.

Lookalike attributes do not count. Duplicate valid markers are retained as
duplicate evidence; conflicting tool/version values coexist. A malformed
framework marker is unknown evidence and cannot be hidden by another valid
marker.

Method-level `GeneratedCodeAttribute` contributes only through that method's
exact document associations. Method-level `CompilerGeneratedAttribute` does
not make an authored Type mixed: ordinary compiler lowering marks accessors,
lambdas, and other implementation details this way. The compiler-synthesis
rule therefore applies only at Type or enclosing-Type grain.

Enclosing-Type inheritance follows exact Metadata nesting identity with a
finite relationship bound. A cycle, malformed declaring-Type relationship, or
bound failure yields unknown evidence for affected Types rather than generated
success.

## Type-contribution evidence

The producer classifies evidence at the narrowest available grain before
forming a Type aggregate. One method-to-document association contributes:

1. `Generated` when its document is generated by the selected path profile;
2. `Generated` when the method carries a valid `GeneratedCodeAttribute`;
3. `Generated` when its declaring Type or an enclosing Type carries
   `CompilerGeneratedAttribute`;
4. `Ordinary` when none of the generated rules applies and its document is
   ordinary; or
5. `Unknown` when its document or any applicable marker evidence is unknown.

The exact rule evidence and producer identities remain attached to the
contribution. A shared document is evaluated independently for each exact
method and Type association.

A valid `GeneratedCodeAttribute` directly on a Type or enclosing Type adds one
generated Type contribution and its declared tool/version identity. It does
not rewrite that Type's method-to-document associations: the attribute may
come from only one declaration of a partial Type. Ordinary associations
therefore coexist with the generated declaration and produce `Mixed`.

A valid `CompilerGeneratedAttribute` directly on a Type or enclosing Type
identifies the Type itself as compiler synthesis. It adds a generated Type
contribution and classifies that Type's method-to-document associations as
generated even when their sequence points refer back to an ordinary document.
This keeps an async or lambda implementation Type generated-only without
changing the ordinary contribution of any neighboring user Type.

Malformed markers add unknown Type-contribution evidence at their exact owner
and inherited scope. They do not reclassify a shared document.

## Type aggregate

One Type row retains:

- exact Type identity and metadata name;
- every associated document row ID and disposition;
- every classified method-to-document and Type-grain contribution;
- every direct or inherited generation marker;
- the distinct path-declared and attribute-declared producer identities;
- ordinary, generated, and unknown document and contribution counts; and
- exactly one aggregate disposition.

The aggregate is:

| Disposition | Rule |
| --- | --- |
| `OrdinaryOnly` | At least one ordinary contribution, no generated contribution, and no unknown evidence. |
| `GeneratedOnly` | At least one generated contribution, no ordinary contribution, and no unknown evidence. |
| `Mixed` | At least one ordinary contribution and at least one generated contribution, with no unknown evidence. |
| `Unknown` | No associated evidence, or any unknown/malformed/bounded evidence. |

An `Unknown` row still retains its known ordinary and generated evidence and
producer identities. Unknown is an aggregate qualification, not evidence
erasure.

Partial Types motivate `Mixed`. A source generator may add members to a Type
that also has authored method bodies. Flattening that Type to authored would
hide generated volume; flattening it to generated would remove authored
architecture. A consumer chooses explicitly whether a mixed Type belongs in a
particular lens.

Types without method-correlated documents remain unknown unless a direct or
inherited Type-grain generation marker supplies positive generated evidence.
Filename inference used by Source presentation does not establish provenance
and is not imported.

## Completeness, bounds, and failure

An available result contains every PDB Document row and every selected
Metadata Type definition exactly once. Its receipt records:

- document and Type counts;
- method-to-document association count;
- generation-marker rows examined;
- path characters and segments examined;
- direct and inherited marker counts;
- each aggregate-disposition count; and
- the exact finite bounds.

Duplicate Type rows, foreign document handles, foreign Type addresses,
out-of-range tokens, or inconsistent binding receipts reject construction.
Counts sum exactly to their declared populations.

A global enumeration or resource-bound failure returns typed incomplete
output and no available complete document. Per-row malformed or ambiguous
untrusted evidence becomes an unknown row when the producer can still prove
complete bounded enumeration.

The detached result retains no MetadataReader, PDB reader, stream, artifact
lease, path authority, or source-content capability.

## Consumer handoff

Consumers select from owner-issued aggregate dispositions:

- an **ordinary-only** lens includes only `OrdinaryOnly`;
- a **generated-only** lens includes only `GeneratedOnly`;
- a mixed lens includes only `Mixed`; and
- an unknown lens includes only `Unknown`.

No consumer calls `OrdinaryOnly` proof of human authorship. A broader
"contains ordinary evidence" lens may include `OrdinaryOnly` and `Mixed`, but
must name that choice and report both counts.

The current Library name-family design uses authored, generated, and unknown
as three exclusive classes. Before implementation it must consume this
four-way owner-issued disposition, rename its positive class to ordinary-only,
and keep mixed Types separate. That is a Research consumer adjustment, not
part of this Metadata owner.

Library Dependency Structure and Library Metrics remain physical-population
owners until their own focused adoptions. This producer does not filter them.

## Required evidence

`ILInspector.Metadata.Tests` is the Release contract gate. It uses
independently compiled fixtures under `fixtures/metadata/` and must cover:

- an ordinary non-embedded document;
- one Roslyn source-generator document whose path-declared assembly, Type, and
  hint identity are retained;
- valid, duplicate, conflicting, lookalike, and malformed
  `GeneratedCodeAttribute` rows;
- unrelated Types sharing a document remain independent when only one method
  or Type has a generation marker;
- an attributed partial Type with an ordinary method contribution is mixed
  rather than having its shared document reclassified;
- direct and enclosing-Type `CompilerGeneratedAttribute`, while a
  compiler-generated method on an ordinary Type does not make the Type mixed;
- an async or lambda implementation Type marked `CompilerGeneratedAttribute`
  is generated-only even though its sequence points refer to an ordinary
  document;
- ordinary-only, generated-only, mixed partial-Type, and no-document unknown
  aggregates;
- embedded authored source remaining unknown rather than ordinary;
- explicit generated output outside the supported path profile remaining
  unknown without an attribute marker;
- `.g.cs` ordinary input remaining ordinary;
- separator variants, dot segments, traversal-like hints, multiple eligible
  path decompositions, long paths, and every finite bound;
- exact artifact/image/PDB binding, duplicate and foreign rows, complete
  counts, deterministic order, and detached lifetime; and
- absent, identity-mismatched, malformed, unsupported, and failed PDB outcomes
  remaining distinct from an available all-unknown result.

The real-asset canary builds and inspects `dotnet-inspect.dll` at the pinned
commit. It requires:

- both System.Text.Json and Markout path-declared generator identities;
- the existing valid System.Text.Json `GeneratedCodeAttribute` declarations;
- ordinary, generated-only, and unknown populations;
- complete population receipts; and
- no path opening or inspected-assembly loading.

The canary records exact counts as change-sensitive evidence, not as a
permanent product invariant.

## Production adoption

1. **Metadata producer:** implement the bounded detached document and Type
   provenance result over one exact PE/PDB binding.
2. **Library name families:** #8698 consumes the four dispositions and issues
   separate ordinary-only, generated-only, mixed, and unknown populations.
3. **Query and CLI:** the Library name-family operation exposes provenance
   fields and population selection through QuerySpace, complete JSON, Markout,
   and `InspectionEnvelope<TContent>`; the non-default `Name Families` section
   labels every lens explicitly.
4. **Browser/Wasm:** the Library experience consumes the same managed query and
   dispositions without TypeScript path parsing.
5. **Structure consumers:** Library Dependency Structure and Library Metrics
   may adopt explicit lenses through separate focused changes while retaining
   their physical defaults until then.
6. **Project analysis:** the architecture workflow composes ordinary,
   generated, mixed, and unknown vocabulary with exact-identity Graph shape
   and amplitude.

This owner adds no standalone command and no default section. The first
observable value arrives through the #8698 CLI and Browser/Wasm consumer.

## Non-claims

This owner does not:

- prove human authorship, repository ownership, generator execution, or build
  reproducibility;
- authenticate path-declared or attribute-declared producer identities;
- infer generation from Type, member, namespace, suffix, or filename names;
- treat `.g.cs`, `obj`, embedding, SourceLink mapping, or
  `CompilerGeneratedAttribute` on a method as sufficient by itself;
- parse source text or an `<auto-generated>` comment;
- acquire a PDB, source document, repository, or generator assembly;
- load or execute inspected code;
- classify source folders or declared architectural structure;
- filter a Research, Analysis, Metrics, or Graph population; or
- define presentation, semantic roles, or architecture quality.

The result is exact about the evidence it observed and conservative about what
that evidence can establish.
