# Bare `-S` default view

Bare `-S` renders a curated high-density view. It is not discovery; use `-D`
for fast orientation and `-D --effective` when actual producer-backed
effectiveness is required.

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
- stay distinct from explicitly selected domain categories.

Sections are stable content units. Presets decide where those sections appear.
The same section can appear in the default view, focused selection, an authored
category, and discovery output.

## Current presets

| Command/context | Bullseye question | Sections | Why these sections |
| --- | --- | --- | --- |
| `package X` | What is this package, and what does it ship? | Effective network-free `Fixed` sections | The candidate rule is stable while applicability remains honest: a package without a README omits that section. `Package Info` summarizes the asset shape without listing every file. |
| `library X` | What is this assembly, and what dense metadata signals are present? | Effective `Base ∩ Fixed ∩ NetworkFree` sections | Identity and bounded evidence remain compact; large metadata inventories stay behind focused sections. |
| selected `member Type.Member:N` | What is this overload, and what does it do? | `Signature`, `Decompiled Source` | Contract plus readable raised C# usually answers the first implementation question without IL or SourceLink source. |
| single `type Type` | What is this type? | `Type Info` | The one section whose row set does not grow with the type, so the overview is the same shape for a 250-member class and an 8-member enum. `Interfaces` is the one collection that is counted rather than listed (it reports `9`). Generic identity and constraints stay width-variable: `Type`, `Base`, and `Type Parameters` all spell out every parameter, so rows widen with arity even though the row *count* does not change. Tracked as #3616. |
| broad `member Type` list view | What API member groups are in this type space? | Member summary sections (`Constructors`, `Properties`, `Method Groups`, etc.) | Compact per-member-kind summaries show return types and overload counts without full signatures. |
| `type` listing (a prefix matching several types) | What assembly is this, and how big is its public surface? | `API Info` | The one section whose row set does not grow with the assembly, so the overview is the same shape for a 4-type library and a 1353-type one. The three surface counts (`Types`, `Methods`, `Properties`) summarize what the per-kind tables (`Classes`, `Structs`, ...) would otherwise list in full. The prefix-match path resolves its sections against the listing pipeline that renders them, so `-D` and `-S` agree there: a query that entered as a single type but renders a listing defers the decision until the type lookup has run, and each render path resolves the selector against its own pipeline. |
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
| `-D --effective` | Which base sections actually have data after full probing? |
| `-S Section` | Show this specific evidence. |
| `-S @Category` | Show one authored evidence domain. |

## Maintenance guidance

When adding or changing a default preset, first write the bullseye question. Then select the smallest section set that answers it. If the question needs more than three sections, the preset is probably not focused enough.

Avoid adding a section just because it is cheap. The default preset is not "all safe sections"; it is the smallest high-value bundle.

For library defaults, large local metadata lists such as `Async Methods`, `Custom Attributes`, `Extension Methods`, `Resources`, and `Type Forwarders` are intentionally represented as counts in `Library Info`. Select those sections explicitly when the list itself is the answer.

Once a command's identity facts exist as a bounded section, the inline compact fields list that carries the same facts belongs to `-v:q` alone. `-v:q` renders no sections, so the line is the only identity that view has; every other view can reach the same facts through the section, and carrying both would answer one question twice. The `type` listing follows this: the compact line renders at `-v:q`, and `API Info` carries the facts everywhere else. The one exception is `--fields`, which names those document fields directly — suppression is a decision about the default view, so an explicit projection opts out of it rather than losing its target.
