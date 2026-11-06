# Which Members mention "stream" in their documentation summary?
.members[] | select(.summary | test("stream"; "i")) | .canonical_signature
