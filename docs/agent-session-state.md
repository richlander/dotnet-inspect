# Agent session state

[Session theme and resume](../AGENTS.md#session-theme-and-resume) and
[Making your work findable](../AGENTS.md#making-your-work-findable) state the
binding rules. This document owns the session-theme lifecycle, post-merge
handoff, tmux mechanics, and the reasoning behind them.

## Establish the session theme

Every agent session has one theme: a concise sentence describing the sustained
purpose of the work. Use the user's wording when they supply one; otherwise
infer it from the requested work and state it visibly before proceeding:

```text
Theme: Make long-running agent sessions easy to recognize and continue.
```

The theme is not the current command, branch, or PR title. Keep it stable across
related tasks and PRs until the user changes it or confirms that the theme is
complete. Derive a short, lowercase, hyphenated slug such as
`agent-session-clarity` for templates that require a space-free value.

## Resume a session

The transcript survives a resumed session; repository and PR state may not.
Before continuing:

1. Restate the theme, using the transcript or the user's latest direction.
2. Confirm the worktree, branch, and head from git. Fetch the effective base and
   re-check the PR per
   [Canonical round flow](../AGENTS.md#canonical-round-flow). Do not pull or
   rebase a pushed branch to catch up.
3. Rename the window, update the pane title, and re-announce the PR as
   described below.
4. State which case applies:

- **Mid-stream:** continue, but handle conflicts, failed gates, or moved bases
  first. Do not revisit decisions already settled in the transcript.
- **Waiting on the user:** restate the full question and options, then wait.
- **Task complete:** state what landed and what proves it, then perform the
  [theme handoff](#complete-a-merge-with-a-theme-handoff).
- **Unclear:** explain what the transcript claims and what git shows, then wait.

## Complete a merge with a theme handoff

After every PR merge, relinquish PR ownership only after a visible theme
handoff:

1. Restate the theme in one or two sentences.
2. Propose the next concrete work that advances that theme, without starting it.
3. If no work remains, state that the theme is complete and ask whether to look
   for a new theme or take on ad-hoc work unrelated to the completed theme.

Use one of these response shapes:

```text
Theme: <one- or two-sentence restatement>
Next: <one concrete task that advances the theme>
```

```text
Theme: <one- or two-sentence restatement>
No work remains on this theme. Should I look for a new theme or take on
ad-hoc work?
```

## Detecting tmux

Everything below applies only inside a tmux pane. Test with `[ -n "$TMUX" ]`
before running any of it; `$TMUX` is set by the tmux client and is empty or
unset in a plain shell, an SSH session without tmux, most CI runners, and most
IDE-embedded terminals. `"${TMUX_PANE:?}"` itself is the safety net for the
common case where tmux is running but the variable is unexpectedly empty — it
is not a substitute for the `$TMUX` check, which guards the case where tmux is
not running at all. When `$TMUX` is unset, skip window naming and state
publishing outright: there is no window to rename and no window-scoped option
to attach state to, so silently continue the task without them rather than
erroring or improvising a substitute.

## Name the window for identity

```sh
tmux rename-window -t "${TMUX_PANE:?}" pr<number>
```

Always target `"${TMUX_PANE:?}"`; a bare command renames another window, and an
empty variable silently targets the current one — `tmux rename-window -t "" …`
does not error, it silently renames whichever window is *current*, which may be
someone else's. Rename the window, never the shared session. Use `pr<number>`,
or `i<number>` before a PR exists. Keep the name stable except for these
temporary suffixes:

| Suffix | Meaning |
| --- | --- |
| `-blocked` | waiting on a human decision |
| `-conflict` | in conflict recovery |

## Set the pane title for current activity

```sh
tmux select-pane -t "${TMUX_PANE:?}" \
  -T "agent-session-clarity: reviewing pr<number>"
```

The window name is stable identity; the pane title is live activity. Copilot
sets a title at startup, which is a useful baseline but often becomes stale.
Replace it at the start of work, after every resume, and at meaningful phase
changes. Prefix a short factual activity with the theme slug, such as
`agent-session-clarity: reviewing pr4405`,
`agent-session-clarity: running tests for pr4405`, or
`agent-session-clarity: waiting for checks on pr4405`; do not churn the title
for individual commands.

Always target `"${TMUX_PANE:?}"` for the same ownership reason as
`rename-window`. The pane title is human-facing status displayed by tmux; tools
must continue to read `@agent` and `@agent_state` rather than scraping it.

## Publish your state where tooling can read it

Update both window-scoped options whenever state changes:

```sh
tmux set -w -t "${TMUX_PANE:?}" @agent \
  "theme agent-session-clarity; round 6 on pr4405, waiting on CI"
tmux set -w -t "${TMUX_PANE:?}" @agent_state \
  "theme=agent-session-clarity pr=4405 head=595e5d4b round=6 reviews=1/2 blocked=4597,4611 rec=wait"

# Clear when this window no longer owns the PR.
tmux set -w -t "${TMUX_PANE:?}" -u @agent
tmux set -w -t "${TMUX_PANE:?}" -u @agent_state
```

**Publish state as single, separate commands.** Never wrap them in `if`, `&&`,
or a `for` loop. Publishing is the one thing that must never stop to ask
permission: an approval prompt on it blocks the agent on the very act of
reporting that it is blocked, and semi-autonomous work stops dead. A bare
`tmux …` matches an approval rule for `tmux`; `if [ … ]; then tmux … && tmux …;
fi` does not match it, because the command being judged is now the compound.
That difference has stalled real work.

The state must include `theme`, `head`, and either `pr` or, before a PR exists,
`issue`; add `round`, `reviews`, `blocked`, `waiting`, and `rec` when
applicable. Values contain no spaces. `rec` is `continue`, `wait`, `merge`,
`split`, `approve`, or `stop`. Clear both options when the window no longer
owns the work.

### `blocked` vs. `waiting`

Both are things you are waiting on, split by **who can act on them**:

- **`blocked`** takes issue or PR numbers only — things a person can open and
  prioritise, and that the next agent hitting the same wall can find instead of
  re-investigating it. If a flake blocks you and no issue exists, file one and
  cite it.
- **`waiting`** takes one or more comma-separated predicates a tool can evaluate
  against your `head`: `check:<name>`, `checks`, `merge`, or `review`. The wait
  ends only when every listed predicate clears. Use it when nothing is wrong
  and nothing is openable — a check that has not reported yet is not a defect
  and does not deserve an issue.

`rec=wait` is coherent when either is populated. `blocked=ci` is the specific
error this split exists to remove: it names nothing a person can open and
nothing a tool can evaluate, so it reads as a wait on nothing.

### Status-wait fields

When GitHub status is being acquired, publish `goal=advance|merge` and the
unresolved status predicates in `waiting`. During a bounded wait, also publish
`status-deadline=<UTC>` and at most one active `schedule=<id>`. Key that
schedule to the recorded `head`, `waiting`, `goal`, and deadline; cancel stale
runs and clear the ID before querying GitHub. Follow
[GitHub status queries](github-status-queries.md) for the request and
response contract and
[Status discovery](round-orchestration.md#status-discovery) for round
transitions and the 60-minute budget.

## Signal when you need a person

When blocked on a human decision, set a persistent `HELP` state and send one
best-effort nudge:

```sh
tmux set -w -t "${TMUX_PANE:?}" @agent \
  "theme agent-session-clarity; HELP: integrate main into pr4405, or close it?"
tmux display-message -d 10000 -t "${TMUX_PANE:?}" \
  "HELP pr4405 in w#{window_index}: integrate main, or close it?"
```

Send the nudge once, then stop and wait; the flag is not an answer. Clear `HELP`
as soon as the decision arrives. Use ordinary state for progress and completion.
