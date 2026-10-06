# Platform assembly-reference binding

## Status and approved scope

This document is the focused owner for binding one ordinary Metadata
`AssemblyRef` to a Library in one already selected Platform target. It is
tracked by
[#8503](https://github.com/richlander/dotnet-inspect/issues/8503) and composes
with:

- [PlatformHouse realization and reference processing](platform-house-reference-processing.md),
  which owns the Platform facade, request, source-policy, execution, and
  receipt contracts;
- [Package-backed Platform realization](package-backed-platform-realization.md),
  which owns exact target coordinates, reference- and runtime-pack member
  realization, Range acquisition, and exact-identity realization demands;
- [Package-origin AssemblyRef supply routing](package-origin-assemblyref-supply-routing.md),
  which owns Package-first external-supplier sequencing; and
- the
  [Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md),
  which owns context precedence and the final host-neutral result.

The motivating production case is
`Microsoft.Azure.SignalR@1.33.1/net8.0`. Its selected Library references
`Microsoft.AspNetCore.SignalR.Core, Version=8.0.0.0`; an eligible ASP.NET Core
Platform target must bind that arbitrary source identity to the namesake
reference Library in the selected target without realizing the complete
targeting pack.

## Authority and exact claim

**Platform assembly-reference binding** owns:

> Given one exact ordinary Metadata `AssemblyBindingRequest`, one complete
> owner-selected Platform composition containing one exact target per eligible
> family, one owner-authorized Reference source plan, and finite work, realize
> at most the namesake target reference member required from each family,
> apply the explicit Platform identity policy, and return either one canonical
> target-Platform Library identity or one typed binding non-success.

It owns:

- the distinction between an arbitrary source `AssemblyRef` identity and the
  canonical identity of the selected target Platform Library;
- the typed namesake binding demand used only for an assembly-reference
  operation;
- Platform identity eligibility over the realized candidate;
- the canonical identity handed to subsequent exact reference or
  implementation realization;
- complete Platform name ownership for the exact family composition; and
- visible absence, owned miss, ambiguity, unavailability, rejection,
  incomplete work, or failure.

It does not own:

- PackageRef correlation, package pruning, or Package/Platform precedence;
- Platform family eligibility or target/version selection;
- installed- or package-backed source coordinates, archive transport, Range
  mechanics, caching, or payload lifetime;
- generic Metadata binding vocabulary or result algebra;
- exact-identity Platform realization;
- Workspace publication and replacement;
- host routing; or
- presentation.

This is an extension of the ordinary PlatformHouse assembly-reference
operation, not a second Platform resolver.

## Normative basis and deliberate policy

ECMA-335 records an assembly reference version, culture, and public-key token,
but it does not define the host's version-unification policy. Metadata owns the
request and binding result algebra. This owner supplies the Platform-specific
identity policy for one already selected target.

The selected Platform target is the version authority. A source Library may
therefore reference an older Platform assembly version than the target
reference pack exposes. Platform binding:

- requires an ordinal-ignore-case simple-name match;
- requires the requested culture when one is present;
- requires the requested public-key token when one is present;
- deliberately ignores the source `AssemblyRef` version when evaluating the
  selected target's namesake reference member; and
- returns the realized member's complete Metadata identity as the canonical
  target identity.

This is equivalent to applying
`AssemblyReferenceIdentity.MatchesCandidate(candidate, ignoreVersion: true)`
under an owner-selected Platform target. It is not a general binding redirect,
package version comparison, or permission to ignore culture or strong-name
identity.

The exact realization contract remains stricter:
`PlatformLibraryDemand.Assembly` continues to require complete identity
equivalence. Callers that already hold a canonical target identity use that
contract unchanged.

## Contract shape

```text
exact ordinary AssemblyBindingRequest
  + complete eligible family composition
  + one exact PlatformFamilyTarget per family
  + authorized Reference source plan
  + finite work
        |
        v
Platform assembly-reference binding
  -> project request simple name to one source-owned reference coordinate
  -> realize at most that namesake member
  -> decode complete target identity
  -> apply Platform name/culture/key policy with target-version unification
        |
        +-- compatible
        |     -> Metadata Resolved
        |     -> canonical target Platform identity
        |
        +-- namesake member, incompatible culture/key
        |     -> NameOwnedNoMatch
        |
        +-- no namesake member in every eligible family
              -> NoNameOwner
```

Unavailable source authority, malformed source evidence, source-policy
ambiguity, finite-work exhaustion, and cancellation retain their existing
typed outcomes. They never become absence.

The exact request, Platform target, family composition, source plan, source
policy generation, content generation, Metadata decision, and canonical
identity remain associated in the result.

## Namesake binding demand

The existing source-neutral `ResolveAssemblyReference` operation retains the
unchanged `AssemblyBindingRequest`. Its source lowering uses a distinct typed
binding demand rather than lowering the arbitrary source identity into the
exact `PlatformLibraryDemand.Assembly` contract.

Conceptually:

```text
PlatformLibraryDemand.AssemblyReferenceBinding(request identity)
```

The exact public type name may change during implementation. The following
distinctions may not:

| Demand | Question | Completion gate |
| --- | --- | --- |
| Exact assembly | Realize this already canonical Platform identity | Complete Metadata identity equivalence |
| Assembly-reference binding | Which Library in this exact target binds this arbitrary source `AssemblyRef`? | Namesake realization plus Platform identity policy |
| Complete population | Realize every member in the selected population | Source-owned complete membership |

A binding demand carries the complete source `AssemblyRef` identity so the
House can evaluate culture and public-key-token compatibility. A source uses
only the simple name to locate a candidate member. The source does not decide
version unification or manufacture the canonical identity.

An absent namesake coordinate is source absence. A case-insensitive coordinate
collision, malformed member, or member whose decoded assembly simple name
differs from the projected coordinate is rejected source evidence; it is not a
successful no-owner claim.

## Reference-first binding and exact continuation

Binding is evaluated against the selected target's Reference view:

1. Family eligibility and exact target selection are already complete.
2. The source projects the request simple name to the target's reference
   member coordinate.
3. The source realizes at most that member and reports its decoded complete
   identity.
4. PlatformHouse applies the Platform identity policy and returns Metadata's
   unchanged decision.
5. A successful decision exposes the realized member's canonical target
   identity.

Reference-only API, type, or member work can consume the already realized
Reference Library. An implementation-demanding operation feeds the canonical
target identity into the exact package-backed implementation demand introduced
by [PR #8886](https://github.com/richlander/dotnet-inspect/pull/8886):

```text
arbitrary source AssemblyRef
  -> namesake target reference binding
  -> canonical target identity
  -> PlatformLibraryDemand.Assembly(canonical identity)
  -> exact manifest-defined implementation realization by Range
```

The implementation source still requires exact identity equivalence. Binding
does not weaken its manifest membership, support-closure, digest,
correspondence, or Metadata completion gates.

## Family composition

Family eligibility is owner-issued before binding:

- the selected Platform context may admit the baseline .NET Runtime family and
  its exact target;
- an exact package target's framework-reference evidence may additionally
  admit ASP.NET Core and its exact target; and
- target-framework spelling or an assembly-name prefix cannot admit a family.

Each eligible family contributes at most one namesake reference candidate from
its exact target. One compatible candidate selects it. Several equally
eligible compatible candidates are ambiguous unless an adjacent owner issued
family precedence. An incompatible namesake candidate contributes owned-miss
evidence; it does not prevent another eligible family from being evaluated.

Complete family composition is required for `NoNameOwner` or
`NameOwnedNoMatch`. Missing, failed, or bounded family evidence is incomplete.

## Closed outcomes

| Complete Platform evidence | Binding outcome |
| --- | --- |
| One compatible namesake candidate | `Resolved` with canonical target identity |
| Several equally eligible compatible candidates | `Ambiguous` |
| No family contains a namesake member | `NoNameOwner` |
| At least one family contains a namesake member, but none satisfies culture/key policy | `NameOwnedNoMatch` |
| Required target, source, or acquisition evidence is unavailable | `Unavailable` |
| Request, correspondence, layout, Metadata, or owner result is invalid | `Rejected` |
| Family composition or finite work does not settle | `Incomplete` |
| An owner fails while producing required evidence | `Failed` |

A version difference alone does not produce `NameOwnedNoMatch`; the selected
Platform target intentionally supplies the canonical version.

## Azure SignalR

For `Microsoft.Azure.SignalR@1.33.1/net8.0`:

1. PackageHouse selects `lib/net8.0/Microsoft.Azure.SignalR.dll`.
2. Metadata observes
   `Microsoft.AspNetCore.SignalR.Core, Version=8.0.0.0`.
3. No retained ordinary Package supplier binds the request.
4. Root-scoped framework-reference evidence admits ASP.NET Core.
5. Platform target policy selects one exact ASP.NET Core target.
6. The binding demand selects only
   `ref/<target-tfm>/Microsoft.AspNetCore.SignalR.Core.dll`.
7. Its decoded name, culture, and public-key token satisfy the request while
   its target-owned version becomes canonical.
8. Reference work consumes that Library directly.
9. Implementation work uses the canonical identity with PR #8886's exact
   runtime-pack demand.

No `Microsoft.AspNetCore.SignalR.Core` component PackageRef, closure-wide
targeting-pack population, package-to-Library map, or source-version equality
is required.

## Pathological cases

### Older source identity on a newer target

A version 8 `System.Text.Json` reference evaluated against a selected version
12 .NET Runtime target can bind the version 12 namesake reference member.
Name, culture, and public-key token remain compatible, and the returned
canonical identity is version 12.

### Wrong public-key token

A namesake target member with a different requested public-key token is a
complete Platform `NameOwnedNoMatch`. Version unification cannot erase the
strong-name mismatch.

### Missing member

When no eligible family contains the namesake reference member, complete
source evidence returns `NoNameOwner`. The absence does not trigger broad pack
population or a NuGet search.

### Same name in two eligible families

Two compatible namesake candidates are ambiguous unless an owner-issued family
precedence chooses one. Enumeration, source latency, and package order are not
precedence.

### Exact caller

A caller that already holds the target's canonical identity continues to use
the exact assembly demand. The binding demand is not a relaxed replacement for
exact realization.

## Cost boundary and evidence

PR #8886 measured the exact package-backed realization boundary over the same
immutable 42,322,665-byte runtime-pack archive and in-memory HTTP transport:

| Scenario | Median time | Transfer | Source work | Libraries |
| --- | ---: | ---: | ---: | ---: |
| Exact `System.Text.Json`, before | 154.86 ms | 42.32 MB | 66.69 MB | 181 |
| Exact `System.Text.Json`, ranged | 12.54 ms | 1.13 MB | 2.57 MB | 1 |
| Exact `System.Private.CoreLib`, before | 155.60 ms | 42.32 MB | 66.69 MB | 181 |
| Exact `System.Private.CoreLib`, ranged | 77.85 ms | 7.82 MB | 17.90 MB | 1 |
| Complete-population control | 155.54 -> 157.65 ms | unchanged | unchanged | 181 |

The full measurement is recorded in the
[PR #8886 performance comment](https://github.com/richlander/dotnet-inspect/pull/8886#issuecomment-5892716579).
Each ranged exact demand used one bodyless size probe and three `206` requests;
complete-population behavior remained a complete download.

Binding adds target-aware selection and one namesake Reference member. It must
reuse the same named-entry Range and entry-cache substrate rather than
construct a parallel catalog or complete-population path. The enforcing
Release gate measures work shape:

- at most one candidate DLL per eligible family;
- no unrelated reference DLL body read;
- no complete archive body for an exact namesake request;
- one canonical identity returned on success; and
- unchanged complete-population behavior.

Timing and transfer measurements remain reported evidence, not brittle pass
thresholds.

## Production adoption and retirement

Adoption is staged:

1. add the typed binding demand and source-neutral Platform identity policy;
2. implement installed and package-backed namesake Reference realization,
   reusing the exact named-member substrate;
3. feed successful canonical identities into PR #8886's exact implementation
   realization;
4. have Package-first external-supplier composition invoke the binding
   operation after ordinary Package tiers select no supplier;
5. adopt the shared result in CLI; and
6. adopt it in Browser/Wasm after browser-safe Range transport and
   refresh-persistent package-entry caching.

The adopting slices retire:

- complete Platform population used only to bind one ordinary `AssemblyRef`;
- closure-wide target identity catalogs built only for that question;
- host-local Platform assembly-name scans; and
- any adapter that lowers an arbitrary source identity into an exact target
  identity demand.

Each implementation slice has one production consumer. Shared substrate
remains host-neutral and Browser/Wasm-compatible.

## Evidence gates

Future Release gates must prove:

- the real Azure SignalR request binds through ASP.NET Core without a component
  PackageRef or complete reference-pack population;
- an older source `System.Text.Json` identity binds to the canonical identity
  of a newer selected .NET Runtime target;
- a wrong public-key token produces `NameOwnedNoMatch`;
- a missing namesake member produces `NoNameOwner`;
- a namesake coordinate whose decoded assembly has another name is rejected;
- collision, malformed Metadata, source-policy ambiguity, unavailable source,
  bounded work, and cancellation remain distinct;
- reference binding reads no unrelated assembly body;
- successful implementation continuation uses the canonical identity and
  PR #8886's exact ranged path;
- the exact-demand control remains exact;
- the complete-population control retains the same membership and acquisition
  behavior; and
- equivalent CLI and Browser/Wasm inputs produce equal host-neutral outcomes.

Real package-backed gates use Microsoft reference and runtime packs. Synthetic
fixtures cover wrong-key, cross-family ambiguity, collision, malformed
Metadata, missing member, and bounded failure.

## Non-goals

- Selecting a Platform family or target version.
- Defining Package/Platform precedence or package pruning.
- Searching NuGet or Platform packs by arbitrary Metadata identity.
- Ignoring culture or public-key-token mismatches.
- Weakening exact Platform realization.
- Loading inspected assemblies.
- Supporting Windows Metadata.
- Host-specific routing or rendering.
