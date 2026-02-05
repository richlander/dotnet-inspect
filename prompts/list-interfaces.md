# List\<T\> Interfaces

Which interfaces does List\<T\> implement?

## Optimal Path (Expert, Markdown)

```bash
dotnet-inspect api "List<T>" --platform System.Collections --interfaces
```

## Optimal Path (Expert, JSON)

```bash
dotnet-inspect api "List<T>" --platform System.Collections --json | jq '.interfaces'
```

## Discovery Path (Learning)

Doesn't know where List\<T\> lives, uses find to discover:

```bash
# Step 1: Find where List<T> is defined
dotnet-inspect find "List*" --framework runtime

# Output shows: System.Collections.Generic.List`1 in System.Collections.dll

# Step 2: Get the type details with interfaces
dotnet-inspect type "List<T>" --platform System.Collections

# Or for just interfaces in JSON:
dotnet-inspect api "List<T>" --platform System.Collections --json | jq '.interfaces'
```

## Expected Output

Should show: IList\<T\>, ICollection\<T\>, IEnumerable\<T\>, IReadOnlyList\<T\>, IReadOnlyCollection\<T\>, IList, ICollection, IEnumerable

## Key Learnings

- Use `find` when you don't know which assembly contains a type
- Use C# generic syntax: `List<T>`, `Dictionary<TKey, TValue>` (tool converts to metadata format)
- List\<T\> is in `System.Collections` assembly (not System.Collections.Generic package)
- Use `--platform` for SDK assemblies (no download needed)
- Use `--interfaces` flag or `--json` to see interface information
- The `type` command shows interfaces by default in tree view
