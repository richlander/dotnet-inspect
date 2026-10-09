# Diff observability

## Status and owner

This document owns the principle every diff producer and view uses to decide
whether a difference is a reported change. It governs
[Fast Diff](fast-diff.md), [IL diff canonicalization](il-diff-canonicalization.md),
[Implementation Diff](implementation-diff.md), and the
[annotated source diff document](annotated-source-diff-document.md). Each of
those owns its own mechanics; this document owns only how they reason about
subtle differences.

## Principle

> **A reported change is observable.** Every change a producer reports must be
> visible in a complete diff view at the level that view presents. A
> difference that no view at any level can show is not a change. A
> difference a view can show must not be hidden.

## Levels of difference

A subtle diff is one that exists at some levels and not at others. Reason
about it per level:

| Level | What differs | Where it is observable |
| --- | --- | --- |
| Semantic | Behavior and meaning: source, signatures, attributes | Annotated source diff (C#) and the API diffs |
| Symbolic IL | Operations, with operands resolved to names | The IL view of the annotated source diff |
| Encoded | Token numbers, blob bytes, heap offsets, row order, short and long branch forms | No view today |

## Rules

1. **A view declares its level and reports only differences at that level.**
   A symbolic view must not present an encoding-only difference as a change,
   and a semantic view must not hide a symbolic difference it claims to cover.
2. **A summarizing producer matches the level of the view it summarizes.**
   Fast Diff's Body axis answers whether the body diff would show something,
   so it compares at the symbolic level. Its `Unchanged` must mean no
   observable difference at that level, and its `Changed` should mean there is
   something to see.
3. **Over-reporting stays within observability.** A producer that may
   over-report still reports only differences a user can find in some view:
   a renumbered compiler-generated name is visible in the IL view, and an
   attribute on an internal member is visible in the complete API and source
   views. A fact no view presents is either added to a view or dropped from
   the producer.
4. **A level below every view is a view gap, not a hidden change.** When
   encoding-only differences matter, the answer is an explicit view at the
   encoded level, such as an encoded-IL view, that reports at that level.
   Producers do not leak encoding differences into symbolic views to
   compensate.
5. **Oracles are views too.** When a producer and an oracle disagree, the side
   that breaks rule 1 is wrong. An oracle that reports an encoding-only
   difference at the symbolic level is not evidence of a missed change.

## Known divergences

These current behaviors break a rule. Each is a follow-up for its owner, not
a change to the principle.

- **Stand-alone signatures in IL comparison.** IL diff canonicalization
  resolves a `calli` stand-alone signature to its signature bytes, which embed
  token numbers. A Type renumbered between builds therefore shows as a
  changed IL operation although the decoded signature is equal. The symbolic
  level needs the signature decoded with its Types resolved by name. Fast Diff
  already compares it that way, so its three System.Private.CoreLib 10 to 11
  `calli` sites are correctly `Unchanged`.
- **`beforefieldinit`.** Fast Diff's Body axis compares this flag when a Type
  has a static constructor, because it changes when initialization runs. No
  view presents Type header flags today, so under rule 3 a view should show it
  or Fast Diff should drop it.
