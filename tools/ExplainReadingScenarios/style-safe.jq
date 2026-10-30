include "reading";
[choices($format) | select(one("byte_divergent") == false)
 | {id, name, oracle_endorsed: one("oracle_endorsed")}]
