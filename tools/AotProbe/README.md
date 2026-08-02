# AotProbe

Measures how two Native AOT codegen settings affect the CLI's text-handling hot paths:

- `IlcInstructionSet` — the instruction-set baseline the binary is compiled against.
- `OptimizationPreference` — `Size` (what `src/dotnet-inspect/dotnet-inspect.csproj`
  sets today) or `Speed`.

`dotnet-inspect` is distributed primarily as Native AOT, so these are questions about
the shipped binary. A JIT benchmark cannot answer them: the JIT selects vector widths
at runtime from the actual CPU, whereas Native AOT bakes them in at compile time.

## Running

```bash
tools/AotProbe/run.sh
```

The script picks the RID and the baseline list from the host architecture, publishes
every combination, and runs each. It takes several minutes, mostly in `dotnet publish`.
Close other work first — figures are best-of-9, but background load still perturbs them.

Clean up with `rm -rf tools/AotProbe/out-*`.

## Reading the output

```text
# x86-64-v3-Speed NAOT v128=1 v256=1 v512=0
x86-64-v3-Speed typename      64 scan=    130.3 pre=    2.41 x=    54
```

`scan` is `InertString.IsPermitted`, a scalar per-scalar walk — representative of the
CLI's text handling generally. `pre` is a printable-ASCII prefilter that falls back to
the scan on a miss, representing a BCL vectorized helper. Both are nanoseconds per
operation. The `v128`/`v256`/`v512` flags report which vector widths the compiled
binary can actually use, which is the fact the baseline controls.

The `nonAscii` row is the case the prefilter loses on: text that is permitted but not
ASCII, so the prefilter misses and its cost lands on top of the scan.

## Why the minimum rather than the mean

The quantity of interest is the cost of the work itself, and every source of noise on a
shared machine only ever adds. An early sweep on the AM5 host had two of six runs
inflated roughly twofold by unrelated load — visible because the *instruction-set
independent* column moved too. Minima across nine repetitions removed that artefact.

## Interpreting a result

The two settings move different code, so a result is two independent readings:

- `OptimizationPreference` moves `scan` and barely touches `pre`.
- The instruction-set baseline moves `pre` and does not touch `scan` at all — a scalar
  loop cannot spend wider vector registers.

A baseline that raises no vector width above the default therefore has nothing to offer
here, however modern the hardware. That is the expected shape of the arm64 result,
because AdvSimd/NEON is mandatory in ARMv8-A and .NET exposes no wider vector on arm64.
