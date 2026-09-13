# Pairwise library call-use

Pairwise Library call-use answers one focused question:

> Which methods in either of two admitted libraries directly call or construct
> methods defined by the other library?

Tracking:

- [#6523](https://github.com/richlander/dotnet-inspect/issues/6523) — exact
  pair occurrence evidence;
- [#6602](https://github.com/richlander/dotnet-inspect/issues/6602) — direct-use
  summary projections;
- [#6694](https://github.com/richlander/dotnet-inspect/issues/6694) — typed
  Markout serialization;
- [#6313](https://github.com/richlander/dotnet-inspect/issues/6313) — broader
  feature-relationship experience.

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

The first summary vocabulary is deliberately mechanical:

- a **consumer use site** is one attributed source method with one or more
  exact pair occurrences;
- a **provider API type** is the structured declaring type of one or more
  exact selected target methods.

These summaries expose useful typed surfaces before the product has a semantic
feature-cluster owner. They do not claim that a direct use site is a public
entry point or feature, or that a provider declaring type is a public API
boundary or cohesive capability.

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

Complete retained signature identity includes custom-modifier payloads and each
function-pointer header, generic and required parameter count, return type,
parameter type, and nested exact type origin. Structural graph identity alone
is not sufficient correspondence-plan cache identity.

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

## Direct-use projections

`AssemblyPairCallUseProjection` derives two summary row sets from the exact
ordered occurrences. It does not reopen either image, rebuild correspondence,
or alter pair completion.

Each occurrence appears exactly once in each projection:

1. **Consumer use sites** group by source registration, source module version
   ID, source MethodDef token, and target registration. A row retains the exact
   source method, the distinct structured target declaring types, the distinct
   target methods, and the indexes of every supporting occurrence.
2. **Provider API types** group by source registration, target registration,
   target module version ID, and the structured target declaring type. A row
   retains the distinct source methods, the distinct target methods, and the
   indexes of every supporting occurrence.

Registration, module, token, and structured type identity establish group
membership. Display spelling is not identity. Occurrence indexes address the
original `AssemblyPairCallUseResult.Occurrences` array and therefore retain the
complete physical receipts without copying or reminting evidence.

Groups and their retained distinct values are ordered by first supporting
occurrence. Because the occurrence order is deterministic and independent of
request argument order, both projections inherit those properties. Repeated
physical sites increase the call-site count but do not increase distinct
method or type counts.

Positive summary rows remain useful when pair evidence is incomplete. The
projection preserves the pair result and its completion state; it never turns
partial positive evidence into a complete breadth or absence claim.

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
token. Row windows apply once before human summaries and call-site table
rendering. Counts operate on the same windowed occurrences, not unique methods.

The command exposes three sections:

| Section | Meaning |
| --- | --- |
| `Consumer Use Sites` | One row per attributed source method and directed target participant, with distinct provider-type and target-member counts |
| `Provider API Types` | One row per structured target declaring type and directed source participant, with distinct source- and target-member counts |
| `Call Sites` | The exact physical occurrence rows |

The CLI represents this document and its three row sets as typed Markout views.
`MarkoutSerializer` and one generated serializer context own section schemas,
projection, and format lowering. The command may choose the appropriate
document or table view for the selected shape, but it does not construct a
`MarkoutWriter`, manually write headings, paragraphs, or tables, or maintain a
parallel list of rendered column labels. No host-specific rendering exception
is approved for this command.

Omitting `-S` preserves the exact call-site view. Bare `-S` selects the two
summary sections; an exact section name selects one projection, and normal
section discovery describes their schemas without acquiring the libraries.
Tabular streams require one selected section, while Markdown and JSON may
carry several.

Row windows apply independently to the selected section rows after summary
groups are formed. A selected summary row retains counts for its complete
group; limiting summary rows does not change the group's underlying occurrence
set. Summary `Call Site Rows` values are one-based references to the default
call-site output, matching `--rows`; the typed projection continues to retain
zero-based indexes into the result array. `--count` counts selected rows after
that window, using the normal multi-section count map when several sections are
selected.

If a later slice projects the result into `InspectionGraphDocument`, every
rolled-up library edge must retain the member-level occurrence receipts behind
it and use a relationship descriptor that explicitly admits that lens.

## Explicit non-goals

This L1 contract does not include:

- signature-only type or member references;
- inheritance or interface implementation;
- field access;
- delegate, reflection, or dynamic-dispatch inference;
- public-entrypoint reachability or local root-to-use-site paths;
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
- participant acquisition and invalid-image failures;
- summary groups retaining every exact occurrence once;
- repeated sites affecting site counts without inflating distinct counts;
- bidirectional and request-order-independent summary ordering.

Repository dogfood uses `DotnetInspector.Presentation` and
`DotnetInspector.MetadataRendering` against their resolved Markout library.
The focused relationship has two consumer use sites and four provider API
types; the broader relationship has thirteen consumer use sites and four
provider API types. Both must retain the existing six and seventy-two exact
call sites before feature clustering or breadth/depth measures are designed.
