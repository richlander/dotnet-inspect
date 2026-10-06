# Agent instructions

## Start here

`dotnet-inspect` builds robust, capable .NET inspection features that provide
foundational capabilities or compelling experiences and are conventionally
sound, delightfully new or unique, or both.

Read [`docs/README.md`](docs/README.md), then only the focused guidance relevant
to the task. Focused documents own their contracts and mechanics; this file is
the launch constitution and router.

## Repository contract

- Start from convention and the simplest sufficient design. Before changing
  behavior, state one normative owner and exact claim; follow
  [Development practices](docs/development-practices.md) and
  [Design scope](docs/design-scope.md).
- Follow the engineering, documentation, worktree, history, and platform rules
  in [Repository workflow](docs/repository-workflow.md). Never develop in the
  primary checkout, amend, or rewrite published history except while restacking
  your own stack under [Stacked PRs](docs/stacked-prs.md).
- Use the SDK, Release build, and focused `dotnet run` test commands in
  [Local development](docs/dev-environment.md); `dotnet test` runs no tests
  here. Match claims to gates using
  [Evidence and validation](docs/evidence-and-validation.md).
- Keep failures visible, defaults safe, expensive or network work explicit,
  product paths NativeAOT- and Browser/Wasm-compatible, and inspected
  assemblies unloaded. Approved exceptions belong in their owning design.
- Use repo-local contributor skills from `.github/skills/` or
  `.claude/skills/`; `skills/` contains product guidance shipped to users.
- Before any coding or review agent dispatch, follow
  [Agent model mapping](docs/agent-models.md).
- When you open or drive a PR, invoke the `steward` skill at publication,
  resume, and every CI, review, conflict, base-movement, check-in, merge, or
  close event.
- Never merge without explicit authorization for that PR. A label, clean
  review, green CI, or readiness comment is not authorization.
- All changed Markdown must pass `npx markdownlint-cli <files>`.

## Session visibility

At start, resume, and every meaningful completed block, emit:

```text
Theme: <stable session theme>. <completed block and current status>.
<Next action or tool-evaluable waiting condition>.
```

After merge, replace PR history with:

```text
Theme: <stable session theme>
Tracking issue: #<overall theme issue>
Next slice: <next independently mergeable work, or none>
Completed: <merged>/<currently planned> slices
Focus: [<short domains>]
```

Follow [Agent session state](docs/agent-session-state.md). Inside tmux only,
run each command separately at start, resume, and meaningful state changes:

```sh
tmux rename-window -t "${TMUX_PANE:?}" \
  "PR <number> | <domain> | <purpose>"
tmux select-pane -t "${TMUX_PANE:?}" -T "<theme>: <current activity>"
tmux set -w -t "${TMUX_PANE:?}" @agent \
  "theme <theme>; round <n>, candidate <n> on PR <number>"
tmux set -w -t "${TMUX_PANE:?}" @agent_state \
  "theme=<theme> pr=<number> head=<sha> round=<n> candidates=<n> usable=<n> findings=<n> reviews=<clean>/<required> rec=<action>"
```

Before a PR, substitute `Issue` and `issue=<number>`. Add `blocked`, `waiting`,
and status-wait fields when applicable; use `HELP` while awaiting a decision.
Clear both options when ownership ends. Announce the issue or PR and branch or
expected head at start, resume, and each round start.

## Review checkpoint

[Round orchestration](docs/round-orchestration.md) owns candidate locks,
eligibility, recovery, labels, review, status waits, and merge preflight. After
every completed round and before any next round or approval prompt, emit this
complete visible report; omit only empty `Blocked` and `Waiting` lines:

```text
Round <n> is complete for PR <number>.
- Theme: <one-sentence session theme>.
- Review model <model> was used for adversarial review.
- Design basis: normative owner <path#section> — <owned claim>; supporting
  <path and role for each model, adjacent contract, constraint, or consumer>.
- Review feedback is: [converging, diverging, neutral, clean].
- Round start: <datetime>.
- Round end: <datetime>.
- Round duration: <hours:minutes>

Progress: <pushed candidates> candidates, <usable reviews> usable reviews,
<accepted findings> accepted findings.
Reviews: <clean>/<required> clean — <status by reviewer>
Blocked: <PR or issue numbers not yours to fix>
Waiting: <comma-separated tool-evaluable predicates>
Recommendation: [continue, wait, merge, split into focused successors,
approve next rounds, stop (reason)]

Resolution: <completed changes or accepted next-round resolution plan>.
```
