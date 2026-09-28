---
name: deep-inspect
description: Use when a dotnet-inspect change needs expensive evidence outside normal PR CI, or when preparing release certification; coordinate Deep Inspect lanes (full slow tests, cross-platform certification, IL round-trip sweep, corpus sensors, package discovery, validity scans, and analysis census).
---

# Deep Inspect

This is a repo-local maintainer skill (CI/certification harness), not a
shipped end-user capability; it is not embedded in the dotnet-inspect binary.

Use this skill when a change needs expensive evidence outside normal PR CI.
Deep Inspect is opt-in for risky PRs. Its `test`, `platform-test`,
and decompiler-corpus jobs run daily to certify a commit for release. Publish
consumes the latest completed certification evidence rather than rerunning the
slow suites merely because `main` moved or the release decision happened
later. Runs do not cancel earlier runs in the same lane; complete outcomes are
required for release risk review.
The nightly release-candidate workflow builds immutable release assets and then
calls the `release-candidate` lane for the exact same commit. That lane combines
release certification, census, and comprehensive Inspect Web evidence without
the separately scheduled authored-corpus ratchet. Every lane can also be
dispatched on demand during the day.

## Lanes

| Lane | Use for | Runs |
| ---- | ------- | ---- |
| `test` | Daily/on-demand release certification or blocking proof before risky merges | Full decompiler tests, full analysis tests, vendored ILAssembler restore, full IL round-trip sweep. |
| `platform-test` | Daily/on-demand release certification across Windows, macOS, and Ubuntu | Reduced cross-platform suite: CLI, CSharpText, artifact, fast decompiler, NuGetFetch offline, metadata, services, query, and Research tests, plus `ilasm`/`ildasm`/`mdv` setup. |
| `census` | Observational broad signal and triage | Real-world corpus sensor, validity predicate scan, uncapped validity sweep, assertion scan, analysis corpus sensor, paydirt recall. |
| `package-sweep` | Weekly/on-demand discovery over current top NuGet packages | Product-backed package acquisition plus bounded per-library fully-raised, validity, defect-class, and promotion-candidate reporting. |
| `authored-corpus` | Daily/on-demand regression ratchet against checksum-verified authored source | Restores the pinned authored-source corpus and fails on quality regression or measurement-integrity loss. |
| `inspect-web` | Daily/on-demand comprehensive Browser/Wasm regression evidence | Runs generated-facade version invariance, mutation controls, and Mono/CoreCLR multi-facade and managed-operation canaries. |
| `release-candidate` | Called after nightly release assets are assembled | Runs `test`, `platform-test`, decompiler corpus, `census`, and `inspect-web` for the candidate's exact SHA. |
| `nightly` | Opt-in next-SDK/compiler validation | Builds with the .NET daily SDK and checks opt-in compiler lowering drift; intentionally excluded from `all`. |
| `all` | Release-candidate deep read | The `test`, `platform-test`, decompiler-corpus, `census`, `authored-corpus`, and `inspect-web` lanes. |

Run manually:

```bash
gh workflow run deep-inspect.yml -f lane=test
gh workflow run deep-inspect.yml -f lane=platform-test
gh workflow run deep-inspect.yml -f lane=census
gh workflow run deep-inspect.yml -f lane=package-sweep
gh workflow run deep-inspect.yml -f lane=authored-corpus
gh workflow run deep-inspect.yml -f lane=inspect-web
gh workflow run deep-inspect.yml -f lane=release-candidate
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
dotnet run --project tests/DotnetInspect.Cli.Tests -c Release
source eng/activate-iltools.sh
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- --gate no-corpus
dotnet run --project tests/ILInspector.Analysis.Tests -c Release
bash eng/restore-ilassembler.sh
dotnet run --project tests/DotnetInspector.ILRoundtrip.Tests -c Release
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- --gate corpus
```

The corpus command runs as a separate workflow job and can take hours. Omit it
only when intentionally reproducing the non-corpus `test` job rather than the
complete dispatched `test` lane.

A green daily or manually dispatched certification requires the `test`,
`platform-test`, and decompiler-corpus jobs at one exact SHA. Publish consumes
the most recent completed, non-cancelled certification run at or before the
release target. Non-success outcomes remain red and require explicit human
acceptance after review; publishing a later descendant is a separate explicit
decision. The target's exact main-push `ci-required` result must succeed, but
main-push CI does not run the PR-only substantive test jobs, and the Deep
Inspect result does not claim to cover intervening changes.

The `platform-test` lane is workflow-owned and runs the reduced
cross-platform suite on Windows, macOS, and Ubuntu. When reproducing a
platform-only break locally, mirror the exact project list and tool activation
from `.github/workflows/deep-inspect.yml` for the affected platform.

The workflow is also the owner of its oracle-acquisition outcome. The `test`
and `platform-test` jobs may continue after an `ilasm`/`ildasm`/`mdv` restore
failure so independent suites still report, but their terminal checks must keep
the lane from succeeding without those oracle comparisons. Daily and
manually-dispatched job outcomes are the evidence for that GitHub Actions
behavior; local tests cover the activation scripts themselves, not workflow
step spelling, ordering, or an exhaustive source model of GitHub Actions.

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

- Treat `test`, `platform-test`, and decompiler-corpus failures as visible
  release risks. Fix them when the release decision requires green evidence;
  otherwise preserve their red conclusions and follow the release workflow's
  explicit failure-acceptance path. Do not automatically rerun Deep Inspect
  merely because the release target moved.
- Treat `census` output as triage signal unless a command exits nonzero by
  design. Compare snapshots against committed baselines and route meaningful
  drift to issues or follow-up PRs.
- Treat `inspect-web` failures as Browser/Wasm regression blockers. Ordinary
  PRs run the fast boundary modes; changes to the facade generators, canaries,
  or managed bridge owners run the complete modes before merge.
- Do not add broad/corpus-style tests to PR CI. Mark them
  `[Trait("Speed", "Slow")]` and keep them in Deep Inspect / full local runs.
