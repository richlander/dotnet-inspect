# Which abstract Members must a Converter-family subclass override?
.members[] | select(.declaring_type_suffix_family == "Converter" and .is_abstract) | .display_signature
