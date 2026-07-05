using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;

namespace ILInspector.Decompiler.Tests;

public class ReturnToSenderFixtureCatalogTests
{
    [Fact]
    public void ReturnToSenderCandidates_ProvideBuiltAssemblyInputs()
    {
        var paths = FixtureCatalog.ReturnToSenderCandidates.AssemblyPaths();

        Assert.NotEmpty(paths);
        Assert.All(paths, path =>
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            Assert.True(peReader.HasMetadata);
            Assert.NotEmpty(peReader.GetMetadataReader().MethodDefinitions);
        });
    }
}
