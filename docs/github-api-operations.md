# GitHub API operations

Practical notes for using `gh` and `gh api` correctly when administering PRs
and issues. [PR and CI discipline](../AGENTS.md#pr-and-ci-discipline) states
the binding summary; this document owns the exact commands.

## Passing file content as a field

When passing a file's content to `gh api` (for example a PR body), use
`-F key=@path` (typed `--field`, which expands `@path`), not `-f key=@path`
(raw `--raw-field`, which sends the literal string `@path`). This is a `gh`
flag distinction, not platform-specific. Verify with
`gh pr view <n> --json body -q .body` after creating or editing a PR body
this way.

## Prefer REST endpoints over failing high-level commands

Avoid high-level `gh` commands when they fail by querying deprecated GraphQL
fields. In particular, `gh pr edit` may hit the removed Projects (classic)
fields even when changing unrelated metadata. Do not retry it; use the
operation-specific REST endpoint through `gh api` instead:

- `PATCH repos/{owner}/{repo}/pulls/<number>` for PR title, body, or base
  changes.
- The issue labels POST and per-label DELETE endpoints for label additions and
  removals.
- The issue assignees POST and DELETE endpoints for assignee additions and
  removals.
- `PATCH repos/{owner}/{repo}/issues/<number>` for milestone changes.

Do not replace complete label or assignee arrays to perform an add or remove.
Verify the resulting metadata after the REST update.

## Bind merge mutations to the head

Every direct or asynchronous merge request must name the exact reviewed or
approved-waiver head SHA as an atomic precondition. A prior read is not enough:
without the precondition, a concurrent write-access push can transfer the
mutation to an unreviewed head.

Keep GitHub auto-merge unarmed while required gates are pending. After green
preflight, perform the authorized direct merge with `--match-head-commit`:

```bash
gh pr merge "$pr_number" --squash \
  --match-head-commit "$head_sha"
```

The stacked-PR asynchronous merge endpoint uses its `sha` field:

```bash
gh api -X PUT "repos/{owner}/{repo}/pulls/$pr_number/merge-async" \
  -f merge_method=squash \
  -f sha="$head_sha"
```

When using GraphQL directly, set `expectedHeadOid` on `MergePullRequestInput`;
never omit it. Treat a mismatch as head movement and return to candidate
formation. If an auto-merge request exists from earlier workflow state, disable
it before a recovery mutation or head-moving push.
