# Which Members override a base Member, and which one? An empty target means the base Member is outside this Library.
.members[] | select(.is_override) | [.documentation_id, (if .overrides == "" then "(outside this Library)" else .overrides end)] | @tsv
