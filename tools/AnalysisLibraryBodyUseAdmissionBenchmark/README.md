# Analysis Library body-use admission benchmark

This development-only NativeAOT harness reproduces the whole-product
`MalformedBody` admission experiment recorded in
[`analysis-library-body-use-admission-2026-09.md`](../../docs/evidence/analysis-library-body-use-admission-2026-09.md).
It compares the two immutable commits named there, including the complete
public result and receipt. It is intentionally outside `dotnet-inspect.slnx`:
normal CI does not build or run it.

Use the repository's general
[`BodyUseScorecard`](../BodyUseScorecard/Program.cs) for new
Direct/LINQ/NLinq/Planner body-use terminal work. This harness predates that
scorecard and remains only so the exact admission-policy experiment is
reproducible; do not extend it into a second general scorecard.

The default assets are pinned package/runtime assemblies already used by the
Analysis test infrastructure:

- System.Text.Json 10.0.0;
- MessagePack 2.5.192; and
- Jurassic 3.2.9.

Publish and run one immutable commit:

```bash
dotnet publish tools/AnalysisLibraryBodyUseAdmissionBenchmark \
  -c Release -r linux-x64 -o artifacts/body-use-admission
artifacts/body-use-admission/analysis-library-body-use-admission-benchmark \
  --rounds 15
```

The harness warms each asset, measures five operations per sample, verifies
that every repetition has an identical full-result SHA-256 fingerprint, and
reports median elapsed time, p95 elapsed time, median CPU time, and median
allocated bytes. Compare the fingerprints and result columns before comparing
timings from two commits built with the same SDK and command.
