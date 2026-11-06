# Every lookup equals its derivation from the records, Type collisions included.
def idx(pairs): [pairs] | group_by(.k) | map({key: .[0].k, value: (map(.v) | unique)}) | from_entries;
def typerecs: (.types | to_entries[]), (.type_collisions[] | {key: .documentation_id, value: .record});
  .parameter_type_members == idx(.members[] | .documentation_id as $id | .parameters[] | select(.modifier != "out") | .type_mentions[] | {k: ., v: $id})
and .return_type_members == idx(.members[] | .documentation_id as $id | .return_type_mentions[] | {k: ., v: $id})
and .produced_type_members == idx(.members[] | .documentation_id as $id | .produced_types[] | {k: ., v: $id})
and .attribute_type_members == idx(.members[] | .documentation_id as $id | .attribute_types[] | {k: ., v: $id})
and .suffix_family_types == idx(typerecs | select(.value.suffix_family != "") | {k: .value.suffix_family, v: .key})
and .name_word_types == idx(typerecs | select(.value.declaration_kind == "definition") | .key as $t | .value.name_words[] | {k: ., v: $t})
and .base_type_derived_types == idx(typerecs | select(.value.base_type != "") | {k: .value.base_type, v: .key})
and .interface_implementing_types == idx(typerecs | .key as $t | .value.interfaces[] | {k: ., v: $t})
