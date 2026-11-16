# Member diff

## Status and owner

This document owns the **Member diff model**: what a comparison of one Member
between two versions shows, for both Public API and Member Body content. It is
tracked by [#11185](https://github.com/richlander/dotnet-inspect/issues/11185)
under the Compare tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

The normative claim is:

> A Member diff is one document over one input of the Member: its
> declaration for Public API, or the complete Member for Member Body. It
> retains the compared lines, the declarations that enclose them on each side,
> and the annotations on both. Unified diff and git hunks lower from it with
> full fidelity, and the inspect printer prints all of it.

It consumes and does not redefine:

| Owner | Consumed contract |
| --- | --- |
| [Annotated Source diff document](annotated-source-diff-document.md) | Member Body's representation: both sides' annotated documents, the text comparison, and the fact comparison |
| [Library API diff presentation](library-api-diff-presentation.md) | Member relations, their correspondence, and Metadata-owned classified API changes |
| [Analysis diff](analysis-diff.md) | The line comparison and its relations |
| [Diff system](diff-system.md#rendering-models) | Markout's `MappedTextDiff` lowering and its GNU and annotated rendering models |
| [Caret stacking](caret-stacking.md) | One caret per annotated extent on a line |

The document is host-neutral. Its query composes beside the Annotated Source
diff document's query in `DotnetInspector.ResearchQueries`, and the CLI and
Inspect Web only render it. The
[Inspect Web Compare Experience](inspect-web-compare-experience.md#member-result-boundary)
adopts this model at its Member result boundary.

## Why

Member Compare answered Public API with classified prose only: "Member
'GetTypeInfo' was added", which does not say which overload, or "the
containing Type was added as a whole". Neither the browser nor CLI `diff -m`
showed the declaration. Member Body, meanwhile, already showed an annotated
diff. The two contents of one Member used two models.

For example, System.Text.Json 10.0.0 adds `JsonSerializerOptions.Strict`. For
9.0.0 to 10.0.0, `diff` reports only "Member 'Strict' was added". As a Member
diff, it is a one-sided `public static JsonSerializerOptions Strict { get; }`
inside `public sealed class JsonSerializerOptions` in `System.Text.Json`. Its
git hunk is headed by that class declaration.

.NET API review reads API change as a signature diff. A text diff can only
show what changed. This system knows the structure, so it can also show where
the change sits: the Type a new Member joins and the declaration a body change
belongs to. The document keeps all of it. Unified diff and git hunks, the
formats people and tools already read, receive everything they can express,
exactly. The inspect printer shows the rest.

## Examples

The examples use a small library, `Example`, in versions 1.0.0 and 2.0.0, and
show the inspect printer. A marker column precedes each line: `+` and `-` for
difference, a space for context, and `·` for enclosure. The `·` marker is
illustrative; [the inspect printer](#inspect-printer) settles the printed form.

**A changed signature, Public API.** `Widget.Run(int)` became `Run(long)`:

```text
·  namespace Example;
·  public class Widget
·  {
-      public void Run(int count);
+      public void Run(long count);       Breaking: parameter type changed
                       ^^^^ changed from int
·      … 4 other members
·  }
```

The differ marks the part that differs, the parameter type, with a caret and
its counterpart. The relation's classified change is a note on the
declaration, not a separate list. The Type declaration is enclosure: it
locates `Run` and makes no claim about whether `Widget` itself changed. The
same diff lowered to a git hunk keeps the compared lines and puts the
enclosing declaration in the hunk heading:

```diff
@@ -1 +1 @@ public class Widget
-public void Run(int count);
+public void Run(long count);
```

**An added Member in an added Type, Public API.** `Gauge` is new in 2.0.0:

```text
·  namespace Example;
·  public sealed class Gauge                  Type added in 2.0.0
·  {
+      public double Read(TimeSpan window);
·      … 2 other members
·  }
```

The Type's declaration line cannot be context, because it does not exist in
1.0.0, and it is not a difference of this Member, because the Type's addition
belongs to the Type. It is enclosure that carries a note pointing to where
that fact is reported. The addition is one-sided. Its git hunk has no heading,
because the baseline has no enclosing declaration.

**An unchanged Member, Public API.**

```text
·  namespace Example;
·  public class Widget
·  {
       public string Name { get; }            Unchanged since 1.0.0
·  }
```

The two declarations compare equal, so the declaration is context. Its
unified diff has no hunk.

**A changed body, Member Body.**

```text
·  namespace Example;
·  public class Widget
·  {
       public void Run(long count)
       {
-          var buffer = new byte[count];
+          var buffer = ArrayPool<byte>.Shared.Rent((int)count);
                        ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ allocation removed
           …
       }
·  }
```

The Member's declaration is compared at this scope and is equal, so it is
context. The containing Type is enclosure. The git hunk's heading is
`public void Run(long count)`, the baseline declaration that precedes the
hunk. The fact transition is a caret annotation once a host presents Member
Body facts, as [Hosts](#hosts) states.

## Document

The document records three parts. No printer infers a part from text or
styling.

**Compared lines.** Each side's input is a numbered sequence of lines. The
comparison relates the two sequences as an `AnalysisDiff`: each line is
**context**, equal on both sides, or **difference**, present on one side only
or changed. These are unified diff's line kinds, and only difference lines
count toward change statistics. Hunks are a lowering of the comparison, not
part of the document, as the Annotated Source diff document states.

**Enclosure.** For each side, the declarations that enclose the compared input,
outermost first, each with its text and optional notes: the namespace and the
containing Types. The compared input's own lines are never enclosure, so
Member Body's declaration is compared and Public API's containing Type is not.
Siblings the input omits are recorded as counts, such as `… 4 other members`.
Enclosure is not compared at this scope and never counts. A note on enclosure
points to where its own change is reported, such as "Type added in 2.0.0",
because a Type's change is reported once, at Type scope.

The document also states which enclosure a host prints, so every host prints
the same one: the current side's when the enclosing declaration exists there,
and the baseline's otherwise. Because enclosure is not compared, a Type that
changed shows only that side's declaration. When a relation's two sides belong
to different declaring Types, each side's enclosure is its own segment, and
the baseline's segment comes first in leading and in trailing enclosure.

**Annotations.** An annotation is a caret on the span it concerns, with its
text beneath, as [caret stacking](caret-stacking.md) renders it, or a note.

- **Public API**: each declaration part that differs between two present
  sides is a caret annotation with its counterpart text, as the structural
  printer labels a changed span ("changed from int"). The caret underlines
  only the tokens that differ within the part, such as `long` in
  `long count`. A part present on one
  side only is a caret labeled added or removed, with no counterpart text. A
  one-sided diff has no part carets, because its whole declaration is the
  difference. Each classified API change of the relation is a note
  on the declaration, because the classifier names no part. The note sits on
  the current side's declaration line, or on the only side present.
- **Member Body**: each fact transition of the fact comparison is a caret
  annotation on the span its coordinates reach.
- **Unanchored**: a classified change or fact transition that reaches no
  compared span, such as an IL-only or header fact, is an unanchored note on
  the Member. No change is dropped, and no caret is invented.
- **Status notes** on context or enclosure, such as "Unchanged since 1.0.0" or
  "Type added in 2.0.0", state status. They are not changes.

The annotations are the answer. A host does not repeat them as a separate
prose list.

## Inputs

**Public API** compares the Member's declaration as the product's C# display
policy prints it, custom attributes included: attributes, modifiers,
signature, accessors, nullability, constraints, and `[Obsolete]`, without a
body. Its lines are numbered within the declaration. The input identifies its
parts: each attribute, each modifier, the return type, the name, each
parameter, each accessor, and each constraint. Parts correspond across sides
by role and position: attributes by attribute Type and then by position among
attributes of that Type, modifiers by keyword, the return type, the name,
parameters by position, accessors by kind, and constraints by type parameter.
A part present on one side only is a difference.

Parts are identified from the model, never inferred from text. When the
printed declaration and the model's parts do not agree, the side records its
parts as unidentified: the compared lines remain, and no part caret is placed.

Attributes pair by their Type's identity, which the API surface records for
each retained attribute beside its rendered text. The surface extractor knows
that identity when it renders the attribute from metadata. MD1 records it
where the surface keeps only rendered text today. A reordered attribute list
therefore pairs `A("old")` with `A("new")` and carets the changed argument.

Type names in both declarations are shortened against the names of both
versions together, so the same Type is spelled the same way on each side. A
name may be qualified more than either version alone would need, for example
when the current version introduces a second Type with the same simple name.
A name never differs between sides only because of how it was shortened.

A containing Type that lies outside the compared surface is recorded in
enclosure by its name only, without declaration text.

The two sides' declarations are compared, and that comparison decides whether
the declaration changed:

- A Member that the Library API Diff relates across versions uses that
  relation's two sides. A Member on one side only is a one-sided diff. A
  related Member whose two declarations print the same is still Changed: its
  lines are context and its classified changes are notes on the declaration,
  because the change lies outside what the declaration prints.
- A related Member under an incomplete comparison is still diffed from the
  relation's two sides, and the document records the comparison as
  incomplete. The host reports that incompleteness as it reports any
  incomplete result.
- A Member with no relation, under a complete comparison, pairs with the
  baseline Member of the same exact identity, the pairing
  [#10201](https://github.com/richlander/dotnet-inspect/issues/10201) owns.
  The declaration comparison decides, so an attribute change outside the
  comparison's classified scope is a difference with its caret and no
  classification note. A missing counterpart is a failure, not an addition.
- A Member with no relation, under an incomplete comparison, is Unavailable
  with the comparison's reason and holds no lines, because the missing
  relation may be a correspondence the comparison did not reach.

This design does not change Member correspondence.

**Member Body** compares the complete Member through the
[Annotated Source diff document](annotated-source-diff-document.md). Its sides,
media, line maps, and comparisons are unchanged.

## Lowering

### Unified diff and git

The document lowers to unified diff through Markout's existing GNU lowering of
`MappedTextDiff`. Full fidelity means:

- the output is a valid unified diff over the two sides' numbered inputs;
- when the inputs differ, applying it to the baseline input with `patch` or
  `git apply` yields the current input;
- when the inputs are equal, it has no hunks, as `diff -u` prints nothing for
  equal files; and
- every line it prints is exactly the document's, with nothing approximated.

The text after a hunk's closing `@@` is its heading. Git writes there the
nearest function line that precedes the hunk's first line on the baseline
side. The lowering follows that rule over the document's baseline
declarations: the nearest declaration line of the baseline input that
precedes the hunk, or else the innermost baseline enclosure. A hunk whose
baseline has neither has no heading.

Only Markout forms hunks, so only Markout can choose a hunk's heading. Markout
0.40.1 has no heading: `TextDiffHunk` carries none, and its formatters write
only a change label after `@@`. MD1 therefore extends Markout through
[Markout co-development](../markout-co-development.md), released before MD1's
dotnet-inspect change:

- `MappedTextDiff` accepts heading candidates: the baseline lines that begin a
  declaration, each with its heading text, and a fallback heading for the
  innermost baseline enclosure. The Member diff document supplies them;
  Markout computes nothing from text.
- Lowering gives each hunk its heading by the rule above, and the formatters
  write it first after `@@`.
- When a hunk also has a change label, the label follows the heading. Both are
  recoverable exactly from the header, even when either text contains the
  separator: an MD1 test parses each labeled,
  headed hunk back to its heading and its label. The separator between them
  differs from existing models and is not obvious, so the MD1 trials choose it
  with the inspect printer's open choices.

A diff without heading candidates lowers exactly as it does in 0.40.1.

Enclosure lines, sibling counts, annotations, and notes do not appear, because
unified diff has no place for them. A heading is not an enclosure line.

### Inspect printer

The inspect printer is the CLI's text output for `diff`. It prints the whole
document: enclosure leading and trailing the compared lines, never
interleaved with them; sibling counts; the compared lines; caret annotations
beneath their lines through the reusable annotation renderer that caret
stacking defines; notes at the end of their lines; and unanchored notes before
the diff. It prints the enclosure segments the document states.

The printer follows unified diff wherever the meaning is the same and
unambiguous: the `+`, `-`, and space markers and the order of lines. Where it
differs and the choice is not obvious, fresh-agent trials under the
[usability trials](../../tools/ExplainReadingScenarios/usability/README.md)
settle it before MD1 lands, with the git lowering of the same diffs as the
baseline. The open choices are:

- how a plain-text enclosure line is told apart from a context line;
- where carets and notes sit relative to the difference lines; and
- whether `@@` hunk headers appear when enclosure already locates the change;
  and
- in unified diff output, the separator between a hunk heading and its change
  label.

A trial asks each agent what changed, where, and whether it is classified,
and records any agent that reads enclosure as a change or as compared.

## Hosts

| Host | Presentation |
| --- | --- |
| Agent | The complete document as JSON, with every line's kind, each side's enclosure, annotations, and notes. CLI `diff -m --json` emits it by default; `-S Changes --json` keeps the classified-change rows |
| CLI text | The inspect printer |
| GNU-compatible output | The unified diff lowering, with hunk headings |
| Inspect Web | Member Compare's Public API and Member Body contents, in the shared [diff viewer](inspect-web-diff-viewer-interaction.md) as enclosure rows, annotations, and its mapped diff |

Member Body fact transitions are in the document from MD3. A host presents
them as annotations when Member Body's fact presentation adopts them; until
then the [Member Body reader](inspect-web-member-body-diff.md) stays text-only,
as its owner states.

Every host renders from the same document. None recomputes a comparison or
infers a line kind.

## Non-claims

This design does not claim:

- a Type-scope or Library-scope diff, which may adopt this document later;
- a Public API input numbered within its Type's listing;
- a change to Member correspondence, so moved and renamed Members stay
  governed by their owners;
- how much work a Member comparison costs, which
  [#10201](https://github.com/richlander/dotnet-inspect/issues/10201) owns; or
- an authored-Source comparison, which remains an
  [Explore](inspect-web-compare-explore.md) mode.

## Adoption

Each step is independently mergeable and lands with its production host.

| Step | Delivers | Production host |
| --- | --- | --- |
| MD1 | The Public API Member diff document: declaration input with identified parts, compared lines, enclosure, and annotations; the released Markout heading extension and the document's unified diff lowering with headings; the inspect printer, with its open choices settled by agent trials | CLI `diff -m`: JSON, inspect printer, and GNU-compatible output |
| MD2 | Inspect Web Member Compare's Public API content on that document, through a typed Browser transport for it, with the diff viewer's enclosure rows, annotation rendering, and Member diff Identical rows; retirement of the inline classified list and authored-Source section | Inspect Web Member Compare |
| MD3 | Member Body on the same document, with the containing Type as enclosure and fact transitions recorded; CLI text becomes the inspect printer, and GNU-compatible output gains hunk headings | CLI `diff -m` Member Body and Inspect Web Member Body |

## Acceptance scenarios

1. A Member whose parameter type changed prints with a caret on that
   parameter's type and the relation's classification as a note on the
   declaration. Its git lowering is one hunk headed by the baseline Type
   declaration.
2. A Member of an added Type prints a one-sided diff whose Type declaration is
   enclosure noted "Type added", and the Type's addition is not counted as a
   Member difference. Its git hunk has no heading.
3. An unchanged Member of a complete comparison prints its declaration as
   context with an "Unchanged since" note, and its unified diff has no hunk.
   A Member whose only change is an attribute argument prints that attribute
   as a difference with its caret. Under an incomplete comparison, a Member
   with no relation is Unavailable with its reason and holds no lines.
4. A body change with an equal declaration prints the declaration as context
   and the containing Type as enclosure. Its JSON records each fact transition
   as an annotation, and an IL-only transition as an unanchored note.
5. The unified diff lowering of each changed Member above applies to the
   baseline input with `git apply` and yields the current input, and the
   unchanged Member's lowering has no hunks. No lowering contains an
   enclosure line, sibling count, annotation, or note. A Member Body hunk
   that has both a baseline declaration and a change label prints both, the
   heading first, and each parses back exactly.
6. JSON output of each of the above records every line's kind and each side's
   enclosure.
7. The MD1 trials record, for each open printer choice, the chosen form and
   the agent answers it was judged on.
8. A Member whose attributes were reordered and one argument changed pairs
   each attribute with its counterpart by Type, and carets only the changed
   argument. A removed `virtual` and an added setter each carry a caret on
   their part.
9. When the current version adds a Type with the same simple name as a Type
   in a Member's declaration, that Type is spelled the same way on both
   sides, and no line differs only in its spelling.
10. A related Member under an incomplete comparison prints its diff, its JSON
    records the comparison as incomplete, and the CLI reports the
    incompleteness and exits nonzero.
