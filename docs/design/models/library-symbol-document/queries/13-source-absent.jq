# Which Members have no source line, and why?
.members[] | select(.source_status != "complete") | [.canonical_signature, .source_status] | @tsv
