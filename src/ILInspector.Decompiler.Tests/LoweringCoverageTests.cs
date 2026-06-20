using System.Reflection;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Guards the <see cref="LoweringCoverage"/> ledger — the two-axis completeness
/// checklist mapping each Roslyn <c>LocalRewriter</c> lowering to a mechanism (the
/// property type: a raising pass, <see cref="ImporterNative"/>, or
/// <see cref="Unhandled"/>) and a completeness level (the
/// <see cref="CompletenessAttribute"/>). These keep it honest: it cannot drift
/// from Roslyn, claim an unregistered pass, violate the cross-axis invariants, or
/// let owed-idiom coverage regress.
/// </summary>
public class LoweringCoverageTests
{
    // The authoritative denominator: every LocalRewriter_*.cs in dotnet/roslyn
    // src/Compilers/CSharp/Portable/Lowering/LocalRewriter/, synced @ main. Re-fetch:
    //   gh api repos/dotnet/roslyn/contents/src/Compilers/CSharp/Portable/Lowering/LocalRewriter \
    //     --jq '.[].name' | sed 's/^LocalRewriter_//;s/\.cs$//'
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

    // Ratchet: owed idioms (mechanism Unhandled / completeness None) only go DOWN.
    // Implementing one lowers it; tighten this in that PR.
    const int OwedCeiling = 15;

    static PropertyInfo[] Ledger() =>
        typeof(LoweringCoverage).GetProperties(BindingFlags.Public | BindingFlags.Static);

    static bool IsPass(PropertyInfo p) => typeof(IIrPass).IsAssignableFrom(p.PropertyType);

    // NOTE: keep the internal CompletenessLevel / CompletenessAttribute types out
    // of any member SIGNATURE here — the fidelity gate reconstructs this test
    // assembly as a whole-module skeleton and a signature referencing an internal
    // decompiler type fails to recompile. Read the attribute inside method bodies
    // only (skeleton stubs erase bodies).

    [Fact]
    public void PropertySet_ExactlyMatchesRoslynLocalRewriters()
    {
        var properties = Ledger().Select(p => p.Name).ToHashSet();
        var roslyn = RoslynLocalRewriters.ToHashSet();

        var missing = roslyn.Except(properties).Order().ToArray();
        var extra = properties.Except(roslyn).Order().ToArray();

        Assert.True(missing.Length == 0,
            $"Roslyn lowerings with no LoweringCoverage property (add them, Unhandled/None until raised): {string.Join(", ", missing)}");
        Assert.True(extra.Length == 0,
            $"LoweringCoverage properties that are not Roslyn lowerings (remove or rename): {string.Join(", ", extra)}");
        Assert.Equal(RoslynLocalRewriters.Length, properties.Count);
    }

    [Fact]
    public void CrossAxisInvariants_Hold()
    {
        var registered = IrPasses.Default.Select(p => p.GetType()).ToHashSet();

        foreach (var p in Ledger())
        {
            var attr = p.GetCustomAttribute<CompletenessAttribute>();
            Assert.True(attr is not null, $"{p.Name}: missing [Completeness] — every entry states its completeness axis.");
            var level = attr!.Level;

            if (p.PropertyType == typeof(ImporterNative))
                Assert.True(level == CompletenessLevel.Full, $"{p.Name}: importer-native must be Full (it is built directly).");
            else if (p.PropertyType == typeof(Unhandled))
                Assert.True(level == CompletenessLevel.None, $"{p.Name}: Unhandled must be None (no mechanism).");
            else if (IsPass(p))
            {
                Assert.True(level is CompletenessLevel.Full or CompletenessLevel.Partial,
                    $"{p.Name}: a pass-handled lowering is Full or Partial, not None.");
                Assert.True(registered.Contains(p.PropertyType),
                    $"{p.Name} -> {p.PropertyType.Name}: pass is not registered in IrPasses.Default.");
            }
            else
                Assert.Fail($"{p.Name}: unknown mechanism type {p.PropertyType.Name} (expected a pass, ImporterNative, or Unhandled).");
        }
    }

    [Fact]
    public void OwedIdioms_DoNotRegress_AndReportBothAxes()
    {
        var props = Ledger();

        // Specialization gradient (derived): a pass used by one property is
        // dedicated; by several, a shared/general pass.
        var passUsage = props.Where(IsPass).GroupBy(p => p.PropertyType).ToDictionary(g => g.Key, g => g.Count());

        int dedicated = passUsage.Count(kv => kv.Value == 1);
        int shared = passUsage.Count(kv => kv.Value > 1);
        int sharedProps = passUsage.Where(kv => kv.Value > 1).Sum(kv => kv.Value);
        int importer = props.Count(p => p.PropertyType == typeof(ImporterNative));

        int full = props.Count(p => IsPass(p) && p.GetCustomAttribute<CompletenessAttribute>()!.Level == CompletenessLevel.Full);
        var partial = props.Where(p => IsPass(p) && p.GetCustomAttribute<CompletenessAttribute>()!.Level == CompletenessLevel.Partial)
            .Select(p => $"{p.Name} ({p.GetCustomAttribute<CompletenessAttribute>()!.Note})").Order().ToArray();
        var owed = props.Where(p => p.PropertyType == typeof(Unhandled))
            .Select(p => $"{p.Name} ({p.GetCustomAttribute<CompletenessAttribute>()!.Note})").Order().ToArray();

        string report =
            $"Lowering coverage over {props.Length} Roslyn LocalRewriter phases\n"
            + $"  Completeness (idioms): {full} Full, {partial.Length} Partial, {owed.Length} None "
            + $"(+ {importer} importer-native mechanical lowerings)\n"
            + $"  Specialization: {dedicated} dedicated passes, {shared} shared passes ({sharedProps} entries), {importer} importer-native\n"
            + $"  Partial — finish these:\n    {string.Join("\n    ", partial)}\n"
            + $"  Owed — start these:\n    {string.Join("\n    ", owed)}";

        Assert.True(owed.Length <= OwedCeiling,
            $"Owed idioms rose to {owed.Length} (ceiling {OwedCeiling}). Implementing one lowers it; if Roslyn added a lowering, account for it.\n\n{report}");
    }
}
