{
  data_scope,
  bindings: [._embedded.bindings[] | {gesture, input_rules}],
  facets: [._embedded.facets[]
    | select(.key == "depends" or .key == "dependency-target" or .key == "dependency-depth")
    | {key, summary, operators, value_kind, values, input_rules, requires}]
}
