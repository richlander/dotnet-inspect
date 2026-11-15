# Member diff

## Status and owner

This document owns the **Member diff model**: what a comparison of one Member
between two versions shows, for both Public API and Member Body content. It is
tracked by [#11185](https://github.com/richlander/dotnet-inspect/issues/11185)
under the Compare tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

The normative claim is:

> A Member diff is one annotated diff over one input of the Member: its
> declaration for Public API, or the complete Member for Member Body. Every
> printed line is a **difference**, **context**, or **enclosure**, and the
> document records which. Classified changes and fact transitions are
> annotations on the spans they concern. Enclosure locates a difference
> within the larger shape and is never compared at that scope. No
> GNU-compatible projection contains enclosure.

It consumes and does not redefine:

| Owner | Consumed contract |
| --- | --- |
| [Annotated Source diff document](annotated-source-diff-document.md) | Member Body's representation: both sides' annotated documents, the text comparison, and the fact comparison |
| [Library API diff presentation](library-api-diff-presentation.md) | Member relations, their correspondence, and Metadata-owned classified API changes |
| [Analysis diff](analysis-diff.md) | The line comparison and its relations |
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

.NET API review reads API change as a signature diff. A text diff can only
show what changed. This system knows the structure, so it can also show where
the change sits: the Type a new Member joins and the declaration a body change
belongs to. That makes the diff a more complete record than its text alone.

## Examples

The examples use a small library, `Example`, in versions 1.0.0 and 2.0.0. A
marker column precedes each line: `+` and `-` for difference, a space for
context, and `·` for enclosure. Enclosure is drawn dimmed in a rich host. The
`·` marker exists only for these examples.

**A changed signature, Public API.** `Widget.Run(int)` became `Run(long)`:

```text
·  namespace Example;
·  public class Widget
·  {
-      public void Run(int count);
+      public void Run(long count);
                       ^^^^ Breaking: parameter type changed from int to long
·      … 4 other members
·  }
```

The classified change is a caret annotation on the parameter type, not a
separate list. The Type declaration is enclosure: it locates `Run` and makes
no claim about whether `Widget` itself changed.

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
that fact is reported. The addition is one-sided.

**An unchanged Member, Public API.**

```text
·  namespace Example;
·  public class Widget
·  {
       public string Name { get; }            Unchanged since 1.0.0
·  }
```

The declaration was compared and is equal, so it is context.

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

The Member's declaration is compared at this scope, so it is context. The
containing Type is enclosure. The fact transition is a caret annotation.

## Line kinds

Every printed line has exactly one kind, recorded in the document. A host
never infers a kind from text or styling.

- **Difference** lines are present on one side only or differ between sides.
  They carry `+` or `-` in every host and count toward change statistics.
- **Context** lines were compared at this scope and are equal on both sides.
- **Enclosure** lines were not compared at this scope. They show the larger
  shape that locates a difference: the namespace, the containing Type's
  declaration, an enclosing declaration, and elided siblings.

Enclosure follows these rules:

- **It sits above the compared input.** Public API compares the declaration,
  so the containing Type and namespace are enclosure. Member Body compares the
  whole Member, so the Member's own declaration is context, and only what
  contains the Member is enclosure.
- **It never counts.** Enclosure contributes to no statistic and carries no
  difference marker.
- **It may carry a note, never a difference.** A note such as "Type added in
  2.0.0" points to where its own change is reported. A Type's change is
  reported once, at Type scope, not again on each Member.
- **It is printed from one stated side.** That is the current version when the
  enclosing declaration exists there, and the baseline otherwise. Because
  enclosure is not compared, a Type that changed shows only that side's
  declaration here.
- **Elision is enclosure.** A line such as `… 4 other members` keeps the shape
  complete without printing unrelated declarations, and it states what was
  left out.

## Inputs

**Public API** compares the Member's declaration as the product's C# display
policy prints it: attributes, modifiers, signature, nullability, constraints,
and `[Obsolete]`, without a body. The declaration input identifies its parts,
such as each attribute, the return type, the name, each parameter, and each
constraint, so an annotation can name the span it concerns. A Member that the
Library API Diff relates across versions uses that relation's two sides; a
Member on one side only is a one-sided diff. This design does not change Member
correspondence.

**Member Body** compares the complete Member through the
[Annotated Source diff document](annotated-source-diff-document.md). Its sides,
media, line maps, and comparisons are unchanged.

Both contents present their input the same way: one annotated diff, with
enclosure supplied by the Member's declaring Type and namespace.

## Annotations

An annotation is a caret on the span it concerns, with its text beneath, as
[caret stacking](caret-stacking.md) renders it.

- **Public API**: each classified API change of the Member relation is one
  annotation on the declaration part it concerns. Its text is the
  classification and the producer's explanation. A change that names no part
  is annotated on the whole declaration, and no change is dropped.
- **Member Body**: each fact transition of the fact comparison is one
  annotation on the span its coordinates reach.
- **Notes** on context or enclosure, such as "Unchanged since 1.0.0" or "Type
  added in 2.0.0", state status. They are not changes.

The annotations are the answer. A host does not repeat them as a separate
prose list.

## Hosts

| Host | Presentation |
| --- | --- |
| Agent | The complete document as JSON, with every line's kind, annotations, and notes |
| CLI text | The annotated diff with enclosure, in Public API and Member Body alike, through the reusable annotation renderer that caret stacking defines |
| GNU-compatible output | Difference and context lines only, through Markout's GNU-style lowering; enclosure, notes, and annotations are omitted |
| Inspect Web | Member Compare's Public API and Member Body contents, in the shared [diff viewer](inspect-web-diff-viewer-interaction.md), with enclosure drawn dimmed |

Every host renders from the same document. None recomputes a comparison or
infers a line kind.

GNU-compatible output omits enclosure because it is not valid patch context:
an enclosure line may not exist on the baseline side.

## Non-claims

This design does not claim:

- a Type-scope or Library-scope diff, which may adopt these line kinds later;
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
| MD1 | The Public API Member diff document: declaration input with identified parts, line kinds, enclosure, and annotations from classified changes | CLI `diff -m`: JSON and annotated text |
| MD2 | Inspect Web Member Compare's Public API content on that document; retirement of the inline classified list and authored-Source section | Inspect Web Member Compare |
| MD3 | Member Body on the same line kinds, with the containing Type as enclosure | CLI `diff -m` Member Body and Inspect Web Member Body |

## Acceptance scenarios

1. A Member whose parameter type changed shows one Public API diff whose
   classified change is a caret on that parameter's type.
2. A Member of an added Type shows a one-sided Public API diff whose Type
   declaration is enclosure noted "Type added", and the Type's addition is not
   counted as a Member difference.
3. An unchanged Member shows its declaration as context with an "Unchanged
   since" note.
4. A body change shows the Member's declaration as context, the containing
   Type as enclosure, and each fact transition as a caret annotation.
5. GNU-compatible output of each of the above contains no enclosure line.
6. JSON output of each of the above records every line's kind.
