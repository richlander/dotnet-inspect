---
name: daily-operations
description: Use for daily chores in dotnet-inspect; establish release-candidate readiness, then inspect main CI, staging, performance, runtime-pin, security, dependency, and regression runs.
---

# Daily operations

This is a repo-local maintainer skill for one evidence-driven pass over routine
repository health. It coordinates existing workflows and specialist skills; it
does not replace their contracts or turn every unusual result into an incident.

Run the pass after the 09:00 UTC schedules have had a reasonable opportunity to
start, or use the same steps for an on-demand status check. GitHub schedules can
be delayed. A missing run is not a failure until queued and in-progress work,
workflow enablement, and recent scheduling delays have been checked.

## Establish the observation window

Record:

- the current UTC time and the previous 30 hours;
- current `origin/main`;
- the latest `main` push and its CI and staging runs;
- the latest nightly release-candidate run and its retained artifact;
- whether today is Monday UTC;
- every run ID, head SHA, event, attempt, status, conclusion, and URL used in
  the report.

Fetch without mutating an open candidate:

```bash
git fetch origin main
main_sha=$(git rev-parse origin/main)
printf 'main: %s\n' "$main_sha"
date -u
```

Use `gh run list` for discovery and `gh run view` for the selected run. Prefer
workflow filenames so display-name changes do not break the pass:

```bash
gh run list --workflow ci.yml --branch main --limit 10 \
  --json databaseId,headSha,event,status,conclusion,createdAt,updatedAt,url
gh run list --workflow deploy-inspect-web.yml --branch main --limit 10 \
  --json databaseId,headSha,event,status,conclusion,createdAt,updatedAt,url
gh run list --workflow release-candidate.yml --branch main --limit 10 \
  --json databaseId,headSha,event,status,conclusion,createdAt,updatedAt,url
gh run list --workflow deep-inspect.yml --event schedule --limit 10 \
  --json databaseId,headSha,event,status,conclusion,createdAt,updatedAt,url
```

Compare the expected-routine table below with the repository's current schedule
inventory. Add any newly scheduled workflow to the pass rather than silently
ignoring it:

```bash
rg -n -B 5 -A 2 'cron:' .github/workflows --glob '*.yml'
```

Do not assume the newest listed run covers `origin/main`; compare full SHAs.
For push workflows with cancel-in-progress behavior, an older cancelled run is
superseded when a newer run covers the intended current head.

## Expected routine

| UTC cadence | Workflow | Evidence or action |
| --- | --- | --- |
| Every `main` push | `ci.yml` | Current-head repository health. |
| Every `main` push | `deploy-inspect-web.yml` | Current-head staging build and deployment. |
| 00:17 daily | `release-candidate.yml` | Build immutable packages and production site, then run exact-SHA release certification, census, and comprehensive Inspect Web evidence. |
| After each completed candidate | `deploy-inspect-web-runtime-sites.yml` | Rebuild the candidate SHA as controlled Mono, CoreCLR IL, and CoreCLR R2R evidence, then automatically deploy both comparison sites. |
| 02:17 daily | `inspect-web-performance-nightly.yml` | Public production Mono/CoreCLR performance evidence. |
| 04:38 daily | `codeql-scheduled.yml` | CodeQL analysis. |
| 05:17 daily | `inspect-web-runtime-pin-proposal.yml` | Newer coherent .NET 12 candidate discovery, full cohort admission, and pin-only proposal branch. |
| 05:23 daily | `npm-audit-scheduled.yml` | Inspect Web lockfile audit and retained report. |
| 05:37 daily | `nuget-audit-scheduled.yml` | Restored NuGet dependency audit for each configured scope. |
| 05:43 daily | `nuget-dependency-submission.yml` | Submission of the resolved NuGet graph. |
| 09:00 daily | `deep-inspect.yml` | Authored-corpus regression ratchet. |
| 09:00 Monday | `deep-inspect.yml` | Top-package discovery sweep. |

Query each scheduled workflow with `--event schedule`; do not mistake a manual
dispatch for proof that its schedule fired. Query the comparison deployment
with `--event workflow_run` and match its recorded candidate run, attempt, and
SHA. The release-candidate run calls Deep Inspect after assembling the retained
assets. Deep Inspect's own schedule has separate 09:00 daily and 09:00 Monday
runs. Select them by creation time and inspect their jobs to confirm the
expected lane actually ran.

For a failed run, start with:

```bash
gh run view <run-id> --json headSha,event,status,conclusion,jobs,url
gh run view <run-id> --log-failed
```

Download artifacts only when the summary and failed logs do not establish the
classification. Keep scratch evidence outside tracked files.

## Apply the owning contracts

Use the `deep-inspect` skill for detailed interpretation, reproduction, or
manual dispatch of any Deep Inspect lane. Its release-certification jobs are
blocking proof; census and package-sweep output are primarily discovery and
triage evidence; authored-corpus and comprehensive Inspect Web are regression
gates.

Use
[`docs/inspect-web-runtime-performance.md`](../../../docs/inspect-web-runtime-performance.md)
for runtime cohort, production synthetic, and pin-advancement semantics:

- Mono or CoreCLR IL rejection, missing evidence, semantic mismatch, and
  unfamiliar CoreCLR R2R failure block the owning operation.
- The explicitly retained CoreCLR R2R thunk failure is an expected correctness
  rejection, not a performance point and not a daily repair item.
- Production performance publishes evidence and drift, not a regression
  threshold. Compare medians and runner load with recent accepted trend points
  before escalating.
- A current or older coherent runtime candidate is a successful no-op.

