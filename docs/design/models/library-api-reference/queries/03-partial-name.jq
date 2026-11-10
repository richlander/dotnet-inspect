# Which Members have a name starting with "Parse"?
.members[] | select(.name | startswith("Parse")) | .canonical_signature
