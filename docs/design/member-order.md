# Member ordering

`dotnet-inspect` should order type/member sections like Microsoft Learn API
reference pages. This makes the output familiar to .NET developers and keeps
agent workflows aligned with the canonical documentation shape.

Representative Learn pages checked while recording this design:

- [System.String](https://learn.microsoft.com/dotnet/api/system.string)
- [System.DateTime](https://learn.microsoft.com/dotnet/api/system.datetime)
- [System.Linq.Enumerable](https://learn.microsoft.com/dotnet/api/system.linq.enumerable)

Not every type has every member kind, so the complete order has to be inferred
across several APIs. `System.String` is the best single regression target for
this ordering because it exercises many uncommon sections on one page, including
operators, explicit interface implementations, and extension methods.

## Canonical member-kind order

1. Constructors
2. Fields
3. Properties
4. Methods
5. Operators
6. Explicit Interface Implementations
7. Extension Methods
8. Events

Only render sections that have data for the selected type and query. Do not add
empty headings just to preserve positions.

## dotnet-inspect mapping

`Method Groups` is dotnet-inspect's compact summary variant of Learn's
`Methods` section. It occupies the Methods slot in default/broad views. When a
diagnostic or exhaustive view includes both `Method Groups` and `Methods`,
render `Method Groups` immediately before `Methods` because it is a progressive
disclosure summary of the same member family.

The effective order is therefore:

1. Constructors
2. Fields
3. Properties
4. Method Groups
5. Methods
6. Operators
7. Explicit Interface Implementations
8. Extension Methods
9. Events

## Implementation notes

Operators are represented in metadata as special methods with `op_` names.
Type/member views classify them into a first-class `Operators` section so they
do not inflate ordinary method groups.

Explicit interface implementations are often private method definitions, but
they are part of the public interface contract. Type/member views include them
without requiring `--all` and render them in `Explicit Interface
Implementations`.

Extension methods already have a relationship command and library-level
sections. Type/member views expose extension methods defined in the inspected
binary when they extend the inspected type. Broader reachable extension-method
discovery stays in the `extensions` command to avoid surprising scope expansion
and extra work in default type/member views.
