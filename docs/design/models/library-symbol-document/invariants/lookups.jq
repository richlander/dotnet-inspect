# Every lookup equals its derivation from the records.
def idx(pairs): [pairs] | group_by(.k) | map({key: .[0].k, value: (map(.v) | unique)}) | from_entries;
  .parameter_type_members == idx(.members[] | .documentation_id as $id | .parameters[] | select(.modifier != "out") | .type_mentions[] | {k: ., v: $id})
and .return_type_members == idx(.members[] | .documentation_id as $id | .return_type_mentions[] | {k: ., v: $id})
and .produced_type_members == idx(.members[] | .documentation_id as $id | .produced_types[] | {k: ., v: $id})
and .attribute_type_members == idx(.members[] | .documentation_id as $id | .attribute_types[] | {k: ., v: $id})
and .suffix_family_types == idx(.types | to_entries[] | select(.value.suffix_family != "") | {k: .value.suffix_family, v: .key})
and .name_word_types == idx(.types | to_entries[] | select(.value.declaration_kind == "definition") | .key as $t | .value.name_words[] | {k: ., v: $t})
and .base_type_derived_types == idx(.types | to_entries[] | select(.value.base_type != "") | {k: .value.base_type, v: .key})
and .interface_implementing_types == idx(.types | to_entries[] | .key as $t | .value.interfaces[] | {k: ., v: $t})
