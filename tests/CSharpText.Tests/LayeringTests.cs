namespace CSharpText.Tests;

public sealed class LayeringTests
{
    private static readonly string[] PreMoveCorpusAssemblies =
    [
        "DiffAsmTarget",
        "DotnetInspector.CSharpBodySlicer",
        "DotnetInspector.CSharpBodySlicer.Tests",
        "ILInspector.Decompiler.Fixtures.NewUnsafe",
        "ILInspector.Metadata",
        "ILInspector.SourceLink",
        "SourceLinkFetch",
    ];

    [Fact]
    public void CSharpText_ReferencesOnlyFrameworkAssemblies()
    {
        var references = typeof(DeclarationIndex).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(references);
    }

    [Fact]
    public void RealSourceCorpus_PreservesEveryPreMoveAssemblyAndPdb()
    {
        foreach (string assemblyName in PreMoveCorpusAssemblies)
        {
            Assert.True(
                File.Exists(Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll")),
                $"Missing corpus assembly {assemblyName}.dll");
            Assert.True(
                File.Exists(Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.pdb")),
                $"Missing corpus symbols {assemblyName}.pdb");
        }
    }
}
