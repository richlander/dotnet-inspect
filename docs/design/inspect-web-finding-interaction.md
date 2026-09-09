# Inspect Web Finding interaction

This document owns browser-side selection of one Research-issued Finding
instance across the Inspect Web member Facts and Annotated Source surfaces.
It consumes the combined managed envelope from
[Inspect Web Finding census transport](inspect-web-finding-census-transport.md)
and the viewer-local transitions from
[Annotated Source viewer interaction](annotated-source-viewer-interaction.md).

End-to-end adoption is tracked by
[issue #5515](https://github.com/richlander/dotnet-inspect/issues/5515);
this browser slice is
[issue #5517](https://github.com/richlander/dotnet-inspect/issues/5517).

## Claim

For one active member Finding census, the browser selects and transfers only
the producer-issued `instanceKey` scoped by that census's
`factCensusReceipt`. Facts rows and Annotated Source document facts that carry
the same receipt-scoped key denote the same exact Finding instance, including
when their descriptor, detail, subject, coordinates, and other display data
are identical.

A result, row, or gesture from another receipt cannot replace or select an
instance in the active census. Missing or mismatched identity remains visible;
the browser never repairs it by comparing display fields, offsets, document
order, or local fact ids.

## Experience

The existing Analysis Facts remain intact. A separate **Findings** section
shows every Research Facts row from the combined census:

- body Finding rows with an `instanceKey` are selection actions;
- member-header rows without an `instanceKey` remain visible and explicitly
  report that source identity is unavailable; and
- the selected body Finding row is identified by exact instance key, not by
  row content.

Activating a keyed Facts row changes the member surface to Annotated Source and
opens the existing modal Finding inspector for the corresponding document
fact. The modal provides a stable inspector opener for anchored, unanchored,
default, and non-default body Findings without inventing a new Annotated
Source opener kind.

Selecting a Finding annotation or inspector action in Annotated Source records
the corresponding exact instance. Returning to Facts highlights that row.
Viewer transitions that replace the primary Finding with a node or no
selection clear the cross-view selection. Detail-only transitions do not.

The interaction is transient. It is not written to browser history, Workspace
state, or share packets.

## State and indexes

One browser interaction value contains:

- the complete combined census envelope;
- an index from document-local `factId` to producer-issued `instanceKey`;
- the inverse index from `instanceKey` to `factId`; and
- at most one selected `instanceKey`.

The root receipt scopes both indexes and the selection. The browser admits the
wire result atomically after the Annotated Source document is validated. It
also checks that keyed Facts rows and sidecar rows form the same unique,
positive key set and that the sidecar covers every document body fact while
excluding member-header facts.

These browser checks defend the interaction boundary and make malformed wire
data visibly unusable. They do not redefine the managed transport's producer
validation.

## Replacement and failure

Facts and Annotated Source use one body- and style-aware request signature for
the combined census. Opening either surface may initiate that request; a
settled result is shared when the user switches between the two surfaces.

A result publishes only when both its request signature and the currently
selected member still match. Publication atomically replaces:

- the census envelope and both indexes;
- the Annotated Source document;
- embedded and modal viewer sessions; and
- the selected instance.

The separate Analysis Facts request retains its existing cache and replacement
rules. It neither clears nor supplies Finding identity.

Malformed census data, request failure, missing sidecar identity, a stale
receipt, or an unknown key/fact id is shown as a Finding interaction failure.
Analysis Facts and Research Findings report their failures independently so
one operation cannot masquerade as a successful empty result for the other.

## Composition boundaries

This owner consumes without redefining:

- receipt/key construction and exact-association meaning from
  [Finding instance census](finding-instance-census.md);
- single-census Research projection from
  [Research Finding census projection](research-finding-census-projection.md);
- wire shape and managed validation from
  [Inspect Web Finding census transport](inspect-web-finding-census-transport.md);
- Analysis Facts data from the existing Analysis facade;
- document validation, fact ids, and viewer-local selection from
  [Annotated Source viewer interaction](annotated-source-viewer-interaction.md);
  and
- Facts and Annotated Source placement from
  [Inspect Web Surface Composition](inspect-web-surface-composition.md).

The composition root owns only request wiring, section switching, and focus
handoff. Receipt/key indexing and selection decisions remain in a focused,
host-neutral TypeScript module, while Facts rendering and binding remain in
the Facts view module.

## Pathological case

The contract-defining case contains two body Findings with identical member,
anchor, category, id, detail, conditionality, IL offset, C# line, and rendered
source coordinates. They have different producer-issued instance keys and
different document fact ids.

Selecting either Facts row opens and highlights only its mapped Annotated
Source instance. Selecting either Annotated Source instance highlights only
its mapped Facts row. Reusing either key with another receipt is rejected, and
removing either sidecar association does not fall back to the identical
display shape.

## Evidence

The Inspect Web TypeScript gates prove:

- exact bidirectional lookup for display-identical Findings;
- rejection of wrong receipts, unknown keys, unknown fact ids, duplicate
  identity, and incomplete sidecars;
- keyed and unkeyed Facts presentation, selected-row state, and escaped
  display data;
- one combined census request shared by Facts and Annotated Source;
- stale same-member completion cannot publish over a newer request;
- Analysis Facts no longer invalidate an active census; and
- existing Annotated Source selection, modal, detail, dismissal, and focus
  behavior remains intact.

The existing managed Release gates continue to prove that a real member
projection transports one receipt and the same distinct key set through both
projections.

## Non-claims

This interaction does not:

- construct, parse, or make Finding identity durable;
- correlate separate census executions;
- merge Analysis DTOs into the Research transport;
- infer identity from Finding values, text, coordinates, or collection order;
- add a Workspace, share, URL, or history field;
- change Annotated Source annotation membership or document construction; or
- define selection across members, packages, processes, or sessions.
