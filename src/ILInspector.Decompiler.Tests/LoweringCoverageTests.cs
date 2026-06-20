using System.Reflection;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Guards the <see cref="LoweringCoverage"/> ledger — the typed completeness
/// checklist mapping each Roslyn <c>LocalRewriter</c> lowering to the raising
/// pass that inverts it (docs/decompiler.md). The ledger is the roadmap; these
/// tests keep it honest: it cannot silently drift from Roslyn, claim a pass that
/// is not wired, or let owed-idiom coverage regress.
/// </summary>
public class LoweringCoverageTests
{
    // The authoritative denominator: every file in dotnet/roslyn
    // src/Compilers/CSharp/Portable/Lowering/LocalRewriter/ (LocalRewriter_*.cs),
    // synced @ main. Re-fetch with:
    //   gh api repos/dotnet/roslyn/contents/src/Compilers/CSharp/Portable/Lowering/LocalRewriter \
    //     --jq '.[].name' | sed 's/^LocalRewriter_//;s/\.cs$//'
    // When this changes (a new C# lowering), add the matching LoweringCoverage
    // property (NoImpl until raised) and update this list in the same PR.
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

    // Ratchet: owed idioms (NoImpl) must only go DOWN. Implementing one (flipping
    // its property from NoImpl to a pass) lowers this; tighten it in that PR.
    const int OwedCeiling = 15;

    static PropertyInfo[] Ledger() =>
        typeof(LoweringCoverage).GetProperties(BindingFlags.Public | BindingFlags.Static);

    [Fact]
    public void PropertySet_ExactlyMatchesRoslynLocalRewriters()
    {
        var properties = Ledger().Select(p => p.Name).ToHashSet();
        var roslyn = RoslynLocalRewriters.ToHashSet();

        var missing = roslyn.Except(properties).Order().ToArray();
        var extra = properties.Except(roslyn).Order().ToArray();

        Assert.True(missing.Length == 0,
            $"Roslyn lowerings with no LoweringCoverage property (add them, NoImpl until raised): {string.Join(", ", missing)}");
        Assert.True(extra.Length == 0,
            $"LoweringCoverage properties that are not Roslyn lowerings (remove or fix the name): {string.Join(", ", extra)}");
        Assert.Equal(RoslynLocalRewriters.Length, properties.Count);
    }

    [Fact]
    public void EveryImplementedEntry_IsRegisteredInThePipeline()
    {
        var registered = IrPasses.Default.Select(p => p.GetType()).ToHashSet();

        var phantom = Ledger()
            .Where(p => typeof(IIrPass).IsAssignableFrom(p.PropertyType))
            .Where(p => !registered.Contains(p.PropertyType))
            .Select(p => $"{p.Name} -> {p.PropertyType.Name}")
            .ToArray();

        Assert.True(phantom.Length == 0,
            $"Ledger claims a raising pass that is not registered in IrPasses.Default: {string.Join(", ", phantom)}");
    }

    [Fact]
    public void OwedIdioms_DoNotRegress()
    {
        var owed = Ledger()
            .Where(p => p.PropertyType == typeof(NoImpl))
            .Select(p => $"{p.Name} ({((NoImpl)p.GetValue(null)!).Form})")
            .Order()
            .ToArray();

        int implemented = Ledger().Count(p => typeof(IIrPass).IsAssignableFrom(p.PropertyType));
        int direct = Ledger().Count(p => p.PropertyType == typeof(Direct));

        Assert.True(owed.Length <= OwedCeiling,
            $"Owed idioms rose to {owed.Length} (ceiling {OwedCeiling}). If you implemented one, lower the ceiling; "
            + $"if Roslyn added a lowering, account for it.\nCoverage: {implemented} raised, {owed.Length} owed, {direct} direct.\n"
            + $"Owed roadmap:\n  {string.Join("\n  ", owed)}");
    }
}
