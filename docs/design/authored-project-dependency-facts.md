# Authored project dependency facts

How one already-acquired project XML document becomes immutable authored
package-declaration evidence without transferring filesystem, MSBuild
evaluation, restore, presentation, or normalized cross-input ownership into
the query layer.

**Status:** implementation contract for #6347, delivering the provider half
of issue #6266 step 4.

## Owner

The **Authored Project Dependency Facts Query** in
`DotnetInspector.Queries` owns:

- bounded parsing of exact caller-supplied project XML bytes;
- the supported authored-syntax subset;
- project-content provenance and semantic project identity;
- literal target-framework observations;
- direct `PackageReference` declarations and their condition association;
- requested NuGet version-constraint validation and normalization;
- typed incomplete evidence for unsupported or unresolved project syntax;
- content-free failures for document-wide rejection; and
- construction-time containment of project-authored display text.

The query does not accept a path, locate or read files, evaluate MSBuild,
resolve imports or properties, initiate restore or build, inspect assets, load
project assemblies, or choose a renderer.

## Consumer and delivery

The immediate concrete consumer is the **Package Dependency Evidence Query**
specified by `docs/design/package-dependency-evidence.md`; #6348 owns that
focused adapter. The end-to-end delivery tracker is #6266. Its eight-step path
connects this provider to CLI adoption in step 7 and inspect-web Browser/Wasm
adoption in step 8.

This query is shared host-neutral substrate, so no single-consumer or
single-host exception applies. Its complexity is justified by both hosts
needing the same bounded and construction-time-safe interpretation of authored
project dependency syntax.

This owner does not render. The normalized consumer preserves typed evidence
to its sink boundary. Later CLI adoption uses Markout as the default
host-neutral lowering and projects JSON-family formats from the same typed
information. Browser adoption consumes the same typed or wire information and
owns only interactive DOM presentation.

## Claim

One execution over exact admitted XML bytes returns:

- a SHA-256 digest over those exact bytes as content provenance;
- a semantic project identity over canonical supported facts and typed
  incompleteness;
- literal target-framework observations;
- package declarations with package identity, requested version constraint,
  source spelling, source-occurrence count, and condition association;
- opaque typed evidence for package-reference syntax that cannot establish one
  direct declaration;
- a complete or incomplete syntax projection; or
- a content-free document-wide failure.

The declaration basis is **authored project syntax**. Complete means that every
relevant construct in the supplied document belongs to the supported syntax
subset and was projected. It does not mean that the effective evaluated
project package set is complete.

## Input boundary

The only input is exact already-acquired project XML bytes. A host may locate
and read a `.csproj`, but locator identity, path provenance, file-system state,
and acquisition diagnostics remain outside this query.

The query consumes the bytes directly through `HardenedXml` with DTD
processing prohibited, a null XML resolver, and an explicit decoded-character
budget. It does not create a second XML-hardening path.

The root must be one `Project` element in either:

- no XML namespace, as used by current SDK-style projects; or
- the legacy MSBuild 2003 namespace.

An ordinary root `Sdk` attribute does not make the lexical projection
incomplete. An explicit `Import` element or child `Sdk` element does: those
constructs can compose additional authored project syntax whose bytes were not
supplied.

## Target-framework observations

The supported target property subset is a direct `TargetFramework` or
`TargetFrameworks` child of an unconditional direct `PropertyGroup` under the
root.

`TargetFramework` contributes one literal observation.
`TargetFrameworks` splits one literal value on semicolons and contributes each
non-empty literal observation. Whitespace around each value is not semantic.
Recognized values use canonical NuGet short-folder identity. Unrecognized
literal values remain distinct through opaque identity and retain an inert
source spelling.

An expression-bearing target value is retained as one unresolved observation
before any semicolon splitting. Semicolons inside property functions, item
expressions, or metadata expressions therefore cannot invent literal targets
from expression arguments.

The following produce incomplete evidence rather than an inferred target:

- a target property containing property, item, or metadata expansion;
- a conditional target property or containing `PropertyGroup`;
- a target property outside a direct root `PropertyGroup`;
- nested XML content where a scalar target value is required;
- an empty `TargetFramework` value or empty `TargetFrameworks` component;
- conflicting `TargetFramework` and `TargetFrameworks` declarations; and
- repeated target declarations whose canonical sets disagree.

