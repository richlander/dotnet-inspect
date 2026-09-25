# Release workflow

This document explains how one retained nightly candidate becomes a coordinated
dotnet-inspect release: seven NuGet packages, one GitHub release, and the
production site at `https://dotnet-inspect.net`. Repository development,
worktree, build, and test rules live in [AGENTS.md](../AGENTS.md). The
executable sources of truth are
[release-candidate.yml](../.github/workflows/release-candidate.yml) and
[release.yml](../.github/workflows/release.yml). The repo-local
[release skill](../.github/skills/release/SKILL.md) is the operator playbook
and must stay aligned with this contract.

## Release boundary

Candidate construction, readiness, and publication have separate owners:

| Owner | Responsibility |
| --- | --- |
| `release-candidate.yml` | Build and retain the complete package/site asset set for one exact `main` SHA, then run exact-SHA Deep Inspect qualification. |
| Daily operations | Establish ready-to-ship version, notes, product-skill, peer-skill, CI, artifact, and certification state. |
| Release agent | Independently revalidate readiness, present concerns, and carry out the operator's publication decision. |
| `release.yml` | Revalidate one selected candidate run and attempt, then publish its retained bytes without rebuilding them. |
| Operator | Select the candidate, accept any completed certification concerns, authorize publication, and approve the production environment. |

The normative identity and readiness contract is
[Nightly release candidate](release-candidate.md). A candidate is not selected
by version, branch, ancestry, or "latest" state. It is selected by workflow run
ID and attempt, which resolve one exact source SHA and one retained artifact ID
and digest.

## Immutable release unit

One publication uses one `(commit SHA, VersionPrefix)` pair across:

- `dotnet-inspect`, the TFM-agnostic pointer package;
- five RID-specific NativeAOT packages;
- `dotnet-inspect.any`, the managed fallback;
- the GitHub release and tag; and
- the production Inspect Web site.

The package and site bytes are built and verified by the candidate workflow
before Deep Inspect runs. Publication may query GitHub, revalidate evidence,
download the selected artifact, authenticate, and upload it. It never runs
`dotnet pack`, `dotnet publish`, an npm build, or any other repair or
normalization step.

The retained candidate contains:

```text
packages/
site/
package-sha256.txt
site-sha256.txt
release-candidate.json
```

The receipt records the source SHA, version, workflow run ID and attempt, and
the package/site manifest digests. GitHub's artifact API supplies the artifact
ID and archive digest. `release.yml` fixes all of those values in its resolve
job and revalidates them independently before package publication and before
production deployment.

## Readiness and concern handling

Daily operations reports a candidate as **ready to ship** only when its exact
SHA has successful required CI, its complete artifact is unexpired, all
required qualification jobs succeeded, and version, release notes, shipped
skills, and any peer publication material are current.

A candidate is **ready with concerns** only when asset construction succeeded
and every required qualification job completed, but one or more qualification
jobs concluded with failure. Cancelled, skipped, running, missing,
mixed-attempt, expired, or structurally incomplete evidence cannot be accepted
as a concern.

The release agent independently checks that report. For a concern-bearing
candidate, it presents every non-success job conclusion to the operator. The
operator may decline the candidate or authorize that exact candidate with
`accept_certification_concerns=true`. Green candidates reject unnecessary
concern acceptance.

## Publication validation

`eng/validate-release-candidate.sh` downloads the selected run, latest-attempt
jobs, retained artifact metadata, and exact-SHA `ci-required` check. Its
file-based validator requires:

- workflow path `.github/workflows/release-candidate.yml`;
- event `schedule` or `workflow_dispatch`, branch `main`, and this repository
  as both workflow and head repository;
- the exact positive attempt selected by the operator;
- completed successful source, five native package, portable package,
  production-site, and assembly jobs from that same attempt;
- one same-attempt copy of every required Deep Inspect candidate job;
- successful exact-SHA `ci-required`;
- a coherent green or explicitly accepted concern outcome;
- exactly one unexpired, nonempty
  `dotnet-inspect-release-candidate` artifact for the run; and
- a valid SHA-256 artifact digest.

Before consuming bytes,
`eng/verify-release-candidate-artifact.sh` requires the checked-out commit,
receipt SHA/version/run/attempt, project and root-skill versions, both manifest
digests, every file checksum, the exact seven-package census and tool settings,
and the complete production-site artifact contract.

