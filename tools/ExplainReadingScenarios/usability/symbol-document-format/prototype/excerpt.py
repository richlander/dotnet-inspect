"""Select an excerpt of extracted records for the design vectors.

Usage: python3 -I excerpt.py <records.json> <out-records.json> <type-id>... -- <member-id>...
Run layout.py on the output; the excerpt's lookups and counts then describe only
the excerpt, and every record stays identical to the complete document's.
"""
import json
import sys

source, target = sys.argv[1], sys.argv[2]
rest = sys.argv[3:]
split = rest.index("--")
type_ids, member_ids = rest[:split], set(rest[split + 1:])
with open(source, encoding="utf-8") as f:
    data = json.load(f)
data["types"] = {k: v for k, v in data["types"].items() if k in type_ids}
data["members"] = [m for m in data["members"] if m["documentation_id"] in member_ids]
missing = member_ids - {m["documentation_id"] for m in data["members"]}
if missing or len(data["types"]) != len(type_ids):
    sys.exit(f"missing: {sorted(missing)} types={len(data['types'])}/{len(type_ids)}")
with open(target, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False)
