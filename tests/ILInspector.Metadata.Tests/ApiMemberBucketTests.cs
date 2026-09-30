using DotnetInspector.Fixtures;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

// A member that implements an interface member through a MethodImpl is
// reachable by exactly the consumers who can see that interface, so it
// belongs to the wider of its own and the interface's accessibility. A
// finalizer keeps its own accessibility, protected.
// (docs/design/api-population-scope.md#spelling-within-api-visibility-scope)
public sealed class ApiMemberBucketTests
{
    static readonly ApiSurface PublicSurface;
    static readonly ApiSurface IncludeAllSurface;
    static readonly ApiSurface SummarySurface;

    static ApiMemberBucketTests()
    {
        string path = typeof(ApiMemberBucketTests).Assembly.Location;
        PublicSurface = Extract(path, includeAll: false);
        IncludeAllSurface = Extract(path, includeAll: true);
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        SummarySurface = ApiSurfaceExtractor.ExtractSummary(peReader);
    }

    [Fact]
    public void InternalInterfaceImplementation_IsInternalNotPublic()
    {
        Assert.DoesNotContain(
            Type(PublicSurface).Members,
            member => member.Name.EndsWith(".Hidden", StringComparison.Ordinal));
        Assert.DoesNotContain(
            Type(SummarySurface).Members,
            member => member.Name.EndsWith(".Hidden", StringComparison.Ordinal));

        ApiMember hidden = Assert.Single(
            Type(IncludeAllSurface).Members,
            member => member.Name.EndsWith(".Hidden", StringComparison.Ordinal));
        Assert.Equal("internal", hidden.Accessibility);
    }

    [Fact]
    public void PublicInterfaceImplementation_StaysPublic()
    {
        ApiMember visible = Assert.Single(
            Type(PublicSurface).Members,
            member => member.Name.EndsWith(".Visible", StringComparison.Ordinal));
        Assert.Null(visible.Accessibility);
        Assert.Contains(
            Type(SummarySurface).Members,
            member => member.Name.EndsWith(".Visible", StringComparison.Ordinal));
    }

    [Fact]
    public void Finalizer_IsProtectedNotPublic()
    {
        Assert.DoesNotContain(
            Type(PublicSurface).Members,
            member => member.Name == "Finalize");
        Assert.DoesNotContain(
            Type(SummarySurface).Members,
            member => member.Name == "Finalize");

        ApiMember finalizer = Assert.Single(
            Type(IncludeAllSurface).Members,
            member => member.Name == "Finalize");
        Assert.Equal("protected", finalizer.Accessibility);
    }

    // VB spells an interface implementation as a private, ordinarily named
    // body. Collection.IListAdd implements the referenced IList.Add, so it is
    // public in the list and the summary, as its overload population is.
    [Fact]
    public void RealVisualBasicInterfaceImplementation_IsPublic()
    {
        string path = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "Microsoft.VisualBasic.Core.dll");
        ApiSurface publicSurface = Extract(path, includeAll: false);
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface summarySurface = ApiSurfaceExtractor.ExtractSummary(peReader);

