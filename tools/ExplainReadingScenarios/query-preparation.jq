# Select only the requested facet, its local context, and host lowering.
(if has("_embedded") then
  . as $root
  | {data_scope,
     facets: ([ (if .kind == "query-facet" then . else empty end),
                ._embedded.facets[] ] | map({key: .key, value: .}) | from_entries),
     bindings: ._embedded.bindings}
else
  {data_scope: {completeness: .selection.completeness},
   facets: (.facets | with_entries(.value |=
      (.facts | with_entries(.key |= gsub("-"; "_"))) + {requires: .requires})),
   bindings: [.bindings[] | (.facts | with_entries(.key |= gsub("-"; "_")))
      + {exposed_facets}]}
end)
| . as $doc
| .facets[$key] as $facet
| {data_scope,
   facet: ($facet | {key, summary, value_kind, operators, values, input_rules, requires}),
   context: [$facet.requires[] | $doc.facets[.]
     | {key, summary, value_kind, operators, values, examples, input_rules, requires}],
   bindings: [.bindings[] | {gesture, input_rules, exposed_facets}]}
