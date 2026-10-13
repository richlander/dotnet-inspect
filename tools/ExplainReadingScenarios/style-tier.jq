include "reading";
[choices($format) | select(one("tier") == $tier)
 | {id, name, option: one("option"), value: one("value")}]
