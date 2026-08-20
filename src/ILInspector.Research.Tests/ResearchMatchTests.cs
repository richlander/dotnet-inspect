using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.Analysis.StructuralCloneFixtures;

namespace ILInspector.Research.Tests;

public class ResearchMatchTests
{
    [Fact]
    public void Compare_ExactCloneAtDifferentIdentity_ClassifiesRenamedOrMoved()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();
        StructuralCloneModuleIdentity identity = MakeIdentity(image, reader);

        ResearchMatchResult result = ResearchMatch.Compare(
            image,
            Method(reader, nameof(StructuralCloneFixture.ExactPositiveA)),
            Method(reader, nameof(StructuralCloneFixture.ExactPositiveB)),
            identity);

        Assert.Equal(StructuralCloneDisposition.Completed, result.Document.Disposition);
        Assert.Equal(ResearchMatchOutcome.RenamedOrMoved, result.Outcome);
    }

    [Fact]
    public void Compare_StructurallyDifferentMethods_ClassifiesUnrelated()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();
        StructuralCloneModuleIdentity identity = MakeIdentity(image, reader);

        ResearchMatchResult result = ResearchMatch.Compare(
            image,
            Method(reader, nameof(StructuralCloneFixture.EdgeRoleNegativeA)),
            Method(reader, nameof(StructuralCloneFixture.EdgeRoleNegativeB)),
            identity);

        Assert.Equal(StructuralCloneRelation.Different, result.Document.Relation);
        Assert.Equal(ResearchMatchOutcome.Unrelated, result.Outcome);
    }

    [Fact]
    public void Compare_VerifiedBoundedEdit_ClassifiesNear()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();
        StructuralCloneModuleIdentity identity = MakeIdentity(image, reader);

        ResearchMatchResult result = ResearchMatch.Compare(
            image,
            Method(reader, nameof(StructuralCloneFixture.NearConstantA)),
            Method(reader, nameof(StructuralCloneFixture.NearConstantB)),
            identity);

        Assert.Equal(StructuralCloneRelation.Near, result.Document.Relation);
        Assert.Equal(ResearchMatchOutcome.Near, result.Outcome);
    }

    [Fact]
    public void Compare_SameMethodAgainstItself_ClassifiesUnchanged()
    {
        // The degenerate A-vs-A case: comparing a method to itself is the only way this
        // slice's single-module boundary can produce a same-declared-identity Exact clone.
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();
        StructuralCloneModuleIdentity identity = MakeIdentity(image, reader);
        MethodDefinitionHandle method = Method(reader, nameof(StructuralCloneFixture.ExactPositiveA));

        ResearchMatchResult result = ResearchMatch.Compare(image, method, method, identity);

        Assert.Equal(StructuralCloneRelation.Exact, result.Document.Relation);
        Assert.Equal(ResearchMatchOutcome.Unchanged, result.Outcome);
    }

    [Fact]
    public void FromDocument_ProjectsWithoutRecomputing()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();
        StructuralCloneModuleIdentity identity = MakeIdentity(image, reader);
        StructuralCloneComparison comparison = StructuralCloneAnalysis.Compare(
            image,
            Method(reader, nameof(StructuralCloneFixture.NearConstantA)),
            Method(reader, nameof(StructuralCloneFixture.NearConstantB)));
        StructuralCloneComparisonDocument document =
            StructuralCloneComparisonDocument.Create(comparison, identity, identity);

        ResearchMatchResult result = ResearchMatch.FromDocument(document);

        Assert.Same(document, result.Document);
        Assert.Equal(ResearchMatchOutcome.Near, result.Outcome);
    }

    static StructuralCloneModuleIdentity MakeIdentity(PEReader image, MetadataReader reader)
        => StructuralCloneModuleIdentity.Create("fixture.dll", image, reader);

    static PEReader OpenFixture()
        => new(File.OpenRead(typeof(StructuralCloneFixture).Assembly.Location));

    static MethodDefinitionHandle Method(MetadataReader reader, string name)
    {
        MethodDefinitionHandle[] matches =
        [
            .. reader.MethodDefinitions.Where(handle =>
                reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    name)),
        ];
        return Assert.Single(matches);
    }
}
