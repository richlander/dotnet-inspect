# Usage: --arg form direct|hal|source --arg task inventory|preparation|context|binding|missing
# Normalize for the reading questions, not by mechanically stripping HAL links.
def records:
  if $form == "direct" then .facets
  elif $form == "hal" or $form == "source" then
    ._embedded.facets | map({key: .key, value: del(.key)}) | from_entries
  else error("unknown form") end;
def binding:
  if $form == "direct" then (.bindings.cli // (.bindings | to_entries[0].value))
  elif $form == "hal" or $form == "source" then ._embedded.bindings[0]
  else error("unknown form") end;
def meaning:
  {summary, operators, value_kind, examples: (.examples // []),
   effects: (.effects // []), input_rules: (.input_rules // []),
   values: (.values // []), requires: (.requires // [])};
. as $doc | records as $facets | binding as $binding |
if $task == "inventory" then
  [$binding.exposed_facets[] | . as $key | $facets[$key] | {key: $key, summary}]
elif $task == "preparation" then
  $facets | with_entries(.value |= meaning)
elif $task == "context" then
  $facets | with_entries(.value |=
    [(.requires // [])[] | . as $key | $facets[$key]
      | if . == null then error("missing selected context: " + $key)
        else {key: $key, meaning: meaning,
              exposed: ($binding.exposed_facets | index($key) != null)} end])
elif $task == "binding" then
  $binding | {gesture, input_rules, exposed_facets}
elif $task == "missing" then
  {key: "license", selected: ($facets | has("license")),
   status: (if $facets | has("license") then "known" else "unselected" end)}
else error("unknown task") end
