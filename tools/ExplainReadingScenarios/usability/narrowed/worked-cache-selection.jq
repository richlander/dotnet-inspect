["prefix", "dependency-target", "depends", "depends-transitive", "dependency-depth"] as $needed
| {facets: [._embedded.facets[] | select(.key as $key | $needed | index($key))],
   bindings: [._embedded.bindings[]
     | .exposed_facets |= map(select(. as $key | $needed | index($key)))]}
