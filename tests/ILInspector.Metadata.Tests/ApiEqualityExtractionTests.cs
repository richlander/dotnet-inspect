using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

public sealed class ApiEqualityExtractionTests
{
    [Fact]
    public void ApiEqualityExtraction_ComparesEqualToTheCompleteExtraction()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        foreach (string path in new[] { pair.OldAssemblyPath(), pair.NewAssemblyPath() })
        {
            using var image = new PEReader(File.OpenRead(path));
            ApiSurface complete = ApiSurfaceExtractor.ExtractDeclarations(
                image, ApiSurfaceExtractionScope.ApiAxis, _ => true);
            ApiSurface equality = ApiSurfaceExtractor.ExtractApiEquality(
                image, ApiSurfaceExtractionScope.ApiAxis, _ => true, includeMembers: null);

            ApiFindingComparison comparison = MetadataFindings.CompareApi(
                complete, equality, new FindingSubject("probe", "API equality"),
                new ApiDiffOptions(ApiDiffScope.All));

            Assert.True(comparison.ApiDiff.IsEmpty);
            Assert.Equal(
                complete.Types.Select(type => (type.FullName, type.Members.Count)),
                equality.Types.Select(type => (type.FullName, type.Members.Count)));
            Assert.Contains(complete.Types, type => type.MemorySafety is not null);
            Assert.All(equality.Types, type =>
            {
                Assert.Null(type.MemorySafety);
                Assert.All(type.Members, member =>
                {
                    Assert.Null(member.MemorySafety);
                    Assert.Null(member.AccessorMemorySafety);
                    Assert.Null(member.MethodSemantics);
                });
            });
        }
    }
}
