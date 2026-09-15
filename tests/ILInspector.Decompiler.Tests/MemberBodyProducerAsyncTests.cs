using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public sealed class MemberBodyProducerAsyncTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicAsyncWithoutAwait_UsesResolvedMethodBodyModifier(
        bool invalidateMetadataToken)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.ClassicAsync",
            configuration,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.dll"));
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures");
        if (invalidateMetadataToken)
        {
            var member = Assert.Single(
                type.Members,
                candidate => candidate.Name == "NoAwait");
            member.MetadataToken = 0x02000001;
        }

        var source = MemberBodyProducer.Project(type, path, pdbPath: null).Output;

        Assert.NotNull(source);
        Assert.Contains("public static async Task NoAwait()", source, StringComparison.Ordinal);
    }
}
