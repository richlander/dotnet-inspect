# No value anywhere is null, every record of a kind has the same fields, and Type collisions hold Type records.
([.. | select(. == null)] | length == 0) and ([.members[] | keys] | unique | length == 1) and ([.types[], .type_collisions[].record | keys] | unique | length == 1) and ([.section_outcomes[] | keys] | unique | length == 1)
