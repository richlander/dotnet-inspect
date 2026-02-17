using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for NuGetSourceResolver and NuGet source configuration.
/// </summary>
public class NuGetSourceResolverTests
{
    [Fact]
    public void ResolveSources_WithNoOptions_ReturnsNuGetOrg()
    {
        var sources = NuGetSourceResolver.ResolveSources(null);

        Assert.Single(sources);
        Assert.Equal("nuget.org", sources[0].Name);
        Assert.Contains("api.nuget.org", sources[0].Url);
    }

    [Fact]
    public void ResolveSources_WithDefaultOptions_ReturnsNuGetOrg()
    {
        var sources = NuGetSourceResolver.ResolveSources(NuGetSourceOptions.Default);

        Assert.Single(sources);
        Assert.Equal("nuget.org", sources[0].Name);
    }

    [Fact]
    public void ResolveSources_WithExplicitSource_ReplacesDefaults()
    {
        var options = new NuGetSourceOptions
        {
            Sources = ["https://my-feed.example.com/v3/index.json"]
        };

        var sources = NuGetSourceResolver.ResolveSources(options);

        Assert.Single(sources);
        Assert.Equal("source1", sources[0].Name);
        Assert.Equal("https://my-feed.example.com/v3/index.json", sources[0].Url);
    }

    [Fact]
    public void ResolveSources_WithMultipleSources_ReturnsAll()
    {
        var options = new NuGetSourceOptions
        {
            Sources = [
                "https://feed1.example.com/v3/index.json",
                "https://feed2.example.com/v3/index.json"
            ]
        };

        var sources = NuGetSourceResolver.ResolveSources(options);

        Assert.Equal(2, sources.Count);
        Assert.Equal("source1", sources[0].Name);
        Assert.Equal("source2", sources[1].Name);
    }

    [Fact]
    public void ResolveSources_WithAddSource_AddsToDefaults()
    {
        var options = new NuGetSourceOptions
        {
            AdditionalSources = ["https://extra-feed.example.com/v3/index.json"]
        };

        var sources = NuGetSourceResolver.ResolveSources(options);

        Assert.Equal(2, sources.Count);
        Assert.Equal("nuget.org", sources[0].Name);
        Assert.Contains("extra-feed.example.com", sources[1].Url);
    }

    [Fact]
    public void ResolveSources_WithSourceAndAddSource_CombinesThem()
    {
        var options = new NuGetSourceOptions
        {
            Sources = ["https://primary.example.com/v3/index.json"],
            AdditionalSources = ["https://extra.example.com/v3/index.json"]
        };

        var sources = NuGetSourceResolver.ResolveSources(options);

        Assert.Equal(2, sources.Count);
        Assert.Contains("primary.example.com", sources[0].Url);
        Assert.Contains("extra.example.com", sources[1].Url);
    }