All independently recognized target observations remain available when the
overall result is incomplete. An absent target property is valid authored
syntax and does not itself make the result incomplete: targets may come from
evaluation outside this basis. Each target observation also retains opaque
syntax-context identity, so changing its unsupported placement or conditions
changes semantic project identity without claiming their evaluated result.
Target syntax that cannot produce an observation, such as an empty value or
nested XML content, retains separate opaque syntax identity so its placement,
conditions, and material shape still contribute to project identity.

## Package declarations

The supported declaration is a direct `PackageReference` child of a direct
`ItemGroup` under the project root with:

- one literal `Include` attribute;
- no `Update`, `Remove`, or `Exclude` attribute;
- one requested version constraint from either a `Version` attribute or one
  direct unconditional `Version` child; and
- an absent condition or a supported target-framework condition.

Package identity matching is ordinal and case-insensitive. A valid package ID
uses lowercase canonical identity while retaining the source spelling as
`InertString`. A valid NuGet version constraint uses
`NuGet.Versioning.VersionRange` normalized spelling while retaining the source
spelling separately. Requested constraints remain constraints; this query
never invents a resolved package coordinate. Canonical constraint identity
normalizes case where NuGet equality is case-insensitive, including prerelease
labels.

Equivalent occurrences with the same canonical package ID, canonical
constraint, and condition identity form one declaration with an exact source
occurrence count. Conflicting constraints for the same package and condition
remain separate declarations and make the result incomplete. Source order,
attribute order, formatting, comments, and package-ID casing do not affect
semantic project identity.

A versionless `PackageReference` remains visible as an unresolved declaration
with its valid package identity and no canonical constraint. This is
incomplete evidence, not an unversioned NuGet range: central package management
or imported properties may supply the effective constraint.

The following also produce incomplete evidence without erasing independently
usable declarations:

- property expansion in `Include` or version syntax;
- item or metadata expansion in package, version, target, or condition syntax;
- an invalid literal package ID or NuGet version constraint;
- case-insensitive duplicate semantic attributes such as `Include` plus
  `include`, or `Version` plus `version`;
- both `Version` attribute and child-element forms;
- a conditioned `Version` child;
- nested XML content where a scalar version value is required;
- `Update`, `Remove`, or `Exclude` item operations;
- `PackageReference` outside the supported direct `ItemGroup` shape;
- an explicit `PackageVersion` item;
- an explicit `GlobalPackageReference` item;
- a `ManagePackageVersionsCentrally` project property; and
- another unsupported construct that can affect package declarations.

The provider retains a bounded inert source spelling when it can safely
identify one declaration value. When case-insensitive duplicate attributes,
several version forms, or overrides can disagree, it withholds the affected
single source value and canonical value but retains every alternative in
boundary-safe unresolved semantic identity. It does not guess the result of
MSBuild expression expansion, central version selection, item-operation
ordering, unsupported ancestry, or imported items.

## Condition association

A declaration is either:

- unconditional;
- associated with one exact target framework; or
- associated with an unresolved condition.

The supported conditional grammar is one equality expression comparing
`$(TargetFramework)` with one literal framework value. Operand order may be
reversed, outer whitespace is ignored, and either single or double quotes may
delimit operands. The `ItemGroup` and `PackageReference` may each carry that
condition. Two supported conditions compose only when they name the same
canonical framework.

Every other condition, two supported conditions naming different frameworks,
and a declaration beneath unsupported ancestry produce an unresolved
condition identity and incomplete evidence. The declaration remains in the
result so a later consumer can preserve that some authored syntax exists
without assigning it to a target the provider cannot establish. Unresolved
condition collections use count-prefixed, individually length-prefixed
identity fields; embedded line breaks or other delimiter-like text cannot
collapse distinct condition sets.

## Completion

The result algebra is closed:

- **Available** carries a complete projection, including a valid project with
  zero target observations and zero package declarations.
- **Incomplete** carries all usable facts plus typed limitation counts.
- **Failed** carries one content-free document-wide failure.

