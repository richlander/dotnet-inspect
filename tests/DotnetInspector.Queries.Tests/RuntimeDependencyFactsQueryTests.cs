using System.Text;

namespace DotnetInspector.Queries.Tests;

public class RuntimeDependencyFactsQueryTests
{
    [Fact]
    public void Execute_CurrentTestHostDepsJsonProjectsACompleteGraph()
    {
        string assemblyName =
            typeof(RuntimeDependencyFactsQueryTests).Assembly.GetName().Name!;
        byte[] bytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.deps.json"));

        RuntimeDependencyFacts facts = Assert.IsType<
            RuntimeDependencyFactsResult.Available>(
                RuntimeDependencyFactsQuery.Execute(bytes)).Value;

        Assert.True(facts.Graph.IsComplete);
        Assert.NotEmpty(facts.Graph.Packages);
        Assert.NotEmpty(facts.Graph.Edges);
        Assert.NotEmpty(facts.Target.SourceNameSpelling.ToString());
    }

    [Fact]
    public void Execute_SelectsExactRuntimeTargetAndProjectsPackageRelationships()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0/linux-x64",
                "signature": ""
              },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "Compile.Only/9.0.0": {
                    "dependencies": {}
                  }
                },
                ".NETCoreApp,Version=v8.0/linux-x64": {
                  "Example.App/1.0.0": {
                    "dependencies": {
                      "Example.Package": "1.0.0"
                    }
                  },
                  "Example.Package/1.0.0": {
                    "dependencies": {
                      "Example.Transitive": "2.0.0"
                    }
                  },
                  "Example.Transitive/2.0.0": {}
                }
              },
              "libraries": {
                "Compile.Only/9.0.0": { "type": "package" },
                "Example.App/1.0.0": { "type": "project" },
                "Example.Package/1.0.0": { "type": "package" },
                "Example.Transitive/2.0.0": { "type": "package" }
              }
            }
            """);

        Assert.Equal("net8.0/linux-x64", facts.Target.Identity);
        Assert.Equal(2, facts.Graph.Packages.Length);
        Assert.Equal(2, facts.Graph.Edges.Length);
        Assert.DoesNotContain(
            facts.Graph.Packages,
            package => package.Identity.Coordinate.PackageId == "compile.only");

        RuntimeDependencyGraphEdge rootEdge = Assert.Single(
            facts.Graph.Edges,
            edge => edge.Dependency.Coordinate.PackageId == "example.package");
        Assert.IsType<RuntimeDependencyGraphParentIdentity.Library>(
            rootEdge.Parent);
        Assert.Equal(1, rootEdge.SourceOccurrenceCount);

        RuntimeDependencyGraphEdge transitiveEdge = Assert.Single(
            facts.Graph.Edges,
            edge => edge.Dependency.Coordinate.PackageId
                == "example.transitive");
        var packageParent =
            Assert.IsType<RuntimeDependencyGraphParentIdentity.Package>(
                transitiveEdge.Parent);
        Assert.Equal(
            "example.package",
            packageParent.Identity.Coordinate.PackageId);
    }

    [Fact]
    public void Execute_RuntimeTargetSelectionIsExactAndCaseSensitive()
    {
        RuntimeDependencyFactsResult result = Execute(
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0/Linux-X64"
              },
              "targets": {
                ".NETCoreApp,Version=v8.0/linux-x64": {}
              },
              "libraries": {}
            }
            """);

        var failed = Assert.IsType<RuntimeDependencyFactsResult.Failed>(result);
        Assert.Equal(
            RuntimeDependencyFailureReason.UnsupportedDocumentShape,
            failed.Failure.Reason);
    }

    [Fact]
    public void Execute_LegacyStringRuntimeTargetIsAccepted()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": ".NETCoreApp,Version=v8.0",
              "targets": {
                ".NETCoreApp,Version=v8.0": {}
              },
              "libraries": {}
            }
            """);

        Assert.Equal("net8.0", facts.Target.Identity);
        Assert.True(facts.Graph.IsComplete);
    }

    [Fact]
    public void Execute_EmptySelectedTargetIsCompleteEmptyEvidence()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0"
              },
              "targets": {
                ".NETCoreApp,Version=v8.0": {}
              },
              "libraries": {}
            }
            """);

        Assert.True(facts.Graph.IsComplete);
        Assert.Empty(facts.Graph.Packages);
        Assert.Empty(facts.Graph.Edges);
        Assert.Empty(facts.Graph.Failures);
    }

    [Fact]
    public void Execute_NonPackageDependencyIsNotInventedAsPackageEvidence()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0"
              },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "Example.App/1.0.0": {
                    "dependencies": {
                      "Example.Library": "1.0.0"
                    }
                  },
                  "Example.Library/1.0.0": {
                    "dependencies": {
                      "Example.Package": "2.0.0"
                    }
                  },
                  "Example.Package/2.0.0": {}
                }
              },
              "libraries": {
                "Example.App/1.0.0": { "type": "project" },
                "Example.Library/1.0.0": { "type": "project" },
                "Example.Package/2.0.0": { "type": "package" }
              }
            }
            """);

        Assert.True(facts.Graph.IsComplete);
        RuntimeDependencyGraphEdge edge = Assert.Single(facts.Graph.Edges);
        Assert.Equal(
            "example.package",
            edge.Dependency.Coordinate.PackageId);
        Assert.IsType<RuntimeDependencyGraphParentIdentity.Library>(
            edge.Parent);
    }

    [Fact]
    public void Execute_CompileOnlyLibrariesAreExcludedFromRuntimeEvidence()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0"
              },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "Example.App/1.0.0": {
                    "dependencies": {
                      "Compile.Only": "1.0.0",
                      "Runtime.Package": "2.0.0"
                    }
                  },
                  "Compile.Only/1.0.0": {
                    "compileOnly": true,
                    "dependencies": {
                      "Runtime.Package": "2.0.0"
                    }
                  },
                  "Runtime.Package/2.0.0": {}
                }
              },
              "libraries": {
                "Example.App/1.0.0": { "type": "project" },
                "Compile.Only/1.0.0": { "type": "package" },
                "Runtime.Package/2.0.0": { "type": "package" }
              }
            }
            """);

        Assert.True(facts.Graph.IsComplete);
        Assert.Equal(
            "runtime.package",
            Assert.Single(facts.Graph.Packages).Identity.Coordinate.PackageId);
        RuntimeDependencyGraphEdge edge = Assert.Single(facts.Graph.Edges);
        Assert.Equal(
            "runtime.package",
            edge.Dependency.Coordinate.PackageId);
        Assert.IsType<RuntimeDependencyGraphParentIdentity.Library>(
            edge.Parent);
    }

    [Fact]
    public void Execute_InvalidKnownFactsRemainTypedIncompleteEvidence()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0"
              },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "Example.App/1.0.0": {
                    "dependencies": {
                      "Missing.Package": "1.0.0"
                    }
                  },
                  "Broken.Metadata/1.0.0": {}
                }
              },
              "libraries": {
                "Example.App/1.0.0": { "type": "project" }
              }
            }
            """);

        Assert.False(facts.Graph.IsComplete);
        Assert.Empty(facts.Graph.Packages);
        Assert.Empty(facts.Graph.Edges);
        Assert.Contains(
            facts.Graph.Failures,
            failure => failure.Reason
                == RuntimeDependencyGraphFailureReason.InvalidLibraryMetadata);
        Assert.Contains(
            facts.Graph.Failures,
            failure => failure.Reason
                == RuntimeDependencyGraphFailureReason.UnresolvedDependency);
    }

    [Fact]
    public void Execute_InvalidParentsCannotIssueNonPackageRelationships()
    {
        RuntimeDependencyFacts missingMetadata = Available(
            """
            {
              "runtimeTarget": { "name": "net8.0" },
              "targets": {
                "net8.0": {
                  "Missing.Metadata/1.0.0": {
                    "dependencies": {
                      "Example.Package": "1.0.0"
                    }
                  },
                  "Example.Package/1.0.0": {}
                }
              },
              "libraries": {
                "Example.Package/1.0.0": { "type": "package" }
              }
            }
            """);
        RuntimeDependencyFacts invalidPackage = Available(
            """
            {
              "runtimeTarget": { "name": "net8.0" },
              "targets": {
                "net8.0": {
                  "Päckage/1.0.0": {
                    "dependencies": {
                      "Example.Package": "1.0.0"
                    }
                  },
                  "Example.Package/1.0.0": {}
                }
              },
              "libraries": {
                "Päckage/1.0.0": { "type": "package" },
                "Example.Package/1.0.0": { "type": "package" }
              }
            }
            """);

        Assert.Empty(missingMetadata.Graph.Edges);
        Assert.Empty(invalidPackage.Graph.Edges);
        Assert.Contains(
            missingMetadata.Graph.Failures,
            failure => failure.Reason
                == RuntimeDependencyGraphFailureReason.InvalidLibraryMetadata);
        Assert.Contains(
            invalidPackage.Graph.Failures,
            failure => failure.Reason
                == RuntimeDependencyGraphFailureReason.InvalidPackageCoordinate);
        Assert.DoesNotContain(
            "päckage",
            invalidPackage.ManifestIdentity.FactsDigest,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_AmbiguousCanonicalPackageCoordinateIsWithheld()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0"
              },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "Example.Package/1.0.0": {},
                  "example.package/1.0.0": {}
                }
              },
              "libraries": {
                "Example.Package/1.0.0": { "type": "package" },
                "example.package/1.0.0": { "type": "package" }
              }
            }
            """);

        Assert.Empty(facts.Graph.Packages);
        RuntimeDependencyGraphFailure failure = Assert.Single(
            facts.Graph.Failures,
            item => item.Reason
                == RuntimeDependencyGraphFailureReason
                    .AmbiguousPackageCoordinate);
        Assert.Equal(2, failure.Count);
    }

    [Fact]
    public void Execute_InvalidDependenciesShapeIsIncompleteNotEmptySuccess()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0"
              },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "Example.Package/1.0.0": {
                    "dependencies": []
                  }
                }
              },
              "libraries": {
                "Example.Package/1.0.0": { "type": "package" }
              }
            }
            """);

        Assert.Single(facts.Graph.Packages);
        Assert.False(facts.Graph.IsComplete);
        Assert.Equal(
            RuntimeDependencyGraphFailureReason.InvalidDependencyShape,
            Assert.Single(facts.Graph.Failures).Reason);
    }

    [Fact]
    public void Execute_InvalidLocalFactsAreCountedByTypedReason()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": { "name": "net8.0" },
              "targets": {
                "net8.0": {
                  "NoVersion": {
                    "dependencies": {
                      "Bad.One": [],
                      "Bad.Two": ""
                    }
                  }
                }
              },
              "libraries": {
                "NoVersion": { "type": "project" }
              }
            }
            """);

        Assert.Equal(
            1,
            Assert.Single(
                facts.Graph.Failures,
                failure => failure.Reason
                    == RuntimeDependencyGraphFailureReason
                        .InvalidTargetLibraryShape).Count);
        Assert.Equal(
            2,
            Assert.Single(
                facts.Graph.Failures,
                failure => failure.Reason
                    == RuntimeDependencyGraphFailureReason
                        .InvalidDependencyCoordinate).Count);
    }

    [Fact]
    public void Execute_InvalidPackageDependencyNameCannotResolveAfterCaseFolding()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": { "name": "net8.0" },
              "targets": {
                "net8.0": {
                  "Example.App/1.0.0": {
                    "dependencies": {
                      "\u212A": "1.0.0"
                    }
                  },
                  "K/1.0.0": {}
                }
              },
              "libraries": {
                "Example.App/1.0.0": { "type": "project" },
                "K/1.0.0": { "type": "package" }
              }
            }
            """);

        Assert.Single(facts.Graph.Packages);
        Assert.Empty(facts.Graph.Edges);
        Assert.False(facts.Graph.IsComplete);
        Assert.Equal(
            RuntimeDependencyGraphFailureReason.InvalidDependencyCoordinate,
            Assert.Single(facts.Graph.Failures).Reason);
    }

    [Fact]
    public void Execute_CoalescedEdgeRetainsOneActualSourceOccurrence()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": { "name": "net8.0" },
              "targets": {
                "net8.0": {
                  "Example.App/1.0.0": {
                    "dependencies": {
                      "Example.Package": "1.0",
                      "example.package": "1.0.0"
                    }
                  },
                  "Example.Package/1.0.0": {}
                }
              },
              "libraries": {
                "Example.App/1.0.0": { "type": "project" },
                "Example.Package/1.0.0": { "type": "package" }
              }
            }
            """);

        RuntimeDependencyGraphEdge edge = Assert.Single(facts.Graph.Edges);
        Assert.Equal(2, edge.SourceOccurrenceCount);
        Assert.Equal(
            "Example.Package",
            edge.SourceDependencyNameSpelling.ToString());
        Assert.Equal(
            "1.0",
            edge.SourceDependencyVersionSpelling.ToString());
    }

    [Theory]
    [InlineData("{")]
    [InlineData(
        """
        {
          "runtimeTarget": { "name": "net8.0", "name": "net9.0" },
          "targets": {},
          "libraries": {}
        }
        """)]
    public void Execute_MalformedOrDuplicateBearingJsonFailsVisibly(
        string json)
    {
        var failed = Assert.IsType<RuntimeDependencyFactsResult.Failed>(
            Execute(json));

        Assert.Equal(
            RuntimeDependencyFailureReason.MalformedOrDuplicateBearingJson,
            failed.Failure.Reason);
    }

    [Fact]
    public void Execute_SemanticReorderingPreservesIdentityNotContentProvenance()
    {
        const string first =
            """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "Example.App/1.0.0": {
                    "dependencies": {
                      "Example.B": "2.0.0",
                      "Example.A": "1.0.0"
                    }
                  },
                  "Example.A/1.0.0": {},
                  "Example.B/2.0.0": {}
                }
              },
              "libraries": {
                "Example.App/1.0.0": { "type": "project" },
                "Example.A/1.0.0": { "type": "package" },
                "Example.B/2.0.0": { "type": "package" }
              }
            }
            """;
        const string second =
            """
            {
              "libraries": {
                "Example.B/2.0.0": { "type": "package" },
                "Example.A/1.0.0": { "type": "package" },
                "Example.App/1.0.0": { "type": "project" }
              },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "Example.B/2.0.0": {},
                  "Example.A/1.0.0": {},
                  "Example.App/1.0.0": {
                    "dependencies": {
                      "Example.A": "1.0.0",
                      "Example.B": "2.0.0"
                    }
                  }
                }
              },
              "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" }
            }
            """;

        RuntimeDependencyFacts left = Available(first);
        RuntimeDependencyFacts right = Available(second);

        Assert.Equal(left.ManifestIdentity, right.ManifestIdentity);
        Assert.NotEqual(left.ContentProvenance, right.ContentProvenance);
    }

    [Fact]
    public void Execute_HostileTargetAndParentTextRemainInertOrOpaque()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": {
                "name": "hostile\u001b/runtime\u0007"
              },
              "targets": {
                "hostile\u001b/runtime\u0007": {
                  "parent\u001b/1.0.0": {
                    "dependencies": {
                      "Example.Package": "1.0.0"
                    }
                  },
                  "Example.Package/1.0.0": {}
                }
              },
              "libraries": {
                "parent\u001b/1.0.0": { "type": "project" },
                "Example.Package/1.0.0": { "type": "package" }
              }
            }
            """);

        Assert.True(
            RestoredProjectIdentityText.IsOpaque(
                facts.Target.FrameworkIdentity));
        Assert.True(
            RestoredProjectIdentityText.IsOpaque(
                facts.Target.RuntimeIdentifierIdentity!));
        var parent = Assert.IsType<
            RuntimeDependencyGraphParentIdentity.Library>(
                Assert.Single(facts.Graph.Edges).Parent);
        Assert.True(
            RestoredProjectIdentityText.IsOpaque(
                parent.Identity.SourceIdentity));
        Assert.DoesNotContain(
            '\u001b',
            facts.ManifestIdentity.TargetIdentity);
        Assert.True(facts.Target.SourceNameSpelling.WasEncoded);
        Assert.DoesNotContain(
            '\u001b',
            facts.Target.SourceNameSpelling.ToString());
    }

    [Fact]
    public void Execute_FrameworkPackageAbsenceIsStillACompleteRuntimeGraph()
    {
        RuntimeDependencyFacts facts = Available(
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v8.0"
              },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "Example.App/1.0.0": {
                    "dependencies": {
                      "Example.Package": "1.0.0"
                    }
                  },
                  "Example.Package/1.0.0": {}
                }
              },
              "libraries": {
                "Example.App/1.0.0": { "type": "project" },
                "Example.Package/1.0.0": { "type": "package" }
              }
            }
            """);

        Assert.True(facts.Graph.IsComplete);
        Assert.DoesNotContain(
            facts.Graph.Packages,
            package => package.Identity.Coordinate.PackageId.StartsWith(
                "microsoft.netcore.app",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_OversizedManifestFailsWithConfiguredLimit()
    {
        byte[] bytes = new byte[RuntimeDependencyFactsQuery.MaxManifestBytes + 1];

        var failed = Assert.IsType<RuntimeDependencyFactsResult.Failed>(
            RuntimeDependencyFactsQuery.Execute(bytes));

        Assert.Equal(
            RuntimeDependencyFailureReason.ConfiguredLimitExceeded,
            failed.Failure.Reason);
    }

    [Theory]
    [InlineData(RuntimeDependencyFactsQuery.MaxScalarCharacters, false)]
    [InlineData(RuntimeDependencyFactsQuery.MaxScalarCharacters + 1, true)]
    public void Execute_RuntimeTargetScalarBoundIsExact(
        int targetLength,
        bool fails)
    {
        string target = new('x', targetLength);
        RuntimeDependencyFactsResult result = Execute(
            $$"""
            {
              "runtimeTarget": { "name": "{{target}}" },
              "targets": { "{{target}}": {} },
              "libraries": {}
            }
            """);

        if (fails)
        {
            Assert.Equal(
                RuntimeDependencyFailureReason.ConfiguredLimitExceeded,
                Assert.IsType<RuntimeDependencyFactsResult.Failed>(result)
                    .Failure.Reason);
        }
        else
        {
            Assert.IsType<RuntimeDependencyFactsResult.Available>(result);
        }
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(RuntimeDependencyFactsQuery.MaxTargetLibraries, false)]
    [InlineData(RuntimeDependencyFactsQuery.MaxTargetLibraries + 1, true)]
    public void Execute_TargetLibraryBoundIsExact(
        int libraryCount,
        bool fails)
    {
        RuntimeDependencyFactsResult result = Execute(
            CreateLibraryBoundManifest(libraryCount));

        if (fails)
        {
            Assert.Equal(
                RuntimeDependencyFailureReason.ConfiguredLimitExceeded,
                Assert.IsType<RuntimeDependencyFactsResult.Failed>(result)
                    .Failure.Reason);
        }
        else
        {
            Assert.True(
                Assert.IsType<RuntimeDependencyFactsResult.Available>(result)
                    .Value.Graph.IsComplete);
        }
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(RuntimeDependencyFactsQuery.MaxDependencyOccurrences, false)]
    [InlineData(RuntimeDependencyFactsQuery.MaxDependencyOccurrences + 1, true)]
    public void Execute_DependencyOccurrenceBoundIsExact(
        int dependencyCount,
        bool fails)
    {
        RuntimeDependencyFactsResult result = Execute(
            CreateDependencyBoundManifest(dependencyCount));

        if (fails)
        {
            Assert.Equal(
                RuntimeDependencyFailureReason.ConfiguredLimitExceeded,
                Assert.IsType<RuntimeDependencyFactsResult.Failed>(result)
                    .Failure.Reason);
        }
        else
        {
            Assert.True(
                Assert.IsType<RuntimeDependencyFactsResult.Available>(result)
                    .Value.Graph.IsComplete);
        }
    }

    [Theory]
    [InlineData(RuntimeDependencyFactsQuery.MaxFailureOccurrences, false)]
    [InlineData(RuntimeDependencyFactsQuery.MaxFailureOccurrences + 1, true)]
    public void Execute_FailureOccurrenceBoundIsExact(
        int failureCount,
        bool fails)
    {
        RuntimeDependencyFactsResult result = Execute(
            CreateFailureBoundManifest(failureCount));

        if (fails)
        {
            Assert.Equal(
                RuntimeDependencyFailureReason.ConfiguredLimitExceeded,
                Assert.IsType<RuntimeDependencyFactsResult.Failed>(result)
                    .Failure.Reason);
        }
        else
        {
            RuntimeDependencyFacts facts =
                Assert.IsType<RuntimeDependencyFactsResult.Available>(result)
                    .Value;
            Assert.Equal(
                failureCount,
                Assert.Single(facts.Graph.Failures).Count);
        }
    }

    private static string CreateLibraryBoundManifest(int libraryCount)
    {
        var target = new StringBuilder();
        var libraries = new StringBuilder();
        for (int index = 0; index < libraryCount; index++)
        {
            if (index > 0)
            {
                target.Append(',');
                libraries.Append(',');
            }

            string key = $"Project{index}/1.0.0";
            target.Append('"').Append(key).Append("\":{}");
            libraries.Append('"').Append(key)
                .Append("\":{\"type\":\"project\"}");
        }

        return $$"""
            {
              "runtimeTarget": { "name": "net8.0" },
              "targets": { "net8.0": { {{target}} } },
              "libraries": { {{libraries}} }
            }
            """;
    }

    private static string CreateDependencyBoundManifest(int dependencyCount)
    {
        int parentCount = Math.Min(
            RuntimeDependencyFactsQuery.MaxTargetLibraries - 2,
            (dependencyCount + 1) / 2);
        int threeDependencyParents = dependencyCount - (parentCount * 2);
        int remaining = dependencyCount;
        var target = new StringBuilder();
        var libraries = new StringBuilder();
        for (int index = 0; index < parentCount; index++)
        {
            if (index > 0)
            {
                target.Append(',');
                libraries.Append(',');
            }

            string key = $"Project{index}/1.0.0";
            int count = Math.Min(
                remaining,
                index < threeDependencyParents ? 3 : 2);
            remaining -= count;
            target.Append('"').Append(key).Append("\":{\"dependencies\":{");
            string[] names = ["Example.A", "Example.B", "example.a"];
            for (int dependency = 0; dependency < count; dependency++)
            {
                if (dependency > 0)
                    target.Append(',');
                string name = names[dependency];
                string version = name.Equals(
                    "Example.B",
                    StringComparison.Ordinal)
                        ? "2.0.0"
                        : "1.0.0";
                target.Append('"').Append(name).Append("\":\"")
                    .Append(version).Append('"');
            }

            target.Append("}}");
            libraries.Append('"').Append(key)
                .Append("\":{\"type\":\"project\"}");
        }

        Assert.Equal(0, remaining);
        if (parentCount > 0)
        {
            target.Append(',');
            libraries.Append(',');
        }

        target.Append(
            "\"Example.A/1.0.0\":{},\"Example.B/2.0.0\":{}");
        libraries.Append(
            "\"Example.A/1.0.0\":{\"type\":\"package\"},"
            + "\"Example.B/2.0.0\":{\"type\":\"package\"}");
        return $$"""
            {
              "runtimeTarget": { "name": "net8.0" },
              "targets": { "net8.0": { {{target}} } },
              "libraries": { {{libraries}} }
            }
            """;
    }

    private static string CreateFailureBoundManifest(int failureCount)
    {
        var dependencies = new StringBuilder();
        for (int index = 0; index < failureCount; index++)
        {
            if (index > 0)
                dependencies.Append(',');
            dependencies.Append("\"Invalid").Append(index)
                .Append("\":\"not-a-version\"");
        }

        return $$"""
            {
              "runtimeTarget": { "name": "net8.0" },
              "targets": {
                "net8.0": {
                  "Example.App/1.0.0": {
                    "dependencies": { {{dependencies}} }
                  }
                }
              },
              "libraries": {
                "Example.App/1.0.0": { "type": "project" }
              }
            }
            """;
    }

    private static RuntimeDependencyFactsResult Execute(string json) =>
        RuntimeDependencyFactsQuery.Execute(Encoding.UTF8.GetBytes(json));

    private static RuntimeDependencyFacts Available(string json) =>
        Assert.IsType<RuntimeDependencyFactsResult.Available>(
            Execute(json)).Value;
}
