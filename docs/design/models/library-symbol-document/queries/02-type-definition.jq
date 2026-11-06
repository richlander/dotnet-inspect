# Which Members does JsonDocument declare? (Type Document)
.members[] | select(.declaring_type == "T:System.Text.Json.JsonDocument") | .signature
