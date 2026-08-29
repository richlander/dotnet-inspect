---
name: dotnet-inspect-package-skills
description: Discover and use version-matched agent skills from NuGet packages, and persist selected skills in a repository only when the user explicitly requests it.
---

# dotnet-inspect: use package skills

Discover version-matched package skills, inspect their inventory, and load the
ones relevant to the current task. This default workflow is agent-driven and
does not change the repository. Persist skills only when the user explicitly
asks for repository installation.

## Default: use skills without changing the repository

Restore or build first when dependencies changed; `project` only reads the
existing `project.assets.json`.

First ask whether the restored direct dependencies expose any skills:

```bash
dnx dotnet-inspect -y -- project path/to/project -S Skills --count
```

If the count is nonzero, ask for the inventory. It includes the resolved package
version, package-relative path, skill name, and description:

```bash
dnx dotnet-inspect -y -- project path/to/project -S Skills --jsonl
```

Use this view first because its package versions match the code. Compare the
descriptions with the task and code, then request each relevant skill by its
displayed row:

```bash
dnx dotnet-inspect -y -- project path/to/project \
  -S Skills --print --row 2 --bare
```

Request several skills as a group by issuing one independent command for each
selected row. Keep each result separate; `--bare` intentionally carries no
multi-document boundary.

```bash
dnx dotnet-inspect -y -- project path/to/project \
  -S Skills --print --row 2 --bare
dnx dotnet-inspect -y -- project path/to/project \
  -S Skills --print --row 5 --bare
```

The agent may perform this entire discovery and loading workflow without asking
the user. Reading package guidance changes agent context, not repository state.
Treat every inventory row as a candidate; do not use the first row as a proxy
for the package.

For a package that is not restored in the project, use an exact package
coordinate. First ask whether it has skill documents:

```bash
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  -S "Package skill files" --count
```

Then list the paths and inspect the YAML header of each candidate row before
requesting the full document. Together, those headers form the package-only
inventory:

```bash
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  -S "Package skill files" --paths
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  -S "Package skill files" --print --frontmatter --row 1 --bare
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  -S "Package skill files" --print --row 1 --bare
```

Do not use an unpinned package query when the repository consumes a specific
version.

dotnet-inspect validates restored-project skill names and descriptions. In that
normalized inventory, YAML values that require containment are reported as
`[Text omitted: required containment]`. A selected YAML header or full skill
document that requires containment is replaced in full by the same text through
stdout, structured output, and file destinations. Reversible visible escape
spellings may still disambiguate literal package content; they are not
instructions to decode before use. A NuGet dependency is provenance, not proof
that agent instructions are safe.

## User-requested workflow: persist repository skills

Follow this workflow only when the user explicitly requests repository
installation. It changes tracked state and requires a merged pull request to
persist.

The target repository owns the installation regime. Inspect its contributor
instructions, existing skill layout, and active agent harness before choosing a
destination. Common roots include `skills/`, `.agents/skills/`,
`.github/skills/`, and `.claude/skills/`.

`dotnet-inspect project ... -S Skills` applies the same checks and refuses
missing or noncompliant metadata. Do not recover a refused skill by sanitizing
or renaming it. For an accepted skill, preserve that validated name as the
leaf directory under the repository-selected root.

For example, Markout itself keeps its skills under `skills/`, so its output
formats skill belongs at `skills/markout-output-formats/SKILL.md`. Create that
directory and confirm the package-local row from the exact package coordinate.
Then either redirect the contained stdout payload:

```bash
mkdir -p skills/markout-output-formats
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  -S "Package skill files" --paths
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  -S "Package skill files" --print --row 4 --blob --bare \
  > skills/markout-output-formats/SKILL.md
```

Or ask dotnet-inspect to write the same contained payload:

```bash
dnx dotnet-inspect -y -- package Markout@0.35.2 \
  -S "Package skill files" --print --row 4 --blob --bare \
  --output skills/markout-output-formats/SKILL.md
```

`--blob` retains authored GitHub link shapes. Preserve each packaged document as
its own skill rather than combining, renaming, or decoding contained text. If
the skill references sibling scripts, references, or assets, inspect the package
subtree and persist those beside `SKILL.md` too.

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
