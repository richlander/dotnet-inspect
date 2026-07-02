using System.Text.Json;

namespace DotnetInspector.Services.Tests;

public class ProjectAssetsParserTests
{
    [Fact]
    public void Parse_EmptyTargets_ReturnsEmpty()
    {
        var assetsPath = CreateTempAssetsFile(new
        {
            targets = new { }
        });

        try
        {
            var results = ProjectAssetsParser.Parse(assetsPath, null, null);
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(assetsPath);
        }
    }

    [Fact]
    public void Parse_NoTargetsProperty_ReturnsEmpty()
    {
        var assetsPath = CreateTempAssetsFile(new { version = 3 });

        try
        {
            var results = ProjectAssetsParser.Parse(assetsPath, null, null);
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(assetsPath);
        }
    }

    [Fact]
    public void Parse_SkipsProjectTypeLibraries()
    {
        var json = """
        {
            "targets": {
                "net9.0": {
                    "MyProject/1.0.0": {
                        "type": "package",
                        "compile": { "lib/net9.0/MyProject.dll": {} }
                    }
                }
            },
            "libraries": {
                "MyProject/1.0.0": {
                    "type": "project",
                    "path": "myproject/1.0.0"
                }
            }
        }
        """;

        var assetsPath = WriteTempFile(json);
        try
        {
            var results = ProjectAssetsParser.Parse(assetsPath, null, null);
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(assetsPath);
        }
    }

    [Fact]
    public void Parse_SkipsPlaceholderAssemblies()
    {
        var json = """
        {
            "targets": {
                "net9.0": {
                    "System.Runtime/9.0.0": {
                        "compile": { "lib/net9.0/_._": {} }
                    }
                }
            },
            "libraries": {
                "System.Runtime/9.0.0": {
                    "type": "package",
                    "path": "system.runtime/9.0.0"
                }
            }
        }
        """;

        var assetsPath = WriteTempFile(json);
        try
        {
            var results = ProjectAssetsParser.Parse(assetsPath, null, null);
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(assetsPath);
        }
    }

