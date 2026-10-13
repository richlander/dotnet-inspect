# --arg form direct|hal|source --arg vocabulary ID --arg value ID
# --arg task value|inventory|defaults|sets|groups
def table:
  if $form == "direct" then .vocabularies
  elif $form == "hal" then
    ._embedded.vocabularies | map({key: .id, value:
      (. + {values: (._embedded.values | map({key: .id, value: del(.id)}) | from_entries)})}) | from_entries
  elif $form == "source" then
    [. , ._embedded.vocabularies[]?] | map({key: .id, value:
      (. + {values: (._embedded.values | map({key: .id, value: del(.id)}) | from_entries),
            maps: (.maps | map({key: .identity, value: .}) | from_entries)})}) | from_entries
  else error("unknown form") end;
if (.scope.completeness // .data_scope.completeness) != "Complete" then
  error("complete selected observation required") else . end |
table as $table | $table[$vocabulary] as $v |
if $task == "inventory" then
  {catalog_values: ($v.catalog_values // $v.members),
   selected: ($v.value_order // [$v.values | keys_unsorted[]]), accepted_by: $v.accepted_by}
elif $task == "defaults" then $v.defaults
elif $task == "sets" then $v.property_sets
elif $task == "groups" then $v.property_groups
elif $task == "value" then
  if $v == null or ($v.values | has($value) | not) then
    {id: $value, status: "unselected"}
  else $v.values[$value] as $row |
    {id: $value, status: "known", name: ($row.name // $value),
     summary: ($row.summary // null),
     maps: [($v.map_order // ($v.maps | keys_unsorted))[] | . as $key | $v.maps[$key] as $map |
       if $map["value-kind"] == "boolean" then
         {key: $key, value: ($v.property_sets[$key] | index($value) != null)}
       elif $v.property_groups | has($key) then
         {key: $key, value: ([$v.property_groups[$key] | to_entries[] |
                             select(.value | index($value) != null) | .key][0] // null)}
       else {key: $key, value: $row[$key]} end],
     context: [$v.maps | to_entries[] | select(.value["value-kind"] == "term") |
       .key as $key | .value["target-vocabulary"] as $target |
       ($row[$key] // []) | (if type == "array" then . else [.] end)[] |
       . as $id | $table[$target].values[$id] as $term |
       if $term == null then error("missing term context")
       else {map: $key, vocabulary: $target, id: $id,
             name: ($term.name // $id), summary: ($term.summary // null)} end]}
  end
else error("unknown task") end
