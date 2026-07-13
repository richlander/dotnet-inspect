extern alias extensionnew;
extern alias extensionold;

using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class MetadataFindingsTests
{
    static readonly FindingSubject Subject = new("api", "API");

    [Fact]
    public void SameSurface_IsExactAndAllPresent()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Get", "int Get()"),
            Property("Name", "string Name { get; }"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Get", "int Get()"),
            Property("Name", "string Name { get; }"),
        ]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        Assert.True(result.IsExact);
        Assert.All(Pairs(result.Types), pair => Assert.Equal(PairKind.Present, pair.Kind));
        Assert.All(Pairs(result.Members), pair => Assert.Equal(PairKind.Present, pair.Kind));
    }

    [Fact]
    public void AddedAndRemovedMembers_AgreeWithApiDiffAnalyzer()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Keep", "void Keep()"),
            Method("Removed", "void Removed()"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Keep", "void Keep()"),
            Method("Added", "void Added()"),
        ]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);
        var expected = GetExpectedMembers(result.ApiDiff);

        Assert.Equal(expected.Added, PairKeys(result.Members, PairKind.Added));
        Assert.Equal(expected.Removed, PairKeys(result.Members, PairKind.Removed));
        Assert.Empty(PairKeys(result.Members, PairKind.Changed));
    }

    [Fact]
    public void MatchedMemberFacetChanges_AreChangedPairs()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Run", "void Run()"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method(
                "Run",
                "void Run()",
                accessibility: "protected",
                isObsolete: true,
                attributes: ["System.ObsoleteAttribute"]),
        ]));

        var result = MetadataFindings.CompareApi(
            oldSurface,
            newSurface,
            Subject,
            new ApiDiffOptions(ApiDiffScope.All));

        var pair = Assert.Single(Pairs(result.Members));
        Assert.Equal(PairKind.Changed, pair.Kind);
        Assert.Contains("accessibility: public -> protected", pair.Detail, StringComparison.Ordinal);
        Assert.Contains("obsolete: false -> true", pair.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchedTypeFacetChanges_AreChangedPairs()
    {
        var oldSurface = Surface(Type("Widget"));
        var newSurface = Surface(Type("Widget", isByRefLike: true));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        var pair = Assert.Single(Pairs(result.Types));
        Assert.Equal(PairKind.Changed, pair.Kind);
        Assert.Contains("byref-like: false -> true", pair.Detail, StringComparison.Ordinal);
        Assert.False(result.IsExact);
        Assert.True(result.ApiDiff.IsEmpty);
    }

    [Fact]
    public void AddedAndRemovedTypes_AgreeWithApiDiffAnalyzer()
    {
        var oldSurface = Surface(Type("Keep"), Type("Removed"));
        var newSurface = Surface(Type("Keep"), Type("Added"));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);
        var pairs = Pairs(result.Types);

        Assert.Contains(pairs, pair =>
            pair is PairFinding<ApiTypeHandle>.Added added
            && added.New.Payload.TypeFullName == "TestNamespace.Added");
        Assert.Contains(pairs, pair =>
            pair is PairFinding<ApiTypeHandle>.Removed removed
            && removed.Old.Payload.TypeFullName == "TestNamespace.Removed");
        Assert.Contains(
            result.ApiDiff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes),
            change => change.Kind == ChangeKind.TypeAdded);
        Assert.Contains(
            result.ApiDiff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes),
            change => change.Kind == ChangeKind.TypeRemoved);
    }

    [Fact]
    public void ReorderedMembers_AreAllPresentUnderIdentitySetMode()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("A", "void A()"),
            Method("B", "void B()"),
            Method("C", "void C()"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("C", "void C()"),
            Method("A", "void A()"),
            Method("B", "void B()"),
        ]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        Assert.True(result.IsExact);
        Assert.Equal(3, Pairs(result.Members).Length);
        Assert.All(Pairs(result.Members), pair => Assert.Equal(PairKind.Present, pair.Kind));
    }

    [Fact]
    public void ApiIdentitySetFindings_DoNotFabricateOrdinals()
    {
        var surface = Surface(Type("Widget", members: [Method("Run", "void Run()")]));

        var type = Assert.IsType<FindingInspection<ApiTypeHandle>.Complete>(
            MetadataFindings.InspectApiTypes(surface, Subject).Value);
        var member = Assert.IsType<FindingInspection<ApiMemberHandle>.Complete>(
            MetadataFindings.InspectApiMembers(surface, Subject).Value);

        Assert.Null(Assert.Single(type.Findings).Ordinal);
        Assert.Null(Assert.Single(member.Findings).Ordinal);
    }

    [Fact]
    public void SignatureChangesRemainConservativeAddRemovePairs()
    {
        var oldSurface = Surface(Type("Widget", members: [Method("Run", "void Run(int value)")]));
        var newSurface = Surface(Type("Widget", members: [Method("Run", "void Run(string value)")]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        Assert.Collection(
            Pairs(result.Members).OrderBy(pair => pair.Kind),
            pair => Assert.Equal(PairKind.Added, pair.Kind),
            pair => Assert.Equal(PairKind.Removed, pair.Kind));
        Assert.Contains(
            result.ApiDiff.TypeDiffs.SelectMany(type => type.Changes),
            change => change.Kind == ChangeKind.MemberSignatureChanged);
    }

    [Fact]
    public void SameRoleExtensionRelocation_DoesNotSoftMatch()
    {
        var oldSurface = Surface(
            Type("Widget"),
            Type("OldExtensions", members:
            [
                StructuredMethod(
                    "Run",
                    "void Run(TestNamespace.Widget value, int count)",
                    "System.Void",
                    [Parameter("TestNamespace.Widget"), Parameter("System.Int32")],
                    isStatic: true,
                    isExtension: true,
                    extendedType: "TestNamespace.Widget"),
            ]));
        var newSurface = Surface(
            Type("Widget"),
            Type("NewExtensions", members:
            [
                StructuredMethod(
                    "Run",
                    "void Run(TestNamespace.Widget value, int count)",
                    "System.Void",
                    [Parameter("TestNamespace.Widget"), Parameter("System.Int32")],
                    isStatic: true,
                    isExtension: true,
                    extendedType: "TestNamespace.Widget"),
            ]));

        var comparison = MetadataFindings.CompareApiMembers(
            oldSurface,
            newSurface,
            Subject,
            acceptanceThreshold: 85);

        Assert.Equal(2, Pairs(comparison).Length);
        Assert.Contains(Pairs(comparison), pair => pair.Kind == PairKind.Added);
        Assert.Contains(Pairs(comparison), pair => pair.Kind == PairKind.Removed);
    }

    [Fact]
    public void ExtensionInstanceParameterMismatch_DoesNotSoftMatch()
    {
        var oldSurface = Surface(
            Type("Widget"),
            Type("WidgetExtensions", members:
            [
                StructuredMethod(
                    "Run",
                    "void Run(TestNamespace.Widget value, int count)",
                    "System.Void",
                    [Parameter("TestNamespace.Widget"), Parameter("System.Int32")],
                    isStatic: true,
                    isExtension: true,
                    extendedType: "TestNamespace.Widget"),
            ]));
        var newSurface = Surface(Type("Widget", members:
        [
            StructuredMethod(
                "Run",
                "void Run(string count)",
                "System.Void",
                [Parameter("System.String")]),
        ]));

        var comparison = MetadataFindings.CompareApiMembers(
            oldSurface,
            newSurface,
            Subject,
            acceptanceThreshold: 85);

        Assert.Equal(2, Pairs(comparison).Length);
        Assert.DoesNotContain(Pairs(comparison), pair => pair.Kind == PairKind.Changed);
    }

    [Fact]
    public void AmbiguousExtensionInstanceEndpoints_DoNotSoftMatch()
    {
        var oldSurface = Surface(
            Type("Widget"),
            Type("WidgetExtensions", members:
            [
                StructuredMethod(
                    "Run",
                    "void Run(TestNamespace.Widget value, int count)",
                    "System.Void",
                    [Parameter("TestNamespace.Widget"), Parameter("System.Int32")],
                    isStatic: true,
                    isExtension: true,
                    extendedType: "TestNamespace.Widget"),
            ]));
        var newSurface = Surface(Type("Widget", members:
        [
            StructuredMethod(
                "Run",
                "void Run(int count)",
                "System.Void",
                [Parameter("System.Int32")]),
            StructuredMethod(
                "Run",
                "void Run(int count)",
                "System.Void",
                [Parameter("System.Int32")]),
        ]));

        var comparison = MetadataFindings.CompareApiMembers(
            oldSurface,
            newSurface,
            Subject,
            acceptanceThreshold: 85);

        Assert.Equal(3, Pairs(comparison).Length);
        Assert.DoesNotContain(Pairs(comparison), pair => pair.Kind == PairKind.Changed);
    }

    [Fact]
    public void ExtensionSoftMatch_CannotDisplaceExactMemberMatch()
    {
        var instance = StructuredMethod(
            "Run",
            "void Run(int count)",
            "System.Void",
            [Parameter("System.Int32")]);
        var oldSurface = Surface(
            Type("Widget", members: [instance]),
            Type("WidgetExtensions", members:
            [
                StructuredMethod(
                    "Run",
                    "void Run(TestNamespace.Widget value, int count)",
                    "System.Void",
                    [Parameter("TestNamespace.Widget"), Parameter("System.Int32")],
                    isStatic: true,
                    isExtension: true,
                    extendedType: "TestNamespace.Widget"),
            ]));
        var newSurface = Surface(Type("Widget", members:
        [
            StructuredMethod(
                "Run",
                "void Run(int count)",
                "System.Void",
                [Parameter("System.Int32")]),
        ]));

        var comparison = MetadataFindings.CompareApiMembers(
            oldSurface,
            newSurface,
            Subject,
            acceptanceThreshold: 85);

        Assert.Equal(2, Pairs(comparison).Length);
        Assert.Single(Pairs(comparison), pair => pair.Kind == PairKind.Present);
        Assert.Single(Pairs(comparison), pair => pair.Kind == PairKind.Removed);
    }

    [Fact]
    public void ExtensionToInstance_DefaultIsConservativeAndOptInIsChanged()
    {
        var oldSurface = Surface(
            Type("Widget"),
            Type("WidgetExtensions", members:
            [
                StructuredMethod(
                    "Run",
                    "void Run(TestNamespace.Widget value, int count)",
                    "System.Void",
                    [Parameter("TestNamespace.Widget"), Parameter("System.Int32")],
                    isStatic: true,
                    isExtension: true,
                    extendedType: "TestNamespace.Widget"),
            ]));
        var newSurface = Surface(Type("Widget", members:
        [
            StructuredMethod(
                "Run",
                "void Run(int count)",
                "System.Void",
                [Parameter("System.Int32")]),
        ]));

        var conservative = MetadataFindings.CompareApiMembers(oldSurface, newSurface, Subject);
        Assert.Equal(2, Pairs(conservative).Length);
        Assert.Contains(Pairs(conservative), pair => pair.Kind == PairKind.Added);
        Assert.Contains(Pairs(conservative), pair => pair.Kind == PairKind.Removed);

        var accepted = MetadataFindings.CompareApiMembers(
            oldSurface,
            newSurface,
            Subject,
            acceptanceThreshold: 85);

        var changed = Assert.IsType<PairFinding<ApiMemberHandle>.Changed>(
            Assert.Single(Pairs(accepted)).Value);
        Assert.NotNull(changed.Match);
        Assert.Equal(MetadataFindings.ExtensionInstanceMatchTier, changed.Match.Tier);
        Assert.Equal(85, changed.Match.Confidence);
        Assert.Contains("soft match: api.member.extension-instance", changed.Detail, StringComparison.Ordinal);
        Assert.Contains("signature:", changed.Detail, StringComparison.Ordinal);
        Assert.Contains("static: true -> false", changed.Detail, StringComparison.Ordinal);
        Assert.Contains("extension: true -> false", changed.Detail, StringComparison.Ordinal);
        Assert.Contains(
            "declaring type: TestNamespace.WidgetExtensions -> TestNamespace.Widget",
            changed.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InstanceToExtension_SoftMatchIsSymmetric()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            StructuredMethod(
                "Run",
                "void Run(int count)",
                "System.Void",
                [Parameter("System.Int32")]),
        ]));
        var newSurface = Surface(
            Type("Widget"),
            Type("WidgetExtensions", members:
            [
                StructuredMethod(
                    "Run",
                    "void Run(TestNamespace.Widget value, int count)",
                    "System.Void",
                    [Parameter("TestNamespace.Widget"), Parameter("System.Int32")],
                    isStatic: true,
                    isExtension: true,
                    extendedType: "TestNamespace.Widget"),
            ]));

        var comparison = MetadataFindings.CompareApiMembers(
            oldSurface,
            newSurface,
            Subject,
            acceptanceThreshold: 85);

        var changed = Assert.IsType<PairFinding<ApiMemberHandle>.Changed>(
            Assert.Single(Pairs(comparison)).Value);
        Assert.Contains("extension: false -> true", changed.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiSurfaceMembersAreNotFilteredByNameHeuristics()
    {
        var oldSurface = Surface(Type("Widget"));
        var newSurface = Surface(Type("Widget", members: [Method("s_value", "void s_value()")]));

        var result = MetadataFindings.CompareApi(oldSurface, newSurface, Subject);

        var added = Assert.Single(Pairs(result.Members));
        Assert.Equal(PairKind.Added, added.Kind);
    }

    [Fact]
    public void TypeScopedCensuses_DistinguishMissingTypeFromExistingEmptyType()
    {
        var missing = Surface(Type("Other"));
        var empty = Surface(Type("Widget"));

        var missingMembers = MetadataFindings.InspectApiMembers(
            missing,
            Subject,
            "TestNamespace.Widget");
        var emptyMembers = MetadataFindings.InspectApiMembers(
            empty,
            Subject,
            "TestNamespace.Widget");

        Assert.IsType<FindingInspection<ApiMemberHandle>.Absent>(missingMembers.Value);
        Assert.Empty(
            Assert.IsType<FindingInspection<ApiMemberHandle>.Complete>(emptyMembers.Value).Findings);
    }

    [Fact]
    public void TypeSelfPresence_RepresentsMissingTypeAsCompleteEmptyCensus()
    {
        var inspection = MetadataFindings.InspectApiType(
            Surface(Type("Other")),
            Subject,
            "TestNamespace.Widget");

        Assert.Empty(
            Assert.IsType<FindingInspection<ApiTypeHandle>.Complete>(inspection.Value).Findings);
    }

    [Fact]
    public void TypeScopedMemberComparison_PreservesFacetClassification()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Run", "void Run()"),
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Run", "void Run()", accessibility: "protected"),
        ]));

        var comparison = MetadataFindings.CompareApiMembers(
            oldSurface,
            newSurface,
            Subject,
            "TestNamespace.Widget");

        var changed = Assert.IsType<PairFinding<ApiMemberHandle>.Changed>(
            Assert.Single(Pairs(comparison)).Value);
        Assert.Contains("accessibility: public -> protected", changed.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TypeAttributeComparison_ExactMatchesBeforeClassifyingValueChanges()
    {
        var oldSurface = Surface(Type("Widget", attributes:
        [
            "System.Obsolete",
            "System.Tag(\"A\")",
            "System.Tag(\"A\")",
        ]));
        var newSurface = Surface(Type("Widget", attributes:
        [
            "System.Tag(\"A\")",
            "System.Tag(\"B\")",
        ]));

        var comparison = MetadataFindings.CompareApiAttributes(
            oldSurface,
            newSurface,
            Subject,
            "TestNamespace.Widget");
        var pairs = Pairs(comparison);

        Assert.Equal(1, pairs.Count(pair => pair.Kind == PairKind.Present));
        Assert.Equal(1, pairs.Count(pair => pair.Kind == PairKind.Changed));
        Assert.Equal(1, pairs.Count(pair => pair.Kind == PairKind.Removed));
        var changed = Assert.IsType<PairFinding<ApiAttributeHandle>.Changed>(
            Assert.Single(pairs, pair => pair.Kind == PairKind.Changed).Value);
        Assert.Equal("System.Tag", changed.Old.Payload.Name);
        Assert.Contains("System.Tag(\"A\") -> System.Tag(\"B\")", changed.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void RealAssemblySelfCompare_IsExact()
    {
        using var stream = File.OpenRead(typeof(ApiDiffAnalyzer).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var result = MetadataFindings.CompareApi(surface, surface, Subject);

        Assert.True(result.IsExact);
        Assert.True(Pairs(result.Members).Length > 50);
    }

    [Fact]
    public void CompiledExtensionToInstance_ProducesSoftChangedCandidate()
    {
        var oldSurface = ExtractSurface(typeof(extensionold::ExtensionInstanceFixture.Widget).Assembly.Location);
        var newSurface = ExtractSurface(typeof(extensionnew::ExtensionInstanceFixture.Widget).Assembly.Location);

        var comparison = MetadataFindings.CompareApiMembers(
            oldSurface,
            newSurface,
            Subject,
            acceptanceThreshold: 85);

        var changed = Assert.Single(
            Pairs(comparison),
            pair => pair is PairFinding<ApiMemberHandle>.Changed
            {
                Old.Payload.Member.Name: "Measure",
                New.Payload.Member.Name: "Measure",
            });
        var matched = Assert.IsAssignableFrom<IMatchedPairFinding>(changed.Value);
        Assert.Equal(MetadataFindings.ExtensionInstanceMatchTier, matched.Match?.Tier);
    }

    static ImmutableArray<PairFinding<T>> Pairs<T>(FindingComparison<T> comparison)
        where T : notnull
        => comparison is FindingComparison<T>.Complete complete
            ? complete.Pairs
            : throw new Xunit.Sdk.XunitException($"Expected a complete comparison; failure: {comparison.Failure}");

    static string[] PairKeys(
        FindingComparison<ApiMemberHandle> comparison,
        PairKind kind)
        => Pairs(comparison)
            .Where(pair => pair.Kind == kind)
            .Select(pair => pair switch
            {
                PairFinding<ApiMemberHandle>.Added added => added.New.Key.IdentityKey,
                PairFinding<ApiMemberHandle>.Removed removed => removed.Old.Key.IdentityKey,
                PairFinding<ApiMemberHandle>.Changed changed => changed.New.Key.IdentityKey,
                PairFinding<ApiMemberHandle>.Present present => present.New.Key.IdentityKey,
                _ => throw new InvalidOperationException(),
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

    static ExpectedMembers GetExpectedMembers(ApiDiff diff)
    {
        var added = new List<string>();
        var removed = new List<string>();
        foreach (var change in diff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes))
        {
            if (change.Subject?.Kind != ApiChangeSubjectKind.Member)
                continue;

            switch (change.Kind)
            {
                case ChangeKind.MemberAdded when change.Subject.NewMember?.CanonicalSignature is { } newKey:
                    added.Add(newKey);
                    break;
                case ChangeKind.MemberRemoved when change.Subject.OldMember?.CanonicalSignature is { } oldKey:
                    removed.Add(oldKey);
                    break;
            }
        }

        return new ExpectedMembers(
            [.. added.Order(StringComparer.Ordinal)],
            [.. removed.Order(StringComparer.Ordinal)]);
    }

    static ApiSurface Surface(params ApiType[] types)
        => new() { Types = [.. types] };

    static ApiSurface ExtractSurface(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(reader);
    }

    static ApiType Type(
        string name,
        string kind = "class",
        string? ns = "TestNamespace",
        bool isByRefLike = false,
        List<ApiMember>? members = null,
        List<string>? attributes = null)
        => new()
        {
            Name = name,
            Kind = kind,
            Namespace = ns,
            IsByRefLike = isByRefLike,
            Members = members ?? [],
            Attributes = attributes ?? [],
        };

    static ApiMember Method(
        string name,
        string signature,
        bool isObsolete = false,
        string? accessibility = null,
        IReadOnlyList<string>? attributes = null)
        => new()
        {
            Name = name,
            Kind = "method",
            Signature = signature,
            IsObsolete = isObsolete,
            Accessibility = accessibility,
            Attributes = attributes?.ToList() ?? [],
        };

    static ApiMember Property(string name, string signature)
        => new() { Name = name, Kind = "property", Signature = signature };

    static ApiMember StructuredMethod(
        string name,
        string signature,
        string returnType,
        List<ApiParameter> parameters,
        bool isStatic = false,
        bool isExtension = false,
        string? extendedType = null)
        => new()
        {
            Name = name,
            Kind = "method",
            Signature = signature,
            SignatureModel = new ApiSignature
            {
                MemberName = name,
                ReturnType = returnType,
                Parameters = parameters,
            },
            IsStatic = isStatic,
            IsExtension = isExtension,
            ExtendedType = extendedType,
        };

    static ApiParameter Parameter(string type, string? modifier = null)
        => new() { Type = type, Modifier = modifier };

    sealed record ExpectedMembers(string[] Added, string[] Removed);
}
