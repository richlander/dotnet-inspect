# Which Types are forwarded, and to where?
.types | to_entries[] | select(.value.declaration_kind == "forwarder") | [.key, .value.forwarded_to_assembly] | @tsv
