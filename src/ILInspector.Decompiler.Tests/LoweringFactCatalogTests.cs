using System.Reflection;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LoweringFactCatalogTests
{
    [Fact]
    public void CatalogDiscoversSidecarProviders()
    {
        var entries = DiscoverFactEntries().Cast<LoweringFactEntry>().ToList();

        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.Await");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.CollectionExpression");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.ForEachStatement");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.Index");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.IsPatternOperator");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.ConditionalAccess");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.LockStatement");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.NullCoalescingAssignmentOperator");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.ObjectOrCollectionInitializerExpression");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.PatternSwitchStatement");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.Range");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.StringInterpolation");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.TupleBinaryOperator");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.TupleCreationExpression");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.UsingStatement");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.Yield");
        Assert.Contains(entries, e => e.Key.ToString() == "LocalRewriter.DeconstructionAssignmentOperator");
        Assert.Contains(entries, e => e.Key.ToString() == "ClosureConversion.Lambda");
        Assert.Contains(entries, e => e.Key.ToString() == "ClosureConversion.LocalFunction");
        Assert.Contains(entries, e => e.Key.ToString() == "ClosureConversion.CapturedClosure");
        Assert.Contains(entries, e => e.Key.ToString() == "ClosureConversion.ExpressionTreeLambda");
    }

    [Fact]
    public void EntriesTargetExistingCoverageRowsAndMechanisms()
    {
        var rows = CoverageRows();

        foreach (var entry in DiscoverFactEntries().Cast<LoweringFactEntry>())
        {
            Assert.True(rows.TryGetValue(entry.Key.ToString(), out var mechanism),
                $"{entry.Key}: fact sidecar targets no LoweringCoverage/ClosureCoverage row.");
            Assert.Equal(mechanism, entry.Mechanism);
            Assert.False(entry.RequiredFacts.IsDefaultOrEmpty, $"{entry.Key}: no required facts named.");
            Assert.All(entry.RequiredFacts, fact =>
            {
                Assert.False(string.IsNullOrWhiteSpace(fact.Id), $"{entry.Key}: empty fact id.");
                Assert.False(string.IsNullOrWhiteSpace(fact.Evidence), $"{entry.Key}/{fact.Id}: empty evidence.");
            });
            Assert.False(string.IsNullOrWhiteSpace(entry.PositiveCoverage), $"{entry.Key}: positive coverage is empty.");
            Assert.False(string.IsNullOrWhiteSpace(entry.AdversarialCoverage), $"{entry.Key}: adversarial coverage is empty.");
        }
    }

    [Fact]
    public void KnownSubstrateDependentCoverageRowsHaveSidecarEntries()
    {
        var entries = DiscoverFactEntries().Cast<LoweringFactEntry>()
            .ToDictionary(e => e.Key);

        foreach (var requirement in SubstrateDependentCoverageRows())
        {
            Assert.True(entries.TryGetValue(requirement.Key, out var entry),
                $"{requirement.Key}: {requirement.Reason} Add a fact sidecar entry or record an explicit exemption in this test.");
            Assert.Equal(requirement.Mechanism, entry.Mechanism);

            foreach (var primitive in requirement.RequiredPrimitiveIds)
            {
                Assert.Contains(entry.RequiredFacts, fact => fact.Id == primitive);
            }
        }
    }

    [Fact]
    public void EntriesAreUniquePerCoverageRow()
    {
        var duplicates = DiscoverFactEntries().Cast<LoweringFactEntry>()
            .GroupBy(e => e.Key)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.ToString())
            .Order()
            .ToArray();

        Assert.True(duplicates.Length == 0,
            "Fact sidecars should keep one entry per coverage row; duplicates fragment the work queue: "
                + string.Join(", ", duplicates));
    }

    static Dictionary<string, Type> CoverageRows()
    {
        var rows = new Dictionary<string, Type>(StringComparer.Ordinal);
        AddRows(rows, typeof(LoweringCoverage), "LocalRewriter");
        AddRows(rows, typeof(ClosureCoverage), "ClosureConversion");
        return rows;
    }

    static IEnumerable<SidecarRequirement> SubstrateDependentCoverageRows() =>
    [
        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.LockStatement)),
            typeof(LockSugarPass),
            [
                "member.corelib-identity:Monitor.Enter",
                "member.corelib-identity:Monitor.Exit",
                "ownership.local-references-only-within",
            ],
            "LockSugarPass composes exact Monitor member identity and ReferenceOwnership proofs."),

        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.ConditionalAccess)),
            typeof(NullConditionalPass),
            [
                "place.variable",
                "type-shape.non-nullable-value",
            ],
            "NullConditionalPass composes PlaceIdentity and type-shape proofs."),

        new(
            new LoweringFactKey(LoweringFactRegister.LocalRewriter, nameof(LoweringCoverage.NullCoalescingAssignmentOperator)),
            typeof(NullCoalescingAssignmentPass),
            [
                "place.variable",
                "place.operands",
                "type-shape.non-nullable-value",
                "dataflow.stack-slot-single-assignment",
            ],
            "NullCoalescingAssignmentPass composes PlaceIdentity, value-type, and spill-confinement proofs."),
    ];

    sealed record SidecarRequirement(
        LoweringFactKey Key,
        Type Mechanism,
        string[] RequiredPrimitiveIds,
        string Reason);

    static void AddRows(Dictionary<string, Type> rows, Type register, string name)
    {
        foreach (var property in register.GetProperties(BindingFlags.Public | BindingFlags.Static))
            rows.Add($"{name}.{property.Name}", property.PropertyType);
    }

    static List<object> DiscoverFactEntries()
    {
        var entries = new List<object>();
        foreach (var type in typeof(ILoweringFactProvider).Assembly.GetTypes())
        {
            if (type is { IsAbstract: false, IsInterface: false }
                && typeof(ILoweringFactProvider).IsAssignableFrom(type)
                && Activator.CreateInstance(type) is ILoweringFactProvider provider)
            {
                entries.AddRange(provider.Entries);
            }
        }
        return entries;
    }
}
