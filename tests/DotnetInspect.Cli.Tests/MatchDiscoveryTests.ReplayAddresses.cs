using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using CSharpText;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Cli.Tests;

public partial class MatchDiscoveryTests
{
    [Fact]
    public void ResolveSelector_AmbiguousUnavailableForwardedTypeDoesNotInventATarget()
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "A.Target"));
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("B", "Widget"),
            "B.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "Widget.Member");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("A.Target", resolved.Error);
        Assert.DoesNotContain("B.Target", resolved.Error);
    }

    [Fact]
    public void ResolveSelector_ExactHealthyTypeDoesNotReportAFailedForwarderPrefix()
    {
        var api = new ApiSurface();
        api.Types.Add(new ApiType
        {
            Namespace = "A.Widget",
            Name = "Member",
        });
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "A.Widget.Member");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    [Fact]
    public void ResolveSelector_MalformedDoubleDotDoesNotReportAForwarderFailure()
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "A.Widget..Bogus");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    [Fact]
    public void ResolveSelector_TrailingDotDoesNotReportAForwarderFailure()
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "A.Widget.");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    [Theory]
    [InlineData("A.Widget...ctor")]
    [InlineData("A.Widget...cctor")]
    public void ResolveSelector_RepeatedDotConstructorDoesNotReportAForwarderFailure(
        string selector)
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", selector);

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    [Fact]
    public void ResolveSelector_ExplicitGenericArityDoesNotReportAnotherForwardedType()
    {
        var api = new ApiSurface();
        api.InspectionFailures.Add(ForwardingFailure(
            DefinitionName("A", "Widget`2"),
            "Failed.Target"));

        MatchCommand.ResolvedSelector resolved =
            MatchCommand.ResolveSelector(api, "/images/Facade.dll", "Widget<T>.Member");

        Assert.Contains("must name a Type.Member selector", resolved.Error);
        Assert.DoesNotContain("Forwarded type", resolved.Error);
        Assert.DoesNotContain("Failed.Target", resolved.Error);
    }

    /// <summary>
    /// The directly named library keeps its full path, which is already replayable. Only the
    /// package case may substitute a package-relative spelling.
    /// </summary>
    [Fact]
    public void Disclosure_ForADirectlyNamedLibrary_KeepsThePathAndNamesNoPackage()
    {
        var request = new MatchDiscoveryRequest(
            "A.Type.Member",
            "A.Type",
            "/images/Target.dll",
            new ILInspector.Analysis.StructuralCloneRetrievalLimits(1, 1));

        string disclosure = MatchDiscoveryFormatter.DisclosureFor(request);

        Assert.Contains("`--library '/images/Target.dll'`", disclosure);
        Assert.DoesNotContain("--package", disclosure);
    }

    /// <summary>
    /// The candidate image that came out of a package extraction is addressed by its exact
    /// package-relative asset and TFM, so another same-named assembly cannot win during replay.
    /// </summary>
    [Fact]
    public void ReplayableCandidateAddress_ForAnImageInsideTheExtraction_RetainsTheExactAsset()
    {
        string extraction = Path.Combine(Path.GetTempPath(), "inspect-api-xyz", "extracted");

        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "Fixture@1.0.0",
                extraction,
                Path.Combine(extraction, "lib", "net10.0", "Target.dll"));

        Assert.Equal("Fixture@1.0.0", address.Package);
        Assert.Equal("lib/net10.0/Target.dll", address.Library);
        Assert.Equal("net10.0", address.Tfm);
    }

    /// <summary>
    /// A directly named library outlives the command, so its path is disclosed unchanged. Nothing
    /// here may shorten an address the caller can still use.
    /// </summary>
    [Fact]
    public void ReplayableCandidateAddress_ForADirectlyNamedLibrary_KeepsThePathIntact()
    {
        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(null, null, "/images/Target.dll");

        Assert.Null(address.Package);
        Assert.Equal("/images/Target.dll", address.Library);
        Assert.Null(address.Tfm);
    }

    /// <summary>
    /// A package run whose candidate image lives outside the extraction is a path the caller
    /// supplied, so it survives the command and must not be rewritten to a bare file name that
    /// the package does not contain.
    /// </summary>
    [Fact]
    public void ReplayableCandidateAddress_ForAnImageOutsideTheExtraction_KeepsThePathIntact()
    {
        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "Fixture.1.0.0.nupkg",
                Path.Combine(Path.GetTempPath(), "inspect-api-xyz"),
                "/images/Target.dll");

        Assert.Null(address.Package);
        Assert.Equal("/images/Target.dll", address.Library);
        Assert.Null(address.Tfm);
    }

    [Fact]
    public void ReplayableCandidateAddress_RejectsAnExtractionSiblingWithTheSamePrefix()
    {
        string extraction = Path.Combine(Path.GetTempPath(), "inspect-api-xyz");
        string candidate = Path.Combine(
            Path.GetTempPath(),
            "inspect-api-xyz-other",
            "lib",
            "net10.0",
            "Target.dll");

        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "Fixture@1.0.0",
                extraction,
                candidate);

        Assert.Null(address.Package);
        Assert.Equal(candidate, address.Library);
        Assert.Null(address.Tfm);
    }

    [Fact]
    public void ReplayableCandidateAddress_ForAGlobalPackageDependency_UsesItsOwnCoordinate()
    {
        string candidate = Path.Combine(
            DotnetInspector.Packages.NuGetCache.GetNuGetCachePath(),
            "dependency.fixture",
            "2.3.4",
            "lib",
            "net10.0",
            "Dependency.dll");

        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "facade.fixture@1.0.0",
                Path.Combine(Path.GetTempPath(), "facade-fixture"),
                candidate,
                AssemblyResolutionProvenance.Package(
                    "dependency.fixture",
                    "2.3.4",
                    tfm: null,
                    rid: null));

        Assert.Equal("dependency.fixture@2.3.4", address.Package);
        Assert.Equal("lib/net10.0/Dependency.dll", address.Library);
        Assert.Equal("net10.0", address.Tfm);
    }

    [Fact]
    public void ReplayableCandidateAddress_ChecksTheDefaultRootAfterAnOverride()
    {
        string overrideRoot = Path.Combine(Path.GetTempPath(), "override-packages");
        string defaultRoot = Path.Combine(Path.GetTempPath(), "default-packages");
        string candidate = Path.Combine(
            defaultRoot,
            "dependency.fixture",
            "2.3.4",
            "lib",
            "net10.0",
            "Dependency.dll");

        MatchDiscovery.ReplayableCandidateAddress address =
            MatchDiscovery.GetReplayableCandidateAddress(
                "facade.fixture@1.0.0",
                Path.Combine(Path.GetTempPath(), "facade-fixture"),
                candidate,
                AssemblyResolutionProvenance.Package(
                    "dependency.fixture",
                    "2.3.4",
                    tfm: null,
                    rid: null),
                packageRoots: [overrideRoot, defaultRoot]);

        Assert.Equal("dependency.fixture@2.3.4", address.Package);
        Assert.Equal("lib/net10.0/Dependency.dll", address.Library);
        Assert.Equal("net10.0", address.Tfm);
    }

    [Fact]
    public void ReplayablePackage_ReplacesARangeWithTheResolvedExactVersion()
    {
        string? package = MatchDiscovery.GetReplayablePackage(
            "Fixture@1.0.0..2.0.0",
            "Fixture",
            "1.2.3");

        Assert.Equal("Fixture@1.2.3", package);
    }

    [Fact]
    public void ReplayablePackage_ReplacesAnUnversionedCoordinateWithTheResolvedExactVersion()
    {
        string? package = MatchDiscovery.GetReplayablePackage(
            "Fixture",
            "Fixture",
            "1.2.3");

        Assert.Equal("Fixture@1.2.3", package);
    }

    [Fact]
    public void Disclosure_ShellQuotesPackageAssetAndTfm()
    {
        var request = new MatchDiscoveryRequest(
            "A.Type.Member",
            "A.Type",
            "lib/net10.0/Target's build.dll",
            new ILInspector.Analysis.StructuralCloneRetrievalLimits(1, 1),
            CandidatePackage: "/packages/Fixture's build.nupkg",
            CandidateTfm: "net10.0");

        string disclosure = MatchDiscoveryFormatter.DisclosureFor(request);

        Assert.Contains(
            "--package "
                + ShellCommandText.Quote("/packages/Fixture's build.nupkg")
                + " --library "
                + ShellCommandText.Quote("lib/net10.0/Target's build.dll")
                + " --tfm "
                + ShellCommandText.Quote("net10.0"),
            disclosure);
        Assert.Contains(
            $"in {ShellCommandText.CurrentDialectName} on a candidate",
            disclosure);
    }

    [Fact]
    public void ShellCommandQuote_UsesTheDeclaredDialect()
    {
        const string value = "Target's build.dll";

        Assert.Equal(
            "'Target'\"'\"'s build.dll'",
            ShellCommandText.Quote(
                value,
                ShellCommandDialect.Posix));
        Assert.Equal(
            "'Target''s build.dll'",
            ShellCommandText.Quote(
                value,
                ShellCommandDialect.PowerShell));
    }

    [Fact]
    public void Disclosure_PackageReplayRetainsSourceSelection()
    {
        var request = new MatchDiscoveryRequest(
            "A.Type.Member",
            "A.Type",
            "lib/net10.0/Target.dll",
            new ILInspector.Analysis.StructuralCloneRetrievalLimits(1, 1),
            CandidatePackage: "Fixture@1.0.0",
            CandidateTfm: "net10.0",
            ReplaySources: new PackageReplaySources(
                ["https://feed-a.invalid/v3/index.json"],
                ["https://feed-b.invalid/v3/index.json"],
                "/configs/NuGet Config",
                null));

        string disclosure = MatchDiscoveryFormatter.DisclosureFor(request);

        Assert.Contains(
            "--source 'https://feed-a.invalid/v3/index.json' "
                + "--add-source 'https://feed-b.invalid/v3/index.json' "
                + "--nugetconfig '/configs/NuGet Config'",
            disclosure);
    }

    [Fact]
    public void ReplaySources_RejectAValueThatDiagnosticsWouldRedact()
    {
        const string secret = "do-not-print";

        bool accepted = MatchDiscovery.TryGetReplaySources(
            new NuGetSourceOptions
            {
                Sources = [$"https://feed.invalid/v3/index.json?token={secret}"],
            },
            out PackageReplaySources? replaySources,
            out string? error);

        Assert.False(accepted);
        Assert.Null(replaySources);
        Assert.Contains("--source", error);
        Assert.Contains("--nugetconfig", error);
        Assert.DoesNotContain(secret, error);
    }

    [Fact]
    public void ReplaySources_SelectedProducerRedactionDoesNotRecommendTheConfigAlreadyInUse()
    {
        const string secret = "do-not-print";

        bool accepted = MatchDiscovery.TryGetReplaySources(
            new NuGetSourceOptions
            {
                Sources = [$"https://feed.invalid/v3/index.json?token={secret}"],
                ConfigFile = "nuget.config",
            },
            out PackageReplaySources? replaySources,
            out string? error,
            selectedVersionSourceRestriction: true);

        Assert.False(accepted);
        Assert.Null(replaySources);
        Assert.Contains("package source mapping", error);
        Assert.DoesNotContain("Configure that source", error);
        Assert.DoesNotContain(secret, error);
    }

    [Theory]
    [InlineData("http://localhost:5000")]
    [InlineData("https://feed.test:443/v3/index.json")]
    [InlineData("https://MyFeed.Test/v3/index.json")]
    [InlineData("HTTPS://feed.test/v3/index.json")]
    [InlineData("https://feed.test/my%20feed/index.json")]
    public void ReplaySources_AcceptHarmlessUrlNormalization(string source)
    {
        bool accepted = MatchDiscovery.TryGetReplaySources(
            new NuGetSourceOptions { Sources = [source] },
            out PackageReplaySources? replaySources,
            out string? error);

        Assert.True(accepted, error);
        Assert.Equal(source, Assert.Single(replaySources!.Sources));
    }

    [Theory]
    [InlineData("--source")]
    [InlineData("--add-source")]
    [InlineData("--nugetconfig")]
    [InlineData("--nugetconfig-directory")]
    public async Task Similar_ReplaySourceContainingMarkdownDelimiter_IsRefused(
        string option)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-replay-source-delimiter-{Guid.NewGuid():N}");
        string value = Path.Combine(root, "value`with-delimiter");
        Directory.CreateDirectory(root);
        if (option == "--nugetconfig-directory")
            Directory.CreateDirectory(value);

        try
        {
            string package = CreatePackageArchive(
                root,
                "package",
                "Match.Replay.SourceDelimiter",
                "1.0.0",
                "lib/net10.0/Target.dll",
                File.ReadAllBytes(TestAssembly));
            NuGetSourceOptions sourceOptions = option switch
            {
                "--source" => new() { Sources = [value] },
                "--add-source" => new() { AdditionalSources = [value] },
                "--nugetconfig" => new() { ConfigFile = value },
                "--nugetconfig-directory" => new() { ConfigDirectory = value },
                _ => throw new InvalidOperationException(),
            };
            var options = new MatchOptions
            {
                LeftSelector = SampleSeed,
                PackagePath = package,
                AssemblyPath = "lib/net10.0/Target.dll",
                IncludeAll = true,
                Similar = true,
                JsonOutput = true,
                SourceOptions = sourceOptions,
            };

            var (exit, output, error) = await RunAsync(options);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(option, error);
            Assert.Contains("cannot be emitted losslessly", error);
            Assert.DoesNotContain(value, error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReplaySources_MakesTheConfigPathIndependentOfTheNextWorkingDirectory()
    {
        string relative = Path.Combine("config", "NuGet.Config");

        bool accepted = MatchDiscovery.TryGetReplaySources(
            new NuGetSourceOptions { ConfigFile = relative },
            out PackageReplaySources? replaySources,
            out string? error);

        Assert.True(accepted, error);
        Assert.Equal(Path.GetFullPath(relative), replaySources!.ConfigFile);
    }

    [Fact]
    public void ReplaySources_MakesRelativeLocalSourcesIndependentOfTheNextWorkingDirectory()
    {
        string workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "match-relative-source",
            "working");

        bool accepted = MatchDiscovery.TryGetReplaySources(
            new NuGetSourceOptions
            {
                Sources = [Path.Combine(".", "feed")],
                AdditionalSources = [Path.Combine("..", "secondary")],
            },
            out PackageReplaySources? replaySources,
            out string? error,
            workingDirectory: workingDirectory);

        Assert.True(accepted, error);
        Assert.Equal(
            Path.GetFullPath("feed", workingDirectory),
            Assert.Single(replaySources!.Sources));
        Assert.Equal(
            Path.GetFullPath(
                Path.Combine("..", "secondary"),
                workingDirectory),
            Assert.Single(replaySources.AdditionalSources));
    }

    [Fact]
    public void ReplaySources_SharedFormatterPreservesConfiguredSpellingsAndQuotes()
    {
        string workingDirectory = Path.Combine(Path.GetTempPath(), "replay's working directory");
        const string source = "https://MyFeed.Test:443/my%20feed/index.json";
        bool accepted = PackageReplaySourceArguments.TryCreate(
            new NuGetSourceOptions
            {
                Sources = [source],
                AdditionalSources = [Path.Combine(".", "feed's folder")],
                ConfigDirectory = Path.Combine(".", "config"),
            },
            "diff --history",
            out PackageReplaySources? replaySources,
            out string? error,
            workingDirectory: workingDirectory);

        Assert.True(accepted, error);
        Assert.Equal(
            $"--source {ShellCommandText.Quote(source)} "
                + $"--add-source {ShellCommandText.Quote(Path.Combine(workingDirectory, "feed's folder"))} "
                + $"--nugetconfig-directory {ShellCommandText.Quote(Path.Combine(workingDirectory, "config"))}",
            PackageReplaySourceArguments.Format(replaySources));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplaySources_SharedErrorsNameTheCallingCommand(bool restricted)
    {
        bool accepted = PackageReplaySourceArguments.TryCreate(
            new NuGetSourceOptions { Sources = ["https://feed.invalid/index.json?token=secret"] },
            "diff --history",
            out PackageReplaySources? replaySources,
            out string? error,
            selectedVersionSourceRestriction: restricted);

        Assert.False(accepted);
        Assert.Null(replaySources);
        Assert.StartsWith("diff --history cannot disclose", error);
        Assert.DoesNotContain("secret", error);
        Assert.Contains(restricted ? "package source mapping" : "--nugetconfig", error);
    }

    [Fact]
    public void ReplaySources_RestrictionRetainsConfigButNotNonReportingSources()
    {
        var original = new NuGetSourceOptions
        {
            Sources = ["https://non-reporter.invalid/index.json"],
            AdditionalSources = ["https://other.invalid/index.json"],
            ConfigDirectory = Path.GetTempPath(),
        };
        const string reporter = "https://reporter.invalid/index.json";

        NuGetSourceOptions? restricted = PackageReplaySourceArguments.RestrictToReportingSources(
            original, [reporter]);

        Assert.Equal(reporter, Assert.Single(restricted!.Sources));
        Assert.Empty(restricted.AdditionalSources);
        Assert.Equal(original.ConfigDirectory, restricted.ConfigDirectory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplaySources_InvalidConfigPathReturnsAnActionableError(bool directory)
    {
        var options = directory
            ? new NuGetSourceOptions { ConfigDirectory = "invalid\0path" }
            : new NuGetSourceOptions { ConfigFile = "invalid\0path" };

        Assert.False(PackageReplaySourceArguments.TryCreate(
            options, "diff --history", out PackageReplaySources? replaySources, out string? error));
        Assert.Null(replaySources);
        Assert.StartsWith("diff --history cannot disclose", error);
        Assert.Contains(directory ? "--nugetconfig-directory" : "--nugetconfig", error);
        Assert.DoesNotContain('\0', error!);
    }

    [Fact]
    public void ReplayConfigDirectory_DoesNotRebaseAnExplicitRelativeSource()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"match-config-directory-source-base-{Guid.NewGuid():N}");
        string configDirectory = Path.Combine(root, "config");
        string commandDirectory = Path.Combine(root, "command");
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(commandDirectory);

        try
        {
            IReadOnlyList<NuGetFetch.PackageSource> sources =
                NuGetSourceResolver.ResolveSources(
                    new NuGetSourceOptions
                    {
                        Sources = [Path.Combine(".", "feed")],
                        ConfigDirectory = configDirectory,
                    },
                    commandDirectory);

            Assert.Equal(
                NuGetFetch.LocalPackageSourceIdentity.Create(
                    Path.Combine(".", "feed"),
                    commandDirectory).CanonicalPath,
                Assert.Single(sources).Url);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReplayConfigDirectory_RejectsARelativeSourceResolutionBase()
    {
        Assert.Throws<ArgumentException>(
            () => NuGetSourceResolver.ResolveSources(
                new NuGetSourceOptions
                {
                    ConfigDirectory = Path.Combine(".", "config"),
                }));
    }

    [Fact]
    public void PhysicalImageLoad_ClearsPackageRangeCoordinates()
    {
        var options = new MatchOptions
        {
            PackagePath = "Fixture@1.0.0..2.0.0",
            PackageRangeAddress = "#3",
        };

        MatchOptions physical = MatchDiscovery.ForPhysicalImageLoad(options);

        Assert.Null(physical.PackagePath);
        Assert.Null(physical.PackageRangeAddress);
    }

}
