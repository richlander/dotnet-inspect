# Worked demo: an agent explores Package Query facets

The agent's task is: **Find packages whose implementation libraries contain the
exact decoded string `https://`, using target framework `net8.0`.** Before
running a package-content query, it needs to find an applicable facet, understand
its operand and effects, and learn how to supply required context.

This worked demo uses actual owner-issued catalog facts and relationships.
Its self-contained `facet-document.json` is an experimental presentation of
those facts, not an implemented CLI selection. The five jq steps run against
that proposed document. Host-specific authoring and operand constraints that
the catalog does not yet expose are identified explicitly in step 5.

The complete proposed [facet JSON](../examples/package-query-facets.json) is
checked in; the jq steps below can use that file directly instead of generating
the temporary example.

## Capture and reproduce

From the compact-projection worktree, with its built CLI:

```bash
python3 tools/ExplainReadingScenarios/facet-demo.py \
  --cli /path/to/compact/dotnet-inspect \
  --output /tmp/explain-facet-demo
```

The runner captures these two existing operations:

```bash
dotnet-inspect explain package-query/query --depth 1 --json
dotnet-inspect explain package-query/bindings/cli --json
```

It derives a self-contained document containing all 19 facets, the CLI binding,
and its 14 exposed query-term IDs. Typed identities are retained with their
shared scope; every original facet fact is checked against its lowered record.
Required-context references resolve to records within the same document.
Order is explicit. Selected populations must be available and complete; the
prototype refuses incomplete data rather than silently publishing partial
success. Other relationships and declaration closure are not this selection.

The document uses a keyed `facets` collection, ordered discovery IDs, and a
small `binding.exposed_facets` index derived from the binding's actual `exposes`
relationship. This index earns its place: it answers which facets this host
lets the agent author, without repeating their descriptions or confusing them
with required context. No keyword or opaque identity is decoded to guess role.

## 1. Discover what this host exposes

The agent lists the CLI binding's exposed facets, then searches their summaries
for a string-literal operation:

```bash
jq '. as $doc | [.binding.exposed_facets[] | $doc.facets[.]
    | {key, name, summary, value_kind}]' \
  /tmp/explain-facet-demo/facet-document.json
jq '. as $doc | [.binding.exposed_facets[] | $doc.facets[.]
    | select(.summary | contains("string-literal"))
    | {key, summary, effects}]' \
  /tmp/explain-facet-demo/facet-document.json
```

The second query returns:

```json
[{
  "key": "library-literal",
  "summary": "Downloads the package and matches decoded string-literal uses across the selected implementation libraries.",
  "effects": [
    "AcquisitionTier: package-content",
    "Capability: package-content-provider"
  ]
}]
```

The agent has found a relevant operation and sees that execution acquires
package content. Searching all 19 facets without applying host exposure would
also list contextual and selector facets as though they were authorable terms.

## 2. Inspect its operand and requirements

```bash
jq '.facets["library-literal"]
    | {key, value_kind, operators, listed_values, examples, effects, requires}' \
  /tmp/explain-facet-demo/facet-document.json
```

Result:

```json
{
  "key": "library-literal",
  "value_kind": "decoded UTF-16 text",
  "operators": ["eq"],
  "listed_values": [],
  "examples": ["https://"],
  "effects": [
    "AcquisitionTier: package-content",
    "Capability: package-content-provider"
  ],
  "requires": ["library-target"]
}
```

An example is not an exhaustive accepted-value list. An empty `listed_values`
array does not establish an unrestricted domain or its length bounds. The
agent can identify the operand kind, supported operator, acquisition effect,
and required-context key from this result, but not every validation rule.

## 3. Resolve the required context locally

```bash
jq '. as $doc | [.facets["library-literal"].requires[] | . as $context
    | $doc.facets[.] | {key, value_kind, examples,
      exposed_as_query_term:
        ($doc.binding.exposed_facets | index($context) != null)}]' \
  /tmp/explain-facet-demo/facet-document.json
```

Result:

