---
name: dotnet-inspect-relationships
version: 0.1.0
description: Trace .NET package and Type dependencies, implementors, subclasses, extensions, calls and callers; use scoped graph evidence to explain connections without claiming unverified coverage.
---

# dotnet-inspect: Trace .NET Dependencies, Calls, and Type Relationships

Use this skill to ask what code depends on, who calls it, or how it integrates.
Run `dnx dotnet-inspect -y -- <command>`. Start with the known relationship;
use `explain "<question>"` only when the route is unfamiliar. `-D` discovers
sections, `-Q` query rules, and `--help` command-specific arguments.

## Dependencies

```bash
dnx dotnet-inspect -y -- depends \
  --package System.Text.Json@10.0.0 --tfm net10.0
dnx dotnet-inspect -y -- depends JsonSerializer \
  --package System.Text.Json@10.0.0 --tfm net10.0
dnx dotnet-inspect -y -- depends --project ./App.csproj \
  --tfm net10.0 -S "Pruning,Latest Versions" --json
```

Without a positional Type, roots are package declarations, restored projects,
libraries or nuspecs. With a Type, the relationship is between Types; do not
confuse it with package dependency resolution.

Use `-S Dependencies` for direct declarations, `-S "Dependency Hierarchy"`
for rooted traversal, and `--depth 1` for direct edges. `--tree` and
`--mermaid` change presentation, not traversal. Shared targets can appear under
several parents: those are distinct relationships, not duplicate packages.
Library `References` is direct assembly metadata; `Reference Hierarchy`
traverses references.

For dependency licenses, explicitly select `-S Licenses`:

```bash
dnx dotnet-inspect -y -- depends --project ./App.csproj -S Licenses --tsv
```

This inventories distinct dependency package coordinates, excluding explicit
roots. Inspect `package Id@version -S "Package Info"` for a root's own license.
License inspection may acquire dependency nuspecs, not package payload content.
Keep Failures, diagnostics and incomplete acquisition visible; missing license
evidence is not proof of no license.

`Pruning` needs the actual target framework. Use `--platform-family aspnetcore`
for a web project's platform comparison. `PlatformDelegation` identifies a
removal candidate, not an update candidate; an older platform version does not
authorize a downgrade. Read `Latest Versions` separately from API compatibility.
Load `skill project-analysis` for dependency-risk judgment.

## A supplied Project file, without restore

```bash
dnx dotnet-inspect -y -- depends --project-document ./App.csproj --envelope
dnx dotnet-inspect -y -- depends \
  --project-document ./obj/project.assets.json --json
```

This reads only the named file: no MSBuild evaluation, adjacent-assets lookup,
restore or dependency acquisition. Authored constraints are not resolved
versions; imported and central values can remain unknown. Restored assets
provide exact target/package evidence. `--project` remains the existing
restored-assets locator. Preserve limitations, Failures, diagnostics and the
nonzero status of incomplete results, even when usable rows remain.

## Implementors, subclasses and extensions

```bash
dnx dotnet-inspect -y -- type IDisposable \
  --platform System.Private.CoreLib -S Implementers
dnx dotnet-inspect -y -- type Stream \
  --platform System.Private.CoreLib -S "Derived Types"
dnx dotnet-inspect -y -- extensions HttpClient --platform --reachable
```

Scope with `--package`, `--library`, a restored `--project`, or platform inputs.
Direct declarations and reachable applicability are different questions;
`--reachable` widens extension discovery through reachable types.

## Calls and callers

Choose a Member from `Member Index`, then retain its stable selector:

```bash
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json -m Serialize -S "Member Index"
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json Serialize:1 -S Calls
dnx dotnet-inspect -y -- member JsonSerializer \
  --package System.Text.Json Serialize:1 -S "Call Graph" --tree
```

`Calls` lists outgoing physical sites; `Callers` searches incoming sites in
authorized scopes. Widen caller scope with `--caller-package`, `--bin`, or
`--project` where supported. `Call Graph` is a bounded bidirectional view;
use `--jsonl` or `--tsv` for edge rows. Repeated physical calls and distinct
graph relationships have different counts.

Use `--all` for non-public targets. A function load or callback registration
does not prove execution. Missing external assemblies and bounded traversal
limit what an empty caller result can establish.

## Cross-package structure and integrations

| Question | Entrance |
| --- | --- |
| Which dependency boundaries does one entrypoint reach? | `graph calls TYPE MEMBER --root-package Id@version --root-tfm net10.0 --tfm net10.0` |
| How do two exact Libraries call each other? | `graph libraries --library Consumer.dll --library Provider.dll` |
| Is a dependency used through a small cluster of APIs? | `graph packages --package Consumer@version --package Provider@version --tfm net10.0` |
| Which frameworks does a Library integrate with? | `library MyLib.dll -S Integrations` |
| Which relationships exist within an explicit package set? | `graph integrations --package A@version --package B@version --tfm net10.0` |

Follow `--help`, `-D`, and installed explanation for cluster drills, public
root paths, integration identities and baseline controls rather than guessing
their bindings. Integration graphs are induced over the explicit set, not an
automatic dependency traversal. Missing ownership is not permission to invent
package coordinates.

Keep package/version, TFM, selected Library, Type and Member identity with
every conclusion. Graph views and row limits do not prove complete coverage.
Offer a Share URL only when it preserves the actual scenario; a separate
Member overview URL does not preserve the Call Graph.
