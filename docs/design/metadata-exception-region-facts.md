# Metadata exception-region facts

Metadata exception-region facts own the physical exception-handling clauses
declared by one managed method body. The exact claim is that a consumer can
distinguish a body with no clauses from an unavailable body and can inspect
the complete declared clause inventory without retaining a PE or metadata
reader.

This document owns the Metadata contract. The
[instruction exception-flow facts](instruction-exception-flow-facts.md) consume
this evidence but do not redefine it.

## Status and decision

This is a target design tracked by
[#6965](https://github.com/richlander/dotnet-inspect/issues/6965). Product
types, closed results, and gates are **unverified on `main`**.

The owner is `ILInspector.Metadata`, alongside Metadata's
`StateMachineRelationshipResult` and `MemorySafetyRulesResult`. Those APIs
issue reusable physical facts once so Analysis, Decompiler, and presentation
consumers do not reinterpret the same rows independently. Exception regions
need the same shared handoff at method-body scope.

The detached handoff values can remain in
`ILInspector.MetadataPrimitives`, where `MethodBodyData` already carries copied
IL and SRM `ExceptionRegion` values. Type placement at that dependency leaf
does not transfer semantic ownership: Metadata issues the body evidence and
defines what its result means.

## Current basis and replacement

Metadata already exposes three partial forms:

- `MethodBodySource.TryRead` copies IL and `ExceptionRegion` values into
  `MethodBodyData`, but returns failure through `bool` plus an error string.
- `PdbContext.ResolveExceptionRegions` projects clause ranges, kinds, and catch
  types into presentation-oriented rows.
- `PdbContext.ResolveExceptionContext` finds the clauses containing one IL
  offset from raw ranges.

The target contract consolidates their physical evidence. Existing callers can
adapt to it incrementally, but completed adoption retires independent clause
numbering, range arithmetic, and success-shaped empty failure from those
paths.

## Body evidence currency

One successful whole-body read issues a **method-body evidence identity** and a
detached immutable body:

- the exact copied IL bytes;
- the complete exception-clause catalog from the same body block; and
- the body evidence identity shared by both.

The identity is scoped to the read body, not merely to a MethodDef token or
display name. `MetadataMethodAddress` identifies the physical MethodDef; the
body evidence identity proves that the IL bytes and clause catalog came from
one admitted body observation. Consumers must not combine clauses from one
read with bytes from another and call the result corresponding evidence.

The result remains usable after the Metadata session closes. It contains no
reader-backed handles that require later dereference. A catch type is carried
as detached resolved identity or explicit unavailable evidence; a raw token is
not presented as a resolved type.

## Clause catalog

Each clause receives an owner-issued **exception clause identity** composed
with the method-body evidence identity. The catalog preserves:

- metadata order;
- catch, filter, `finally`, and `fault` kind;
- exact half-open try and handler extents;
- the exact half-open filter extent when present; and
- resolved catch-type identity or typed unavailability when catch-type
  resolution was requested.

The catalog does not silently discard a malformed clause. Construction either
publishes the complete catalog or reports why the body or catalog is
unavailable.

Two clauses can declare the same try extent. Metadata preserves both clause
identities and their order; it does not collapse them into one semantic
protected region. Grouping, nesting validation, and execution semantics belong
to Instructions.

## Direct Metadata questions

The catalog directly answers physical questions that do not require
instruction decoding:

- whether a readable method body declares any exception clauses;
- total and per-kind clause counts;
- the exact declared ranges and catch-type evidence;
- which clause extents contain a numeric IL offset; and
- whether clauses share an identical declared try extent.

`HasExceptionRegions` is derived from an available catalog's count. It is not a
standalone boolean: `Available([])`, `NoBody`, and `Unavailable` remain
different outcomes.

Offset containment is physical range evidence. It does not claim the offset is
an instruction boundary, reachable, or in a valid exception-flow topology.
Consumers needing those claims use Instructions.

These direct questions are useful to Analysis for method signals, candidate
selection, and inventory; to Decompiler for import admission and catch-type
evidence; and to Metadata presentation for the existing **Exception Regions**
section.

## Closed result

One request returns exactly one result:

| Result | Meaning |
| --- | --- |
| `Available` | The copied body and complete clause catalog were materialized from one admitted body observation. The catalog may be empty. |
| `NoBody` | The MethodDef definitively declares no managed IL body. |
| `Unavailable` | The token, implementation kind, body header, clause table, requested catch-type resolution, or admitted work bound prevented a complete answer. |

`Unavailable` carries a typed reason and the method identity available at the
failure point. Malformed body data, a non-IL implementation, an invalid token,
and a budget refusal remain distinct. No result returns shortened IL or a
partial clause catalog as `Available`.

## Consumer boundary

Metadata owns body and clause evidence, not its operational interpretation:

- Analysis can count, filter, and locate declared clauses directly.
- Instructions validates the evidence against decoded IL and derives flow.
- Decompiler can retain clause and catch-type identities through import.
- CLI and browser presentation render owner-issued facts rather than rereading
  PE state.

Consumers own their own recommendations, raises, diagnostics, and display
selection. A catch clause is not evidence that a runtime exception is caught;
a `finally` clause is not by itself an execution-path result.

## Evidence plan

The contract remains unverified until a Metadata implementation slice supplies
Release gates for:

- no body, known-empty, available non-empty, malformed, non-IL, and
  work-refused outcomes;
- catch, filter, `finally`, and `fault` clauses with exact ranges and order;
- shared try extents without identity collapse;
- detached use after reader disposal;
- catch-type resolution and unavailable evidence; and
- correspondence between the copied IL and clause catalog from one body read.

Existing `MethodBodySource`, `PdbContext`, and **Exception Regions** behavior
provide compatibility cases, not independent oracles for the new closed
result.

## Non-claims

Metadata exception-region facts do not:

- decode instructions or validate instruction boundaries;
- construct basic blocks or control-flow edges;
- infer semantic nesting, handler execution order, or continuations;
- select a catch for a thrown value;
- model structured IR or source syntax; or
- establish Analysis or Decompiler policy.
