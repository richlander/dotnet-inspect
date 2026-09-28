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
