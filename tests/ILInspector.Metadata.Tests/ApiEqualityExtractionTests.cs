using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
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

    [Theory]
    [InlineData("SetterToProtected", "Value", "Value")]
    [InlineData("SetterToProtected", "set_Value", "Value")]
    [InlineData("ConstantValue", "Value", "Value")]
    [InlineData("DefaultParameter", "Value", "Value")]
    [InlineData("DefaultParameter", "Missing", null)]
    public void MemberSelection_ProjectsTheMembersANameOrAccessorNameSelects(
        string typeName, string selected, string? projected)
    {
        using var image = new PEReader(File.OpenRead(
            FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath()));
        MetadataReader reader = image.GetMetadataReader();
        var selection = new ApiMemberNameSelection();
        selection.Add(Encoding.UTF8.GetBytes(selected));

        ApiSurface surface = ApiSurfaceExtractor.ExtractApiEquality(
            image,
            ApiSurfaceExtractionScope.ApiAxis,
            type => reader.StringComparer.Equals(reader.GetTypeDefinition(type).Name, typeName),
            selection);

        ApiType type = Assert.Single(surface.Types);
        Assert.Equal(
            projected is null ? [] : [projected],
            type.Members.Select(member => member.Name));
    }

    [Fact]
    public void MemberSelection_AddsEachNameOnce()
    {
        var selection = new ApiMemberNameSelection();
        selection.Add("Value"u8);
        selection.Add("Value"u8);
        selection.Add("get_Value"u8);

        Assert.Equal(2, selection.Count);
        Assert.True(selection.Contains("get_Value"u8));
        Assert.False(selection.Contains("Valu"u8));
    }
}
