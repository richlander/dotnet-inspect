using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

// The compact public Member Count and public API-surface extraction share one
// set of member-admission rules. On a real asset, both paths must admit the
// same members on every public Type, so a Count never disagrees with its Rows.
public sealed class ApiMemberAdmissionParityTests
{
    static readonly string SystemTextJsonPath = Path.Combine(
        AppContext.BaseDirectory,
        "PinnedArtifacts",
        "runtime",
        "System.Text.Json.dll");

    [Fact]
    public void SummaryAndPublicExtract_AdmitTheSameMembersOnEveryPublicType()
    {
        ApiSurface extracted;
        ApiSurface summary;
        using (var stream = File.OpenRead(SystemTextJsonPath))
        using (var peReader = new PEReader(stream))
        {
            extracted = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
        }
        using (var stream = File.OpenRead(SystemTextJsonPath))
        using (var peReader = new PEReader(stream))
        {
            summary = ApiSurfaceExtractor.ExtractSummary(peReader);
        }

        Dictionary<string, string[]> extractedMembers = MembersByType(extracted);
        Dictionary<string, string[]> summaryMembers = MembersByType(summary);

        Assert.Equal(
            extractedMembers.Keys.Order(StringComparer.Ordinal),
            summaryMembers.Keys.Order(StringComparer.Ordinal));
        Assert.All(
            extractedMembers,
            pair => Assert.Equal(pair.Value, summaryMembers[pair.Key]));
        Assert.True(
            extractedMembers.Values.Sum(names => names.Length) > 500,
            $"The real asset should exercise a large public Member population; saw {extractedMembers.Values.Sum(names => names.Length)} across {extractedMembers.Count} Types.");
    }

    static Dictionary<string, string[]> MembersByType(ApiSurface surface)
        => surface.Types.ToDictionary(
            type => type.FullName,
            type => type.Members
                .Select(member => member.Name)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
}
