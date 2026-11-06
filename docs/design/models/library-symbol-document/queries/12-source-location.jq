# Where is JsonDocument.Parse(Stream) in source?
.members[] | select(.documentation_id == "M:System.Text.Json.JsonDocument.Parse(System.IO.Stream,System.Text.Json.JsonDocumentOptions)") | [.source_url, .source_line] | @tsv
