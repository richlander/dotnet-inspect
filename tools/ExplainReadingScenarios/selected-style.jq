# HAL presents the selected vocabulary as root state and related resources under _embedded.
(if has("_embedded") then
  . as $root
  | {vocabularies: ([ $root, $root._embedded["inspect:vocabularies"][] ]
      | map({key: .id, value: {values: ._embedded["inspect:values"], property_sets, property_groups}})
      | from_entries)}
else . end)
| .vocabularies["csharp.style-choices"] as $choices
| .vocabularies["csharp.style-tiers"] as $tiers
| {choices: $choices.values,
   tiers: [$tiers.values[] | {id, name, summary, order: (.order | tonumber)}],
   choice_sets: $choices.property_sets,
   tier_sets: $tiers.property_sets,
   conflict_groups: $choices.property_groups.conflict_group}
| if $task == "menu" then
  [.choices[] | {id, name, summary, tier}]
elif $task == "safe" then
  . as $doc | [.choices[] | . as $choice
    | select($doc.choice_sets.byte_divergent | index($choice.id) == null)
    | {id, name, oracle_endorsed:
      ($doc.choice_sets.oracle_endorsed | index($choice.id) != null)}]
elif $task == "conflicts" then
  [.conflict_groups | to_entries[] | {group: .key, choices: .value}] | sort_by(.group)
elif $task == "tier" then
  [.choices[] | select(.tier == $tier) | {id, name, option, value}]
elif $task == "known-choice" then
  first(.choices[] | select(.id == $id) | {id, name, summary, option, value})
elif $task == "grouped-menu" then
  . as $doc | [.tiers[] | . as $tier
    | {tier: ($tier + {byte_divergent:
        ($doc.tier_sets.byte_divergent | index($tier.id) != null)}),
       choices: [$doc.choices[] | select(.tier == $tier.id) | {id, name, summary}]}]
  | sort_by(.tier.order)
elif $task == "endorsed" then
  {oracle: .choice_sets.oracle_endorsed, corpus: .choice_sets.corpus_endorsed}
else error("unknown task") end
