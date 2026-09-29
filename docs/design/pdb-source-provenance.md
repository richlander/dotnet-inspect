# PDB source provenance

## Status, owner, and claim

Status: **implemented and Release-gated contract** for
[#8643](https://github.com/richlander/dotnet-inspect/issues/8643).

The **PDB Source Provenance** owner in `ILInspector.Metadata` defines this
claim:

> Given one exact managed image and its matching loaded Portable PDB, issue
> every PDB document's qualified path evidence and every Type definition's
> complete ordinary-evidence-only, generated-evidence-only, mixed-evidence, or
> unknown aggregate without inferring provenance from symbol names or claiming
> physical syntax-tree origin.

Metadata owns PE/PDB identity, document and sequence-point facts, generation
attributes, Type-definition identity, and this evidence classification.
Artifact acquisition retains outer artifact scope. SourceLink retains URL-map,
checksum, source-content, and repository provenance. Research and Graph
consumers own their population lenses and never parse paths or attributes.

This contract uses **ordinary document evidence**, not unqualified "authored
source," for its positive class. Ordinary evidence means a mapped compiler
document for which this methodology found no source-generator evidence. It
does not prove human authorship, physical syntax-tree origin, or the absence of
files generated before compiler invocation.

The Release gates and pinned real-asset evidence under
[Required evidence](#required-evidence) verify this contract.

## User question

> Which exact Types have only ordinary PDB evidence, which have
> source-generator or compiler-synthesis evidence, which contain both evidence
> classes, and where is the evidence insufficient?

This classification enables an explicit ordinary-evidence lens for
architecture vocabulary and structure. It does not remove generated code from
the physical Library or reinterpret generated code as unimportant.

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
An embedded Portable PDB establishes correspondence by its PE containment. A
standalone Portable PDB requires a matching Portable CodeView identity from the
PE; successful decoding without that positive identity is unavailable, not a
matching result.

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
- raw path character and segment counts used for finite-bound receipts;
- checksum algorithm and checksum bytes when present;
- whether the document carries Embedded Source custom debug information;
- every exact associated method and Type-definition address;
- its disposition and owner-issued evidence; and
- every path-declared producer identity.

Paths remain evidence, not filesystem authority. Classification performs
bounded lexical inspection only; it never opens the recorded path.

### Ordinary document evidence

A non-embedded document is `OrdinaryDocumentEvidence`.

This means the document reached the compiler as an ordinary input under this
methodology. MSBuild-generated `.g.cs` files supplied as ordinary compiler
inputs are indistinguishable from human-authored files and remain ordinary.
Filename suffixes such as `.g.cs`, directory names, and symbol names do not
change the disposition.

### Generated-path document evidence

A document is `GeneratedPathEvidence` when this evidence path succeeds:

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

The hint suffix follows Roslyn's pinned `AdditionalSourcesCollection` grammar:
Unicode identifier-part characters plus period, comma, hyphen, plus, grave
accent, underscore, spaces, parentheses, brackets, braces, slash, and
backslash are allowed. Spaces may not terminate a segment, and empty, dot, or
dot-dot segments are invalid. Other characters remain unknown rather than
generated.

This profile intentionally recognizes the observed
`System.Text.Json.SourceGeneration.JsonSourceGenerator` and
`Markout.SourceGeneration.MarkoutSourceGenerator` paths. A generator whose
assembly/type spelling does not follow this conservative shape remains
unknown unless another evidence path identifies it. The profile is a
versioned .NET SDK/Roslyn convention, not a Portable PDB standard.

### Unknown document evidence

A document is `UnknownDocumentEvidence` when no generated-path evidence
succeeds and it is:

- embedded;
- ambiguous under the selected generated-path profile; or
- beyond a document or path bound.

Unknown retains every reason and any partial known evidence. It is not
rewritten to ordinary or generated by a consumer.

An unassociated non-embedded document retains ordinary document evidence but
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

Authentication accepts the platform-signed framework definitions or facades
used by supported targets: `System.Runtime` and `netstandard` for both markers,
`System` for `GeneratedCodeAttribute`, and `mscorlib` for
`CompilerGeneratedAttribute`. Assembly name alone never establishes
authenticity.

A valid method-level `GeneratedCodeAttribute` adds one exact
`MarkerGenerated` contribution to its declaring Type whether or not the method
has a body, sequence points, or a document association. It does not rewrite
the method's mapped-document contributions. Method-level
`CompilerGeneratedAttribute` does not make an ordinary user Type mixed:
compiler lowering marks accessors, lambdas, and other implementation details
this way. The compiler-synthesis rule therefore applies only at Type or
enclosing-Type grain.

Enclosing-Type inheritance follows exact Metadata nesting identity with a
finite relationship bound. A cycle, malformed declaring-Type relationship, or
bound failure yields unknown evidence for affected Types rather than generated
success.

## Type-contribution evidence

The producer classifies evidence at the narrowest available grain before
forming a Type aggregate. One method-to-document association contributes:

1. `CompilerSynthesized` when its declaring Type or an enclosing Type carries
   `CompilerGeneratedAttribute`;
2. `MappedGenerated` when its document has `GeneratedPathEvidence`;
3. `MappedOrdinary` when neither compiler synthesis nor generated-path
   evidence applies and its document has `OrdinaryDocumentEvidence`; or
4. `Unknown` when its document has `UnknownDocumentEvidence` or association
   evidence exceeds a bound.

The exact rule evidence and producer identities remain attached to the
contribution. A shared document is evaluated independently for each exact
method and Type association.

A method-to-document association is a **mapped destination**, not proof of the
physical syntax tree that produced the method. C# `#line` and
`#pragma checksum` can map an ordinary syntax tree to a generated-looking
document path. When Roslyn also embeds the physical document, it records the
actual embedded-source checksum rather than an inconsistent checksum declared
by the pragma. Portable PDB sequence points do not retain a separate
physical-origin identity from which this owner could recover that distinction.

`MappedGenerated` and `MappedOrdinary` therefore state exactly the PDB evidence
observed. They are never renamed `PhysicallyGenerated`, `PhysicallyOrdinary`,
or `Authored` by this owner or a consumer. Direct framework markers remain
separate contribution kinds. This qualification preserves the useful
conventional architecture lens without overstating what Portable PDBs can
prove.

A valid `GeneratedCodeAttribute` directly on a Type or enclosing Type adds one
`MarkerGenerated` Type contribution and its declared tool/version identity. It
does not rewrite that Type's method-to-document associations: the attribute
may come from only one declaration of a partial Type. `MappedOrdinary`
associations therefore coexist with the generated declaration and produce
`MixedEvidence`.

A valid `GeneratedCodeAttribute` on a method follows the same independence
rule at method grain. Its exact method address, declared tool/version identity,
and `MarkerGenerated` contribution remain in the Type row even when the method
is abstract, bodyless, has only hidden sequence points, or has no PDB method
row. A method with both a marker and mapped evidence retains both
contributions.

A valid `CompilerGeneratedAttribute` directly on a Type or enclosing Type
identifies the Type itself as compiler synthesis. It adds a
`CompilerSynthesized` Type contribution and classifies that Type's
method-to-document associations as compiler-synthesized even when their
sequence points refer back to a document with ordinary evidence. This keeps an
async or lambda implementation Type generated-evidence-only without changing
the mapped-ordinary contribution of any neighboring user Type.

Malformed markers add unknown Type-contribution evidence to the exact method's
declaring Type or the exact Type owner and its inherited scope. They do not
require a document association and do not reclassify a shared document.

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
| `OrdinaryEvidenceOnly` | At least one `MappedOrdinary` contribution, no generated contribution, and no unknown evidence. |
| `GeneratedEvidenceOnly` | At least one `MappedGenerated`, `MarkerGenerated`, or `CompilerSynthesized` contribution, no `MappedOrdinary` contribution, and no unknown evidence. |
| `MixedEvidence` | At least one `MappedOrdinary` contribution and at least one generated contribution, with no unknown evidence. |
| `Unknown` | No associated evidence, or any unknown/malformed/bounded evidence. |

An `Unknown` row still retains its known ordinary and generated evidence and
producer identities. Unknown is an aggregate qualification, not evidence
erasure.

Partial Types motivate `MixedEvidence`. A source generator may add members to
a Type that also has mapped-ordinary method bodies. Flattening that Type to
ordinary evidence would hide generated volume; flattening it to generated
evidence would remove ordinary architecture evidence. A consumer chooses
explicitly whether a mixed-evidence Type belongs in a particular lens.

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

- an **ordinary-evidence-only** lens includes only `OrdinaryEvidenceOnly`;
- a **generated-evidence-only** lens includes only
  `GeneratedEvidenceOnly`;
- a mixed-evidence lens includes only `MixedEvidence`; and
- an unknown lens includes only `Unknown`.

No consumer calls `OrdinaryEvidenceOnly` proof of human authorship or physical
syntax-tree origin. A broader "contains ordinary evidence" lens may include
`OrdinaryEvidenceOnly` and `MixedEvidence`, but must name that choice and
report both counts. Presentation labels mapped-document and direct-marker
evidence rather than shortening either to physical provenance.

The current Library name-family design uses authored, generated, and unknown
as three exclusive classes. Before implementation it must consume this
four-way owner-issued disposition, rename its positive class to
ordinary-evidence-only, keep mixed-evidence Types separate, and preserve
contribution-kind counts. That is a Research consumer adjustment, not part of
this Metadata owner.

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
- authentic `netstandard` facade markers and legacy framework marker
  identities;
- unrelated Types sharing a document remain independent when only one method
  or Type has a generation marker;
- an attributed partial Type with an ordinary method contribution is mixed
  rather than having its shared document reclassified;
- an ordinary method plus an attributed abstract method without sequence
  points produces `MixedEvidence`, retaining the abstract method's exact
  `MarkerGenerated` contribution;
- direct and enclosing-Type `CompilerGeneratedAttribute`, while a
  compiler-generated method on an ordinary Type does not make the Type mixed;
- an async or lambda implementation Type marked `CompilerGeneratedAttribute`
  is generated-evidence-only even though its sequence points refer to a
  document with ordinary evidence;
- ordinary-evidence-only, generated-evidence-only, mixed-evidence
  partial-Type, and no-document unknown aggregates;
- an ordinary method mapped by `#line` and `#pragma checksum` to the same
  generated-looking embedded document row produces `MappedGenerated` evidence,
  retains Roslyn's actual embedded-source checksum, and makes no physical-origin
  claim;
- embedded ordinary source remaining unknown rather than ordinary;
- explicit generated output outside the supported path profile remaining
  unknown without an attribute marker;
- `.g.cs` ordinary input remaining ordinary;
- separator variants, dot segments, traversal-like hints, multiple eligible
  path decompositions, invalid Roslyn hint characters and segments, long paths,
  encoding-expanding inert paths, and every finite bound;
- exact artifact/image/PDB binding, duplicate and foreign rows, complete
  counts, deterministic order, and detached lifetime; and
- a standalone PDB without positive PE CodeView correspondence remaining
  unavailable, and malformed enclosing-Type relationships becoming unknown;
- absent, identity-mismatched, malformed, unsupported, and failed PDB outcomes
  remaining distinct from an available all-unknown result.

The real-asset canary builds and inspects `dotnet-inspect.dll` at the pinned
commit. It requires:

- both System.Text.Json and Markout path-declared generator identities;
- the existing valid System.Text.Json `GeneratedCodeAttribute` declarations;
- ordinary-evidence-only, generated-evidence-only, and unknown populations;
- complete population receipts; and
- no path opening or inspected-assembly loading.

The canary records exact counts as change-sensitive evidence, not as a
permanent product invariant.

The implementation canary rebuilt commit
`b8dafb797636da14319e5896052c40ba9e0c1370` in Release, admitted the PE through
an artifact-backed stream, loaded the Portable PDB from a stream, and produced:

| Evidence | Exact count |
| --- | ---: |
| Documents | 3,272 |
| Types | 4,347 |
| Method-to-document associations | 52,973 |
| Authentic generation-marker rows | 19,819 |
| Ordinary-evidence-only Types | 1,411 |
| Generated-evidence-only Types | 2,790 |
| Mixed-evidence Types | 0 |
| Unknown Types | 146 |
| Markout generated-path documents | 244 |
| System.Text.Json generated-path documents | 2,625 |
| Valid System.Text.Json `GeneratedCodeAttribute` rows | 53 |

On Linux x64 with .NET SDK `11.0.100-rc.1.26425.128`, the median of five warm
measurements was 6.2 ms for PE/PDB opening. Temporary phase instrumentation
over five additional warm inspections measured medians of 36.3 ms for document
classification, 211.6 ms for association construction, and 39.7 ms for
aggregate construction. These measurements are implementation evidence, not a
runtime guarantee.

## Production adoption

1. **Metadata producer:** implement the bounded detached document and Type
   provenance result over one exact PE/PDB binding.
2. **Library name families:** #8698 consumes the four dispositions and issues
   separate ordinary-evidence-only, generated-evidence-only, mixed-evidence,
   and unknown populations.
3. **Query and CLI:** the Library name-family operation exposes provenance
   fields and population selection through QuerySpace, complete JSON, Markout,
   and `InspectionEnvelope<TContent>`; the non-default `Name Families` section
   labels every lens explicitly.
4. **Browser/Wasm:** the Library experience consumes the same managed query and
   dispositions without TypeScript path parsing.
5. **Structure consumers:** Library Dependency Structure and Library Metrics
   may adopt explicit lenses through separate focused changes while retaining
   their physical defaults until then.
6. **Project analysis:** the architecture workflow composes qualified
   ordinary, generated, mixed, and unknown evidence with exact-identity Graph
   shape and amplitude.

This owner adds no standalone command and no default section. The first
observable value arrives through the #8698 CLI and Browser/Wasm consumer.

## Non-claims

This owner does not:

- prove human authorship, repository ownership, generator execution, or build
  reproducibility;
- prove the physical syntax-tree origin of a method from its mapped sequence
  points;
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