The CI workflow contract fixes revalidation before download, selection by
artifact ID, digest mismatch failure, retained-byte verification before either
consumer, package publication before site deployment, and Azure build
disablement.

## Dispatch

From the **Publish** workflow on `main`, enter:

1. `candidate_run_id`: the selected `release-candidate.yml` run ID.
2. `candidate_attempt`: its exact complete workflow attempt.
3. `accept_certification_concerns`: `false` for green evidence; `true` only
   after the operator accepts every disclosed concern for this candidate.
4. `confirm`: `publish`.

The resolve job fails unless the dispatch itself is from `main` and the
confirmation is exact. It records the resolved SHA, attempt, artifact ID,
artifact digest, and concern-acceptance state as immutable outputs for both
consumers.

The package job then:

1. checks out the candidate SHA;
2. revalidates the selected GitHub evidence against every resolved identity;
3. downloads the exact artifact ID with digest mismatch configured as an
   error;
4. verifies the retained receipt, manifests, package set, and site;
5. compares any already-published NuGet package bytes with the retained package
   before treating it as a retry;
6. obtains the temporary NuGet API key through OIDC;
7. publishes native packages first, the managed fallback next, and the pointer
   last; and
8. creates the GitHub release at the candidate SHA from those same retained
   packages.

The production job depends on successful package/GitHub publication. In the
`inspect-web-production-promotion` environment it independently revalidates,
downloads, and verifies the same artifact ID and digest, then uploads
`site/wwwroot` and `site/api` with Azure's app and API builds disabled.

Comparison websites are independent automatic evidence built from the
candidate SHA. Their success or failure neither authorizes nor supplies
production publication.

## Package publication order and reach

The pointer package references runtime-specific packages and is therefore
published last. `--skip-duplicate` permits recovery after partial publication
without changing the candidate identity.

The managed fallback must retain
`tools/net10.0/any/DotnetToolSettings.xml`, and the pointer must retain
`tools/any/any/DotnetToolSettings.xml`. Candidate construction and publication
verification both check those paths.

## Release-note and skill checkpoint

`src/DotnetInspect.Cli/release-notes.md`, root `README.md`, and product skills
under `skills/` ship in the candidate's package bytes. They cannot be repaired
at publication time. Daily operations must reconcile them before rerunning the
candidate workflow, and the release agent must independently confirm that the
selected candidate contains the reviewed result.

The root product skill version must equal `VersionPrefix`. Focused product
skills may keep independently versioned frontmatter. Repo-local maintainer
skills under `.github/skills/` are not embedded product content.

When the peer `richlander/dotnet-skills` bootstrap or manifests require an
update, daily operations prepares that coordinated publication before the
candidate becomes ready. The release decision covers only the prepared exact
candidate and peer material presented to the operator.

## Verification and recovery

After publication:

- verify all seven package IDs and the intended version on nuget.org;
- verify the GitHub release tag targets the candidate SHA and contains the same
  seven packages;
- verify the production site's data bar reports that version and linked SHA;
  and
- record the release URL, full SHA, run ID, attempt, and artifact identity on
  the successor release tracker.

Recovery preserves identity:

- **Resolution or revalidation fails:** stop and inspect the selected run. Do
  not substitute a nearby run, attempt, SHA, or artifact.
- **Candidate assets are missing, expired, or mixed across attempts:** the
  candidate is not publishable. Daily operations repairs readiness and creates
  a complete replacement candidate.
- **Qualification is cancelled, skipped, running, or structurally missing:**
  it is incomplete and cannot be accepted.
- **Qualification completed with failure:** review every disclosed outcome.
  Either decline the candidate or explicitly accept those concerns for this
  run and attempt.
- **Package publication partially succeeds:** rerun `release.yml` with the
  same inputs. Exact NuGet package-byte comparison, duplicate skipping, and
  artifact revalidation retain the original release unit. A same-version
  package with different bytes fails before any additional package is pushed.
- **The GitHub release exists:** verify its tag target and attached packages
  match the candidate before treating a retry as successful.
- **Production deployment fails after packages publish:** rerun with the same
  inputs. Do not advance the version or choose another artifact to repair only
  the site.
- **Any identity member changes:** treat it as another candidate and obtain a
  new operator decision.
