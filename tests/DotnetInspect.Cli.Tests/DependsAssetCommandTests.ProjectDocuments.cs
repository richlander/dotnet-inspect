namespace DotnetInspect.Cli.Tests;

public partial class DependsAssetCommandTests
{
    private const string SuppliedAuthoredProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
          <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup>
        </Project>
        """;

    private const string SuppliedAssets = """
        {
          "version": 3,
          "targets": {
            "net11.0": {
              "Newtonsoft.Json/13.0.3": { "type": "package" }
            }
          },
          "projectFileDependencyGroups": {
            "net11.0": [ "Newtonsoft.Json >= 13.0.3" ]
          },
          "project": {
            "frameworks": {
              "net11.0": {
                "dependencies": {
                  "Newtonsoft.Json": { "target": "Package", "version": "[13.0.3, )" }
                }
              }
            }
          }
        }
        """;

    [Theory]
    [InlineData("--json")]
    [InlineData("--envelope")]
    public async Task SuppliedAuthoredDocumentPreservesTypedContent(string format)
    {
        await WithSuppliedProjectFileAsync("Input.csproj", SuppliedAuthoredProject, async path =>
        {
            var result = await RunCapturedOfflineAsync(
                ["depends", "--project-document", path, format, "--compact"]);
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using var json = System.Text.Json.JsonDocument.Parse(result.Output);
            var content = format == "--envelope"
                ? json.RootElement.GetProperty("content")
                : json.RootElement;
            Assert.Equal("AuthoredProject", content.GetProperty("kind").GetString());
            Assert.Equal("Input.csproj", content.GetProperty("declared_name").GetString());
            var authored = content.GetProperty("authored_project");
            Assert.Equal("available", authored.GetProperty("kind").GetString());
            var declaration = Assert.Single(authored.GetProperty("value")
                .GetProperty("package_declarations").EnumerateArray());
            Assert.Equal("newtonsoft.json",
                declaration.GetProperty("canonical_package_id").GetString());
            Assert.Equal("Newtonsoft.Json",
                declaration.GetProperty("source_package_id_spelling").GetString());
            Assert.Equal("13.0.3",
                declaration.GetProperty("source_version_constraint_spelling").GetString());
            Assert.Equal("unconditional",
                declaration.GetProperty("condition").GetProperty("kind").GetString());
            Assert.Empty(content.GetProperty("evidence")
                .GetProperty("restored_packages").EnumerateArray());
            if (format == "--envelope")
            {
                Assert.Equal("project-input",
                    json.RootElement.GetProperty("result_kind").GetString());
                Assert.Equal(1, json.RootElement.GetProperty("schema_version").GetInt32());
                Assert.Contains("Local project evidence",
                    json.RootElement.GetProperty("share").GetRawText(),
                    StringComparison.Ordinal);
                Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            }
        });
    }

    [Theory]
    [InlineData("TargetFramework", "net11.0", "target-framework", 0)]
    [InlineData("Configuration", "Release", "unresolved", 1)]
    public async Task SuppliedAuthoredConditionsRetainTheirPayloads(
        string property, string value, string kind, int exitCode)
    {
        string input = SuppliedAuthoredProject.Replace(
            "<ItemGroup>",
            $"<ItemGroup Condition=\"'$({property})' == '{value}'\">",
            StringComparison.Ordinal);
        await WithSuppliedProjectFileAsync("Input.csproj", input, async path =>
        {
            var result = await RunCapturedOfflineAsync(
                ["depends", "--project-document", path, "--json"]);
            Assert.Equal(exitCode, result.ExitCode);
            using var json = System.Text.Json.JsonDocument.Parse(result.Output);
            var declaration = Assert.Single(json.RootElement.GetProperty("authored_project")
                .GetProperty("value").GetProperty("package_declarations").EnumerateArray());
            var condition = declaration.GetProperty("condition");
            Assert.Equal(kind, condition.GetProperty("kind").GetString());
            string? spelling = kind == "target-framework"
                ? condition.GetProperty("source_spelling").GetString()
                : Assert.Single(condition.GetProperty("source_spellings").EnumerateArray()).GetString();
            Assert.Contains(value, spelling, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task SuppliedCentralVersionsStayIncompleteAndDoNotUseAdjacentAssets()
    {
        // Shape retained from DotnetInspect.Cli.csproj at bebfab236a6a3061bc74c53d22067acb86050afc.
        const string authored = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>$(DefaultTargetFramework)</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Markout" /></ItemGroup>
            </Project>
            """;
        await WithSuppliedProjectFileAsync("Input.csproj", authored, async path =>
        {
            string obj = Path.Combine(Path.GetDirectoryName(path)!, "obj");
            Directory.CreateDirectory(obj);
            await File.WriteAllTextAsync(Path.Combine(obj, "project.assets.json"), SuppliedAssets);
            var result = await RunCapturedOfflineAsync(
                ["depends", "--project-document", path, "--envelope"]);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("incomplete", result.Error, StringComparison.Ordinal);
            using var json = System.Text.Json.JsonDocument.Parse(result.Output);
            var content = json.RootElement.GetProperty("content");
            var provider = content.GetProperty("authored_project");
            Assert.Equal("incomplete", provider.GetProperty("kind").GetString());
            var declaration = Assert.Single(provider.GetProperty("value")
                .GetProperty("package_declarations").EnumerateArray());
            Assert.Equal("markout", declaration.GetProperty("canonical_package_id").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null,
                declaration.GetProperty("canonical_version_constraint").ValueKind);
            Assert.NotEmpty(provider.GetProperty("limitations").EnumerateArray());
            Assert.Empty(content.GetProperty("evidence")
                .GetProperty("restored_packages").EnumerateArray());

            var table = await RunCapturedOfflineAsync(["depends", "--project-document", path]);
            Assert.Equal(1, table.ExitCode);
            Assert.Contains("Markout", table.Output, StringComparison.Ordinal);
            Assert.Contains("(unresolved)", table.Output, StringComparison.Ordinal);

            var legacy = await RunCapturedOfflineAsync(
                ["depends", "--project", path, "-S", "Dependencies", "--json"]);
            Assert.Equal(0, legacy.ExitCode);
            Assert.Contains("newtonsoft.json", legacy.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("markout", legacy.Output, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("available")]
    [InlineData("unavailable")]
    [InlineData("partial")]
    [InlineData("graph-failed")]
    [InlineData("document-failed")]
    public async Task SuppliedAssetsPreserveProviderVariantsAndPayloads(string scenario)
    {
        string input = scenario switch
        {
            "partial" => SuppliedAssets.Replace(
                """{ "type": "package" }""",
                """{ "type": "package", "dependencies": { "Missing.Package": "1.0.0" } }""",
                StringComparison.Ordinal),
            "graph-failed" => SuppliedAssets.Replace(
                "\"targets\": {", "\"targets\": { \"NET11.0\": {},",
                StringComparison.Ordinal),
            "document-failed" => "{",
            _ => SuppliedAssets,
        };
        await WithSuppliedProjectFileAsync("project.assets.json", input, async path =>
        {
            string[] target = scenario == "unavailable" ? ["--tfm", "net12.0"] : [];
            var result = await RunCapturedOfflineAsync(
                ["depends", "--project-document", path, .. target, "--json", "--compact"]);
            Assert.Equal(scenario == "available" ? 0 : 1, result.ExitCode);
            using var json = System.Text.Json.JsonDocument.Parse(result.Output);
            var traversal = json.RootElement.GetProperty("restored_traversal");
            string expected = scenario is "partial" ? "available"
                : scenario.EndsWith("-failed", StringComparison.Ordinal) ? "failed" : scenario;
            Assert.Equal(expected, traversal.GetProperty("kind").GetString());
            if (scenario is "available" or "partial")
            {
                var value = traversal.GetProperty("value");
                Assert.Equal(scenario == "partial" ? "Partial" : "Complete",
                    value.GetProperty("completion").GetString());
                var facts = value.GetProperty("facts");
                Assert.Equal("available", facts.GetProperty("declaration").GetProperty("kind").GetString());
                Assert.Equal("available", facts.GetProperty("graph").GetProperty("kind").GetString());
                Assert.Equal("unavailable",
                    facts.GetProperty("package_pruning").GetProperty("kind").GetString());
                Assert.Contains("13.0.3", value.GetRawText(), StringComparison.Ordinal);
            }
            else if (scenario == "unavailable")
            {
                Assert.Equal("unavailable", traversal.GetProperty("facts")
                    .GetProperty("graph").GetProperty("kind").GetString());
                Assert.NotEmpty(json.RootElement.GetProperty("evidence")
                    .GetProperty("dependencies").EnumerateArray());
            }
            else
            {
                var failure = traversal.GetProperty("failure");
                Assert.Equal(scenario == "graph-failed" ? "graph" : "document",
                    failure.GetProperty("kind").GetString());
                Assert.Equal(scenario == "graph-failed" ? "AmbiguousTargetIdentity" : "MalformedOrDuplicateBearingJson",
                    failure.GetProperty("failure").GetProperty("reason").GetString());
            }
            if (scenario != "available")
                Assert.NotEmpty(result.Error);
        });
    }

    [Theory]
    [InlineData("--envelope")]
    [InlineData("--json")]
    [InlineData("--table")]
    [InlineData("--markdown")]
    [InlineData("--tsv")]
    public async Task SuppliedFailedPruningIsVisibleDespiteACompleteGraph(string format)
    {
        string input = SuppliedAssets.Replace(
            "\"dependencies\": {",
            "\"packagesToPrune\": \"invalid\", \"dependencies\": {",
            StringComparison.Ordinal);
        await WithSuppliedProjectFileAsync("project.assets.json", input, async path =>
        {
            bool structured = format is "--envelope" or "--json";
            string[] selection = structured ? [] : ["-S", "Failures"];
            var result = await RunCapturedOfflineAsync(
                ["depends", "--project-document", path, .. selection, format]);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("packagesToPrune", result.Error, StringComparison.Ordinal);
            if (structured)
            {
                using var json = System.Text.Json.JsonDocument.Parse(result.Output);
                var content = format == "--envelope"
                    ? json.RootElement.GetProperty("content")
                    : json.RootElement;
                var traversal = content.GetProperty("restored_traversal").GetProperty("value");
                Assert.Equal("Complete", traversal.GetProperty("completion").GetString());
                Assert.Equal("failed", traversal.GetProperty("facts")
                    .GetProperty("package_pruning").GetProperty("kind").GetString());
                var failure = Assert.Single(content.GetProperty("evidence")
                    .GetProperty("failures").EnumerateArray());
                Assert.Equal("Pruning", failure.GetProperty("phase").GetString());
                Assert.Equal("InvalidShape", failure.GetProperty("reason").GetString());
            }
            else
            {
                Assert.Contains("Pruning", result.Output, StringComparison.Ordinal);
                Assert.Contains("InvalidShape", result.Output, StringComparison.Ordinal);
                Assert.Contains("packagesToPrune", result.Output, StringComparison.Ordinal);
            }
        });
    }

    [Theory]
    [InlineData("Input.csproj", 1_048_577)]
    [InlineData("project.assets.json", 4_194_305)]
    public async Task OversizedSuppliedDocumentsRetainTypedProviderRejection(string name, int length)
    {
        await WithSuppliedProjectFileAsync(name, new string(' ', length), async path =>
        {
            var result = await RunCapturedOfflineAsync(
                ["depends", "--project-document", path, "--envelope"]);
            Assert.Equal(1, result.ExitCode);
            Assert.NotEmpty(result.Error);
            using var json = System.Text.Json.JsonDocument.Parse(result.Output);
            Assert.Contains("ConfiguredLimitExceeded",
                json.RootElement.GetProperty("content").GetRawText(), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    public async Task SuppliedDeclarationsUseNormalFormatLowering(string format)
    {
        await WithSuppliedProjectFileAsync("Input.csproj", SuppliedAuthoredProject, async path =>
        {
            var result = await RunCapturedOfflineAsync(
                ["depends", "--project-document", path, "-S", "Dependencies", format]);
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            Assert.Contains("Newtonsoft.Json", result.Output, StringComparison.Ordinal);
            Assert.Contains("13.0.3", result.Output, StringComparison.Ordinal);
            if (format == "--markdown")
                Assert.Contains("Authored syntax only", result.Output, StringComparison.Ordinal);
            if (format == "--jsonl")
            {
                using var row = System.Text.Json.JsonDocument.Parse(result.Output);
                Assert.Equal("Newtonsoft.Json", row.RootElement.GetProperty("package").GetString());
            }
        });
    }

    [Theory]
    [InlineData("--package", "Newtonsoft.Json")]
    [InlineData("--project", "missing.csproj")]
    [InlineData("--depth", "1")]
    [InlineData("--count", null)]
    [InlineData("--share", null)]
    [InlineData("--rows", "1..2")]
    public async Task SuppliedDocumentRejectsUnrelatedSourceAndWorkGestures(string option, string? value)
    {
        string[] values = value is null ? [] : [value];
        var result = await RunCapturedAsync(
            ["depends", "--project-document", "missing.csproj", option, .. values]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(option, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Could not find", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuppliedDocumentDiscoveryDoesNotReadTheFile()
    {
        var result = await RunCapturedAsync(
            ["depends", "--project-document", "missing.csproj", "-D"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("Dependencies", result.Output, StringComparison.Ordinal);
        Assert.Contains("Restored Packages", result.Output, StringComparison.Ordinal);
        Assert.Contains("Failures", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedSuppliedProjectRetainsItsTypedFailure()
    {
        await WithSuppliedProjectFileAsync("Input.csproj", "<Project", async path =>
        {
            var result = await RunCapturedOfflineAsync(
                ["depends", "--project-document", path, "--envelope"]);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("well-formed XML", result.Error, StringComparison.Ordinal);
            using var json = System.Text.Json.JsonDocument.Parse(result.Output);
            var provider = json.RootElement.GetProperty("content").GetProperty("authored_project");
            Assert.Equal("failed", provider.GetProperty("kind").GetString());
            Assert.Equal("MalformedXml", provider.GetProperty("failure").GetProperty("reason").GetString());
        });
    }

    [Fact]
    public async Task MissingSuppliedProjectReportsAcquisitionFailureWithoutContent()
    {
        string path = Path.Combine(CreateTemporaryDirectory(), "missing.csproj");
        try
        {
            var result = await RunCapturedOfflineAsync(
                ["depends", "--project-document", path, "--envelope"]);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.NotEmpty(result.Error);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task SuppliedDocumentRespectsTheEnvironmentFormat()
    {
        string? previous = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "tsv");
            await WithSuppliedProjectFileAsync("Input.csproj", SuppliedAuthoredProject, async path =>
            {
                var result = await RunCapturedOfflineAsync(
                    ["depends", "--project-document", path, "-S", "Dependencies"]);
                Assert.Equal(0, result.ExitCode);
                Assert.Contains("Newtonsoft.Json\t13.0.3\t", result.Output, StringComparison.Ordinal);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", previous);
        }
    }

    private static async Task WithSuppliedProjectFileAsync(
        string name,
        string content,
        Func<string, Task> action)
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, name);
            await File.WriteAllTextAsync(path, content);
            await action(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
