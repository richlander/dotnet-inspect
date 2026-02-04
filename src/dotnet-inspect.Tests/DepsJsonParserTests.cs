using DotnetInspector;
using DotnetInspector.Inspectors;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for DepsJsonParser JSON parsing functionality.
/// </summary>
public class DepsJsonParserTests : IDisposable
{
    private readonly string _tempDir;

    public DepsJsonParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"deps-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string WriteDepsJson(string content)
    {
        var path = Path.Combine(_tempDir, "test.deps.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Parse_ExtractsRuntimeTargetRid()
    {
        var deps = WriteDepsJson("""
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0/linux-x64"
              }
            }
            """);

        var result = new InspectionResult();
        DepsJsonParser.Parse(deps, result);

        Assert.Equal("linux-x64", result.RuntimeTargetRid);
    }

    [Fact]
    public void Parse_HandlesRuntimeTargetWithoutRid()
    {
        var deps = WriteDepsJson("""
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0"
              }
            }
            """);

        var result = new InspectionResult();
        DepsJsonParser.Parse(deps, result);

        Assert.Null(result.RuntimeTargetRid);
    }

    [Fact]
    public void Parse_ExtractsPackageDependencies()
    {
        var deps = WriteDepsJson("""
            {
              "libraries": {
                "System.Text.Json/8.0.0": {
                  "type": "package"
                },
                "Microsoft.Extensions.Logging/8.0.0": {
                  "type": "package"
                }
              }
            }
            """);

        var result = new InspectionResult();
        DepsJsonParser.Parse(deps, result);

        Assert.NotNull(result.RuntimeDependencies);
        Assert.Equal(2, result.RuntimeDependencies.Count);
        Assert.Contains(result.RuntimeDependencies, d => d.Id == "System.Text.Json" && d.Version == "8.0.0");
        Assert.Contains(result.RuntimeDependencies, d => d.Id == "Microsoft.Extensions.Logging" && d.Version == "8.0.0");
    }

    [Fact]
    public void Parse_IgnoresNonPackageLibraries()
    {
        var deps = WriteDepsJson("""
            {
              "libraries": {
                "System.Text.Json/8.0.0": {
                  "type": "package"
                },
                "MyApp/1.0.0": {
                  "type": "project"
                },
                "Microsoft.NETCore.App.Ref/8.0.0": {
                  "type": "reference"
                }
              }
            }
            """);

        var result = new InspectionResult();
        DepsJsonParser.Parse(deps, result);

        Assert.NotNull(result.RuntimeDependencies);
        Assert.Single(result.RuntimeDependencies);
        Assert.Equal("System.Text.Json", result.RuntimeDependencies[0].Id);
    }

    [Fact]
    public void Parse_HandlesEmptyLibraries()
    {
        var deps = WriteDepsJson("""
            {
              "libraries": {}
            }
            """);

        var result = new InspectionResult();
        DepsJsonParser.Parse(deps, result);

        Assert.Null(result.RuntimeDependencies);
    }

    [Fact]
    public void Parse_HandlesMissingLibraries()
    {
        var deps = WriteDepsJson("""
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0"
              }
            }
            """);

        var result = new InspectionResult();
        DepsJsonParser.Parse(deps, result);

        Assert.Null(result.RuntimeDependencies);
    }

    [Fact]
    public void Parse_HandlesMalformedLibraryName()
    {
        var deps = WriteDepsJson("""
            {
              "libraries": {
                "NoVersionSlash": {
                  "type": "package"
                },
                "Valid/1.0.0": {
                  "type": "package"
                }
              }
            }
            """);

        var result = new InspectionResult();
        DepsJsonParser.Parse(deps, result);

        // Only the valid entry should be parsed
        Assert.NotNull(result.RuntimeDependencies);
        Assert.Single(result.RuntimeDependencies);
        Assert.Equal("Valid", result.RuntimeDependencies[0].Id);
    }

    [Fact]
    public void Parse_HandlesInvalidJson_DoesNotThrow()
    {
        var deps = WriteDepsJson("{ invalid json }");

        var result = new InspectionResult();

        // Should not throw
        DepsJsonParser.Parse(deps, result);

        Assert.Null(result.RuntimeDependencies);
        Assert.Null(result.RuntimeTargetRid);
    }

    [Fact]
    public void Parse_CombinesRuntimeTargetAndDependencies()
    {
        var deps = WriteDepsJson("""
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v9.0/win-x64"
              },
              "libraries": {
                "Newtonsoft.Json/13.0.3": {
                  "type": "package"
                }
              }
            }
            """);

        var result = new InspectionResult();
        DepsJsonParser.Parse(deps, result);

        Assert.Equal("win-x64", result.RuntimeTargetRid);
        Assert.NotNull(result.RuntimeDependencies);
        Assert.Single(result.RuntimeDependencies);
        Assert.Equal("Newtonsoft.Json", result.RuntimeDependencies[0].Id);
        Assert.Equal("13.0.3", result.RuntimeDependencies[0].Version);
    }

    [Fact]
    public void Parse_HandlesLibraryWithMissingType()
    {
        var deps = WriteDepsJson("""
            {
              "libraries": {
                "MissingType/1.0.0": {},
                "HasType/2.0.0": {
                  "type": "package"
                }
              }
            }
            """);

        var result = new InspectionResult();
        DepsJsonParser.Parse(deps, result);

        // Only the entry with type should be processed
        Assert.NotNull(result.RuntimeDependencies);
        Assert.Single(result.RuntimeDependencies);
        Assert.Equal("HasType", result.RuntimeDependencies[0].Id);
    }
}