Limitations are additive and canonicalized by reason. Multiple source
occurrences of one reason contribute an exact count. No diagnostic echoes raw
project text.

Incomplete evidence is usable evidence. A consumer must preserve its facts and
completion; it must not convert the result to unavailable or complete-empty.

## Bounds and failures

The query enforces independent configured limits for:

- admitted XML bytes;
- decoded XML characters;
- one scalar spelling;
- XML element depth;
- target observations;
- package-reference occurrences; and
- typed limitation occurrences.

Exceeding any limit fails the whole document with
`ConfiguredLimitExceeded`. This avoids returning a prefix that could be
mistaken for the complete authored file.

The document-wide failure reasons are:

- `MalformedXml`;
- `UnsupportedDocumentShape`; and
- `ConfiguredLimitExceeded`.

Malformed XML includes prohibited DTD syntax. Unsupported root names or
namespaces are unsupported document shape. Declaration-local invalid or
unsupported syntax is incomplete evidence rather than document-wide failure.

Failures contain only stable reason, optional XML line and position, and a
stable content-free message. They do not contain project-authored text.

## Identity and provenance

After the byte admission bound succeeds,
`AuthoredProjectContentProvenance` is lowercase SHA-256 over the exact admitted
bytes. It changes for comments, whitespace, attribute order, or equivalent
source spelling and is never semantic identity.

`AuthoredProjectIdentity` is a lowercase SHA-256 digest over a typed,
length-prefixed canonical encoding of:

- canonical target observations;
- canonical declarations and condition identities;
- opaque target syntax context and unresolved dependency syntax;
- source-occurrence counts; and
- typed limitation reasons and counts.

Collections are canonically ordered before encoding. Every field is
length-prefixed and every collection is count-prefixed, so project-authored
text cannot imitate an identity boundary. Harmless formatting, XML property or
attribute order, comments, and package-ID casing therefore preserve semantic
identity while material declaration, condition, target, occurrence-count, or
completion changes do not.

Unrecognized framework and unresolved-condition identities use opaque SHA-256
tokens. Raw project text never becomes a public identity string.

## Containment

All project-authored spellings exposed by the query are constructed as
`InertString` with `TextPolicy.Field` and the scalar bound. Canonical package
IDs, canonical version constraints, and canonical framework identities use
validated bounded grammars rather than display text.

Failures and limitation reasons are enums with content-free messages. The
query does not log, render, or interpolate project-authored text into
diagnostics.

## Non-claims

This owner does not claim:

- a project path, project name, or locator identity;
- file-system snapshot stability;
- MSBuild evaluation or an effective evaluated-project package set;
- import, property, item-operation, or condition evaluation outside the
  supported grammar;
- central package version resolution;
- restore, build, `project.assets.json`, resolved coordinates, graph edges, or
  package-pruning conclusions;
- normalized cross-input package evidence;
- CLI grammar, sections, Count, JSON, Markout, or browser presentation; or
- Windows Metadata support.

## Evidence

The implementation is gated by
`AuthoredProjectDependencyFactsQueryTests`:

