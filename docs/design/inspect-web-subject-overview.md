# Inspect Web Subject Overview

## Owner and claim

Inspect Web Subject Overview owns the Browser presentation grammar for one
subject's orientation page. An Overview renders one vertical flow whose
summary keeps direct subject metadata, owner-issued subject rollups, and
non-overlapping numeric navigation distinct. Bounded structural relationships
and ordinary metadata follow as rows.

Type is the first adopter. Its Overview and Members are separate
product-issued working surfaces. Opening Type Overview may request the existing
exact-Type metadata projection over the retained inspection scope, but performs
no source, method-body, network, or relation analysis.

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
2. base Type first, when present;
3. implemented interfaces in owner-issued order; and
4. straightforward identity and declaration metadata.

The member count opens Type Members without changing the selected Type.
Members retains the existing member filters, rows, exact-member transitions,
and Member working surfaces. Metadata, Source, Compare, and forwarded-Type
behavior remain separate.

Loading and failure remain visible. Until exact-Type metadata settles,
Overview may show retained direct facts and a relationship-loading state. A
failed or unavailable projection does not become an empty base or interface
population.

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
- the member-count link opens the unchanged Members surface;
- Type Overview and Members restore as distinct facets; and
- ordinary Overview navigation performs no relation, source, body, or network
  work.

## Non-claims

This owner does not define Type metadata or hierarchy semantics, method
decorations, relation acquisition, Type-edge aggregation, call graphs, Member
detail, navigation identity, or workspace restoration. Package, Library, and
Member adoption are separate focused efforts.
