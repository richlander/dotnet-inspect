using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class ApiHeaderProvenanceTests
{
    [Theory]
    [InlineData("Example.Package", "2.0.0", "NuGet", "net10.0")]
    [InlineData("Example.Package", "2.0.0", "Project", "net10.0")]
    [InlineData("Example.Package", null, "NuGet", null)]
    [InlineData(null, "11.0.0", "Platform", "net11.0")]
    [InlineData(null, null, "Library", null)]
    public async Task TypeHeader_SeparatesSubjectFromProvenance(
        string? package, string? version, string source, string? tfm)
    {
        var output = new StringWriter();
        var options = new TypeOptions
        {
            MarkdownExplicitlySet = true,
            Verbosity = Verbosity.Quiet
        };

        int exit = await ApiCommand.WriteTypeOutputAsync(
            CreateType(), "lib/net10.0/Example.dll", package, version,
            source, tfm, options, output);

        string text = output.ToString();
        Assert.Equal(0, exit);
        Assert.StartsWith("# Example.Widget\n\n", text);
        Assert.Contains("Library: lib/net10.0/Example.dll", text);
        Assert.Contains($"Source: {source}", text);
        AssertField(text, "Package", package);
        AssertField(text, "Version", version);
        AssertField(text, "TFM", tfm);
        Assert.True(text.IndexOf("Library:", StringComparison.Ordinal)
            < text.IndexOf("Kind:", StringComparison.Ordinal));
        Assert.DoesNotContain("## Methods", text);
    }

    [Theory]
    [InlineData(Verbosity.Quiet, false)]
    [InlineData(Verbosity.Minimal, false)]
    [InlineData(Verbosity.Normal, false)]
    [InlineData(Verbosity.Detailed, false)]
    [InlineData(Verbosity.Minimal, true)]
    [InlineData(Verbosity.Normal, true)]
    [InlineData(Verbosity.Detailed, true)]
    public async Task TypePlaintext_RespectsProvenanceVisibility(
        Verbosity verbosity, bool focused)
    {
        var output = new StringWriter();
        var options = new TypeOptions
        {
            PlainText = true,
            Format = OutputFormat.PlainText,
            Verbosity = verbosity,
            IncludeSections = focused ? ["Methods"] : null
        };

        int exit = await ApiCommand.WriteTypeOutputAsync(
            CreateType(), "lib/net10.0/Example.dll", "Example.Package", "2.0.0",
            "NuGet", "net10.0", options, output);

        string text = output.ToString();
        Assert.Equal(0, exit);
        Assert.DoesNotContain("(Example.Package", text);
        if (focused)
        {
            Assert.Contains("Run", text);
            Assert.DoesNotContain("Package:", text);
            Assert.DoesNotContain("Library:", text);
        }
        else
        {
            Assert.StartsWith("Example.Widget\n\n", text);
            Assert.Contains("Package: Example.Package", text);
            Assert.Contains("Version: 2.0.0", text);
            Assert.Contains("TFM: net10.0", text);
            Assert.Contains("Library: lib/net10.0/Example.dll", text);
            Assert.Contains("Source: NuGet", text);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MemberInventory_KeepsProvenanceOutsideTitle(bool focused, bool plaintext)
    {
        var output = new StringWriter();
        var options = new MemberOptions
        {
            Verbosity = Verbosity.Minimal,
            MemberFilter = ["Run"],
            IncludeSections = focused ? ["Methods"] : null
        };
        if (plaintext)
            options = options with { PlainText = true, Format = OutputFormat.PlainText };

        int exit = await ApiCommand.WriteTypeOutputAsync(
            CreateType(), "lib/net10.0/Example.dll", "Example.Package", "2.0.0",
            "NuGet", "net10.0", options, output);

        string text = output.ToString();
        Assert.Equal(0, exit);
        Assert.Contains("Run", text);
        Assert.DoesNotContain("(Example.Package", text);
        if (focused)
        {
            Assert.DoesNotContain("Package:", text);
            Assert.DoesNotContain("Library:", text);
        }
        else
        {
            Assert.StartsWith(plaintext ? "Example.Widget\n\n" : "# Example.Widget\n\n", text);
            Assert.Contains("Package: Example.Package", text);
            Assert.Contains("Version: 2.0.0", text);
            Assert.Contains("Library: lib/net10.0/Example.dll", text);
        }
    }

    [Fact]
    public async Task SelectedMember_KeepsSubjectAndAcquisitionSummary()
    {
        var output = new StringWriter();
        var options = new MemberOptions
        {
            Verbosity = Verbosity.Minimal,
            MemberFilter = ["Run"],
            OverloadIndex = 1,
            IncludeSections = ["Signature"]
        };

        int exit = await ApiCommand.WriteTypeOutputAsync(
            CreateType(), "lib/net10.0/Example.dll", "Example.Package", "2.0.0",
            "NuGet", "net10.0", options, output);

        string text = output.ToString();
        Assert.Equal(0, exit);
        Assert.StartsWith("# Example.Widget.Run\n\n", text);
        Assert.Contains("Package: Example.Package", text);
        Assert.Contains("Library: lib/net10.0/Example.dll", text);
        Assert.Contains("## Signature", text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectedAsset_UsesPackageRelativeOrLocalPath(bool packageRootRetained)
    {
        string root = Path.GetFullPath("example-package");
        string path = Path.Combine(root, "lib", "net10.0", "Example.dll");
        var loaded = new ApiServices.LoadedApiSurface(
            new ApiSurface(), path, path,
            new Dictionary<ApiType, ResolvedAssemblyReference>());

        string actual = loaded.GetLibraryAssetPath(packageRootRetained ? root : null);

        Assert.Equal(packageRootRetained ? "lib/net10.0/Example.dll" : path, actual);
    }

    [Fact]
    public void DescriptorlessRoot_RetainsItsActualInputPath()
    {
        var type = CreateType();
        string path = Path.GetFullPath("Example.netmodule");
        var loaded = new ApiServices.LoadedApiSurface(
            new ApiSurface { Types = [type] }, path, path,
            new Dictionary<ApiType, ResolvedAssemblyReference>());
        Assert.Equal(path, loaded.GetLibraryAssetPath(null));
    }

    private static ApiType CreateType() => new()
    {
        Namespace = "Example",
        Name = "Widget",
        Kind = "class",
        Members =
        [
            new ApiMember { Name = "Run", Kind = "method", Signature = "void Run()" }
        ]
    };

    private static void AssertField(string output, string name, string? value)
    {
        if (value is null)
            Assert.DoesNotContain($"{name}:", output);
        else
            Assert.Contains($"{name}: {value}", output);
    }
}
