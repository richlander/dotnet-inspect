# Ecosystem CLI

## Status

Describes the `ecosystem` command as built by
[#6456](https://github.com/richlander/dotnet-inspect/pull/6456), plus the
`Pruning` section added afterward. It records what exists rather than
proposing something new; the command landed without a design document.

## Owner and claim

This owner defines one command that renders the product-owned ecosystem
registry as ordinary sections:

> Which ecosystems this build knows, what each declares, and — for one
> ecosystem — the facts only that ecosystem can answer.

It does not own ecosystem identity, membership, or contribution shape, which
belong to [Static Ecosystem Packs](ecosystem-packs.md); nor the prune
inventory it renders, which belongs to
[Platform/package pruning](platform-package-pruning.md).

## Discovery here, observation on the subject

This command answers what the product *knows*. What a specific artifact
*contains* belongs to `library`, `type`, and `member`.

Integrations make the split concrete, because both sides have a claim to the
word. The command states its own scope in the output rather than leaving it
implied:

> Product-configured ecosystem knowledge. This catalog is not an exhaustive
> description of the external ecosystems.

and, for an ecosystem with no bound concepts:

> No Integration concepts are explicitly bound to this ecosystem in the current
> product build. This does not mean the external ecosystem has no integrations.

That disclosure is load-bearing: a projection that would drop it is rejected
rather than silently narrowed.

## Focus selects a section set

The optional operand names one registered ecosystem. It is a row selector, not
a subject route, and the argument is `ArgumentArity.ZeroOrOne`, so the parser
itself rejects a second operand. There is no `ecosystem aspire library X`,
because drill-in already belongs to `library`, `type`, and `member`.

Focus does not filter a fixed section list — it **builds a different one**:

| Focus | Default section | Section set |
| --- | --- | --- |
| none | `Ecosystems` | catalog-wide, every pack |
| one ecosystem | `Ecosystem Info` | that pack's sections |

This is why the command declares no section categories: scoping is structural,
so a category door would be a second mechanism for a job already done.

## Sections

Shared across every focus: `Namespace Hints`, `Core Packages`,
`Tool Packages`, `Known Integrations`, `Demos`. Catalog-wide adds
`Ecosystems`; a focused view adds `Ecosystem Info`.

`Demos` reports what each pack declares and points at the `demo` command,
which owns demo discovery and execution. Two paths to the same content is the
failure this shape avoids.

### Pruning belongs to the platform ecosystem alone

`Pruning` exists only when the platform ecosystem is the focus, because
subsumption is a fact about a platform target rather than about a package
ecosystem — `ecosystem aspnetcore` registers ASP.NET Core package content, not
a shared framework. It is neither catalog-wide nor reachable from another pack.

| Column | Meaning |
| --- | --- |
| `Package` | The subsumed package identity |
| `Supplied By` | The shared framework that supplies it |
| `Supplied` | The version that framework supplies |
| `Kind` | `live` or `frozen` |

`Kind` is what explains a result rather than restating it. A frozen entry is
subsumed for any plausible request; a live entry tracks the pack and turns
entirely on the version comparison.

Rows come from the reference pack installed on this machine, so the answer is
exact for the pack actually present and needs no acquisition. A missing or
unreadable pack reports the failure on stderr and renders no rows. The rendered
text then says what was read — "No platform prune inventory was read for this
target" — rather than what the platform contains, because the three ways to
reach zero rows (no pack installed, a pack publishing no prune data, a pack that
could not be read) are one outcome on stdout and none of them licenses the claim
that nothing is subsumed. That claim would be the over-claiming failure
[pruning](platform-package-pruning.md#a-false-yes-is-worse-than-a-false-no)
exists to prevent.

Zero rows is zero rows in every format: structured output carries an empty array
rather than one row of empty strings, which a consumer would read as an identity
with a blank name.

### One family, because the command has no target

The section reports `Microsoft.NETCore.App`, read from the installed runtime
reference pack. `Supplied By` names the family rather than leaving it implied,
because membership follows the framework families a target actually composes,
and a web app composes `Microsoft.AspNetCore.App` as well.

Composing that second family is a question about a target, and this command has
no target — `library` and `package` are where one exists. Answering for a web
app here would answer for an app shape the user never named, which is the same
over-claim in a different direction.

## Section rows are produced on demand

Every section this command rendered originally read compiled-in descriptors,
so materializing all of them cost nothing. `Pruning` is the first backed by an
installed reference pack, and that premise does not hold for it.

Rows are therefore produced on first access rather than at construction, so an
unselected section is free. Cost is decided by the section ladder rather than
by the constructor, which is what lets `ecosystem platform` render
`Ecosystem Info` without reading a pack.

The command takes its prune source as a factory so that property is countable
rather than asserted: a test supplies a counting source and pins zero reads for
the catalog and focused-info routes, and exactly one for the selected section.
The same seam reaches the read-failure path without an unreadable machine.

A section whose cost grows further — acquisition, network, or analysis — is
the signal to adopt the
[section pipeline](section-model.md)'s query-demand model, as `library` and
`package` do, rather than extending on-demand rows again.

## Non-claims

No change to `--platform`, `--extensions`, or `--aspnetcore`, which remain
search-narrowing flags; to `demo`; or to any existing section. No new
acquisition, network path, or platform exception. This owner renders the
registry rather than extending it.

Pack discovery reuses `PlatformResolver`, which is what every other installed-
platform path in the CLI uses today. `DotnetInspector.Platforms.Installed` is the
newer hardened reader — bounded observation, typed outcomes, an explicit
Browser/Wasm verdict — and has no production consumer yet. Adopting it is a
migration for that owner to schedule across the CLI, not something one section
should do alone.
