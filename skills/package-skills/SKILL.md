---
name: dotnet-inspect-package-skills
description: Acquire version-matched SKILL.md files from NuGet packages, select every skill relevant to the consuming code, and persist them where the target repository's agent harness discovers them.
---

# dotnet-inspect: acquire package skills

List the version-matched package skills, decide which ones match the code, and
write those skills into the repository. Keep this agent-led: dotnet-inspect
provides the inventory and document content; the agent owns selection and
persistence.

## List the restored dependency skills

Restore or build first when dependencies changed; `project` only reads the
existing `project.assets.json`. List every skill from direct dependencies with
its resolved package version, path, name, and description:

```bash
dnx dotnet-inspect -y -- project path/to/project -S Skills --jsonl
```

Use this view first because its package versions match the code. Inspect an
exact package coordinate when the dependency has not been added yet:

```bash
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  -S "Package skill files" --table
```

Do not install from an unpinned package query when the repository consumes a
specific version.

The package table exposes paths and sizes, not parsed skill identities or
descriptions. In this package-only flow, print every row and inspect its YAML
frontmatter before selecting or writing anything. Require a declared `name`
that is 1-64 lowercase ASCII letters, digits, or hyphens, with no leading,
trailing, or consecutive hyphens, and exactly matches the package directory
containing `SKILL.md`. Also require a declared `description` of 1-1024
characters that explains when to use the skill. Refuse the candidate if any
check fails. Never compose an output path from a package-authored value before
completing those checks.

## Select from the whole skill set

Treat every listed row as a candidate. `--print` requires `--row` when the
package contains multiple skills:

```bash
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  -S "Package skill files" --print --row 1 --bare
```

Do not use the first row as a proxy for the package. Compare every description
with the code and expected work, then inspect each plausible skill on stdout.
Keep separate skills separate so their descriptions can trigger independently.

Markout demonstrates the pattern: it ships the core `markout` skill plus
focused skills for conditional composition, output formats, built-in shapes,
and composite cells/cards. A consumer using several of those facets should
persist the core skill and each matching focused skill, not only the core skill
and not an arbitrary single row.

Review skill instructions and any referenced scripts before persisting them. A
NuGet dependency is provenance, not proof that agent instructions are safe.

## Install for repository discovery

The target repository owns the installation regime; the package does not.
Inspect repository instructions, its existing skill layout, and the active
harness, then follow any user direction. Common repository roots include
`skills/`, `.agents/skills/`, `.github/skills/`, and `.claude/skills/`.

`dotnet-inspect project ... -S Skills` applies the same checks and refuses
missing or noncompliant metadata. Do not recover a refused skill by sanitizing
or renaming it. For an accepted skill, preserve that validated name as the
leaf directory under the repository-selected root.

For example, Markout itself keeps its skills under `skills/`, so its output
formats skill belongs at `skills/markout-output-formats/SKILL.md`. Create that
directory, then ask dotnet-inspect to write the selected package document
directly to `-o`/`--output`:

```bash
mkdir -p skills/markout-output-formats
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  --path skills/markout-output-formats/SKILL.md --content --blob --bare \
  --output skills/markout-output-formats/SKILL.md
```

`--blob` retains authored GitHub link shapes instead of rewriting them for raw
content. Preserve the packaged document rather than combining, renaming, or
silently editing it. If the skill references sibling scripts, references, or
assets, inspect the package subtree and persist those beside `SKILL.md` too.

Do not duplicate the same skill into several roots; harnesses may discover
duplicate names and the copies will drift.

## Keep skills version-matched

Commit selected skills with the dependency change. Record the package id,
resolved version, and package-relative path in the commit or pull request. On
every package upgrade, rerun the project inventory, compare every installed
skill with its new packaged counterpart, add newly relevant focused skills,
and remove a skill only when the code no longer uses the behavior it covers.

Omit `-o`/`--output` to review the document on stdout before writing it. Load
`dotnet-inspect skill private-feeds` when exact reacquisition needs custom
sources or credentials. Use a local `.nupkg` path in place of `Package@Version`
for an unpublished package canary.
