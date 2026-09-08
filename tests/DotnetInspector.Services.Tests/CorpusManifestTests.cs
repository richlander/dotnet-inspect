namespace DotnetInspector.Services.Tests;

public class CorpusManifestTests
{
    [Fact]
    public void JsonRoundTrip_PreservesSchemaAndEntries()
    {
        var manifest = new CorpusManifest
        {
            Entries =
            [
                new CorpusManifestEntry(AssemblySetSourceKind.Package, "Newtonsoft.Json", "13.0.3", "net8.0"),
                new CorpusManifestEntry(AssemblySetSourceKind.PlatformFramework, "Microsoft.NETCore.App", "10.0.0"),
                new CorpusManifestEntry(AssemblySetSourceKind.Assembly, @"C:\pkgs\Local.dll"),
            ],
        };

        var json = manifest.ToJson();
        var roundTripped = CorpusManifest.FromJson(json);

        Assert.Equal(CorpusManifest.CurrentSchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal(manifest.Entries, roundTripped.Entries);
    }

    [Fact]
    public void ToJson_SerializesEnumAsNameAndOmitsNulls()
    {
        var manifest = new CorpusManifest
        {
            Entries = [new CorpusManifestEntry(AssemblySetSourceKind.Package, "Serilog", "3.1.1")],
        };

        var json = manifest.ToJson();

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"Package\"", json);
        // Tfm was null and must be omitted rather than serialized as an explicit null.
        Assert.DoesNotContain("\"tfm\"", json);
    }

    [Fact]
    public void FromJson_UnsupportedSchemaVersion_Throws()
    {
        const string json = """
        { "schemaVersion": 99, "entries": [] }
        """;

        Assert.Throws<NotSupportedException>(() => CorpusManifest.FromJson(json));
    }

    [Fact]
    public void FromJson_NullDocument_Throws()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => CorpusManifest.FromJson("null"));
    }

    [Fact]
    public void FromJson_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => CorpusManifest.FromJson(string.Empty));
    }

    [Fact]
    public void FromJson_EmptyEntries_ParsesAsEmptyManifest()
    {
        // An empty entry list is valid data and must round-trip; refusing to *operate* on an empty
        // corpus is enforced at the population boundary, not during parsing.
        const string json = """
        { "schemaVersion": 1, "entries": [] }
        """;

        var manifest = CorpusManifest.FromJson(json);

        Assert.Empty(manifest.Entries);
    }

    [Fact]
    public void FromJson_MissingEntries_ParsesAsEmptyManifest()
    {
        const string json = """
        { "schemaVersion": 1 }
        """;

        var manifest = CorpusManifest.FromJson(json);

        Assert.Empty(manifest.Entries);
    }

    [Fact]
    public void EmptyManifest_JsonRoundTripsSymmetrically()
    {
        var roundTripped = CorpusManifest.FromJson(new CorpusManifest().ToJson());

        Assert.Empty(roundTripped.Entries);
        Assert.Equal(CorpusManifest.CurrentSchemaVersion, roundTripped.SchemaVersion);
    }

    [Fact]
    public void Entries_DefensivelyCopiesAndExposesReadOnlyView()
    {
        var source = new List<CorpusManifestEntry>
        {
            new(AssemblySetSourceKind.Package, "P", "1.0.0"),
        };

        var manifest = new CorpusManifest { Entries = source };
        source.Clear(); // mutating the original must not affect the manifest

        Assert.Single(manifest.Entries);
        Assert.IsNotType<List<CorpusManifestEntry>>(manifest.Entries);
        Assert.Throws<NotSupportedException>(() => ((IList<CorpusManifestEntry>)manifest.Entries).Clear());
    }
}
