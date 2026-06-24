# Graph Signal Annotations

How analysis **signals** are projected onto call-graph nodes, and how the same
mechanism extends to exception-risk triage.

Related docs:

- [Output shapes](output-shapes.md) — the projection/shape model these fields plug into
- [Hidden-fact annotations](hidden-fact-annotations.md) — the per-method evidence model

## Two halves of a triage view

A useful triage graph needs both axes:

1. **Scale / leverage** — many callers, deep graph, calls in loops: the
   `fanin`/`fanout`/`depth`/`loop` cues already on `Call Graph`/`Caller Graph`.
2. **Kind of concern** — allocation, copy, unsafe, exception, reflection, I/O:
   the **signals** described here.

The high-value result is their intersection: a leveraged method that *also*
carries a costly or risky signal. Signals are projected the same way the perf
cues are — via `--fields` — so an agent requests only the fields a workflow needs
and the graph stays readable (signals never appear in the default view).

```bash
member Type Method:1 --library Product.dll -S "Call Graph" \
  --fields "Fanout,Fanin,Depth,Loop,Alloc,Copy,Unsafe"
```

```text
└─ BuildCallTree(int, int, int) (fanout 16, alloc 13)
   ├─ MethodKey(...) (copy 1, alloc 1)
   └─ <BuildCallTree>g__Build|9(...) (alloc 10)
```

## Current signals

All derive from the existing call index (`DirectCalls`) and unsafe evidence — no
extra IL scan — and are carried on `CallTreePerf` and rendered by
`FormatCallGraphAnnotation` (`ApiOutputFormatter`).

| Field | Means | Derivation |
| --- | --- | --- |
| `Alloc` / `Allocations` | object allocations in the body | count of `newobj` call edges (`CallKind.NewObject`) |
| `Copy` / `Copies` | copy/materialize calls | callees in a curated set (`ToArray`, `ToList`, `CopyTo`, `GetSubArray`, `Substring`, `Concat`, `Join`) |
| `Unsafe` | method has unsafe evidence | any `UnsafeEvidence` for the method |

A node renders a signal only when its count is non-zero, so requesting `--fields
Alloc` annotates only the allocating nodes.

## Growing the vocabulary

The model is deliberately small and grows by adding a per-method signal plus a
`FormatCallGraphAnnotation` case and a schema field. Near-term candidates:

- `newarr` array allocations (a cheap IL-scan counter, folded into `Alloc`).
- `reflection` — callees under `System.Reflection`, `Activator`,
  `System.Linq.Expressions`.
- delegate/closure allocations (display-class `newobj`, `ldftn`).

## Follow-up: exception-risk triage

The same graph-annotation mechanism supports a **correctness**-focused workflow —
exception-risk triage — by adding exception signals instead of cost signals:

| Field (proposed) | Means | Derivation |
| --- | --- | --- |
| `Throw` / `Throws` | the body throws | `throw`/`rethrow` opcodes, or `newobj` of `System.Exception`-derived types |
| `Catch` | the body handles | exception-handling clauses with a catch handler |
| `Finally` | the body has cleanup | `finally`/`fault` handler clauses |

Throw/catch/finally come from the method body's exception-handling regions
(`MethodBody.ExceptionRegions`) and a small opcode counter — the same shape as the
loop-region scan already used for the `loop` cue. With those projected, the
existing `Caller Graph` answers exception-reachability questions directly:

- *Where can exceptions originate?* — nodes with `Throw`.
- *Which public entry points reach throw-heavy paths?* — `Caller Graph` rooted at
  a throwing method, reading `root`/`entrypoint` classification.
- *Where is risk swallowed vs propagated?* — `Throw` without an enclosing `Catch`
  on the path.

This mirrors the perf-triage skill (Top Leverage → `Call Graph`/`Caller Graph`)
but reads exception signals instead of allocation/copy/loop cost. No new output
shape is required — only new signal fields on the same projection mechanism.
