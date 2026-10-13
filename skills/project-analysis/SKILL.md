---
name: dotnet-inspect-project-analysis
version: 0.4.0
description: Review a project, package, or library as a 30-second elevator pitch or an evidence-bounded deep dive — learn it, weigh dependency risk, map complexity and clustering, or pursue a topic such as performance, unsafe code, or ownership.
---

# dotnet-inspect: project analysis

Use this skill when the user wants a review rather than one fact:

> Give me the one-page summary of this project, package, or library — and tell
> me what I need to know now.

The tool supplies metrics; you supply the judgment. Binary analysis is not an
oracle. Say what the evidence shows, label what you infer, invite the user's
design context, and let that context sharpen or soften what you report.

```bash
dnx dotnet-inspect -y -- <command>
```

## Let the product describe itself

Find routes with the installed product rather than from memory, and copy every
path, section name, and facet key from its output:

1. `explain "<task text>"` searches installed capabilities and returns exact
   resource paths with their production commands.
2. `<command> -D` lists sections and categories; on `library` (add
   `--namesake-library` for a package), each row carries
   a `Path` you pass unchanged to `explain <path>` for its columns, formats, and
   shape. `explain library/sections` lists the complete Library catalog,
   including analysis sections that no category lists, such as
   `Library Metrics`, `Dependency Structure`, `Name Families`,
   `Architectural Families`, `Clone Candidates`, and `Member Metrics`.
3. `<command> -Q "<Section>"` lists the facet keys and operators that
   `--where`, `--order-by`, and `--top` accept for that section.
4. For a vocabulary, query-space, or query-facet path, `explain <path> .hal --json`
   embeds values, rules, and related resources; read `_embedded` before
   following `_links`.
5. `-E .tips` adds bounded follow-up gestures on stderr after ordinary output.

Explanation coverage is still growing. Where it stops, use the sections this
skill names and continue:

| Gap | Use instead | Tracked by |
| --- | --- | --- |
| Capability search covers Package Query only | `library -D`, `explain library/sections` | #10068 |
| `depends` and `diff` have no explain resources | `depends -D`, `diff -D` | #10069, #10070 |
| `depends --explain`, `diff --explain`, and `diff -Q` are read as positional input | Do not use them; read the error as "not offered" | #10067 |
| Section explanations list columns but not meaning | The boundaries stated below | #10077 |
| `explain -h` fails | Root `--help` and this section | #10076 |

## Choose a scenario, depth, and subject

| Scenario | The user wants to know |
| --- | --- |
| **Learn** | What is this, how is it organized, and where do I start reading? |
| **Dependency risk** | What upstream change have we not integrated, and what stands in the way? |
| **Complexity and clustering** | Are type, namespace, and package relationships conventional, or tangled? Is a dependency used so thinly it could be inlined? |
| **Topic deep-dive** | Does performance, unsafe code, ownership, or async usage deserve attention? |

**Depth.** A *quick review* is an elevator pitch, bounded by time. A *thorough
review* is an argument, bounded by evidence. Start quick unless the user asks
for depth or names one thread.

**Subject.** A *snapshot* reviews one state: `main:HEAD`, a restored project, or
one package version. A *delta* reviews a change: a topic branch against main,
or package `A` against `A'`. For a delta, measure each metric on both sides and
report each concern as introduced, pre-existing, resolved, or worsened. Use
`diff` where it covers the metric; otherwise run the metric twice and join by
exact identity.

## Quick review: the elevator pitch

**Budget:** about 30 seconds of tool time, excluding any build, in about three
rounds: triage, a parallel bounded read, and an optional confirmation.

**Where the cost goes.** Dependencies get cheap checks only. Expensive analysis
runs on the target library, never across its dependencies. Complexity, naming,
and clustering wait for a thorough review; they are only worth doing well.

