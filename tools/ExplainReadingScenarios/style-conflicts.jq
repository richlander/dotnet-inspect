include "reading";
[choices($format) | {id, conflict_group: optional("conflict_group")}
 | select(.conflict_group != null)]
| group_by(.conflict_group)
| map({group: .[0].conflict_group, choices: map(.id)})
