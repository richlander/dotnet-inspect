namespace CiChangeDetection.Planning;

/// <summary>
/// The raw path-routing selections, one per shell-classifier output name.
/// These are pre-event selections: the effective plan fields apply the event
/// rules on top.
/// </summary>
internal readonly record struct RoutingSelections(
    bool Code,
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
        true, true, true, true, true, true, true, true, true, true, true);
}

/// <summary>
/// The effective validation selections carried by a plan. Every field is a
/// typed selection consumed by a job or a named in-job validation unit.
/// </summary>
internal sealed class ValidationSelections
{
    internal ValidationSelections(
        bool test,
        bool dependencyPolicy,
        bool cSharpDiffSmoke,
        bool decompilerGates,
        bool markdownlint,
        bool ilDiffSmoke,
        bool ilRoundTrip,
        bool pack,
        bool buildNet10,
        bool inspectWeb,
        bool skillGate,
        bool tla)
    {
        if (ilRoundTrip && !test)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "ilRoundTrip requires test");
        }

        Test = test;
        DependencyPolicy = dependencyPolicy;
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
    }

    internal bool Test { get; }

    internal bool DependencyPolicy { get; }

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
    /// Applies the repository's event rules to raw routing selections. A push
    /// runs the focused dependency-policy composition gate rather than the
    /// pre-merge test matrix; documentation lint, the Browser/Wasm lane, and
    /// the TLA+ lane have no event gate.
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
            dependencyPolicy: kind == PlanEventKind.Push,
            cSharpDiffSmoke: selections.CSharpDiff && preMerge,
            decompilerGates: selections.Decompiler && preMerge,
            markdownlint: selections.Docs,
            ilDiffSmoke: selections.IlDiff && preMerge,
            ilRoundTrip: selections.IlRoundtrip && preMerge,
            pack: selections.Packaging && preMerge,
            buildNet10: selections.Shipped && preMerge,
            inspectWeb: selections.Web,
            skillGate: selections.Skills && preMerge,
            tla: selections.Tla);
    }
}
