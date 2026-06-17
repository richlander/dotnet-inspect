# Shared metadata primitives — target layering

## Problem

Three layers read `System.Reflection.Metadata` (SRM) independently and each
re-implements the same mechanical primitives:

| Concern | `ILInspector.Metadata` | `ILInspector.Analysis` | `ILInspector.Decompiler` (Pipeline) |
| --- | --- | --- | --- |
| Type-name resolution (handle → name, nested-type walking) | `TypeResolver` | `TypeRefDecoder` / `MemberResolver.ResolveParentType` | `IrImporter` (inline) |
| Signature decoding | `SignatureDecoder : ISignatureTypeProvider<string,…>` | `TypeRefDecoder : ISignatureTypeProvider<TypeRef,…>` | own provider → IR `TypeRef` |
| Generic-parameter context | `GenericContext` | `GenericScope` | (inline) |
| Attribute decode | `AttributeReader` (decode + render) | — | — |
| Method-handle resolution (name + overload) | `AttributeReader.FindMethodHandleInType` | `MemberResolver` | (inline) |

The walks are the same; only the *output type* differs. This is real
duplication, not speculative.

## Why a shared library is sound (and what it must not become)

SRM is already the shared primitive, and its `ISignatureTypeProvider<T>` /
`ICustomAttributeTypeProvider<T>` pattern is the idiomatic sharing mechanism —
each consumer implements the provider for the type it needs and SRM does the
walk. A **lean, SRM-only** helper library is just a thin convenience layer over
that; it does **not** break the idiom or the layers' independence, *provided*
it stays SRM-only (no rendering, no `ApiSurfaceExtractor`, no I/O).

The discipline that keeps it sound:

- **Share mechanical primitives that return neutral types** — strings, handles,
  typed argument values. These have one correct form regardless of consumer.
- **Do not unify the output models.** The three `TypeRef`s exist for different
  reasons (display string, evidence matching, codegen IR). Each keeps its own
  `ISignatureTypeProvider<T>`; they may be *built on* the shared name-resolution
  helpers but are never collapsed into one type.
- **Rendering stays in the consumer.** Turning typed attribute data into C#
  (`[Flags]`, `(uint)x`) is a decompiler concern and stays in
  `ILInspector.Metadata` / the decompiler.

## Target layering

A new `ILInspector.MetadataPrimitives` assembly sits **below** `Metadata`,
referencing only SRM (BCL):

```text
                ┌─────────────────────────────┐
                │  ILInspector.MetadataPrimitives  │  (SRM only)
                │  GenericContext                  │
                │  TypeResolver, SignatureDecoder  │  (← cluster, migrates together)
                │  AttributeDecoder (decode core)  │
                │  method-handle resolution        │
                └─────────────────────────────┘
                   ▲            ▲            ▲
       ┌───────────┘            │            └───────────┐
ILInspector.Metadata   ILInspector.Analysis   ILInspector.Decompiler (Pipeline)
(rendering, ApiSurface, (evidence indexing,    (IR import, C# printing —
 SourceLink — on top)   own TypeRef on top)     own IR TypeRef on top)
```

No layer gains a dependency on another layer; each gains only the lean,
SRM-only primitives. The Decompiler `Pipeline` stays "SRM-only" because the
helper is SRM-only. `Analysis` stays independent of `Metadata`.

### What moves

- `GenericContext` (the leaf — depends on nothing internal)
- `TypeResolver` + `SignatureDecoder` (**mutually recursive** — `TypeResolver`
  decodes type specs through `SignatureDecoder`, which resolves names through
  `TypeResolver`; they move as one unit)
- the attribute **decode** core — the `ICustomAttributeTypeProvider`,
  `DecodeValue` wrapper, attribute-type-name resolution, and the
  re-emitted-attribute noise filter
- method-handle resolution by name + overload

### What stays

- attribute **rendering** to C# (`RenderAttributes` and the `[...]` formatting)
- `ApiSurfaceExtractor`, `SourceLink`, `PdbContext`, and the rest of the
  metadata *product* surface
- each consumer's own `TypeRef`/IR model and its `ISignatureTypeProvider<T>`

## The key finding: it's an entangled cluster, used widely

The shareable primitives are not independent functions — `TypeResolver` and
`SignatureDecoder` are mutually recursive and both need `GenericContext`, with
the attribute decode layered on top of name resolution. And they are consumed
across **~19 files in 3 projects** (Metadata ×13, Decompiler ×6, the CLI).

