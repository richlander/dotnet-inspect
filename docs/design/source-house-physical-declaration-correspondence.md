# SourceHouse physical-declaration correspondence

## Status and ownership

This document is the normative owner for SourceHouse physical-declaration
correspondence, tracked by
[#6584](https://github.com/richlander/dotnet-inspect/issues/6584). It is
DocumentationHouse production-adoption slice 13 under
[#6579](https://github.com/richlander/dotnet-inspect/issues/6579).
The first typed in-memory issuer and SourceHouse validator path is
DocumentationHouse slice 14, tracked by
[#7859](https://github.com/richlander/dotnet-inspect/issues/7859).

The one claim is:

> Given one exact attestation-backed SourceHouse authored-source result for a
> supported TypeDef or MethodDef, its exact Library and selected assembly
> content, one host-authorized build attestation bound to that direct-emitted
> module and the same attestor-issued physical source-input identity, exact
> source bytes, and finite work, SourceHouse issues resource-free
> correspondence to one raw physical C# declaration or typed non-success. The
> result preserves request, policy, Library, artifact, module, source input,
> attestation, and declaration association without deriving authority from
> Portable PDB mapped locations, paths, names, content equality, display text,
> or declaration proximity.

SourceHouse owns:

- the accepted physical-declaration attestation contract;
- association of an attestation with one exact SourceHouse request, result,
  target, Library content reference, module, and physical source input;
- validation of the direct-emitted module target, attestor-issued physical
  source-input identity, and exact source bytes;
- the closed correspondence outcome;
- finite-work policy and evidence; and
- the detached correspondence identity and generation consumed by later
  DocumentationHouse integration.

The build attestor owns its build authorization, Roslyn compilation, raw syntax
selection, final-artifact observation, authentication mechanism, and issuance.
Metadata continues to own exact TypeDef and MethodDef locations and compiler
XML-documentation identity. SourceLink continues to own PDB interpretation,
document mapping, checksum evidence, and source decoding. CSharpText remains
model-free and owns declaration and attached-comment recognition inside a
caller-vouched physical span. DocumentationHouse consumes SourceHouse
correspondence but does not construct or weaken it.

This design does not redefine those adjacent owners.

## Product question

**Did this one physical declaration produce this exact Metadata target in this
exact module?**

```text
Correspond(
    SourceHouse request and authored result,
    exact Library implementation assembly,
    authorized build attestation,
    attestor-issued physical source-input identity,
    exact source bytes)
  -> Exact
   | Unavailable
   | Conflict
   | Rejected
   | Failed
   | Incomplete
```

This is stronger than asking which document and lines a debugger reports for a
MethodDef. It is also narrower than proving that a repository, source archive,
or build is generally trustworthy. One exact result authorizes only one target,
module, physical source input, source content, and raw declaration span.

## Why Portable PDB evidence is insufficient

A Portable PDB associates a MethodDef row with sequence points and document
rows. A recognized document checksum can authenticate candidate bytes for that
document. Neither record names the physical syntax tree that produced the
MethodDef.

`#line` can route sequence points from one compilation input into another real
document. The destination may have valid Source Link provenance, exact
checksum-valid bytes, and a plausible declaration. `#pragma checksum` can
prescribe the checksum attached to a mapped document. Embedded source makes
document bytes available but does not add a MethodDef-to-physical-declaration
relation.

The same limitation applies to TypeDefinitionDocument custom debug
information. It associates a TypeDef with one document, not one declaration
span, and cannot represent every part of a partial type.

PDB, Source Link, embedded source, source checksums, and compiler source-file
counts therefore remain useful acquisition and integrity evidence. None is
physical-declaration authority.

## Demo and motivating asset

The real production-shaped subject is
`CSharpText.MemberSlicing.MemberTextSlicer.ExtractMemberText` in this
repository. Its built assembly, Portable PDB, Source Link source, and XML
documentation already exercise SourceHouse member-parts acquisition.

The proposed build evidence binds:

```text
final CSharpText.MemberSlicing module digest and MVID
  + exact ExtractMemberText MethodDef address
  + compiler XML ID for that MethodDef
  + attestor-issued MemberTextSlicer.cs physical source-input identity
  + exact MemberTextSlicer.cs source digest and encoding
  + raw UTF-16 declaration span and syntax kind
  + authorized attestor identity and generation
```

Given that row and an attestation-backed SourceHouse result carrying the same
physical source-input identity and exact bytes, the expected result is one
`Exact` correspondence. CSharpText can later inspect the vouched span without
searching the file by method name.

The pathological neighbor maps another compilation input into
`MemberTextSlicer.cs` with `#line` and reuses that document's valid checksum.
PDB and source integrity may both succeed. The build attestation still names
the originating input and raw declaration span, so the mapped destination
cannot satisfy correspondence for the requested MethodDef.

This is a contract mockup. Slice 14 supplies the first production SourceHouse
issuer and Release gate.

## Evidence basis

### Host-authorized build attestation

Exact correspondence requires an attestation from a producer authorized by the
SourceHouse operation plan. Attestation bytes, a package sidecar, a signature,
a repository path, or a familiar builder label do not authorize themselves.
The host supplies the capability that validates the producer's authentication
and identifies the accepted issuer and attestation generation.

An accepted initial-profile attestation relates:

- one direct compiler-emitted managed module, bound by exact content digest and
  non-empty MVID;
- one TypeDef or MethodDef address in that module;
- the compiler XML-documentation identity projected for that exact target;
- one opaque physical source-input identity minted for one syntax tree in that
  exact compilation, scoped by issuer and attestation generation;
- one attestation-backed physical source contribution carrying that same
  identity, exact content digest, encoding, and bytes;
- one zero-based raw UTF-16 declaration span and declaration syntax kind;
- the attestor identity, format version, and generation; and
- the compiler/build identity needed to interpret the attestation profile.

The exact direct-emit module is the attestation subject, not a same-named
assembly, an earlier compiler output, or a later transformed module. A linker,
rewriter, or other post-emit transform can preserve a unique compiler
documentation ID while replacing, merging, or synthesizing its Metadata target.
The initial profile therefore makes every changed post-emit module
`Unavailable`; a new digest or attestation alone cannot restore lineage. A
future profile may admit one transform only when its separately authorized
evidence proves exact input-target-to-output-target lineage.

Source content uses exact bytes. Line-ending-normalized checksum acceptance may
remain sufficient for ordinary SourceHouse authored-source acquisition, but it
cannot satisfy this stronger correspondence because raw character spans would
address different text.

The physical source-input identity distinguishes compilation inputs, not text.
Two inputs with equal paths, URLs, PDB document rows, digests, encodings,
bytes, and spans remain different identities. The identity is issuer-scoped
and build-scoped and has no caller-constructed equality outside its attestation
generation. It is not a hash or a normalized path.

An authorized attestation-backed source capability supplies the source bytes
for that identity and causes SourceHouse to retain the identity in the
authored-source result. The capability may use an issuer-owned embedded source
record or retrieval descriptor, but the resulting provenance must name the
same attested input. Ordinary PDB, Source Link, local-file, or repository source
results do not gain this identity from equal bytes.

### Build-time declaration bridge

The initial implementable producer profile uses Roslyn at build time:

1. the build attestor obtains a source symbol's compiler documentation ID and
   raw declaring syntax reference from the exact compilation and mints one
   opaque identity for its physical syntax-tree input;
2. it rejects implicit, generated, partial, or multiply declared shapes outside
   the supported profile;
3. it observes the exact module bytes produced by that same compiler emission,
   before any linker or rewriter, and accepts the row only when the same
   compiler documentation ID identifies exactly one supported target; and
4. it binds that exact metadata address and direct-emit module identity to the
   physical source-input identity, attestation-backed source material, raw
   source digest, encoding, span, and syntax kind.

Roslyn's `ISymbol.DeclaringSyntaxReferences` exposes physical declaring syntax
and explicitly distinguishes implicit and multi-location symbols.
`ISymbol.GetDocumentationCommentId` supplies the same compiler identity family
that Metadata projects as `XmlDocMemberIdentity`.

Compiler XML IDs deliberately erase some metadata distinctions. Custom
modifiers are absent, and function-pointer parameters can produce an empty
parameter projection. The attestor must therefore establish uniqueness in the
direct-emit module. A duplicate or unprojectable ID cannot be resolved by
source order, metadata row order, signature display, or proximity; it produces
non-success. Uniqueness is only the bridge across one compiler emission; it is
not lineage evidence through a later transformation.

This bridge does not add Roslyn to a product runtime. SourceHouse validates the
issued attestation with its existing content-first, SRM-only mechanisms. The
build producer may use Roslyn because it runs beside the compilation whose
physical syntax it attests.

### Supporting evidence and analogues

| Mechanism | Useful evidence | Boundary |
| --- | --- | --- |
| Portable PDB MethodDebugInformation and document checksums | Exact MethodDef row, mapped debugger locations, and candidate-document integrity | No physical syntax-tree or declaration-span identity |
| TypeDefinitionDocument | One compiler-selected document associated with a TypeDef | Optional, one document only, and not a declaration or partial-type inventory |
| Source Link and embedded source | Retrieval provenance or bytes for one PDB document | No target-to-physical-declaration relation |
| Roslyn declaring syntax plus compiler documentation ID | Build-time symbol-to-raw-syntax relation and a bridge to Metadata's existing compiler identity | Requires a trusted producer, direct-emit uniqueness, and no post-emit transform; ordinary emit exposes no general symbol-to-token map |
| Deterministic build inputs | Evidence that the same complete inputs reproduce the same output | Forward reproducibility, not per-target reverse provenance |
| SLSA/in-toto-style provenance | Authorized builder identity, artifact subject digest, and build materials | Artifact-level provenance; no declaration-to-Metadata relation |

The design adopts the attestation pattern of binding an authorized producer to
an exact artifact subject and materials, then adds the missing per-target
relation. It does not treat a general supply-chain attestation as declaration
evidence.

No external code is transferred.

## Request and association

One correspondence request consumes:

- the exact SourceHouse request and completed attestation-backed authored-source
  result;
- the exact `LibraryReference` and selected implementation-assembly
  `LibraryContentReference`;
- the exact TypeDef or MethodDef target selected by that SourceHouse request;
- the source-result policy, PDB policy, request identity, operation-plan
  identity, and policy generation;
- the exact selected physical source input, its attestor-issued identity,
  source-material evidence, decoded bytes, encoding, and source-result
  identity;
- one finite correspondence plan; and
- zero or more contributions from host-authorized attestation capabilities.

Operation eligibility is resolved before any attestation capability is invoked
or contribution is enumerated. The completed authored-source result must have
been issued by an authorized attestation-backed source capability and must
carry its attestor-issued physical source-input identity. An ordinary PDB,
Source Link, embedded-source, local-file, or repository result lacks that
authority and makes physical correspondence `Unavailable`. Offered attestation
contributions are not decoded, authenticated, or compared with an ineligible
result. Their presence cannot turn missing applicable source evidence into
`Rejected`.

SourceHouse always retains the exact module and Metadata target evidence for an
available authored-source result. When that target has no unique compiler XML
identity, the identity is absent, the correspondence is `Unavailable`, and no
attestation capability is invoked; this cannot fail or weaken ordinary source
acquisition.

Contribution admission and aggregation are ordered:

1. SourceHouse enumerates the complete bounded contribution set, decodes it,
   and validates each claimed issuer authorization. Bound exhaustion is
   `Incomplete`; an operational decode, authentication, hashing, or inspection
   failure is `Failed`; a completed authentication denial is `Rejected`.
2. Every decoded authorized contribution must match the request, plan, policy,
   Library, content reference, Artifact generation, module digest and MVID,
   Metadata target, source-result identity, attestor-issued physical
   source-input identity, exact source bytes and encoding, attestation
   generation, and validation-profile version authorized by the plan. Its
   declaration span must be non-negative and within the exact decoded source
   under overflow-safe arithmetic, and its syntax kind must be permitted for
   the attested target profile. Any association mismatch or invalid claimed row
   makes the operation `Rejected`. A valid contribution cannot mask a stale,
   incorrectly indexed, malformed, out-of-bounds, or target-incompatible one.
3. A contribution becomes **accepted** only after all those associations and
   row-validity checks pass. Source-bounds validation does not traverse the
   span or charge its declared-length work limit. An otherwise valid in-bounds
   span that exceeds that plan limit produces `Incomplete`; an out-of-bounds
   span remains `Rejected`. If no accepted row exists because the supported
   target has no unique compiler identity or no authorized attestation row was
   supplied, the result is `Unavailable`. Rejected rows never become
   `Unavailable`.
4. Accepted contributions are aggregated only after every contribution passes
   the preceding stages. Equal target, source, raw span, and syntax kind may
   corroborate one `Exact` result. Contributions that agree on every
   request-bound association but disagree on raw span or syntax kind produce
   `Conflict`.

After caller cancellation, operation eligibility is decided first. For an
eligible request, contribution outcome precedence is `Incomplete`, `Failed`,
`Rejected`, then `Unavailable`, `Conflict`, or `Exact`. The first three
contribution outcomes prevent aggregation; source order, capability order, and
first success do not affect the result.

The selected assembly content reference supplies the Artifact identity and
generation. SourceHouse inspects the same assembly bytes to validate the
attested digest, MVID, target address, and compiler documentation identity.
Equivalent assembly name, version, public-key token, path, or MVID from another
Library does not substitute.

The source result and attestation must carry the same attestor-issued physical
source-input identity and exact source bytes and encoding. A path, Source Link
URL, PDB document identity, content digest, or byte equality may be retained as
provenance but never constructs or substitutes for that join. A source result
without the identity is ineligible and produces `Unavailable`, even if its
bytes match. The attested span must be in bounds for the exact decoded text and
must name the attested declaration syntax kind.

The correspondence is issued for the current SourceHouse request and result.
It cannot be replayed under another request, Library content reference,
Artifact generation, operation plan, policy generation, source-result identity,
physical source-input identity, attestation generation, or parser profile.

## Initial supported profile

The initial exact profile admits:

- one non-implicit C# TypeDef with exactly one declaring syntax reference; and
- one non-implicit, non-accessor C# MethodDef with exactly one supported
  declaration syntax reference and one unique direct-emit-module compiler
  documentation identity.

The attested module must be the exact direct output of the Roslyn emission that
supplied the symbol and syntax evidence. Post-emit transformed modules are
outside the initial profile.

Constructors and operators are eligible when they satisfy the same profile.
Partial types, partial members, generated syntax, synthesized methods,
accessors, bodyless declarations, and targets without one compiler
documentation identity are initially `Unavailable`. Their exclusion prevents
an apparently exact result from hiding multiple physical declarations or no
physical declaration. A later owner-scoped extension may add one shape only
with its own exact producer evidence and pathological gate.

Nested declarations and overloads are not exclusions. Their exact declaring
syntax and compiler identity must remain unique under the profile. Aliases and
conditional compilation are settled by the exact compilation observed by the
attestor; runtime code does not re-evaluate source conditions. The attested
direct-emit module and raw syntax span are the evidence.

The raw span addresses the complete declaration node selected by the attestor.
It is not a PDB sequence-point range, logical `#line` range, documentation
comment span, body-only span, or line-only approximation. CSharpText decides
which attached trivia belongs to that declaration in slice 15; SourceHouse does
not make that lexical decision here.

## Outcomes

The closed outcome family is:

- **Exact** — every required association validates and every accepted
  contribution names the same one target, physical source input, source
  content, and raw declaration;
- **Unavailable** — no authorized applicable evidence exists, the target is
  outside the supported profile, its compiler documentation identity is not
  unique, no accepted attestation row names it, or the SourceHouse result lacks
  the attestor-issued physical source-input identity;
- **Conflict** — fully associated, independently accepted contributions name
  different raw declaration spans or syntax kinds for the same exact request,
  target, module, physical source-input identity, and source content; the
  result retains bounded descriptors for every conflicting contribution;
- **Rejected** — an owner-issued input or claimed association names the wrong
  request, Library, content, Artifact generation, module, target, source result,
  physical source-input identity, source content, policy, attestation
  generation, or authorized validation-profile version, or carries a malformed,
  out-of-bounds, or target-incompatible declaration span or syntax kind;
- **Failed** — authorized attestation decoding, module inspection, source
  decoding, hashing, or target validation fails; and
- **Incomplete** — a declared byte, record, source, candidate, span, or deadline
  bound prevents authoritative completion.

Caller cancellation remains cancellation. It does not become `Unavailable` or
`Incomplete`.

Equal corroborating contributions may yield `Exact`; they do not multiply the
declaration. Their bounded issuer and generation evidence is retained.
SourceHouse never selects the first declaration, first metadata row, first
attestor, or highest-precedence text when evidence is non-unique or conflicts.

`Unavailable` does not weaken an otherwise available authored-source result.
It means only that the result cannot yet authorize exact
physical-declaration-dependent consumers such as authored documentation.

## Result identity and lifetime

An `Exact` result retains:

- the SourceHouse request, source-result, plan, and policy identities;
- the exact Library and implementation-assembly content reference;
- Artifact, content, and module identities and generations;
- the exact Metadata target address and compiler documentation identity;
- the attestor-issued physical source-input identity, exact digest, encoding,
  and raw declaration span;
- the attestor identity, profile, and generation;
- accepted corroboration evidence;
- charged work and finite limits; and
- one opaque correspondence identity and generation.

Non-success results retain the same request association and bounded evidence
needed to explain the outcome.

Every result is detached and resource-free. It retains no Library lease,
borrow, reader, stream, callback, source capability, attestation capability,
authentication key, or reopening authority. Content access and lease settlement
remain owned by the surrounding SourceHouse operation.

## Replacement and freshness

Correspondence is immutable evidence about one completed relation. It is not a
claim that a path, repository branch, cache key, or display coordinate remains
current.

A changed Library content reference, Artifact generation, module digest or
MVID, physical source-input identity, source digest or encoding, SourceHouse
result identity, policy generation, attestation generation, or
validation-profile version requires a new correspondence operation. Equal
paths, assembly identities, XML IDs, source text, or declaration spans do not
transfer evidence between generations.

The join currency consumed by DocumentationHouse is the owner-issued
correspondence identity and generation together with the exact SourceHouse
result, target, Library content, physical source input, and declaration span.
A consumer does not reconstruct that tuple from its fields.

## Finite work

The host-authorized correspondence plan bounds:

- attestation bytes and records;
- accepted issuers and contributions;
- module bytes inspected;
- source bytes and decoded characters;
- attested physical source-input records;
- target candidates and corroborating rows;
- retained conflict descriptors;
- declaration span length; and
- an absolute deadline.

Hashing, Metadata projection, attestation decoding, and source-span validation
charge those bounds. Exceeding one produces `Incomplete` with the named
boundary. A shortened manifest or candidate set never establishes
`Unavailable`, uniqueness, or `Exact`.

## Security and platform compatibility

Assembly, PDB, source, and attestation bytes may originate from untrusted
internet content. Exact correspondence requires a separately authorized
attestor capability and validates every artifact association before publishing
`Exact`. Unauthenticated or unauthorized bytes cannot manufacture provenance.

The SourceHouse validator is host-neutral, pathless, SRM-only, Roslyn-free, and
does not load or execute the inspected assembly. It accepts content-backed
inputs and can run under NativeAOT and single-threaded Browser/Wasm. The
build-time attestor is outside the product inspection path and may use Roslyn.
These are inherited product-path requirements. The existing
`product-libraries-use-repository-and-platform-assemblies` dependency-policy
rule supplies full composition-gate coverage against a product SourceHouse
library acquiring Roslyn or another external runtime dependency. A later
implementation that introduces a platform-specific API or dependency must
declare and gate that exception rather than treating this inheritance as proof.

The design adds no defense against trusted in-process callers deliberately
constructing impossible private state, local same-machine interference, or
files changing during inspection. Existing owner-issued content generations
govern local replacement.

## Pathological cases

### Valid mapped destination belongs to another declaration

One compilation input uses `#line` to map a MethodDef into another real source
document. The destination bytes satisfy their PDB checksum and Source Link
mapping and contain a plausible declaration. The stronger neighbor makes the
destination byte-for-byte identical to the originating input.

The attestation names the originating syntax tree's physical source-input
identity, exact bytes, and raw span. The PDB-selected destination has no such
identity and therefore cannot satisfy the source association, regardless of
content equality. SourceHouse retains ordinary mapped-source evidence but
physical correspondence is `Unavailable`. Separately, an otherwise eligible
attestation-backed result whose physical source-input identity disagrees with
an admitted authorized contribution is `Rejected`.

### Prescribed checksum is not provenance

`#pragma checksum` supplies the checksum emitted for a mapped document. The
document bytes pass ordinary checksum verification. No authorized attestation
binds those bytes to the target's raw declaring syntax, so correspondence is
`Unavailable`.

### Reordered overloads do not cross-match

Two overloads share a name and move within one source file. Each attestation row
binds a compiler identity and exact final Metadata address to a raw syntax span.
Row order, source order, and method name are irrelevant. A compiler-ID collision
is `Unavailable` rather than resolved by position.

### Partial and synthesized declarations stay non-exact

A partial type has two declaring syntax references, or a generated MethodDef
has no explicit declaring syntax. Neither is reduced to the first document or
nearest declaration. The initial profile returns `Unavailable`.

### Stale generation cannot replay

An attestation correctly names the source and module from an earlier build, but
the SourceHouse request selects a replacement Artifact generation with
equivalent assembly identity. Exact digest, content-reference, MVID, and
generation association rejects the stale evidence.

## Production adoption

This design is the current slice in the existing DocumentationHouse plan:

1. **Slice 13 — complete:** lock SourceHouse physical-declaration
   correspondence and its build-attestation evidence basis.
2. **Slice 14 — current implementation:** provide one production build-time
   issuer and SourceHouse validator path with a real TypeDef and MethodDef.
3. **Slices 15–16:** lock and implement CSharpText declaration-attached
   documentation extraction under #6583.
4. **Slices 17–19:** adapt SourceHouse correspondence and CSharpText evidence
   into DocumentationHouse and Queries.
5. **Slices 20–21:** adopt the same host-neutral authored-documentation result
   in Inspect Web and the CLI, then retire remaining `SourceEnricher`
   documentation composition.
6. **Slice 22:** delete `DocCommentParser` after every consumer is gone.

The first implementation uses this repository's
`MemberTextSlicer.ExtractMemberText` source in a direct build-time emit owned by
`SourceBuildAttestation`, then supplies that exact PE, Portable PDB, physical
source input, and typed attestation through ordinary Library and SourceHouse
settlement. Roslyn remains outside the SourceHouse runtime graph. The path is
typed and in-memory; authentication and serialization remain issuer-owned
non-claims. Release gates demonstrate both the exact row and the
checksum-valid, byte-identical mapped-destination pathological case.

There is no rendering change. Correspondence is typed producer evidence;
DocumentationHouse and host presentation retain their existing owners.

## Required gates

The implementation must provide Release gates for:

- one real TypeDef and ordinary MethodDef issuing exact detached
  correspondence from an authorized attestation;
- exact request, policy, Library, implementation content, Artifact generation,
  module digest, MVID, Metadata target, source result, attestor-issued physical
  source-input identity, source digest, encoding, span, attestor, and profile
  association;
- one valid contribution beside one stale or incorrectly indexed target,
  module, source, policy, or generation contribution producing `Rejected`
  rather than `Exact` or `Conflict`;
- fully associated contributions that disagree only on raw declaration span or
  syntax kind producing `Conflict` with bounded evidence;
- a lone out-of-bounds or target-incompatible row, and a valid row beside the
  same invalid evidence, both producing `Rejected`;
- an otherwise valid in-bounds span exceeding its declared work limit producing
  `Incomplete` rather than `Rejected`;
- a `#line` mapping into another real checksum-valid source document, including
  a byte-identical copy of the physical input, failing to authorize that
  destination declaration;
- an ordinary identity-less PDB or Source Link result beside an otherwise
  matching offered attestation short-circuiting to `Unavailable` without
  invoking the attestation capability;
- two byte-identical physical compilation inputs retaining distinct
  attestor-issued identities that cannot be reconstructed from their bytes,
  paths, PDB rows, encodings, or spans;
- `#pragma checksum` evidence remaining insufficient without an attestation;
- reordered overloads, nested declarations, aliases, and active conditional
  compilation retaining the exact attested target and span;
- compiler-ID collision, partial declarations, generated declarations,
  accessors, and bodyless declarations remaining non-exact under the initial
  profile;
- changed module, source, Artifact, result, policy, attestation, and profile
  generations requiring new correspondence;
- a post-emit transform preserving one unique compiler documentation ID
  remaining non-exact without separately authorized target-lineage evidence;
- malformed attestation, malformed Metadata, invalid source encoding,
  finite-work exhaustion, and cancellation remaining visible; and
- results retaining no live resource or authority.

## Non-claims

This owner does not define:

- build-system authorization, authentication formats, key management, or
  supply-chain policy;
- post-emit linker or rewriter target lineage;
- Roslyn compilation, syntax, symbol, or emit semantics;
- Metadata addresses, compiler XML-ID grammar, or API extraction;
- Library realization, content roles, borrowing, or retirement;
- PDB identity, sequence-point mapping, Source Link, source transport, checksum
  policy, or decoding;
- CSharpText declaration recognition or documentation-comment parsing;
- DocumentationHouse demand, channel settlement, field policy, or rendering;
- correspondence for properties, events, fields, accessors, bodyless,
  generated, synthesized, or multiply declared source shapes in the initial
  profile;
- general repository or source authenticity;
- reproducible-build proof, whole-assembly source completeness, or semantic
  equivalence; or
- decompiled text as source or documentation provenance.

## References

- [Portable PDB format][portable-pdb]
- [Roslyn `ISymbol.DeclaringSyntaxReferences`][declaring-syntax]
- [Roslyn `ISymbol.GetDocumentationCommentId`][documentation-id]
- [Roslyn deterministic inputs][deterministic-inputs]
- [SLSA build provenance][slsa-provenance]
- [PDB and source acquisition](../pdb-acquisition.md)
- [SourceHouse](source-house.md)
- [DocumentationHouse](documentation-house.md)
- [Type, member, and API representation](type-member-api-representation.md)

[portable-pdb]: https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md
[declaring-syntax]: https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.isymbol.declaringsyntaxreferences
[documentation-id]: https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.isymbol.getdocumentationcommentid
[deterministic-inputs]: https://github.com/dotnet/roslyn/blob/main/docs/compilers/Deterministic%20Inputs.md
[slsa-provenance]: https://slsa.dev/spec/v1.2/build-provenance
