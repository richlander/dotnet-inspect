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

Library triage is split into kind-scoped sections under `@Performance`
(`Performance: Boxing`, `Performance: Arrays`, `Performance: Closures and
delegates`, and more). Structural discovery lists the authored kinds without
running analysis; add `--effective` to retain only kinds with findings for this
library. A count executes the selected group and includes zero-row kinds.
Type/member scope keeps the focused `Performance Triage` lens.

```bash
dnx dotnet-inspect -y -- library MyLib.dll -D @Performance
dnx dotnet-inspect -y -- library MyLib.dll -D @Performance --effective
dnx dotnet-inspect -y -- library MyLib.dll -S @Performance --count
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance: Boxing" --jsonl
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance:*" \
  --where "Priority>=high" --top 20 --tsv
dnx dotnet-inspect -y -- library MyLib.dll \
  --triage-shape scan-method-in-loop-call,scan-method-in-recursive-traversal,linq-scan-in-loop,string-build-in-loop \
  --top 20 --tsv
dnx dotnet-inspect -y -- library MyLib.dll --triage-shape capturing-delegate --top 10 --jsonl
```

Target IL-visible costs (allocations: box, newarr, delegate newobj,
ToArray/ToList/Concat), not JIT-handled concerns (isinst/castclass folding,
devirtualization, bounds-check elimination, null-check folding).

Use `--where "Priority>=high"` for the signal-dense first pass, `--loop` for
repeated costs, `--min-confidence high|medium|low` for an evidence/rewrite
confidence floor, `--triage-shape` for one or more shapes, and `--top N` for
the curated ranked prefix. Supplying any of those flags selects the applicable
performance lens automatically. In library row formats, `Performance:*`
flattens two or more populated kind sections into one table with a leading
`Kind` column. If filtering leaves one populated kind, row formats use that
kind's concrete schema without `Kind`; use structured `--json` when the kind
discriminator must remain explicit. `@Performance` also includes heterogeneous
sections, so use it for discovery, counts, Markdown, or JSON documents instead.
`--top` narrows ranked data before rendering; `--rows N` caps rendered rows
afterward. Common shapes
include `capturing-delegate`, `box-value-type`,
`generic-parameter-object-box` (an unconstrained generic value boxed for
`object.Equals`; it allocates only for value-type instantiations and starts at
medium priority unless loop evidence proves repetition), `small-array`,
`cache-lookup-factory-delegate` (a per-call instance factory passed to
`ConcurrentDictionary.GetOrAdd`), `linq-scan-in-loop`,
`scan-method-in-loop-call` (a linear-scan helper invoked from a caller loop),
`scan-method-in-recursive-traversal` (a scan repeated once per recursive
traversal node), `materialize-in-loop` (a loop-invariant `ToArray`/`ToList`
that can be hoisted), `string-build-in-loop`, `enumerator-allocation`,
`async-state-machine`, `sync-call-in-async` (an async method calling a
synchronous API with a signature-compatible `Async` sibling), and
`allocation-hotspot`. Query the algorithmic shapes explicitly:
scan helpers stay low-confidence because static analysis cannot
prove that the scanned sequence grows with the loop or traversal, so a
`--min-confidence high` pass intentionally excludes them.

The default `Triage` order keeps `Priority` separate from `Confidence`.
`Priority` is a static actionability judgment: directly evidenced algorithmic amplification,
avoidable cache-lookup factory allocations, and actionable high allocation
weight rank high; recursive scan helpers without shared-source identity and
other generic repeated costs rank medium; ordinary one-shot
candidates rank low. Escape-unknown `small-array` rows remain medium even at
high weight because no safe stack rewrite is proven. `Confidence` describes
certainty in the evidence and proposed rewrite, so a high-priority,
low-confidence row is intentionally an early investigation target rather than
a claimed runtime win. Flattened `Performance:*` row output preserves this
global order across kinds.

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

