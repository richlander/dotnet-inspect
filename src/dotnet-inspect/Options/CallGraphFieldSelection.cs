namespace DotnetInspector.Options;

internal enum CallGraphField
{
    Fanout,
    Fanin,
    Depth,
    Loop,
    Root,
    Source,
    Allocations,
    Copies,
    Unsafe,
    Reflection,
    Throws,
    ExceptionTypes,
    Catches,
    Finallys,
    EvidenceIL,
    AsyncAlternatives,
}

internal static class CallGraphFieldSelection
{
    const string SectionName = "Call Graph";

    static readonly Definition[] Definitions =
    [
        Define(CallGraphField.Fanout, "Fanout", "FanoutCount"),
        Define(CallGraphField.Fanin, "Fanin", "FaninCount"),
        Define(CallGraphField.Depth, "Depth", "MaxDepth"),
        Define(CallGraphField.Loop, "Loop", "InLoop", "Looping"),
        Define(CallGraphField.Root, "Root", "RootKind", "Classification"),
        Define(CallGraphField.Source, "Source", "Assembly"),
        Define(CallGraphField.Allocations, "Alloc", "Allocations"),
        Define(CallGraphField.Copies, "Copy", "Copies"),
        Define(CallGraphField.Unsafe, "Unsafe"),
        Define(CallGraphField.Reflection, "Reflection"),
        Define(CallGraphField.Throws, "Throw", "Throws", "ThrowSites"),
        Define(
            CallGraphField.ExceptionTypes,
            "Exceptions",
            "ExceptionTypes",
            "ConstructedExceptions"),
        Define(CallGraphField.Catches, "Catch", "Catches"),
        Define(CallGraphField.Finallys, "Finally", "Finallys"),
        Define(CallGraphField.EvidenceIL, "EvidenceIL", "Evidence", "IL"),
        Define(
            CallGraphField.AsyncAlternatives,
            "Async",
            "AsyncAlternative",
            "AsyncAlternatives"),
    ];

    internal static string[] Names { get; } =
        [.. Definitions.SelectMany(static definition => definition.Names)];

    internal static IReadOnlyList<CallGraphField> Resolve(
        IReadOnlyList<string> patterns)
    {
        var resolved = new List<CallGraphField>();
        var seen = new HashSet<CallGraphField>();
        foreach (string pattern in patterns)
        {
            foreach (Definition definition in Definitions)
            {
                if (seen.Contains(definition.Field)
                    || definition.Schema.ValidateProjection(
                        SectionName,
                        [pattern]).Resolved.Length == 0)
                {
                    continue;
                }

                seen.Add(definition.Field);
                resolved.Add(definition.Field);
            }
        }

        return resolved;
    }

    static Definition Define(
        CallGraphField field,
        params string[] names) =>
        new(
            field,
            names,
            new Markout.DocumentSchema().Add(
                SectionName,
                "field",
                names));

    readonly record struct Definition(
        CallGraphField Field,
        string[] Names,
        Markout.DocumentSchema Schema);
}
