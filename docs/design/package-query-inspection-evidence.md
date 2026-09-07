# Package Query inspection evidence

## Claim and ownership

Package Query emits package-specific inspection evidence as a total and a
bounded preview of the items actually observed. Query-wide source selection
and provenance remain distinguishable from inspection facts about a package.
This document is the sole owner of that evidence contract.

[Input selection](package-query-input-selection.md) supplies the candidate
scope. [Package Query](package-query-cli.md) owns facet meaning, acquisition
tiers, matching, and failure accounting. The
[Browser experience](package-query-experience.md) owns presentation and
demand-window interaction. This contract consumes their outcomes without
changing them.

The consumer is the Package Query result card. The shared evidence also
supports the planned CLI Package Query projection through the existing
Sections/Markout path. [#6071](https://github.com/richlander/dotnet-inspect/issues/6071)
tracks this adoption within
[#5816](https://github.com/richlander/dotnet-inspect/issues/5816).

## Item meaning and bounds

An item summary has a total count and at most three previews. Counts describe
the complete observed item set, not the preview length. Each preview uses
`InertString` field encoding with a 160-character display budget. A shortened
preview is display text, never a package coordinate or archive-entry handle.

| Facet | Counted item | Preview |
| --- | --- | --- |
| Has dependencies / no dependencies | Distinct declared dependency IDs across manifest groups, using ordinal case-insensitive identity | Up to three IDs, ordered ordinal case-insensitively, preserving the first declared spelling of each ID |
| Embedded SKILL.md | Distinct matching archive-entry paths, using ordinal path identity and the existing case-insensitive skill-document predicate | Up to three actual paths, ordered ordinally |

Dependency summaries describe declarations, not a resolved dependency closure
or an applicable framework selection. Repeated declarations for different
target frameworks count once. Zero dependencies is a known empty item set.

A root `skills/SKILL.md` preview remains that path. The inventory does not
establish a skill's declared name, valid frontmatter, or valid document body.
Content that cannot be acquired or evaluated retains the existing visible
failure outcome; unavailable evidence is not an empty item set.

Only already-requested inspection tiers contribute summaries. A dependency
summary consumes the admitted manifest; a skills summary consumes the entry
inventory already used by its selected content facet. Summaries do not request
additional manifests, archives, or skill-document bodies.

## Evidence and rendering

Each evidence entry retains its product-issued ID and inert explanation, and
identifies whether it is query-wide context or package-specific evidence. Item
summaries remain typed alongside their compact text explanation; consumers do
not parse counts or identities out of prose.

The Browser transports these fields through its generated facade. Its existing
HTML card renderer uses the shared explanation, while query-scoped evidence
appears once for the displayed result set rather than inside each card. This
continues the Browser's existing deliberate host-specific rendering instead of
introducing Markout into interactive cards. Operation feedback, item failures,
completion accounting, and window credit retain their existing owners.

The planned CLI projection consumes the same evidence and lowers its compact
explanation through Sections/Markout. CLI facet execution remains unimplemented;
this evidence change does not advertise a new CLI command.

## Boundary and evidence

The external actor is a package publisher; input arrives as manifest dependency
IDs and admitted archive paths. At the transition to display evidence, preview
construction applies the existing `InertString` field contract. Browser HTML
encoding remains the final sink boundary. Typed item counts are calculated
before preview encoding or shortening.

`PackageQueryTests` is the Release outcome gate for distinct IDs, multiple
frameworks, root and nested skill paths, preview bounds, text containment,
unchanged acquisition counts, and visible unavailable content. Browser engine
tests gate the typed projection; frontend source and view tests gate transport,
package-specific cards, and once-per-result-set context.

The design follows ordinary count-plus-preview disclosure: NuGet manifest
dependency groups supply the existing structured facts, while the current
Package Query inventory supplies skill-document presence. These are input
evidence, not authority for a new dependency resolver or skill parser.

## Three-step production adoption

1. Produce shared typed summaries and scope classification with their outcome
   gates.
2. Adopt them in the Browser facade and cards, retiring repeated query context
   from cards in the same change.
3. Adopt the shared evidence when CLI Package Query execution lands under
   [#5919](https://github.com/richlander/dotnet-inspect/issues/5919) and the
   [CLI owner](package-query-cli.md); keep this step visibly pending in #6071.
