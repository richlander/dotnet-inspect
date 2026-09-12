using CSharpText;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using ILInspector.Metadata.TypeDependencyFixtures;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for TypeDependencyScanner — walks type hierarchies upward.
/// Uses platform ref assemblies for realistic metadata.
/// </summary>
public class TypeDependencyScannerTests
{
    private static readonly string[] RefAssemblies = GetRefAssemblyPaths();

    private static string[] GetRefAssemblyPaths()
    {
        // Find the ref pack directory for the current runtime
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string root;

        if (dotnetRoot != null)
        {
            root = dotnetRoot;
        }
        else
        {
            // Assembly.Location is under shared/Microsoft.NETCore.App/<version>/ — walk up to dotnet root
            var sharedDir = Path.GetDirectoryName(Path.GetDirectoryName(typeof(object).Assembly.Location))
                ?? "/usr/lib/dotnet/shared/Microsoft.NETCore.App";
            root = Path.GetFullPath(Path.Combine(sharedDir, "..", ".."));
        }

        var refDir = Directory.GetDirectories(Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref"))
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (refDir == null)
            return [];

        var netDir = Directory.GetDirectories(Path.Combine(refDir, "ref"))
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (netDir == null)
            return [];

        return Directory.GetFiles(netDir, "*.dll");
    }

    [Fact]
    public void Stream_HasExpectedDependencies()
    {
        var result = TypeDependencyScanner.BuildDependencyTree("Stream", RefAssemblies);

        Assert.NotEmpty(result.Tree);

        var names = result.Tree.Select(n => n.TypeName).ToList();
        Assert.Contains(names, n => n == "System.IDisposable");
        Assert.Contains(names, n => n == "System.IAsyncDisposable");
        // Stream extends MarshalByRefObject
        Assert.Contains(names, n => n == "System.MarshalByRefObject");
    }

    [Fact]
    public void IDisposable_HasNoDependencies()
    {
        // IDisposable is a leaf — no base interfaces
        var result = TypeDependencyScanner.BuildDependencyTree("IDisposable", RefAssemblies);

        Assert.Empty(result.Tree);
    }

    [Fact]
    public void UnknownType_ReturnsEmpty()
    {
        var result = TypeDependencyScanner.BuildDependencyTree("NonExistentType12345", RefAssemblies);

        Assert.Empty(result.Tree);
    }

    [Fact]
    public void EmptyAssemblyList_ReturnsEmpty()
    {
        var result = TypeDependencyScanner.BuildDependencyTree("Stream", []);

        Assert.Empty(result.Tree);
    }

    [Fact]
    public void IComparable_Generic_HasNoDependencies()
    {
        // IComparable<T> is a leaf interface
        var result = TypeDependencyScanner.BuildDependencyTree("IComparable", RefAssemblies);

        Assert.True(result.Found);
        Assert.Empty(result.Tree);
    }

    [Fact]
    public void Tree_DeduplicatesExpansions()
    {
        // INumber<TSelf> has many transitive deps;
        // each type should be expanded (shown with children) at most once
        var result = TypeDependencyScanner.BuildDependencyTree("INumber", RefAssemblies);

        Assert.NotEmpty(result.Tree);

        // Collect all expanded nodes (those with children)
        var expandedNames = new List<string>();
        CollectExpandedNames(result.Tree, expandedNames);

        // Each expanded name should appear exactly once
        var duplicates = expandedNames
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Relationships_RetainEverySharedDagEdge()
    {
        TypeDependencyResult result =
            TypeDependencyScanner.BuildDependencyTree(
                "Int128",
                RefAssemblies);

        Assert.Equal(CountNodes(result.Tree), result.Relationships.Count);
        Assert.Contains(
            result.Relationships
                .GroupBy(
                    static relationship => relationship.TargetTypeName,
                    StringComparer.OrdinalIgnoreCase),
            static incoming => incoming.Count() > 1);
    }

    [Fact]
    public void Relationships_DepthOneStopsAfterDirectEdges()
    {
        TypeDependencyResult result =
            TypeDependencyScanner.BuildDependencyTree(
                "Int128",
                RefAssemblies,
                maximumDepth: 1);

        Assert.NotEmpty(result.Relationships);
        Assert.All(
            result.Relationships,
            relationship => Assert.Equal(
                result.MatchedType,
                relationship.SourceTypeName));
        Assert.All(
            result.Tree,
            node => Assert.Empty(node.Children));
    }

    [Fact]
    public void DepthBoundaries_RetainExactBoundedTypeIdentities()
    {
        TypeDependencyResult unbounded =
            TypeDependencyScanner.BuildDependencyTree(
                "Int128",
                RefAssemblies);
        TypeDependencyResult bounded =
            TypeDependencyScanner.BuildDependencyTree(
                "Int128",
                RefAssemblies,
                maximumDepth: 1);

        Assert.NotEmpty(bounded.DepthBoundaries);
        Assert.All(
            bounded.DepthBoundaries,
            boundary =>
            {
                Assert.Equal(1, boundary.MaximumDepth);
                Assert.Contains(
                    bounded.Relationships,
                    relationship =>
                        relationship.TargetTypeName
                            == boundary.TypeName);
                Assert.Contains(
                    unbounded.Relationships,
                    relationship =>
                        relationship.SourceTypeName
                            == boundary.TypeName);
                Assert.DoesNotContain(
                    bounded.Relationships,
                    relationship =>
                        relationship.SourceTypeName
                            == boundary.TypeName);
            });
    }

    [Fact]
    public void Relationships_DepthBoundUsesShortestPathExpansionBudget()
    {
        const int maximumDepth = 4;
        TypeDependencyResult unbounded =
            TypeDependencyScanner.BuildDependencyTree(
                "Int128",
                RefAssemblies);
        TypeDependencyResult bounded =
            TypeDependencyScanner.BuildDependencyTree(
                "Int128",
                RefAssemblies,
                maximumDepth);

        var firstReachedDepth = new Dictionary<string, int>(
            StringComparer.Ordinal)
        {
            [unbounded.MatchedType!] = 0,
        };
        foreach (TypeDependencyRelationship relationship
            in unbounded.Relationships)
        {
            int sourceDepth = firstReachedDepth[relationship.SourceTypeName];
            firstReachedDepth.TryAdd(
                relationship.TargetTypeName,
                sourceDepth + 1);
        }

        Dictionary<string, int> shortestDepth =
            FindShortestDepths(unbounded);
        string boundaryFirstType = Assert.Single(
            firstReachedDepth
                .Where(pair =>
                    pair.Value == maximumDepth
                    && shortestDepth[pair.Key] < maximumDepth
                    && unbounded.Relationships.Any(relationship =>
                        relationship.SourceTypeName == pair.Key))
                .Select(static pair => pair.Key));

        var expected = unbounded.Relationships
            .Where(relationship =>
                shortestDepth[relationship.SourceTypeName] < maximumDepth)
            .Select(RelationshipIdentity)
            .ToHashSet();
        var actual = bounded.Relationships
            .Select(RelationshipIdentity)
            .ToHashSet();

        Assert.Equal(actual.Count, bounded.Relationships.Count);
        Assert.True(
            expected.SetEquals(actual),
            $"Expected bounded relationships for children of "
                + $"{boundaryFirstType}.");
    }

    [Fact]
    public void Relationships_ExpandDistinctConstructedGenericTypes()
    {
        TypeDependencyResult result =
            TypeDependencyScanner.BuildDependencyTree(
                typeof(TypeDependencyConstructedRoot).FullName!,
                [typeof(TypeDependencyConstructedRoot).Assembly.Location]);

        Assert.Equal(6, result.Relationships.Count);
        Assert.Contains(
            result.Relationships,
            static relationship =>
                relationship.SourceTypeName.EndsWith(
                    "TypeDependencyGenericShared<int>",
                    StringComparison.Ordinal)
                && relationship.TargetTypeName.EndsWith(
                    "TypeDependencyGenericBase<int>",
                    StringComparison.Ordinal));
        Assert.Contains(
            result.Relationships,
            static relationship =>
                relationship.SourceTypeName.EndsWith(
                    "TypeDependencyGenericShared<string>",
                    StringComparison.Ordinal)
                && relationship.TargetTypeName.EndsWith(
                    "TypeDependencyGenericBase<string>",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Relationships_PreserveCaseDistinctTypeIdentities()
    {
        TypeDependencyResult result =
            TypeDependencyScanner.BuildDependencyTree(
                typeof(TypeDependencyCaseRoot).FullName!,
                [typeof(TypeDependencyCaseRoot).Assembly.Location]);

        Assert.Equal(4, result.Relationships.Count);
        Assert.Contains(
            result.Relationships,
            static relationship =>
                relationship.SourceTypeName.EndsWith(
                    "TypeDependencyCaseBranch",
                    StringComparison.Ordinal)
                && relationship.TargetTypeName.EndsWith(
                    "TypeDependencyCaseLeaf",
                    StringComparison.Ordinal));
        Assert.Contains(
            result.Relationships,
            static relationship =>
                relationship.SourceTypeName.EndsWith(
                    "TypeDependencyCasebranch",
                    StringComparison.Ordinal)
                && relationship.TargetTypeName.EndsWith(
                    "TypeDependencyCaseleaf",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ExactLookup_PrefersCaseDistinctSemanticIdentity()
    {
        string target = typeof(TypeDependencyCasebranch).FullName!;
        TypeDependencyResult result =
            TypeDependencyScanner.BuildDependencyTree(
                target,
                [typeof(TypeDependencyCasebranch).Assembly.Location]);

        Assert.Equal(target, result.MatchedType);
        TypeDependencyRelationship relationship =
            Assert.Single(result.Relationships);
        Assert.EndsWith(
            "TypeDependencyCaseleaf",
            relationship.TargetTypeName,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Relationships_ExpandCaseDistinctConstructedGenericTypes()
    {
        TypeDependencyResult result =
            TypeDependencyScanner.BuildDependencyTree(
                typeof(TypeDependencyCaseGenericRoot).FullName!,
                [typeof(TypeDependencyCaseGenericRoot).Assembly.Location]);

        Assert.Equal(6, result.Relationships.Count);
        Assert.Contains(
            result.Relationships,
            static relationship =>
                relationship.SourceTypeName.EndsWith(
                    "TypeDependencyGenericShared"
                    + "<ILInspector.Metadata.TypeDependencyFixtures."
                    + "TypeDependencyCaseValue>",
                    StringComparison.Ordinal)
                && relationship.TargetTypeName.EndsWith(
                    "TypeDependencyGenericBase"
                    + "<ILInspector.Metadata.TypeDependencyFixtures."
                    + "TypeDependencyCaseValue>",
                    StringComparison.Ordinal));
        Assert.Contains(
            result.Relationships,
            static relationship =>
                relationship.SourceTypeName.EndsWith(
                    "TypeDependencyGenericShared"
                    + "<ILInspector.Metadata.TypeDependencyFixtures."
                    + "TypeDependencyCasevalue>",
                    StringComparison.Ordinal)
                && relationship.TargetTypeName.EndsWith(
                    "TypeDependencyGenericBase"
                    + "<ILInspector.Metadata.TypeDependencyFixtures."
                    + "TypeDependencyCasevalue>",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Relationships_DoNotBorrowCaseDistinctDefinitionWhenExactTypeIsMissing()
    {
        const string root =
            "ILInspector.Metadata.TypeDependencyCrossAssemblyFixtures.Root";
        FixtureDefinition consumer =
            FixtureCatalog.MetadataTypeDependencyConsumer;
        FixtureDefinition reference =
            FixtureCatalog.MetadataTypeDependencyReference;

        TypeDependencyResult missingReference =
            TypeDependencyScanner.BuildDependencyTree(
                root,
                [consumer.AssemblyPath()]);

        Assert.Contains(
            missingReference.Relationships,
            static relationship =>
                relationship.SourceTypeName.EndsWith(
                    ".Root",
                    StringComparison.Ordinal)
                && relationship.TargetTypeName.EndsWith(
                    ".Casebranch",
                    StringComparison.Ordinal));
        Assert.All(
            missingReference.Relationships,
            static relationship => Assert.EndsWith(
                ".Root",
                relationship.SourceTypeName,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            missingReference.Relationships,
            static relationship =>
                relationship.TargetTypeName.EndsWith(
                    ".UnrelatedLeaf",
                    StringComparison.Ordinal));

        TypeDependencyResult resolvedReference =
            TypeDependencyScanner.BuildDependencyTree(
                root,
                [consumer.AssemblyPath(), reference.AssemblyPath()]);

        Assert.Equal(2, resolvedReference.Relationships.Count);
        Assert.Contains(
            resolvedReference.Relationships,
            static relationship =>
                relationship.SourceTypeName.EndsWith(
                    ".Casebranch",
                    StringComparison.Ordinal)
                && relationship.TargetTypeName.EndsWith(
                    ".ActualLeaf",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            resolvedReference.Relationships,
            static relationship =>
                relationship.SourceTypeName.EndsWith(
                    ".Casebranch",
                    StringComparison.Ordinal)
                && relationship.TargetTypeName.EndsWith(
                    ".UnrelatedLeaf",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ConcreteType_ResolvesTypeArguments()
    {
        // Int128 implements interfaces with concrete type arguments
        var result = TypeDependencyScanner.BuildDependencyTree("Int128", RefAssemblies);

        Assert.NotEmpty(result.Tree);

        var allNames = new List<string>();
        CollectNames(result.Tree, allNames);

        // Should have concrete type args like System.Int128, not just TSelf
        Assert.Contains(allNames, n => n.Contains("System.Int128"));
    }

    [Fact]
    public void DirectDepsOnly_ExcludesTransitive()
    {
        // INumber<TSelf> directly inherits from INumberBase<TSelf>
        // INumberBase<TSelf> inherits from IAdditionOperators, etc.
        // The root nodes of INumber should NOT include IAdditionOperators
        // (it should only appear as a child of INumberBase)
        var result = TypeDependencyScanner.BuildDependencyTree("INumber", RefAssemblies);

        Assert.NotEmpty(result.Tree);

        var directNames = result.Tree.Select(n => FqnParser.NormalizeTypeName(n.TypeName)).ToList();

        // INumberBase should be a direct dep of INumber
        Assert.Contains(directNames, n => TypeMatcher.GetBaseName(n).EndsWith("INumberBase"));

        // IAdditionOperators is a transitive dep (through INumberBase), not direct
        Assert.DoesNotContain(directNames, n => TypeMatcher.GetBaseName(n).EndsWith("IAdditionOperators"));
    }

    [Fact]
    public void SystemRoot_IsExcluded()
    {
        // Object, ValueType, Enum should never appear in the tree
        var result = TypeDependencyScanner.BuildDependencyTree("Int128", RefAssemblies);

        var allNames = new List<string>();
        CollectNames(result.Tree, allNames);

        Assert.DoesNotContain(allNames, n => n == "System.Object");
        Assert.DoesNotContain(allNames, n => n == "System.ValueType");
        Assert.DoesNotContain(allNames, n => n == "System.Enum");
    }

    [Fact]
    public void DescriptorPopulation_FoundAndAllHealthyIsComplete()
    {
        const string root =
            "ILInspector.Metadata.TypeDependencyCrossAssemblyFixtures.Root";
        ResolvedAssemblyReference consumer =
            Descriptor(
                FixtureCatalog.MetadataTypeDependencyConsumer
                    .AssemblyPath());
        ResolvedAssemblyReference reference =
            Descriptor(
                FixtureCatalog.MetadataTypeDependencyReference
                    .AssemblyPath());

        TypeDependencyPopulationResult result =
            TypeDependencyScanner.BuildDependencyPopulation(
                root,
                [consumer, reference]);
        TypeDependencyResult pathResult =
            TypeDependencyScanner.BuildDependencyTree(
                root,
                [
                    FixtureCatalog.MetadataTypeDependencyConsumer
                        .AssemblyPath(),
                    FixtureCatalog.MetadataTypeDependencyReference
                        .AssemblyPath(),
                ]);

        Assert.True(result.HasSurvivingParticipant);
        Assert.True(result.IsComplete);
        Assert.True(result.Dependency.Found);
        Assert.Equal(pathResult.MatchedType, result.Dependency.MatchedType);
        Assert.Equal(
            TreeShape(pathResult.Tree),
            TreeShape(result.Dependency.Tree));
        Assert.Equal(
            pathResult.Relationships,
            result.Dependency.Relationships);
        Assert.Equal(
            [consumer.Registration, reference.Registration],
            result.Candidates.Select(
                static candidate => candidate.Registration));
        Assert.Same(
            consumer.Registration,
            result.MatchedRegistration);
        Assert.All(
            result.Candidates,
            static candidate =>
                Assert.IsType<
                    TypeDependencyCandidateOutcome.Completed>(
                        candidate));
    }

    [Fact]
    public void DescriptorPopulation_DepthBoundRetainsTypedBoundaries()
    {
        Type target = typeof(TypeDependencyConstructedRoot);
        ResolvedAssemblyReference assembly =
            Descriptor(target.Assembly.Location);

        TypeDependencyPopulationResult result =
            TypeDependencyScanner.BuildDependencyPopulation(
                target.FullName!,
                [assembly],
                maximumDepth: 1);

        Assert.True(result.IsComplete);
        Assert.NotEmpty(result.Dependency.Relationships);
        Assert.All(
            result.Dependency.Relationships,
            relationship => Assert.Equal(
                result.Dependency.MatchedType,
                relationship.SourceTypeName));
        Assert.NotEmpty(result.Dependency.DepthBoundaries);
        Assert.All(
            result.Dependency.DepthBoundaries,
            boundary => Assert.Equal(1, boundary.MaximumDepth));
    }

    [Fact]
    public void DescriptorPopulation_NotFoundAndAllHealthyIsCertified()
    {
        ResolvedAssemblyReference assembly =
            Descriptor(
                typeof(TypeDependencyScannerTests)
                    .Assembly.Location);

        TypeDependencyPopulationResult result =
            TypeDependencyScanner.BuildDependencyPopulation(
                "No.Such.Type",
                [assembly]);

        Assert.True(result.HasSurvivingParticipant);
        Assert.True(result.IsComplete);
        Assert.False(result.Dependency.Found);
        Assert.Empty(result.Dependency.Tree);
        Assert.Empty(result.Dependency.Relationships);
        Assert.Null(result.MatchedRegistration);
    }

    [Fact]
    public void DescriptorPopulation_RejectionMakesSurvivingGraphIncomplete()
    {
        const string root =
            "ILInspector.Metadata.TypeDependencyCrossAssemblyFixtures.Root";
        ResolvedAssemblyReference rejected =
            Descriptor(
                FixtureCatalog.MetadataTypeDependencyConsumer
                    .AssemblyPath(),
                selectedName: "WrongIdentity");
        ResolvedAssemblyReference healthy =
            Descriptor(
                FixtureCatalog.MetadataTypeDependencyConsumer
                    .AssemblyPath());

        TypeDependencyPopulationResult result =
            TypeDependencyScanner.BuildDependencyPopulation(
                root,
                [rejected, healthy]);

        Assert.True(result.HasSurvivingParticipant);
        Assert.True(result.Dependency.Found);
        Assert.False(result.IsComplete);
        var failure =
            Assert.IsType<TypeDependencyCandidateOutcome.Rejected>(
                result.Candidates[0]);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            failure.Failure.Kind);
        Assert.Same(rejected.Registration, failure.Registration);
        Assert.IsType<TypeDependencyCandidateOutcome.Completed>(
            result.Candidates[1]);
        Assert.Same(
            healthy.Registration,
            result.Candidates[1].Registration);
    }

    [Fact]
    public void DescriptorPopulation_RejectionCleanupCannotAbortHealthyNeighbor()
    {
        const string root =
            "ILInspector.Metadata.TypeDependencyCrossAssemblyFixtures.Root";
        string path =
            FixtureCatalog.MetadataTypeDependencyConsumer
                .AssemblyPath();
        ResolvedAssemblyReference rejected =
            Descriptor(
                path,
                selectedName: "WrongIdentity",
                openRead: () =>
                    new ThrowingDisposeStream(
                        File.ReadAllBytes(path)));
        ResolvedAssemblyReference healthy =
            Descriptor(path);

        TypeDependencyPopulationResult result =
            TypeDependencyScanner.BuildDependencyPopulation(
                root,
                [rejected, healthy]);

        Assert.True(result.Dependency.Found);
        Assert.False(result.IsComplete);
        Assert.IsType<TypeDependencyCandidateOutcome.Rejected>(
            result.Candidates[0]);
        Assert.IsType<TypeDependencyCandidateOutcome.Completed>(
            result.Candidates[1]);
    }

    [Fact]
    public void DescriptorPopulation_ReleaseAttemptsEveryCompletedImage()
    {
        string path =
            FixtureCatalog.MetadataTypeDependencyConsumer
                .AssemblyPath();
        byte[] bytes = File.ReadAllBytes(path);
        int disposeCount = 0;
        ResolvedAssemblyReference first =
            Descriptor(
                path,
                openRead: () =>
                    new ThrowingDisposeStream(
                        bytes,
                        () => disposeCount++));
        ResolvedAssemblyReference second =
            Descriptor(
                path,
                openRead: () =>
                    new ThrowingDisposeStream(
                        bytes,
                        () => disposeCount++));

        AggregateException failure =
            Assert.Throws<AggregateException>(() =>
                TypeDependencyScanner.BuildDependencyPopulation(
                    "No.Such.Type",
                    [first, second]));

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal(2, disposeCount);
    }

    [Fact]
    public void DescriptorPopulation_AllRejectedIsUnavailableInInputOrder()
    {
        string path =
            typeof(TypeDependencyScannerTests).Assembly.Location;
        ResolvedAssemblyReference first =
            Descriptor(path, selectedName: "WrongFirst");
        ResolvedAssemblyReference second =
            Descriptor(path, selectedName: "WrongSecond");

        TypeDependencyPopulationResult result =
            TypeDependencyScanner.BuildDependencyPopulation(
                "No.Such.Type",
                [first, second]);

        Assert.False(result.HasSurvivingParticipant);
        Assert.False(result.IsComplete);
        Assert.False(result.Dependency.Found);
        Assert.Equal(
            [first.Registration, second.Registration],
            result.Candidates.Select(
                static candidate => candidate.Registration));
        Assert.All(
            result.Candidates,
            static candidate =>
            {
                var rejected =
                    Assert.IsType<
                        TypeDependencyCandidateOutcome.Rejected>(
                            candidate);
                Assert.Equal(
                    CandidateOpenFailureKind.InvalidImage,
                    rejected.Failure.Kind);
            });
    }

    private static ResolvedAssemblyReference Descriptor(
        string path,
        string? selectedName = null,
        Func<Stream>? openRead = null)
    {
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "type dependency scanner tests"));
        return ResolvedAssemblyReference.Create(
            source.Identity with
            {
                Name = selectedName ?? source.Identity.Name,
            },
            path: null,
            openRead ?? (() => File.OpenRead(path)),
            source.Provenance,
            source.LastWriteTimeUtc);
    }

    private sealed class ThrowingDisposeStream(
        byte[] bytes,
        Action? onDispose = null)
        : MemoryStream(bytes, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                onDispose?.Invoke();
                throw new IOException(
                    "Synthetic dependency-scan cleanup failure.");
            }
        }
    }

    private static void CollectNames(List<TypeDependencyNode> nodes, List<string> result)
    {
        foreach (var node in nodes)
        {
            result.Add(node.TypeName);
            CollectNames(node.Children, result);
        }
    }

    private static void CollectExpandedNames(List<TypeDependencyNode> nodes, List<string> result)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Count > 0)
            {
                result.Add(FqnParser.NormalizeTypeName(node.TypeName));
                CollectExpandedNames(node.Children, result);
            }
        }
    }

    private static int CountNodes(IReadOnlyList<TypeDependencyNode> nodes) =>
        nodes.Count
        + nodes.Sum(static node => CountNodes(node.Children));

    private static Dictionary<string, int> FindShortestDepths(
        TypeDependencyResult result)
    {
        var depths = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [result.MatchedType!] = 0,
        };
        var pending = new Queue<string>();
        pending.Enqueue(result.MatchedType!);

        while (pending.TryDequeue(out string? source))
        {
            int childDepth = depths[source] + 1;
            foreach (TypeDependencyRelationship relationship
                in result.Relationships.Where(relationship =>
                    relationship.SourceTypeName == source))
            {
                if (depths.TryAdd(
                    relationship.TargetTypeName,
                    childDepth))
                {
                    pending.Enqueue(relationship.TargetTypeName);
                }
            }
        }

        return depths;
    }

    private static (
        string Source,
        string Target,
        TypeDependencyRelationshipKind Kind) RelationshipIdentity(
            TypeDependencyRelationship relationship) =>
        (
            relationship.SourceTypeName,
            relationship.TargetTypeName,
            relationship.Kind);

    private static IEnumerable<string> TreeShape(
        IReadOnlyList<TypeDependencyNode> nodes,
        string prefix = "")
    {
        foreach (TypeDependencyNode node in nodes)
        {
            yield return prefix + node.TypeName;
            foreach (string child in TreeShape(
                node.Children,
                prefix + "  "))
            {
                yield return child;
            }
        }
    }
}
