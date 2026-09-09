using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NuGetFetch;
using Xunit.Sdk;
using static ILInspector.Metadata.Tests.CustomAttributeFidelityOracle;

namespace ILInspector.Metadata.Tests;

public sealed class CustomAttributeCorpusTests(ITestOutputHelper output)
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<PrimitiveTypeCode>() },
    };

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PinnedPackage_AllAttributeRowsEqualIndependentOracle()
    {
        CorpusInput input = LoadInput();
        Assert.NotEmpty(input.Assemblies);
        Assert.NotEmpty(input.Enums);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var http = new HttpClient();
        var client = new NuGetClient(http);
        await using var source = await client.DownloadAsync(
            input.Id, input.Version, cancellationToken: cancellationToken);
        using var package = new MemoryStream();
        await source.CopyToAsync(package, cancellationToken);
        VerifyHash(package.ToArray(), input.Sha256, $"{input.Id}@{input.Version}");
        package.Position = 0;
        using var zip = new ZipArchive(package, ZipArchiveMode.Read);
        var images = input.Assemblies.Concat(input.RetainedDependencies)
            .Select(entry => ReadImage(zip, entry)).ToArray();
        string[] frameworkPaths =
        [
            typeof(object).Assembly.Location,
            typeof(JsonSerializer).Assembly.Location,
            typeof(System.Text.RegularExpressions.Regex).Assembly.Location,
            Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll"),
        ];
        var framework = frameworkPaths.Select(path =>
            new Image(Path.GetFileName(path), File.ReadAllBytes(path))).ToArray();
        var definitions = images.Concat(framework).Select(image => image.Descriptor()).ToArray();
        TypeResolutionRequest[] requests = input.Enums.Select(evidence =>
        {
            Assert.False(string.IsNullOrWhiteSpace(evidence.Source));
            Assert.True(TypeResolutionEnumWidth.TryCreateRequest(
                evidence.Definition, definitions[0], AssemblyResolutionScope.Any, out var request));
            Assert.Equal(evidence.Name, request.Type.ToMetadataFullName());
            return request;
        }).ToArray();
        using var context = TypeResolutionContext.Create(
            new FixtureBindingPolicy(definitions), definitions, requests);
        foreach (var (request, evidence) in requests.Zip(input.Enums))
        {
            var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(context.Resolve(request));
            Assert.True(context.TryGetEnumUnderlyingType(resolved.Definition, out var width));
            Assert.Equal(evidence.UnderlyingType, width);
        }

        var frozen = TypeResolutionEnumWidth.CreateResolver(context, requests);
        var plannedNames = input.Enums.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        bool Resolve(string name, out PrimitiveTypeCode width)
        {
            width = default;
            if (!plannedNames.Contains(name))
                return false;
            width = frozen(name);
            return true;
        }

        var oracle = CreateSourceOracle(input.Enums);
        var observations = new List<Observation>();
        foreach (var image in images.Take(input.Assemblies.Length))
        {
            using var pe = new PEReader(new MemoryStream(image.Bytes, writable: false));
            MetadataReader reader = pe.GetMetadataReader();
            observations.Add(Sweep(
                image.Path, reader, reader.CustomAttributes, oracle, Resolve));
        }

        var fixtureCases = CustomAttributeFidelitySamples.EnumCases.ToArray();
        var fixtures = new CustomAttributeFidelityTests();
        foreach (object[] sample in fixtureCases)
            fixtures.RetainedCrossAssemblyEnums_EqualProducerTruth(
                (Type)sample[0], (CustomAttributeValue<string>)sample[1]);

        var report = new
        {
            SchemaVersion = 1,
            Decoder = AssemblyEvidence(typeof(AttributeDecoder).Assembly),
            Harness = AssemblyEvidence(typeof(CustomAttributeCorpusTests).Assembly),
            Oracle = AssemblyEvidence(typeof(MetadataReader).Assembly),
            Runtime = RuntimeInformation.FrameworkDescription,
            CheckedOutCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            Package = input,
            FrameworkDefinitions = framework.Select(image => new ImageEntry(image.Path, Hash(image.Bytes))),
            CompanionFixtureProducerSdk = typeof(CustomAttributeCorpusTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "D3FixtureProducerSdk").Value,
            CompanionProducerTruthCases = fixtureCases.Length,
            Assemblies = observations,
            Passed = observations.All(observation => observation.Passed),
        };
        string json = JsonSerializer.Serialize(report, JsonOptions);
        output.WriteLine(json);
        if (Environment.GetEnvironmentVariable("DOTNET_INSPECT_D3_REPORT") is { Length: > 0 } reportPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await File.WriteAllTextAsync(reportPath, json + "\n", cancellationToken);
        }

        Assert.All(observations, observation => Assert.True(
            observation.Passed,
            $"{observation.Path}: {observation.Rows} rows, {observation.Equal} equal, "
            + $"{observation.Refused} refused, {observation.OracleRejected} oracle failures, "
            + $"{observation.Different} differences, {observation.Defaulted} defaulted. "
            + string.Join("; ", observation.Failures)));
    }

    [Fact]
    public void SourceOracle_DoesNotGuessAnUnlistedEnumWidth()
    {
        var provider = new IndependentProvider();
        Assert.Throws<NotSupportedException>(() => provider.GetUnderlyingEnumType("Unlisted.Enum"));
    }

    [Fact]
    public void SourceOracle_PreservesSerializedEnumAndTypeofNames()
    {
        const string serialized = "Example.E, Example, Version=1.0.0.0";
        var oracle = CreateSourceOracle(
            [new("Example.E", "Example.E, Example", [serialized], PrimitiveTypeCode.Int64, "source")]);
        Assert.Equal(serialized, oracle.GetTypeFromSerializedName(serialized));
        Assert.Equal(PrimitiveTypeCode.Int64, oracle.GetUnderlyingEnumType(serialized));
        AssertValuesEqual(
            new([new("System.Type", serialized)], []),
            new([new("System.Type", serialized)], []));
    }

    [Fact]
    public void Sweep_AccountsForCompilerProducedRowsAndOracleFailure()
    {
        var sample = typeof(CustomAttributeFidelitySamples.Primitives);
        using var pe = new PEReader(File.OpenRead(sample.Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        var handle = (TypeDefinitionHandle)MetadataTokens.EntityHandle(sample.MetadataToken);
        var result = Sweep("primitive fixture", reader,
            reader.GetTypeDefinition(handle).GetCustomAttributes(), CreateSourceOracle([]), null);
        Assert.True(result.Passed);
        Assert.Equal(result.Rows, result.Equal);

        handle = (TypeDefinitionHandle)MetadataTokens.EntityHandle(
            typeof(CustomAttributeCorpusSamples.WithLocalEnum).MetadataToken);
        result = Sweep("enum without an oracle", reader,
            reader.GetTypeDefinition(handle).GetCustomAttributes(), CreateSourceOracle([]), null);
        Assert.False(result.Passed);
        Assert.Equal(result.Rows, result.OracleRejected);
        Assert.Equal(result.Rows, result.Equal + result.Refused + result.OracleRejected + result.Different);
    }

    [Fact]
    public void EmptyObservationAndWrongImageCannotPass()
    {
        Assert.False(new Observation("empty", 0, 0, 0, 0, 0, 0, []).Passed);
        Assert.Throws<InvalidDataException>(() => VerifyHash([1, 2], Hash([1, 3]), "fixture"));
    }

    static Observation Sweep(
        string path, MetadataReader reader, CustomAttributeHandleCollection handles,
        IndependentProvider oracle, AttributeDecoder.EnumWidthResolver? resolver)
    {
        int equal = 0, refused = 0, oracleRejected = 0, different = 0, defaulted = 0;
        var failures = new List<string>();
        foreach (var handle in handles)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            var attribute = reader.GetCustomAttribute(handle);
            var actual = AttributeDecoder.TryDecodeDetailed(
                reader, attribute, enumUnderlyingType: resolver, preserveSerializedTypeNames: true);
            if (actual is not { } decoded)
            {
                refused++;
                Failure("product refusal");
                continue;
            }
            if (decoded.FixedArgumentEnumWidthDefaulted.Any(value => value)
                || decoded.NamedArgumentEnumWidthDefaulted.Any(value => value))
            {
                defaulted++;
                Failure("defaulted enum width");
            }
            CustomAttributeValue<string> expected;
            try
            {
                expected = attribute.DecodeValue(oracle);
            }
            catch (Exception ex) when (ex is NotSupportedException or BadImageFormatException)
            {
                oracleRejected++;
                Failure($"oracle: {ex.Message}");
                continue;
            }
            try
            {
                AssertValuesEqual(expected, decoded.Value);
                equal++;
            }
            catch (XunitException ex)
            {
                different++;
                Failure($"value mismatch: {ex.Message}");
            }

            void Failure(string message)
            {
                if (failures.Count < 20)
                    failures.Add($"0x{MetadataTokens.GetToken(handle):x8}: {message}");
            }
        }
        return new(path, handles.Count, equal, refused, oracleRejected, different, defaulted, failures);
    }

    static CorpusInput LoadInput()
    {
        using var stream = typeof(CustomAttributeCorpusTests).Assembly.GetManifestResourceStream(
            "ILInspector.Metadata.Tests.Corpus.custom-attribute-d3.json")
            ?? throw new InvalidDataException("D3 corpus record is missing.");
        var input = JsonSerializer.Deserialize<CorpusInput>(stream, JsonOptions)
            ?? throw new InvalidDataException("D3 corpus record is empty.");
        Assert.Equal(1, input.SchemaVersion);
        return input;
    }

    static Image ReadImage(ZipArchive zip, ImageEntry entry)
    {
        using var stream = (zip.GetEntry(entry.Path)
            ?? throw new InvalidDataException($"Missing D3 image: {entry.Path}")).Open();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        byte[] image = bytes.ToArray();
        VerifyHash(image, entry.Sha256, entry.Path);
        return new(entry.Path, image);
    }

    static object AssemblyEvidence(Assembly assembly) => new
    {
        assembly.FullName,
        InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        Sha256 = Hash(File.ReadAllBytes(assembly.Location)),
    };

    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static void VerifyHash(byte[] bytes, string expected, string name)
    {
        if (Hash(bytes) != expected)
            throw new InvalidDataException($"D3 content hash mismatch: {name}");
    }

    static IndependentProvider CreateSourceOracle(IEnumerable<EnumEvidence> enums)
    {
        var widths = new Dictionary<string, PrimitiveTypeCode>(StringComparer.Ordinal);
        foreach (var evidence in enums)
        {
            widths.Add(evidence.Name, evidence.UnderlyingType);
            foreach (string serialized in evidence.SerializedNames)
                widths.Add(serialized, evidence.UnderlyingType);
        }
        return new(widths);
    }

    sealed record Image(string Path, byte[] Bytes)
    {
        public ResolvedAssemblyReference Descriptor()
        {
            using var pe = new PEReader(new MemoryStream(Bytes, writable: false));
            return ResolvedAssemblyReference.Create(
                AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()),
                path: null,
                openRead: () => new MemoryStream(Bytes, writable: false),
                provenance: AssemblyResolutionProvenance.Local($"D3 image: {Path}"));
        }
    }

    sealed record ImageEntry(string Path, string Sha256);
    sealed record ProducerEvidence(
        string Sdk, string SourceCommit, string BuildUrl, long BuildArtifactId,
        string BuildArtifactSha256, string UnsignedPackageSha256);
    sealed record EnumEvidence(
        string Name, string Definition, string[] SerializedNames,
        PrimitiveTypeCode UnderlyingType, string Source);
    sealed record CorpusInput(
        int SchemaVersion, string Id, string Version, string Sha256, ProducerEvidence Producer,
        ImageEntry[] Assemblies, ImageEntry[] RetainedDependencies, EnumEvidence[] Enums);
    sealed record Observation(
        string Path, int Rows, int Equal, int Refused, int OracleRejected,
        int Different, int Defaulted, IReadOnlyList<string> Failures)
    {
        public bool Passed => Rows > 0 && Equal == Rows
            && Refused == 0 && OracleRejected == 0 && Different == 0 && Defaulted == 0;
    }
}
