# Member source comparison query

## Status and ownership

This document defines the `DotnetInspector.Queries`-owned member source
comparison operation for
[#5682](https://github.com/richlander/dotnet-inspect/issues/5682), within the
Inspect Web composition tracker
[#5528](https://github.com/richlander/dotnet-inspect/issues/5528) and the
structured-comparison tracker
[#5526](https://github.com/richlander/dotnet-inspect/issues/5526).

The normative claim is:

> An explicit member source-comparison query resolves one exact implementation
> MethodDef and returns the independently attempted checksum-verified PDB source
> and decompilation evidence for that same retained member, with each endpoint's
> availability and failure preserved.

This is a focused L1 query design. It consumes:

- member identity, implementation-participant resolution, retained-image
  access, and binding-policy validation from existing assembly-context queries;
- SourceLink, symbol, checksum, and source acquisition from the existing source
  services;
- decompilation from `MemberBodyProducer`; and
- cancellation and query-cost semantics from the query layer.

It does not define source text presentation, line correspondence,
`AnalysisDiff<string>`, Markout lowering, CLI output, browser transport, or
Inspect Web interaction. Those are later focused owners.

## Why a distinct query exists

`AssemblyContextSourceQuery.ExecuteMemberAsync` implements the behavior-safe
Source default: it returns complete verified PDB source when available and runs
decompilation only as a fallback. A comparison is a different explicit
operation because it needs both attempts.

Changing the ordinary Source query to always decompile would impose comparison
cost on a non-comparison request and would alter its result contract. The
comparison query therefore reuses the same resolution and acquisition
machinery but has its own moderated query definition and result type.

## Request and subject

The request identifies:

- one implementation participant in one binding-consistent
  `AssemblyContextGroup`;
- one metadata type identity;
- one member anchor;
- one physical MethodDef token expected to denote that member; and
- decompiler printer options.

The query resolves the member once. PDB acquisition and decompilation operate
on that same resolution and retained assembly evidence. A host cannot provide
two independently resolved endpoint subjects and ask the query to treat them as
one comparison.

A property or event accessor request remains a request for its physical
MethodDef. Metadata owns the deterministic projection from the API surface's
property or event row to its getter, setter, adder, or remover method model.
The query accepts that projected method only when both its physical token and
member anchor match the request. PDB evidence may therefore contain the owning
source property or event declaration while decompiled evidence contains the
selected physical accessor; the query does not promote any other owner row or
guess an accessor from its display name.

## Result

The result is presentation-neutral and has five top-level outcomes:

- `Available`: the target resolved and at least one endpoint attempt produced
  usable evidence;
- `Unavailable`: the target resolved, but neither endpoint produced usable
  evidence;
- `NotFound`: the request's type, anchor, and MethodDef token did not resolve
  one exact target;
- `Failed`: inspection or binding-policy validation failed before a current
  complete result could be published; or
- `Rejected`: retained assembly access was rejected before the comparison
  could execute.

`NotFound`, `Failed`, and `Rejected` carry their typed cause and no endpoint
attempts. `Available` and `Unavailable` contain two independent endpoint
attempts.

### PDB endpoint

The PDB endpoint is either:

- `Available`, carrying the complete checksum-verified member source inspection
  and its repository provenance; or
- `Unavailable`, carrying the typed PDB/source acquisition attempt and failure.

The endpoint preserves product-issued repository provenance separately from
resolved or fetch URLs. This query does not decide whether a host may expose an
Open action.

### Decompiled endpoint

The decompiled endpoint is either:

- `Available`, carrying the complete `MemberRenderResult`; or
- `Unavailable`, carrying the typed decompilation status and failure detail but
  no candidate comparison text.

The query does not turn `MemberRenderResult` into a host's final comparison
text. In particular, the existing CLI Source Diff compares a CLI-owned member
projection whose indentation and signature wrapping differ from
`MemberBodyProducer`'s whole-type segment. The next presentation design must
choose and own one canonical endpoint projection before either host computes
line correspondence.

`MemberRenderResult.Failed` may internally retain a source-shaped diagnostic
comment in its `Text` property. That diagnostic is failure detail, not
decompiled source. An unavailable endpoint does not expose it through a
candidate-text member.

### Partial availability

One available endpoint and one unavailable endpoint is a successful typed
query result, not an exception and not an empty comparison. Consumers decide
whether their surface can present one endpoint, present the failure, or require
both before offering a diff.

The query never manufactures source text for an unavailable endpoint and never
turns a decode, acquisition, or decompilation failure into an empty string.

## Execution

The operation:

1. validates the request and resolves the exact member through one retained
   assembly session;
2. records the group's binding-policy version;
3. attempts SourceLink, symbol, checksum, and member-source acquisition;
4. validates cancellation and binding-policy currency after external
   acquisition and source disposal;
5. produces the member decompilation from the same retained resolution;
6. validates cancellation and binding-policy currency after decompilation; and
7. returns both endpoint attempts without choosing a presentation.

The endpoint attempts are independent after the shared resolution. PDB failure
does not prevent decompilation. PDB success does not suppress decompilation.
A decompilation failure does not discard complete verified PDB evidence.

Portable-PDB acquisition is pathless by default. A content-only host probes
embedded PDB evidence and the configured symbol services without consulting an
ambient assembly path. A path-backed host may explicitly enable adjacent-PDB
reads; that capability is separate from permission to read checksum-mapped
local source files. Enabling either capability does not imply the other.

The query reuses existing source-query helpers rather than implementing another
SourceLink map reader, checksum verifier, source fetcher, member resolver, or
decompiler path. Shared helpers may be extracted within
`DotnetInspector.Queries` when necessary; they do not become public merely to
serve a browser host.

## Cost and cancellation

The comparison query is `InspectionCost.Moderated`. It runs only after an
explicit diff gesture or an explicitly selected CLI Diff section.

Cancellation is observed:

- before retained-image work;
- during symbol and source acquisition;
- after source disposal;
- before decompilation;
- through binding-policy resolution used by decompilation; and
- before publishing the result.

Cancellation aborts the operation. Binding-policy invalidation produces the
typed top-level `Failed` outcome rather than an endpoint-unavailable result. A
result cannot combine endpoints produced under different binding-policy
versions.

## Consumers and staged adoption

The query is shared substrate with two planned consumers:

1. [#5683](https://github.com/richlander/dotnet-inspect/issues/5683) will define
   a host-neutral member source-diff presentation adapter that consumes the
   endpoint evidence, chooses the canonical comparison text, produces
   `AnalysisDiff<string>`, and lowers through Markout; the CLI Source Diff path
   will migrate to that adapter while preserving or explicitly changing its
   user-visible endpoint contract; and
2. Inspect Web will consume the same adapter through the bounded worker
   transport in
   [#5684](https://github.com/richlander/dotnet-inspect/issues/5684), surface
   placement in [#5685](https://github.com/richlander/dotnet-inspect/issues/5685),
   and viewer interaction in
   [#5686](https://github.com/richlander/dotnet-inspect/issues/5686).

Neither consumer is implemented in this design slice. The next stacked design
must resolve the current CLI projection versus `MemberBodyProducer` text
difference before promising output compatibility.

## Gates

Release query tests prove:

- complete verified PDB source and complete decompilation are both returned;
- PDB success does not suppress decompilation;
- PDB failure preserves its typed attempt while decompilation succeeds;
- decompilation failure preserves its typed attempt while PDB source succeeds;
- an unavailable decompilation endpoint exposes no candidate text even when
  its underlying failure detail uses a source-shaped diagnostic;
- both unavailable attempts produce explicit `Unavailable`;
- an unresolved type, anchor, or MethodDef token produces explicit `NotFound`;
- a resolution-time inspection failure produces explicit `Failed`;
- retained-image rejection produces explicit `Rejected`;
- both attempts refer to the same resolved MethodDef and retained participant;
- a physical property or event accessor resolves through the shared metadata
  projection while retaining its exact token and anchor;
- adjacent portable PDBs are considered only when the host explicitly enables
  that path-backed capability, while the default remains pathless;
- cancellation during either attempt does not publish a partial success;
- binding-policy invalidation during either attempt produces `Failed` and does
  not combine stale and current evidence; and
- ordinary `AssemblyContextSourceQuery` behavior remains PDB-first with
  decompilation only as fallback.

Layering tests prove `DotnetInspector.Queries` acquires no dependency on
Markout, `DotnetInspector.Presentation`, CLI assemblies, or browser assemblies.
