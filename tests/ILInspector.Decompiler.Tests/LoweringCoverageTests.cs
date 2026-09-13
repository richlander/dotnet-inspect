using System.Reflection;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Guards the lowering ledger — a self-checking map of the whole pipeline.
/// Two Roslyn-forward registers (<see cref="LoweringCoverage"/> for
/// <c>LocalRewriter</c>, <see cref="ClosureCoverage"/> for closures) map each
/// compiler lowering to a mechanism (the property type) and a completeness level
/// (<see cref="CompletenessAttribute"/>); <see cref="NativePasses"/> captures the
/// passes that invert no Roslyn lowering. These tests enforce the cross-axis
/// invariants, the checked-in Roslyn snapshot, non-full gap notes, and the
/// partition: every <see cref="IrPasses.Default"/> pass type is classified by
/// one register.
/// </summary>
public class LoweringCoverageTests
{
    // The checked-in denominator: every LocalRewriter_*.cs in dotnet/roslyn
    // src/Compilers/CSharp/Portable/Lowering/LocalRewriter/, synced @ main.
    // PropertySet_ExactlyMatchesRoslynLocalRewriters guards this list against the
    // ledger's properties — an in-repo consistency check. Keeping the list itself
    // current with upstream Roslyn is a human responsibility (no live fetch at test
    // time); when Roslyn adds/renames a lowering, a maintainer re-fetches and updates
    // this list and the ledger together. Re-fetch:
    //   gh api repos/dotnet/roslyn/contents/src/Compilers/CSharp/Portable/Lowering/LocalRewriter \
    //     --jq '.[] | select(.name | startswith("LocalRewriter_") and endswith(".cs")) | .name | sub("^LocalRewriter_"; "") | sub("\\.cs$"; "")'
    static readonly string[] RoslynLocalRewriters =
    [
        "AnonymousObjectCreation", "AsOperator", "AssignmentOperator", "Await",
        "BasePatternSwitchLocalRewriter", "BinaryOperator", "Block", "BreakStatement",
        "Call", "CollectionExpression", "CompoundAssignmentOperator", "ConditionalAccess",
        "ConditionalOperator", "ContinueStatement", "Conversion", "DeconstructionAssignmentOperator",
        "DelegateCreationExpression", "DoStatement", "Event", "ExpressionStatement",
        "Field", "FixedStatement", "ForEachStatement", "ForStatement",
        "FunctionPointerInvocation", "GotoStatement", "HostObjectMemberReference", "IfStatement",
        "Index", "IndexerAccess", "IsOperator", "IsPatternOperator",
        "LabeledStatement", "Literal", "LocalDeclaration", "LockStatement",
        "MultipleLocalDeclarations", "NullCoalescingAssignmentOperator", "NullCoalescingOperator",
        "ObjectCreationExpression", "ObjectOrCollectionInitializerExpression", "PatternSwitchStatement",
        "PointerElementAccess", "PreviousSubmissionReference", "PropertyAccess", "Query",
        "Range", "ReturnStatement", "StackAlloc", "StringConcat",
        "StringInterpolation", "SwitchExpression", "ThrowStatement", "TryStatement",
        "TupleBinaryOperator", "TupleCreationExpression", "UnaryOperator", "UsingStatement",
        "WhileStatement", "Yield",
    ];

    // Roslyn-forward registers: mechanism is the property type, completeness is the
    // attribute. Owed counts are derived from the entries so raise PRs only edit the
    // row they improve rather than a central counter.
    static readonly (Type Type, string Name)[] CoverageRegisters =
    [
        (typeof(LoweringCoverage), "LocalRewriter"),
        (typeof(ClosureCoverage), "ClosureConversion"),
    ];

    static PropertyInfo[] Props(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Static);
    static bool IsPass(PropertyInfo p) => typeof(IIrPass).IsAssignableFrom(p.PropertyType);

    // NOTE: keep the internal CompletenessLevel / CompletenessAttribute / NativeAttribute
    // types out of any member SIGNATURE here — the fidelity gate reconstructs this
    // test assembly as a whole-module skeleton and a signature referencing an
    // internal decompiler type fails to recompile. Read them inside method bodies.

    [Fact]
    public void PropertySet_ExactlyMatchesCheckedInRoslynLocalRewriterSnapshot()
    {
        var properties = Props(typeof(LoweringCoverage)).Select(p => p.Name).ToHashSet();
        var roslyn = RoslynLocalRewriters.ToHashSet();

        var missing = roslyn.Except(properties).Order().ToArray();
        var extra = properties.Except(roslyn).Order().ToArray();

        Assert.True(missing.Length == 0,
            $"Checked-in Roslyn lowerings with no LoweringCoverage property (add them, Unhandled/None until raised): {string.Join(", ", missing)}");
        Assert.True(extra.Length == 0,
            $"LoweringCoverage properties that are not checked-in Roslyn lowerings (remove or rename): {string.Join(", ", extra)}");
        Assert.Equal(RoslynLocalRewriters.Length, properties.Count);
    }

