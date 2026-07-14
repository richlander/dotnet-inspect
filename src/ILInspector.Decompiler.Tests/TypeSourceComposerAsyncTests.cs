using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public sealed class TypeSourceComposerAsyncTests
{
    [Fact]
    public void ClassicAsyncWithoutAwait_UsesMetadataBodyModifier()
    {
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.ClassicAsync",
            "release",
            "ILInspector.Decompiler.Fixtures.ClassicAsync.dll"));
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures");

        var source = TypeSourceComposer.Compose(type, path, pdbPath: null);

        Assert.NotNull(source);
        Assert.Contains("public static async Task NoAwait()", source, StringComparison.Ordinal);
    }
}
