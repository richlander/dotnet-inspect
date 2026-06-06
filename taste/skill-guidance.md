# Embedded skill guidance

Use this when editing `skills/dotnet-inspect/SKILL.md`.

## Purpose

The embedded skill is the authoritative agent guide for `dotnet-inspect`. It should teach reliable workflows, syntax guardrails, output-shape expectations, and high-value examples. It should not be a changelog or an exhaustive command manual.

## Good

```md
For full public signature or overload inventories, start with `type Type --package Foo --shape`; it gives the clean declaration shape with parameter names, nullable annotations, defaults, and generic parameters.
```

Why it works:

- It maps a common task to one best first command.
- It explains why that command is efficient.
- It avoids sending agents through lower-level source/IL workflows unnecessarily.

```md
`-n N` and numeric shorthand like `-6` work like `head`; `--tail N` works like `tail`; add `--rows` to make head counts cap Markdown table data rows instead of output lines.
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
- Prefer fidelity guidance: `Original Source` is SourceLink-backed original source when available; `Decompiled Source` is lowered C# readable best-effort; raw/annotated IL is highest fidelity.

```md
Use `--plaintext` for text-only output.
```

Why it is bad:

- All CLI formats are text output.
- Do not feature `--plaintext` in primary skill guidance unless a concrete agent workflow benefits from it.

## Rules

- Lead with the best first command for the task.
- Prefer workflow guidance over implementation inventory.
- Use familiar CLI analogies when they clarify behavior (`--oneline` like `docker images`, `-n`/`--tail` like `head`/`tail`).
- Describe `-D`/`-S` as the uppercase cross-command query namespace when explaining discovery and section selection.
- Explain `--preview` as the single prerelease opt-in alias in the skill, even when other aliases exist.
- Keep SourceLink/PDB wording precise: PDBs carry SourceLink data; they are not SourceLink themselves.
- Keep Signals row ownership clear; avoid describing rows as belonging to multiple sections.
- Update examples when output shape changes, especially compact context rows and selected-section output.
