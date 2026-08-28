# GitHub status queries

This document owns how repository agents query and interpret GitHub pull
request, mergeability, and CI state. `AGENTS.md` owns which states gate work;
[`round-orchestration.md`](round-orchestration.md#status-discovery) owns what a
round does with the result. This document does not define review eligibility,
merge authorization, candidate formation, or scheduling policy.

## Query only for a decision

Treat shared GitHub API capacity as scarce. A status read is justified only
when its answer unlocks an eligibility, recovery, completion, readiness, or
merge decision. Do not query merely because a push happened or time passed,
probe quota before a request, or spend one API bucket to decide whether to use
another.

Use REST for routine PR lifecycle, mergeability, and CI status. Use GraphQL
only when the task genuinely needs graph-shaped data that the REST pair cannot
provide economically, such as review threads with their comments or one
consistent snapshot of several related objects. Do not use GraphQL as a
fallback when REST is rate-limited.

## Routine REST snapshot

Run each request as a separate agent tool call so a failure is classified
before another request spends capacity. Query the PR first:

```bash
pr_number=1234
gh api "repos/{owner}/{repo}/pulls/$pr_number" \
  --include \
  --jq '{head:.head.sha,state,merged,draft,mergeable,mergeable_state}'
```

Handle lifecycle, head mismatch, and `mergeable: false` before querying checks:

- Classify a merged PR as terminal success.
- Classify a closed, unmerged PR or a draft as requiring workflow action.
- Classify a returned head different from the expected head as a head mismatch
  that invalidates all fixed-head evidence.
- Classify `mergeable: false` as a conflict; CI cannot change that observation.

When CI state is still required, copy the validated 40-character head into a
separate request:

```bash
head_sha="replace-with-validated-head-sha"
gh api "repos/{owner}/{repo}/commits/$head_sha/check-runs?per_page=100" \
  --include \
  --jq '[.check_runs[]|select(.name=="ci-required")|{status,conclusion}]'
```

`gh api` expands `{owner}` and `{repo}` but not arbitrary `{n}` or `{sha}`
placeholders. The explicit SHA pin ensures the check belongs to the validated
head. The PR endpoint also triggers GitHub's mergeability computation.

`--include` exposes the HTTP status and the `Retry-After`,
`x-ratelimit-remaining`, and `x-ratelimit-reset` headers without another API
request; `--jq` still selects the response body.

## Graph-shaped snapshots

When GraphQL is justified, request the lifecycle and fixed-head fields needed
by the same interpretation rules: `state`, `merged`, `headRefOid`,
`baseRefOid`, `baseRef { target { oid } }`, `isDraft`, `mergeable`,
`mergeStateStatus`, and `statusCheckRollup` state and contexts with
`pageInfo`. Request enough contexts for the normal check matrix; if another
page exists and `ci-required` is absent, page before concluding that the check
is missing.

Use `git fetch`, not GraphQL, to discover the live base tip for carry-forward
analysis.

## Classify failures

Classify a failed REST request from its status, headers, and surfaced body:

- HTTP 429 is rate-limited.
- HTTP 403 is rate-limited only when `Retry-After` is present,
  `x-ratelimit-remaining` is zero, or the body identifies a secondary rate
  limit.
- A transport failure or GitHub 5xx response is transient.
- Every other non-success or malformed response is terminal, including 401,
  an unclassified 403, 404, and 422.

For a rate-limited result, return the concrete reason and a retry-not-before
time. `Retry-After` is authoritative when present; use `x-ratelimit-reset` only
when `x-ratelimit-remaining` is zero. For a transient result without an
authoritative time, return a conservative retry recommendation rather than
silently treating the failure as pending.

For a terminal result, return the concrete response and terminal
classification. State mutation, scheduling, recovery, and user notification
belong to round orchestration. No retry counter or guessed retry budget is part
of the query contract.

## Interpret a successful snapshot

Confirm the returned head before using any state. A run or check identifier is
pinned to one commit and cannot detect a later push.

Keep these distinctions:

- Green CI does not imply mergeability.
- REST `mergeable_state` and GraphQL `mergeStateStatus` are composite merge
  states, not CI conclusions.
- REST `mergeable: null` and GraphQL `mergeable: UNKNOWN` are pending, not
  conflict-free.
- A missing `ci-required` is inconclusive, not green.
- Green is REST conclusion `success` or GraphQL conclusion `SUCCESS`.
- A skipped leaf job is not evidence; the aggregate `ci-required` still must
  conclude successfully.
- REST `mergeable_state: "behind"` requires carry-forward classification before
  a readiness statement or merge.
- REST `mergeable_state: "blocked"` prevents readiness or merge even when
  conflict-free and green; it does not by itself prevent round advancement
  when that round's prerequisites are satisfied.

When orchestration records that `ci-required` was already green for the
expected head, the next snapshot may omit the check request while mergeability
remains pending. Once mergeability becomes definite, re-read `ci-required`
before returning a green observation because a workflow rerun can change check
state without changing the head.
