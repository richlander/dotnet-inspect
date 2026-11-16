# Frozen-answer assessment

The author manually assessed [the frozen criteria](criteria.txt) against
answers and recorded outputs. Automated replay checks evidence integrity,
not these judgments. All six tasks passed the stated product-evidence gates;
format recovery and failed discovery remain costs, not erased failures.

## Relationship readers

Both answers identify
`Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions`
and the public `AddHttpClient` overload taking only
`Microsoft.Extensions.DependencyInjection.IServiceCollection`, with stable
selector `AddHttpClient~5c44566d15`. Both retain exact package `10.0.13`,
`net10.0`, and the limit to that selected population. Neither claims global
coverage or treats a recorded index starter as applicable API proof.

For both readers, response 2 supplies the receiver, declaring class, package
version and full receiver-marked signature. Response 4 supplies the selector
and canonical signature. Response 5 supplies `public extension` classification
and documentation that it adds `IHttpClientFactory` and related services.
That last read fills requested accessibility and service-meaning evidence;
the retrospective's word “confirm” alone does not establish a prohibited
verification-only request. Full evidence is present after response 5; neither
makes subsequent product requests.

- [relation-1 answer](relation-1/answer.json) combines the receiver spelling
  with a conventional `public static` signature and retains provenance.
- [relation-2 answer](relation-2/answer.json) retains the literal product
  `public extension` signature plus canonical identity. Its C#-style rendering
  omits `static`; do not treat that rendering as a compiler-checked declaration.
  The exact raw signature and member selection satisfy this reading task.
- Both response-3 files contain type metadata with `members: []`, despite
  response-4 TSV containing 20 overloads. This accounts for one recoverable
  unproductive request in each trial; exit-based failure counts do not show it.
- Both loaded the relationships skill before querying; neither used explain,
  root discovery, HAL links or local jq. No new explain-value claim is supported
  for this task.

## Detached readers

Both [detached-1](detached-1/answer.json) and
[detached-2](detached-2/answer.json) establish `System.Text.Json`, `10.0.0`,
`net10.0`, and absent RID. Both distinguish the supplied asserted path,
`contexts/g0` and `focused: true` from facts the tool can qualify without a
parent. Both preserve Unavailable, rather than converting it to false or
trusting the asserted fields. Neither manufactures a parent, realizes a
Workspace, acquires content or requests unrelated package data.

- Detached-1 response 4 and detached-2 response 5 are the actual detached
  explanations, explicitly reporting all three parent-dependent facts as
  unavailable. These responses cover the requested values and states.
- Detached-1 then follows the emitted `describedby` to response 5 for meanings
  such as declared version pin and requested framework/RID. That prose is new
  information, so classify it as semantic elaboration, not demonstrated
  verification-only waste. Detached-2 stops without that elaboration.
- The failed searches are detached-1 response 2 and detached-2 responses 1–2.
  All return no matches. Both recover through `explain --help`. This is
  discovery friction with useful subsequent interpretation, not evidence that
  root graph navigation was attempted or failed.

## Parent-qualified edit readers

Both [parent-1](parent-1/answer.json) and [parent-2](parent-2/answer.json)
qualify the original declaration through `workspace component list` using the
actual parent token: package/version/TFM, absent RID, canonical path,
`contexts/g0` and focus true. They then perform an ordinary immutable Package
update. Neither uses `explain --component --parent-packet`; this is successful
execution through an existing route, not adoption of that explain entrance.

- Parent-1 response 4 and parent-2 response 3 contain original context/focus.
- Parent-1 response 5 and parent-2 response 4 contain `succeeded: true`,
  source `10.0.0`, destination `10.0.1`, Applied navigation and `package.libraries`
  inspector, ExactPath retention, no diagnostics and the actual successor token.
- Grader-only decoding of the returned tokens shows every original packet
  field equal except `t[0][1]`, now `10.0.1`. This checks preservation of context,
  focus and saved views, and matches each frozen answer to the product output.
  The grader reads are not participant requests and are separately retained.
- Parent-1 stops with the successful update envelope. Parent-2 additionally
  requests the successor component list (response 5), explicitly showing
  membership/focus that the update envelope does not separately enumerate.
  Its retrospective names that information gap. Do not classify this as
  verification-only solely because another participant stopped earlier.
- Both first try unsupported JSON flags and then a file path where a canonical
  packet value is required. Parent-1's third failure is its own token-copying
  error, retained as an unviewed 330-byte diagnostic. These are five nonzero
  exits across two successful tasks, not hidden by the eventual answers.

## What this supports

The new detached explanation surface can preserve unknown context correctly
when agents reach it. Existing Workspace operations can produce useful edits
without an explain detour. Existing skills remain substantial and effective
for the known relationship route. Registration and format failures create
observable recovery costs. No clear verification-only request is established
by these traces and the separate neutral retrospectives; do not infer this
stopping-rule result generalizes to other questions.

The three task families were provided explicitly, including exact package
versions and, for the detached task, the distinction to investigate. They test
navigation and execution on landed behavior, not spontaneous recognition of
an unspecified user need. A future scoped-collection evaluation must test
unknown-population discovery after the owner publication and host route ship.
