# Fast Diff cost-floor probe

A disposable probe, not product code. For one exact Library pair, it answers
"did this Type change?" for every Type and stops at the first difference. It
exists to measure the cost floor of a body-aware Library pass for Fast Diff
(#9716, #9717).

## Mechanism

- Each side is read once with System.Reflection.Metadata. Each metadata token
  is resolved to a symbolic key once per side and then memoized.
- Compiler-generated nested types (`<>c`, `<M>d__N`, display classes) are
  folded into their declaring Type. Type scope therefore needs no per-Member
  owner attribution.
- Pass 1 runs a metadata census per Type: attributes, base type, interfaces,
  fields, methods, parameters, properties, events, and custom-attribute blobs.
  Any difference settles the Type `Changed`. With `POSITIONAL=1`, members are
  compared in table order without allocation, and any misalignment counts as
  `Changed`.
- Pass 2 applies only to Types still unchanged. It compares IL in lockstep
  with symbolic token operands, user-string values, exception regions, locals,
  `init locals`, and `.maxstack`, and stops at the first difference. A
  differing body size counts as a conservative `Changed`.

The census is raw-metadata equality, not Metadata-owned Finding semantics, so
it may over-report, for example a private attribute change. It still needs a
corpus soundness gate against the complete diff. Library-wide facts, such as
assembly-level attributes and extension members declared on other Types, are
not handled.

## Run

```sh
dotnet build -c Release
POSITIONAL=1 dotnet bin/Release/net11.0/bench.dll <old.dll> <new.dll> [iterations]
```

## Results (JIT, both sides, including PE open)

| Pair | Types | Changed | Census | With bodies |
| --- | ---: | ---: | ---: | ---: |
| Aspire.Hosting 13.6.0 to 13.6.1 | 1,182 | 2 | 47 ms | 61 to 82 ms |
| System.Text.Json 9.0.0 to 10.0.0 | 303 | 122 | 5 ms | 5 to 6 ms |

Aspire used the `net8.0` assets and System.Text.Json the `net9.0` assets.

Aspire scanned 862 KB of IL across 13,523 bodies. It found one body-only Type
(`PeriodicRestartAsyncEnumerable`) and one metadata Type
(`KubernetesService`). These match the two changed Types found by the
owner-attributed digest prototype, which cost about 840 ms per side in
NativeAOT.
