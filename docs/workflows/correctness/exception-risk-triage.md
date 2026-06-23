---
id: exception-risk-triage
description: Static artifact triage for exception origins and leveraged propagation paths
commands: [library, member]
areas: [correctness, exceptions, calls, decompiler, source, il]
---

# Correctness: Exception Risk Triage

> Rank exception paths from shipped artifacts. This workflow produces
> evidence-backed risk hypotheses, not a proof that an exception is unhandled.

The goal is to find exception paths worth human correctness review by combining
leverage and exception evidence:

- **Leverage**: many inbound callers, public entry points, a deep outbound call
  graph, exception-bearing calls inside loops, or a combination of these.
- **Exception evidence**: exception constructors, `throw` IL, catch/finally
  shape in decompiled source or IL, cancellation-as-control-flow, and call graph
  paths from public entry points to throw/cancel sites.

If the workflow cannot prove the exception kind or propagation risk because
dotnet-inspect lacks a needed view (for example projected exception annotations
inside `Call Graph`), file a product feature issue instead of forcing a weak
correctness finding.

Always report the artifact boundary: package or platform library, version, TFM,
assembly, and any caller corpus scanned with `--bin`, `--project`, or
`--caller-package`. Without a caller corpus, inbound arcs are limited to the
selected assembly.

## Preconditions

For this repository's product binary examples, build the local product first:

```bash
dotnet build src/dotnet-inspect -c Release -v:q
```

## 1. Establish the artifact boundary

> Goal: name the exact artifact before interpreting exception risk.

```prompt
What artifact am I inspecting for dotnet-inspect exception-risk triage?
```

```bash
dotnet-inspect library artifacts/bin/dotnet-inspect/release/dotnet-inspect.dll -v:q
```

```expect
dotnet-inspect.dll
Source: File
```

```query
grep -E 'dotnet-inspect.dll|Source: File'
```

## 2. Find a candidate exception-origin method

> Goal: start from a method family and get a stable selector for exception
> evidence.

```prompt
Which SharedOptions method validates renderer flags?
```

```bash
dotnet-inspect member SharedOptions \
  --library artifacts/bin/dotnet-inspect/release/dotnet-inspect.dll \
  --all -m ValidateRendererFlags \
  -S "Member Index" --columns "Selector;Canonical Signature" --tsv
```

```expect
ValidateRendererFlags
M:DotnetInspector.Services.SharedOptions.ValidateRendererFlags
```

```query
grep 'ValidateRendererFlags'
```

## 3. Capture exception-kind evidence

> Goal: cite exact call evidence for the exception type, then use IL when the
> selected method can render it.

```prompt
What exception type can SharedOptions.ValidateRendererFlags construct?
```

```bash
dotnet-inspect member SharedOptions \
  --library artifacts/bin/dotnet-inspect/release/dotnet-inspect.dll \
  --all -m ValidateRendererFlags --index 1 \
  -S "Calls,Call Graph" --rows -n 80
```

```expect
OperationCanceledException..ctor()
newobj
TextWriter.WriteLine
```

```query
grep -E 'OperationCanceledException|newobj|TextWriter.WriteLine'
```

Interpretation: `OperationCanceledException` may be intentional control flow
rather than a bug. The correctness question is whether callers expect and handle
that cancellation path, and whether the user-facing error is clear.

When IL is available, confirm the actual throw opcode:

```bash
dotnet-inspect member SectionPipeline \
  --library artifacts/bin/dotnet-inspect/release/dotnet-inspect.dll \
  --all -m Add --index 1 \
  -S "Calls,IL" --rows -n 120
```

```expect
InvalidOperationException..ctor
newobj
throw
```

## 4. Measure propagation leverage

> Goal: identify whether a throw/cancel site is reached from high-value command
> paths.

```prompt
Which product paths reach SharedOptions.ResolveFormat and its validation path?
```

```bash
dotnet-inspect member SharedOptions \
  --library artifacts/bin/dotnet-inspect/release/dotnet-inspect.dll \
  --all -m ResolveFormat --index 1 \
  -S "Callers,Call Graph" \
  --bin artifacts/bin/dotnet-inspect/release --rows -n 120
```

```expect
## Callers
CreateLibraryCommand
CreateTypeCommand
CreateMemberCommand
ValidateRendererFlags
fanin
depth
```

```query
grep -E 'Create(Library|Type|Member)Command|ValidateRendererFlags|fanin|depth'
```

Interpretation: leverage makes an exception path worth reviewing. A throw site
with no meaningful callers is usually a local invariant; a throw/cancel path
reachable from many public commands deserves stronger user-facing behavior and
tests.

## 5. Inspect the local throw site in context

> Goal: decide whether the exception path is an invariant, an expected user
> error, or a risky unhandled path. Prefer decompiled source when available; when
> source is partial, keep the claim grounded in `Calls`, `Call Graph`, and
> whatever IL evidence is available.

```prompt
Show the call graph and IL for SharedOptions.ValidateRendererFlags.
```

```bash
dotnet-inspect member SharedOptions \
  --library artifacts/bin/dotnet-inspect/release/dotnet-inspect.dll \
  --all -m ValidateRendererFlags --index 1 \
  -S "Calls,Call Graph" --rows -n 140
```

```expect
ValidateRendererFlags
OperationCanceledException
TextWriter.WriteLine
## Call Graph
```

```query
grep -E 'OperationCanceledException|TextWriter.WriteLine|Call Graph'
```

Interpretation: a method that writes to `Console.Error` and throws
`OperationCanceledException` may be a deliberate CLI parse failure. A useful
finding should ask whether every command path converts that exception into the
intended exit code and avoids stack traces.

## Report template

Use this compact shape for handoffs and PR-review notes:

| Field | Required content |
| --- | --- |
| Artifact boundary | package/platform/library, version, TFM, assembly, caller corpus |
| Leverage | caller count, public entry points, deep call graph, loop-heavy calls |
| Exception evidence | exception type, constructor IL, `throw` IL, catch/finally/source context |
| Propagation risk | where the exception can reach, and whether callers appear to handle it |
| Confidence | high/medium/low, based on reachability and evidence quality |
| Falsifier | caller catches/normalizes it, path is impossible, or error is tested |
| Next proof | targeted CLI scenario, unit test, source audit, or graph annotation feature |

Do not report every `throw` as a bug. The strongest correctness finding is:
artifact queries show a specific exception path, a meaningful propagation route,
and missing or unclear handling evidence.
