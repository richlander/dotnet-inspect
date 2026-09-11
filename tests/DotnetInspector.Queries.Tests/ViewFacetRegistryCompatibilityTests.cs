using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Queries.Tests;

public sealed partial class ViewFacetRegistryCompatibilityTests
{
    [Fact]
    public void ShippedFacets_RetainIdentityKindAndPurpose()
    {
        using Stream stream = typeof(ViewFacetRegistryCompatibilityTests)
            .Assembly.GetManifestResourceStream(
                "DotnetInspector.Queries.Tests.ViewFacetCompatibility.json")
            ?? throw new InvalidOperationException(
                "The View Facet Registry compatibility manifest is missing.");
        CompatibilityEntry[] manifest =
            JsonSerializer.Deserialize(
                stream,
                ViewFacetCompatibilityJsonContext.Default
                    .CompatibilityEntryArray)
            ?? throw new InvalidOperationException(
                "The View Facet Registry compatibility manifest is invalid.");

        ViewFacetRegistration[] registrations =
        [
            .. InspectionViewFacetCatalog.Registry.Registrations,
        ];
        Assert.Equal(
            registrations.Select(registration =>
                    registration.Descriptor.Id.Value)
                .Order(),
            manifest.Select(entry => entry.Id).Order());
        foreach (CompatibilityEntry entry in manifest)
        {
            ViewFacetRegistration registration = Assert.Single(
                registrations,
                candidate =>
                    candidate.Descriptor.Id.Value == entry.Id);
            Assert.Equal(
                entry.Kind,
                registration.Descriptor.Kind.ToString());
            Assert.Equal(entry.Purpose, registration.Purpose);
        }
    }

    sealed record CompatibilityEntry(
        [property: JsonPropertyName("id")]
        string Id,
        [property: JsonPropertyName("kind")]
        string Kind,
        [property: JsonPropertyName("purpose")]
        string Purpose);

    [JsonSerializable(typeof(CompatibilityEntry[]))]
    sealed partial class ViewFacetCompatibilityJsonContext :
        JsonSerializerContext;
}
