# Diff observability

## Status and owner

This document defines a cross-cutting pattern: the principle diff producers
and views adopt to decide whether a difference is a reported change, and how
to reason about subtle differences. It owns only the pattern. Each producer
or view adopts it as its own focused effort and owns its adoption decisions.

[Fast Diff](fast-diff.md) is the first adopter. IL diff canonicalization's
adoption is filed as
[#9801](https://github.com/richlander/dotnet-inspect/issues/9801); other
owners adopt in the same way.

## Principle

> **A reported change is observable.** Every change a producer reports should
> be visible in a complete diff view at the level that view presents. A
> difference that no view at any level can show is not a change. A
> difference a view can show must not be hidden.

## Levels of difference

A subtle diff is one that exists at some levels and not at others. Reason
about it per level:

| Level | What differs | Where it is observable |
| --- | --- | --- |
| Semantic | Behavior and meaning: source, signatures, attributes | Source and API diff views |
| Symbolic IL | Operations, with operands resolved to names | IL diff views |
| Encoded | Token numbers, blob bytes, heap offsets, row order, short and long branch forms, body size | No view today |

## Rules

1. **A view declares its level and does not overstate.** It presents only
   differences at its level as changes there: a symbolic view does not
   present an encoding-only difference as a symbolic change, and a semantic
   view does not hide a semantic difference it claims to cover. When a view
   shows no difference at its level but the subject changed at another level,
   it says so and names the level, rather than saying nothing.
2. **Every reported change has a view.** A surface that reports a change
   provides a view that shows it, defaulting to the highest level at which the
   change is visible. Declaring a change without any view that shows it is not
   sufficient.
3. **A summarizing producer targets the level of the view it summarizes.** A
   producer that answers "would this view show a difference?" compares at
   that view's level, so its `Unchanged` means no observable difference at
   that level and its `Changed` means there is something to see.
4. **Over-reporting stays within observability.** A producer that may
   over-report reports only differences a user can find in some view at some
   level. A compared fact that no view presents is a known divergence of that
   producer until a view presents it or the producer stops comparing it.
5. **A level below every view is a gap to close.** When differences at a level
   matter, the answer is a view at that level, such as an encoded-IL view.
   Until it exists, a difference only at that level is a producer's known
   divergence, and producers do not leak it into views at other levels to
   compensate.
6. **Oracles are views too.** When a producer and an oracle disagree, the side
   that breaks rule 1 is wrong. An oracle that reports an encoding-only
   difference at the symbolic level is not evidence of a missed change, and a
   producer that reports one is over-reporting.

## Adoption

An adopting owner records, in its own document:

- the level each of its results targets;
- the facts it compares that fall outside that level or that no view
  presents, as known divergences; and
- how its gates or oracles are read under rule 6.

Divergences are fixed by their owner as follow-ups. They do not change this
pattern.
