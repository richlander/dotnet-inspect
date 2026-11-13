# These adapters expose the traversal cost; they are probes, not product contracts.
def browser_choices:
  .content.vocabularies[]
  | select(.identity.value == "csharp.style-choices")
  | .terms[]
  | {id: .identity.value, name: .displayLabel, summary,
     maps: (.mapEntries | map({key: .map.value,
       value: [.values[] | if .kind == "term" then .identity.value else .value end]})
       | from_entries)};

# Select only root-declared choice targets from the heterogeneous expansion.
def explained_choices:
  (.relationships["vocabulary-value"].targets | map(.identity)) as $ids
  | ._embedded.resources[]
  | select(.identity as $identity | $ids | index($identity))
  | .facts
  | {id: .identity, name, summary,
     maps: (."map-entries" | map({key: .map, value: [.value.value]}) | from_entries)};

def choices($format):
  if $format == "browser" then browser_choices
  elif $format == "compact" then explained_choices
  else error("unknown input format") end;

def one($map):
  .maps[$map] as $values
  | if ($values | length) == 1 then $values[0]
    else error("expected exactly one value for " + $map) end;

# Optional absence stays explicit; false is a value, not missing data.
def optional($map):
  .maps[$map] as $values
  | if $values == null or ($values | length) == 0 then null
    elif ($values | length) == 1 then $values[0]
    else error("expected at most one value for " + $map) end;
