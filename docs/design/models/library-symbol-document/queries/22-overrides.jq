# Which Members override a base Member, and which one?
.members[] | select(.overrides != "") | [.documentation_id, .overrides] | @tsv
