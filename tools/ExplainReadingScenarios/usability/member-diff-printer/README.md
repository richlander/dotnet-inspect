# Member diff printer trials

A `·` marker alone does not tell a reader that enclosure was not compared.
Both participants in the unmarked arm and both in the marked arm read every
enclosure line as shared context. They reported that `Widget` was
`public sealed class Widget` in 1.0.0 and unchanged, which is false. A
one-line legend removed every such misread, in both legend arms. `@@` hunk
headers changed nothing. Carets and notes beneath and beside their lines were
read correctly everywhere.

For the unified diff header, a pipe and a bracketed label were each
recovered exactly in 12 of 12 headers, and `//` in 11 of 12. The miss left an
escaped `\/\/` in place.

Normative owner: [Member diff — Inspect printer](../../../../docs/design/member-diff.md#inspect-printer).
Focused issue: [#11185](https://github.com/richlander/dotnet-inspect/issues/11185).
Design: [PR #11187](https://github.com/richlander/dotnet-inspect/pull/11187).

## Fixed inputs

[Scenarios](scenarios.json) hold six Member comparisons from the design's
examples and their ground truth: a changed parameter type, a Member of an
added Type, an unchanged Member, an attribute-only change, a body change, and
a removed Member. In the scenarios, `Widget` became `sealed` in 2.0.0, and the
inspect printer shows its 2.0.0 declaration as enclosure. A reader who takes
enclosure for compared context concludes that `Widget` was already sealed in
1.0.0. That misread is the primary measure.

[`generate.py`](generate.py) renders each arm from the scenarios:

| Arm | Rendering |
| --- | --- |
| G | Git lowering with hunk headings: the conventional baseline, not a candidate |
| U | Inspect printer, enclosure unmarked |
| M | Enclosure marked `·` |
| ML | M with a one-line legend |
| MLH | ML with `@@` hunk headers |
| sep-pipe, sep-bracket, sep-comment | Six unified diff headers: `heading \| label`, `heading [label]`, `heading // label`, each escaping its own separator inside a heading |

The separator headers include an indexer, a default value containing `|`, and
one containing `"a // b"`, so each form meets one heading that it must escape.

[Criteria](criteria.txt), the [protocol](participant.txt), the
[main](task-main.txt) and [separator](task-separator.txt) tasks, and the
rendered [arms](arms/) are hashed in [dispatch.json](dispatch.json) before
dispatch. [`run.py`](run.py) refuses to dispatch if any input changed.

Each participant is a fresh GPT-6 Sol agent at high reasoning, with two per
arm. It runs in an empty directory with no custom instructions and no tools,
and its rendering is embedded in the prompt. It answers in JSON. There are
16 participants, all of whose answers parsed.

## Results

[`score.py`](score.py) scores the frozen [answers](results/) against the
criteria and writes [assessment.json](assessment.json).

| Arm | Enclosure misreads, of 5 per participant | Errors outside the misread |
| --- | --- | --- |
| G | 0, 0 | 0, 0 |
| U | 5, 5 | 1, 0 |
| M | 5, 5 | 1, 0 |
| ML | 0, 0 | 1, 1 |
| MLH | 0, 0 | 1, 1 |

Each misread fails two fields, the 1.0.0 declaration and whether the Type
changed, so `assessment.json` counts 10 more field errors in each U and M
answer.

Every error outside the misread answers the same question in S2: whether the
added `Gauge` Type's declaration changed. Six participants answered "not
stated", because there was no earlier declaration to compare; two answered
"changed". That question was ambiguous for an added Type, so it is not a
misread. U and M
participants said so directly: "Treated · lines as shared context."

Carets and notes were attributed correctly in every inspect arm: the
parameter type in S1, the attribute argument in S4, the body allocation in S5,
and the removal in S6. Two participants called S5's "allocation removed" a
classification, which the criteria accept.

| Separator | Exact heading and label, of 6 per participant |
| --- | --- |
| Pipe | 6, 6 |
| Bracket | 6, 6 |
| `//` | 5, 6 |

The `//` miss transcribed `"a \/\/ b"` with its escape and noted that it could
not tell a display escape from source text. Every participant that unescaped
did so correctly and said it had to infer the escape.

## Decisions by the fixed criteria

1. **Enclosure marking:** ML, `·` with a one-line legend, is the only form
   without misreads. A marker without a legend does not work.
2. **Caret and note placement:** accepted as designed.
3. **`@@` headers in the inspect printer:** not adopted. MLH matched ML.
4. **Separator:** pipe and bracket tied, so the operator decided, and chose
   the pipe. Brackets occur in many declarations (arrays, indexers,
   attributes) and would need escaping often. `|` occurs only in default
   values.

Two participants per arm are diagnostic, not a population estimate.
