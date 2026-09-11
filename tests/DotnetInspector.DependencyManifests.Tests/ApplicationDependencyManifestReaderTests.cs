using System.Text;

namespace DotnetInspector.DependencyManifests.Tests;

public sealed class ApplicationDependencyManifestReaderTests
{
    [Fact]
    public void Parse_AdmitsCurrentSdkGeneratedManifest()
    {
        string assemblyName =
            typeof(ApplicationDependencyManifestReaderTests)
                .Assembly.GetName().Name!;
        byte[] bytes = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                $"{assemblyName}.deps.json"));

        ApplicationDependencyManifest manifest = Succeeded(
            ApplicationDependencyManifestReader.Parse(
                bytes,
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            manifest.RuntimeTargetName,
            manifest.CompilationTargetName);
        ApplicationDependencyManifestLibrary reader =
            Assert.Single(
                manifest.Libraries,
                library =>
                    library.Key.StartsWith(
                        "DotnetInspector.DependencyManifests/",
                        StringComparison.Ordinal));
        Assert.Equal(
            ApplicationDependencyLibraryKind.Project,
            reader.Kind);
        Assert.Contains(
            reader.Assets,
            asset =>
                asset.Role
                    == ApplicationDependencyAssetRole.Runtime
                && asset.Coordinate.FileName
                    == "DotnetInspector.DependencyManifests.dll");
    }

    [Fact]
    public void Parse_ComposesRidlessCompileAndRuntimeTargetAssets()
    {
        ApplicationDependencyManifest manifest = Succeeded(
            ApplicationDependencyManifestReader.Parse(
                Utf8(
                    """
                    {
                      "runtimeTarget": {
                        "name": ".NETCoreApp,Version=v11.0/linux-x64"
                      },
                      "targets": {
                        ".NETCoreApp,Version=v11.0": {
                          "CompileOnly/1.0.0": {
                            "compile": {
                              "ref/net11.0/CompileOnly.dll": {}
                            }
                          }
                        },
                        ".NETCoreApp,Version=v11.0/linux-x64": {
                          "RuntimeOnly/1.0.0": {
                            "runtime": {
                              "runtimes/linux-x64/lib/net11.0/RuntimeOnly.dll": {
                                "localPath": "RuntimeOnly.dll"
                              }
                            }
                          }
                        },
                        "decoy": {
                          "Decoy/1.0.0": {
                            "runtime": {
                              "Decoy.dll": {}
                            }
                          }
                        }
                      },
                      "libraries": {
                        "CompileOnly/1.0.0": {
                          "type": "package",
                          "path": "compileonly/1.0.0"
                        },
                        "RuntimeOnly/1.0.0": {
                          "type": "project"
                        },
                        "Decoy/1.0.0": {
                          "type": "project"
                        }
                      }
                    }
                    """),
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ".NETCoreApp,Version=v11.0/linux-x64",
            manifest.RuntimeTargetName);
        Assert.Equal(
            ".NETCoreApp,Version=v11.0",
            manifest.CompilationTargetName);
        Assert.Equal(
            ["CompileOnly/1.0.0", "RuntimeOnly/1.0.0"],
            manifest.Libraries.Select(
                static library => library.Key));
        Assert.Equal(
            ApplicationDependencyLibraryKind.Package,
            manifest.Libraries[0].Kind);
        Assert.Equal(
            "compileonly/1.0.0",
            manifest.Libraries[0].DeclaredPath!.Value);
        Assert.Equal(
            ApplicationDependencyAssetRole.Compile,
            Assert.Single(manifest.Libraries[0].Assets).Role);
        Assert.Equal(
            ApplicationDependencyLibraryKind.Project,
            manifest.Libraries[1].Kind);
        ApplicationDependencyManifestAsset runtime =
            Assert.Single(manifest.Libraries[1].Assets);
        Assert.Equal(
            ApplicationDependencyAssetRole.Runtime,
            runtime.Role);
        Assert.Equal("RuntimeOnly.dll", runtime.LocalPath!.Value);
    }

    [Fact]
    public void Parse_RejectsInvalidUtf8()
    {
        byte[] bytes = Utf8(
            """
            {
              "runtimeTarget": { "name": "target" },
              "targets": { "target": {} },
              "libraries": {}
            }
            """).ToArray();
        bytes[Array.IndexOf(bytes, (byte)'t')] = 0xff;

        var rejected = Assert.IsType<
            ApplicationDependencyManifestParseOutcome.Rejected>(
                ApplicationDependencyManifestReader.Parse(
                    bytes,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationDependencyManifestDiagnosticKind.MalformedJson,
            rejected.Diagnostic.Kind);
    }

    [Theory]
    [InlineData(
        """{"runtimeTarget":{"name":"target"},"targets":{"target":{}},"libraries":{},"libraries":{}}""",
        ApplicationDependencyManifestDiagnosticKind.MalformedJson)]
    [InlineData(
        """{"targets":{},"libraries":{}}""",
        ApplicationDependencyManifestDiagnosticKind.MissingRuntimeTarget)]
    [InlineData(
        """{"runtimeTarget":{"name":"target/rid"},"targets":{"target/rid":{}},"libraries":{}}""",
        ApplicationDependencyManifestDiagnosticKind.MissingCompilationTarget)]
    [InlineData(
        """{"runtimeTarget":{"name":"target"},"targets":{"target":{"P/1.0.0":{"runtime":{"../P.dll":{}}}}},"libraries":{"P/1.0.0":{"type":"package"}}}""",
        ApplicationDependencyManifestDiagnosticKind.InvalidAssetCoordinate)]
    [InlineData(
        """{"runtimeTarget":{"name":"target"},"targets":{"target":{"P/1.0.0":{}}},"libraries":{"P/1.0.0":{"type":"package","path":null}}}""",
        ApplicationDependencyManifestDiagnosticKind.InvalidLibraryMetadata)]
    public void Parse_RejectsInvalidDocuments(
        string json,
        ApplicationDependencyManifestDiagnosticKind expected)
    {
        var rejected = Assert.IsType<
            ApplicationDependencyManifestParseOutcome.Rejected>(
                ApplicationDependencyManifestReader.Parse(
                    Utf8(json),
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(expected, rejected.Diagnostic.Kind);
    }

    [Fact]
    public void Parse_ReportsAssetBudgetWithoutPartialResult()
    {
        ApplicationDependencyManifestParseOutcome outcome =
            ApplicationDependencyManifestReader.Parse(
                Utf8(
                    """
                    {
                      "runtimeTarget": { "name": "target" },
                      "targets": {
                        "target": {
                          "P/1.0.0": {
                            "runtime": {
                              "One.dll": {},
                              "Two.dll": {}
                            }
                          }
                        }
                      },
                      "libraries": {
                        "P/1.0.0": { "type": "project" }
                      }
                    }
                    """),
                new ApplicationDependencyManifestParseBudget(
                    maxBytes: 4096,
                    maxScalarCharacters: 128,
                    maxLibraries: 4,
                    maxAssets: 1),
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            ApplicationDependencyManifestParseOutcome.Incomplete>(
                outcome);
        Assert.Equal(
            ApplicationDependencyManifestDiagnosticKind
                .WorkLimitExceeded,
            incomplete.Diagnostic.Kind);
    }

    private static ApplicationDependencyManifest Succeeded(
        ApplicationDependencyManifestParseOutcome outcome) =>
        Assert.IsType<
            ApplicationDependencyManifestParseOutcome.Succeeded>(
                outcome).Value;

    private static ReadOnlyMemory<byte> Utf8(string value) =>
        Encoding.UTF8.GetBytes(value);
}
