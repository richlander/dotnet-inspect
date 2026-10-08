if $task == "menu" then
  [.choices[] | {id, name, summary, tier: .tier.id}]
elif $task == "safe" then
  [.choices[] | select(.byte_divergent == false) | {id, name, oracle_endorsed}]
elif $task == "conflicts" then
  [.choices[] | select(.conflict_group != null)] | group_by(.conflict_group)
  | map({group: .[0].conflict_group, choices: map(.id)})
elif $task == "tier" then
  [.choices[] | select(.tier.id == $tier) | {id, name, option, value}]
elif $task == "known-choice" then
  first(.choices[] | select(.id == $id) | {id, name, summary, option, value})
elif $task == "grouped-menu" then
  . as $doc | [.tiers[] | . as $tier
    | {tier: $tier, choices: [$doc.choices[] | select(.tier.id == $tier.id)
      | {id, name, summary}]}] | sort_by(.tier.order)
else error("unknown task") end
