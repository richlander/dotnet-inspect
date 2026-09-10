using System.Text;

namespace DotnetInspector.Platforms.Formats.Tests;

public sealed class PlatformManifestReaderTests
{
    [Fact]
    public void RuntimeConfiguration_ParsesEffectiveFrameworkSettings()
    {
        PlatformManifestParseOutcome<PlatformRuntimeConfiguration> outcome =
            PlatformRuntimeConfigurationReader.Parse(
                Utf8(
                    """
                    {
                      "runtimeOptions": {
                        "rollForward": "LatestMinor",
                        "frameworks": [
                          {
                            "name": "Microsoft.NETCore.App",
                            "version": "11.0.0"
                          },
                          {
                            "name": "Microsoft.AspNetCore.App",
                            "version": "11.0.1",
                            "rollForward": "Disable"
                          }
                        ]
                      }
                    }
                    """),
                cancellationToken: TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            PlatformManifestParseOutcome<
                PlatformRuntimeConfiguration>.Succeeded>(outcome);
        Assert.Collection(
            succeeded.Value.Frameworks,
            framework =>
            {
                Assert.Equal(
                    "Microsoft.NETCore.App",
                    framework.Name.Value);
                Assert.Equal(
                    PlatformFrameworkRollForward.LatestMinor,
                    framework.RollForward);
                Assert.True(framework.ApplyPatches);
            },
            framework =>
            {
                Assert.Equal(
                    "Microsoft.AspNetCore.App",
                    framework.Name.Value);
                Assert.Equal(
                    PlatformFrameworkRollForward.Disable,
                    framework.RollForward);
            });
    }

    [Fact]
    public void RuntimeConfiguration_ParsesDependencyFreeLeaf()
    {
        PlatformManifestParseOutcome<PlatformRuntimeConfiguration> outcome =
            PlatformRuntimeConfigurationReader.Parse(
                Utf8(
                    """
                    {
                      "runtimeOptions": {
                        "tfm": "net11.0"
                      }
                    }
                    """),
                cancellationToken: TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            PlatformManifestParseOutcome<
                PlatformRuntimeConfiguration>.Succeeded>(outcome);
        Assert.Empty(succeeded.Value.Frameworks);
    }

    [Fact]
    public void RuntimeConfiguration_ParsesLegacyCompatibilitySettings()
    {
        PlatformManifestParseOutcome<PlatformRuntimeConfiguration> outcome =
            PlatformRuntimeConfigurationReader.Parse(
                Utf8(
                    """
                    {
                      "runtimeOptions": {
                        "applyPatches": false,
                        "rollForwardOnNoCandidateFx": 2,
                        "framework": {
                          "name": "Microsoft.NETCore.App",
                          "version": "11.0.0"
                        }
                      }
                    }
                    """),
                cancellationToken: TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            PlatformManifestParseOutcome<
                PlatformRuntimeConfiguration>.Succeeded>(outcome);
        PlatformFrameworkReference framework =
            Assert.Single(succeeded.Value.Frameworks);
        Assert.Equal(
            PlatformFrameworkRollForward.Major,
            framework.RollForward);
        Assert.False(framework.ApplyPatches);
    }

    [Theory]
    [InlineData(
        """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","name":"Other","version":"11.0.0"}}}""",
        PlatformManifestDiagnosticKind.MalformedJson)]
    [InlineData(
        """{"runtimeOptions":{"framework":{"name":"../escape","version":"11.0.0"}}}""",
        PlatformManifestDiagnosticKind.InvalidFrameworkName)]
    [InlineData(
        """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"11.0"}}}""",
        PlatformManifestDiagnosticKind.InvalidVersion)]
    [InlineData(
        """{"runtimeOptions":{"rollForward":"Minor","applyPatches":true}}""",
        PlatformManifestDiagnosticKind.InvalidRollForward)]
    [InlineData(
        """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"11.0.0","rollForward":"3"}}}""",
        PlatformManifestDiagnosticKind.InvalidRollForward)]
    [InlineData(
        """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"11.0.0","rollForward":"Minor, LatestPatch"}}}""",
        PlatformManifestDiagnosticKind.InvalidRollForward)]
    [InlineData(
        """{"runtimeOptions":{"frameworks":[{"name":"Microsoft.NETCore.App","version":"11.0.0"},{"name":"Microsoft.NETCore.App","version":"11.0.1"}]}}""",
        PlatformManifestDiagnosticKind.DuplicateFrameworkReference)]
    [InlineData(
        """{"runtimeOptions":{},"\uD800":0}""",
        PlatformManifestDiagnosticKind.MalformedJson)]
    public void RuntimeConfiguration_RejectsInvalidDocuments(
        string json,
        PlatformManifestDiagnosticKind expected)
    {
        PlatformManifestParseOutcome<PlatformRuntimeConfiguration> outcome =
            PlatformRuntimeConfigurationReader.Parse(
                Utf8(json),
                cancellationToken: TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            PlatformManifestParseOutcome<
                PlatformRuntimeConfiguration>.Rejected>(outcome);
        Assert.Equal(expected, rejected.Diagnostic.Kind);
    }

    [Fact]
    public void RuntimeConfiguration_ReportsFrameworkBudget()
    {
        PlatformManifestParseOutcome<PlatformRuntimeConfiguration> outcome =
            PlatformRuntimeConfigurationReader.Parse(
                Utf8(
                    """
                    {
                      "runtimeOptions": {
                        "frameworks": [
                          {
                            "name": "Microsoft.NETCore.App",
                            "version": "11.0.0"
                          },
                          {
                            "name": "Microsoft.AspNetCore.App",
                            "version": "11.0.0"
                          }
                        ]
                      }
                    }
                    """),
                new PlatformManifestParseBudget(
                    maxBytes: 4096,
                    maxFrameworkReferences: 1,
                    maxLibraries: 1,
                    maxAssets: 1),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            PlatformManifestParseOutcome<
                PlatformRuntimeConfiguration>.Incomplete>(outcome);
    }

    [Fact]
    public void DependencyManifest_UsesNamedTargetManagedAssets()
    {
        PlatformManifestParseOutcome<PlatformDependencyManifest> outcome =
            PlatformDependencyManifestReader.Parse(
                Utf8(
                    """
                    {
                      "runtimeTarget": {
                        "name": ".NETCoreApp,Version=v11.0/osx-arm64"
                      },
                      "targets": {
                        ".NETCoreApp,Version=v11.0": {
                          "ignored/1.0.0": {
                            "runtime": {
                              "Ignored.dll": {}
                            }
                          }
                        },
                        ".NETCoreApp,Version=v11.0/osx-arm64": {
                          "runtime/11.0.0": {
                            "runtime": {
                              "System.Runtime.dll": {},
                              "runtimes/osx-arm64/lib/net11.0/System.Text.Json.dll": {}
                            },
                            "native": {
                              "System.Private.CoreLib.dll": {},
                              "libhostpolicy.dylib": {}
                            }
                          }
                        }
                      }
                    }
                    """),
                cancellationToken: TestContext.Current.CancellationToken);

        var succeeded = Assert.IsType<
            PlatformManifestParseOutcome<
                PlatformDependencyManifest>.Succeeded>(outcome);
        Assert.Equal(
            ".NETCoreApp,Version=v11.0/osx-arm64",
            succeeded.Value.RuntimeTargetName);
        Assert.Equal(
            [
                "System.Private.CoreLib.dll",
                "System.Runtime.dll",
                "runtimes/osx-arm64/lib/net11.0/System.Text.Json.dll",
            ],
            succeeded.Value.ManagedAssets.Select(
                static asset => asset.Value));
    }

    [Theory]
    [InlineData(
        """{"targets":{}}""",
        PlatformManifestDiagnosticKind.MissingRuntimeTarget)]
    [InlineData(
        """{"runtimeTarget":{"name":"target"},"targets":{}}""",
        PlatformManifestDiagnosticKind.MissingRuntimeTargetAssets)]
    [InlineData(
        """{"runtimeTarget":{"name":"target"},"targets":{"target":{"x/1":{"runtime":{"../Escape.dll":{}}}}}}""",
        PlatformManifestDiagnosticKind.InvalidAssetCoordinate)]
    [InlineData(
        """{"runtimeTarget":{"name":"target"},"targets":{"target":{"x/1":{"runtime":{"C:/outside/Managed.dll":{}}}}}}""",
        PlatformManifestDiagnosticKind.InvalidAssetCoordinate)]
    [InlineData(
        """{"runtimeTarget":{"name":"target"},"targets":{"target":{}},"\uD800":0}""",
        PlatformManifestDiagnosticKind.MalformedJson)]
    public void DependencyManifest_RejectsInvalidDocuments(
        string json,
        PlatformManifestDiagnosticKind expected)
    {
        PlatformManifestParseOutcome<PlatformDependencyManifest> outcome =
            PlatformDependencyManifestReader.Parse(
                Utf8(json),
                cancellationToken: TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            PlatformManifestParseOutcome<
                PlatformDependencyManifest>.Rejected>(outcome);
        Assert.Equal(expected, rejected.Diagnostic.Kind);
    }

    [Fact]
    public void DependencyManifest_ReportsAssetBudget()
    {
        PlatformManifestParseOutcome<PlatformDependencyManifest> outcome =
            PlatformDependencyManifestReader.Parse(
                Utf8(
                    """
                    {
                      "runtimeTarget": { "name": "target" },
                      "targets": {
                        "target": {
                          "runtime/1.0.0": {
                            "runtime": {
                              "One.dll": {},
                              "Two.dll": {}
                            }
                          }
                        }
                      }
                    }
                    """),
                new PlatformManifestParseBudget(
                    maxBytes: 4096,
                    maxFrameworkReferences: 1,
                    maxLibraries: 1,
                    maxAssets: 1),
                TestContext.Current.CancellationToken);

        Assert.IsType<
            PlatformManifestParseOutcome<
                PlatformDependencyManifest>.Incomplete>(outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DependencyManifest_FailureIsIndependentOfAssetOrder(
        bool invalidFirst)
    {
        string assets = invalidFirst
                ? """
                  "C:/outside/Managed.dll": {},
                  "Good.dll": {}
                  """
                : """
                  "Good.dll": {},
                  "C:/outside/Managed.dll": {}
                  """;
        PlatformManifestParseOutcome<PlatformDependencyManifest> outcome =
                PlatformDependencyManifestReader.Parse(
                    Utf8(
                        $$"""
                        {
                          "runtimeTarget": { "name": "target" },
                          "targets": {
                            "target": {
                              "runtime/1.0.0": {
                                "runtime": {
                                  {{assets}}
                                }
                              }
                            }
                          }
                        }
                        """),
                    new PlatformManifestParseBudget(
                        maxBytes: 4096,
                        maxFrameworkReferences: 1,
                        maxLibraries: 1,
                        maxAssets: 1),
                    TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
                PlatformManifestParseOutcome<
                    PlatformDependencyManifest>.Rejected>(outcome);
        Assert.Equal(
                PlatformManifestDiagnosticKind.InvalidAssetCoordinate,
                rejected.Diagnostic.Kind);
    }

    private static ReadOnlyMemory<byte> Utf8(string value) =>
        Encoding.UTF8.GetBytes(value);
}
