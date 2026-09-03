namespace CiChangeDetection.Planning;

/// <summary>
/// The raw path-routing selections, one per shell-classifier output name.
/// These are pre-event selections: the effective plan fields apply the event
/// rules on top.
/// </summary>
internal readonly record struct RoutingSelections(
    bool Code,
    bool CodeqlActions,
    bool CodeqlCSharp,
    bool CodeqlJavaScript,
    bool CSharpDiff,
    bool Decompiler,
    bool Docs,
    bool IlDiff,
    bool IlRoundtrip,
    bool Packaging,
    bool Shipped,
    bool Web,
    bool Skills,
    bool Tla)
{
    /// <summary>
    /// Gets the selections that a change set of every routed kind produces.
    /// </summary>
    internal static RoutingSelections All { get; } = new(
        true, true, true, true, true, true, true,
        true, true, true, true, true, true, true);
}

/// <summary>
/// The effective validation selections carried by a plan. Every field is a
/// typed selection consumed by a job or a named in-job validation unit.
/// </summary>
internal sealed class ValidationSelections
{
    internal ValidationSelections(
        bool test,
        bool cSharpDiffSmoke,
        bool decompilerGates,
        bool markdownlint,
        bool ilDiffSmoke,
        bool ilRoundTrip,
        bool pack,
        bool buildNet10,
        bool inspectWeb,
        bool skillGate,
        bool tla,
        bool codeqlActions,
        bool codeqlCSharp,
        bool codeqlJavaScript)
    {
        if (ilRoundTrip && !test)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "ilRoundTrip requires test");
        }

        Test = test;
        CSharpDiffSmoke = cSharpDiffSmoke;
        DecompilerGates = decompilerGates;
        Markdownlint = markdownlint;
        IlDiffSmoke = ilDiffSmoke;
        IlRoundTrip = ilRoundTrip;
        Pack = pack;
        BuildNet10 = buildNet10;
        InspectWeb = inspectWeb;
        SkillGate = skillGate;
        Tla = tla;
        CodeqlActions = codeqlActions;
        CodeqlCSharp = codeqlCSharp;
        CodeqlJavaScript = codeqlJavaScript;
    }

    internal bool Test { get; }

    internal bool CSharpDiffSmoke { get; }

    internal bool DecompilerGates { get; }

    internal bool Markdownlint { get; }

    internal bool IlDiffSmoke { get; }

    internal bool IlRoundTrip { get; }

    internal bool Pack { get; }

    internal bool BuildNet10 { get; }

    internal bool InspectWeb { get; }

    internal bool SkillGate { get; }

    internal bool Tla { get; }

    /// <summary>
    /// Gets a value indicating whether CodeQL analyzes GitHub Actions
    /// workflows.
    /// </summary>
    internal bool CodeqlActions { get; }

    /// <summary>
    /// Gets a value indicating whether CodeQL analyzes C# sources.
    /// </summary>
    internal bool CodeqlCSharp { get; }

    /// <summary>
    /// Gets a value indicating whether CodeQL analyzes JavaScript and
    /// TypeScript sources.
    /// </summary>
    internal bool CodeqlJavaScript { get; }

    /// <summary>
    /// Applies the repository's event rules to raw routing selections. A push
    /// runs neither the pre-merge test matrix nor the validations placed
    /// behind it; documentation lint, the Browser/Wasm lane, the TLA+ lane,
    /// and the CodeQL lanes have no event gate. CodeQL keeps running on a
    /// push because code scanning alerts are reported against the default
    /// branch: gating it on the pre-merge event would leave that baseline
    /// frozen at whatever last ran before the merge.
    /// </summary>
    /// <param name="selections">The raw routing selections.</param>
    /// <param name="kind">The provenance kind supplying the event rule.</param>
    /// <returns>The effective validation selections.</returns>
    internal static ValidationSelections FromRouting(
        RoutingSelections selections,
        PlanEventKind kind)
    {
        bool preMerge = kind != PlanEventKind.Push;
        return new ValidationSelections(
            test: selections.Code && preMerge,
            cSharpDiffSmoke: selections.CSharpDiff && preMerge,
            decompilerGates: selections.Decompiler && preMerge,
            markdownlint: selections.Docs,
            ilDiffSmoke: selections.IlDiff && preMerge,
            ilRoundTrip: selections.IlRoundtrip && preMerge,
            pack: selections.Packaging && preMerge,
            buildNet10: selections.Shipped && preMerge,
            inspectWeb: selections.Web,
            skillGate: selections.Skills && preMerge,
            tla: selections.Tla,
            codeqlActions: selections.CodeqlActions,
            codeqlCSharp: selections.CodeqlCSharp,
            codeqlJavaScript: selections.CodeqlJavaScript);
    }
}
