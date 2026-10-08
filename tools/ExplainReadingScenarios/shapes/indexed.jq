if $task == "menu" then
  . as $doc | [.choice_order[] | $doc.choices[.] | {id, name, summary, tier}]
elif $task == "safe" then
  . as $doc | [.choice_order[] | $doc.choices[.] | select(.byte_divergent == false)
    | {id, name, oracle_endorsed}]
elif $task == "conflicts" then
  [.conflict_groups | to_entries[] | {group: .key, choices: .value}] | sort_by(.group)
elif $task == "tier" then
  . as $doc | [.tier_choices[$tier][] | $doc.choices[.] | {id, name, option, value}]
elif $task == "known-choice" then
  .choices[$id] | {id, name, summary, option, value}
elif $task == "grouped-menu" then
  . as $doc | [.tiers[] | . as $tier
    | {tier: $tier, choices: [$doc.tier_choices[$tier.id][] | $doc.choices[.]
      | {id, name, summary}]}] | sort_by(.tier.order)
else error("unknown task") end
