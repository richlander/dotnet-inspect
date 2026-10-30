if $task == "discover" then
  . as $doc | [.binding.exposed_facets[] | $doc.facets[.]
    | {key, name, summary, value_kind}]
elif $task == "find-literal" then
  . as $doc | [.binding.exposed_facets[] | $doc.facets[.]
    | select(.summary | contains("string-literal")) | {key, summary, effects}]
elif $task == "inspect" then
  .facets[$key] | {key, value_kind, operators, listed_values, examples, effects, requires}
elif $task == "context" then
  . as $doc | [.facets[$key].requires[] | . as $context
    | $doc.facets[.] | {key, value_kind, examples,
      exposed_as_query_term: ($doc.binding.exposed_facets | index($context) != null)}]
elif $task == "closed-values" then
  .facets["dependencies"] | {key, value_kind, operators, listed_values}
else error("unknown exploration task") end
