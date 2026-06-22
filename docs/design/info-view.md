# Bare `-S` default view

Bare `-S` renders a curated high-density view. It is not discovery; use `-D` to discover effective sections and columns.

This view is a high-density preset for the question an agent or human is most likely asking after the default output is too thin but before exhaustive inspection is warranted. It should answer one bullseye question with a small set of stable sections.

## Design contract

A default preset should:

- answer one clear question for the command/context;
- use real sections, not fake schema entries;
- include one to three token-dense sections;
- prefer local or bounded work;
- prefer summary variants when they answer the question;
- avoid exhaustive inventories unless they are the point of the question;
- avoid compact context rows; focused `-S Section` owns that framing;
- stay distinct from `-S @All`, which is the exhaustive document view.

Sections are stable content units. Presets decide where those sections appear. The same section can appear in the default view, focused selection, `-S @All`, and discovery output.

## Current presets

| Command/context | Bullseye question | Sections | Why these sections |
| --- | --- | --- | --- |
| `package X` | What is this package, and what does it ship? | `Package Info`, `Library Files` | Identity/metadata plus asset shape answer most package triage questions without a dependency inventory. |
| `library X` | What is this assembly, and what dense metadata signals are present? | `Library Info` | Library Info includes identity plus counts for large metadata lists, avoiding noisy inventories while signaling what exists. |
| selected `member Type.Member:N` | What is this overload, and what does it do? | `Signature`, `Decompiled Source` | Contract plus readable raised C# usually answers the first implementation question without IL or SourceLink source. |
| `type Type` or broad `member Type` list view | What API member groups are in this type space? | Member summary sections (`Constructors`, `Properties`, `Method Groups`, etc.) | Compact per-member-kind summaries show return types and overload counts without full signatures. |
| `member Type -m Name` | Which overloads exist for this logical member? | `Methods` or the matching member-kind section | The query is already narrowed to one member name, so overload rows with signatures are the bounded high-value answer. |

## Non-presets and open questions

Some commands or contexts should not get a default preset until there is a clear bullseye question.

| Command/context | Status | Reason |
| --- | --- | --- |
| `diff` | No preset yet | Existing flags (`--breaking`, `--additive`, filters) already define the question. |
| relationship commands | No preset yet | Their default outputs are already focused answers. |

## Relationship to other selectors

| Syntax | Question answered |
| --- | --- |
| default / `-v:m` | What is the quickest useful answer? |
| bare `-S` | What is the best dense evidence bundle? |
| `-D` | What can I select or project? |
| `-S Section` | Show this specific evidence. |
| `-S @All` | Show the exhaustive document coherently. |

## Maintenance guidance

When adding or changing a default preset, first write the bullseye question. Then select the smallest section set that answers it. If the question needs more than three sections, the preset is probably not focused enough.

Avoid adding a section just because it is cheap. `@Default` is not "all safe sections"; it is the smallest high-value bundle.

For library defaults, large local metadata lists such as `Async Methods`, `Custom Attributes`, `Extension Methods`, `Resources`, and `Type Forwarders` are intentionally represented as counts in `Library Info`. Select those sections explicitly when the list itself is the answer.