    [Fact]
    public void CoverageRegisters_CrossAxisInvariants_Hold()
    {
        var registered = IrPasses.Default.Select(p => p.GetType()).ToHashSet();

        foreach (var (type, name) in CoverageRegisters)
            foreach (var p in Props(type))
            {
                var attr = p.GetCustomAttribute<CompletenessAttribute>();
                Assert.True(attr is not null, $"{name}.{p.Name}: missing [Completeness] — every entry states its completeness axis.");
                var level = attr!.Level;

                if (p.PropertyType == typeof(ImporterNative))
                    Assert.True(level == CompletenessLevel.Full, $"{name}.{p.Name}: importer-native must be Full.");
                else if (p.PropertyType == typeof(Unhandled))
                    Assert.True(level == CompletenessLevel.None, $"{name}.{p.Name}: Unhandled (owed) must be None.");
                else if (p.PropertyType == typeof(Declined))
                    Assert.True(level == CompletenessLevel.None, $"{name}.{p.Name}: Declined (no IL anchor) must be None — none of the idiom comes back.");
                else if (IsPass(p))
                {
                    Assert.True(level is CompletenessLevel.Full or CompletenessLevel.Partial,
                        $"{name}.{p.Name}: a pass-handled lowering is Full or Partial, not None.");
                    Assert.True(registered.Contains(p.PropertyType),
                        $"{name}.{p.Name} -> {p.PropertyType.Name}: pass is not registered in IrPasses.Default.");
                }
                else
                    Assert.Fail($"{name}.{p.Name}: unknown mechanism type {p.PropertyType.Name} (expected a pass, ImporterNative, Unhandled, or Declined).");
            }
    }

    // The owed roadmap — "what's left to raise?" — is exactly the Unhandled rows.
    // Declined rows are also (None) but carry NO owed work: they have no IL anchor,
    // so they must never be counted as owed (the trap that nearly spawned a QueryPass
    // during the LINQ work). This pins the two markers as disjoint roadmap classes.
    [Fact]
    public void DeclinedRows_AreNotOwed_AndAreDistinctFromUnhandled()
    {
        var declined = CoverageRegisters
            .SelectMany(r => Props(r.Type))
            .Where(p => p.PropertyType == typeof(Declined))
            .Select(p => p.Name)
            .ToArray();
        var owed = CoverageRegisters
            .SelectMany(r => Props(r.Type))
            .Where(p => p.PropertyType == typeof(Unhandled))
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Contains("Query", declined);
        foreach (var name in declined)
            Assert.DoesNotContain(name, owed);
    }

    [Fact]
    public void NativePasses_AreRegistered_AndDisjointFromCoverage()
    {
        var registered = IrPasses.Default.Select(p => p.GetType()).ToHashSet();
        var coveragePassTypes = CoverageRegisters.SelectMany(r => Props(r.Type)).Where(IsPass).Select(p => p.PropertyType).ToHashSet();

        foreach (var p in Props(typeof(NativePasses)))
        {
            Assert.True(IsPass(p), $"NativePasses.{p.Name}: must be a pass type ({p.PropertyType.Name} is not an IIrPass).");
            var native = p.GetCustomAttribute<NativeAttribute>();
            Assert.True(native is not null, $"NativePasses.{p.Name}: missing [Native] — say what it reconstructs.");
            Assert.False(string.IsNullOrWhiteSpace(native!.What),
                $"NativePasses.{p.Name}: [Native] What is empty — the whole point of this register is to say what the pass reconstructs.");
            Assert.True(registered.Contains(p.PropertyType), $"NativePasses.{p.Name} -> {p.PropertyType.Name}: not in IrPasses.Default.");
            Assert.False(coveragePassTypes.Contains(p.PropertyType),
                $"NativePasses.{p.Name}: {p.PropertyType.Name} also inverts a Roslyn lowering — it belongs in a coverage register, not here.");
        }
    }

    [Fact]
    public void EveryPipelinePassType_IsClassified_ByOneRegister()
    {
        var defaultTypes = IrPasses.Default.Select(p => p.GetType()).ToHashSet();
        var coveragePassTypes = CoverageRegisters.SelectMany(r => Props(r.Type)).Where(IsPass).Select(p => p.PropertyType).ToHashSet();
        var nativeTypes = Props(typeof(NativePasses)).Select(p => p.PropertyType).ToHashSet();
        var classified = coveragePassTypes.Concat(nativeTypes).ToHashSet();

        var unclassified = defaultTypes.Except(classified).Select(t => t.Name).Order().ToArray();
        var phantom = classified.Except(defaultTypes).Select(t => t.Name).Order().ToArray();

        Assert.True(unclassified.Length == 0,
            $"Pipeline pass types classified by neither a Roslyn-forward register nor NativePasses: {string.Join(", ", unclassified)}");
        Assert.True(phantom.Length == 0,
            $"Ledger references pass types not in IrPasses.Default: {string.Join(", ", phantom)}");
    }

    // Where the mechanism axis already tells the full story (a Full importer-native
    // lowering needs no caveat), a note is optional. But anything LESS than Full is a
    // gap, and an undocumented gap is a silent "trust me": a Partial row must say what
    // it does NOT cover, an owed (None/Unhandled) row must name the idiom it owes, and a
    // declined (None/Declined) row must state its no-IL-anchor rationale. This keeps the
    // "finish these" / "start these" roadmap from decaying into bare property names.
    [Fact]
    public void NonFullCoverageEntries_CarryAGapNote()
    {
        foreach (var (type, name) in CoverageRegisters)
            foreach (var p in Props(type))
            {
                var attr = p.GetCustomAttribute<CompletenessAttribute>()!;
                if (attr.Level == CompletenessLevel.Full)
                    continue;

                Assert.False(string.IsNullOrWhiteSpace(attr.Note),
                    $"{name}.{p.Name}: {attr.Level} with no note. A Partial row must state what it does not cover; "
                        + "an owed (Unhandled) row must name the idiom it owes; a declined (Declined) row must state "
                        + "its no-IL-anchor rationale — otherwise the gap is undocumented.");
            }
    }
}