So this is a **staged migration**, not a single move — which is exactly why it
warrants a design note before code moves.

## Migration sequence (leaf-first, each step behind green tests)

1. **Stand up the library and move the leaf, `GenericContext`.** *(this change)*
   To keep the first step churn-free it retains the `ILInspector.Metadata`
   namespace, so existing `using ILInspector.Metadata;` resolves it transitively
   and only `Metadata` adds the project reference. See **Namespace** below.
2. **Move `TypeResolver` + `SignatureDecoder` together** (the recursive unit);
   re-point Metadata's call sites.
3. **Split the attribute primitive**: decode core → the library; rendering stays
   in `Metadata.AttributeReader`, calling the shared decoder.
4. **Adopt in `Analysis`**: reference the library and delete the hand-rolled
   name-resolution in `TypeRefDecoder`/`MemberResolver` that duplicates it,
   keeping its evidence `TypeRef` and provider.
5. **Adopt in the Decompiler `Pipeline`** likewise.

Stop after any step; each is independently valuable.

## Namespace

Step 1 keeps the moved types in the `ILInspector.Metadata` namespace so the
move is reference-only (zero `using` churn). That leaves a namespace/assembly
mismatch, acceptable as a transitional state but not the end goal. The decision
to make once `Analysis` adopts the library (step 4): either give the primitives
their own `ILInspector.MetadataPrimitives` namespace (consumers update `using`s,
and `Analysis` no longer appears to "use Metadata"), or keep a deliberate shared
`ILInspector.Metadata` family namespace across the assemblies. The former is
cleaner for the independence story; resolve it then, not now.

## Non-goals

- A unified `TypeRef`. The models stay per-consumer.
- Pulling rendering or I/O into the primitives library.
- Pre-emptive breadth: primitives earn their place from demonstrated
  duplication (the rule of three), not speculation.

## Decision (2026-06): stop after step 3

**Steps 1–3 are complete (#578/#579/#580) and stand. Do not do step 4 (Analysis
adopts) or step 5 (Pipeline adopts) now.** Keep `Analysis` at zero project
references; keep the `ILInspector.Metadata` namespace on the moved primitives.

The migration sequence above was written without a real second consumer of
`Analysis`, so step 4's payoff was a hypothesis. The memory-safety unsafe-mode
detector (`ILInspector.Analysis.App`, `CallerUnsafeMode` in `LibraryBodyIndex`)
is now that consumer, and it reads metadata three ways — attribute-by-name,
signature decode, module-attribute scan — exercising exactly the primitives this
note is about. It turns the hypothesis into evidence:

- **TypeRef unification is decisively wrong.** The detector's pointer-signature
  check needs `TypeRefKind.Pointer` — *semantic* structure. `Metadata.TypeResolver`
  produces display **strings** and cannot answer "is there a pointer in this
  signature." `Analysis`'s own `TypeRefDecoder → TypeRef` is what makes the check
  possible. A shared model would have forced `Analysis` to keep its own anyway.
- **`Analysis`'s independence is load-bearing.** The whole detector shipped
  SRM-direct with no dependency negotiation. That is the property step 4 spends.
- **The real duplication is tiny and stable.** `Analysis` hand-rolled
  `AttributeTypeName` (ctor → declaring-type namespace+name, `MemberReference`
  vs `MethodDefinition`) — the same SRM walk as
  `AttributeDecoder.GetAttributeTypeName`, differing only in return shape
  (`(ns, name)` vs full-name `string?`). It needs **only the name**, not
  `TryDecode`/`CustomAttributeValue` argument decode. The shareable slice is
  ~15 lines of mechanical SRM that does not churn.

The trade-off, now concrete: sharing buys deleting ~15 stable lines; it costs
`Analysis` its first project reference and the zero-dependency independence that
just paid off. Tolerate the duplication.

**Trip-wire (the only condition to revisit):** if the Decompiler `Pipeline` also
needs attribute-name reads, that is rule-of-three across projects — at that point
share `GetAttributeTypeName` *only* (the name walk, never `TryDecode`, never a
`TypeRef`). Until that third sighting, no action.

Underlying principle: let real consumers *pull* primitives into the shared
library; do not *push* them in speculatively. The first real pull pulled almost
nothing — which is the answer.
