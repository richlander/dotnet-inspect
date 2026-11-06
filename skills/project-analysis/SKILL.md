---
name: dotnet-inspect-project-analysis
version: 0.4.1
description: Review .NET projects, packages and libraries as a quick orientation or evidence-bounded deep dive; explain dependencies, structure, performance candidates and ownership concerns without treating metrics as verdicts.
---

# dotnet-inspect: Review .NET Projects, Packages, and Libraries

Use this skill when the user wants a review rather than one fact. The tool
supplies evidence; you supply judgment. Label inference and missing coverage.
Run `dnx dotnet-inspect -y -- <command>`.

## Command conventions

`<target>` is `--package Id@v --namesake-library`, `path/to/Lib.dll`, or a
restored project's built output. Select one exact Library when the analysis or
format requires it. Package-wide composition does not imply every section or
row format is supported.

Use known commands directly. For unfamiliar evidence, use
`explain "<question>"` and follow the returned resource and CLI binding.
`-D` discovers sections; `-Q "<Section>"` discovers supported predicates and
operators. Do not copy a displayed field into a filter without checking.

## Choose scope and depth

A *snapshot* reviews one state; a *delta* compares two. In a delta, label each
concern introduced, pre-existing, resolved or worsened. Preserve exact package
versions, TFM, Library and Member identities across the comparison.

Start quick unless the user asks for depth or a specific thread. A review is
not an inspect-everything checklist.

## Quick review: the elevator pitch

Aim for about 30 seconds of tool time, excluding builds. Dependencies get
cheap checks only; expensive analysis runs on the target, never across its
dependencies by default. Pursue one consequential candidate rather than every
available section.

```bash
dnx dotnet-inspect -y -- package Id@v -S "Package Info,README,Signals"
dnx dotnet-inspect -y -- library --package Id@v
dnx dotnet-inspect -y -- depends --project <path> --tfm <restored-tfm> \
  -S "Pruning,Latest Versions" --json
```

The bare `library --package Id@v` output gives a public namespace/Type tree.
`project <path> -S Skills` discovers version-matched package guidance.
For a supplied file without restore, use
`depends --project-document ./App.csproj --envelope` or name the exact
`project.assets.json`. Authored constraints are not resolved versions;
no evaluation, restore or implicit acquisition is performed.

**Validate before you narrate.** Preserve exit status, stderr, limitations and
completion receipts; never read `0`, empty output, or exit code 1 as checked
absence. Use structured output when ordinary presentation omits completion.
Verify the actual restored TFM and platform family before interpreting pruning.

Report one page: purpose and shape, distinctive strengths, early concerns
with evidence, what affects today's decision, and questions that can wait.

## Thorough review: the explanatory deep dive

An evidence-bounded review follows consequential lowlights to an outcome:

| Stage | Meaning | Entrance |
| --- | --- | --- |
| Early sign | A metric stands out | One focused Library section |
| Located | Exact Members and IL positions are known | Member inventory, IL, unsafe operations |
| Traced | Reach and context are understood | Calls, callers, ownership paths |
| Dated | A change window is known | Diff and history |
| Explained | Implementation and intent are understood | Source, decompilation, design context |

End each pursued lowlight **confirmed**, **dismissed**, or **open**; name the
missing evidence for open ones. Stop when those outcomes are established, not
when every metric has been read.

### Learn

```bash
dnx dotnet-inspect -y -- library <target> -S "Library Metrics"
dnx dotnet-inspect -y -- member <Type> --package Id@v -S "Member Index"
dnx dotnet-inspect -y -- member <Type> --package Id@v <Selector> -S Signature
```

Use Member Index to choose a stable selector, then Signature to establish
return types and actual declarations. A name or canonical lookup signature
alone does not prove a factory/builder relationship.
Load `skill relationships` for how the parts connect.

### Dependency risk

1. **Remove what the platform supplies.** `-S Pruning` compares against one
   exact platform. A `PlatformDelegation` reference is a removal candidate,
   never to update merely because a newer package exists.
2. **Weigh updates for what remains.** `-S "Latest Versions"` gives version
   lag and asset compatibility, not API compatibility.
3. **Name the impediments.** `diff --package Id@current..latest --breaking`
   gives assessed breaks; `-S Changes` retains the full API change population.
   Analysis and implementation comparisons can expose other migration costs.

Load `skill compatibility` for version comparison and `skill signals` for
advisories and provenance. Changed URLs in literals can matter: discover
`explain "compare string literals"` and read the returned binding's predicate
rules before executing. Changed strings do not prove network behavior.

Use restored roots for target-aware dependency conclusions. A package manifest
group, a consumer target and platform-provided references are different
evidence. Preserve failed or incomplete pruning even if graph rows exist.

### Complexity and clustering

```bash
dnx dotnet-inspect -y -- library <target> -S "Dependency Structure" --json
dnx dotnet-inspect -y -- library <target> -S "Architectural Families"
dnx dotnet-inspect -y -- library <target> -S "Clone Candidates"
```

Follow installed section explanations for namespace cycles, levels and
explaining Type edges. Join by typed identities and exact Library, never by
namespace-prefix guesses or display labels. Compiled-IL complexity is not
authored intent or a maintainability score. Compiler-generated families need
interpretation; partial clone results do not prove absence.

For thin dependency usage, start with `graph packages` over the two exact
Packages and target framework, then follow direct-use clusters. A small call
surface is evidence for investigation, not sufficient reason to inline code.

### Topic deep-dive

```bash
dnx dotnet-inspect -y -- library <target> -S "Top Leverage"
dnx dotnet-inspect -y -- library <target> -S "Performance:*" \
  --where "Priority>=high" --top 10
dnx dotnet-inspect -y -- library <target> -S "Unsafe Code"
dnx dotnet-inspect -y -- library <target> -S "Resource Triage"
```

Run focused sections separately when limits or failure visibility matter.
Load `skill performance` for cost candidates, or `skill correctness` for
unsafe code and ownership. Static priority is not measured cost; unsafe code
needs domain context. Resource Triage has no positive completion receipt:
report pooled-resource coverage as unverified, not clean.

## Compose the report

Separate **fact**, **derived observation**, **interpretation** and
**hypothesis**. Cite the producing command and exact subject. Make reproducible
joins, retain diagnostics, and do not parse presentation to invent missing
identity or coverage.

If evidence is unavailable, state the gap and the next focused probe. Keep the
report bounded to the user's decision and invite design context where it
could change the interpretation.