Use the `release` skill only when the user asks to ship. Daily certification,
green CI, a ready candidate, staging success, or a pin proposal is not release
authorization.

## Establish release readiness

Daily operations owns the ready-to-ship state; the release skill independently
validates that state only after the user asks to ship. Start from the newest
completed, non-cancelled `release-candidate.yml` run. Record its run ID,
attempt, exact SHA, job conclusions, artifact ID, digest, expiry, and URL.
Require exactly one unexpired `dotnet-inspect-release-candidate` artifact.

Inspect the run's jobs. Asset construction must be successful; missing,
cancelled, skipped, or failed asset jobs are blockers and cannot become
release concerns. Successful certification is clean. Completed non-success
Deep Inspect jobs make the candidate ready with concerns only after every
outcome is recorded. Cancelled, skipped, running, or structurally missing
certification is incomplete rather than an operator-acceptable concern.

Check repository preparation against the latest published GitHub release:

```bash
project_version=$(
  sed -n \
    's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' \
    src/DotnetInspect.Cli/DotnetInspect.Cli.csproj
)
root_skill_version=$(sed -n 's/^version: //p' skills/dotnet-inspect/SKILL.md)
released_tag=$(gh release view --json tagName --jq .tagName)
printf 'project=%s root-skill=%s released=%s\n' \
  "$project_version" "$root_skill_version" "$released_tag"
```

The project version must be the intended successor to the published release,
and the root shipped skill version must match it. Reconcile release notes and
every product skill changed by candidate-history features against the current
release tracker. Confirm the peer `richlander/dotnet-skills` bootstrap and
manifests are prepared for coordinated publication when their content or
version must change. Focused product skills may retain their own independently
versioned frontmatter; do not mechanically overwrite those versions.

When any preparation is stale, daily operations owns the repair: create a
focused branch and PR through the normal validation and review flow. Never
merge that repair, publish the peer plugin, or reinterpret stale material as
ready without the existing operator authorization. After the repair lands,
manually dispatch `release-candidate.yml` from `main` so the ready assets and
evidence share the repaired exact SHA.

## Check proposal and failure queues

List outstanding runtime-pin proposal branches:

```bash
git ls-remote --heads origin \
  'refs/heads/automation/inspect-web-runtime-pin-*'
```

One outstanding branch intentionally blocks later pin discovery. Report its
name, age, candidate identity, evidence run, and whether it still changes only
`inspect-web/runtime-cohort-pin.json`. Do not merge or delete it as a daily
cleanup action. If no branch exists, a successful `no-newer-candidate` proposal
run is clean.

Release-candidate or Deep Inspect scheduled failures open or update a
`nightly-failure` issue. Check the queue and correlate every open issue with the
newest owning run:

```bash
gh issue list --state open --label nightly-failure --limit 20 \
  --json number,title,updatedAt,url
```

Do not duplicate an issue already maintained by a workflow. Do not close one
until its failure is triaged and the relevant replacement evidence is clean.

## Classify before acting

Assign exactly one primary classification to each exception:

| Classification | Meaning | Daily action |
| --- | --- | --- |
| Blocker requiring repair | Current evidence demonstrates a product, contract, security, dependency, certification, or staging failure. | Preserve the first failing proof, identify the owning subsystem, and prioritize repair. |
| Evidenced transient | The unchanged head failed for an external or runner condition and concrete evidence excludes an authored defect. | Rerun only failed jobs or the unchanged workflow; retain both run IDs. |
| Observational drift | Evidence changed without violating an owned threshold or contract. | Quantify against recent accepted evidence and open or update focused triage only when meaningful. |
| Expected rejection or non-action | The owner explicitly admits the result, the candidate is not newer, or a run was superseded. | Record the reason and take no repair action. |
| Clean | The expected run completed successfully and produced its required evidence. | No action. |

Never label a failure transient merely because a retry might pass. Require an
external symptom such as runner provisioning, service outage, rate limiting, or
a known nondeterministic infrastructure failure plus unchanged-source evidence.
Use GitHub's rerun operation so the head SHA remains fixed:

```bash
gh run rerun <run-id> --failed
```

For `release-candidate.yml`, rerun the complete workflow instead:

```bash
gh run rerun <candidate-run-id>
```

The new attempt must rebuild and overwrite the complete retained candidate so
its receipt, assets, and certification share one attempt. A failed-jobs-only
candidate rerun mixes prior-attempt assets with later evidence and is not
ready.

Before manually dispatching a missing schedule, confirm there is no queued or
in-progress run for that workflow and that the workflow remains enabled on
`main`. Dispatch the same workflow with its scheduled defaults; do not use
diagnostic overrides as a substitute for the scheduled contract. For missing
nightly assets plus certification, dispatch `release-candidate.yml`, not
Deep Inspect by itself.

## Report

Lead with the overall state and the first action. Keep the report compact:

```text
Daily operations — 2026-07-14 UTC
Main: <sha>
Overall: blocker | attention | clean

Priority:
1. <owner>: <classification>, <run or issue>, <next required action>

Clean/no-action:
- <workflow>: <run>, <classification and concise reason>

Outstanding:
- Release candidate: <run and ready | ready with concerns | not ready>
- Runtime pin: <none | branch and age>
- Nightly failures: <none | issue list>
```

Order actions by: security or dependency exposure; current-main CI or staging;
release-certification and regression blockers; pin advancement blockers;
meaningful performance or corpus drift; housekeeping. Include accepted
transients and expected rejections under clean/no-action so they do not obscure
real work.

Do not merge pull requests, delete proposal branches, publish packages, promote
deployments, approve environments, or change baselines without the existing
task-specific authorization and workflow.