        foreach (ApiSurface surface in new[] { publicSurface, summarySurface })
        {
            ApiType collection = surface.Types.Single(
                type => type.Namespace == "Microsoft.VisualBasic"
                    && type.Name == "Collection");
            ApiMember add = Assert.Single(
                collection.Members,
                member => member.Name == "IListAdd");
            Assert.Null(add.Accessibility);
        }
    }

    // A non-private body keeps its own accessibility even when it implements
    // a wider interface member, wherever that interface is declared. VB is
    // the compiler that emits these shapes.
    [Theory]
    [InlineData("FriendImplementsLocalPublic", "internal")]
    [InlineData("ProtectedImplementsLocalPublic", "protected")]
    [InlineData("FriendImplementsReferenced", "internal")]
    [InlineData("PrivateImplementsLocalInternal", "internal")]
    [InlineData("PrivateImplementsLocalInternalProperty", "internal")]
    [InlineData("PrivateImplementsLocalPublic", null)]
    public void VisualBasicImplementation_BucketFollowsOwnAccessUnlessPrivate(
        string name,
        string? accessibility)
    {
        string path = FixtureCatalog.MetadataVbInterfaceImplementations.AssemblyPath();
        ApiSurface publicSurface = Extract(path, includeAll: false);
        ApiSurface includeAllSurface = Extract(path, includeAll: true);
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface summarySurface = ApiSurfaceExtractor.ExtractSummary(peReader);

        ApiMember member = Assert.Single(
            VbType(includeAllSurface).Members,
            candidate => candidate.Name == name);
        Assert.Equal(accessibility, member.Accessibility);
        bool isPublic = accessibility is null;
        Assert.Equal(
            isPublic,
            VbType(publicSurface).Members.Any(candidate => candidate.Name == name));
        Assert.Equal(
            isPublic,
            VbType(summarySurface).Members.Any(candidate => candidate.Name == name));
    }

    // The design's C#-spelling oracle: JsonElement.ArrayEnumerator's explicit
    // IEnumerator.Current is one public property whose private get_Current
    // accessor composes into it; the two explicit GetEnumerator
    // implementations are public (docs/design/type-member-inspection-documents.md#composition-count).
    [Fact]
    public void RealArrayEnumerator_ComposesExplicitCurrentIntoOnePublicProperty()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "runtime",
            "System.Text.Json.dll");
        ApiType enumerator = Extract(path, includeAll: true).Types.Single(
            type => type.FullName == "System.Text.Json.JsonElement+ArrayEnumerator"
                || type.FullName == "System.Text.Json.JsonElement.ArrayEnumerator");

        ApiMember current = Assert.Single(
            enumerator.Members,
            member => member.Name == "System.Collections.IEnumerator.Current");
        Assert.Equal("property", current.Kind);
        Assert.Null(current.Accessibility);
        Assert.DoesNotContain(
            enumerator.Members,
            member => member.Name.EndsWith(".get_Current", StringComparison.Ordinal));

        Dictionary<string, int> buckets = enumerator.Members
            .GroupBy(member => member.Accessibility switch
            {
                null => "public",
                string access when access.Contains("protected", StringComparison.Ordinal) => "protected",
                string access when access.Contains("internal", StringComparison.Ordinal) => "internal",
                _ => "private",
            })
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(8, buckets.GetValueOrDefault("public"));
        Assert.Equal(0, buckets.GetValueOrDefault("protected"));
        Assert.Equal(1, buckets.GetValueOrDefault("internal"));
        Assert.Equal(3, buckets.GetValueOrDefault("private"));
    }

    // VB spells an explicit property as a private, ordinarily named property
    // whose private getter Implements the interface's getter. The property
    // composes its accessor and takes its public interface's bucket.
    [Fact]
    public void RealVisualBasicExplicitProperty_ComposesItsAccessorAndIsPublic()
    {
        string path = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "Microsoft.VisualBasic.Core.dll");
        ApiType collection = Extract(path, includeAll: false).Types.Single(
            type => type.Namespace == "Microsoft.VisualBasic"
                && type.Name == "Collection");

        ApiMember count = Assert.Single(
            collection.Members,
            member => member.Name == "ICollectionCount");
        Assert.Equal("property", count.Kind);
        Assert.Null(count.Accessibility);
        Assert.DoesNotContain(
            collection.Members,
            member => member.Name == "get_ICollectionCount");
    }

    // An attached extension belongs to the narrower of its own accessibility
    // and its declaring Type's: only consumers who can see the declaring Type
    // can call it (docs/design/api-population-scope.md#spelling-within-api-visibility-scope).
    [Fact]
    public void RealAttachedExtension_TakesTheNarrowerOfItsOwnAndItsDeclaringTypeAccess()
    {
        ApiSurface json = Extract(
            Path.Combine(AppContext.BaseDirectory, "PinnedArtifacts", "runtime", "System.Text.Json.dll"),
            includeAll: true);
        ApiMember readWithVerify = Attached(
            json, "System.Text.Json.Utf8JsonReader", "System.Text.Json.JsonHelpers", "ReadWithVerify");
        Assert.Equal("internal", readWithVerify.Accessibility);
        // The bucket classifies the declaration; its spelling keeps the
        // declared modifier.
        Assert.Equal("public", readWithVerify.DeclaredAccessibility);

        ApiSurface coreLib = Extract(
            Path.Combine(AppContext.BaseDirectory, "PinnedArtifacts", "System.Private.CoreLib.dll"),
            includeAll: true);
        Assert.All(
            AttachedAll(coreLib, "System.Type", "System.Reflection.SignatureTypeExtensions", "TryMakeArrayType"),
            member =>
            {
                Assert.Equal("private", member.Accessibility);
                Assert.Null(member.DeclaredAccessibility);
            });
        Assert.All(
            AttachedAll(coreLib, "System.String", "System.MemoryExtensions", "AsSpan"),
            member =>
            {
                Assert.Null(member.Accessibility);
                Assert.Null(member.DeclaredAccessibility);
            });
    }

    static ApiMember Attached(
        ApiSurface surface,
        string receiver,
        string declaringType,
        string name)
        => Assert.Single(AttachedAll(surface, receiver, declaringType, name));

    static IReadOnlyList<ApiMember> AttachedAll(
        ApiSurface surface,
        string receiver,
        string declaringType,
        string name)
    {
        ApiMember[] members =
        [
            .. surface.Types.Single(type => type.FullName == receiver).Members.Where(member =>
                member.Kind == "extension-method"
                && member.Name == name
                && member.DeclaringType == declaringType),
        ];
        Assert.NotEmpty(members);
        return members;
    }

    static ApiType VbType(ApiSurface surface)
        => surface.Types.Single(type => type.Name == "VbImplementations");

    static ApiType Type(ApiSurface surface)
        => surface.Types.Single(type => type.Name == nameof(BucketFixture));

    static ApiSurface Extract(string path, bool includeAll)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, includeAll);
    }
}

internal interface IBucketInternalContract
{
    void Hidden();
}

public interface IBucketPublicContract
{
    void Visible();
}

public sealed class BucketFixture : IBucketInternalContract, IBucketPublicContract
{
    void IBucketInternalContract.Hidden()
    {
    }

    void IBucketPublicContract.Visible()
    {
    }

    ~BucketFixture()
    {
    }
}
