using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Type_SingleType_SelectWithColumns_ProjectsColumns()    {        var options = new TypeOptions        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Name"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("| Name |", output);
        Assert.DoesNotContain("Return Type", output);
        Assert.DoesNotContain("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_SelectEmptySection_WritesNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Values"]  // enum-only section; JsonSerializer is a class
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("section 'Values' has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_SelectPopulatedSection_NoEmptyNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_JsonWithSelect_ScopesToSection()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            JsonOutput = true,
            Select = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        var members = doc.RootElement.GetProperty("members");
        Assert.True(members.GetArrayLength() > 0);
        foreach (var m in members.EnumerateArray())
            Assert.Equal("property", m.GetProperty("kind").GetString());
        // Non-selected facets are scoped out.
        Assert.Empty(doc.RootElement.GetProperty("interfaces").EnumerateArray());
    }

    [Fact]
    public async Task Type_SingleType_JsonWithSelectEmptySection_EmptyMembersAndNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            JsonOutput = true,
            Select = ["Values"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.Empty(doc.RootElement.GetProperty("members").EnumerateArray());
        Assert.Contains("section 'Values' has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverMethods_Schema_ListsAllColumns()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Methods"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Name", output);
        Assert.Contains("Signature", output);
    }

    [Fact]
    public async Task Type_SingleType_Discover_DefaultsToDiscoverableSections()
    {
        // -D with no --schema now defaults to effective discovery: it resolves the source
        // and lists only sections that actually have data (the empty-section footgun fix).
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverSchema_ListsAllStaticSections()
    {
        // --schema opts back out to the cheap, offline static schema listing, which
        // includes sections that may have no data (e.g. Custom Attributes, Fields).
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = [],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Custom Attributes | section |", output);
        Assert.Contains("| Fields | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_OnlyShowsSectionsWithData()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Methods | section |", output);
        Assert.DoesNotContain("| Source Files | section |", output);
        Assert.DoesNotContain("| Fields | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_SourceDiscovery_IncludesSelectableCodeSections()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = [SectionCategoryNames.Source]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Decompiled Source | section |", output);
        Assert.Contains("| PDB Source | section |", output);
        Assert.Contains("| IL | section |", output);
        Assert.DoesNotContain("| Properties | section |", output);
        Assert.DoesNotContain("| Method Groups | section |", output);
        Assert.DoesNotContain("| Facts | section", output);
    }

    [Fact]
    public async Task Type_SingleType_SourceFilesSection_RendersTypeSourceUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "-S", "Source Files", "--tips", "q", "-n", "28", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Files", output);
        Assert.Contains("| Url |", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_IncludesSelectableCustomAttributesSection()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
    }

    [Fact]
    public async Task Type_DiscoverEmptySection_Effective_ReportsNoDataNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Custom Attributes"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        // A valid-but-empty section reports a clear "no data" note rather than the
        // misleading "Section not found", and exits 0.
        Assert.Equal(0, exit);
        Assert.Contains("section 'Custom Attributes' has no data", error);
        Assert.DoesNotContain("not found", error);
        Assert.DoesNotContain("| Name | column |", output);
    }

    [Fact]
    public async Task Type_DiscoverUnknownSection_Effective_ReportsNotFound()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Bogus"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        // A genuinely unknown section still reports "not found" (with suggestions) and exits 1.
        Assert.Equal(1, exit);
        Assert.Contains("Section 'Bogus' not found", error);
    }

    [Fact]
    public async Task Type_DiscoverSection_WithoutEffective_HidesSelectColumn()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // The historical Select overload-index column is no longer queryable; selectors
        // live in the dedicated Member Index section.
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_SourceFiles_Urls_EmitsUrlColumn(bool preferRendered)
    {
        var (exit, output, error) = await RunAppAsync(
            [
                "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "--urls", "--tips", "q",
                .. preferRendered ? new[] { "--prefer-rendered-urls" } : [],
            ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.StartsWith(
            preferRendered
                ? "https://github.com/JamesNK/Newtonsoft.Json/blob/"
                : "https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/",
            line));
    }

    [Fact]
    public async Task Type_SourceFiles_UrlsJsonArray_EmitsSingleArrayDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--json-array", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(1, rows[0].GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", rows[0].GetProperty("url").GetString());
        Assert.Equal(2, rows[1].GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", rows[1].GetProperty("url").GetString());
    }

    [Fact]
    public async Task TypeListing_ColumnProjectionWithJson_IsRejected()
    {
        // #3386: --columns/--fields select table columns; document --format json has no column-slicing
        // facility. The combination used to silently drop the column filter and emit the whole
        // typed document; it now fails closed instead.
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Runtime", "-S", "Interfaces", "--columns", "Type", "--format=json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --format json", error);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task TypeListing_PayloadProjection_IsRejected()
    {
        // #3386: the type-listing surface exposes no printable payload, so a payload projection
        // used to dump the whole ~20 MB surface and then trip the projection audit. It now fails
        // closed before rendering, and the audit must not add a second, misleading line.
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Runtime", "-S", "Classes", "--value", "--format=json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not supported when listing types", error);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task SingleType_ColumnProjectionWithJson_IsRejected()
    {
        // #3386: the same rejection applies on the single-type path.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "--platform", "System.Runtime", "-S", "Methods", "--fields", "Name", "--format=json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --format json", error);
    }

    [Fact]
    public async Task GlobListing_Discovery_IsHonoredNotRejected()
    {
        // #3386 regression guard: the glob (and prefix-browse) fallback routes ignored -D
        // discovery and fell through to WriteFullApiOutput. Once that path rejects --fields with
        // --format json,
        // a discovery request there would have been rejected with a misleading column message.
        // Discovery must be dispatched before the projection guard, matching the main listing path.
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.Sample*", "--library", TestAssemblyPath,
            "-D", "Classes", "--fields", "Name", "--format=json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --format json", error);
        using var document = JsonDocument.Parse(output);
        Assert.NotEmpty(document.RootElement.EnumerateArray());
        Assert.All(
            document.RootElement.EnumerateArray(),
            row => Assert.Equal(
                ["name"],
                row.EnumerateObject().Select(property => property.Name)));
    }

    [Fact]
    public async Task GlobListing_Discovery_UsesMatchedTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.Sample*Constraint*", "--library", TestAssemblyPath,
            "-D", "Enums", "--format=table", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Section 'Enums' not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobListing_Discovery_RejectsUnmatchedGlob()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Zqqxnomatch.*", "--library", TestAssemblyPath,
            "-D", "Classes", "--fields", "Name", "--format=json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Type 'Zqqxnomatch.*' not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_SourceFiles_Value_RowSelectsUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--value", "--row", "2", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", output.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_SourceFiles_UrlsRejectsRowsMode()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--rows", "1", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--rows cannot be combined with --urls", error);
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRequiresRowWhenMultipleUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("selected section has 2 rows; use --row N|first|last to choose one row", error);
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRowFetchesSelectedSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "2", "--format=jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Contains("ReadAsInt32Async", document.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRowFirstFetchesFirstSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "first", "--format=jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", document.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRowLastFetchesLastSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "last", "--format=jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", document.RootElement.GetProperty("url").GetString());
    }


    [Fact]
    public async Task Type_SourceFiles_PrintJsonArrayEmitsSelectedRowAsSingleElementArray()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "1", "--json-array", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        var single = Assert.Single(rows);
        Assert.Equal(1, single.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", single.GetProperty("url").GetString());
        Assert.Contains("JsonReader", single.GetProperty("content").GetString());
    }

    /// <summary>
    /// <c>--json</c> selects an output format and <c>--print</c> selects an output shape,
    /// so they compose: the projection owns the request and the plain type surface must not
    /// claim it. Regression for #3379, where the type-surface early return preceded the
    /// projection dispatch and silently discarded <c>--print</c> with exit 0.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintJson_EmitsSelectedDocumentNotTypeSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "1", "--format=json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
        Assert.Equal("Source Files", document.RootElement.GetProperty("section").GetString());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Contains("JsonReader", document.RootElement.GetProperty("content").GetString());
        Assert.False(document.RootElement.TryGetProperty("metadata_name", out _));
    }

    /// <summary>
    /// Cardinality validation belongs to the projection, so it must run under <c>--json</c> too.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintJson_RequiresRowWhenMultipleUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--format=json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("selected section has 2 rows; use --row N|first|last to choose one row", error);
    }

    [Fact]
    public async Task Type_SourceFiles_UrlsJson_EmitsProjectedRowsNotTypeSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--format=json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", rows[0].GetProperty("url").GetString());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", rows[1].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Type_SourceFiles_ValueJson_EmitsSelectedRowNotTypeSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--value", "--row", "2", "--format=json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", document.RootElement.GetProperty("value").GetString());
    }

    /// <summary>
    /// A failed acquisition of the selected row must stay visible rather than degrade into
    /// success-shaped output. Only the transport is substituted, so the real SourceFetch
    /// still applies scheme restriction, caching, and status handling.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintRow_FetchFailureIsHardError()
    {
        using var client = new HttpClient(new NotFoundHandler());
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-fetch-failure-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "--print", "--row", "2", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("failed to fetch verified source for row 2", error);
            Assert.Contains("Could not fetch SourceLink source", error);
            Assert.DoesNotContain("/Src/Newtonsoft.Json/JsonReader.Async.cs", error);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    /// <summary>
    /// The acquisition guarantee must hold under <c>--json</c> as well; before #3379 this
    /// combination exited 0 with the type surface and never attempted the fetch.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceDocument_PrintRowJson_FetchFailureIsHardError(bool member)
    {
        using var client = new HttpClient(new NotFoundHandler());
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-fetch-failure-json-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                [
                    member ? "member" : "type", member ? "JsonConvert" : "JsonReader",
                    "--package", "Newtonsoft.Json@13.0.3",
                    .. member ? new[] { "-m", "SerializeObject" } : [],
                    "-S", member ? "Source Locations" : "Source Files",
                    "--print", "--row", "2", "--format=json", "--tips", "q",
                ]);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("failed to fetch verified source for row 2", error);
            Assert.Contains("Could not fetch SourceLink source", error);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRow_RedirectedChecksumMismatchIsHardError()
    {
        using var client = new HttpClient(new SourceResponseHandler(
            "redirected content"u8.ToArray(),
            "https://spsprodeus27.vssps.visualstudio.com/_signin"));
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-cross-origin-source-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "--print", "--row", "2", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("does not match the portable-PDB checksum", error);
            Assert.DoesNotContain("spsprodeus27", error);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceDocument_PrintRow_RejectsSameOriginChecksumMismatch(bool member)
    {
        using var client = new HttpClient(new SourceResponseHandler(
            "same-origin but wrong content"u8.ToArray()));
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-checksum-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                [
                    member ? "member" : "type", member ? "JsonConvert" : "JsonReader",
                    "--package", "Newtonsoft.Json@13.0.3",
                    .. member ? new[] { "-m", "SerializeObject" } : [],
                    "-S", member ? "Source Locations" : "Source Files",
                    "--print", "--row", "2", "--tips", "q",
                ]);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("does not match the portable-PDB checksum", error);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task Type_JsonArrayRequiresProjectionShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--json-array", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--json-array requires --value, --urls, --paths, or --print", error);
    }

    [Fact]
    public async Task Type_Count_RendersScalarForTableSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonConvert", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Member Index", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("67", output.Trim());
    }

    [Fact]
    public async Task Type_Count_RendersScalarForVectorSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("2", output.Trim());
    }

    [Fact]
    public async Task Type_AnnotatedSourceDocument_IsNotAdvertisedAsATypeSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "-S", "Annotated Source Document", "--format=json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Select value 'Annotated Source Document' not found", error);
    }

    [Fact]
    public async Task Type_DoesNotOfferFocus()
    {
        // The caret gesture only renders into member sections (Annotated
        // Source, Cost Overlay, Semantics Overlay). Offering --focus on `type`
        // would be a switch that cannot change any output there.
        var (exit, _, error) = await RunAppAsync(
            "type", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "--focus", "allocation", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.Contains("--focus", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Discovery_DoesNotListCostOverlay()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath, "-D", "--format=table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Cost Overlay", output);
        Assert.DoesNotContain("Semantics Overlay", output);
    }
}
