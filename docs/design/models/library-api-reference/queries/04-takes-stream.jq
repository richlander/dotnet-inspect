# Which Members take a parameter whose type is exactly System.IO.Stream, not counting out parameters?
.members[] | select(any(.parameters[]; .modifier != "out" and .type == "System.IO.Stream")) | .documentation_id