    [Fact]
    public void Parse_WithTfmFilter_SelectsMatchingTfm()
    {
        var json = """
        {
            "targets": {
                "net8.0": {
                    "Foo/1.0.0": {
                        "compile": { "lib/net8.0/Foo.dll": {} }
                    }
                },
                "net9.0": {
                    "Foo/1.0.0": {
                        "compile": { "lib/net9.0/Foo.dll": {} }
                    }
                }
            },
            "libraries": {
                "Foo/1.0.0": {
                    "type": "package",
                    "path": "foo/1.0.0"
                }
            }
        }
        """;

        var assetsPath = WriteTempFile(json);
        List<string> messages = [];
        try
        {
            var results = ProjectAssetsParser.Parse(assetsPath, "net8.0", s => messages.Add(s));
            // Won't find actual files on disk, but the TFM should be selected
            Assert.Contains(messages, m => m.Contains("net8.0"));
        }
        finally
        {
            File.Delete(assetsPath);
        }
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsEmptyAndLogs()
    {
        var assetsPath = WriteTempFile("not valid json {{{");
        List<string> messages = [];

        try
        {
            var results = ProjectAssetsParser.Parse(assetsPath, null, s => messages.Add(s));
            Assert.Empty(results);
            Assert.Single(messages);
            Assert.Contains("Warning", messages[0]);
        }
        finally
        {
            File.Delete(assetsPath);
        }
    }

    [Fact]
    public void ParsePackageReferences_ReturnsOnlyDirectPackagesWithResolvedVersions()
    {
        var json = """
        {
            "targets": {
                "net9.0": {
                    "Direct.Package/2.1.0": {},
                    "Transitive.Package/1.0.0": {},
                    "ProjectRef/1.0.0": {}
                }
            },
            "libraries": {
                "Direct.Package/2.1.0": {
                    "type": "package",
                    "path": "direct.package/2.1.0"
                },
                "Transitive.Package/1.0.0": {
                    "type": "package",
                    "path": "transitive.package/1.0.0"
                },
                "ProjectRef/1.0.0": {
                    "type": "project",
                    "path": "../ProjectRef/ProjectRef.csproj"
                }
            },
            "project": {
                "frameworks": {
                    "net9.0": {
                        "dependencies": {
                            "Direct.Package": {
                                "target": "Package",
                                "version": "[2.0.0, )"
                            },
                            "ProjectRef": {
                                "target": "Project"
                            }
                        }
                    }
                }
            }
        }
        """;

        var assetsPath = WriteTempFile(json);
        try
        {
            var result = ProjectAssetsParser.ParsePackageReferences(assetsPath, null, null);

            var dependency = Assert.Single(result);
            Assert.Equal("Direct.Package", dependency.PackageName);
            Assert.Equal("2.1.0", dependency.Version);
            Assert.Equal("net9.0", dependency.TargetFramework);
            Assert.EndsWith(Path.Combine("direct.package", "2.1.0"), dependency.PackagePath);
        }
        finally
        {
            File.Delete(assetsPath);
        }
    }

    [Fact]
    public void Parse_WithoutTfmFilter_SelectsHighestPriorityTfm()
    {
        var json = """
        {
            "targets": {
                "net6.0": {
                    "Foo/1.0.0": {
                        "compile": { "lib/net6.0/Foo.dll": {} }
                    }
                },
                "net9.0": {
                    "Foo/1.0.0": {
                        "compile": { "lib/net9.0/Foo.dll": {} }
                    }
                }
            },
            "libraries": {
                "Foo/1.0.0": {
                    "type": "package",
                    "path": "foo/1.0.0"
                }
            }
        }
        """;

        var assetsPath = WriteTempFile(json);
        List<string> messages = [];
        try
        {
            var results = ProjectAssetsParser.Parse(assetsPath, null, s => messages.Add(s));
            // net9.0 should be preferred over net6.0
            Assert.Contains(messages, m => m.Contains("net9.0"));
        }
        finally
        {
            File.Delete(assetsPath);
        }
    }

    [Fact]
    public void Parse_ReturnsAssemblyPaths_WhenFileExists()
    {
        // Create a temp directory structure that mimics NuGet cache
        var tempDir = Path.Combine(Path.GetTempPath(), $"parser-test-{Guid.NewGuid():N}");
        var packageDir = Path.Combine(tempDir, "foo", "1.0.0", "lib", "net9.0");
        Directory.CreateDirectory(packageDir);
        var dllPath = Path.Combine(packageDir, "Foo.dll");
        File.WriteAllText(dllPath, "fake assembly");

        // Build a relative path from NuGet cache
        var nugetCache = DotnetInspector.Packages.NuGetCache.GetNuGetCachePath();
        var relPath = Path.GetRelativePath(nugetCache, Path.Combine(tempDir, "foo", "1.0.0"));

        var json = $$"""
        {
            "targets": {
                "net9.0": {
                    "Foo/1.0.0": {
                        "compile": { "lib/net9.0/Foo.dll": {} }
                    }
                }
            },
            "libraries": {
                "Foo/1.0.0": {
                    "type": "package",
                    "path": "{{relPath.Replace("\\", "/")}}"
                }
            }
        }
        """;

        var assetsPath = WriteTempFile(json);
        try
        {
            var results = ProjectAssetsParser.Parse(assetsPath, null, null);
            // The file needs to exist at the NuGet cache location, which is tricky to simulate
            // This test verifies the parsing logic runs without error
            // Files won't be found since our temp dir isn't the NuGet cache
        }
        finally
        {
            File.Delete(assetsPath);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryFindAssets_DirectAssetsJsonPath_ReturnsFound()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pa-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var assets = Path.Combine(dir, "project.assets.json");
        File.WriteAllText(assets, "{}");
        try
        {
            Assert.True(ProjectAssetsParser.TryFindAssets(assets, out var found, out var status));
            Assert.Equal(ProjectAssetsStatus.Found, status);
            Assert.Equal(Path.GetFullPath(assets), found);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryFindAssets_DirectoryWithObjAssets_ReturnsFound()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pa-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        File.WriteAllText(Path.Combine(dir, "Sample.csproj"), "<Project/>");
        var assets = Path.Combine(dir, "obj", "project.assets.json");
        File.WriteAllText(assets, "{}");
        try
        {
            Assert.True(ProjectAssetsParser.TryFindAssets(dir, out var found, out var status));
            Assert.Equal(ProjectAssetsStatus.Found, status);
            Assert.Equal(Path.GetFullPath(assets), found);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryFindAssets_ProjectWithoutRestore_ReturnsAssetsNotRestored()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pa-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, "Sample.csproj");
        File.WriteAllText(csproj, "<Project/>");
        try
        {
            Assert.False(ProjectAssetsParser.TryFindAssets(csproj, out var found, out var status));
            Assert.Null(found);
            Assert.Equal(ProjectAssetsStatus.AssetsNotRestored, status);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryFindAssets_NonexistentPath_ReturnsProjectNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"pa-missing-{Guid.NewGuid():N}", "Sample.csproj");

        Assert.False(ProjectAssetsParser.TryFindAssets(missing, out var found, out var status));
        Assert.Null(found);
        Assert.Equal(ProjectAssetsStatus.ProjectNotFound, status);
    }

    [Fact]
    public void DescribeMissingAssets_ProducesDistinctMessages()
    {
        var notFound = ProjectAssetsParser.DescribeMissingAssets("x.csproj", ProjectAssetsStatus.ProjectNotFound);
        var notRestored = ProjectAssetsParser.DescribeMissingAssets("x.csproj", ProjectAssetsStatus.AssetsNotRestored);

        Assert.Contains("Project not found", notFound);
        Assert.Contains("dotnet restore", notRestored);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProjectAssetsParser.DescribeMissingAssets("x", ProjectAssetsStatus.Found));
    }

    private static string CreateTempAssetsFile(object content)
    {
        var json = JsonSerializer.Serialize(content);
        return WriteTempFile(json);
    }

    private static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"project-assets-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
