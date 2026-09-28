using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.NamingFixtures;
using ILInspector.JsExportSurface.OperatorFixtures;
using ILInspector.JsExportSurface.PublishabilityFixtures;
using ILInspector.JsExportSurface.ScalarFixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed partial class JsExportSurfaceBuilderTests
{
    /// <summary>
    /// Directions come from how exports use a type, so a DTO reached only
    /// through a resolved return wire type is serialize-only, one reached only
    /// through a resolved parameter wire type is deserialize-only, and one
    /// reached both ways is bidirectional.
    /// </summary>
    [Theory]
    [InlineData(
        nameof(DirectionalOutputDto),
        JsonWireDirection.Serialize)]
    [InlineData(
        nameof(DirectionalInputDto),
        JsonWireDirection.Deserialize)]
    [InlineData(
        nameof(DirectionalSharedInputDto),
        JsonWireDirection.Deserialize)]
    [InlineData(
        nameof(DirectionalAccessorInputDto),
        JsonWireDirection.Deserialize)]
    [InlineData(
        nameof(DirectionalServerNoteDto),
        JsonWireDirection.Both)]
    [InlineData(
        nameof(DirectionalNote),
        JsonWireDirection.Serialize)]
    [InlineData(
        nameof(DirectionalConditionalNote),
        JsonWireDirection.Serialize)]
    public void Build_RecordsSerializeOnlyDirectionForReturnOnlyDto(
        string typeName,
        JsonWireDirection expected)
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);

        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(apiSurface, bodyIndex);

        ApiType record = Assert.Single(
            surface.Records,
            candidate => candidate.Name == typeName);
        Assert.Equal(expected, surface.WireDirections[record]);
    }

    [Theory]
    [InlineData(
        nameof(DirectionalOutputDto.DefaultHidden),
        JsonWireIgnoreCondition.WhenWritingDefault)]
    [InlineData(
        nameof(DirectionalOutputDto.NullHidden),
        JsonWireIgnoreCondition.WhenWritingNull)]
    public void Build_PreservesConditionalPresenceFromCompiledMetadata(
        string memberName,
        JsonWireIgnoreCondition condition)
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurface();
        ApiType record = Assert.Single(
            surface.Records,
            candidate => candidate.Name == nameof(DirectionalOutputDto));
        ApiMember member = Assert.Single(
            record.Members,
            candidate => candidate.Name == memberName);

        Assert.Equal([condition], member.JsonIgnoreConditions);
        Assert.Equal(
            JsonWireMemberPresence.Conditional,
            JsonWireMemberRules.GetPresence(
                member,
                JsonWireDirection.Serialize));
        Assert.Equal(
            JsonWireMemberPresence.Present,
            JsonWireMemberRules.GetPresence(
                member,
                JsonWireDirection.Deserialize));
    }

    /// <summary>
    /// Without body evidence there are no resolved wire types, so no direction
    /// is recorded and consumers fall back to the conservative bidirectional
    /// reading.
    /// </summary>
    [Fact]
    public void Build_RecordsNoDirectionsWithoutBodyEvidence()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurface();

        Assert.Empty(surface.WireDirections);
    }

    [Fact]
    public void Build_RecordsInactiveDiscoveredTypeAsNone()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ILInspector.JsExportSurface.JsExportSurface surface =
            JsExportSurfaceBuilder.Build(
            ApiSurfaceExtractor.Extract(
                peReader,
                includeAll: false),
            bodyIndex);

        ApiType inactive = Assert.Single(
            surface.Records,
            type => type.Name
                == nameof(DirectionalInactiveInputDto));
        Assert.Equal(
            JsonWireDirection.None,
            surface.WireDirections[inactive]);
    }

    [Fact]
    public void Build_ResolvesParameterWireTypeReferences()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: false);
        var bodyIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);

        JsExportFunction function = Assert.Single(
            JsExportSurfaceBuilder.Build(apiSurface, bodyIndex).Functions,
            candidate => candidate.Name == "SetDirectionalInput");

        Assert.Contains(
            function.ParameterWireTypeReferences,
            reference => reference.DefinitionName?.Segments
                is [nameof(DirectionalInputDto)]);
    }
}
