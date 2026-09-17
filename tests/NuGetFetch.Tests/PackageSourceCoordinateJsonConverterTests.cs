using System.Text.Json;
using System.Text.Json.Serialization;

namespace NuGetFetch.Tests;

public sealed class PackageSourceCoordinateJsonConverterTests
{
    [Theory]
    [InlineData(null, "PackageId", "Version")]
    [InlineData(JsonKnownNamingPolicy.CamelCase, "packageId", "version")]
    [InlineData(JsonKnownNamingPolicy.SnakeCaseLower, "package_id", "version")]
    public void RoundTripUsesNamingPolicyAndValidatedNormalization(
        JsonKnownNamingPolicy? namingPolicy,
        string packageIdProperty,
        string versionProperty)
    {
        JsonSerializerOptions options = Options(namingPolicy);

        string json = JsonSerializer.Serialize(
            PackageSourceCoordinate.Create("Example.Package", "1.0"),
            options);
        PackageSourceCoordinate? roundTripped =
            JsonSerializer.Deserialize<PackageSourceCoordinate>(json, options);

        Assert.Contains($"\"{packageIdProperty}\"", json);
        Assert.Contains($"\"{versionProperty}\"", json);
        Assert.NotNull(roundTripped);
        Assert.Equal("example.package", roundTripped.PackageId);
        Assert.Equal("1.0.0", roundTripped.Version);
    }

    [Theory]
    [InlineData("""{"package_id":"a","package_id":"b","version":"1.0.0"}""")]
    [InlineData("""{"package_id":"a"}""")]
    [InlineData("""{"version":"1.0.0"}""")]
    [InlineData("""{"package_id":"","version":"1.0.0"}""")]
    [InlineData("""{"package_id":"a","version":"not-a-version"}""")]
    public void InvalidCoordinateIsRejected(string json)
    {
        JsonSerializerOptions options =
            Options(JsonKnownNamingPolicy.SnakeCaseLower);

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PackageSourceCoordinate>(
                json,
                options));
    }

    private static JsonSerializerOptions Options(
        JsonKnownNamingPolicy? namingPolicy) =>
        new()
        {
            PropertyNamingPolicy = namingPolicy switch
            {
                JsonKnownNamingPolicy.CamelCase =>
                    JsonNamingPolicy.CamelCase,
                JsonKnownNamingPolicy.SnakeCaseLower =>
                    JsonNamingPolicy.SnakeCaseLower,
                null => null,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(namingPolicy)),
            },
        };
}
