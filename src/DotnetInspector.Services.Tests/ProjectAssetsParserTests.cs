using System.Text.Json;

namespace DotnetInspector.Services.Tests;

[Collection(NuGetCacheRootCollection.Name)]
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
    public void ParsePackageFileEntries_ReturnsDirectPackageFilesMatchingPattern()
    {
        // library.path is a relative coordinate under the NuGet cache in every assets file NuGet
        // writes, and ResolvePackagePath now refuses anything else, so this redirects the cache
        // root rather than pointing library.path at an absolute temp directory.
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"pa-files-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(cacheRoot, "direct.package", "2.1.0");
        var skillPath = Path.Combine(packageRoot, "skills", "query", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(skillPath)!);
        File.WriteAllText(skillPath, "# Skill");

        Directory.CreateDirectory(Path.Combine(cacheRoot, "transitive.package", "1.0.0", "skills", "transitive"));

        var json = $$"""
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
                    "path": "direct.package/2.1.0",
                    "files": [
                        42,
                        "README.md",
                        "skills/query/SKILL.md"
                    ]
                },
                "Transitive.Package/1.0.0": {
                    "type": "package",
                    "path": "transitive.package/1.0.0",
                    "files": [
                        "skills/transitive/SKILL.md"
                    ]
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
        var previousCache = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", cacheRoot);
        try
        {
            var result = ProjectAssetsParser.ParsePackageFileEntries(
                assetsPath,
                null,
                ["skills/**/SKILL.md"],
                null);

            var entry = Assert.Single(result);
            Assert.Equal("Direct.Package", entry.PackageName);
            Assert.Equal("2.1.0", entry.Version);
            Assert.Equal("skills/query/SKILL.md", entry.Path);
            Assert.Equal("net9.0", entry.TargetFramework);
            Assert.Equal(Path.GetFullPath(packageRoot), entry.PackagePath);
            Assert.Equal(Path.GetFullPath(skillPath), entry.FullPath);
            Assert.True(ProjectAssetsParser.HasPackageFileEntries(assetsPath, null, ["skills/**/SKILL.md"], null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousCache);
            File.Delete(assetsPath);
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void ParsePackageFileEntries_CanMatchTopLevelSkillFile()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"pa-files-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(cacheRoot, "direct.package", "1.0.0");
        var skillPath = Path.Combine(packageRoot, "skills", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(skillPath)!);
        File.WriteAllText(skillPath, "# Skill");

        var json = $$"""
        {
            "targets": {
                "net9.0": {
                    "Direct.Package/1.0.0": {}
                }
            },
            "libraries": {
                "Direct.Package/1.0.0": {
                    "type": "package",
                    "path": "direct.package/1.0.0",
                    "files": [
                        "skills/SKILL.md"
                    ]
                }
            },
            "project": {
                "frameworks": {
                    "net9.0": {
                        "dependencies": {
                            "Direct.Package": {
                                "target": "Package",
                                "version": "[1.0.0, )"
                            }
                        }
                    }
                }
            }
        }
        """;

        var assetsPath = WriteTempFile(json);
        var previousCache = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", cacheRoot);
        try
        {
            var entry = Assert.Single(ProjectAssetsParser.ParsePackageFileEntries(
                assetsPath,
                null,
                ["skills/SKILL.md", "skills/**/SKILL.md"],
                null));

            Assert.Equal("skills/SKILL.md", entry.Path);
            Assert.Equal(Path.GetFullPath(skillPath), entry.FullPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousCache);
            File.Delete(assetsPath);
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void ParsePackageFileEntries_ReturnsEmptyWhenPatternDoesNotMatch()
    {
        var json = """
        {
            "targets": {
                "net9.0": {
                    "Direct.Package/1.0.0": {}
                }
            },
            "libraries": {
                "Direct.Package/1.0.0": {
                    "type": "package",
                    "path": "direct.package/1.0.0",
                    "files": [
                        "README.md"
                    ]
                }
            },
            "project": {
                "frameworks": {
                    "net9.0": {
                        "dependencies": {
                            "Direct.Package": {
                                "target": "Package",
                                "version": "[1.0.0, )"
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
            Assert.Empty(ProjectAssetsParser.ParsePackageFileEntries(
                assetsPath,
                null,
                ["skills/**/SKILL.md"],
                null));
            Assert.False(ProjectAssetsParser.HasPackageFileEntries(
                assetsPath,
                null,
                ["skills/**/SKILL.md"],
                null));
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
    public void Parse_WithoutTfmFilter_SelectsHighestLongFormTfm()
    {
        var json = """
        {
            "targets": {
                ".NETCoreApp,Version=v8.0": {},
                "net472": {}
            },
            "libraries": {}
        }
        """;

        var assetsPath = WriteTempFile(json);
        List<string> messages = [];
        try
        {
            _ = ProjectAssetsParser.Parse(assetsPath, null, s => messages.Add(s));

            Assert.Contains(messages, m => m.Contains(".NETCoreApp,Version=v8.0"));
        }
        finally
        {
            File.Delete(assetsPath);
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

    [Fact]
    public void TryFindAssets_ExistingDirectoryWithoutProject_ReturnsProjectNotFound()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pa-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "no project here");
        try
        {
            Assert.False(ProjectAssetsParser.TryFindAssets(dir, out var found, out var status));
            Assert.Null(found);
            Assert.Equal(ProjectAssetsStatus.ProjectNotFound, status);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryFindAssets_ExistingNonProjectFile_ReturnsProjectNotFound()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pa-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "notes.txt");
        File.WriteAllText(file, "not a project");
        try
        {
            Assert.False(ProjectAssetsParser.TryFindAssets(file, out var found, out var status));
            Assert.Null(found);
            Assert.Equal(ProjectAssetsStatus.ProjectNotFound, status);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A crafted <c>project.assets.json</c> whose library <c>path</c> traverses out of the NuGet
    /// cache must not resolve, even when a readable file is planted exactly where the unguarded
    /// combine would land.
    /// </summary>
    /// <remarks>
    /// The traversal is computed with <see cref="Path.GetRelativePath(string, string)"/> from the
    /// real cache root to a temp directory, so the payload is genuinely reachable and the test
    /// fails if the guard is removed. The threat model lists <c>project.assets.json</c> and the
    /// paths inside it as untrusted, with unintended file reads as the risk.
    /// </remarks>
    [Fact]
    public void Parse_WithTraversingLibraryPath_DoesNotEscapeTheNuGetCache()
    {
        var payloadRoot = Path.Combine(Path.GetTempPath(), $"assets-traversal-{Guid.NewGuid():N}");
        var payloadDirectory = Path.Combine(payloadRoot, "lib", "net8.0");
        Directory.CreateDirectory(payloadDirectory);
        var payload = Path.Combine(payloadDirectory, "payload.dll");
        File.WriteAllText(payload, "payload");

        var cacheRoot = DotnetInspector.Packages.NuGetCache.GetNuGetCachePath();
        var traversal = Path.GetRelativePath(cacheRoot, payloadRoot).Replace(Path.DirectorySeparatorChar, '/');

        // The traversal has to actually reach the payload, or the test would pass without the guard.
        Assert.Contains("..", traversal, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(cacheRoot, traversal.Replace('/', Path.DirectorySeparatorChar), "lib", "net8.0", "payload.dll")));

        var assetsPath = CreateTempAssetsFile(new Dictionary<string, object>
        {
            ["version"] = 3,
            ["targets"] = new Dictionary<string, object>
            {
                ["net8.0"] = new Dictionary<string, object>
                {
                    ["Evil.Package/1.0.0"] = new Dictionary<string, object>
                    {
                        ["type"] = "package",
                        ["compile"] = new Dictionary<string, object>
                        {
                            ["lib/net8.0/payload.dll"] = new Dictionary<string, object>()
                        }
                    }
                }
            },
            ["libraries"] = new Dictionary<string, object>
            {
                ["Evil.Package/1.0.0"] = new Dictionary<string, object>
                {
                    ["type"] = "package",
                    ["path"] = traversal
                }
            }
        });

        try
        {
            var results = ProjectAssetsParser.Parse(assetsPath, null, null);

            Assert.DoesNotContain(results, r => r.Path.Contains("payload.dll", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(assetsPath);
            Directory.Delete(payloadRoot, recursive: true);
        }
    }

    internal static string CreateTempAssetsFile(object content)
    {
        var json = JsonSerializer.Serialize(content);
        return WriteTempFile(json);
    }

    internal static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"project-assets-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
