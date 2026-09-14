# GitHub status queries

This document owns how repository agents query and interpret GitHub pull
request, mergeability, and CI state. `AGENTS.md` owns which states gate work;
[`round-orchestration.md`](round-orchestration.md#status-discovery) owns what a
round does with the result. This document does not define review eligibility,
merge authorization, candidate formation, or scheduling policy.

## Query only for a decision

Repository policy treats GitHub API capacity as scarce. GitHub documents
separate primary REST and GraphQL limits, while secondary limits such as
concurrent requests and CPU time are shared across both APIs. Use response
headers instead of a quota probe where possible; GitHub notes that the rate
limit endpoint can itself count against secondary limits. See
[REST API rate limits][rate-limits].

The [round cadence](round-orchestration.md#bounded-status-waiting) is a decision
that justifies a read; it is not a time-triggered poll. Repository policy uses
REST for routine PR lifecycle, mergeability, and CI status. Use GraphQL for
graph-shaped data such as review threads or a consistent group of related
objects. A merge or readiness goal also justifies GraphQL because
`mergeStateStatus` is the documented field that reports a blocked merge.
During a bounded third- or sixth-round wait, GraphQL may also provide CI status
when the REST **primary** limit is exhausted because the primary limits are
separate, but the lifecycle and status reads remain separated by the local
conflict probe below. Do not switch APIs to evade a secondary limit.

## Routine REST snapshot

Repository policy queries the PR before checks so lifecycle, head mismatch, and
conflict can short-circuit a second request. Run each request as a separate
agent tool call so a failure is classified before another request spends
capacity:

```bash
pr_number=1234
gh api "repos/{owner}/{repo}/pulls/$pr_number" \
  --include \
  --jq '{head:.head.sha,base:.base.ref,state,merged,draft,mergeable,mergeable_state}'
```

Handle lifecycle, candidate mismatch, and `mergeable: false` before checks:

- Classify a merged PR as terminal success.
- Classify a closed, unmerged PR or a draft as requiring workflow action.
- Classify a returned head different from the expected head as a head mismatch
  that invalidates all fixed-head evidence.
- Classify a returned base ref different from the expected base ref as a
  candidate mismatch that invalidates all review and merge authorization.
- Treat `mergeable: false` as not conflict-free. GitHub's GraphQL
  `MergeableState.CONFLICTING` documents the corresponding conflict meaning.

When CI state is still required, copy the validated 40-character head into a
separate request. The same response body projects both the aggregate gate and
non-green leaf checks:

```bash
head_sha="replace-with-validated-head-sha"
gh api "repos/{owner}/{repo}/commits/$head_sha/check-runs?per_page=100" \
  --include \
  --jq '{gate:[.check_runs[]|select(.name=="ci-required")|
                 {status,conclusion}],
         non_green:[.check_runs[]|
                    select(.conclusion!=null
                           and .conclusion!="success"
                           and .conclusion!="skipped"
                           and .conclusion!="neutral")|
                    {name,conclusion}]}'
```

The check-runs endpoint is paginated with a maximum `per_page` of 100. If the
response `link` header advertises `rel="next"` and `ci-required` is absent,
follow the pages before concluding that the gate is missing. Follow every page
when reporting leaf-check problems. When only the aggregate is needed, the
endpoint also accepts `check_name=ci-required`. GitHub limits this endpoint to
check runs from the 1,000 most recent check suites on one Git reference, so
even complete pagination has that documented ceiling. See
[List check runs for a Git reference][check-runs].

`gh api` expands `{owner}`, `{repo}`, and `{branch}` but not arbitrary
placeholders such as `{n}` or `{sha}`. The explicit SHA pin ensures the check
request names the validated head. GitHub documents that required checks must
pass on the latest commit SHA, but reports against the test merge commit when
that commit carries a status. If the expected gate is absent, check which
commit GitHub identifies as the required-check target.

GitHub documents that getting a pull request triggers its test-merge
calculation. A null `mergeable` means the background job is still running;
after giving it time, resubmit the request. `--include` emits the HTTP status
and response headers; `--jq` selects values from the response body. See
[Get a pull request][get-pr],
[required-check troubleshooting][required-checks],
[REST pagination][pagination], and the [`gh api` manual][gh-api].

## Probe the live base locally

GitHub's test-merge result does not replace a local conflict probe during a
bounded status wait. After the PR response validates lifecycle, head, and base
ref, a reported GitHub conflict may short-circuit the snapshot. Otherwise,
fetch the live base non-mutating before querying checks:

```bash
git fetch origin "$base_ref"
base_tip=$(git rev-parse FETCH_HEAD)
```

Compare `base_tip` with the `conflict-checked-base` recorded for the expected
head and base ref. When no tip is recorded or the tip changed, run the
non-mutating test merge:

```bash
git merge-tree --write-tree "$head_sha" "$base_tip"
```

Exit status zero records `conflict-checked-base=$base_tip` and allows the
snapshot to continue. Exit status one means the candidate conflicts with the
live base and enters conflict recovery before any CI query. Any other exit
status is a probe failure, not evidence of either outcome; classify a concrete
transport failure as transient and an invalid ref, missing object, or command
failure as terminal.

Fetch on every scheduled snapshot so base movement is discovered, but rerun
the test merge only for an unrecorded tip. Base movement alone does not
invalidate the candidate, trigger integration, or spend review evidence. The
probe establishes only whether Git can form a tree merge for that head and
base tip; it does not establish CI state, semantic non-interaction, or merge
authorization.

## Graph-shaped snapshots

When GraphQL is justified during a bounded status wait, first request only the
lifecycle and fixed-head fields needed by the same interpretation rules:
`state`, `merged`, `headRefOid`, `baseRefName`, `baseRefOid`,
`baseRef { target { oid } }`, `isDraft`, `mergeable`, and `mergeStateStatus`.
Do not request `statusCheckRollup` in that first query.

After validating the response and completing a clean local conflict probe,
request `headRefOid` and `statusCheckRollup` state and contexts with `pageInfo`
in a second query. Confirm `headRefOid` still equals the expected head before
using the status result. Request enough contexts for the normal check matrix;
if another page exists and `ci-required` is absent, page before concluding that
the check is missing. These fields and the `MergeableState` and
`MergeStateStatus` enums are defined in the [GraphQL object][graphql-pr] and
[enum][graphql-enums] references.

Use `git fetch`, not GraphQL, to discover the live base tip for local conflict
probes and carry-forward analysis.

## Classify failures

GitHub documents the following rate-limit evidence:

- Primary and secondary limits can return HTTP 403 or 429.
- Primary exhaustion reports `x-ratelimit-remaining: 0`.
- A secondary-limit response carries an identifying error message and may
  include `Retry-After`; GitHub notes that some secondary triggers are not
  disclosed.
- When neither `Retry-After` nor an exhausted-primary reset applies, wait at
  least one minute and use exponentially increasing delays. Continuing to
  request while rate-limited can result in an integration ban.

Repository policy classifies HTTP 403 as rate-limited only when `Retry-After`
is present, `x-ratelimit-remaining` is zero, or the body identifies a secondary
rate limit. It treats a transport failure or GitHub 5xx response as transient
and every other non-success or malformed response as terminal, including 401,
an unclassified 403, 404, and 422. Those transient and terminal categories are
repository heuristics, not GitHub guarantees.

For a rate-limited result, return the concrete reason and a retry-not-before
time. `Retry-After` is authoritative when present; use `x-ratelimit-reset` only
when `x-ratelimit-remaining` is zero. For a transient result without an
authoritative time, return a conservative retry recommendation rather than
silently treating the failure as pending.

For a terminal result, return the concrete response and terminal
classification. State mutation, scheduling, recovery, and user notification
belong to round orchestration. No retry counter or guessed retry budget is part
of the query contract. See [REST API rate limits][rate-limits].

## Interpret a successful snapshot

Confirm the returned head and base ref before using any state. A run or check
identifier is pinned to one commit and cannot detect a later push.

Keep these distinctions:

- Green CI does not imply mergeability.
- REST `mergeable: null` and GraphQL `mergeable: UNKNOWN` are pending, not
  conflict-free.
- Repository terminology calls REST `mergeable: true` or GraphQL
  `mergeable: MERGEABLE` positive mergeability. GitHub explicitly documents
  the GraphQL value in terms of merge conflicts and documents the REST value as
  the result of its test-merge computation.
- A local conflict against the fetched live base takes precedence over positive
  or pending GitHub mergeability and enters conflict recovery.
- A missing `ci-required` is inconclusive, not green.
- GitHub branch protection accepts required-check conclusions `success`,
  `skipped`, and `neutral`. This repository deliberately uses the stricter
  rule that the aggregate `ci-required` check itself must conclude `success`.
- A skipped leaf job is not evidence by itself; this repository's aggregate
  decides whether skipped work was expected.
- GraphQL `mergeStateStatus` is a documented composite merge state.
- GraphQL `mergeStateStatus: BLOCKED` separately reports that the merge is
  blocked even when conflict-based mergeability is positive.
- REST `mergeable_state` is documented only as a string, without published
  values or semantics. Retain it for diagnostics, but do not drive transitions
  from specific values such as `behind` or `blocked`.

Read `ci-required` freshly for every round and every boundary. GitHub documents
that a workflow rerun uses the same commit SHA, so check state can change
without a new head. See [required-check troubleshooting][required-checks] and
[rerunning workflows][rerun-workflows].

[get-pr]: https://docs.github.com/en/rest/pulls/pulls?apiVersion=2022-11-28#get-a-pull-request
[check-runs]: https://docs.github.com/en/rest/checks/runs?apiVersion=2022-11-28#list-check-runs-for-a-git-reference
[gh-api]: https://cli.github.com/manual/gh_api
[graphql-enums]: https://docs.github.com/en/graphql/reference/enums
[graphql-pr]: https://docs.github.com/en/graphql/reference/objects#pullrequest
[pagination]: https://docs.github.com/en/rest/using-the-rest-api/using-pagination-in-the-rest-api?apiVersion=2022-11-28
[rate-limits]: https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api?apiVersion=2022-11-28
[required-checks]: https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks
[rerun-workflows]: https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs
