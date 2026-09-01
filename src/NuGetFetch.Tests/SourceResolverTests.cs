using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Tests for SourceResolver nuget.config parsing, credential extraction,
/// and source resolution. Ported from dotnet-inspect.
/// </summary>
public class SourceResolverTests : IDisposable
{
    private readonly string _tempDir;

    public SourceResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nf-src-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);

    [Fact]
    public void ResolveSources_NoArgs_ReturnsNuGetOrg()
    {
        // Isolated temp dir avoids picking up repo nuget.config
        var sources = SourceResolver.ResolveSources(workingDirectory: _tempDir);

        Assert.NotEmpty(sources);
        Assert.Contains(sources, s => s.IsNuGetOrg);
    }

    [Fact]
    public void MergeConfigFiles_NoConfig_PreservesDefault()
    {
        var sources = SourceResolver.MergeConfigFiles([], PackageSources.Default);

        Assert.True(Assert.Single(sources).IsNuGetOrg);
    }

    [Fact]
    public void MergeConfigFiles_AddWithoutClear_PrecedesDefault()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="PrivateFeed" value="https://private.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.MergeConfigFiles([configPath], PackageSources.Default);

        Assert.Equal(["PrivateFeed", "nuget.org"], sources.Select(source => source.Name));
    }

    [Fact]
    public void MergeConfigFiles_RedeclaredDefault_KeepsDeclarationOrder()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="PrivateFeed" value="https://private.example.com/v3/index.json" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.MergeConfigFiles([configPath], PackageSources.Default);

        Assert.Equal(["PrivateFeed", "nuget.org"], sources.Select(source => source.Name));
    }

    [Fact]
    public void MergeConfigFiles_CaseVariantName_OverridesDefault()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="NuGet.Org" value="https://mirror.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.MergeConfigFiles([configPath], PackageSources.Default);

        PackageSource source = Assert.Single(sources);
        Assert.Equal("NuGet.Org", source.Name);
        Assert.Equal("https://mirror.example.com/v3/index.json", source.Url);
    }

    [Fact]
    public void MergeConfigFiles_CaseVariantName_DisablesDefault()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="PrivateFeed" value="https://private.example.com/v3/index.json" />
              </packageSources>
              <disabledPackageSources>
                <add key="NuGet.Org" value="true" />
              </disabledPackageSources>
            </configuration>
            """);

        var sources = SourceResolver.MergeConfigFiles([configPath], PackageSources.Default);

        PackageSource source = Assert.Single(sources);
        Assert.Equal("PrivateFeed", source.Name);
    }

    [Fact]
    public void MergeConfigFiles_NearerCaseVariant_OverridesSource()
    {
        var parentConfig = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="PrivateFeed" value="https://parent.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        var childConfig = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="privatefeed" value="https://child.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.MergeConfigFiles(
            [childConfig, parentConfig],
            PackageSources.Empty);

        PackageSource source = Assert.Single(sources);
        Assert.Equal("https://child.example.com/v3/index.json", source.Url);
    }

    [Fact]
    public void MergeConfigFiles_CaseVariantCredentialName_MatchesSource()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="PrivateFeed" value="https://private.example.com/v3/index.json" />
              </packageSources>
              <packageSourceCredentials>
                <privatefeed>
                  <add key="Username" value="user" />
                  <add key="ClearTextPassword" value="password" />
                </privatefeed>
              </packageSourceCredentials>
            </configuration>
            """);

        var sources = SourceResolver.MergeConfigFiles([configPath], PackageSources.Empty);

        PackageSourceCredential credential =
            Assert.IsType<PackageSourceCredential>(Assert.Single(sources).Credential);
        Assert.Equal("user", credential.Username);
        Assert.Equal("password", credential.Password);
    }

    [Fact]
    public void MergeConfigFiles_NearerFalse_ReenablesDefault()
    {
        var parentConfig = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <disabledPackageSources>
                <add key="nuget.org" value="true" />
              </disabledPackageSources>
            </configuration>
            """);
        var childConfig = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <disabledPackageSources>
                <add key="NuGet.Org" value="false" />
              </disabledPackageSources>
            </configuration>
            """);

        var sources = SourceResolver.MergeConfigFiles(
            [childConfig, parentConfig],
            PackageSources.Default);

        Assert.True(Assert.Single(sources).IsNuGetOrg);
    }

    [Fact]
    public void ResolveSources_ExplicitSource_ReplacesDefaults()
    {
        var sources = SourceResolver.ResolveSources(
            explicitSource: "https://my-feed.example.com/v3/index.json");

        Assert.Single(sources);
        Assert.Equal("https://my-feed.example.com/v3/index.json", sources[0].Url);
    }

    [Fact]
    public void ResolveSources_AdditionalSources_AreCombined()
    {
        var sources = SourceResolver.ResolveSources(
            additionalSources: ["https://extra.example.com/v3/index.json"],
            workingDirectory: _tempDir);

        Assert.Contains(sources, s => s.Url == "https://extra.example.com/v3/index.json");
        // Should also include nuget.org (or whatever the default config provides)
        Assert.True(sources.Count >= 2);
    }

    [Fact]
    public void ResolveSources_WithConfigFile_ParsesSources()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="MyFeed" value="https://my-feed.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(configPath: configPath);

        Assert.Single(sources);
        Assert.Equal("MyFeed", sources[0].Name);
        Assert.Equal("https://my-feed.example.com/v3/index.json", sources[0].Url);
    }

    [Fact]
    public void ResolveSources_ExplicitConfigWithClear_RemainsEmpty()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(configPath: configPath);

        Assert.Empty(sources);
    }

    [Fact]
    public void ResolveSources_ConfigWithClear_ClearsPreviousSources()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="OnlyThis" value="https://only-this.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(configPath: configPath);

        Assert.Single(sources);
        Assert.Equal("OnlyThis", sources[0].Name);
    }

    [Fact]
    public void ResolveSources_DisabledSource_IsExcluded()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="EnabledFeed" value="https://enabled.example.com/v3/index.json" />
                <add key="DisabledFeed" value="https://disabled.example.com/v3/index.json" />
              </packageSources>
              <disabledPackageSources>
                <add key="DisabledFeed" value="true" />
              </disabledPackageSources>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(configPath: configPath);

        Assert.Single(sources);
        Assert.Equal("EnabledFeed", sources[0].Name);
    }

    [Fact]
    public void ResolveSources_ConfigAndAdditionalSources_Combined()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="ConfigFeed" value="https://config.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(
            configPath: configPath,
            additionalSources: ["https://extra.example.com/v3/index.json"]);

        Assert.Equal(2, sources.Count);
        Assert.Equal("ConfigFeed", sources[0].Name);
        Assert.Contains("extra.example.com", sources[1].Url);
    }

    [Fact]
    public void ResolveSources_ConfigRelativePathsUseEachDeclaringDirectory()
    {
        string parentDirectory = Path.Combine(_tempDir, "parent");
        string childDirectory = Path.Combine(parentDirectory, "child");
        Directory.CreateDirectory(childDirectory);
        string parentConfig = WriteConfigAt(
            parentDirectory,
            """
            <configuration>
              <packageSources>
                <add key="parent" value="feed" />
              </packageSources>
            </configuration>
            """);
        string childConfig = WriteConfigAt(
            childDirectory,
            """
            <configuration>
              <packageSources>
                <add key="child" value="feed" />
              </packageSources>
            </configuration>
            """);

        IReadOnlyList<PackageSource> sources = SourceResolver.MergeConfigFiles(
            [childConfig, parentConfig],
            PackageSources.Empty);

        Assert.Equal(
            [
                Path.Combine(parentDirectory, "feed"),
                Path.Combine(childDirectory, "feed"),
            ],
            sources.Select(source => source.Url));
    }

    [Fact]
    public void ResolveSources_CommandRelativePathUsesWorkingDirectory()
    {
        string workingDirectory = Path.Combine(_tempDir, "working");
        Directory.CreateDirectory(workingDirectory);

        PackageSource source = Assert.Single(
            SourceResolver.ResolveSources(
                explicitSource: Path.Combine("feeds", "."),
                workingDirectory: workingDirectory));

        Assert.Equal(
            Path.Combine(workingDirectory, "feeds"),
            source.Url);
    }

    [Fact]
    public void ResolveSources_ConfigPathAndFileUriShareCanonicalSpelling()
    {
        string feed = Path.Combine(_tempDir, "feed");
        string configPath = WriteConfig($"""
            <configuration>
              <packageSources>
                <add key="path" value="{feed}" />
                <add key="uri" value="{new Uri(feed).AbsoluteUri}" />
              </packageSources>
            </configuration>
            """);

        IReadOnlyList<PackageSource> sources =
            SourceResolver.ResolveSources(configPath: configPath);

        Assert.Equal(2, sources.Count);
        Assert.All(sources, source => Assert.Equal(feed, source.Url));
    }

    [Fact]
    public void ResolveSources_ConfigExpandsPercentEnvironmentVariables()
    {
        string variableName = $"DOTNET_INSPECT_FEED_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variableName, _tempDir);
        try
        {
            string configPath = WriteConfig($"""
                <configuration>
                  <packageSources>
                    <add key="local" value="%{variableName}%/feed" />
                  </packageSources>
                </configuration>
                """);

            PackageSource source = Assert.Single(
                SourceResolver.ResolveSources(configPath: configPath));

            Assert.Equal(Path.Combine(_tempDir, "feed"), source.Url);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void ResolveSources_UnsupportedSchemeFailsBeforeClientCreation()
    {
        string configPath = WriteConfig("""
            <configuration>
              <packageSources>
                <add key="ftp" value="ftp://feed.example/packages" />
              </packageSources>
            </configuration>
            """);

        Assert.Throws<UnsupportedSourceException>(
            () => SourceResolver.ResolveSources(configPath: configPath));
    }

    [Fact]
    public void ResolveSources_MalformedFileUriFailsBeforeClientCreation()
    {
        const string Source = "file://user@server/share";

        Assert.False(SourceResolver.IsSupportedSource(Source));
        Assert.Throws<UnsupportedSourceException>(
            () => SourceResolver.ResolveSources(explicitSource: Source));
    }

    [Fact]
    public void ResolveSources_NonExistentExplicitConfig_DoesNotUseDefaults()
    {
        var sources = SourceResolver.ResolveSources(
            configPath: "/nonexistent/path/nuget.config");

        Assert.Empty(sources);
    }

    [Fact]
    public void ResolveSources_ClearTextCredentials_ParsesCredentials()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="MyPrivateFeed" value="https://private.example.com/v3/index.json" />
              </packageSources>
              <packageSourceCredentials>
                <MyPrivateFeed>
                  <add key="Username" value="myuser" />
                  <add key="ClearTextPassword" value="mypassword" />
                </MyPrivateFeed>
              </packageSourceCredentials>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(configPath: configPath);

        Assert.Single(sources);
        Assert.NotNull(sources[0].Credential);
        Assert.Equal("myuser", sources[0].Credential!.Username);
        Assert.Equal("mypassword", sources[0].Credential!.Password);
    }

    [Fact]
    public void ResolveSources_EncodedSourceName_ParsesCredentials()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="My Private Feed" value="https://private.example.com/v3/index.json" />
              </packageSources>
              <packageSourceCredentials>
                <My_x0020_Private_x0020_Feed>
                  <add key="Username" value="spaceuser" />
                  <add key="ClearTextPassword" value="spacepass" />
                </My_x0020_Private_x0020_Feed>
              </packageSourceCredentials>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(configPath: configPath);

        Assert.Single(sources);
        Assert.NotNull(sources[0].Credential);
        Assert.Equal("spaceuser", sources[0].Credential!.Username);
        Assert.Equal("spacepass", sources[0].Credential!.Password);
    }

    [Fact]
    public void ResolveSources_NoCredentials_LeavesCredentialNull()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="PublicFeed" value="https://public.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(configPath: configPath);

        Assert.Single(sources);
        Assert.Null(sources[0].Credential);
    }

    [Fact]
    public void LoadSourcesFromConfig_ValidConfig_ReturnsSources()
    {
        var configPath = WriteConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="Feed1" value="https://feed1.example.com/v3/index.json" />
                <add key="Feed2" value="https://feed2.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.LoadSourcesFromConfig(configPath);

        Assert.Equal(2, sources.Count);
        Assert.Equal("Feed1", sources[0].Name);
        Assert.Equal("Feed2", sources[1].Name);
    }

    [Fact]
    public void FindConfigFiles_WalksDirectoryHierarchy()
    {
        var rootDir = Path.Combine(_tempDir, "root");
        var subDir = Path.Combine(rootDir, "sub", "folder");
        Directory.CreateDirectory(subDir);

        // Create nuget.config in root
        File.WriteAllText(Path.Combine(rootDir, "nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="RootFeed" value="https://root.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var files = SourceResolver.FindConfigFiles(subDir);

        // Should find the config by walking up from sub/folder to root
        Assert.Contains(files, f => f.Contains("root") && f.EndsWith("config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveSources_HierarchyWalk_FindsParentConfig()
    {
        var rootDir = Path.Combine(_tempDir, "hier");
        var subDir = Path.Combine(rootDir, "sub", "folder");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(rootDir, "nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="RootFeed" value="https://root.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        // FindConfigFiles from subdirectory should walk up and find root config
        var configFiles = SourceResolver.FindConfigFiles(subDir);
        Assert.Contains(configFiles, f => f.Contains("hier", StringComparison.OrdinalIgnoreCase) && f.EndsWith("config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveSources_ClearInNearestConfig_ClearsParentSources()
    {
        // Simulate: parent config has Feed1, child config has <clear/> + Feed2
        var rootDir = Path.Combine(_tempDir, "cleartest");
        var subDir = Path.Combine(rootDir, "sub");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(rootDir, "NuGet.Config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="ParentFeed" value="https://parent.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        File.WriteAllText(Path.Combine(subDir, "NuGet.Config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="ChildFeed" value="https://child.example.com/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(workingDirectory: subDir);

        PackageSource source = Assert.Single(sources);
        Assert.Equal("ChildFeed", source.Name);
    }

    [Fact]
    public void ResolveSources_ClearInAmbientConfig_RemovesDefault()
    {
        var workingDirectory = Path.Combine(_tempDir, "ambient-clear");
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(workingDirectory, "NuGet.Config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(workingDirectory: workingDirectory);

        Assert.Empty(sources);
    }

    [Fact]
    public void ResolveSources_AddSourceAfterAmbientClear_UsesOnlyAddedSource()
    {
        var workingDirectory = Path.Combine(_tempDir, "ambient-clear-add");
        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(Path.Combine(workingDirectory, "NuGet.Config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
              </packageSources>
            </configuration>
            """);

        var sources = SourceResolver.ResolveSources(
            additionalSources: ["https://additional.example.com/v3/index.json"],
            workingDirectory: workingDirectory);

        PackageSource source = Assert.Single(sources);
        Assert.Equal("https://additional.example.com/v3/index.json", source.Url);
    }

    [Fact]
    public void ResolvePackageSourceMapping_Absent_IsDisabled()
    {
        PackageSourceMapping mapping = SourceResolver.ResolvePackageSourceMapping(
            configPath: WriteConfig("""
                <configuration>
                  <packageSources>
                    <add key="private" value="https://private.example/v3/index.json" />
                  </packageSources>
                </configuration>
                """));

        Assert.False(mapping.IsEnabled);
        Assert.Empty(mapping.GetConfiguredPackageSources("Example.Package"));
    }

    [Fact]
    public void ResolvePackageSourceMapping_UsesExactThenLongestPrefixThenDefault()
    {
        PackageSourceMapping mapping = SourceResolver.ResolvePackageSourceMapping(
            configPath: WriteConfig("""
                <configuration>
                  <packageSourceMapping>
                    <packageSource key="default">
                      <package pattern="*" />
                    </packageSource>
                    <packageSource key="family">
                      <package pattern="Contoso.*" />
                    </packageSource>
                    <packageSource key="specific">
                      <package pattern="Contoso.Tools.*" />
                    </packageSource>
                    <packageSource key="exact">
                      <package pattern="Contoso.Tools.Build" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """));

        Assert.Equal(
            ["exact"],
            mapping.GetConfiguredPackageSources("contoso.tools.build"));
        Assert.Equal(
            ["specific"],
            mapping.GetConfiguredPackageSources("CONTOSO.Tools.Compiler"));
        Assert.Equal(
            ["family"],
            mapping.GetConfiguredPackageSources("Contoso.Core"));
        Assert.Equal(
            ["default"],
            mapping.GetConfiguredPackageSources("Other.Package"));
    }

    [Fact]
    public void ResolvePackageSourceMapping_ReturnsEverySourceWithWinningPattern()
    {
        PackageSourceMapping mapping = SourceResolver.ResolvePackageSourceMapping(
            configPath: WriteConfig("""
                <configuration>
                  <packageSourceMapping>
                    <packageSource key="primary">
                      <package pattern="Contoso.*" />
                    </packageSource>
                    <packageSource key="mirror">
                      <package pattern="contoso.*" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """));

        Assert.Equal(
            ["primary", "mirror"],
            mapping.GetConfiguredPackageSources("Contoso.Core"));
    }

    [Fact]
    public void MergePackageSourceMappings_NearestSourceKeyReplacesPatternListCaseInsensitively()
    {
        string parent = WriteConfig("""
            <configuration>
              <packageSourceMapping>
                <packageSource key="Private">
                  <package pattern="Parent.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        string child = WriteConfig("""
            <configuration>
              <packageSourceMapping>
                <packageSource key="private">
                  <package pattern="Child.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        PackageSourceMapping mapping =
            SourceResolver.MergePackageSourceMappings([child, parent]);

        Assert.Empty(mapping.GetConfiguredPackageSources("Parent.Package"));
        Assert.Equal(
            ["private"],
            mapping.GetConfiguredPackageSources("Child.Package"));
    }

    [Fact]
    public void MergePackageSourceMappings_ClearRemovesInheritedMappings()
    {
        string parent = WriteConfig("""
            <configuration>
              <packageSourceMapping>
                <packageSource key="parent">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        string child = WriteConfig("""
            <configuration>
              <packageSourceMapping>
                <clear />
                <packageSource key="child">
                  <package pattern="Child.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        PackageSourceMapping mapping =
            SourceResolver.MergePackageSourceMappings([child, parent]);

        Assert.Empty(mapping.GetConfiguredPackageSources("Other.Package"));
        Assert.Equal(
            ["child"],
            mapping.GetConfiguredPackageSources("Child.Package"));
    }

    [Fact]
    public void ResolvePackageSourceMapping_SourceWithoutPatternsFails()
    {
        string config = WriteConfig("""
            <configuration>
              <packageSourceMapping>
                <packageSource key="private" />
              </packageSourceMapping>
            </configuration>
            """);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => SourceResolver.ResolvePackageSourceMapping(configPath: config));

        Assert.Contains("must contain at least one package pattern", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Contoso.*.Tools")]
    [InlineData("Contoso**")]
    public void ResolvePackageSourceMapping_InvalidPatternFails(string pattern)
    {
        string config = WriteConfig($"""
            <configuration>
              <packageSourceMapping>
                <packageSource key="private">
                  <package pattern="{pattern}" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        Assert.Throws<InvalidDataException>(
            () => SourceResolver.ResolvePackageSourceMapping(configPath: config));
    }

    private string WriteConfig(string xml)
    {
        var path = Path.Combine(_tempDir, $"nuget-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, xml);
        return path;
    }

    private static string WriteConfigAt(string directory, string xml)
    {
        string path = Path.Combine(directory, "NuGet.Config");
        File.WriteAllText(path, xml);
        return path;
    }
}
