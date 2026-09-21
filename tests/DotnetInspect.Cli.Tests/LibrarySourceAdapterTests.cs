using System.IO.Compression;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using NuGetFetch;
using DesktopPackageExtractor =
    DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class LibrarySourceAdapterTests
{
    public LibrarySourceAdapterTests() =>
        NuGetCache.Initialize("dotnet-inspect");

    [Fact]
    public void PackageDeclarationKeepsLibraryTargetSeparate()
    {
        Assert.True(
            LibrarySourceAdapter.TryDeclare(
                "Microsoft.Azure.SignalR.Common.dll",
                "Microsoft.Azure.SignalR@1.33.1",
                platformAssembly: null,
                "source",
                out SourceIntent intent,
                out string? error),
            error);

        var package = Assert.IsType<SourceSelector.PackageReference>(
            Assert.Single(intent.Selectors));
        Assert.Equal("Microsoft.Azure.SignalR", package.PackageId);
        Assert.Equal("1.33.1", package.Version);

        Assert.True(
            LibrarySourceAdapter.TryBind(
                new LibraryOptions
                {
                    SourceIntent = intent,
                    AssemblyName =
                        "Microsoft.Azure.SignalR.Common.dll",
                },
                out LibrarySourceBinding? binding,
                out error),
            error);
        Assert.Equal(
            "Microsoft.Azure.SignalR.Common.dll",
            binding!.AssemblyName);
        Assert.Equal(
            "Microsoft.Azure.SignalR@1.33.1",
            binding.PackageArgument);
        Assert.False(binding.PackageTarget!.IsLocalFile);
        Assert.Null(binding.PlatformAssembly);
    }

    [Theory]
    [InlineData("Contoso", false)]
    [InlineData("Contoso@", true)]
    public void PackageBindingPreservesVersionExpressionPresence(
        string packageReference,
        bool hasVersionExpression)
    {
        Assert.True(
            LibrarySourceAdapter.TryDeclare(
                assemblyName: null,
                packageReference,
                platformAssembly: null,
                "source",
                out SourceIntent intent,
                out string? error),
            error);

        Assert.True(
            LibrarySourceAdapter.TryBind(
                new LibraryOptions
                {
                    SourceIntent = intent,
                },
                out LibrarySourceBinding? binding,
                out error),
            error);

        Assert.Equal(
            hasVersionExpression,
            binding!.PackageTarget!.DeclaredVersionExpression
                is not null);
        Assert.Equal(string.Empty, binding.PackageTarget.Version);
    }

    [Fact]
    public void LocalArchiveDeclarationPreservesPath()
    {
        const string path = "./local/Contoso.1.0.0.nupkg";

        Assert.True(
            LibrarySourceAdapter.TryDeclare(
                assemblyName: null,
                path,
                platformAssembly: null,
                "source",
                out SourceIntent intent,
                out string? error),
            error);

        var archive = Assert.IsType<SourceSelector.PackageArchive>(
            Assert.Single(intent.Selectors));
        Assert.Equal(path, archive.Path);

        Assert.True(
            LibrarySourceAdapter.TryBind(
                new LibraryOptions
                {
                    SourceIntent = intent,
                },
                out LibrarySourceBinding? binding,
                out error),
            error);
        Assert.True(binding!.PackageTarget!.IsLocalFile);
    }

    [Theory]
    [InlineData("relative/library.dll", typeof(SourceSelector.Library))]
    [InlineData("System.Text.Json", typeof(SourceSelector.PlatformLibrary))]
    public void UnaryLibrarySourcesDeclareOneSelector(
        string value,
        Type expectedType)
    {
        string? library =
            expectedType == typeof(SourceSelector.Library) ? value : null;
        string? platform =
            expectedType == typeof(SourceSelector.PlatformLibrary)
                ? value
                : null;

        Assert.True(
            LibrarySourceAdapter.TryDeclare(
                library,
                packagePath: null,
                platform,
                "source",
                out SourceIntent intent,
                out string? error),
            error);

        Assert.IsType(expectedType, Assert.Single(intent.Selectors));
    }

    [Fact]
    public void EmptyDeclarationSupportsStructuralDiscovery()
    {
        Assert.True(
            LibrarySourceAdapter.TryDeclare(
                assemblyName: null,
                packagePath: null,
                platformAssembly: null,
                "source",
                out SourceIntent intent,
                out string? error),
            error);

        Assert.Same(SourceIntent.Empty, intent);
    }

    [Fact]
    public void LegacyOptionsAreLoweredThroughTypedIntent()
    {
        Assert.True(
            LibrarySourceAdapter.TryBind(
                new LibraryOptions
                {
                    PackagePath = "Contoso@latest",
                    AssemblyName = "Contoso.dll",
                },
                out LibrarySourceBinding? binding,
                out string? error),
            error);

        Assert.IsType<SourceSelector.PackageReference>(
            Assert.Single(binding!.Intent.Selectors));
        Assert.Equal("Contoso@latest", binding.PackageArgument);
        Assert.Equal("Contoso.dll", binding.AssemblyName);
    }

    [Fact]
    public void MultiSourceDeclarationIsRejected()
    {
        var options = new LibraryOptions
        {
            SourceIntent = SourceIntent.Create(
            [
                new SourceSelector.Library("one.dll"),
                new SourceSelector.PlatformLibrary("System.Text.Json"),
            ]),
        };

        Assert.False(
            LibrarySourceAdapter.TryBind(
                options,
                out LibrarySourceBinding? binding,
                out string? error));

        Assert.Null(binding);
        Assert.Equal(
            "Library inspection accepts exactly one source selector.",
            error);
    }

    [Fact]
    public void UnsupportedSelectorIsRejected()
    {
        var options = new LibraryOptions
        {
            SourceIntent = SourceIntent.Create(
            [
                new SourceSelector.Project("example.csproj"),
            ]),
        };

        Assert.False(
            LibrarySourceAdapter.TryBind(
                options,
                out LibrarySourceBinding? binding,
                out string? error));

        Assert.Null(binding);
        Assert.Equal(
            "Library inspection does not support source selector 'Project'.",
            error);
    }

    [Fact]
    public async Task TypedPlatformDeclarationDrivesExecution()
    {
        var options = new LibraryOptions
        {
            SourceIntent = SourceIntent.Create(
            [
                new SourceSelector.PlatformLibrary(
                    "System.Text.Json"),
            ]),
            Select = ["Library Info"],
        };

        var (exit, output, error) =
            await ConsoleCapture.RunAsync(
                () => LibraryCommand.ExecuteAsync(options));

        Assert.True(
            exit == 0,
            $"Expected success.{Environment.NewLine}"
                + $"Error: {error}{Environment.NewLine}"
                + $"Output: {output}");
        Assert.Contains("## Library Info", output);
        Assert.Contains("System.Text.Json", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task TypedArchiveWithoutNupkgSuffixDrivesLocalAcquisition()
    {
        string root =
            Directory.CreateTempSubdirectory(
                "library-source-archive-").FullName;
        try
        {
            string archive = CreatePackage(
                root,
                "Local.Archive",
                "1.0.0",
                "local-package");
            var options = new LibraryOptions
            {
                SourceIntent = SourceIntent.Create(
                [
                    new SourceSelector.PackageArchive(archive),
                ]),
                AssemblyName = "DotnetInspect.Cli.Tests.dll",
                Select = ["Library Info"],
            };

            var (exit, output, error) =
                await ConsoleCapture.RunAsync(
                    () => LibraryCommand.ExecuteAsync(options));

            Assert.True(
                exit == 0,
                $"Expected success.{Environment.NewLine}"
                    + $"Error: {error}{Environment.NewLine}"
                    + $"Output: {output}");
            Assert.Contains("## Library Info", output);
            Assert.Contains("DotnetInspect.Cli.Tests", output);
            Assert.Empty(error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TypedArchiveWithoutNupkgSuffixDrivesAggregateAcquisition()
    {
        string root =
            Directory.CreateTempSubdirectory(
                "library-source-archive-aggregate-").FullName;
        try
        {
            string archive = CreatePackage(
                root,
                "Local.Archive",
                "1.0.0",
                "local-package");
            var options = new LibraryOptions
            {
                SourceIntent = SourceIntent.Create(
                [
                    new SourceSelector.PackageArchive(archive),
                ]),
                Select = ["Library Info"],
            };

            var (exit, output, error) =
                await ConsoleCapture.RunAsync(
                    () => LibraryCommand.ExecuteAsync(options));

            Assert.True(
                exit == 0,
                $"Expected success.{Environment.NewLine}"
                    + $"Error: {error}{Environment.NewLine}"
                    + $"Output: {output}");
            Assert.Contains(
                "## Library Info (lib/net11.0/DotnetInspect.Cli.Tests.dll)",
                output);
            Assert.Empty(error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task
        TypedPackageReferenceEndingNupkgUsesReferenceAcquisition()
    {
        const string PackageId = "Contoso.nupkg";
        var options = new LibraryOptions
        {
            SourceIntent = SourceIntent.Create(
            [
                new SourceSelector.PackageReference(PackageId),
            ]),
            AssemblyName = "DotnetInspect.Cli.Tests.dll",
        };
        Assert.True(
            LibrarySourceAdapter.TryBind(
                options,
                out LibrarySourceBinding? binding,
                out string? error),
            error);
        Assert.False(binding!.PackageTarget!.IsLocalFile);

        using var handler = new NotFoundPackageHandler();
        using var client = new HttpClient(handler);
        PackageExtractionOutcome outcome =
            await DesktopPackageExtractor.ExtractPackageAsync(
                client,
                binding.PackageTarget,
                sourceOptions: handler.SourceOptions);

        Assert.False(outcome.IsSuccess);
        Assert.Contains(PackageId, outcome.ErrorMessage);
        Assert.DoesNotContain(
            "File not found",
            outcome.ErrorMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            handler.Requests,
            uri => uri.AbsolutePath.Contains(
                "contoso.nupkg",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task
        ExplicitEmptyPackageVersionDoesNotResolveLatest()
    {
        Assert.True(
            LibrarySourceAdapter.TryDeclare(
                assemblyName: null,
                packagePath: "Contoso@",
                platformAssembly: null,
                "source",
                out SourceIntent intent,
                out string? error),
            error);
        Assert.True(
            LibrarySourceAdapter.TryBind(
                new LibraryOptions
                {
                    SourceIntent = intent,
                },
                out LibrarySourceBinding? binding,
                out error),
            error);

        using var handler = new NotFoundPackageHandler();
        using var client = new HttpClient(handler);
        PackageExtractionOutcome outcome =
            await DesktopPackageExtractor.ExtractPackageAsync(
                client,
                binding!.PackageTarget!,
                sourceOptions: handler.SourceOptions);

        Assert.False(outcome.IsSuccess);
        Assert.Contains("cannot be empty", outcome.ErrorMessage);
        Assert.DoesNotContain(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                "/contoso/index.json",
                StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("library", "--package")]
    [InlineData("coordinate", "--platform")]
    public async Task CommandAdaptersSurfaceMalformedDeclarations(
        string route,
        string option)
    {
        string[] arguments = route == "library"
            ? ["library", option, "\0", "-D", "--schema"]
            :
            [
                "library",
                "coordinate",
                "0x06000001+0x0",
                option,
                "\0",
            ];

        var root = CommandLineBuilder.CreateRootCommand();
        string[] processed =
            CommandLineBuilder.PreprocessArgs(arguments, root);
        var (exit, output, error) =
            await ConsoleCapture.RunAsync(
                () => CommandLineBuilder.InvokeAsync(
                    root.Parse(processed),
                    processed));

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"Invalid value '", error);
        Assert.Contains($"for {option}.", error);
    }

    private static string CreatePackage(
        string root,
        string packageId,
        string version,
        string fileName)
    {
        string content = Path.Combine(
            root,
            $"{Guid.NewGuid():N}-content");
        string libraryDirectory = Path.Combine(
            content,
            "lib",
            "net11.0");
        Directory.CreateDirectory(libraryDirectory);
        File.Copy(
            typeof(LibrarySourceAdapterTests).Assembly.Location,
            Path.Combine(
                libraryDirectory,
                "DotnetInspect.Cli.Tests.dll"));
        File.WriteAllText(
            Path.Combine(content, $"{packageId}.nuspec"),
            $$"""
              <?xml version="1.0" encoding="utf-8"?>
              <package>
                <metadata>
                  <id>{{packageId}}</id>
                  <version>{{version}}</version>
                  <authors>dotnet-inspect tests</authors>
                  <description>Typed source intent fixture.</description>
                </metadata>
              </package>
              """);
        string archive = Path.Combine(root, fileName);
        ZipFile.CreateFromDirectory(content, archive);
        return archive;
    }

    private sealed class NotFoundPackageHandler : HttpMessageHandler
    {
        private const string Index =
            "https://reference.example/v3/index.json";
        private const string FlatContainer =
            "https://reference.example/v3-flatcontainer/";

        internal NuGetSourceOptions SourceOptions { get; } = new()
        {
            Sources = [Index],
        };

        internal List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (request.RequestUri!.AbsoluteUri == Index)
            {
                return Task.FromResult(
                    new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $$"""
                              {
                                "version": "3.0.0",
                                "resources": [
                                  {
                                    "@id": "{{FlatContainer}}",
                                    "@type": "PackageBaseAddress/3.0.0"
                                  }
                                ]
                              }
                              """),
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(
                    System.Net.HttpStatusCode.NotFound));
        }
    }
}
