# Which Members mention "stream" in their documentation summary?
.members[] | select(.documentation.summary | test("stream"; "i")) | .canonical_signature
