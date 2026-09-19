using System.Collections.Immutable;
using System.Runtime.InteropServices;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

/// <summary>
/// Gates the content-shaped projection path a host without a filesystem depends on: a
/// <see cref="MetadataSource"/> opened from a stream-backed <see cref="ResolvedAssemblyReference"/>
/// with no path, and a caller-supplied <see cref="ResearchAssemblyContext"/>.
/// </summary>
public class ContentShapedMemberProjectionTests
{
    [Fact]
    public void ResearchViewsFacade_ForwardsToMemberProjectionProducer()
    {
        using MetadataSource source = OpenFromContent();
        var request = new MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            AnnotatedSource: true,
            Registry: new ResearchFactRegistry());

        MemberProjectionResult direct =
            MemberProjectionProducer.Produce(request);
        MemberProjectionResult forwarded =
            ResearchViews.ProjectMember(request);

        Assert.Equal(
            direct.AnnotatedSource?.Output,
            forwarded.AnnotatedSource?.Output);
        Assert.Equal(
            direct.AnnotatedSource?.Diagnostics,
            forwarded.AnnotatedSource?.Diagnostics);
        Assert.Equal(
            direct.SelectedMethodToken,
            forwarded.SelectedMethodToken);
    }

    [Fact]
    public void PathlessSourceProjectsWithoutFabricatingAFilePath()
    {
        using MetadataSource source = OpenFromContent();

        // Before FilePath existed, MetadataSource.Path fell back to the assembly's identity name
        // and ResolveAssemblyContext read it as a file, so this projection failed with
        // FileNotFoundException instead of observing a consistent absence of assembly context.
        var projection = MemberProjectionProducer.Produce(new MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            SourceDocument: true));

        Assert.Null(projection.SourceDocumentFailure);
        var document = Assert.IsType<AnnotatedSourceDocument>(projection.SourceDocument);
        Assert.NotEmpty(document.Text);
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.CSharp);
    }

    [Fact]
    public void SuppliedAssemblyContextRestoresTheFactsPathResolutionCannotReach()
    {
        using MetadataSource pathless = OpenFromContent();
        var withoutContext = MemberProjectionProducer.Produce(new MemberProjectionRequest(
            pathless,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            SourceDocument: true));
        Assert.Empty(Assert.IsType<AnnotatedSourceDocument>(withoutContext.SourceDocument).Facts);

        using MetadataSource supplied = OpenFromContent();
        var withContext = MemberProjectionProducer.Produce(new MemberProjectionRequest(
            supplied,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            SourceDocument: true,
            Assembly: ResearchAssemblyContext.Create(LibraryBodyIndex.OpenFromPrefetchedImage(
                "lib/net11.0/ILInspector.Research.Tests.dll",
                Image(),
                LibraryBodyAnalysisFeatures.Default))));

        var document = Assert.IsType<AnnotatedSourceDocument>(withContext.SourceDocument);
        Assert.Contains(document.Facts, fact => fact.Descriptor == "alloc.box");

        // The supplied context must produce the same facts the path-derived one does, or the
        // browser and the CLI would disagree about the same member.
        using MetadataSource fromPath = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var expected = MemberProjectionProducer.Produce(new MemberProjectionRequest(
            fromPath,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            SourceDocument: true));
        Assert.Equal(
            Assert.IsType<AnnotatedSourceDocument>(expected.SourceDocument).Facts,
            document.Facts);
    }

    static ImmutableArray<byte> Image() => ImmutableCollectionsMarshal.AsImmutableArray(
        File.ReadAllBytes(typeof(ResearchFixture).Assembly.Location));

    static MetadataSource OpenFromContent()
    {
        ImmutableArray<byte> image = Image();
        var reference = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity("ILInspector.Research.Tests", null, null, null),
            path: null,
            () => new MemoryStream(ImmutableCollectionsMarshal.AsArray(image)!, writable: false),
            AssemblyResolutionProvenance.Package("probe", "1.0.0", "net11.0", rid: null));
        return MetadataSource.OpenWithoutSymbols(reference, new UnresolvableReferences());
    }

    sealed class UnresolvableReferences : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope) => null;
    }
}
