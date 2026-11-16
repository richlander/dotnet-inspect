# Which Members have no source line, and why?
.members[] | select(.source.status != "complete") | [.canonical_signature, .source.status] | @tsv
