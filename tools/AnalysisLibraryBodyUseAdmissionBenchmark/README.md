# Analysis Library body-use admission benchmark

This development-only NativeAOT harness compares immutable commits of the
Analysis Library body-use operation. It is intentionally outside
`dotnet-inspect.slnx`: normal CI does not build or run it.

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

The harness warms each asset, verifies that every repetition has an identical
full-result SHA-256 fingerprint, and reports median elapsed time, p95 elapsed
time, and median allocated bytes. Compare the fingerprints and result columns
before comparing timings from two commits built with the same SDK and command.
