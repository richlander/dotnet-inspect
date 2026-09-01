# Embedded skill guidance

Use this when editing user-facing product skills under `skills/`, especially
`skills/dotnet-inspect/SKILL.md`.

## Purpose

The embedded skills are the authoritative current agent guides for
`dotnet-inspect`. They should teach reliable workflows, syntax guardrails,
output-shape expectations, and high-value examples. They should not be a
changelog or an exhaustive command manual.

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
For full public signature or overload inventories, start with `type Type --package Foo --shape`; it gives the clean declaration shape with parameter names, nullable annotations, defaults, and generic parameters.
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
[`AGENTS.md`](../AGENTS.md#task-specific-guidance) states the binding
separation; this section owns the mechanics.

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
`src/dotnet-inspect/dotnet-inspect.csproj`; the embeds are enumerated per
skill. `FocusedSkillFilesRegistryAndEmbeddedResourcesAgree` keeps the skill
directories, runtime registry, and embedded resources equal. Its YAML
frontmatter `description:` is the single source of truth for the generated
skill listing.
