# Which Members return exactly JsonDocument?
.members[] | select(.return_type == "System.Text.Json.JsonDocument") | .canonical_signature
