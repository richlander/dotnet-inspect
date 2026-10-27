# Library Reference Inspection

## Owner and claim

Library Reference Inspection owns the completed host-neutral answer to one
exact managed Library's direct metadata-reference question.

> For one exact managed Library, Library Reference Inspection converts one
> owner-issued direct `AssemblyRef` query outcome into one detached,
> deterministically ordered available, rejected, or failed outcome and returns
> it in `InspectionEnvelope<LibraryReferenceInspectionOutcome>`.

Metadata owns reference identity and reading. Assembly Context owns participant
admission, lifetime, and typed rejection. The inspection consumes those
owner-issued outcomes; it does not reopen a Library or reinterpret metadata.

This owner is introduced by #10727 as slice 2/4 of #10635. The CLI References
section is the bounded first adopter. #10759 makes Inspect Web the second
adopter while retaining Browser-owned destination resolution, icons, graph,
navigation, interaction, and rendering.

## Content

`LibraryReferenceInspectionOutcome` is an owner-specific Outcome:

- `Available` carries every detached direct-reference identity and may be
  empty;
- `Rejected` carries the Assembly Context owner's typed participant rejection;
  and
- `Failed` carries a typed unexpected-operation failure and contained detail.

Available rows preserve duplicates and full identity fields. They are ordered
by simple name using ordinal case-insensitive comparison, then ordinal name,
version, culture, and public-key token. Hosts may select or render rows after
the operation, but they do not reconstruct or reorder the baseline population.

The envelope's Share outcome is non-projectable until an exact Library request
has a canonical Workspace projection. Supplemental diagnostics are empty;
non-success that determines the answer remains primary content.

## Production boundaries

Hosts retain:

- source authorization and acquisition;
- Library and Assembly Context lifetime;
- section selection and semantic row selection;
- navigation and destination resolution;
- interaction state, transport, and rendering.

Hosts consume the completed envelope once. They do not classify query
exceptions, map Assembly Context rejection, normalize reference fields, or
choose baseline ordering.

## Evidence

`System.Text.Json@10.0.0` /
`lib/net10.0/System.Text.Json.dll` is the real populated scenario.
The selected runtime's `System.Private.CoreLib` is the valid empty neighbor.
Authored duplicate and case-distinct identities prove deterministic
full-identity ordering without deduplication.

The shared gate covers populated, empty, rejected, failed, ordering, envelope,
and source-generated JSON behavior. The adopter gates prove CLI References
rendering and row selection and Inspect Web References presentation consume
the same owner-issued rows.

## Non-claims

This owner does not define acquisition, Workspace composition, Library
lifetime, reference binding or traversal, dependency ownership, package
manifest dependencies, reference graphs, navigation, or presentation. It does
not merge Package and Library questions or replace neighboring Library
sections.
