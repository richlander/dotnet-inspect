using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class ImplementationComparisonQueryTests
{
    [Fact]
    public void Execute_UsesSuppliedAssemblyContentForCSharpAndIlEvidence()
    {
        string oldPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string newPath = FixtureCatalog.DiffPair.NewAssemblyPath();

        ImplementationDiffResult result =
            ImplementationComparisonQuery.Execute(
                new ImplementationComparisonInput(
                    [StreamBackedInput(oldPath, "old.dll")],
                    [StreamBackedInput(newPath, "new.dll")],
                    TypeFilters: new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "DiffSample",
                    }));

        ImplementationDiffMember member = Assert.Single(
            result.Members,
            member => member.Subject.Display.Contains(
                "ConstantValue",
                StringComparison.Ordinal));
        Assert.Contains(
            member.Changes,
            change => change.Mechanism
                == ResearchChangeMechanism.CSharp);
        Assert.Contains(
            member.Changes,
            change => change.Mechanism
                == ResearchChangeMechanism.IlBody);
    }

    [Fact]
    public void Definition_IsUnbounded()
        => Assert.Equal(
            InspectionCost.Unbounded,
            ImplementationComparisonQuery.Definition.Cost);

    static ImplementationAssemblyInput StreamBackedInput(
        string path,
        string displayName)
    {
        byte[] content = File.ReadAllBytes(path);
        ResolvedAssemblyReference pathReference =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "implementation query test identity"));
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}",
            displayName);
        ResolvedAssemblyReference contentReference =
            ResolvedAssemblyReference.Create(
                pathReference.Identity,
                missingPath,
                () => new MemoryStream(content, writable: false),
                AssemblyResolutionProvenance.Local(
                    "implementation query test content"));
        return new ImplementationAssemblyInput(
            contentReference,
            MetadataSource.DefaultAssemblyReferenceResolver(path),
            LibraryBodyIndex.Open(path));
    }
}
