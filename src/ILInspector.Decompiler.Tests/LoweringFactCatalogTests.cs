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

    [Fact]
    public void PrimitiveIdsUseRegisteredNamespaces()
    {
        var unknown = AllPrimitives()
            .Select(fact => fact.Id)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !FactPrimitiveCatalog.IsRegistered(id))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(unknown.Length == 0,
            "Fact primitive ids must use a registered namespace (FactPrimitiveCatalog.Namespaces) "
                + "or an allow-listed shape primitive (FactPrimitiveCatalog.ShapePrimitives). "
                + "Add the new primitive to the registry intentionally, or fix the typo: "
                + string.Join(", ", unknown));
    }

    [Fact]
    public void PrimitiveIdsHaveNoCaseVariantAliases()
    {
        var aliases = AllPrimitives()
            .Select(fact => fact.Id)
            .Distinct(StringComparer.Ordinal)
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => "{" + string.Join(" | ", group.Order(StringComparer.Ordinal)) + "}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(aliases.Length == 0,
            "Fact primitive ids that differ only by case are duplicate aliases for the same predicate; "
                + "unify them to one canonical spelling: " + string.Join(", ", aliases));
    }

    [Fact]
    public void SubstrateEvidenceReferencesStillBind()
    {
        var stale = new List<string>();

        foreach (var fact in AllPrimitives())
        {
            foreach (var (typeName, member) in FactPrimitiveCatalog.SubstrateReferences(fact.Evidence))
            {
                var type = FactPrimitiveCatalog.SubstratePredicateTypes.First(t => t.Name == typeName);
                var bound = type.GetMember(member,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

                if (bound.Length == 0)
                    stale.Add($"{fact.Id}: evidence references missing {typeName}.{member}");
            }
        }

        Assert.True(stale.Count == 0,
            "Fact primitive evidence references a substrate predicate that no longer exists; "
                + "update the evidence to a real method or remove the stale reference: "
                + string.Join("; ", stale));
    }

    static IEnumerable<FactPrimitive> AllPrimitives()
        => DiscoverFactEntries().Cast<LoweringFactEntry>().SelectMany(entry => entry.RequiredFacts);

    static Dictionary<string, Type> CoverageRows()
    {
        var rows = new Dictionary<string, Type>(StringComparer.Ordinal);
        AddRows(rows, typeof(LoweringCoverage), "LocalRewriter");
        AddRows(rows, typeof(ClosureCoverage), "ClosureConversion");
        return rows;
    }

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