| Question | Evidence |
| --- | --- |
| What is it? | `library --package Id@v` default output for the namespace and type tree; `package Id@v -S "Package Info,README"`; `project <path> -S Skills` |
| Removable references | `depends --project <path> --tfm <restored-tfm> -S Pruning` |
| Far behind upstream | `-S "Latest Versions"` in the same `depends` call |
| Breaks between current and latest | `diff --package Id@current..latest --breaking`, per retained dependency |
| Changed URLs in literals | `diff --package Id@current..latest --analysis string-literals --where "Literal contains http"` |
| Known vulnerabilities | `package Id@current -S Signals`: the `Known vulnerabilities` row is a checked count with its source; `-S Vulnerabilities` lists advisories only when there are some |
| Unsafe in dependencies (context, not a verdict) | `library --package Id@current --namesake-library -S Signals`, plus `-S "Unsafe Members" --count` |
| Concerning patterns in the target | `library <target> -S "Unsafe Members,Resource Triage"` and `-S "Performance:*" --where "Priority>=high" --top 10` |

Run the per-dependency checks in one shell call with parallelism, for example
`xargs -P 8`. A restored four-package app takes about 1 s for Pruning plus
Latest Versions and about 7 s for every per-dependency check in parallel on a
cold cache.

**Validate before you narrate.** A result enters the story only after you check
it:

- read stderr, the exit code, and every completion or summary field; for
  `depends`, add `--json` and read `summary`, because Markdown omits the
  completion receipts;
