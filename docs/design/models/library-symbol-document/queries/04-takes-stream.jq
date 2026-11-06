# Which Members take a parameter whose type is exactly System.IO.Stream?
.members[] | select(any(.parameters[]; .type == "System.IO.Stream")) | .documentation_id