| Gate | Claim |
| --- | --- |
| `Execute_ProjectsLiteralSingleAndMultiTargetSyntax` | Literal targets, unconditional declarations, and exact target conditions produce canonical facts. |
| `Execute_ProjectsAttributeAndElementVersionForms` | Both supported version spellings produce equivalent declarations. |
| `Execute_ValidEmptyProjectIsComplete` | Valid empty syntax is available complete-empty evidence. |
| `Execute_EmptyVersionIsIncomplete` | Empty attribute and child-element versions cannot become complete missing constraints. |
| `Execute_EmptyVersionConditionIsUnconditional` | Empty condition attributes do not suppress an otherwise supported version. |
| `Execute_AmbiguousSemanticAttributesRemainUnresolved` | Case-insensitive duplicate Include, Version, and Condition attributes cannot select an arbitrary winner. |
| `Execute_AmbiguousAttributeOrderDoesNotChooseAWinner` | Reordering ambiguous attributes preserves unresolved semantic identity. |
| `Execute_ExcludeIsIncompleteAndChangesSemanticIdentity` | Unsupported item selection remains incomplete and changes unresolved identity. |
| `Execute_UnsupportedAncestryRetainsFactsAsIncomplete` | Nested targets remain visible and nested declarations cannot become unconditional. |
| `Execute_NestedVersionMarkupCannotBecomeLiteralConstraint` | Nested or foreign markup cannot flatten into a supported scalar version. |
| `Execute_VersionAlternativesRemainInSemanticIdentity` | Conditioned and conflicting version alternatives are withheld from canonical projection but remain identity-bearing evidence. |
| `Execute_VersionAlternativeOrderIsNotSemantic` | Reordering unresolved version alternatives preserves canonical semantic identity and multiplicity. |
| `Execute_UnresolvedConditionIdentityPreservesFieldBoundaries` | Distinct unresolved condition collections cannot collide through delimiter text. |
| `Execute_AllUnsupportedConditionsContributeLimitations` | Every unsupported condition contributes to exact additive limitation counts before unresolved association. |
| `Execute_UnsupportedAncestryStillClassifiesEveryCondition` | Forced unresolved association does not bypass per-condition limitation classification. |
| `Execute_SharedAncestorConditionCountsOnce` | One source condition shared by several declarations contributes one limitation occurrence while remaining associated with each declaration. |
| `Execute_UnresolvedNestedShapeIgnoresCommentsAndAttributeOrder` | Opaque nested version syntax uses structural canonical identity rather than raw XML serialization. |
| `Execute_AmbiguousVersionConditionOrderIsNotSemantic` | Reordering case-insensitive duplicate condition attributes on Version or VersionOverride cannot select an identity winner. |
| `Execute_UnsupportedSyntaxPreservesUsableDeclarationsAsIncomplete` | Imports, property indirection, central management, item operations, and unsupported conditions remain visible without erasing usable facts. |
| `Execute_GlobalPackageReferenceIsCentralManagementEvidence` | Global package-reference syntax cannot collapse into a complete-empty project. |
| `Execute_CentralManagementPropertyPresenceIsIncomplete` | Empty, false, or nested central-management property syntax remains visible without flattened interpretation. |
| `Execute_ItemAndMetadataExpressionsAreNotLiteralFrameworks` | Item and metadata expressions remain unresolved rather than opaque literal frameworks. |
| `Execute_TargetFrameworkExpressionSemicolonsDoNotInventTargets` | Semicolons inside an unresolved MSBuild expression cannot create literal target observations. |
| `Execute_TargetConditionsContributeToSemanticIdentity` | Material changes to unresolved target conditions change project identity. |
| `Execute_UnsupportedTargetAncestryContributesToSemanticIdentity` | Distinct unsupported target placements remain identity-bearing without claiming evaluation. |
| `Execute_UnprojectedTargetContextContributesToSemanticIdentity` | Empty and nested target syntax retains material condition context without producing target observations. |
| `Execute_IncludeLessPackageOperationsContributeToIdentity` | Package item operations without Include remain typed opaque evidence and affect identity. |
| `Execute_NuGetEquivalentPrereleaseConstraintsDoNotConflict` | NuGet-equivalent prerelease casing collapses into one declaration with exact occurrence count. |
| `Execute_ConflictingDeclarationsAreIncomplete` | Conflicting constraints remain separate and cannot become a complete declaration. |
| `Execute_ContentAndSemanticIdentityRemainDistinct` | Formatting and casing alter content provenance without altering semantic identity. |
| `Execute_RejectsMalformedDtdAndUnsupportedRoots` | Hardened XML and root-shape failures remain visible and content-free. |
| `Execute_Enforces*Limit` | Byte, decoded-character, scalar, target-observation, package-reference, and limitation bounds fail closed. |
| `Execute_EnforcesXmlElementDepthLimit` | Deep unsupported XML fails visibly before recursive canonicalization can exhaust the process stack. |
| `Execute_HostileTextIsContainedAtConstruction` | Exposed source spellings are inert and failures echo no source content. |

Inline exact XML inputs are the correct fixtures for syntax-shape and boundary
tests: no separately compiled artifact is under inspection, and the query
accepts content rather than a path. A later host-adoption fixture must use the
normal fixture catalog if its acquisition or build boundary becomes evidence.
