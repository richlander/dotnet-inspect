using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using Xunit;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserViewFacetCatalogTests
{
    [Fact]
    public void ListLibraryViewFacets_ProjectsExactRegistryDescriptorsInOwnerOrder() =>
        AssertRegistryProjection(
            StructuralSubjectKind.Library,
            Interop.Catalog.CatalogExports.ListLibraryViewFacets());

    [Fact]
    public void ListMemberViewFacets_ProjectsExactRegistryDescriptorsInOwnerOrder() =>
        AssertRegistryProjection(
            StructuralSubjectKind.Member,
            Interop.Catalog.CatalogExports.ListMemberViewFacets());

    [Fact]
    public void ListPackageViewFacets_ProjectsExactRegistryDescriptorsInOwnerOrder() =>
        AssertRegistryProjection(
            StructuralSubjectKind.Package,
            Interop.Catalog.CatalogExports.ListPackageViewFacets());

    [Fact]
    public void ListTypeViewFacets_ProjectsExactRegistryDescriptorsInOwnerOrder() =>
        AssertRegistryProjection(
            StructuralSubjectKind.Type,
            Interop.Catalog.CatalogExports.ListTypeViewFacets());

    private static void AssertRegistryProjection(
        StructuralSubjectKind kind,
        string json)
    {
        ViewFacetDescriptor[] expected =
        [
            .. InspectionViewFacetCatalog.Registry
                .Discover(kind),
        ];

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement[] actual =
        [
            .. document.RootElement.GetProperty("facets").EnumerateArray(),
        ];

        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            ViewFacetDescriptor descriptor = expected[index];
            JsonElement projected = actual[index];
            Assert.Equal(descriptor.Id.Value,
                projected.GetProperty("id").GetString());
            Assert.Equal(descriptor.Kind.ToString(),
                projected.GetProperty("kind").GetString());
            Assert.Equal(descriptor.Title,
                projected.GetProperty("title").GetString());
            Assert.Equal(descriptor.Summary,
                projected.GetProperty("summary").GetString());
            Assert.Equal(descriptor.Order,
                projected.GetProperty("order").GetInt32());
            Assert.Equal(descriptor.Role?.ToString(),
                projected.GetProperty("role").GetString());
        }
    }
}
