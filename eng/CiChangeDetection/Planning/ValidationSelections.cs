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
    bool Packaging,
    bool Web,
    bool Skills,
    bool Tla)
{
    /// <summary>
    /// Gets the selections that a change set of every routed kind produces.
    /// </summary>
    internal static RoutingSelections All { get; } = new(
        true, true, true, true, true,
        true, true, true, true);
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
        bool pack,
        bool inspectWeb,
        bool skillGate,
        bool tla)
    {
        Test = test;
        DependencyPolicy = dependencyPolicy;
        CSharpDiffSmoke = cSharpDiffSmoke;
        DecompilerGates = decompilerGates;
        Markdownlint = markdownlint;
        IlDiffSmoke = ilDiffSmoke;
        Pack = pack;
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

    internal bool Pack { get; }

    internal bool InspectWeb { get; }

    internal bool SkillGate { get; }

    internal bool Tla { get; }

    /// <summary>
    /// Applies the repository's event rules to raw routing selections. A
    /// push runs the focused dependency-policy composition gate rather than
    /// the pre-merge test job; documentation lint, the Browser/Wasm lane
    /// and the TLA+ lane have no event gate.
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
            pack: selections.Packaging && preMerge,
            inspectWeb: selections.Web,
            skillGate: selections.Skills && preMerge,
            tla: selections.Tla);
    }
}
