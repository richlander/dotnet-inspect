# Which abstract classes or interfaces have obligations that may come from outside this Library?
.types | to_entries[] | select(.value.required_overrides_complete | not) | [.key, .value.base_type] | @tsv
