# Inspect Web Subject Overview

## Owner and claim

Inspect Web Subject Overview owns the Browser presentation grammar for one
subject's orientation page. An Overview renders one vertical flow whose
summary keeps direct subject metadata, owner-issued subject rollups, and
non-overlapping numeric navigation distinct. Bounded structural relationships
and ordinary metadata follow as rows.

Type is the first adopter. Its Overview adopts the existing exact-Type metadata
surface and prepends the reusable summary; Members remains a separate
product-issued working surface. Opening Type Overview may request the existing
metadata and relationship projections over the retained inspection scope, but
performs no source, method-body, or network analysis.

## Inputs and boundaries

Overview consumes typed facts issued by their semantic owners. It may group and
label those facts for presentation, but does not infer identity, declaration
metadata, rollups, relationships, or counts from display text.

The Type adopter consumes:

- retained Type identity, kind, accessibility, assembly, and member count;
- exact-Type modifiers, base Type, interfaces, and declaration metadata from
  the existing metadata projection; and
- owner-issued Type rollups when such facts are available.

The presentation distinguishes:

- **Type metadata**: direct declaration facts such as `class`, `sealed`, and
  `public`;
- **Type rollup**: facts derived across the Type's members, such as a future
  owner-issued `unsafe` or `async` decoration; and
- **numeric navigation**: counts whose activation opens another working
  surface, beginning with Members.

An absent rollup population produces no rollup group. Presentation never
reconstructs `async`, `unsafe`, or another rollup from signatures, source text,
return-Type spelling, or other display content.

## Type composition

Type Overview uses this order:

1. direct metadata, rollups when available, and numeric navigation;
2. the headerless Type Shape key/value rows;
3. member composition;
4. implemented interfaces and hierarchy evidence;
5. custom attributes; and
6. the existing Type relationship graph when available.

Analysis is a navigation item, not Overview content. It opens Type Analysis
with Relationships as its initial view; any cached called-Type count may refine
the label without moving the behavioral graph, its completion warnings, or
member/site evidence into Overview.

The former `type.metadata` Browser route is a compatibility alias for Overview
and does not appear as a separate navigation item.

The member count opens Type Members without changing the selected Type.
Members keeps compact owner-issued MemberGroups as its default, least-work
content. A low-prominence Content selector inside the existing filter
disclosure may explicitly request complete exact-Member rows or the same rows
with documentation. Both exact views consume the shared Type document
operations; the documented selection alone authorizes documentation
attachment. Documentation availability or failure never removes a declaration,
changes declaration order, or selects a different schema.

Browser presentation may associate each shared row with an existing
Browser-owned selector by exact declaration token so the current exact-Member
transitions and Member working surfaces remain available. That association is
navigation enrichment, not population, identity, ordering, or documentation
authority. Missing selector correspondence fails visibly. Package and Platform
documentation capabilities remain source-owned; uploaded Libraries retain the
complete exact rows and visibly report that compiled documentation is
unavailable.

Members otherwise retains its existing filters and exact-Member transitions.
Metadata, Source, Compare, and forwarded-Type behavior remain separate.

Loading and failure remain visible. Until exact-Type metadata settles,
Overview may show retained direct facts and a relationship-loading state. A
failed or unavailable projection does not become an empty base or interface
population.

### Custom attribute rows

Custom attributes render as one syntax-highlighted C# attribute per list row,
preserving the projected order and count. Long arguments wrap within the row;
larger populations extend the ordinary Overview scroll rather than flowing
sideways as chips. An empty population omits the section.

The motivating asset is `System.Text.Json` `11.0.0-preview.7.26381.103`,
`net11.0`, Type `System.Text.Json.Utf8JsonReader`. Its `System.Obsolete`
attribute contains a long compiler-compatibility message and a Boolean
argument; `System.Diagnostics.DebuggerDisplay` contains a quoted expression.
Open that exact coordinate in Type Overview to reproduce. Focused
`type-panel.test.ts` cases retain these spellings and gate separate rows,
syntax tokens, order, count, larger populations, and empty-section omission.

## First-adopter evidence

`System.Text.Json` 10.0.13
`System.Text.Json.JsonDocument` is the production demonstration. It proves
that direct Type metadata, member navigation, base Type, interfaces, and
ordinary metadata form one vertical page while no relation request occurs.

Focused gates prove:

- metadata, rollup, and numeric groups remain distinct;
- no rollup group appears without owner-issued rollup facts;
- base Type precedes interfaces;
- metadata failure remains visible rather than becoming absence;
- the member-count link opens Members with compact groups still selected;
- exact and documented Member content preserve one declaration population and
  order;
- documented content is requested only by its explicit Content selection;
- documentation and selector-enrichment failures remain visible without
  removing declarations;
- Type Overview and Members restore as distinct facets; and
- ordinary Overview navigation performs no relation, source, body, or network
  work.

## Non-claims

This owner does not define Type metadata or hierarchy semantics, method
decorations, relation acquisition, Type-edge aggregation, call graphs, Member
detail, navigation identity, or workspace restoration. Package, Library, and
Member adoption are separate focused efforts.
