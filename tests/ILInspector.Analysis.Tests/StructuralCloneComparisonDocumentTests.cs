using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneComparisonDocumentTests
{
    [Fact]
    public void Create_ExactComparison_ProjectsMatchingFields()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();
        StructuralCloneComparison comparison = Compare(
            image,
            reader,
            nameof(StructuralCloneFixture.ExactPositiveA),
            nameof(StructuralCloneFixture.ExactPositiveB));
        StructuralCloneModuleIdentity identity =
            StructuralCloneModuleIdentity.Create("fixture.dll", image, reader);

        StructuralCloneComparisonDocument document =
            StructuralCloneComparisonDocument.Create(comparison, identity, identity);

        Assert.Equal(StructuralCloneComparisonDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Equal(StructuralCloneComparisonDocument.CurrentMethodologyVersion, document.MethodologyVersion);
        Assert.Equal(StructuralCloneDisposition.Completed, document.Disposition);
        Assert.Equal(StructuralCloneRelation.Exact, document.Relation);
        Assert.NotNull(document.Correspondence);
        Assert.Equal(comparison.Left.Token, document.LeftToken);
        Assert.Equal(comparison.Right.Token, document.RightToken);
        Assert.Equal(identity.ModuleVersionId, document.Left.ModuleVersionId);
        Assert.Equal(identity.ModuleVersionId, document.Right.ModuleVersionId);
    }

    [Fact]
    public void Create_LeftIdentityModuleMismatch_Throws()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();
        StructuralCloneComparison comparison = Compare(
            image,
            reader,
            nameof(StructuralCloneFixture.ExactPositiveA),
            nameof(StructuralCloneFixture.ExactPositiveB));
        StructuralCloneModuleIdentity mismatched =
            new("other.dll", new string('a', 64), Guid.NewGuid());

        Assert.Throws<ArgumentException>(
            () => StructuralCloneComparisonDocument.Create(comparison, mismatched, mismatched));
    }

    [Fact]
    public void Constructor_DifferentModuleVersionIdsAcrossSides_Throws()
    {
        StructuralCloneModuleIdentity left =
            new("left.dll", new string('a', 64), Guid.NewGuid());
        StructuralCloneModuleIdentity right =
            new("right.dll", new string('b', 64), Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => new StructuralCloneComparisonDocument(
            StructuralCloneComparisonDocument.CurrentSchemaVersion,
            StructuralCloneComparisonDocument.CurrentMethodologyVersion,
            left,
            right,
            LeftToken: 0x06000001,
            RightToken: 0x06000001,
            StructuralCloneDisposition.Unsupported,
            Relation: null,
            Correspondence: null,
            Alignment: null,
            Blockers: [UnsupportedBlocker()],
            Receipt: EmptyReceipt(),
            AlignmentReceipt: null));
    }

    [Fact]
    public void Constructor_SameModuleVersionIdDifferentHash_Throws()
    {
        // A module version id alone is not a sufficient identity: MVIDs are not
        // guaranteed globally unique, so two byte-distinct modules could otherwise
        // slip past the A-vs-A boundary while carrying different content hashes.
        Guid sharedModuleVersionId = Guid.NewGuid();
        StructuralCloneModuleIdentity left =
            new("left.dll", new string('a', 64), sharedModuleVersionId);
        StructuralCloneModuleIdentity right =
            new("right.dll", new string('b', 64), sharedModuleVersionId);

        Assert.Throws<ArgumentException>(() => new StructuralCloneComparisonDocument(
            StructuralCloneComparisonDocument.CurrentSchemaVersion,
            StructuralCloneComparisonDocument.CurrentMethodologyVersion,
            left,
            right,
            LeftToken: 0x06000001,
            RightToken: 0x06000001,
            StructuralCloneDisposition.Unsupported,
            Relation: null,
            Correspondence: null,
            Alignment: null,
            Blockers: [UnsupportedBlocker()],
            Receipt: EmptyReceipt(),
            AlignmentReceipt: null));
    }

    [Fact]
    public void Constructor_NullReceipt_Throws()
    {
        StructuralCloneModuleIdentity identity =
            new("fixture.dll", new string('a', 64), Guid.NewGuid());

        Assert.Throws<ArgumentNullException>(() => new StructuralCloneComparisonDocument(
            StructuralCloneComparisonDocument.CurrentSchemaVersion,
            StructuralCloneComparisonDocument.CurrentMethodologyVersion,
            identity,
            identity,
            LeftToken: 0x06000001,
            RightToken: 0x06000002,
            StructuralCloneDisposition.Unsupported,
            Relation: null,
            Correspondence: null,
            Alignment: null,
            Blockers: [UnsupportedBlocker()],
            Receipt: null!,
            AlignmentReceipt: null));
    }

    [Theory]
    [InlineData(0x00000000)] // nil token
    [InlineData(0x02000001)] // TypeDef, not MethodDef
    [InlineData(unchecked((int)0x0A000001))] // MemberRef, not MethodDef
    public void Constructor_NonMethodDefToken_Throws(int token)
    {
        StructuralCloneModuleIdentity identity =
            new("fixture.dll", new string('a', 64), Guid.NewGuid());

        // MetadataTokens.MethodDefinitionHandle(int) masks off a token's table bits and
        // keeps only the row number, so a non-MethodDef token would otherwise silently
        // round-trip into a plausible-looking MethodDef handle while LeftToken/RightToken
        // retained the original, differently-tabled value.
        Assert.Throws<ArgumentException>(() => new StructuralCloneComparisonDocument(
            StructuralCloneComparisonDocument.CurrentSchemaVersion,
            StructuralCloneComparisonDocument.CurrentMethodologyVersion,
            identity,
            identity,
            LeftToken: token,
            RightToken: 0x06000002,
            StructuralCloneDisposition.Unsupported,
            Relation: null,
            Correspondence: null,
            Alignment: null,
            Blockers: [UnsupportedBlocker()],
            Receipt: EmptyReceipt(),
            AlignmentReceipt: null));
    }

    [Fact]
    public void Constructor_CompletedWithoutRelation_ThrowsViaReissue()
    {
        StructuralCloneModuleIdentity identity =
            new("fixture.dll", new string('a', 64), Guid.NewGuid());

        // The document reissues its fields through the same product factory a
        // live comparison uses; a completed disposition without a relation
        // must fail the identical invariant StructuralCloneComparison enforces.
        Assert.Throws<ArgumentException>(() => new StructuralCloneComparisonDocument(
            StructuralCloneComparisonDocument.CurrentSchemaVersion,
            StructuralCloneComparisonDocument.CurrentMethodologyVersion,
            identity,
            identity,
            LeftToken: 0x06000001,
            RightToken: 0x06000002,
            StructuralCloneDisposition.Completed,
            Relation: null,
            Correspondence: null,
            Alignment: null,
            Blockers: [],
            Receipt: EmptyReceipt(),
            AlignmentReceipt: null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Constructor_UnsupportedSchemaVersion_Throws(int schemaVersion)
    {
        StructuralCloneModuleIdentity identity =
            new("fixture.dll", new string('a', 64), Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() => new StructuralCloneComparisonDocument(
            schemaVersion,
            StructuralCloneComparisonDocument.CurrentMethodologyVersion,
            identity,
            identity,
            LeftToken: 0x06000001,
            RightToken: 0x06000002,
            StructuralCloneDisposition.Unsupported,
            Relation: null,
            Correspondence: null,
            Alignment: null,
            Blockers: [UnsupportedBlocker()],
            Receipt: EmptyReceipt(),
            AlignmentReceipt: null));
    }

    [Fact]
    public void ModuleIdentity_Create_ReadsFixtureHashAndModuleVersionId()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();

        StructuralCloneModuleIdentity identity =
            StructuralCloneModuleIdentity.Create("fixture.dll", image, reader);

        Assert.Equal(64, identity.Sha256.Length);
        Assert.Equal(
            reader.GetGuid(reader.GetModuleDefinition().Mvid),
            identity.ModuleVersionId);
    }

    [Theory]
    [InlineData("", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("fixture.dll", "not-hex")]
    [InlineData("fixture.dll", "ABCDEF0000000000000000000000000000000000000000000000000000000000")]
    public void ModuleIdentity_InvalidFields_Throws(string fileName, string sha256)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new StructuralCloneModuleIdentity(fileName, sha256, Guid.NewGuid()));
    }

    [Fact]
    public void ModuleIdentity_EmptyModuleVersionId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new StructuralCloneModuleIdentity("fixture.dll", new string('a', 64), Guid.Empty));
    }

    [Fact]
    public void Document_JsonRoundTrip_PreservesRelationAndCorrespondence()
    {
        using PEReader image = OpenFixture();
        MetadataReader reader = image.GetMetadataReader();
        StructuralCloneComparison comparison = Compare(
            image,
            reader,
            nameof(StructuralCloneFixture.ExactPositiveA),
            nameof(StructuralCloneFixture.ExactPositiveB));
        StructuralCloneModuleIdentity identity =
            StructuralCloneModuleIdentity.Create("fixture.dll", image, reader);
        StructuralCloneComparisonDocument document =
            StructuralCloneComparisonDocument.Create(comparison, identity, identity);

        string json = JsonSerializer.Serialize(
            document,
            StructuralCloneComparisonDocumentJsonContext.Default.StructuralCloneComparisonDocument);
        StructuralCloneComparisonDocument? replayed = JsonSerializer.Deserialize(
            json,
            StructuralCloneComparisonDocumentJsonContext.Default.StructuralCloneComparisonDocument);

        Assert.NotNull(replayed);
        Assert.Equal(document.Relation, replayed.Relation);
        Assert.Equal(document.Disposition, replayed.Disposition);
        Assert.Equal(document.LeftToken, replayed.LeftToken);
        Assert.Equal(document.RightToken, replayed.RightToken);
        Assert.Equal(document.Left.Sha256, replayed.Left.Sha256);
        Assert.NotNull(replayed.Correspondence);
        Assert.Equal(
            document.Correspondence!.Blocks.Length,
            replayed.Correspondence!.Blocks.Length);
    }

    static StructuralCloneComparison Compare(
        PEReader image,
        MetadataReader reader,
        string leftName,
        string rightName)
        => StructuralCloneAnalysis.Compare(
            image,
            Method(reader, leftName),
            Method(reader, rightName));

    static StructuralCloneBlocker UnsupportedBlocker()
        => new(
            StructuralCloneBlockerKind.NoMethodBody,
            StructuralCloneSide.Left,
            "test blocker");

    static StructuralCloneVerificationReceipt EmptyReceipt()
        => new(
            LeftBodyBytes: 0,
            RightBodyBytes: 0,
            LeftInstructions: 0,
            RightInstructions: 0,
            LeftBlocks: 0,
            RightBlocks: 0,
            LeftEdges: 0,
            RightEdges: 0,
            LeftLocals: 0,
            RightLocals: 0,
            RefinementRounds: 0,
            SearchSteps: 0,
            SearchExhausted: true,
            WitnessFound: false);

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
