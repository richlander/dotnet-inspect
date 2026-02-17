# Backlog

## Use Markout MarkoutField for demo list

The `demo list` output currently formats the numbered list manually. The
existing `MarkoutField` type renders as a compact inline summary
(`Key: Value | Key: Value`), which works for metadata but not for a
16-item numbered list. This item is blocked until Markout adds a
vertical/list rendering mode for `MarkoutField` or a dedicated
`MarkoutList` type.

```csharp
// Desired: vertical labeled list with bold keys
// 1. Insight  What does the generic math hierarchy look like?
//            dotnet-inspect api System.Runtime "INumber<TSelf>" --shape
```
