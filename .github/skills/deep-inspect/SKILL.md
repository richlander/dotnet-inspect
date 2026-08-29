---
name: deep-inspect
description: Use when a dotnet-inspect change needs expensive evidence outside normal PR CI, or when preparing release certification; coordinate Deep Inspect lanes (full slow tests, cross-platform certification, IL round-trip sweep, corpus sensors, package discovery, validity scans, and analysis census).
---

# Deep Inspect

This is a repo-local maintainer skill (CI/certification harness), not a
shipped end-user capability; it is not embedded in the dotnet-inspect binary.

Use this skill when a change needs expensive evidence outside normal PR CI.
Deep Inspect is opt-in for risky PRs. Its `test`, `platform-test`, and
decompiler-corpus jobs run daily to certify a commit for release, and can be
dispatched on demand during the day. Publish consumes that evidence rather than
rerunning the slow suites.

## Lanes

| Lane | Use for | Runs |
| ---- | ------- | ---- |
| `test` | Daily/on-demand release certification or blocking proof before risky merges | Full decompiler tests, full analysis tests, vendored ILAssembler restore, full IL round-trip sweep. |
| `platform-test` | Daily/on-demand release certification across Windows, macOS, and Ubuntu | Reduced cross-platform suite: CLI, CSharpText, artifact, fast decompiler, NuGetFetch offline, metadata, services, query, and Research tests, plus `ilasm`/`ildasm`/`mdv` setup. |
| `census` | Observational broad signal and triage | Real-world corpus sensor, validity predicate scan, uncapped validity sweep, assertion scan, analysis corpus sensor, paydirt recall. |
| `package-sweep` | Weekly/on-demand discovery over current top NuGet packages | Product-backed package acquisition plus bounded per-library fully-raised, validity, defect-class, and promotion-candidate reporting. |
| `authored-corpus` | Regression ratchet against checksum-verified authored source | Restores the pinned authored-source corpus and fails on quality regression or measurement-integrity loss. |
| `nightly` | Opt-in next-SDK/compiler validation | Builds with the .NET daily SDK and checks opt-in compiler lowering drift; intentionally excluded from `all`. |
| `all` | Release-candidate deep read | The `test`, `platform-test`, decompiler-corpus, `census`, and `authored-corpus` lanes. |

Run manually:

```bash
gh workflow run deep-inspect.yml -f lane=test
gh workflow run deep-inspect.yml -f lane=platform-test
gh workflow run deep-inspect.yml -f lane=census
gh workflow run deep-inspect.yml -f lane=package-sweep
gh workflow run deep-inspect.yml -f lane=authored-corpus
gh workflow run deep-inspect.yml -f lane=nightly
gh workflow run deep-inspect.yml -f lane=all
```

Inspect recent runs and artifacts:

```bash
gh run list --workflow deep-inspect.yml --limit 10
gh run view <run-id> --log-failed
gh run download <run-id> -D /tmp/deep-inspect-<run-id>
```

## Local equivalents

For the test lane:

```bash
dotnet build dotnet-inspect.slnx -c Release
dotnet run --project src/dotnet-inspect.Tests -c Release
source eng/activate-iltools.sh
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- --gate no-corpus
dotnet run --project src/ILInspector.Analysis.Tests -c Release
bash eng/restore-ilassembler.sh
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- --gate corpus
```

The corpus command runs as a separate workflow job and can take hours. Omit it
only when intentionally reproducing the non-corpus `test` job rather than the
complete dispatched `test` lane.

A successful daily or manually dispatched certification requires the `test`,
`platform-test`, and decompiler-corpus jobs at one exact `main` SHA. Publish
requires that certification run ID. Publishing a later descendant remains an
explicit operator decision. Its exact main-push `ci-required` result must
succeed, but main-push CI does not run the PR-only substantive test jobs, and
the certification does not claim to cover intervening changes.

The `platform-test` lane is workflow-owned and runs the reduced
cross-platform suite on Windows, macOS, and Ubuntu. When reproducing a
platform-only break locally, mirror the exact project list and tool activation
from `.github/workflows/deep-inspect.yml` for the affected platform.

For the census lane, prefer the workflow so artifacts are retained. If running
locally, use the same scripts/baselines as `deep-inspect.yml` and preserve the
generated snapshots/cards under `/tmp` or `artifacts/` for review.

The package sweep runs every Monday at 09:00 UTC and can also be dispatched
manually. It is owned by `@richlander`, is discovery-only, and never gates a
pull request. Each run resolves the latest stable versions for ranks 1-10,
records exact package/version/TFM provenance, and samples at most 250 methods
and 25 semantic-validity candidates per selected library. Promote a package to
an existing pinned corpus only after a reported defect or unsupported shape is
accepted for ongoing coverage.

## Reading results

- Treat `test`, `platform-test`, and decompiler-corpus failures as blockers:
  reproduce locally, identify the first failing proof, and fix it before
  certification can authorize publish.
- Treat `census` output as triage signal unless a command exits nonzero by
  design. Compare snapshots against committed baselines and route meaningful
  drift to issues or follow-up PRs.
- Do not add broad/corpus-style tests to PR CI. Mark them
  `[Trait("Speed", "Slow")]` and keep them in Deep Inspect / full local runs.
