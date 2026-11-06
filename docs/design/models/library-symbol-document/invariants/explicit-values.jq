# No value anywhere is null, and every record of a kind has the same fields.
([.. | select(. == null)] | length == 0) and ([.members[] | keys] | unique | length == 1) and ([.types[] | keys] | unique | length == 1)
