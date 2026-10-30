using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// A type-filtered declaration extraction decodes only the admitted Types,
/// and each admitted Type yields the same declarations as a complete walk.
/// </summary>
public sealed class ApiSurfaceTypeFilteredExtractionTests
{
    [Fact]
    public void ExtractDeclarations_TypeFilter_MatchesCompleteWalkForAdmittedTypes()
    {
        using var stream = File.OpenRead(
            typeof(ApiSurfaceExtractor).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface complete = ApiSurfaceExtractor.ExtractDeclarations(
            peReader,
            ApiSurfaceExtractionScope.IncludeAll,
            includeCompilerGenerated: true);
        ApiType[] chosen =
        [
            .. complete.Types
                .Where(type => type.Members.Count > 0)
                .Where((_, index) => index % 50 == 0)
                .Take(8),
        ];
        Assert.True(chosen.Length > 1);
        var admitted = chosen
            .Select(type => MetadataTokens.TypeDefinitionHandle(
                type.MetadataToken!.Value & 0x00FFFFFF))
            .ToHashSet();

        ApiSurface filtered = ApiSurfaceExtractor.ExtractDeclarations(
            peReader,
            ApiSurfaceExtractionScope.IncludeAll,
            admitted.Contains,
            includeCompilerGenerated: true);

        Assert.Equal(
            chosen.Select(Snapshot),
            filtered.Types.Select(Snapshot));
    }

    static string Snapshot(ApiType type) =>
        $"{type.Namespace}.{type.Name}:"
        + string.Join(
            ";",
            type.Members.Select(member =>
                $"{member.Kind} {member.Name} {member.Signature} "
                + $"{member.MetadataToken} {member.GetterToken} "
                + $"{member.SetterToken} {member.MethodSemantics}"));
}