Exact rows retain machine-readable provenance from the native Analysis
producer in structured JSON:
`Candidate`, `Finding` (`analysis.allocation` or `analysis.call-site`),
`Provenance=exact`,
`Assembly`, `ModuleVersionId`, `MethodToken`, `Operation`, `Token`,
`EvidenceMethod`, and `IL`.
`MethodToken` identifies the source-facing member, while `EvidenceMethod`
is present when the instruction is mapped to a separate MethodDef; for an async
source member, it can name the generated `MoveNext` body whose offset appears in
`IL`. The exact body coordinate is `Assembly` + (`EvidenceMethod` when present,
otherwise `MethodToken`) + `IL`; `ModuleVersionId` distinguishes physical
module builds when static inputs carry it, and `Token` is the operand of
`Operation`. Use these fields for runtime/static joins or to carry one triage
row into the matching `diff`/`timeline` confirmation workflow
without parsing `Evidence` text:

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance:*" \
  --where "Finding=analysis.allocation" --where "Operation=box" --json
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance:*" \
  --where "Finding=analysis.call-site" --json
```

Aggregate rows such as `allocation-hotspot` use `Provenance=aggregate` and have
a `pt~` candidate id but no exact source Finding, operation, or token.
`Provenance=unmatched` flags an instruction-level row that did not join to the
expected producer census.

## Correlate triage with an allocation trace

Export nested JSON, whose deep rows carry the declaring method coordinate, then
pass it to `runfaster` with a trace captured from the same assembly build:

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance:*" \
  --where "Priority>=high" --json > triage.json
runfaster correlate --triage triage.json --trace workload.nettrace
```

Compact `Performance:* --jsonl` rows omit deep provenance and cannot support an
exact trace join. `runfaster` keeps their operation `Token` separate from the
source-facing `MethodToken`, uses `EvidenceMethod` as the physical body token
when supplied, and reports missing runtime coordinates explicitly. Blank
flattened cells are treated as absent; invalid non-empty or conflicting
supplied evidence-method tokens fail visibly.
For a filtered export, the trace join stops at the first frame in the
represented assembly; it does not walk past an unexported in-assembly callee
and credit an outer caller. If `--library` and `--triage` name the same physical
candidate, the shape-compatible triage row carries the runtime evidence.
The raw library row is marked `superseded-by-triage`, not workload-cold.
Type-level ambiguity and its site cap count the shared coordinate once unless
several library MVIDs make an older MVID-less triage row's module version
ambiguous.
Triage and library inputs from different builds retain distinct MVIDs and can
therefore increase ambiguity or exceed the type-confirmation site cap.

## Select direct caller-loop repetition

A once-per-call allocation can still be repeated by an upstream caller's loop.
Select rows with an exact direct invocation receipt:

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance:*" \
  --where "CallerLoop=direct" --json
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

## Triage exception-path pool churn

Select the explicit `Resource Triage` library section to find `ArrayPool<T>`
acquisitions whose exact def-use path reaches an external-input boundary before
modeled cleanup:

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Resource Triage" --jsonl
```

Treat `pool-churn-on-exception` as a profiling and hardening candidate, not a
permanent-memory-leak or memory-corruption accusation. Static analysis proves
the unprotected boundary shape and API evidence, not runtime frequency. Use
`Candidate`, `Finding=analysis.resource-lifecycle`, `Acquire IL`, `Boundary IL`,
and `Boundary` to retain exact provenance while drilling the method. Each
boundary is one row; a multi-boundary candidate repeats its candidate and
acquisition fields so every operation stays paired with its own IL offset.

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

`Call Graph` is a bounded bidirectional graph: inbound callers up to entry
points and outbound calls, centred on the selected member. Project per-node cost
with `--fields` (alloc, copy, unsafe, reflection, throw/exception,
catch/finally). Its default Markdown edge table is best for comparing
relationships and cost cues. Use `--tree` when the path toward or away from the
candidate matters, `--mermaid` for a standalone diagram, or
`--markdown --mermaid` to embed the diagram. Use `--tsv` or `--jsonl` when a
script will consume the same edge rows. Requested cost cues remain annotations
in the node labels; they do not become separate machine columns.

```bash
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll -S "Call Graph,Facts"
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll -S "Call Graph" --fields "Throw,Catch,Finally"
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll -S "Call Graph" --fields "Alloc,Loop" --tree
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll -S "Call Graph" --jsonl
```
