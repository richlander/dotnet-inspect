# Embedded skill guidance

Use this when editing user-facing product skills under `skills/`, especially
`skills/dotnet-inspect/SKILL.md`, or evaluating their discovery tradeoffs.

## Purpose

The embedded skills are the authoritative current agent guides for
`dotnet-inspect`. They should teach reliable workflows, syntax guardrails,
output-shape expectations, and high-value examples. They should not be a
changelog or an exhaustive command manual.

## Breadth in skills, depth in evidence

Skills own capability breadth; the evidence model owns depth. A reader should
recognize the questions the tool can help answer, how far the investigation
can go, and a productive entrance without first knowing the catalog's terms.
Advertise depth without documenting every step needed to reach it.

A capability signpost names the question, the evidence it can lead to, a best
first command or discovery route, and any essential interpretation boundary.
For example, "compare APIs" alone hides implementation, analysis and literal
differences; listing every Diff flag instead duplicates the reference manual.
Describe those investigative possibilities and give one useful entrance.

The root skill maps the breadth. Focused skills retain domain judgment,
workflow composition, stopping rules and proven recipes. Installed explanation,
section/query discovery and inspection results supply detailed bindings,
values, prerequisites, provenance, coverage and continuations. Do not require
an explanation detour when a known command already answers the question, or
remove a working recipe while its required depth is not discoverable.

Judge guidance first by question coverage and useful completion. Cost comparisons
must preserve material findings and scope qualifications, and account for all
guidance read and recovery work. Smaller serialized content alone is not
evidence of a more efficient agent.

### Evaluate the discovery premium

A well-written, evaluated skill recipe is the fast-path baseline. Evidence is
not expected to beat it, and call-count parity is not the goal. Exhaustive
recipes for every capability would increase consumed guidance and duplicate
changing CLI details, making guidance costly to maintain and prone to drift.

For controlled comparisons on the same task and tool build, with equal
correctness and completion, up to 25% additional calls through evidence is
excellent; up to 50% is satisfactory. An eight-call skill baseline permits ten
calls for excellent and twelve for satisfactory. Count guidance, discovery,
execution and failed recovery calls consistently; these bands are not latency
budgets or context-token measurements. An acceptable premium can justify
removing mechanical recipe content, weighed against task frequency and
maintenance cost. Retain capability signposts, judgment and interpretation
boundaries.

Representative controlled pilots are sufficient to establish the baseline for
this strategy. Do not require a pilot for every CLI command, section or
capability, or a fresh pilot for every skill edit or recipe removal. The
discoverability and correctness requirements above still apply.

An observed task with outlier discovery or recovery cost may justify a
targeted pilot to understand the challenge and inform whether to improve the
evidence path or retain a recipe; it is not an automatic prerequisite. Do not
expand the evaluation campaign merely to fill the capability inventory.

## Current guidance is the compatibility layer

Product skills are the primary compatibility mitigation for this fast-moving
agent-focused CLI. The checked-in skill and the behavior it teaches move
together: an agent asks the current binary for current guidance rather than
relying on yesterday's flags or defaults.

Update every affected product skill in the same change as a command, flag,
default, workflow, or output-shape change. Re-run examples against the changed
tool. Do not retain obsolete CLI syntax solely because an earlier skill taught
it, and do not describe migration history; replace it with the current best
workflow. A stale shipped skill is a compatibility failure even when an old
invocation still happens to work.

## Good

```md
For full public signature or overload inventories, start with `type Type --package Foo --tree`; it gives the clean declaration tree with parameter names, nullable annotations, defaults, and generic parameters.
```

Why it works:

- It maps a common task to one best first command.
- It explains why that command is efficient.
- It avoids sending agents through lower-level source/IL workflows unnecessarily.

```md
`-n N` and numeric shorthand like `-6` work like `head`; `--tail` takes the same count from the end, like `tail`; `--rows N` caps Markdown table data rows instead of output lines, and `--rows 2..10` names the rows to keep.
```

Why it works:

- It links tool behavior to familiar CLI concepts.
- It teaches one important modifier without listing every option.

## Bad

```md
Recent decompiler work improved collection expression lowering and pointer lowering.
```

Why it is bad:

- It inventories implementation changes instead of teaching what agents can rely on.
- Prefer fidelity guidance: `PDB Source` is checksum-matched source selected by Portable PDB evidence when available; `Decompiled Source` is lowered C# readable best-effort; raw/annotated IL is highest fidelity.

```md
Use `--plaintext` for text-only output.
```

Why it is bad:

- All CLI formats are text output.
- Do not feature `--plaintext` in primary skill guidance unless a concrete agent workflow benefits from it.

## Rules

- Lead with the best first command for the task.
- Prefer workflow guidance over implementation inventory.
- Use familiar CLI analogies when they clarify behavior (`--table` as pretty-printed rows, `--tsv` as stable tab-separated rows, `-n`/`--tail` like `head`/`tail`).
- Describe `-D`/`-S` as the uppercase cross-command query namespace when explaining discovery and section selection.
- Explain `--preview` as the single prerelease opt-in alias in the skill, even when other aliases exist.
- Keep SourceLink/PDB wording precise: PDBs carry SourceLink data; they are not SourceLink themselves.
- Keep Signals row ownership clear; avoid describing rows as belonging to multiple sections.
- Update every affected product skill in the same PR as the behavior it teaches.
- Update examples when output shape changes, especially compact context rows and selected-section output.

## User-facing vs. repo-local skills

Keep user-facing product skills and repository-maintainer skills separate.
[Repository workflow](../docs/repository-workflow.md#documentation-ownership)
states the binding separation; this section owns the mechanics.

- `skills/` contains user-facing guidance shipped in the dotnet-inspect binary.
  Use these skills when consuming the published tool or when a product change
  needs its user-facing commands, examples, and expectations reviewed. Do not
  select them merely because an agent is maintaining this repository; they are
  product artifacts, not contributor runbooks.
- `.github/skills/` and `.claude/skills/` contain repo-local guidance for
  contributors and agents (release, CI, corpus maintenance, and other
  repository operations). Do not register or embed these skills in the
  product, and keep repository operations out of the user-facing `skills/`
  tree.

### Registering a new focused product skill

When adding a focused product skill under `skills/`, register it in
`SkillCommand.Skills` and add an `EmbeddedResource` line for it in
`src/DotnetInspect.Cli/DotnetInspect.Cli.csproj`; the embeds are enumerated per
skill. `FocusedSkillFilesRegistryAndEmbeddedResourcesAgree` keeps the skill
directories, runtime registry, and embedded resources equal. Its YAML
frontmatter `description:` is the single source of truth for the generated
skill listing.
