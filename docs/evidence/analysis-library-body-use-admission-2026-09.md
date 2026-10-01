# Analysis Library body-use admission benchmark

## Conclusion

Removing the `MalformedBody` admission check produced byte-for-byte-equivalent
full-result fingerprints and identical result columns for System.Text.Json
10.0.0, MessagePack 2.5.192, and Jurassic 3.2.9. All three baseline executions
had zero unavailable bodies, so this comparison exercises the proposed
well-formed fast path; it does not decide the malformed-body contract.

The experiment showed no defensible elapsed-time difference on this host.
Median point estimates favored removal by 0.7% to 4.1%, but every paired
elapsed and CPU ratio range crossed parity. Host load rose from approximately
30 to 71 on 24 logical CPUs during measurement. Median allocation differences
of 5 to 8 bytes per operation are negligible at 17.8 to 30.6 MB allocated per
operation.

The aggregate results are in
[analysis-library-body-use-admission-2026-09.tsv](analysis-library-body-use-admission-2026-09.tsv).

## Compared variants

- Baseline `2bd839613e7e78294f47d8722e24783f68999e88` contains the
  development-only harness and the existing admission check.
- Experiment `5c6652ca5f7319ae689a4f6bb27899bcad1aebf8` removes the
  `MalformedBody` scan and the resulting dead unavailable-body counter.
- `git diff` between those commits changes only
  `src/ILInspector.Analysis/Planning/AnalysisLibraryBodyUse.cs`: one counter,
  the 11-line check, and the counter's result projection.

Both commits contain identical harness source under
`tools/AnalysisLibraryBodyUseAdmissionBenchmark/`. The harness remains outside
`dotnet-inspect.slnx`; it is the committed reproducer for this exact
whole-product admission experiment, not a CI test or supported performance
offering. The commits and measurements predate the general
Direct/LINQ/NLinq/Planner
[`BodyUseScorecard`](../../tools/BodyUseScorecard/Program.cs). New body-use
terminal investigations use that scorecard; this narrow harness is not a
parallel oracle.

## Method

- SDK: `11.0.100-rc.1.26425.128`.
- Host: Linux 6.14, AMD Ryzen 9 9900X, 12 cores and 24 logical CPUs.
- Runtime: separate NativeAOT `linux-x64` binaries.
- Binary SHA-256:
  - baseline
    `f5ae53585114c25e78e59bf4f47e22a7cc1b722dff718b21b28cfd545a89346f`;
  - experiment
    `8d1222d988e3b5bda18d90881c3a15ed8aa2a3485f8d7cf1480eef7a6ac2f9bd`.
- Two warmups per asset, five product operations per sample, 15 samples per
  run, and six paired runs.
- Pair order alternated baseline/experiment and experiment/baseline.
- Both processes were pinned to logical CPU 23.
- The harness hashes the complete public result contract outside the timed
  region and rejects result drift within a run.

Publish each exact commit:

```bash
dotnet publish tools/AnalysisLibraryBodyUseAdmissionBenchmark \
  -c Release -r linux-x64 -o artifacts/body-use-admission
```

Run one side of a pair:

```bash
taskset -c 23 \
  artifacts/body-use-admission/analysis-library-body-use-admission-benchmark \
  --rounds 15
```

## Interpretation boundary

This first benchmark establishes that the check has no observable result effect
on the three selected real assemblies. It does not establish that removing the
check is correct for malformed inputs, because none of these assets produced a
`MalformedBody` diagnostic.

The noisy host supports no elapsed-time win or regression claim. A later
decision that depends on a small performance difference should rerun these
exact commits and commands on an accepted, quiet performance host. The result
equality evidence is independent of that timing limitation.
