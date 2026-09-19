# Package Query inspection evidence

## Claim and ownership

Package Query emits semantic answers separately from package-specific
inspection evidence. Evidence is structured data: named properties, numeric
facts, or a total with a bounded preview of the items actually observed.
Query-wide source selection and provenance remain distinguishable from
inspection facts about a package. This document is the sole owner of that
answer-and-evidence contract.

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
| Has dependencies / no dependencies | Distinct declared dependency IDs in the selected dependency scope, using ordinal case-insensitive identity | Up to three IDs, ordered ordinal case-insensitively, preserving the first declared spelling of each ID |
| Depends on package | Distinct selected declaration tuples of manifest group, declared package ID, and version range | Up to three `group: ID range` values, ordered ordinally |
| Embedded SKILL.md | Distinct matching archive-entry paths, using ordinal path identity and the existing case-insensitive skill-document predicate | Up to three actual paths, ordered ordinally |

Dependency summaries describe declarations, not a resolved dependency closure.
Package Query uses all manifest groups by default. An explicit
`dependency-target=<tfm>` uses the dependency-group owner's compatible
selection and retains requested-target and selected-group evidence separately.
`dependency-target=all` is query scope, not the manifest group named `any`.
Zero dependencies is a known empty item set only for all groups, one selected
empty group, or a manifest with no dependency groups; no matching requested
target is not empty evidence.

A root `skills/SKILL.md` preview remains that path. The inventory does not
establish a skill's declared name, valid frontmatter, or valid document body.
Content that cannot be acquired or evaluated retains the existing visible
failure outcome; unavailable evidence is not an empty item set.

Only already-requested inspection tiers contribute summaries. A dependency
summary consumes the admitted manifest; a skills summary consumes the entry
inventory already used by its selected content facet. Summaries do not request
additional manifests, archives, or skill-document bodies.

## Answers, evidence, and rendering

Each selected term that matches emits one typed answer carrying the
product-issued term ID, semantic value, and originating Portable Query term.
For example, `license=MIT` answers `MIT`; it does not answer with a sentence
about how MIT was identified. Presence terms answer their closed value, such
as `true` or `none`; `license=any` therefore answers `true`.

Evidence separately retains the product-issued ID, query or package scope,
originating term, and only structured facts:

- named inert properties such as nuspec license declaration kind and value;
- a typed number such as total downloads; or
- a complete observed item count with bounded inert previews.

Package Query never authors explanatory text. Consumers must not parse answers,
counts, identities, or provenance out of prose because the query layer emits no
such prose.

The Browser transports answers and evidence through its generated facade. Its
HTML card renderer constructs host presentation from those typed values, while
query-scoped evidence appears once for the displayed result set rather than
inside each card. This continues the Browser's existing deliberate
host-specific rendering instead of introducing Markout into interactive cards.
Operation feedback, item failures, completion accounting, and window credit
retain their existing owners.

The CLI projection consumes the same answers and evidence. Sections/Markout
renders a direct `Answer` column and a host-authored `Evidence` presentation;
JSON and JSONL remain valid projected data rather than query-authored prose.

## Boundary and evidence

The external actor is a package publisher; input arrives as manifest dependency
IDs and admitted archive paths. At the transition to display evidence, preview
construction applies the existing `InertString` field contract. Browser HTML
encoding remains the final sink boundary. Typed item counts are calculated
before preview encoding or shortening.

`PackageQueryTests` is the Release outcome gate for semantic answers, structured
license provenance, distinct IDs, all-group and selected-group dependency
scope, compatible selection, selected-empty, no-groups and no-match
distinctions, multiple frameworks, root and nested skill paths, preview bounds,
unchanged acquisition counts, and visible unavailable content. Browser engine
tests gate the typed projection; frontend source and view tests gate transport,
host rendering, package-specific cards, and once-per-result-set context.

The design follows ordinary count-plus-preview disclosure: NuGet manifest
dependency groups supply the existing structured facts, while the current
Package Query inventory supplies skill-document presence. These are input
evidence, not authority for a new dependency resolver or skill parser.

## Three-step production adoption

1. Produce shared typed answers, evidence facts, summaries, and scope
   classification with their outcome gates.
2. Adopt them in the Browser facade and cards, with presentation authored by
   the Browser host.
3. Adopt them in CLI Package Query through Sections/Markout, with direct answer
   values and host-authored evidence presentation.
