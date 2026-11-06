"""Lay out extracted records as a Library symbol document.

Reads the extractor's JSON, derives every lookup from the records, and writes
one JSON object with one record per line, as docs/design/library-symbol-document.md
specifies. Usage: python3 -I layout.py <records.json> <out.json>
"""
import json
import sys


def index(pairs):
    out = {}
    for key, value in pairs:
        bucket = out.setdefault(key, [])
        if value not in bucket:
            bucket.append(value)
    return {k: sorted(out[k]) for k in sorted(out)}


def lookups(types, members, collisions=()):
    # Type lookups derive from every Type record, including retained collisions.
    type_records = list(types.items()) + [(c["documentation_id"], c["record"]) for c in collisions]
    return {
        "parameter_type_members": index(
            (t, m["documentation_id"]) for m in members
            for p in m["parameters"] if p["modifier"] != "out" for t in p["type_mentions"]),
        "return_type_members": index(
            (t, m["documentation_id"]) for m in members
            for t in m["return_type_mentions"]),
        "produced_type_members": index(
            (t, m["documentation_id"]) for m in members
            for t in m["produced_types"]),
        "attribute_type_members": index(
            (t, m["documentation_id"]) for m in members
            for t in m["attribute_types"]),
        "suffix_family_types": index(
            (r["suffix_family"], k) for k, r in type_records if r["suffix_family"]),
        "name_word_types": index(
            (w, k) for k, r in type_records if r["declaration_kind"] == "definition" for w in r["name_words"]),
        "base_type_derived_types": index(
            (r["base_type"], k) for k, r in type_records if r["base_type"]),
        "interface_implementing_types": index(
            (i, k) for k, r in type_records for i in r["interfaces"]),
    }


def dump(value):
    return json.dumps(value, ensure_ascii=False)


def dictionary(name, mapping, last=False):
    lines = [f"{dump(name)}: {{"]
    items = list(mapping.items())
    for i, (k, v) in enumerate(items):
        lines.append(f"{dump(k)}: {dump(v)}" + ("," if i < len(items) - 1 else ""))
    lines.append("}" + ("" if last else ","))
    return lines


def main(source, target):
    with open(source, encoding="utf-8") as f:
        data = json.load(f)
    types, members = data["types"], data["members"]
    outcomes = data["section_outcomes"]
    settled = {"complete", "not_requested", "offline"}
    trailer = {
        "section_outcomes": outcomes,
        "counts": {"types": len(types) + len(data["type_collisions"]), "members": len(members)},
        "outcome": "complete" if all(o["status"] in settled for o in outcomes) else "partial",
    }

    out = ["{"]
    for key in ("schema_version", "generator", "library", "request"):
        out.append(f"{dump(key)}: {dump(data[key])},")
    out += dictionary("types", types)
    collisions = data["type_collisions"]
    out.append('"type_collisions": [')
    for i, c in enumerate(collisions):
        out.append(dump(c) + ("," if i < len(collisions) - 1 else ""))
    out.append("],")
    out.append('"members": [')
    for i, m in enumerate(members):
        out.append(dump(m) + ("," if i < len(members) - 1 else ""))
    out.append("],")
    for key, value in lookups(types, members, collisions).items():
        out += dictionary(key, value)
    items = list(trailer.items())
    for i, (key, value) in enumerate(items):
        out.append(f"{dump(key)}: {dump(value)}" + ("," if i < len(items) - 1 else ""))
    out.append("}")

    with open(target, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out) + "\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
