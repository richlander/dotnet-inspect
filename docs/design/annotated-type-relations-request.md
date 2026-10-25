# Annotated Type Relations request

## Status and owner

This document owns the host-neutral request selection for
[Annotated Type Relations](annotated-type-relations.md). It owns:

- explicit selection of all applicable relationship families or a non-empty
  set of canonical relationship ids;
- canonical color-id and predicate-color selection;
- deterministic binding of those ids to one admitted immutable
  [relations catalog](annotated-type-relations-planning.md);
- preservation of both requested selection mode and resolved family ids; and
- typed rejection before producer work begins.

Its exact claim is:

> For one exact Type focus and one admitted immutable relations registry
> snapshot, one request explicitly selects either all applicable families or a
> non-empty exact family-id set, direction, optional colors, and a terminal;
> resolution deterministically returns the existing exact planning request
> plus the requested and resolved family identities, or a typed rejection,
> without executing producer work.

This is slice 5/6 under
[#10283](https://github.com/richlander/dotnet-inspect/issues/10283), focused as
[#10561](https://github.com/richlander/dotnet-inspect/issues/10561). The CLI is
the first adopter. Inspect Web adopts the same request and document in slice 6.

## Consumed contracts

| Owner | Contract consumed |
| --- | --- |
| [Annotated Type Relations](annotated-type-relations.md) | Exact Type focus, semantic direction, selected relationship and color families, and Rows, Count, and Exists meaning |
| [Relations planning](annotated-type-relations-planning.md) | Immutable registry, exact descriptor identity, Type-direction admission, terminal adapter binding, typed query demand, and planning failures |
| [Relations generation](annotated-type-relations-generation.md) | Settlement of typed contributions into the canonical document |
| [Inspection operation composition](inspection-operation-composition.md) | Host-neutral execution and the completed envelope boundary |

Request selection does not redefine relationship, color, query, execution,
generation, envelope, or host semantics.

## Request

One request carries:

- one exact Type `SubjectRelationFocusAuthority`;
- one relationship selection;
- incoming, outgoing, or both focus incidence;
- zero or more canonical color ids;
- a subset of those colors required as predicates; and
- exactly one Rows, Count, or Exists terminal.

Relationship selection has exactly two forms:

- `AllApplicable`; or
- `Explicit`, containing one or more canonical registered relationship ids.

An empty explicit set is invalid. Repeated relationship, color, or required
color ids are invalid rather than silently collapsed. Required colors must
also be selected.

Opening a Type supplies no implicit relation request. `AllApplicable` is an
explicit work gesture.

## Resolution

Resolution binds against one immutable admitted relations catalog and one
inspection query catalog snapshot.

`Explicit` resolves every canonical id to the exact descriptor instance owned
by the admitted catalog. Unknown ids fail; text never becomes a replacement
descriptor.

`AllApplicable` selects, in registry order, every relationship registration
whose descriptor admits the requested Type incidence. It does not select a
family lacking that direction and does not claim incoming completeness for an
outgoing-only adopter.

Both forms then use the existing exact planner. An unsupported terminal or an
adapter absent from the query catalog remains a visible planning failure.
`AllApplicable` cannot silently omit such a selected family to manufacture a
successful smaller request.

Colors always bind by explicit canonical id in this slice. Required colors are
the exact resolved subset used by predicate admission. Color selection never
changes relationship identity.

An accepted resolution retains:

- the original selection form;
- the resolved relationship ids in registry order;
- the resolved color and required-color ids in registry order; and
- the accepted exact `AnnotatedTypeRelationsPlan`.

Equal catalog and query-catalog snapshots plus equal requests produce equal
resolved identities and query demand.

## Failure contract

Construction rejects an invalid focus, enum, empty explicit selection,
repeated id, or required color absent from the selected color set.

Resolution returns one typed rejection for:

- no relationship admitted for an all-applicable direction;
- an unknown relationship id;
- an unknown color id;
- any existing exact-planning failure.

Resolution executes no query and converts no rejection into an empty accepted
selection.

## Production adoption

The existing exact `type` command supplies the first host gesture. It lowers
CLI text to this request, executes only the accepted typed query demand, asks
the existing generation owner for the document, and crosses the completed host
boundary as `InspectionEnvelope<AnnotatedTypeRelationsDocument>`.

The CLI keeps syntax, authorization, diagnostics, operation lifetime, and
presentation. It performs no relation work without the explicit gesture. The
shared path remains NativeAOT- and Browser/Wasm-compatible so Inspect Web can
adopt it without duplicating selection or semantic execution.

The real-asset gates use `System.Text.Json` 10.0.13 and `Newtonsoft.Json`
13.0.4 from the outgoing-call adopter.

## Gates

- **Closed all selection:** `AllApplicable` resolves exactly the
  direction-admitted registry snapshot.
- **Exact explicit identity:** canonical ids bind to catalog-owned descriptor
  instances; unknown and repeated ids fail.
- **Color subset:** every required color is selected and exact.
- **Planner authority:** terminal and query availability failures remain exact
  planning failures.
- **Equivalence:** all-applicable and explicit requests resolving to the same
  families produce equal exact plans and documents.
- **Work exclusion:** a family absent from the resolved request performs no
  adapter or producer work.
- **Explicit gesture:** an ordinary Type inspection performs no relations
  request.
- **Cross-host seam:** the CLI consumes an
  `InspectionEnvelope<AnnotatedTypeRelationsDocument>` from the reusable
  operation.

## Non-claims

- This owner does not define a relationship family, color, planner, query
  executor, result document, or rendering.
- It does not define authored presets, a host default, incoming populations,
  or a universal authorization model.
- It does not define CLI grammar or Browser controls.
- It does not define portable payload or share-link encoding. Until portable
  projection is adopted, Share reports a typed non-projectable path rather
  than approximating the request.
