# Member Index

The `Member Index` section is the canonical overload-selection surface for
member queries. It is intentionally separate from informational overload
sections such as `Methods`: those sections explain APIs, while `Member Index`
provides terse, copyable selectors.

## Columns

| Column | Purpose |
| ------ | ------- |
| `Selector` | Human/interactive selector, such as `Serialize:1`. It is compact but tied to the current overload order. |
| `Stable` | Durable selector, such as `Serialize~1dc14dd1fb`. It is based on the canonical signature digest. |
| `Canonical Signature` | Printed source string used to compute the stable selector digest. |

Example:

```text
M:System.Text.Json.JsonSerializer.Serialize<TValue>(TValue,System.Text.Json.JsonSerializerOptions?)
```

## Canonical signature shape

Canonical signatures are identity-only. They do not include return type arrows,
parameter names, default values, or documentation.

| Member kind | Shape |
| ----------- | ----- |
| Method | `M:Namespace.Type.Member(Type1,Type2)` |
| Generic method | `M:Namespace.Type.Member<T>(T,System.String)` |
| Constructor | `M:Namespace.Type.#ctor(Type1,Type2)` |
| Property | `P:Namespace.Type.Property` |
| Field | `F:Namespace.Type.Field` |
| Event | `E:Namespace.Type.Event` |

Type names are the same canonical display names printed in member signatures,
with parameter names and defaults removed. Generic argument lists and nullable
annotations are preserved where they are part of the printed signature identity.

## Digest contract

The `Stable` selector digest is the first 10 lowercase hex characters of:

```text
SHA256("dotnet-inspect.member-index.v1\n" + canonical_signature)
```

The version prefix (`dotnet-inspect.member-index.v1`) is part of the contract
and is included in the hash. It is a domain-separation/version prefix: the same
canonical signature hashed for another purpose would not share this digest, and
incompatible canonicalization changes can move to a later suffix while keeping
old selectors understandable.

`canonical_signature` is the raw `Canonical Signature` cell value, not Markdown
formatting. In Markdown output Markout may render the value as inline code, but
the backticks are not part of the hash input. Prefer `--tsv` or `--jsonl` when
copying canonical signatures for recomputation because those formats expose the
raw value.

Digest prefixes are accepted by `Name~digest`. If a prefix matches multiple
members, the command reports matching candidates and asks for a longer prefix.

If this ever needs to be mentioned in `SKILL.md`, keep it to one sentence:
`Name~digest` hashes `dotnet-inspect.member-index.v1\n` plus the raw
`Canonical Signature` text, without Markdown backticks.

## Selector guidance

Use `Selector` (`Name:N`) for immediate interactive drill-in after viewing the
same index. Use `Stable` (`Name~digest`) in docs, scripts, issues, and agent
handoffs.
