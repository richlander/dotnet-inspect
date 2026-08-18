namespace DotnetInspector.Options;

internal static class CallGraphFieldSelection
{
    const string ProbeSection = "Call Graph";

    static readonly Markout.DocumentSchema AsyncAlternativesSchema =
        new Markout.DocumentSchema().Add(
            ProbeSection,
            "field",
            "Async",
            "AsyncAlternative",
            "AsyncAlternatives");

    internal static bool IsAsyncAlternatives(string fieldName) =>
        AsyncAlternativesSchema.ValidateProjection(
            ProbeSection,
            [fieldName]).Resolved.Length > 0;
}