- never read `0`, empty output, or exit code 1 as "checked, none found" —
  a failed section and an empty section can look alike (#10073), and a
  populated `Analysis Diff --changed` currently exits 1 (#10075);
- confirm `--tfm` names the project's restored target — Pruning echoes it as
  `Platform Target` — because a wrong target can look like success, and web
  apps need `--platform-family aspnetcore` for Pruning;
- for a package root, `--tfm` selects a dependency group by exact match, so a
  newer consumer framework can select nothing while Pruning reports `Complete`
  (#10071); prefer a restored project root;
- treat a zero from `package query --where "library-literal=…"` as unverified
  until the summary shows no `notApplicable` candidates (#10072); and
- check that numbers are plausible for the subject's size and domain.

`-n` limits the whole output, not each section (#10074); read the head of each
ranked section in its own call. `Top Leverage` ignores `--top`, so use `-n`
(#10085). For a package, add `--namesake-library` before asking for TSV or
JSONL rows; the package-wide view renders Markdown only.

**Report one page:**

- **What it is** — purpose, shape, size, main types, how the parts meet, and
  what it depends on.
- **Highlights** — what is distinctive or well made.
- **Lowlights** — each an *early sign* with its evidence and the thread that
  would confirm or dismiss it.
- **Know now** — anything that affects a decision the user is making today.
- **Can wait** — threads worth pulling, each phrased as a question. These are
  the thorough review's starting points.

## Thorough review: the explanatory deep dive

You are the join pivot. Run as many invocations as the evidence needs, join
row families by exact identity, and pull threads this skill does not
anticipate, as long as every claim cites the command that produced it.

Highlights are usually easy to notice and validate: check each once. Spend the
time on lowlights, taking each as far as the evidence allows:

| Stage | Meaning | Typical evidence |
| --- | --- | --- |
| Early sign | A metric stands out | Section outlier |
| Located | Exact Members and IL positions are known | `member`, Unsafe Operations, IL |
| Traced | How far it reaches is understood | Call graph, Top Leverage, exception regions, ownership paths |
| Dated | When it appeared is known | `diff --history`, Analysis Diff |
| Explained | Source and intent are understood | Source, decompiled source, design context |

End each lowlight as **confirmed**, **dismissed**, or **open**; for an open one,
name the missing evidence. Stop when every pursued lowlight has an outcome or is
blocked by a product gap, not when a call count is reached.

## Scenarios

### Learn

**Quick:** identity, purpose, README and package skills, public surface shape,
`Integrations`, and direct dependencies.

**Thorough:** `Name Families` and `Architectural Families` for naming and
structural roles; `Top Leverage` for the Members everything else reaches;
type hierarchy and `Async Methods` for interaction patterns; then a focused
`member … -S "Call Graph" --tree` for one central path. Load
`skill relationships` for calls and `skill decompiler` when the question needs
code.

### Dependency risk

Pruning and updating pull in opposite directions; resolve them in order:

1. **Remove what the platform supplies.** A `PlatformDelegation` row is a
   reference to delete, never to update. Upgrading such a reference to a newer
   version defeats pruning: harm for no benefit.
2. **Weigh updates for what remains.** Read `Current`, `Latest`, `Behind`,
   `Majors Behind`, and `Latest Compatible` together. `Latest Compatible: No`
   means the latest version offers no compile group for the target framework,
   and `NoAssets` means it has no compile assets at all; neither is an API
   verdict.
3. **Name the impediments.** For each candidate update, `diff --breaking`
   gives assessed breaks; `-S Changes` keeps the complete API population,
   including changes without a classification. Add `-S "Analysis Diff"` for
   unsafe, allocation, and throw shifts, and the string-literal analysis for
   changed endpoints.

Load `skill compatibility` for diff and history, and `skill signals` for
provenance, signatures, and vulnerabilities.

### Complexity and clustering

Thorough only. Start with `Library Metrics` for implementation volume,
distribution, and concentration; `Dependency Structure` for namespace cycles
and levels; `Architectural Families` and `Clone Candidates` for repeated
structure; and `Member Metrics` for outliers. Complexity is normal-flow
cyclomatic complexity over compiled IL: not authored intent, and not a
maintainability score.

Trace a surprising namespace edge to its types with
`type <T> --package Id@v --all -S "Called Types"`, and to call sites with
`member <T> --all -m <name> -S Callers`. Non-public and obsolete code needs
`--all` in `type`, `member`, and `diff`; without it, `type` and `diff` report
"not found" or a selector error rather than a visibility hint
(#10081, #10086).

Read these with care until their owners close the gaps:

- `Dependency Structure` has no JSON form, and `--json` drops it silently
  (#3748); use Markdown or TSV. Its level and cycle labels are not yet
  explained (#10077); confirm a cycle from mutual edge rows.
- `Member Metrics` has no ranking operators (#10083); rank its rows locally.
- Architectural Families can include compiler-generated names such as `c`
  and `d` (#10082); set them aside.
- An empty `Clone Candidates` result carries no receipt (#10080); report it
  as not established, not as "no clones".

For a dependency that may be used too thinly to justify itself, run
`graph packages --package Consumer@v --package Provider@v --tfm <tfm>` and
read how many direct-use clusters and distinct Members cross the boundary.
Clustered dependency communities are not yet product evidence (#8406); do not
simulate them from layout.

### Topic deep-dive

- **Performance:** intersect `Top Leverage` with `Performance:*` rows. Keep
  Priority separate from Confidence; static evidence names candidates, and a
  benchmark or trace confirms them. Load `skill performance`.
- **Unsafe and interop:** `Unsafe Members` and `P/Invoke Methods`, judged
  against the subject's domain: a codec or interop layer needs what a business
  library does not. Load `skill correctness`.
- **Ownership:** `Resource Triage` (listed as `Array Pool Escapes`) emits
  candidates but has no positive completion receipt (#7553), and it currently
  fails with `InterfaceApplicationIncomplete` on some pool-heavy libraries
  (#10084). Always report pooled-resource coverage as unverified, and treat
  `pool-churn-on-exception` as a hardening candidate, not a proven leak. When
  it fails, trace rent and return sites by hand: `member … --all -S "IL,Exception
  Regions"` on the renting and returning Members, then their callers.
- **Dating a change:** pairwise `diff -t T -m M --all --analysis <name>
  -S Transitions` proves an adjacent pair; `diff --history … --finding
  analysis.<name>` locates the window. Start a history range at a version
  where the Member exists, because a missing Member stops evaluation (#7805).
- **Async:** `Async Methods` for the surface, `Performance: Sync Calls in Async`
  for blocking inside async paths.

## Compose the report

Classify each statement as **fact** (owner-issued evidence), **derived
observation** (a reproducible join or calculation), **interpretation**
(labeled), or **hypothesis** (with its next probe). Join and cite by the
strongest identity available: package and version, TFM, assembly, Type key,
stable Member selector, or IL offset — never a display label.

Close every review by offering its open questions and asking what design
context the user can add.

When a review needs evidence the product does not issue, record the subject,
command, expected and actual result, diagnostics, and timing; file one focused
issue against the owning component; and continue with other evidence. Never
parse presentation as a substitute or report partial data as complete.
