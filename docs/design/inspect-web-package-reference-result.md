# Inspect-web package reference result

## Owned claim

The package facade's assembly-reference result is one completed outcome: an
available reference list, including an empty list; an explicit not-requested
result; or a failure message. The generated TypeScript consumer preserves
those distinctions when rendering the selected library's references.

This document owns only that Browser wire-result and presentation contract.
`PackageDependencyGroupsQuery` owns manifest dependency evidence;
`AssemblyContextReferencesQuery` owns direct assembly-reference evidence;
JsExportSurface owns serializer evidence; and
[`ts-jsexport`](ts-jsexport.md#json-union-lowering) owns union lowering.
Their contracts are consumed, not redefined here.

## Wire result

`BrowserPackageDependencies.AssemblyReferences` carries a native C# union of
`BrowserAssemblyReferenceOutcome` and `string`. The outcome carries an
exact `WasRequested` Boolean, the reference rows, and an optional message. A
requested outcome preserves the reference order and fields. An unrequested
outcome carries an empty reference array and the explicit reason assembly
inspection did not run. The string case carries the existing inspection or
compile-library-unavailability message.
`AssemblyReferenceError` is retired rather than maintained as parallel state.
The existing assembly label, manifest dependency groups, dependency-group
diagnostic, and compile-library availability remain independent facts.

System.Text.Json writes the active case inline over the existing string
transport. The completed object contains `wasRequested`, `references`, and
`message`; the failure case is a string. The generated alias also includes the
native union's default `null`. The Boolean distinguishes the two non-failure
object outcomes; no parallel hand-authored TypeScript contract is introduced.

An ordinary producer returns one constructed outcome, not the default union.
The consumer nevertheless handles the generated default-null alternative as a
settled missing-result diagnostic. It never interprets it as work that has not
started.

## Presentation

An available empty list renders the existing no-direct-references message. The
not-requested case renders an explicit neutral result. A string is a failure
even when it is empty; an empty message uses an explicit no-details diagnostic.
Default `null` renders an explicit missing-result diagnostic. None of these
cases renders a successful empty list or a loading state. Existing text
escaping and reference-row presentation are preserved.

This result is not the frontend request lifecycle. Loading, cancellation,
freshness, and stale-completion ownership keep their current owner and behavior.
The closed [#4600 stack](https://github.com/richlander/dotnet-inspect/pull/4600)
is comparative evidence for distinguishing settled empty-message failures
from unattempted work, not a dependency or a plan to restore that stack.

## Adoption and evidence

Tracked by [#6191](https://github.com/richlander/dotnet-inspect/issues/6191),
this is the fourth and final slice of
[#5892](https://github.com/richlander/dotnet-inspect/issues/5892):
Metadata marker evidence (#5896), JsExportSurface serializer evidence (#6089),
TypeScript generation and its compiler/runtime harness (#6139), and this
production inspect-web adoption. The CLI generator and browser/Wasm consumer
exercise the same generated contract.

The package facade owns the wire types and uses its assembly-local generated
serializer context. The browser consumes the generated declaration rather
than declaring a parallel wire union. Rendering remains in the existing
browser HTML presentation path, consuming that typed result; this change adds
no new rendering format or replacement transport.

`AssemblyContextReferencesQuery.ExecuteParticipantRows` issues the detached
name, version, culture, and public-key-token rows consumed by the package
facade. The facade orders and lowers those owner-issued rows into its wire
records. Rejected participant access reaches the facade through the query's
typed detached rejection kind and detail. The facade does not project Metadata
assembly identities or candidate-open failures for this result.

`BrowserAssemblyReferenceResultTests` gates the real generated serializer's
nonempty list, empty list, not-requested result, failure text including an
empty message, and default null. `BrowserEngineBoundaryTests` gates the real
package query's available reference projection, detached rejection message,
and preservation of manifest dependencies without assembly inspection.
`test/library-references.test.ts` gates the corresponding rendered outcomes;
generated facade drift and frontend type checking gate the typed handoff. The
existing real-Wasm
`eng/test-inspect-web-package-adoption-gate.sh` exercises the published facade's
available and unavailable package cases in Firefox.