```json
[{
  "key": "library-target",
  "value_kind": "NuGet target framework",
  "examples": ["net10.0"],
  "exposed_as_query_term": false
}]
```

The agent now knows that a target framework is required and that it cannot
supply it as another authored query term. `net10.0` is an example; it does not
supersede the user's `net8.0`. This lookup needs no neighboring-resource fetch.
The correct CLI gesture still requires the host information in step 5.

## 4. Compare a facet with listed closed values

For contrast, the agent can inspect the dependency facet:

```bash
jq '.facets["dependencies"]
    | {key, value_kind, operators, listed_values}' \
  /tmp/explain-facet-demo/facet-document.json
```

Result:

```json
{
  "key": "dependencies",
  "value_kind": "closed value",
  "operators": ["eq"],
  "listed_values": ["none", "cross-prefix"]
}
```

Its issued operand kind and listed values differ from literal text. The agent
must not apply one facet's value-domain rules to another just because both
support `eq`.

## 5. Build the host invocation and expose missing metadata

The owning [library-literal contract](../../../docs/design/package-query-library-literal.md#term-contract)
specifies facts that the current explanation catalog does not supply in
structured form:

- Supply required target context through CLI `--tfm TFM`. The planner authors
  `library-target`; it is not an independently selectable `--where` term.
- The literal is exact decoded text: preserve whitespace, case, Unicode, and
  line breaks. Its length is 1–1,024 UTF-16 code units, and it is single-valued.
- A query containing this term admits at most five package candidates. One
  exact package input admits at most one candidate.

The agent must obtain this guidance before execution; neither relation names,
examples, nor empty values lists establish these rules. For the example exact
package `Microsoft.Azure.SignalR`, the planned invocation is:

```bash
dotnet-inspect package query Microsoft.Azure.SignalR \
  --where 'library-literal=https://' --tfm net8.0 --json
```

This acquisition-capable query is shown, not executed by the demo. For another
package population, substitute the user's selection and observe the owner-issued
candidate bound. The planned query executes `library-literal` with its exact
operand and gets target context through the host flag, rather than authoring
`library-target=net8.0`.

The runner executes only the two documented pre-acquisition refusal cases:

```text
Without --tfm:
Error: library-literal requires one exact target framework.

With --where library-target=net8.0:
Error: Package Query does not define term 'library-target'; run
'package query -Q Packages' for the current vocabulary.
```

These gaps are adoption obligations: Query Space must issue operand/selection
constraints, and the CLI binding must expose how the required context is
supplied. Those owners retain their contracts; Resource Explanation consumes
the issued facts. A future self-contained facet selection should include that
information once, so this stage needs no supplementary contract read. It must
not maintain an independent list of validation rules in the projector.

## Measured query and retrieval costs

Pinned production prototype: `0.27.0+5ce2e00`. The runner writes the original
responses, prototype, five query answers, source hashes, and report outside the
repository. Sizes are minified UTF-8 JSON before compression, including the
output newline where emitted. These are reading measurements, not latency.

| Read | Answer bytes |
| --- | ---: |
| Discover 14 exposed terms | 2,385 |
| Find the literal facet | 234 |
| Inspect operand and requirements | 239 |
| Resolve contextual target and exposure | 118 |
| Inspect listed dependency values | 110 |

The existing two catalog responses total 40,606 + 6,835 = **47,441 B** across
two requests. The experimental facet document is **11,167 B**, proposed as one
retrieval, and answers all five catalog queries locally. Generating it today
still requires those two current requests; it is not a new production endpoint.
The supplemental owner-contract consultation in step 5 is additional work,
not counted in the catalog-byte comparison. Complete agent-task retrieval
cannot be claimed until the missing constraint and binding facts are exposed.

The full 19-facet catalog is an explicit exploration dataset, not a proposed
minimum payload for a known facet, `.tips`, or `.reference`. A focused selection
could include the known facet and only its required context. Evaluate that
selection separately using the same agent decisions and total retrieval costs.
