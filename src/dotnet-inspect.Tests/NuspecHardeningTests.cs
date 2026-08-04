using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspector.Models;
using DotnetInspector.Services;
using InertText;

namespace DotnetInspector.Tests;

public sealed class NuspecHardeningTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"dotnet-inspect-nuspec-{Guid.NewGuid():N}");

    public NuspecHardeningTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of test data.
        }
    }

    [Fact]
    public void PresentationBoundDescription_IsCarriedAsInertString()
    {
        Assert.Equal(typeof(InertString?), typeof(NuspecData).GetProperty(nameof(NuspecData.Description))!.PropertyType);
        Assert.Equal(typeof(InertString?), typeof(InspectionResult).GetProperty(nameof(InspectionResult.Description))!.PropertyType);
    }

    [Fact]
    public async Task MalformedNuspec_ProducesOneLineTypedDiagnostic()
    {
        string package = WritePackage(
            "Malformed.Package",
            """
            <package>
              <metadata>
                <id>SHOULD-NOT-REACH-THE-DIAGNOSTIC</metadata>
              </metadata>
            </package>
            """);

        var (exit, output, error) = await RunAppAsync("package", package, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Error: Package manifest is not well-formed XML at line ", error);
        Assert.DoesNotContain("SHOULD-NOT-REACH-THE-DIAGNOSTIC", error);
        Assert.DoesNotContain(nameof(System.Xml.XmlException), error);
        Assert.DoesNotContain(" at DotnetInspector.", error);
        Assert.Single(error.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'));
    }

    [Fact]
    public async Task HostileDescription_RemainsQuotedInMarkdownAndContainedInJson()
    {
        const string bidi = "\u202E";
        string description = $$"""
            Intro{{bidi}}tail

            ## Verified publisher

            | Check | Result |
            | ----- | ------ |
            | Signature | valid |
            """;
        string package = WritePackage(
            "Hostile.Description",
            $$"""
            <package>
              <metadata>
                <id>Hostile.Description</id>
                <version>1.0.0</version>
                <authors>example</authors>
                <description><![CDATA[{{description}}]]></description>
              </metadata>
            </package>
            """);

        var (markdownExit, markdown, markdownError) =
            await RunAppAsync("package", package, "--tips", "q");

        Assert.Equal(0, markdownExit);
        Assert.Empty(markdownError);
        string normalized = markdown.ReplaceLineEndings("\n");
        Assert.Contains("> Intro\\u202Etail", normalized);
        Assert.Contains("\n> ## Verified publisher", normalized);
        Assert.Contains("\n> | Check | Result |", normalized);
        Assert.DoesNotContain("\n## Verified publisher", normalized);
        int rawBidiIndex = normalized.IndexOf(bidi, StringComparison.Ordinal);
        Assert.True(rawBidiIndex < 0, $"Raw bidi scalar at output index {rawBidiIndex}.");

        var (jsonExit, json, jsonError) =
            await RunAppAsync("package", package, "--json", "--tips", "q");

        Assert.Equal(0, jsonExit);
        Assert.Empty(jsonError);
        using var document = JsonDocument.Parse(json);
        string serializedDescription =
            document.RootElement.GetProperty("description").GetString()!;
        Assert.StartsWith("Intro\\u202Etail", serializedDescription);
        Assert.Contains("\n## Verified publisher", serializedDescription);
        Assert.Equal(-1, serializedDescription.IndexOf(bidi, StringComparison.Ordinal));
        Assert.False(serializedDescription.StartsWith("> ", StringComparison.Ordinal));
    }

    private string WritePackage(string name, string nuspec)
    {
        string path = Path.Combine(_directory, $"{name}.1.0.0.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(
            archive.CreateEntry($"{name}.nuspec").Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(nuspec);
        return path;
    }

    private static Task<(int ExitCode, string Output, string Error)> RunAppAsync(
        params string[] args)
        => ConsoleCapture.RunAsync(async () =>
        {
            args = CommandLineBuilder.PreprocessArgs(args);
            var parseResult = CommandLineBuilder.CreateRootCommand().Parse(args);
            return await CommandLineBuilder.InvokeAsync(parseResult);
        });
}
