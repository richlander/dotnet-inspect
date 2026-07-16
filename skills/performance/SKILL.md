---
name: dotnet-inspect-performance
version: 0.1.0
description: Whole-assembly call-graph leverage ranking and performance triage for libraries (experimental).
---

# dotnet-inspect: performance analysis and triage

Use this skill to find the members worth optimizing or hardening first in a .NET
assembly, and to triage them against actionable rewrite shapes. This analysis is
experimental; section names and signal sets may change between releases.

```bash
dnx dotnet-inspect -y -- <command>
```

## Rank by leverage first

`Top Leverage` ranks members by call-graph leverage: direct callers, `Root
Reach` (distinct entry points that transitively reach a member), fanout, depth,
and loop calls. Start here on a whole library, then narrow to a type.

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Top Leverage"
dnx dotnet-inspect -y -- type MyType --library MyLib.dll --all -S "Top Leverage"
```

Ranking rows carry a copyable `Stable` selector, `Visibility`, and `Selector`.
Add `--all` to include non-public members.

## Triage against rewrite shapes

`Performance Triage` re-ranks the same leverage against actionable rewrite
shapes — small non-escaping arrays, temporary or span-to-array copies, capturing
delegates, stateless instance methods — so hot, fixable members surface first.

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance Triage"
dnx dotnet-inspect -y -- library MyLib.dll --loop --min-confidence high --top 20 --tsv
dnx dotnet-inspect -y -- library MyLib.dll \
  --triage-shape scan-method-in-loop-call,linq-scan-in-loop,string-build-in-loop \
  --top 20 --tsv
dnx dotnet-inspect -y -- library MyLib.dll --triage-shape capturing-delegate --top 10 --jsonl
```

Target IL-visible costs (allocations: box, newarr, delegate newobj,
ToArray/ToList/Concat), not JIT-handled concerns (isinst/castclass folding,
devirtualization, bounds-check elimination, null-check folding).

Use `--loop` for repeated hot costs, `--min-confidence high|medium|low` for a
confidence floor, `--triage-shape` for one or more shapes, and `--top N` for the
curated ranked prefix. Supplying any of those flags selects `Performance Triage`
automatically on `library`, `type`, and `member`. `--top` narrows the ranked data
before rendering; `-n N --rows` is a generic rendered-row cap applied afterward.
Common shapes include `capturing-delegate`, `box-value-type`, `small-array`,
`linq-scan-in-loop`, `scan-method-in-loop-call` (a linear-scan helper invoked
from a caller loop), `materialize-in-loop` (a loop-invariant `ToArray`/`ToList`
that can be hoisted), `string-build-in-loop`, `enumerator-allocation`,
`async-state-machine`, and `allocation-hotspot`. Query the algorithmic shapes
explicitly: `scan-method-in-loop-call` stays low-confidence because static
analysis cannot prove that the scanned sequence grows with the caller's loop,
so a `--min-confidence high` pass intentionally excludes it.

For registry, pipeline, or object-graph construction that does not match a local
rewrite shape, opt into the aggregate allocation fanout:

```bash
dnx dotnet-inspect -y -- library MyLib.dll \
  --triage-shape allocation-fanout \
  --order-by "OncePaths desc" --top 20 --tsv
```

`Direct Sites` is local to the method. `Once Paths` composes exact
intra-assembly callsites and counts repeated callsites separately; conditional,
repeated, unknown, cached, and opaque paths remain separate columns. Treat this
as IL-visible normal-return-path quantity, not runtime bytes or observed
frequency. A high `Opaque Paths` count means virtual, external, delegate,
recursive, or runtime-library work still needs a drill or profiler.

Exact rows retain machine-readable provenance from the native Analysis producer:
`Candidate`, `Finding` (`analysis.allocation` or `analysis.call-site`),
`Provenance=exact`,
`Operation`, `Token`, and `IL`. Use these fields for runtime/static joins or to
carry one triage row into the matching `diff`/`timeline` confirmation workflow
without parsing `Evidence` text:

```bash
dnx dotnet-inspect -y -- library MyLib.dll \
  --where "Finding=analysis.allocation" --where "Operation=box" --jsonl
dnx dotnet-inspect -y -- library MyLib.dll \
  --where "Finding=analysis.call-site" --jsonl
```

Aggregate rows such as `allocation-hotspot` use `Provenance=aggregate` and have
a `pt~` candidate id but no exact source Finding, operation, or token.
`Provenance=unmatched` flags an instruction-level row that did not join to the
expected producer census.

## Select direct caller-loop repetition

A once-per-call allocation can still be repeated by an upstream caller's loop.
Select rows with an exact direct invocation receipt:

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance Triage" \
  --where "CallerLoop=direct" --jsonl
```

`CallerLoopDepth` and `CallerLoopWitness` identify the deterministic invocation
site. This evidence does not change the row's local `Loop`, multiplicity,
confidence, weight, candidate identity, or default rank. Use it to select a
candidate for profiling, not as proof that the caller is hot or that the loop
executes.

Only resolved invocation edges qualify. Function loads and callback
registration do not prove callback execution, and recursive traversal does not
prove realized depth or frequency. Do not infer either case into caller-loop
evidence; require runtime evidence or a stronger product-owned invocation
contract.

Not every shape is a pure hot-path win. `async-state-machine` is reported as
amortized (low confidence) unless the allocation sits in a loop: async lowering
moves work into a state object rather than eliminating it, often once per
call/enumeration/subscription. Treat amortized rows as context, and confirm a
real per-item cost with a profiler before optimizing.

## Confirm when an allocation appeared

Correlate one method's native allocation census across caller-selected package
cells:

```bash
dnx dotnet-inspect -y -- timeline --package MyLib@1.0.0..2.0.0 \
  -t MyType -m HotPath \
  --finding analysis.allocation --at first --at last
```

Repeat `--at` for sparse probes or use `--at all` for an explicitly bounded
dense traversal. These probes locate a candidate old/new boundary; they do not
establish onset. Confirm one method's adjacent pair with Analysis's native
allocation Findings:

```bash
dnx dotnet-inspect -y -- diff --package MyLib@1.4.0..1.5.0 \
  -t MyType -m HotPath \
  --finding analysis.allocation
```

The method target must resolve at one or both endpoints. `PairFinding.Added`
confirms an allocation occurrence was introduced, while `Present`, `Removed`,
and `Changed` identify a wrong boundary, disappearance, or changed allocation
facets. The command does not traverse versions; the caller owns the search
policy and bound.

## Trace a likely cause to a new call

After confirming an allocation boundary, compare the same caller method's
direct-call census:

```bash
dnx dotnet-inspect -y -- diff --package MyLib@1.4.0..1.5.0 \
  -t MyType -m HotPath \
  --finding analysis.call-site
```

The target method is the caller and each row identifies a callee.
`PairFinding.Added` confirms a new call occurrence, such as a newly introduced
`Enumerable.ToArray`. `Changed` can show that an existing call moved into a loop
or changed dispatch/opcode facets. Use the single-version `Calls` section while
probing versions; use this final adjacent comparison as the onset proof.

## Drill a candidate

`Call Graph` is a bounded outbound tree; `Caller Graph` is a bounded reverse
tree to entry points. Project per-node cost with `--fields` (alloc, copy,
unsafe, reflection, throw/exception, catch/finally).

```bash
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll -S "Call Graph,Facts"
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll -S "Caller Graph" --fields "Throw,Catch,Finally"
```
