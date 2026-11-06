# Do the parameter lookups equal their derivation from the records?
.parameter_type_members == ([.members[] | .documentation_id as $id | .parameters[].type_mentions[] | {key: ., value: $id}] | group_by(.key) | map({key: .[0].key, value: (map(.value) | unique)}) | from_entries)
