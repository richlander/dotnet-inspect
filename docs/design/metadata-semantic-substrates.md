# Metadata semantic substrates

A **Metadata semantic substrate** authenticates higher-level structural meaning
from physical metadata and publishes immutable typed outcomes that independent
higher layers can consume. It sits above raw row and blob decoding and below
consumer semantics: it establishes what the metadata structurally asserts and
takes no position on IL-body attribution, source reconstruction, project policy,
recommendation, or presentation.

The pattern breaks a layering tie. Without it, Analysis and Decompiler either
derive the same metadata meaning independently or one consumes the other's
interpretation. A substrate gives both a host-neutral answer that neither
consumer owns.

## Status and decision

Proposed by
[#5273](https://github.com/richlander/dotnet-inspect/issues/5273). The normative
owner is `ILInspector.Metadata`.

This document defines only the pattern contract: what a substrate may derive,
what it must publish and distinguish, and what remains outside its boundary. It
does not specify how an existing owner adopts the pattern. Each adoption is a
separate focused effort naming this document, one owner at a time, per
[Stage implementation after locking the design](../design-scope.md#stage-implementation-after-locking-the-design).

Analysis and Decompiler are the first named consumers. The next planned
validation is [#5253](https://github.com/richlander/dotnet-inspect/issues/5253).
The substrate and its results remain host-neutral; CLI and browser/Wasm product
paths consume them through shared libraries rather than through host-specific
implementations.

## Precedents

Three existing components establish the shape.

| Component | Meaning authenticated |
| --- | --- |
| `StateMachineRelationshipIndex` | Compiler state-machine claims, kickoff/state-machine pairing, and exact interface roles. |
| `MemorySafetyMetadataIndex` | Module memory-safety rules and member unsafe-contract evidence. |
| `TypeDeclarationResult` and its probe | Type declaration, definition-kind classification, forwarding hops, and module exports. |

They agree on the architectural boundary: each derives a relationship rather
than decoding one field, publishes typed evidence-bearing results, and remains
below consumer policy. Their differences in identity, bounds, and outcome
vocabulary motivate the common contract below; they do not define exceptions
for new substrates.

## Admission test

A meaning is **admissible** as a semantic substrate only when all five
requirements hold. Failing any one keeps it in an ordinary Metadata helper or
in consumer-owned composition.

Admission is necessary, not sufficient. It decides whether a meaning belongs
in a substrate. A component publishing that meaning must also satisfy the
publication, identity, construction, and boundary contracts below.

1. **Derived meaning.** The result establishes a relationship, contract, or
   disposition that no single metadata row or blob states outright. Reading one
   table column or decoding one attribute is a helper.
2. **Metadata-only evidence.** Every published fact follows from metadata
   inside the declared acquisition scope. A meaning requiring an IL body,
   reconstructed control flow, a PDB, source text, or a project file is not a
   substrate fact.

   Metadata evidence has two grades:

   - **Structural** evidence is asserted by metadata tables.
   - **Conventional** evidence follows from a compiler convention, such as a
     generated-name grammar.

   A conventional result must be labelled conventional and carry the matched
   evidence so a consumer can decide whether to trust it. It must never be
   presented as structural. Generated-name evidence uses the shared
   `GeneratedNameGrammar`, not an ad-hoc string test.
3. **Independent multi-consumer demand.** At least two independent derivations
   or reads of the same meaning exist, and at least one is above
   `ILInspector.Metadata`.

   Demand may be shown by **consumption**, where a layer reads an existing typed
   result, or by **duplication**, where independent components derive the same
   meaning. Duplication is the stronger signal because it demonstrates the
   drift the substrate exists to prevent.

   The unit is a published meaning, not a class. A component cannot use demand
   for one family to admit another family bundled beside it. The admitting
   issue and PR carry the current demand evidence; this document maintains no
   cross-component census.
4. **Policy neutrality.** The result states what the metadata establishes, not
   what a consumer should display, recommend, filter, or reconstruct.
5. **Closed outcomes.** Every reachable disposition **of the meaning**,
   including failure, is expressible in the proposed outcome type without
   string parsing or inference from absence.

   A bad coordinate, an unrequested bound, and an exhausted budget are facts
   about the request or the operation rather than dispositions of the meaning.
   They are governed by the publication contract. The converse question -
   whether a published type declares cases that cannot occur for a particular
   semantic coordinate - is tracked separately by
   [#5838](https://github.com/richlander/dotnet-inspect/issues/5838).

### Worked admissions

**Property backing storage - admit; accessor association rides along.** These
are two meanings. Accessor association is structural: `MethodSemantics` states
it directly, so it is a helper rather than an independently admissible meaning.
Backing-storage association is conventional: it rests on the
`<Prop>k__BackingField` grammar
(`src/ILInspector.MetadataPrimitives/GeneratedNameGrammar.cs:57`-`58`), not on a
row asserting the relationship.

The conventional meaning has independent demand. Metadata derives
auto-property backing-field descriptors in
`src/ILInspector.Metadata/ApiSurfaceExtractor.cs:3099`-`3104` and authenticates
matching fields at `:3162`-`3191`. The Decompiler independently constructs and
matches the same backing-field name in
`src/ILInspector.Decompiler/MemberBodyProducer.cs:1630`-`1645`.

The substrate is admitted on property backing-storage association and may
publish the structural accessor relationship needed to identify the property.
It must label the backing result conventional, carry the matched name, remain
policy-neutral, and distinguish associated, absent, and ambiguous outcomes.

**Lambda and local-function raising - reject.** It fails requirement 2. The
meaning depends on IL patterns, captured-variable flow, and reconstructed
control flow. Metadata can authenticate a compiler-generated relationship; it
cannot establish which source construct produced it.

## Outcome vocabulary

Substrates share required distinctions, not one generic result type. A shared
algebra across unrelated domains would either omit a distinction one domain
needs or burden another domain with irrelevant cases.

A substrate declares each distinction reachable through its chosen contract:

- **Resolved** - the meaning was established, with supporting evidence.
- **Absent** - the metadata is well formed and the meaning does not apply.
- **Malformed** - relevant metadata is present but cannot be decoded.
- **Ambiguous** - multiple candidates satisfy the structural test and no rule
  selects one.
- **Unsupported** - the shape decodes but falls outside the declared scope.
- **Budget-limited** - a declared work or resource bound stopped derivation.
- **Unexamined** - the substrate deliberately did not look.
- **Invalid coordinate** - the caller supplied a key outside this reader.

Three rules keep those distinctions usable:

- Never collapse a failure into absence. Unreadable metadata must not become
  success-shaped "not present" output.
- Admitted absence is distinct from unexamined. Not looking is not evidence
  that a meaning is absent.
- Never publish a caller error or an operation-imposed bound as a claim that the
  artifact is malformed.

A substrate may add domain-specific distinctions. It must not add a case whose
only meaning is presentational.

## Identity, evidence, and reader lifetime

A substrate whose consumers can hold coordinates from multiple modules accepts
a scoped identity carrying both module identity and handle. It rejects a
foreign coordinate before reading the row.

Published identities are otherwise durable within their declared scope. A
result coordinate carries the module it belongs to and can detect use against a
different reader. A consumer still validates the row against the target reader
before dereferencing it; module identity is a misuse guard, not authentication.

A substrate whose boundary is explicitly single-module may accept a raw
metadata handle carrying only table and row information. It must range-check the
handle and return a typed invalid-coordinate outcome for an out-of-range row.
It cannot detect an in-range handle originating from a different module, so the
API must document that limit.

Published result values may outlive the `MetadataReader` that produced them.
The substrate object may retain the reader for later queries, but retained
results must not require that reader to explain their meaning.

Evidence travels with the outcome. A result carries the physical observations,
tokens, or relationship steps supporting it so a consumer can explain the fact
without re-deriving it.

## Construction, bounds, and caching

Construction is total over admissible arguments. Hostile, truncated, or
malformed metadata produces typed outcomes rather than escaping exceptions.
Null readers and invalid caller-supplied bounds remain programming errors and
may throw.

Table scans, name resolution, and relationship traversal take explicit work
bounds. Exhaustion produces **Budget-limited**, not `Malformed`, absence, or an
escaping budget exception.

Caching belongs to the consumer. A consumer may create a substrate per
operation or retain one for a reader's lifetime. The substrate introduces no
shared registry or process-wide state spanning readers.

Reader-keyed memoization is permitted when it is observationally transparent
and its lifetime is bounded by the reader. The invariant is keying and lifetime,
not whether an implementation field is `static`.

## Guarantees and consumer boundary

A substrate guarantees:

- derivation from a reader without a consumer interpretation as input;
- immutable, evidence-bearing, policy-neutral results;
- operation only within the declared module and acquisition scope;
- typed visibility of malformed input, exhausted bounds, and invalid
  coordinates; and
- no dependency between independent consumers.

A substrate deliberately leaves these decisions to consumers:

- spelling, filtering, disclosure, severity, and recommendations;
- cross-assembly composition and disagreement resolution;
- caching and operation lifetime; and
- whether, when, and how to adopt the substrate.

An adopting owner may temporarily compare a substrate result with a private
derivation to demonstrate equivalence. The adoption owns that transition and
its observable compatibility; this document neither requires permanent
duplication nor forbids the comparison needed to remove it safely.

## Discovery

A substrate's owning design names this document and supplies its own admission
evidence. This document names only the three precedents above. There is no
registration row, registry service, naming convention, or maintained census.

## Known deviations

| Gap | Tracker | Relation to this contract |
| --- | --- | --- |
| Declaration-probe budget exhaustion is published as artifact malformation | [#5708](https://github.com/richlander/dotnet-inspect/issues/5708) | Deviation |
| Reachable outcome distinctions are collapsed across existing components | [#5730](https://github.com/richlander/dotnet-inspect/issues/5730) | Deviation |
| Whole-table declaration construction lacks a work bound | [#5731](https://github.com/richlander/dotnet-inspect/issues/5731) | Deviation |
| Published row coordinates are not durably scoped to their module | [#5711](https://github.com/richlander/dotnet-inspect/issues/5711) | Deviation |
| A declaration failure type spans unrelated domains with mismatched codomains | [#5750](https://github.com/richlander/dotnet-inspect/issues/5750) | Context deferred to [#5838](https://github.com/richlander/dotnet-inspect/issues/5838), not a deviation from this contract |
| Existing entry points publish result types broader than their observed codomains | [#5754](https://github.com/richlander/dotnet-inspect/issues/5754) | Context deferred to [#5838](https://github.com/richlander/dotnet-inspect/issues/5838), not a deviation from this contract |

## Counterexamples

These meanings remain outside Metadata.

| Concept | Why it is not a substrate |
| --- | --- |
| Async, iterator, lambda, and local-function source reconstruction | Requires IL bodies, captured-variable flow, and reconstructed control flow. |
| Control-flow-derived source forms | Requires the IR and CFG; the result is reconstruction policy. |
| Allocation, span, clone, and caller-reachability analysis | Derived from instructions, call sites, and graph traversal. |
| Source and PDB provenance | Requires evidence outside the assembly. |
| Compiler-generated naming as an origin or kind claim | A name match alone is conventional evidence and cannot authenticate the source construct. |
| C# spelling and printer fidelity | Consumer policy, even when it consumes authenticated facts. |

Metadata may authenticate a relationship asserted by metadata or narrowly
labelled conventional evidence. It cannot promote a name or body pattern into a
structural source claim.

## Gates

This document defines a pattern and introduces no product behavior, so it has no
behavior gate of its own.

Each admitting issue and PR carries the evidence for requirement 3. Each
implemented substrate names gates for its construction totality, bounds,
identity, evidence lifetime, and routing obligations, or records the applicable
property as `unverified`.

An adoption replacing a consumer's private derivation changes behavior wherever
the two disagree. The adopting owner must demonstrate equivalence or document
the intended difference; this document does not prescribe another owner's
gate.

## Non-goals

- No registry, resolver service, maintained census, or generic outcome type.
- No sweep converting existing helpers or consumers.
- No movement of body-derived, source-derived, project-derived, or
  presentation semantics into Metadata.
- No cross-assembly composition and no new acquisition scope.
- No exact-codomain rule for independently consumable outcome positions; that
  focused design belongs to
  [#5838](https://github.com/richlander/dotnet-inspect/issues/5838).

## Open questions

The exact-codomain contract for substrate outcome types is the focused design
question tracked by
[#5838](https://github.com/richlander/dotnet-inspect/issues/5838).
