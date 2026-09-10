# Pairwise library call-use

Pairwise Library call-use answers one focused question:

> Which methods in either of two admitted libraries directly call or construct
> methods defined by the other library?

Tracking: [#6523](https://github.com/richlander/dotnet-inspect/issues/6523),
as a focused prerequisite for the broader feature-relationship experience in
[#6313](https://github.com/richlander/dotnet-inspect/issues/6313).

This document owns the L1 pair result: participant identity, admitted
occurrence kinds, physical evidence, ordering, and completion. Analysis owns
IL-body facts and catalog member correspondence. Inspection graph documents
own graph-wide subjects, relationships, and rendering if a later consumer
projects these occurrences into that envelope.

## Consumer

The first consumer is the CLI:

```console
dotnet-inspect graph libraries \
  --library ./Consumer.dll \
  --library ./Provider.dll
```

The command admits exactly two explicit local managed libraries into one
binding-consistent assembly context. The result supports both readings without
changing stored edge direction:

- local source members that use APIs in the other library;
- target APIs in the other library that the local source members use.

The Browser/Wasm host can consume the same host-neutral query after it has a
two-library selection surface. The query must not depend on CLI paths, console
formatting, or host-specific acquisition.

## Normative claim

Given two distinct exact participants in one `AssemblyContextGroup`, the query
returns every resolved physical `call`, `callvirt`, and `newobj` occurrence
whose attributed caller definition belongs to either participant and whose
exact selected callee definition belongs to the other participant.

The result is deterministic and carries enough evidence to identify:

- the exact source and target acquisition registrations;
- the source and target assembly identities and module version IDs;
- the attributed source method and selected target method;
- the physical evidence method;
- the IL offset and operand token;
- the call kind and exact-target state.

`CatalogCallGraphScope.ResolvedCalls` enforces cross-participant member
correspondence without traversal bounds. `AssemblyPairCallUseQuery` restricts
the admitted call kinds and carries acquisition, analysis, and pair-specific
correspondence completion state.

## Input and identity

The request names two `ResolvedAssemblyReference` values already admitted to
the same `AssemblyContextGroup`.

Participant identity is the acquisition registration, not display name,
simple assembly name, file path, or an independently reconstructed
`AssemblyReferenceIdentity`. The result also retains assembly identity,
provenance, and module version ID for durable explanation.

The participants form an induced pair. The query does not traverse to a third
library, widen through transitive references, or infer a relationship merely
because one assembly references another.

The request is invalid when:

- either registration is absent from the group;
- both values identify the same registration.
- both registrations describe the same physical assembly artifact, identified
  by equivalent assembly identity and module version ID; the Analysis catalog
  intentionally canonicalizes one physical artifact and cannot attribute its
  definitions to two pair endpoints.

## Occurrence currency

One result row is one physical IL occurrence. Rows are not deduplicated by
source method, target method, type, library, or display spelling.

An occurrence consists of:

| Field | Meaning |
| --- | --- |
| Source participant | Exact participant owning the attributed caller |
| Source method | Attributed semantic caller definition |
| Target participant | Exact participant owning the selected callee definition |
| Target method | Selected exact callee definition |
| Evidence method | Physical method body containing the instruction |
| IL offset | Instruction offset in the evidence method |
| Operand token | Metadata token encoded by the instruction |
| Call kind | `call`, `callvirt`, or `newobj` |
| Exact target | Whether the static operand fixes the runtime target |

Attributed callers and physical evidence methods may differ for compiler-
generated bodies. Both identities remain visible; the result must not rewrite
the physical receipt to look like it occurred in the attributed source method.

`ldftn`, `ldvirtftn`, and `calli` are outside the L1 result. They remain
available as Analysis evidence for a later relationship design but are not
silently relabeled as direct calls.

## Correspondence and direction

Analysis resolves call-site member references and method specifications against
the fixed catalog generation for the pair. A row is admitted only when:

1. the caller definition is physically stored in the source participant;
2. exactly one definition in the other participant corresponds to the callee;
3. the correspondence is established either by the catalog projection or by
   the operand's exact assembly-reference identity plus its complete normalized
   open member signature.

The metadata-signature fallback exists for a narrow case: a third-party type in
the member signature may be unavailable even though both sides carry the same
complete signature and the operand names the exact target assembly identity.
It does not match by simple assembly name or display text, and version-skewed
assembly references remain unresolved.

The stored semantic direction is always caller to callee. Reversing the order
of the two request arguments does not reverse an occurrence or make one
participant the permanent "consumer."

Calls to third participants and calls resolved within the same participant are
excluded from the pair rows. Their existence does not become pair evidence.

## Completion

No rows means "no observed pair call-use" only when `IsComplete` is true.

The result is complete only when:

- both participant images were acquired;
- both images produced full Method Evidence indexes;
- neither index reported a body-analysis diagnostic;
- every admitted call operand naming the other participant matched one exact
  selected definition in that participant.

An unavailable or malformed participant produces a typed failure beside any
available participant state. The query does not throw away successful
participant evidence and does not return a success-shaped empty result.

An unresolved or incomplete call naming the other participant may have
targeted it, so that pair-specific gap prevents an absence claim even when the
result contains some exact rows. Exact rows remain useful positive evidence in
an incomplete result. Unrelated unresolved calls to framework or third-party
assemblies do not make the pair incomplete.

For completion, an unresolved assembly reference names the selected participant
when its name, normalized culture, and public-key token match. Its version may
differ so that genuine version skew remains visible; a same-simple-name
reference with a different culture or public-key token is unrelated.

The query does not claim that virtual dispatch is closed. A `callvirt`
occurrence identifies its selected static operand. `DirectCall.ExactTarget`
continues to disclose whether that operand fixes the runtime target.

## Ordering

Occurrences are ordered by:

1. source assembly name;
2. source module version ID;
3. source method token;
4. target assembly name;
5. target module version ID;
6. target method token;
7. IL offset;
8. operand token.

This order is independent of request argument order and preserves repeated
physical call sites.

## CLI projection

`graph libraries` initially lowers the typed occurrence rows as a table-shaped
edge list. It does not fabricate assembly-level `call` edges or package-level
call semantics.

The default human view summarizes source-member, target-member, and call-site
counts per observed direction, then shows the directed source and target member
pair, call kind, evidence method, and physical IL offset. The evidence method
remains visible even when it equals the attributed source method so generated-
body locations cannot appear to belong to the declared method. Structured
formats retain the exact occurrence rows, including full assembly identities,
source and target MVIDs and method tokens, and the evidence method MVID and
token.
Row windows apply once before human summaries and table rendering. Counts
operate on the same windowed occurrences, not unique methods.

If a later slice projects the result into `InspectionGraphDocument`, every
rolled-up library edge must retain the member-level occurrence receipts behind
it and use a relationship descriptor that explicitly admits that lens.

## Explicit non-goals

This L1 contract does not include:

- signature-only type or member references;
- inheritance or interface implementation;
- field access;
- delegate, reflection, or dynamic-dispatch inference;
- public-entrypoint reachability;
- transitive dependency paths;
- automatic feature naming or clustering;
- breadth, depth, leverage, or importance scores;
- package, project, or network acquisition.

`depends` continues to own dependency and reference traversal. Top Leverage
continues to describe internal call-graph importance. Performance Triage
continues to describe local optimization candidates. Integrations continues to
describe curated ecosystem concepts and observed Integration signals. None of
those facilities manufactures pairwise call-use evidence.

## Pathological evidence

Contract tests cover:

- exact `call`, `callvirt`, and `newobj` occurrences;
- repeated physical sites to the same target;
- several source methods using one target;
- one source method using several targets;
- request-order independence;
- exclusion of same-library and third-participant calls;
- unresolved correspondence making absence incomplete;
- participant acquisition and invalid-image failures.

Repository dogfood uses `DotnetInspector.Presentation` and
`DotnetInspector.MetadataRendering` against their resolved Markout library.
The result must reproduce the exact existing Markout call-use examples before
feature clustering or breadth/depth measures are designed.
