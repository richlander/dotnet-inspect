# Self-contained style document experiment

This experiment starts with the core data, critical queries, and normalization
tradeoffs. It does not start from the existing explanation graph's JSON tree.
It is a proposed lowering experiment, not a production style schema or an
adoption of Product Vocabulary's separately owned grammar migration (#9401).

The [JSON examples](../examples/README.md) include all four measured shapes,
pretty-printed for direct inspection.

The main precedent is the self-contained
[CVE document and specification](https://github.com/dotnet/designs/tree/b9bc7465feae40aa606b8e781fc290c20b690748/accepted/2025/cve-schema).
That design combines discoverable arrays, keyed shared records, and small
relationship indexes. Its indexes are justified by critical queries rather
than added to every relationship by convention. HAL navigation is a separate
choice and is unnecessary to answer any query in this experiment.

## Core data and questions

The selected core data consists of all 17 owner-issued style choices, all four
tiers, and all declared choice/tier properties. Names, descriptions, option
and value tokens, byte divergence, endorsement flags, conflict membership,
and tier order are retained. Shared vocabulary identity, source snapshot, and
property declarations appear once. Completeness applies to these two selected
populations, not every fact and relationship in a Resource Explanation catalog.

The seven critical questions are:

1. What choices exist, what do they mean, and which tier owns each?
2. Which choices preserve IL bytes, and which are oracle-endorsed?
3. Which selections are mutually exclusive?
4. Which option/value selections belong to one tier?
5. Given one known choice ID, what does it select?
6. How do I build a complete menu grouped under tier names and descriptions?
7. Which choices have oracle or corpus endorsement?

The sixth task matters: the earlier depth-one compact response includes the
Tier vocabulary root, but not its four term records. Rendering its complete
labels and descriptions requires another request. A self-contained style
document should answer this task locally.

## Reproduce

First capture the unchanged browser export using the parent directory's
capture tool. Then run from the worktree root:

```bash
python3 tools/ExplainReadingScenarios/compare-shapes.py \
  --browser-json /tmp/explain-reading-browser.json \
  --cli /path/to/compact/dotnet-inspect \
  --output /tmp/explain-shape-comparison
jq --arg task tier --arg tier Synthesis --arg id slot-local-names \
  -f tools/ExplainReadingScenarios/shapes/shared.jq \
  /tmp/explain-shape-comparison/shared.json
```

The script derives every record and index from actual owner data. It rejects
incomplete declaration coverage, missing required properties, identity or
property collisions, unsupported cardinalities, and nonlocal term references.
It verifies lossless recovery of every selected choice property, tier record,
description, and choice order, executes all 28 candidate/task combinations,
and compares their answers. A synthetic empty-tier boundary checks that a
group without choices still appears in the menu. The original four jq tasks
are also cross-checked
against the browser payload. No successful Type fixture is invented.

Generated documents, individual query answers, source hash, and comparison
report are written outside the repository. The optional CLI argument captures
both current compact responses and their total retrieval cost. The capture
and jq checks are functional evidence, not Wasm or performance measurements.

## Measured tradeoffs

Input: the 32,060 B production browser vocabulary export already measured in
the parent scenarios. Its two style vocabulary objects total 19,915 B. The
current compact CLI witness is `0.27.0+5ce2e00`. Sizes are UTF-8 minified JSON
with a trailing newline, before compression. Generated documents use the same
serializer settings; changes in escaping do not explain their size differences.

| Shape | Bytes | Normalization choice | Query cost |
| --- | ---: | --- | --- |
| Flat | 14,420 | Choice array with complete tier repeated per choice; shared tier table retains all tiers, including any empty tier. | Simple filters; repeated descriptions; grouped menu still joins tiers to retain empty groups. |
| Shared | 10,996 | Choice array references a tier ID; tier records stored once in a keyed table. | Simple choice filters and known-tier lookup; grouped menu joins choices by tier ID. |
| Sparse | 9,918 | Choice and tier records omit boolean flags and optional conflict fields; complete positive membership sets and conflict groups store those observations once. | Direct endorsement and conflict lookup; per-record negative predicates use set membership. |
| Indexed | 12,972 | Choices and tiers keyed by ID; explicit choice order; tier and conflict indexes contain only IDs. | Direct known-choice and reverse-group lookups; ordered listing adds a lookup per ID. |

All seven answers agree across candidates. Answer sizes remain 4,889 B for the
menu, 1,537 B for byte-preserving choices, 317 B for conflicts, 239 B for one
tier's selections, 208 B for a known choice, 5,542 B for the complete grouped
menu, and 204 B for the two endorsement lists. Each candidate answers all questions from one retrieved document.
Current compact CLI retrieval for the grouped menu is 43,136 + 6,799 =
49,935 B across two requests. The browser already supplies all core style data
within one 32,060 B response, including other vocabularies.

All candidates retain the same selected meaning. The flat candidate retains a
shared tier table so a tier without choices would not disappear; simply
flattening only observed choice/tier combinations would lose that fact.
In the indexed candidate, array order is retained explicitly rather than
relying on JSON object enumeration order. Its indexes are derived, never
independently authored.

## Proposed choice

The sparse shape is the preferred tested candidate. It keeps a discoverable
choice array and keyed shared tiers, but represents sparse properties as
complete membership tables. Of the original 68 choice-level flag/conflict
entries, 43 are false booleans and 12 are absent optional conflicts. Keeping
these in every entity record obscures the actual two divergent choices, five
oracle-endorsed choices, one corpus-endorsed choice, and two conflict groups.

`choice_sets` and `tier_sets` list IDs for true boolean observations;
`conflict_groups` maps group IDs to their member choice IDs. The tables are
core property observations, not redundant indexes over flags still repeated
in records. False and optional absence can be reconstructed for known records
because every source observation is available, its declared coverage is
complete, and the selected record populations are complete. Missing membership
is not a default for an unknown record or an unavailable observation.

The 9,918 B candidate saves 1,078 B (about 9.8%) against the shared baseline.
More significantly, the positive-data questions become directly addressable:

```jq
.choice_sets.corpus_endorsed
.conflict_groups["var-spelling-style"]
```

A per-record negative predicate now requires a membership lookup; `sparse.jq`
shows that extra work explicitly. This tradeoff earns its place for endorsement
and conflict discovery instead of pretending every query improves. Names,
summaries, option/value selections, tier references, and all property
interpretation remain available. The script reconstructs every original flag,
optional conflict, and tier flag before accepting the candidate.

The indexed candidate costs 1,976 B more than the record-based shared shape
(about 18%). It earns simpler direct
ID and conflict-group queries, while adding a lookup to ordered discovery.
For 17 choices, the evidence does not yet justify adopting all three indexes.
If direct ID lookup becomes a critical dominant task, evaluate a keyed choice
collection with explicit order separately from adding every reverse index.
No blanket rule mandates arrays or dictionaries for every explain component.

In the record-based comparison shapes, absent optional conflict properties
are null because the source declares complete OptionalOne and supplies an empty
observation. In the sparse shape, the same absence is encoded by lack of group
membership within the complete selected population. Unavailable or failed
observations cannot be encoded that way.
This experiment rejects unsupported input rather than pretending to settle
general outcome encoding. Original property declarations are retained;
we have not shortened the contract vocabulary as part of this comparison.

The shape is selected for the explicit whole-style-data task. A concise summary,
`.tips`, or `.reference` has a different core dataset and should not carry this
whole collection. That does not make the complete style document unnecessarily
large: its descriptions alone serve the requested menu task. The ordinary
explanation default and selection spellings need the focused owner's design
decision before production adoption.