    [Fact]
    public void ResolveSources_WithConfigFile_ParsesSources()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "nuget.config");
            File.WriteAllText(configPath, """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <add key="MyFeed" value="https://my-feed.example.com/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            var options = new NuGetSourceOptions
            {
                ConfigFile = configPath
            };

            var sources = NuGetSourceResolver.ResolveSources(options);

            Assert.Single(sources);
            Assert.Equal("MyFeed", sources[0].Name);
            Assert.Equal("https://my-feed.example.com/v3/index.json", sources[0].Url);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSources_WithConfigFileClear_ClearsPreviousSources()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "nuget.config");
            File.WriteAllText(configPath, """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="OnlyThis" value="https://only-this.example.com/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            var options = new NuGetSourceOptions
            {
                ConfigFile = configPath
            };

            var sources = NuGetSourceResolver.ResolveSources(options);

            Assert.Single(sources);
            Assert.Equal("OnlyThis", sources[0].Name);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSources_WithDisabledSource_ExcludesIt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "nuget.config");
            File.WriteAllText(configPath, """
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

            var options = new NuGetSourceOptions
            {
                ConfigFile = configPath
            };

            var sources = NuGetSourceResolver.ResolveSources(options);

            Assert.Single(sources);
            Assert.Equal("EnabledFeed", sources[0].Name);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSources_WithConfigFileAndAddSource_CombinesThem()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "nuget.config");
            File.WriteAllText(configPath, """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <add key="ConfigFeed" value="https://config.example.com/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            var options = new NuGetSourceOptions
            {
                ConfigFile = configPath,
                AdditionalSources = ["https://extra.example.com/v3/index.json"]
            };

            var sources = NuGetSourceResolver.ResolveSources(options);

            Assert.Equal(2, sources.Count);
            Assert.Equal("ConfigFeed", sources[0].Name);
            Assert.Contains("extra.example.com", sources[1].Url);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSources_WithNonExistentConfigFile_FallsBackToDefaults()
    {
        var options = new NuGetSourceOptions
        {
            ConfigFile = "/nonexistent/path/nuget.config"
        };

        var sources = NuGetSourceResolver.ResolveSources(options);

        // Falls back to nuget.org when config file doesn't exist
        Assert.Single(sources);
        Assert.Equal("nuget.org", sources[0].Name);
    }

    [Fact]
    public void NuGetSourceOptions_HasCustomConfiguration_DetectsOptions()
    {
        Assert.False(NuGetSourceOptions.Default.HasCustomConfiguration);
        Assert.False(new NuGetSourceOptions().HasCustomConfiguration);

        Assert.True(new NuGetSourceOptions { Sources = ["x"] }.HasCustomConfiguration);
        Assert.True(new NuGetSourceOptions { AdditionalSources = ["x"] }.HasCustomConfiguration);
        Assert.True(new NuGetSourceOptions { ConfigFile = "x" }.HasCustomConfiguration);
    }

    [Fact]
    public void NuGetSource_GetFlatContainerUrl_ReturnsNuGetOrgUrl()
    {
        var nugetOrg = NuGetSource.NuGetOrg;

        var flatUrl = nugetOrg.GetFlatContainerUrl();

        Assert.NotNull(flatUrl);
        Assert.Contains("v3-flatcontainer", flatUrl);
    }

    [Fact]
    public void NuGetSource_GetFlatContainerUrl_ReturnsNullForOtherFeeds()
    {
        var customFeed = new NuGetSource("custom", "https://my-feed.example.com/v3/index.json");

        var flatUrl = customFeed.GetFlatContainerUrl();

        Assert.Null(flatUrl);
    }

    [Fact]
    public void ResolveSources_WalksDirectoryHierarchy()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"nuget-hierarchy-test-{Guid.NewGuid():N}");
        var subDir = Path.Combine(rootDir, "sub", "folder");
        Directory.CreateDirectory(subDir);

        try
        {
            // Create nuget.config in root
            var configPath = Path.Combine(rootDir, "nuget.config");
            File.WriteAllText(configPath, """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <add key="RootFeed" value="https://root.example.com/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            // Resolve from subdirectory (should walk up and find root config)
            var sources = NuGetSourceResolver.ResolveSources(null, subDir);

            // Should find RootFeed from the config we created
            // (may also include other sources from user/system configs)
            Assert.Contains(sources, s => s.Name == "RootFeed");
            Assert.Contains(sources, s => s.Url == "https://root.example.com/v3/index.json");
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSources_WithClearTextCredentials_ParsesCredentials()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-cred-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "nuget.config");
            File.WriteAllText(configPath, """
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

            var options = new NuGetSourceOptions { ConfigFile = configPath };
            var sources = NuGetSourceResolver.ResolveSources(options);

            Assert.Single(sources);
            Assert.NotNull(sources[0].Credentials);
            Assert.Equal("myuser", sources[0].Credentials!.Username);
            Assert.Equal("mypassword", sources[0].Credentials!.Password);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSources_WithEncodedSourceName_ParsesCredentials()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-cred-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "nuget.config");
            // Spaces in source names are encoded as _x0020_ in the XML element name
            File.WriteAllText(configPath, """
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

            var options = new NuGetSourceOptions { ConfigFile = configPath };
            var sources = NuGetSourceResolver.ResolveSources(options);

            Assert.Single(sources);
            Assert.NotNull(sources[0].Credentials);
            Assert.Equal("spaceuser", sources[0].Credentials!.Username);
            Assert.Equal("spacepass", sources[0].Credentials!.Password);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSources_WithNoCredentials_LeavesCredentialsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"nuget-cred-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "nuget.config");
            File.WriteAllText(configPath, """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <add key="PublicFeed" value="https://public.example.com/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            var options = new NuGetSourceOptions { ConfigFile = configPath };
            var sources = NuGetSourceResolver.ResolveSources(options);

            Assert.Single(sources);
            Assert.Null(sources[0].Credentials);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void NuGetSource_GetAuthHeader_ReturnsBasicAuth()
    {
        var source = new NuGetSource("test", "https://test.example.com/v3/index.json")
        {
            Credentials = new NuGetSourceCredential("user", "pass")
        };

        var header = source.GetAuthHeader();

        Assert.NotNull(header);
        Assert.Equal("Basic", header!.Scheme);
        // "user:pass" base64
        var decoded = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(header.Parameter!));
        Assert.Equal("user:pass", decoded);
    }

    [Fact]
    public void NuGetSource_GetAuthHeader_ReturnsNullWithoutCredentials()
    {
        var source = new NuGetSource("test", "https://test.example.com/v3/index.json");

        Assert.Null(source.GetAuthHeader());
    }
}
