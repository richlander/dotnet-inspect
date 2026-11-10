# Which Members are declared by Types in the Converter name family?
.members[] | select(.declaring_type_suffix_family == "Converter") | .canonical_signature
