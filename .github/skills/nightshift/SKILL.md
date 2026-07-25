---
name: nightshift
description: >-
  The Nightshift orientation: what Nightshift is (Turnstile, Nightshift,
  Octoshift), the unit of work (an "order" = one landable PR), the five roles and
  what each owns, and how they coordinate through state rather than chat. Start
  here, then run `nightshift skill <role>` for your role's operating skill. Use
  this when asked what Nightshift is, how the roles fit together, or to get
  oriented before joining a shift.
---

# Nightshift

Nightshift drives many units of work to completion in parallel by shifting the
mechanical oversight onto the night-shift coordinator — so the people who set
direction don't spend their attention on it. Work is built and reviewed to a clean
bar on a **night shift**, while direction-setting and the merge decision stay
deliberate acts on the **day shift**. It is a **loose harness**: it sits above many
coding-agent harnesses and coordinates them through shared state — leases, orders,
directives — never their inner loops. **Loosely overlaid; strictly gated.**

You are reading the orientation. When you know your role, get its operating skill:

```bash
nightshift skill <role>   # planner | coordinator | worker | builder | reviewer
```

Run `nightshift skill` with no argument to reprint this orientation. (`planner` and
`coordinator` share one skill; a Planner is a Coordinator scoped to registering work.)

## The three layers

- **Turnstile** — a credential-free coordination kernel (kv, leases, watch) over a
  local Unix socket. No GitHub, no network, no auth.
- **Nightshift** — the shift mechanics on top of Turnstile: plans, orders, the ready
  set, claims, and landing.
- **Octoshift** — the optional bridge that watches GitHub merges and drives landing
  back into Nightshift.

## The unit of work

The unit of work is an **order**: one **landable PR**, the atomic unit of both claim
and merge, bound to at most one GitHub issue. A **plan** (`orders.json`) is a DAG of
orders for a feature; `after` edges express which orders must land before others can
start. An order is claimed by one worker, built and reviewed to a clean adversarial
verdict, opened as a PR and cleared by the coordinator, merged, and **landed** — at
which point its dependents open.

## The five roles

**Roles are responsibilities, not people.** A role names *what* work is done and its
boundaries — build versus review, who writes to GitHub, which model runs — not how
many processes call `nightshift`. Any role can be filled by a person or an agent, and
one session can fill several. Most sessions on a machine are workers.

| Role | Owns | Skill |
|---|---|---|
| **Product Manager** | The expanding shape of the product: new issues, taste, where features must be re-shaped or composed. Sets direction. | — |
| **Planner** | Turns intent (often issues) into orders and registers them with nightshift. | `nightshift skill planner` |
| **Coordinator** | Runs the shift: registers plans, keeps the ready set live, owns the GitHub surface (push, PRs, the one clearance note), first-level escalation, lands merged orders. | `nightshift skill coordinator` |
| **Worker** | Claims one order and takes it to a reviewed branch (handed back for the coordinator to push) — building it *and* reviewing it, usually via subagents. | `nightshift skill worker` |
| **PR Lander** | Holds merge authority; keeps sequenced PRs flowing. | — |

The **Worker** builds and reviews by spawning subagents (an optimization that
preserves its context window and supplies model diversity). Those two
responsibilities each have their own skill a worker points a subagent at:

- **Builder** — build one order into commits on its branch: `nightshift skill builder`
- **Reviewer** — review one order's diff read-only and report inward: `nightshift skill reviewer`

## How the roles work together

Because roles can be distinct sessions — or distinct machines — they **do not talk
directly**. They coordinate through Nightshift/Turnstile state: an escalation a worker
raises surfaces to the coordinator as state, read off the board like every other
signal. Every instruction a worker needs arrives as the **return value of a gate call**
(`next`, `check`), never as chat.

The life of one order:

1. A Product Manager files an issue; the Planner turns it into orders and registers
   them (`add`/`plan`).
2. The Coordinator seeds the specs and the ready set. A Worker claims a ready order
   with `next`.
3. The Worker **builds** it (a builder subagent on model A) and **reviews** it (a
   reviewer subagent on model B ≠ A), fixing and re-reviewing until **two clean reviews
   from two different models** on the same final head.
4. The Worker hands the branch back and `release`s it done, with the review attestation.
5. The Coordinator pushes the branch, opens/updates the PR, and posts the **one**
   clearance note.
6. The PR Lander merges. The Coordinator **lands** the order, and its dependents open.

## The invariants that hold regardless of the split

- **A Worker is always a separate instance** from the Coordinator/Planner. The
  coordinator never claims, builds, reviews, or spawns workers — workers `join` and
  `next` on their own.
- **The builder never reviews its own work.** Building produces a committed branch;
  grading your own homework is not a review.
- **Every change clears the gate:** two clean reviews from two **different** models on
  the final head — governance and docs PRs included.
- **Only the coordinator writes to GitHub** — every push, PR, and the single clearance
  note. Build and review roles run with no write access to origin.

## The CLI contract

Every agent-facing verb signals two ways at once: an **exit code** (branch on it
without parsing stdout) and a **human-readable token on the first line of stdout**
(`WORK` / `NOWORK` / `OK` / `HALT` / `DRAINING` / `QUERY` / `FENCE_STALE` and
siblings). Your role skill tells you which verb you wait on and how to wait without
stalling or polling.

## Where to go next

- Your role's operating skill: `nightshift skill worker` (or `planner`,
  `coordinator`, `builder`, `reviewer`).
- Design docs live under `docs/design/` — `nightshift-vision.md`, `nightshift-spec.md`,
  `workflow.md`, `turnstile.md`, `octoshift.md`.
