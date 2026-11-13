# Continuous resource discovery: worked proposal

Status: **proposal mockups, not current product behavior**. This is the
Resource Explanation slice [#10133](https://github.com/richlander/dotnet-inspect/issues/10133)
of [#10132](https://github.com/richlander/dotnet-inspect/issues/10132).
The [owning contract](../../../docs/design/resource-explanation.md#continuous-discovery-and-declarative-components)
states the boundary. Paths and handoff fields below illustrate the proposed
explanation graph; they are not newly admitted commands or Workspace packet
fields. Only the component inventory/extraction command shown explicitly as
current is implemented.

## Enter at a lean root

Question: **How can I construct an inspection environment containing
System.Text.Json 10.0.0 for net10.0, and understand one extracted contribution?**

Bare `dotnet-inspect explain` is intended to expose the root. The equivalent
agent HAL root could contain this subset of its registered destinations:

```json
{
  "_links": {
    "self": {"href": "inspect-resource:/catalog"},
    "subjects": {"href": "inspect-resource:/subjects"},
    "operations": {"href": "inspect-resource:/operations"}
  },
  "_embedded": {
    "subjects": [
      {
        "key": "workspace",
        "summary": "A declaration of content and configuration for an inspection environment.",
        "_links": {
          "self": {"href": "inspect-resource:/subjects/workspace"}
        }
      },
      {
        "key": "package",
        "summary": "A package whose content, metadata and relationships can be inspected.",
        "_links": {
          "self": {"href": "inspect-resource:/subjects/package"}
        }
      }
    ],
    "operations": [
      {
        "key": "find",
        "summary": "Find packages or code by properties and relationships at a selected result grain.",
        "_links": {
          "self": {"href": "inspect-resource:/operations/find"}
        }
      },
      {
        "key": "diff",
        "summary": "Compare subjects and inspect their changes.",
        "_links": {
          "self": {"href": "inspect-resource:/operations/diff"}
        }
      },
      {
        "key": "package",
        "summary": "Inspect a package's content and metadata.",
        "_links": {
          "self": {"href": "inspect-resource:/operations/package"}
        }
      }
    ]
  },
  "scope": {"population": "illustrative-root-subset"}
}
```

This is an excerpt, not a complete proposed inventory. A production root must
account for every admitted kind and installed resource through onward routes.
These proposed addresses identify resources independently of representation.
The request selects HAL or direct JSON; the format is not repeated in each URL.
This changes the proposed discovery binding, not today's implemented projected
address parser.
The embedded entries are summaries, not canonical definitions. Their `self`
links identify the resources whose detail can be queried; the root does not
already contain those definitions or their facet inventories.
Each emitted link must be usable through its host after adoption. The catalog
resource supplies the logical root; existing resource paths need not be renamed
under a common string prefix to become its graph descendants.

The Workspace subject description matches the construction question. Its next reading
advertises component kinds, including a package contribution: a package
reference plus requested realization configuration such as TFM/RID. The kind
contract carries field semantics, requirements and meaningful next operations.
It does not embed every potential package or deployed library.

## Cross between subjects and operations

`subjects` lists kinds, not all existing instances. Package, Library, Type and
Member are subject kinds; Find, Depends, Diff and the Package/Type inspection
operations describe work. Workspace is a declarative subject in this graph.
Both collections contain explanation resources, while their semantic roles
remain distinct. Facets, schemas and vocabularies can be further graph nodes
without being forced into one of these two roles.

The two `package` entries above are deliberately different. Their role-qualified
identities and addresses remain distinct even though their local keys match.
The mockup's path namespaces illustrate that distinction; they do not require
renaming every existing address or deriving identity from a command name.
Direct JSON must preserve the same identities and typed relationships.

| Starting question | Proposed route |
| --- | --- |
| I have a package; what can I do with it? | Subjects -> Package -> owner-registered applicable operations |
| What can Diff expose over packages? | Operations -> Diff -> Package -> applicable facets, inputs and context |
| Which packages depend on Polly? | Operations -> Find -> package result grain -> dependency facets |
| How can I construct the inspection environment? | Subjects -> Workspace -> contribution contracts and construction affordances |

Subject-to-operation relationships preserve declared conditions. An operation
overview gives enough information to choose an input kind, without embedding
all of its subject-specific facet definitions. A selected description keeps
accepted input subjects separate from produced kinds and selectable result
grains; none is inferred from the operation name. Following an explanation
link reads knowledge; the reader executes work only through the owner-advertised
operation binding.

### Query Diff for a subject kind

For **what does Diff expose over Package?**, the reader selects the root's Diff
summary and requests its destination. This illustrative operation overview
routes to subject-specific detail:

```json
{
  "summary": "Compare subjects and inspect their changes.",
  "_links": {
    "self": {"href": "inspect-resource:/operations/diff"},
    "subjects": {"href": "inspect-resource:/operations/diff/subjects"}
  },
  "_embedded": {
    "subjects": [
      {
        "key": "package",
        "summary": "Explore the registered comparison capabilities for packages.",
        "_links": {
          "self": {"href": "inspect-resource:/operations/diff/subjects/package"}
        }
      }
    ]
  },
  "scope": {"population": "illustrative-operation-subject-subset"}
}
```

The reader queries that Package destination to learn its owner-registered Diff
facets, accepted inputs, result grains and required context. This is still
capability explanation: it does not compare two package versions. The mockup
does not assert that today's Diff owner registers this combination or prescribe
its facet names. Adoption must use the actual owner-issued set, with visible
unsupported or unavailable outcomes where appropriate.

The subject-first route, Package -> applicable operations -> Diff, reaches the
same selected description. A reader who already knows the destination can
request it directly, skipping both summaries. A single selected answer can
support jq over its returned facets; enumerating unrelated subjects is not a
prerequisite.

The neighboring case is a different subject kind, such as Member. Package's
facets cannot be reused as its answer: request that combination and preserve
its owner's applicability/context outcome. An omitted Member entry in this
illustrative subset does not establish that Diff over Member is unsupported.

### Query Find for a result grain

Find and its proposed `--packages` grain are the user's consolidation direction;
current query migration owns availability and actual command binding. A reader
with the relevant keys can address that capability directly instead of walking
through Workspace construction. A subject-first reader reaches that same
operation through an applicable-operation relationship. These are connected
entry routes rather than independent command and subject manuals.

## Extract a current component

Workspace Definitions already documents the real package contribution used
here. Given its canonical packet in `$w`, these commands are current:

```bash
dotnet-inspect workspace component list --packet "$w" > components.json
jq '.packages[] | select(.package_id == "System.Text.Json")' \
  components.json > component.json
```

The selected inventory descriptor contains:

```json
{
  "path": "packages/system.text.json@10.0.0/net10.0/~",
  "package_id": "System.Text.Json",
  "version": "10.0.0",
  "framework": "net10.0"
}
```

The path is packet-local. It contains `@` and `~` and is not an installed
ResourcePath. It cannot be passed to today's exact explain resolver as a
substitute for that packet. The inventory is also not the packet's serialized
package tuple: Workspace owns their correspondence.

## Carry the owner kind into explanation

The owner integration must preserve enough type information that extraction
does not force the reader to recognize arbitrary JSON shapes. For example, an
owner-issued extraction envelope could look like this:

```json
{
  "contract": "workspace/components/package",
  "value": {
    "path": "packages/system.text.json@10.0.0/net10.0/~",
    "package_id": "System.Text.Json",
    "version": "10.0.0",
    "framework": "net10.0"
  }
}
```

This envelope is an illustrative handoff, not a new packet wire contract or an
existing input flag. Workspace adoption owns its exact projection and admission
of parent context; Contextual Resource Explanation owns the consuming host
gesture. The registered contract identifies the validating semantic owner.
Naming a contract never bypasses that validation. The reader should be able to
submit this value directly to explain through the adopted entrance.

After validation, explanation combines the detached facts with the same package
contribution contract reached from the root. It can answer:

- Which package, version and framework are declared?
- What does the framework field configure?
- Which fields were supplied, which have owner-declared defaults, and what
  additional input is required for other questions?
- How does this kind of contribution participate in Workspace construction?

The leaf can carry substantial rules and examples because the question has now
selected that kind. Semantic facts and interpretation remain owner-issued;
explain does not maintain a second validation manual. Direct JSON and HAL
express the same selected knowledge, and a saved response supports jq lookups
without issuing verification-only copies.

## A missing-context question stays visibly unanswered

Ask of that standalone value: **which context contains this contribution?**
The example has no packet/context basis. Its path can recur in another packet,
and `contexts/g0` in some active Workspace would not identify its parent.
Explanation must preserve that missing knowledge. It may direct the caller to
supply the parent through the owner's supported admission, or visibly refuse a
request whose validation requires it. It cannot silently select an active
Workspace or claim global identity for the component.

Likewise, **which libraries would this package contribute?** requires evidence
beyond this declaration. Explanation describes the appropriate realization or
inspection operation; it does not download the package or invent result rows.
A supplied version and TFM do not establish that any inspection has occurred.

A valid packet carrying the same package in a different context is the
neighboring scope case. A malformed contribution, unknown contract or unsupported
version must fail owner admission rather than being interpreted as search text.
A typed standalone coordinate without occurrence identity can still support
local questions if its owner admits that input.

## Wormholes preserve scope

After an authorized realization, a library's reverse-reference index may support
**which other libraries in this Workspace reference this library?** The owner
can expose that scoped related collection as a wormhole. The collection's
members are requested only when the question needs them; the parent does not
embed every reverse match. The index's coverage and completeness accompany the
relation. A Workspace-local index does not advertise all packages on NuGet.

The declaration's kind, its component occurrence when supplied, and the
libraries realized from it remain distinct identities with owner-issued
correspondence. Their relationships connect the continuous graph without
turning the packet into live execution authority.

## Evidence boundary

This document establishes a reviewable target, not implemented reachability,
component admission, byte savings, runtime performance or agent success.
The pinned motivating source is
[WorkspaceComponentEditing.cs at bdba62c](https://github.com/richlander/dotnet-inspect/blob/bdba62c74c77f98ad5fb4937f022681c5c42241e/src/DotnetInspector.Queries/Definitions/WorkspaceComponentEditing.cs)
and the owner-issued
[component example](../../../docs/design/workspace-definitions.md#component-identity-and-immutable-packet-editing).
The owner and tracker name the enforcing gates and production-host adoption
steps. First-round validation for these Markdown-only mockups is markdownlint
and adversarial design review; later product tests must establish the behavior.
