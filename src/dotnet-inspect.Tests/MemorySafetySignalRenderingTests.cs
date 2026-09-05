using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Gates the Signals projection of the module memory-safety rules state. The
/// end-to-end cases prove the scanner-to-renderer wiring over real images; the
/// projection cases cover states whose derivation is already gated in
/// <c>ILInspector.Metadata.Tests</c> and only need a rendering contract here.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class MemorySafetySignalRenderingTests
{
    const string MarkerEvidence = "module MemorySafetyRulesAttribute";

    [Fact]
    public async Task Signals_RecognizedVersion_ReportsUpdated()
    {
        string output = await RunSignalsAsync(BuildMarkerImage(2));

        Assert.Contains(
            "| Memory safety | Memory safety model | Updated (v2) |",
            output);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(0)]
    public async Task Signals_UnrecognizedVersion_ReportsUnsupportedWithActualValue(
        int version)
    {
        string output = await RunSignalsAsync(BuildMarkerImage(version));

        Assert.Contains(
            $"| Memory safety | Memory safety model | Unsupported (v{version}) |",
            output);
        Assert.DoesNotContain("Updated", output);
    }

    [Fact]
    public async Task Signals_DisagreeingMarkers_ReportConflict()
    {
        string output = await RunSignalsAsync(BuildMarkerImage(2, 3));

        Assert.Contains(
            "| Memory safety | Memory safety model | Conflicting markers |",
            output);
        Assert.DoesNotContain("Updated", output);
    }

    [Fact]
    public async Task Signals_NoMarker_ReportsNotMarked()
    {
        string output = await RunSignalsAsync(BuildMarkerImage(version: null));

        Assert.Contains(
            "| Memory safety | Memory safety model | Not marked |",
            output);
    }

    [Fact]
    public void Projection_Legacy_RendersNotMarked()
    {
        var (value, evidence) = AuditSignalBuilder.FormatMemorySafetyModel(
            Available(MemorySafetyRulesState.Legacy));

        Assert.Equal("Not marked", value);
        Assert.Equal(MarkerEvidence, evidence);
    }

    [Fact]
    public void Projection_MissingMetadata_RendersNotMarked()
    {
        var (value, _) = AuditSignalBuilder.FormatMemorySafetyModel(null);

        Assert.Equal("Not marked", value);
    }

    [Fact]
    public void Projection_Malformed_RendersMalformedAndKeepsDetail()
    {
        var (value, evidence) = AuditSignalBuilder.FormatMemorySafetyModel(
            Available(
                MemorySafetyRulesState.Malformed,
                new MemorySafetyRulesObservation(
                    AttributeToken: 0x0C000001,
                    MemorySafetyRulesObservationState.Malformed,
                    Version: null,
                    Detail: "constructor argument could not be decoded")));

        Assert.Equal("Malformed marker", value);
        Assert.Equal(
            $"{MarkerEvidence}; constructor argument could not be decoded",
            evidence);
    }

    [Fact]
    public void Projection_Unavailable_RendersUnavailableAndKeepsFailure()
    {
        var (value, evidence) = AuditSignalBuilder.FormatMemorySafetyModel(
            new MemorySafetyRulesResult.Unavailable(
                new MemorySafetyMetadataFailure(
                    MemorySafetyMetadataFailureKind.BudgetExceeded,
                    "Memory-safety module metadata exceeded its scan budget."),
                ImmutableArray<MemorySafetyRulesObservation>.Empty));

        Assert.Equal("Unavailable", value);
        Assert.Equal(
            $"{MarkerEvidence}; Memory-safety module metadata exceeded its scan budget.",
            evidence);
    }

    [Fact]
    public void Projection_Conflicting_ReportsMarkerCount()
    {
        var (value, evidence) = AuditSignalBuilder.FormatMemorySafetyModel(
            Available(
                MemorySafetyRulesState.Conflicting,
                Decoded(0x0C000001, 2),
                Decoded(0x0C000002, 3)));

        Assert.Equal("Conflicting markers", value);
        Assert.Equal($"{MarkerEvidence}; 2 markers", evidence);
    }

    static MemorySafetyRulesResult.Available Available(
        MemorySafetyRulesState state,
        params MemorySafetyRulesObservation[] observations)
        => new(state, [.. observations]);

    static MemorySafetyRulesObservation Decoded(int token, int version)
        => new(token, MemorySafetyRulesObservationState.Decoded, version, null);

    static async Task<string> RunSignalsAsync(byte[] image)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"memory-safety-signals-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        try
        {
            var options = new LibraryOptions
            {
                AssemblyName = path,
                IncludeSections = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase) { "Signals" }
            };

            var (exit, output, _) = await ConsoleCapture.RunAsync(
                () => LibraryCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            return output;
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Builds a well-formed image whose module optionally carries one or two
    /// <c>MemorySafetyRulesAttribute</c> markers with the supplied versions.
    /// </summary>
    static byte[] BuildMarkerImage(int? version, int? secondVersion = null)
    {
        var metadata = new MetadataBuilder();
        ModuleDefinitionHandle module = metadata.AddModule(
            0,
            metadata.GetOrAddString("MarkerProbe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MarkerProbe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Int32());

        MethodDefinitionHandle constructor = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            constructor);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("MemorySafetyRulesAttribute"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            constructor);

        foreach (int marker in new[] { version, secondVersion }
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value))
        {
            var value = new BlobBuilder();
            value.WriteUInt16(1);
            value.WriteInt32(marker);
            value.WriteUInt16(0);
            metadata.AddCustomAttribute(
                module,
                constructor,
                metadata.GetOrAddBlob(value));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
