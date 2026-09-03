using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;

namespace DotnetInspector.Queries.Tests;

public sealed class RestoredProjectDependencyFactsQueryTests
{
    const string Bidi = "\u202E";

    // ---- .csproj locator vs direct-assets byte and semantic identity ----

    [Fact]
    public void Execute_CsprojLocatorAndDirectAssetsAreByteAndSemanticallyIdentical()
    {
        string projectDirectory = FixtureCatalog.RestoredProjectDependencyFacts.ProjectDirectory();
        string csproj = Path.Combine(projectDirectory, "DotnetInspector.RestoredProjectFixtures.csproj");
        Assert.True(File.Exists(csproj));

        Assert.True(
            ProjectAssetsParser.TryFindAssets(csproj, out string? fromCsproj, out ProjectAssetsStatus csprojStatus));
        Assert.Equal(ProjectAssetsStatus.Found, csprojStatus);

        Assert.True(
            ProjectAssetsParser.TryFindAssets(
                projectDirectory,
                out string? fromDirectory,
                out ProjectAssetsStatus directoryStatus));
        Assert.Equal(ProjectAssetsStatus.Found, directoryStatus);
        Assert.Equal(fromCsproj, fromDirectory);

        byte[] locatorBytes = File.ReadAllBytes(fromCsproj!);
        byte[] directBytes = ReadCopiedAssetsBytes();
        Assert.Equal(locatorBytes, directBytes);

        RestoredProjectDependencyFacts fromLocator = Available(RestoredProjectDependencyFactsQuery.Execute(locatorBytes));
        RestoredProjectDependencyFacts fromDirect = Available(RestoredProjectDependencyFactsQuery.Execute(directBytes));

        Assert.Equal(fromLocator.ContentProvenance.Sha256, fromDirect.ContentProvenance.Sha256);
        Assert.Equal(fromLocator.SelectionIdentity, fromDirect.SelectionIdentity);
        Assert.Equal(Describe(fromLocator), Describe(fromDirect));
        Assert.Equal(SHA256.HashData(locatorBytes), Convert.FromHexString(fromLocator.ContentProvenance.Sha256));
    }

    // ---- Fixture integrity: the manifest sidecar carries the same seed ----

    [Fact]
    public void Fixture_NuspecDependencyGroupsMatchRestoredDeclarationGroupsExactly()
    {
        byte[] nuspecBytes = File.ReadAllBytes(
            FixtureCatalog.RestoredProjectDependencyFacts.AssetPath("manifest.nuspec"));
        PackageManifestFactsResult manifestResult = PackageManifestFactsQuery.ExecuteSelfAttested(nuspecBytes);
        PackageManifestFacts manifest =
            Assert.IsType<PackageManifestFactsResult.Available>(manifestResult).Value;

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(ReadCopiedAssetsBytes()));
        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.True(declaration.IsComplete);

        // The two inputs are compared here, in the fixture-integrity gate, and never inside a
        // query: neither owner composes the other's evidence.
        string[] manifestGroups =
            [.. manifest.DependencyGroups
                .Select(group => Describe(
                    group.TargetFramework.ToLowerInvariant(),
                    group.Dependencies.Select(d => (d.Id, d.VersionRange))))
                .OrderBy(text => text, StringComparer.Ordinal)];
        string[] restoredGroups =
            [.. declaration.Groups
                .Select(group => Describe(
                    group.Identity.PivotIdentity,
                    group.Packages.Select(p => (p.CanonicalPackageId, p.CanonicalVersionConstraint))))
                .OrderBy(text => text, StringComparer.Ordinal)];

        Assert.Equal(restoredGroups, manifestGroups);
        Assert.Equal(2, restoredGroups.Length);

        static string Describe(string framework, IEnumerable<(string Id, string Range)> dependencies) =>
            framework.ToLowerInvariant()
            + " => "
            + string.Join(
                ", ",
                dependencies
                    .Select(d => $"{d.Id.ToLowerInvariant()}@{VersionRange.Parse(d.Range).ToNormalizedString()}")
                    .OrderBy(text => text, StringComparer.Ordinal));
    }

    // ---- Target selection: default, exact framework, framework+RID -------

    [Fact]
    public void Execute_DefaultSelection_PrefersHigherPriorityNonRuntimeTarget()
    {
        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(ReadCopiedAssetsBytes()));

        Assert.NotNull(facts.SelectedTarget);
        Assert.Equal("net11.0", facts.SelectedTarget!.FrameworkIdentity);
        Assert.Null(facts.SelectedTarget.RuntimeIdentifierIdentity);
        Assert.Equal(RestoredProjectTargetSelectionProvenance.Default, facts.SelectedTarget.Provenance);
        Assert.Equal("net11.0", facts.SelectionIdentity.TargetIdentity);
    }

    [Fact]
    public void Execute_EqualPriorityDefaultTargets_TieBreakByOpaqueIdentity()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["unknown0"] = new JsonObject(),
                ["unknown4"] = new JsonObject(),
            },
            rootGroups: new JsonObject
            {
                ["unknown0"] = new JsonArray(),
                ["unknown4"] = new JsonArray(),
            },
            frameworks: new JsonObject
            {
                ["unknown0"] = new JsonObject(),
                ["unknown4"] = new JsonObject(),
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));
        RestoredProjectDependencyFacts reordered = Available(
            RestoredProjectDependencyFactsQuery.Execute(WithReversedPropertyOrder(bytes)));

        string expectedIdentity = "sha256:" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes("unknown4")));
        Assert.Equal(expectedIdentity, facts.SelectedTarget!.FrameworkIdentity);
        Assert.Equal("unknown4", facts.SelectedTarget.SourceFrameworkSpelling.ToString());
        Assert.Equal(Describe(facts), Describe(reordered));
        Assert.Equal(facts.SelectionIdentity, reordered.SelectionIdentity);
    }

    [Fact]
    public void Execute_ExactFrameworkRequest_SelectsRequestedTargetWithSameSemanticIdentityAsDefault()
    {
        byte[] bytes = ReadCopiedAssetsBytes();
        RestoredProjectDependencyFacts defaultFacts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));
        RestoredProjectDependencyFacts requestedFacts = Available(
            RestoredProjectDependencyFactsQuery.Execute(bytes, new RestoredProjectTargetRequest("net11.0")));

        Assert.Equal(RestoredProjectTargetSelectionProvenance.Requested, requestedFacts.SelectedTarget!.Provenance);
        Assert.Equal(defaultFacts.SelectionIdentity, requestedFacts.SelectionIdentity);
        Assert.NotEqual(defaultFacts.SelectedTarget!.Provenance, requestedFacts.SelectedTarget.Provenance);
    }

    [Fact]
    public void Execute_FrameworkOnlyRequest_ExcludesGeneratedRuntimeSpecificTarget()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                ReadCopiedAssetsBytes(),
                new RestoredProjectTargetRequest("net11.0")));

        Assert.Equal("net11.0", facts.SelectedTarget!.FrameworkIdentity);
        Assert.Null(facts.SelectedTarget.RuntimeIdentifierIdentity);
    }

    [Fact]
    public void Execute_FrameworkAndRuntimeRequest_SelectsGeneratedRuntimeSpecificTarget()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                ReadCopiedAssetsBytes(),
                new RestoredProjectTargetRequest("net11.0", "linux-x64")));

        Assert.Equal("net11.0", facts.SelectedTarget!.FrameworkIdentity);
        Assert.Equal("linux-x64", facts.SelectedTarget.RuntimeIdentifierIdentity);
        Assert.Equal("net11.0/linux-x64", facts.SelectionIdentity.TargetIdentity);
        Assert.Equal(RestoredProjectTargetSelectionProvenance.Requested, facts.SelectedTarget.Provenance);
        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
        Assert.Contains(graph.Packages, p => p.Identity.Coordinate.PackageId == "nuget.packaging");
    }

    [Fact]
    public void Execute_OnlyRuntimeSpecificTargets_SelectsHighestPriorityRuntimeTarget()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                JsonObject targets = root["targets"]!.AsObject();
                targets.Remove("net10.0");
                targets.Remove("net11.0");
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(mutated));

        Assert.Equal("net11.0", facts.SelectedTarget!.FrameworkIdentity);
        Assert.Equal("linux-x64", facts.SelectedTarget.RuntimeIdentifierIdentity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_CaseOnlyDuplicateTargetPivots_FailGraphRatherThanFollowJsonOrder(bool reversed)
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                JsonObject targets = root["targets"]!.AsObject();
                JsonNode clone = targets["net11.0"]!.DeepClone()!;
                targets.Add("NET11.0", clone);
            });
        if (reversed)
            mutated = WithReversedPropertyOrder(mutated);

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(mutated));

        Assert.Null(facts.SelectedTarget);
        RestoredProjectGraphResult.Failed failed = Assert.IsType<RestoredProjectGraphResult.Failed>(facts.Graph);
        Assert.Equal(RestoredProjectGraphFailureReason.AmbiguousTargetIdentity, failed.Failure.Reason);

        // The declaration phase is independent of target-identity ambiguity.
        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.True(declaration.IsComplete);
        Assert.NotEmpty(declaration.Groups);
    }

    // ---- Schema version 3: normalized request and pivot correlation -------

    [Fact]
    public void Execute_SchemaVersion3_ShortRequestSelectsLongFormTargetPivot()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                SchemaVersion3Document(),
                new RestoredProjectTargetRequest("net11.0")));

        Assert.Equal("net11.0", facts.SelectedTarget!.FrameworkIdentity);
        Assert.Equal(
            ".NETCoreApp,Version=v11.0",
            facts.SelectedTarget.SourceFrameworkSpelling.ToString());
        Assert.Equal(RestoredProjectTargetSelectionProvenance.Requested, facts.SelectedTarget.Provenance);
    }

    [Theory]
    [InlineData("UAP,Version=v10.0", "uap10.0")]
    [InlineData("MonoAndroid,Version=v10.0", "monoandroid10.0")]
    [InlineData(".NETPortable,Version=v0.0,Profile=Profile7", "portable-net45+win8")]
    [InlineData("Xamarin.iOS,Version=v1.0", "xamarinios10")]
    public void Execute_SchemaVersion3_ShortRequestCorrelatesWithNuGetLongForm(
        string longFramework,
        string shortFramework)
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                [longFramework] = new JsonObject(),
            },
            rootGroups: new JsonObject
            {
                [longFramework] = new JsonArray(),
            },
            frameworks: new JsonObject
            {
                [shortFramework] = new JsonObject
                {
                    ["dependencies"] = new JsonObject(),
                },
            },
            version: 3);

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                bytes,
                new RestoredProjectTargetRequest(shortFramework)));

        Assert.Equal(shortFramework, facts.SelectedTarget!.FrameworkIdentity);
        Assert.Equal(
            shortFramework,
            Assert.Single(
                Assert.IsType<RestoredProjectDeclarationResult.Available>(
                    facts.Declaration).Groups).FrameworkIdentity.Identity);
        Assert.True(
            Assert.IsType<RestoredProjectGraphResult.Available>(
                facts.Graph).IsComplete);
    }

    [Theory]
    [InlineData(
        ".NETCoreApp,Version=v8.0,Profile=bogus",
        "net8.0")]
    [InlineData(
        ".NETCoreApp,Version=v8.0,Platform=unsupportedos,PlatformVersion=1.0",
        "net8.0-unsupportedos1.0")]
    [InlineData(
        ".NETCoreApp,Version=v8.0,Platform=unsupported",
        "net8.0-unsupported")]
    [InlineData(
        ".NETCoreApp,Version=v8",
        "net8.0")]
    [InlineData(
        ".NETPortable,Version=v0.0,Profile=net45+win8",
        "portable-net45+win8")]
    public void Execute_SchemaVersion3_NuGetLongFormSemanticsRemainCanonical(
        string longFramework,
        string shortFramework)
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                [longFramework] = new JsonObject(),
            },
            rootGroups: new JsonObject
            {
                [longFramework] = new JsonArray(),
            },
            frameworks: new JsonObject
            {
                [shortFramework] = new JsonObject
                {
                    ["dependencies"] = new JsonObject(),
                },
            },
            version: 3);

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                bytes,
                new RestoredProjectTargetRequest(shortFramework)));

        Assert.Equal(shortFramework, facts.SelectedTarget!.FrameworkIdentity);
        Assert.True(
            Assert.IsType<RestoredProjectGraphResult.Available>(
                facts.Graph).IsComplete);
    }

    [Fact]
    public void Execute_SchemaVersion3_OpaqueTargetDoesNotCollideWithCanonicalTarget()
    {
        const string canonicalFramework = ".NETCoreApp,Version=v8.0";
        const string malformedFramework =
            ".NETCoreApp,Version=v8.0,Unknown=value";
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                [canonicalFramework] = new JsonObject(),
                [malformedFramework] = new JsonObject(),
            },
            rootGroups: new JsonObject
            {
                [canonicalFramework] = new JsonArray(),
            },
            frameworks: new JsonObject
            {
                ["net8.0"] = new JsonObject
                {
                    ["dependencies"] = new JsonObject(),
                },
            },
            version: 3);

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                bytes,
                new RestoredProjectTargetRequest("net8.0")));
        RestoredProjectDependencyFacts reordered = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                WithReversedPropertyOrder(bytes),
                new RestoredProjectTargetRequest("net8.0")));

        Assert.Equal("net8.0", facts.SelectedTarget!.FrameworkIdentity);
        Assert.True(
            Assert.IsType<RestoredProjectGraphResult.Available>(
                facts.Graph).IsComplete);
        Assert.Equal(Describe(facts), Describe(reordered));
        Assert.Equal(facts.SelectionIdentity, reordered.SelectionIdentity);
    }

    [Fact]
    public void Execute_SchemaVersion3_AmbiguousPlatformSpellingDoesNotMatchAnotherIdentity()
    {
        const string longFramework =
            ".NETCoreApp,Version=v8.0,Platform=foo2,PlatformVersion=1.0";
        const string differentShortFramework = "net8.0-foo21.0";
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                [longFramework] = new JsonObject(),
            },
            rootGroups: new JsonObject
            {
                [longFramework] = new JsonArray(),
            },
            frameworks: new JsonObject
            {
                [differentShortFramework] = new JsonObject
                {
                    ["dependencies"] = new JsonObject(),
                },
            },
            version: 3);

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                bytes,
                new RestoredProjectTargetRequest(differentShortFramework)));

        Assert.Null(facts.SelectedTarget);
        Assert.IsType<RestoredProjectGraphResult.Unavailable>(facts.Graph);
    }

    [Fact]
    public void Execute_SchemaVersion3_CorrelatesShortDeclarationPivotWithLongTargetPivot()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                SchemaVersion3Document(),
                new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        RestoredProjectDeclarationGroup group = Assert.Single(declaration.Groups);
        Assert.Equal("net11.0", group.Identity.PivotIdentity);
        Assert.Equal(RestoredProjectFrameworkIdentityKind.Recognized, group.FrameworkIdentity.Kind);
        Assert.Equal("net11.0", group.FrameworkIdentity.Identity);
    }

    [Fact]
    public void Execute_SchemaVersion3_CorrelatesLongRootGroupPivotAndProjectsGraph()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                SchemaVersion3Document(),
                new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
        RestoredProjectPackageNode foo = Assert.Single(graph.Packages, p => p.Identity.Coordinate.PackageId == "foo");
        Assert.Equal(RestoredProjectDependencyRole.Direct, foo.Role);
        RestoredProjectPackageNode bar = Assert.Single(graph.Packages, p => p.Identity.Coordinate.PackageId == "bar");
        Assert.Equal(RestoredProjectDependencyRole.Transitive, bar.Role);
    }

    // ---- Groups, ranges, and valid empty groups --------------------------

    [Fact]
    public void Execute_DeclarationGroups_RetainAuthoredRangesPerFramework()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                ReadCopiedAssetsBytes(),
                new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.True(declaration.IsComplete);

        RestoredProjectDeclarationGroup net11Group = Assert.Single(
            declaration.Groups,
            g => g.Identity.PivotIdentity == "net11.0");
        RestoredProjectDeclaredPackage nugetPackaging = Assert.Single(
            net11Group.Packages,
            p => p.CanonicalPackageId == "nuget.packaging");
        Assert.Equal("[7.0.3, )", nugetPackaging.CanonicalVersionConstraint);
        Assert.Equal("NuGet.Packaging", nugetPackaging.SourcePackageIdSpelling.ToString());
    }

    [Fact]
    public void Execute_EmptyDependenciesObject_ProjectsValidEmptyGroup()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["project"]!["frameworks"]!["net10.0"]!["dependencies"] = new JsonObject());

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net10.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        RestoredProjectDeclarationGroup group = Assert.Single(
            declaration.Groups,
            g => g.Identity.PivotIdentity == "net10.0");
        Assert.Empty(group.Packages);
        Assert.True(declaration.IsComplete);
    }

    [Fact]
    public void Execute_AbsentDeclarationDependencies_ProjectsValidEmptyGroup()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["project"]!["frameworks"]!["net10.0"]!.AsObject().Remove("dependencies"));

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.True(declaration.IsComplete);
        RestoredProjectDeclarationGroup group = Assert.Single(
            declaration.Groups,
            g => g.Identity.PivotIdentity == "net10.0");
        Assert.Empty(group.Packages);
    }

    [Fact]
    public void Execute_NonObjectDeclarationDependencies_LeavesDeclarationIncomplete()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["project"]!["frameworks"]!["net10.0"]!["dependencies"] = "not-an-object");

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.False(declaration.IsComplete);
        Assert.Equal(RestoredProjectPhaseCompletion.Incomplete, declaration.Completion);
        Assert.Contains(
            declaration.Failures,
            f => f.Reason == RestoredProjectDeclarationFailureReason.InvalidGroupShape);

        // The unrelated declaration failure leaves the selected graph fully usable.
        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
    }

    [Theory]
    [InlineData("""{ "version": "[1.0.0, )" }""")]
    [InlineData("""{ "target": "Reference", "version": "[1.0.0, )" }""")]
    [InlineData("""{ "target": 7, "version": "[1.0.0, )" }""")]
    public void Execute_DependencyWithoutExplicitPackageOrProjectTarget_IsInvalidDeclarationEvidence(string entryJson)
    {
        var frameworks = new JsonObject
        {
            ["net11.0"] = new JsonObject
            {
                ["dependencies"] = new JsonObject { ["Unclassified.Package"] = JsonNode.Parse(entryJson) },
            },
        };

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(DocumentWithFrameworks(frameworks)));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.False(declaration.IsComplete);
        RestoredProjectDeclarationFailure failure = Assert.Single(declaration.Failures);
        Assert.Equal(RestoredProjectDeclarationFailureReason.UnclassifiedDependencyTarget, failure.Reason);
        Assert.Empty(Assert.Single(declaration.Groups).Packages);
    }

    [Fact]
    public void Execute_ProjectReferences_DoNotConsumeThePackageDeclarationBound()
    {
        var dependencies = new JsonObject();
        for (int i = 0; i < RestoredProjectDependencyFactsQuery.MaxDeclaredPackages; i++)
        {
            dependencies.Add(
                $"Referenced.Project{i}",
                new JsonObject { ["target"] = "Project", ["version"] = "[1.0.0, )" });
        }

        dependencies.Add("Real.Package", new JsonObject { ["target"] = "Package", ["version"] = "[2.1.0, )" });

        var frameworks = new JsonObject
        {
            ["net11.0"] = new JsonObject { ["dependencies"] = dependencies },
        };

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(DocumentWithFrameworks(frameworks)));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.True(declaration.IsComplete);
        RestoredProjectDeclaredPackage package = Assert.Single(Assert.Single(declaration.Groups).Packages);
        Assert.Equal("real.package", package.CanonicalPackageId);
    }

    [Fact]
    public void Execute_MorePackageDeclarationsThanTheBound_LeavesDeclarationIncomplete()
    {
        var dependencies = new JsonObject();
        for (int index = 0; index <= RestoredProjectDependencyFactsQuery.MaxDeclaredPackages; index++)
        {
            dependencies.Add(
                $"Package{index:D5}",
                new JsonObject { ["target"] = "Package", ["version"] = "[1.0.0, )" });
        }

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                DocumentWithFrameworks(
                    new JsonObject
                    {
                        ["net11.0"] = new JsonObject { ["dependencies"] = dependencies },
                    })));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.False(declaration.IsComplete);
        Assert.Equal(
            RestoredProjectDependencyFactsQuery.MaxDeclaredPackages,
            Assert.Single(declaration.Groups).Packages.Length);
        Assert.Contains(
            declaration.Failures,
            failure => failure.Reason == RestoredProjectDeclarationFailureReason.ConfiguredLimitExceeded);
    }

    [Fact]
    public void Execute_MoreProjectReferencesThanTheBound_LeavesDeclarationIncomplete()
    {
        var dependencies = new JsonObject();
        for (int index = 0; index <= RestoredProjectDependencyFactsQuery.MaxDeclaredProjectReferences; index++)
        {
            dependencies.Add(
                $"Project{index:D5}",
                new JsonObject { ["target"] = "Project" });
        }

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                DocumentWithFrameworks(
                    new JsonObject
                    {
                        ["net11.0"] = new JsonObject { ["dependencies"] = dependencies },
                    })));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.False(declaration.IsComplete);
        Assert.Empty(Assert.Single(declaration.Groups).Packages);
        Assert.Contains(
            declaration.Failures,
            failure => failure.Reason == RestoredProjectDeclarationFailureReason.ConfiguredLimitExceeded);
    }

    [Fact]
    public void Execute_CaseOnlyDeclarationPivots_StayDistinctGroupsAndOnlyFailRootCorrelation()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                JsonObject frameworks = root["project"]!["frameworks"]!.AsObject();
                frameworks.Add("NET11.0", frameworks["net11.0"]!.DeepClone());
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.True(declaration.IsComplete);
        Assert.Equal(3, declaration.Groups.Length);
        Assert.Equal(
            declaration.Groups.Length,
            declaration.Groups.Select(g => g.Identity.PivotIdentity).Distinct(StringComparer.Ordinal).Count());

        RestoredProjectGraphResult.Failed failed = Assert.IsType<RestoredProjectGraphResult.Failed>(facts.Graph);
        Assert.Equal(RestoredProjectGraphFailureReason.AmbiguousRootCorrelation, failed.Failure.Reason);
    }

    // ---- Direct, transitive, and diamond edge shape -----------------------

    [Fact]
    public void Execute_Net11Graph_HasDirectTransitiveAndDiamondEdges()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                ReadCopiedAssetsBytes(),
                new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);

        RestoredProjectPackageNode markdownTable = Assert.Single(
            graph.Packages,
            p => p.Identity.Coordinate.PackageId == "markdowntable.formatting");
        Assert.Equal(RestoredProjectDependencyRole.Direct, markdownTable.Role);

        RestoredProjectPackageNode nugetVersioning = Assert.Single(
            graph.Packages,
            p => p.Identity.Coordinate.PackageId == "nuget.versioning");
        Assert.Equal(RestoredProjectDependencyRole.Transitive, nugetVersioning.Role);

        RestoredProjectGraphEdge[] diamondEdges =
            [.. graph.Edges.Where(e => e.Dependency.Coordinate.PackageId == "nuget.versioning")];
        Assert.True(diamondEdges.Length >= 2, "Expected at least two distinct parent edges converging on NuGet.Versioning.");
        Assert.Contains(diamondEdges, e => e.Parent is RestoredProjectGraphParentIdentity.Package);
        Assert.Contains(diamondEdges, e => e.Parent is RestoredProjectGraphParentIdentity.Project);
        Assert.All(diamondEdges, e => Assert.Equal(RestoredProjectDependencyRole.Transitive, e.Role));
    }

    [Fact]
    public void Execute_ReachableNodeWithoutDependencies_IsAValidLeaf()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                ReadCopiedAssetsBytes(),
                new RestoredProjectTargetRequest("net10.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
        Assert.Equal(RestoredProjectPhaseCompletion.Complete, graph.Completion);
        RestoredProjectPackageNode leaf = Assert.Single(graph.Packages);
        Assert.Equal("microsoft.codeanalysis.bannedapianalyzers", leaf.Identity.Coordinate.PackageId);
        Assert.Equal(RestoredProjectDependencyRole.Direct, leaf.Role);
    }

    [Fact]
    public void Execute_ReachableNodeWithNonObjectDependencies_IsIncompleteNotSilentlyComplete()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["targets"]!["net10.0"]!["Microsoft.CodeAnalysis.BannedApiAnalyzers/5.6.0"]!["dependencies"] =
                new JsonArray());

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net10.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        RestoredProjectGraphFailure failure = Assert.Single(graph.Failures);
        Assert.Equal(RestoredProjectGraphFailureReason.InvalidNodeShape, failure.Reason);
        Assert.Single(graph.Packages);
    }

    // ---- Complete-empty, incomplete, unavailable, and failed graph --------

    [Fact]
    public void Execute_NoRootEntries_ProjectsCompleteEmptyGraph()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["projectFileDependencyGroups"]!["net10.0"] = new JsonArray());

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net10.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.Empty(graph.Packages);
        Assert.Empty(graph.Edges);
        Assert.True(graph.IsComplete);
    }

    [Fact]
    public void Execute_MissingTargetNodeForRootEntry_ProjectsIncompleteGraph()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["targets"]!["net11.0"]!.AsObject().Remove("NuGet.Packaging/7.0.3"));

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Contains(
            graph.Failures,
            f => f.Reason == RestoredProjectGraphFailureReason.UnresolvedRootEntry);
        Assert.DoesNotContain(graph.Packages, p => p.Identity.Coordinate.PackageId == "nuget.packaging");
    }

    [Fact]
    public void Execute_UnmatchedRequestedFramework_ProjectsUnavailableGraph()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                ReadCopiedAssetsBytes(),
                new RestoredProjectTargetRequest("net12.0")));

        Assert.Null(facts.SelectedTarget);
        Assert.IsType<RestoredProjectGraphResult.Unavailable>(facts.Graph);
    }

    [Fact]
    public void Execute_AmbiguousRootDependencyGroupPivot_ProjectsFailedGraph()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                JsonArray original = root["projectFileDependencyGroups"]!["net11.0"]!.AsArray();
                root["projectFileDependencyGroups"]!.AsObject().Add("NET11.0", original.DeepClone());
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectGraphResult.Failed failed = Assert.IsType<RestoredProjectGraphResult.Failed>(facts.Graph);
        Assert.Equal(RestoredProjectGraphFailureReason.AmbiguousRootCorrelation, failed.Failure.Reason);
    }

    // ---- Root-entry parsing ------------------------------------------------

    [Fact]
    public void Execute_RootEntryWithoutRangeMarker_ResolvesAsAWholeName()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                var entries = new JsonArray();
                foreach (JsonNode? entry in root["projectFileDependencyGroups"]!["net10.0"]!.AsArray())
                {
                    string text = entry!.GetValue<string>();
                    int marker = text.LastIndexOf(" >= ", StringComparison.Ordinal);
                    entries.Add(marker < 0 ? text : text[..marker]);
                }

                root["projectFileDependencyGroups"]!["net10.0"] = entries;
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net10.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
        Assert.Single(graph.Packages);
    }

    [Fact]
    public void Execute_RootEntryUsesRightmostRangeMarker()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                // The literal marker is legal inside a range spelling, so only the rightmost one
                // can be the separator.
                root["projectFileDependencyGroups"]!["net10.0"] =
                    new JsonArray("Microsoft.CodeAnalysis.BannedApiAnalyzers >= 5.6.0 >= 5.6.0");
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net10.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Contains(graph.Failures, f => f.Reason == RestoredProjectGraphFailureReason.UnresolvedRootEntry);
    }

    // ---- Edge identity uniqueness -----------------------------------------

    [Fact]
    public void Execute_RepeatedEqualRootEntries_CoalesceIntoOneEdge()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["projectFileDependencyGroups"]!["net10.0"]!.AsArray()
                .Add("microsoft.codeanalysis.bannedapianalyzers >= 5.6.0"));

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net10.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
        Assert.Single(graph.Edges);
    }

    [Fact]
    public void Execute_EqualSemanticConstraintSpellings_CoalesceDeterministically()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["Foo/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("Foo >= 1.0.0") },
            frameworks: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["dependencies"] = new JsonObject
                    {
                        ["Foo"] = new JsonObject { ["target"] = "Package", ["version"] = "1.0.0" },
                        ["foo"] = new JsonObject { ["target"] = "Package", ["version"] = "[1.0.0, )" },
                    },
                },
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));
        RestoredProjectDependencyFacts reordered = Available(
            RestoredProjectDependencyFactsQuery.Execute(WithReversedPropertyOrder(bytes)));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
        RestoredProjectGraphEdge edge = Assert.Single(graph.Edges);
        Assert.Equal("[1.0.0, )", edge.CanonicalVersionConstraint);
        Assert.Equal("1.0.0", edge.SourceVersionConstraintSpelling.ToString());
        Assert.Equal(Describe(facts), Describe(reordered));
        Assert.Equal(facts.SelectionIdentity, reordered.SelectionIdentity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_ConflictingConstraintsForOneEdge_EmitNoEdgeAndLeaveGraphIncomplete(bool reversed)
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["project"]!["frameworks"]!["net10.0"]!["dependencies"]!.AsObject()
                .Add(
                    "microsoft.codeanalysis.bannedapianalyzers",
                    new JsonObject { ["target"] = "Package", ["version"] = "[9.9.9, )" }));
        if (reversed)
            mutated = WithReversedPropertyOrder(mutated);

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net10.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Contains(graph.Failures, f => f.Reason == RestoredProjectGraphFailureReason.ConflictingEdgeConstraint);
        Assert.Empty(graph.Edges);
        RestoredProjectPackageNode package = Assert.Single(graph.Packages);
        Assert.Equal(RestoredProjectDependencyRole.Direct, package.Role);
    }

    // ---- Declaration and graph phases fail independently ------------------

    [Fact]
    public void Execute_UnrelatedDeclarationConflict_DoesNotAffectSelectedGraphCompleteness()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                var conflicting = new JsonObject
                {
                    ["dependencies"] = new JsonObject
                    {
                        ["Conflicting.Package"] = new JsonObject { ["target"] = "Package", ["version"] = "[1.0.0, )" },
                        ["conflicting.package"] = new JsonObject { ["target"] = "Package", ["version"] = "[2.0.0, )" },
                    },
                };
                root["project"]!["frameworks"]!.AsObject().Add("net12.0-unrelated", conflicting);
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.False(declaration.IsComplete);
        Assert.Contains(
            declaration.Failures,
            f => f.Reason == RestoredProjectDeclarationFailureReason.ConflictingPackageDeclaration);

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
    }

    [Fact]
    public void Execute_FailedDeclarationCapability_LeavesGraphUnavailableNotFailed()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["project"]!["frameworks"] = new JsonArray());

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Failed failed =
            Assert.IsType<RestoredProjectDeclarationResult.Failed>(facts.Declaration);
        Assert.Equal(RestoredProjectDeclarationFailureReason.InvalidGroupShape, failed.Failure.Reason);

        // Root package constraints genuinely cannot be established, so the graph is unavailable
        // rather than inheriting the declaration phase's failure.
        Assert.IsType<RestoredProjectGraphResult.Unavailable>(facts.Graph);
        Assert.NotNull(facts.SelectedTarget);
    }

    [Fact]
    public void Execute_UnavailableGraph_DoesNotAffectDeclarationCompleteness()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                ReadCopiedAssetsBytes(),
                new RestoredProjectTargetRequest("net12.0")));

        Assert.IsType<RestoredProjectGraphResult.Unavailable>(facts.Graph);
        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.True(declaration.IsComplete);
        Assert.NotEmpty(declaration.Groups);
    }

    // ---- Malformed, duplicate-bearing, hostile, and limit inputs ----------

    [Fact]
    public void Execute_MalformedJson_FailsVisibly()
    {
        RestoredProjectDependencyFactsResult result = RestoredProjectDependencyFactsQuery.Execute(
            Encoding.UTF8.GetBytes("{ not json"));

        RestoredProjectDependencyFactsResult.Failed failed = Assert.IsType<RestoredProjectDependencyFactsResult.Failed>(result);
        Assert.Equal(RestoredProjectDependencyFailureReason.MalformedOrDuplicateBearingJson, failed.Failure.Reason);
    }

    [Fact]
    public void Execute_DuplicateTopLevelProperty_FailsVisibly()
    {
        RestoredProjectDependencyFactsResult result = RestoredProjectDependencyFactsQuery.Execute(
            Encoding.UTF8.GetBytes(
                """
                { "version": 4, "version": 4 }
                """));

        RestoredProjectDependencyFactsResult.Failed failed = Assert.IsType<RestoredProjectDependencyFactsResult.Failed>(result);
        Assert.Equal(RestoredProjectDependencyFailureReason.MalformedOrDuplicateBearingJson, failed.Failure.Reason);
    }

    [Theory]
    [InlineData("""{ "version": 4, "\uD800": {} }""")]
    [InlineData("""{ "version": 4, "project": { "frameworks": "\uD800" } }""")]
    public void Execute_UnpairedSurrogateEscape_FailsVisibly(string json)
    {
        RestoredProjectDependencyFactsResult result =
            RestoredProjectDependencyFactsQuery.Execute(Encoding.UTF8.GetBytes(json));

        RestoredProjectDependencyFactsResult.Failed failed =
            Assert.IsType<RestoredProjectDependencyFactsResult.Failed>(result);
        Assert.Equal(RestoredProjectDependencyFailureReason.MalformedOrDuplicateBearingJson, failed.Failure.Reason);
    }

    [Fact]
    public void Execute_HostileFrameworkPivotSpelling_IsContainedNotRejected()
    {
        string hostilePivot = $"net{Bidi}8.0";
        var frameworks = new JsonObject
        {
            [hostilePivot] = new JsonObject { ["dependencies"] = new JsonObject() },
        };

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(DocumentWithFrameworks(frameworks)));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        RestoredProjectDeclarationGroup group = Assert.Single(declaration.Groups);
        Assert.Equal(RestoredProjectFrameworkIdentityKind.Unrecognized, group.FrameworkIdentity.Kind);
        Assert.StartsWith("sha256:", group.FrameworkIdentity.Identity, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", group.Identity.PivotIdentity, StringComparison.Ordinal);
        Assert.Equal(group.Identity.PivotIdentity, group.OrderKey);

        string spelling = group.SourcePivotSpelling.ToString();
        Assert.DoesNotContain('\u202E', spelling);
        Assert.Contains(@"\u202E", spelling, StringComparison.Ordinal);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Fact]
    public void Execute_DifferentUnrecognizedFrameworks_DoNotShareIdentity()
    {
        var frameworks = new JsonObject
        {
            ["not-a-tfm-one"] = new JsonObject { ["dependencies"] = new JsonObject() },
            ["not-a-tfm-two"] = new JsonObject { ["dependencies"] = new JsonObject() },
        };

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(DocumentWithFrameworks(frameworks)));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.Equal(2, declaration.Groups.Length);
        Assert.All(
            declaration.Groups,
            g => Assert.Equal(RestoredProjectFrameworkIdentityKind.Unrecognized, g.FrameworkIdentity.Kind));
        Assert.NotEqual(
            declaration.Groups[0].FrameworkIdentity.Identity,
            declaration.Groups[1].FrameworkIdentity.Identity);
        Assert.NotEqual(
            declaration.Groups[0].Identity.PivotIdentity,
            declaration.Groups[1].Identity.PivotIdentity);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Theory]
    [InlineData("Foo,Version=v1.0")]
    [InlineData(".NETPortable,Version=v0.0,Profile=ProfileZZZ")]
    [InlineData("portable-net45+foo")]
    public void Execute_UnresolvableNuGetFrameworkRemainsUnrecognized(
        string sourceFramework)
    {
        var frameworks = new JsonObject
        {
            [sourceFramework] = new JsonObject
            {
                ["dependencies"] = new JsonObject(),
            },
        };

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                DocumentWithFrameworks(frameworks)));
        RestoredProjectDeclarationGroup group = Assert.Single(
            Assert.IsType<RestoredProjectDeclarationResult.Available>(
                facts.Declaration).Groups);

        Assert.Equal(
            RestoredProjectFrameworkIdentityKind.Unrecognized,
            group.FrameworkIdentity.Kind);
        Assert.StartsWith(
            "sha256:",
            group.FrameworkIdentity.Identity,
            StringComparison.Ordinal);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Theory]
    [InlineData("uap10.0", "uap10.0")]
    [InlineData("monoandroid10.0", "monoandroid10.0")]
    [InlineData("portable-net45+win8", "portable-net45+win8")]
    public void Execute_NuGetFrameworkFamiliesUseCanonicalIdentity(
        string sourceFramework,
        string canonicalFramework)
    {
        var frameworks = new JsonObject
        {
            [sourceFramework] = new JsonObject
            {
                ["dependencies"] = new JsonObject(),
            },
        };

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                DocumentWithFrameworks(frameworks)));
        RestoredProjectDeclarationGroup group = Assert.Single(
            Assert.IsType<RestoredProjectDeclarationResult.Available>(
                facts.Declaration).Groups);

        Assert.Equal(
            RestoredProjectFrameworkIdentityKind.Recognized,
            group.FrameworkIdentity.Kind);
        Assert.Equal(canonicalFramework, group.FrameworkIdentity.Identity);
        Assert.Equal(canonicalFramework, group.Identity.PivotIdentity);
    }

    [Fact]
    public void Execute_LongPlatformFrameworkUsesCanonicalShortIdentity()
    {
        const string sourceFramework =
            ".NETCoreApp,Version=v8.0,Platform=windows,PlatformVersion=10.0.19041.0";
        var frameworks = new JsonObject
        {
            [sourceFramework] = new JsonObject
            {
                ["dependencies"] = new JsonObject(),
            },
        };

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                DocumentWithFrameworks(frameworks)));
        RestoredProjectDeclarationGroup group = Assert.Single(
            Assert.IsType<RestoredProjectDeclarationResult.Available>(
                facts.Declaration).Groups);

        Assert.Equal(
            RestoredProjectFrameworkIdentityKind.Recognized,
            group.FrameworkIdentity.Kind);
        Assert.Equal(
            "net8.0-windows10.0.19041",
            group.FrameworkIdentity.Identity);
        Assert.StartsWith(
            "sha256:",
            group.Identity.PivotIdentity,
            StringComparison.Ordinal);
        Assert.Equal(sourceFramework, group.SourcePivotSpelling.ToString());
    }

    [Fact]
    public void Execute_MalformedLongPlatformVersionRemainsUnrecognized()
    {
        const string sourceFramework =
            ".NETCoreApp,Version=v8.0,Platform=windows,PlatformVersion=bogus";
        var frameworks = new JsonObject
        {
            [sourceFramework] = new JsonObject
            {
                ["dependencies"] = new JsonObject(),
            },
        };

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                DocumentWithFrameworks(frameworks)));
        RestoredProjectDeclarationGroup group = Assert.Single(
            Assert.IsType<RestoredProjectDeclarationResult.Available>(
                facts.Declaration).Groups);

        Assert.Equal(
            RestoredProjectFrameworkIdentityKind.Unrecognized,
            group.FrameworkIdentity.Kind);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Theory]
    [InlineData(".NETCoreApp,Version=v8.0,")]
    [InlineData(".NETCoreApp,Version=v8.0,Unknown=value")]
    [InlineData(".NETCoreApp,Version=v8.0,Version=v9.0")]
    [InlineData(".NETCoreApp,Platform=windows")]
    [InlineData(".NETCoreApp,Version=v8.0,Profile=bog us")]
    [InlineData(".NETCoreApp,Version=v8.0,Profile= bogus")]
    [InlineData(".NETCoreApp,Version=v8.0,Profile=bogus ")]
    [InlineData(".NETCoreApp,Version=v8.0,Platform=win dows")]
    [InlineData(".NETCoreApp,Version=v8.0,Platform= windows")]
    [InlineData(".NETCoreApp,Version=v8.0,Platform=windows ")]
    [InlineData(".NETCoreApp,Version=v8.0,Platform=windows,PlatformVersion= 1.0")]
    [InlineData(".NETCoreApp,Version=v8.0,Profile=bogus,Platform=windows")]
    [InlineData(".NETCoreApp,Version=v3.1,Platform=windows")]
    [InlineData(".NETFramework,Version=v4.8,Platform=windows")]
    public void Execute_MalformedLongFrameworkAttributesRemainUnrecognized(
        string sourceFramework)
    {
        var frameworks = new JsonObject
        {
            [sourceFramework] = new JsonObject
            {
                ["dependencies"] = new JsonObject(),
            },
        };

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                DocumentWithFrameworks(frameworks)));
        RestoredProjectDeclarationGroup group = Assert.Single(
            Assert.IsType<RestoredProjectDeclarationResult.Available>(
                facts.Declaration).Groups);

        Assert.Equal(
            RestoredProjectFrameworkIdentityKind.Unrecognized,
            group.FrameworkIdentity.Kind);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Fact]
    public void Execute_DifferentUnresolvablePortableFrameworksRemainDistinct()
    {
        const string first = "portable-net45+foo";
        const string second = "portable-net45+bar";
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                [first] = new JsonObject(),
                [second] = new JsonObject(),
            },
            rootGroups: new JsonObject
            {
                [first] = new JsonArray(),
                [second] = new JsonArray(),
            },
            frameworks: new JsonObject
            {
                [first] = new JsonObject { ["dependencies"] = new JsonObject() },
                [second] = new JsonObject { ["dependencies"] = new JsonObject() },
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(bytes));
        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(
                facts.Declaration);

        Assert.All(
            declaration.Groups,
            group => Assert.Equal(
                RestoredProjectFrameworkIdentityKind.Unrecognized,
                group.FrameworkIdentity.Kind));
        Assert.NotEqual(
            declaration.Groups[0].FrameworkIdentity.Identity,
            declaration.Groups[1].FrameworkIdentity.Identity);
        Assert.True(
            Assert.IsType<RestoredProjectGraphResult.Available>(
                facts.Graph).IsComplete);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Fact]
    public void Execute_SelectedTargetUsesTheSameNuGetFrameworkIdentity()
    {
        const string framework = "uap10.0";
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                [framework] = new JsonObject(),
            },
            rootGroups: new JsonObject
            {
                [framework] = new JsonArray(),
            },
            frameworks: new JsonObject
            {
                [framework] = new JsonObject
                {
                    ["dependencies"] = new JsonObject(),
                },
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                bytes,
                new RestoredProjectTargetRequest(framework)));

        Assert.Equal(framework, facts.SelectedTarget!.FrameworkIdentity);
        Assert.Equal(framework, facts.SelectionIdentity.TargetIdentity);
        Assert.Equal(
            framework,
            Assert.Single(
                Assert.IsType<RestoredProjectDeclarationResult.Available>(
                    facts.Declaration).Groups).FrameworkIdentity.Identity);
    }

    [Fact]
    public void Execute_LongAndShortDuplicateTargetPivotsFailGraph()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net8.0"] = new JsonObject(),
                [".NETCoreApp,Version=v8.0"] = new JsonObject(),
            },
            rootGroups: new JsonObject(),
            frameworks: new JsonObject());

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(bytes));

        Assert.Null(facts.SelectedTarget);
        Assert.Equal(
            RestoredProjectGraphFailureReason.AmbiguousTargetIdentity,
            Assert.IsType<RestoredProjectGraphResult.Failed>(
                facts.Graph).Failure.Reason);
    }

    [Theory]
    [InlineData("uap10.0", "UAP,Version=v10.0")]
    [InlineData("monoandroid10.0", "MonoAndroid,Version=v10.0")]
    [InlineData("portable-net45+win8", ".NETPortable,Version=v0.0,Profile=Profile7")]
    public void Execute_NuGetLongAndShortDuplicateTargetPivotsFailGraph(
        string shortFramework,
        string longFramework)
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                [shortFramework] = new JsonObject(),
                [longFramework] = new JsonObject(),
            },
            rootGroups: new JsonObject(),
            frameworks: new JsonObject());

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(bytes));

        Assert.Null(facts.SelectedTarget);
        Assert.Equal(
            RestoredProjectGraphFailureReason.AmbiguousTargetIdentity,
            Assert.IsType<RestoredProjectGraphResult.Failed>(
                facts.Graph).Failure.Reason);
    }

    [Fact]
    public void Execute_HostileTargetPivotSpelling_YieldsAnOpaqueSelectedTargetIdentity()
    {
        string hostileFramework = $"net{Bidi}8.0";
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                [hostileFramework] = new JsonObject
                {
                    ["Foo/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject { [hostileFramework] = new JsonArray("Foo >= 1.0.0") },
            frameworks: new JsonObject
            {
                [hostileFramework] = new JsonObject
                {
                    ["dependencies"] = new JsonObject
                    {
                        ["Foo"] = new JsonObject { ["target"] = "Package", ["version"] = "[1.0.0, )" },
                    },
                },
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        Assert.NotNull(facts.SelectedTarget);
        Assert.StartsWith("sha256:", facts.SelectedTarget!.FrameworkIdentity, StringComparison.Ordinal);
        Assert.Equal(facts.SelectedTarget.FrameworkIdentity, facts.SelectionIdentity.TargetIdentity);
        Assert.Contains(@"\u202E", facts.SelectedTarget.SourceFrameworkSpelling.ToString(), StringComparison.Ordinal);

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
        Assert.Single(graph.Packages);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Theory]
    [InlineData(",")]
    [InlineData(",,")]
    [InlineData(",Version=v1.0")]
    public void Execute_BlankLongFormFrameworkIdentifier_YieldsOpaqueEvidence(
        string hostileFramework)
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                [hostileFramework] = new JsonObject(),
            },
            rootGroups: new JsonObject
            {
                [hostileFramework] = new JsonArray(),
            },
            frameworks: new JsonObject
            {
                [hostileFramework] = new JsonObject
                {
                    ["dependencies"] = new JsonObject(),
                },
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(bytes));

        Assert.StartsWith(
            "sha256:",
            facts.SelectedTarget!.FrameworkIdentity,
            StringComparison.Ordinal);
        RestoredProjectDeclarationGroup group = Assert.Single(
            Assert.IsType<RestoredProjectDeclarationResult.Available>(
                facts.Declaration).Groups);
        Assert.Equal(
            RestoredProjectFrameworkIdentityKind.Unrecognized,
            group.FrameworkIdentity.Kind);
        Assert.True(
            Assert.IsType<RestoredProjectGraphResult.Available>(
                facts.Graph).IsComplete);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Fact]
    public void Execute_UnicodeCaseEquivalentRuntimeTargets_AreAmbiguousRegardlessOfPropertyOrder()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0/\u03a3"] = new JsonObject(),
                ["net11.0/\u03c2"] = new JsonObject(),
            },
            rootGroups: new JsonObject(),
            frameworks: new JsonObject());

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                bytes,
                new RestoredProjectTargetRequest("net11.0", "\u03a3")));
        RestoredProjectDependencyFacts reordered = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                WithReversedPropertyOrder(bytes),
                new RestoredProjectTargetRequest("net11.0", "\u03a3")));

        Assert.Null(facts.SelectedTarget);
        RestoredProjectGraphResult.Failed graph = Assert.IsType<RestoredProjectGraphResult.Failed>(facts.Graph);
        Assert.Equal(RestoredProjectGraphFailureReason.AmbiguousTargetIdentity, graph.Failure.Reason);
        Assert.Equal(Describe(facts), Describe(reordered));
        Assert.Equal(facts.SelectionIdentity, reordered.SelectionIdentity);
    }

    [Fact]
    public void Execute_UnicodeCaseEquivalentFrameworkTargets_AreAmbiguousRegardlessOfPropertyOrder()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["\u03a3"] = new JsonObject(),
                ["\u03c2"] = new JsonObject(),
            },
            rootGroups: new JsonObject(),
            frameworks: new JsonObject());

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                bytes,
                new RestoredProjectTargetRequest("\u03a3")));
        RestoredProjectDependencyFacts reordered = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                WithReversedPropertyOrder(bytes),
                new RestoredProjectTargetRequest("\u03a3")));

        Assert.Null(facts.SelectedTarget);
        RestoredProjectGraphResult.Failed graph = Assert.IsType<RestoredProjectGraphResult.Failed>(facts.Graph);
        Assert.Equal(RestoredProjectGraphFailureReason.AmbiguousTargetIdentity, graph.Failure.Reason);
        Assert.Equal(Describe(facts), Describe(reordered));
        Assert.Equal(facts.SelectionIdentity, reordered.SelectionIdentity);
    }

    [Fact]
    public void Execute_HostileProjectNodeName_YieldsAnOpaqueProjectParentIdentity()
    {
        string hostileProject = $"Evil{Bidi}Proj";
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    [$"{hostileProject}/1.0.0"] = new JsonObject
                    {
                        ["type"] = "project",
                        ["dependencies"] = new JsonObject { ["Foo"] = "1.0.0" },
                    },
                    ["Foo/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray($"{hostileProject} >= 1.0.0") },
            frameworks: new JsonObject
            {
                ["net11.0"] = new JsonObject { ["dependencies"] = new JsonObject() },
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);
        RestoredProjectGraphEdge edge = Assert.Single(graph.Edges);
        RestoredProjectGraphParentIdentity.Project parent =
            Assert.IsType<RestoredProjectGraphParentIdentity.Project>(edge.Parent);
        Assert.StartsWith("sha256:", parent.Identity.SourceIdentity, StringComparison.Ordinal);
        Assert.Equal(RestoredProjectDependencyRole.Transitive, edge.Role);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Fact]
    public void Execute_ProjectDependencyWithMalformedVersion_IsIncompleteAndDoesNotTraverse()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["Root.Project/1.0.0"] = new JsonObject
                    {
                        ["type"] = "project",
                        ["dependencies"] = new JsonObject { ["Child.Project"] = new JsonObject() },
                    },
                    ["Child.Project/1.0.0"] = new JsonObject
                    {
                        ["type"] = "project",
                        ["dependencies"] = new JsonObject { ["Foo"] = "1.0.0" },
                    },
                    ["Foo/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("Root.Project >= 1.0.0") },
            frameworks: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["dependencies"] = new JsonObject
                    {
                        ["Root.Project"] = new JsonObject { ["target"] = "Project" },
                    },
                },
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Empty(graph.Packages);
        Assert.Empty(graph.Edges);
        RestoredProjectGraphFailure failure = Assert.Single(graph.Failures);
        Assert.Equal(RestoredProjectGraphFailureReason.UnresolvedDependency, failure.Reason);
    }

    [Fact]
    public void Execute_NonCanonicalPackageNodeName_IsRejectedWithoutEnteringIdentity()
    {
        const string NonCanonicalPackageId = "\u65e5\u672c\u8a9e";
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["Root.Project/1.0.0"] = new JsonObject
                    {
                        ["type"] = "project",
                        ["dependencies"] = new JsonObject { [NonCanonicalPackageId] = "1.0.0" },
                    },
                    [$"{NonCanonicalPackageId}/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("Root.Project >= 1.0.0") },
            frameworks: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["dependencies"] = new JsonObject
                    {
                        ["Root.Project"] = new JsonObject { ["target"] = "Project" },
                    },
                },
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Empty(graph.Packages);
        Assert.Empty(graph.Edges);
        Assert.Contains(
            graph.Failures,
            failure => failure.Reason == RestoredProjectGraphFailureReason.UnresolvedDependency);
        AssertNoArtifactTextInIdentities(facts);
    }

    [Fact]
    public void Execute_ProjectNodeWithInvalidVersion_IsUnresolved()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["Root.Project/not-a-version"] = new JsonObject { ["type"] = "project" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("Root.Project >= 1.0.0") },
            frameworks: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["dependencies"] = new JsonObject
                    {
                        ["Root.Project"] = new JsonObject { ["target"] = "Project" },
                    },
                },
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Empty(graph.Packages);
        Assert.Empty(graph.Edges);
        Assert.Contains(
            graph.Failures,
            failure => failure.Reason == RestoredProjectGraphFailureReason.UnresolvedRootEntry);
    }

    [Fact]
    public void Execute_OversizedWholeDocument_FailsWithConfiguredLimit()
    {
        byte[] oversized = new byte[RestoredProjectDependencyFactsQuery.MaxAssetsBytes + 1];

        RestoredProjectDependencyFactsResult result = RestoredProjectDependencyFactsQuery.Execute(oversized);

        RestoredProjectDependencyFactsResult.Failed failed = Assert.IsType<RestoredProjectDependencyFactsResult.Failed>(result);
        Assert.Equal(RestoredProjectDependencyFailureReason.ConfiguredLimitExceeded, failed.Failure.Reason);
    }

    [Fact]
    public void Execute_OversizedScalar_LeavesItsPhaseIncomplete()
    {
        string oversizedPivot = new('x', RestoredProjectDependencyFactsQuery.MaxScalarCharacters + 1);
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                DocumentWithFrameworks(
                    new JsonObject
                    {
                        [oversizedPivot] = new JsonObject { ["dependencies"] = new JsonObject() },
                    })));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.False(declaration.IsComplete);
        Assert.Empty(declaration.Groups);
        Assert.Contains(
            declaration.Failures,
            failure => failure.Reason == RestoredProjectDeclarationFailureReason.InvalidGroupShape);
    }

    [Fact]
    public void Execute_TooManyDeclarationGroups_LeavesDeclarationIncomplete()
    {
        var frameworks = new JsonObject();
        for (int i = 0; i <= RestoredProjectDependencyFactsQuery.MaxDeclarationGroups; i++)
            frameworks.Add($"synthetic{i}", new JsonObject { ["dependencies"] = new JsonObject() });

        byte[] bytes = DocumentWithFrameworks(frameworks);
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.False(declaration.IsComplete);
        Assert.Equal(RestoredProjectDependencyFactsQuery.MaxDeclarationGroups, declaration.Groups.Length);
        Assert.Contains(
            declaration.Failures,
            f => f.Reason == RestoredProjectDeclarationFailureReason.ConfiguredLimitExceeded);

        RestoredProjectDependencyFacts reordered = Available(
            RestoredProjectDependencyFactsQuery.Execute(WithReversedPropertyOrder(bytes)));
        Assert.Equal(Describe(facts), Describe(reordered));
        Assert.Equal(facts.SelectionIdentity, reordered.SelectionIdentity);
    }

    [Fact]
    public void Execute_ProjectOnlyChainAtTheNodeBound_TraversesIterativelyToItsFullDepth()
    {
        byte[] bytes = ProjectChainDocument(RestoredProjectDependencyFactsQuery.MaxGraphNodes - 1);

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.True(graph.IsComplete);

        // The leaf is only reachable after walking every project node in the chain.
        RestoredProjectPackageNode leaf = Assert.Single(graph.Packages);
        Assert.Equal("chain.leaf.package", leaf.Identity.Coordinate.PackageId);
        Assert.Equal(RestoredProjectDependencyRole.Transitive, leaf.Role);
        Assert.Single(graph.Edges);
    }

    [Fact]
    public void Execute_ProjectOnlyChainBeyondTheNodeBound_LeavesGraphIncomplete()
    {
        byte[] bytes = ProjectChainDocument(RestoredProjectDependencyFactsQuery.MaxGraphNodes);

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        RestoredProjectGraphFailure failure = Assert.Single(
            graph.Failures,
            f => f.Reason == RestoredProjectGraphFailureReason.ConfiguredLimitExceeded);
        Assert.Equal(1, failure.Count);
    }

    [Fact]
    public void Execute_MoreEdgesThanTheEdgeBound_LeavesGraphIncomplete()
    {
        const int Parents = 130;
        const int Children = 127;
        Assert.True(Parents * Children > RestoredProjectDependencyFactsQuery.MaxGraphEdges);
        Assert.True(Parents + Children <= RestoredProjectDependencyFactsQuery.MaxGraphNodes);

        var targets = new JsonObject();
        var rootEntries = new JsonArray();
        for (int parent = 0; parent < Parents; parent++)
        {
            var dependencies = new JsonObject();
            for (int child = 0; child < Children; child++)
                dependencies.Add($"Child.Package{child}", "1.0.0");

            targets.Add(
                $"Parent.Project{parent}/1.0.0",
                new JsonObject { ["type"] = "project", ["dependencies"] = dependencies });
            rootEntries.Add($"Parent.Project{parent} >= 1.0.0");
        }

        for (int child = 0; child < Children; child++)
            targets.Add($"Child.Package{child}/1.0.0", new JsonObject { ["type"] = "package" });

        byte[] bytes = SyntheticDocument(
            targets: new JsonObject { ["net11.0"] = targets },
            rootGroups: new JsonObject { ["net11.0"] = rootEntries },
            frameworks: new JsonObject { ["net11.0"] = new JsonObject { ["dependencies"] = new JsonObject() } });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Contains(graph.Failures, f => f.Reason == RestoredProjectGraphFailureReason.ConfiguredLimitExceeded);
        Assert.True(graph.Edges.Length <= RestoredProjectDependencyFactsQuery.MaxGraphEdges);

        RestoredProjectDependencyFacts reordered = Available(
            RestoredProjectDependencyFactsQuery.Execute(WithReversedPropertyOrder(bytes)));
        Assert.Equal(Describe(facts), Describe(reordered));
        Assert.Equal(facts.SelectionIdentity, reordered.SelectionIdentity);
    }

    [Fact]
    public void Execute_RepeatedEdgesCannotEvadeTheEdgeOccurrenceBound()
    {
        const string PackageId = "abcdefghijklmnop";
        var dependencies = new JsonObject();
        for (int occurrence = 0; occurrence <= RestoredProjectDependencyFactsQuery.MaxGraphEdges; occurrence++)
        {
            char[] spelling = PackageId.ToCharArray();
            for (int bit = 0; bit < spelling.Length; bit++)
            {
                if ((occurrence & (1 << bit)) != 0)
                    spelling[bit] = char.ToUpperInvariant(spelling[bit]);
            }

            dependencies.Add(new string(spelling), "1.0.0");
        }

        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["Root.Project/1.0.0"] = new JsonObject
                    {
                        ["type"] = "project",
                        ["dependencies"] = dependencies,
                    },
                    [$"{PackageId}/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("Root.Project >= 1.0.0") },
            frameworks: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["dependencies"] = new JsonObject
                    {
                        ["Root.Project"] = new JsonObject { ["target"] = "Project" },
                    },
                },
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Contains(graph.Failures, f => f.Reason == RestoredProjectGraphFailureReason.ConfiguredLimitExceeded);
        Assert.Single(graph.Edges);
    }

    [Fact]
    public void Execute_RootConstraintScanBeyondItsBound_IsIncomplete()
    {
        var declarations = new JsonObject
        {
            ["Root.Project"] = new JsonObject { ["target"] = "Project" },
        };
        for (int index = 0;
             index <= RestoredProjectDependencyFactsQuery.MaxDeclaredPackages
                + RestoredProjectDependencyFactsQuery.MaxDeclaredProjectReferences;
             index++)
        {
            declarations.Add(
                $"Synthetic.Project{index:D5}",
                new JsonObject { ["target"] = "Project" });
        }

        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["Root.Project/1.0.0"] = new JsonObject { ["type"] = "project" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("Root.Project >= 1.0.0") },
            frameworks: new JsonObject
            {
                ["net11.0"] = new JsonObject { ["dependencies"] = declarations },
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Empty(graph.Packages);
        Assert.Empty(graph.Edges);
        Assert.Contains(graph.Failures, f => f.Reason == RestoredProjectGraphFailureReason.ConfiguredLimitExceeded);
    }

    [Fact]
    public void Execute_PackageRootWithoutConstraint_RemainsDirectAndTraversesUsableDependencies()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["Root.Package/1.0.0"] = new JsonObject
                    {
                        ["type"] = "package",
                        ["dependencies"] = new JsonObject { ["Child.Package"] = "2.0.0" },
                    },
                    ["Child.Package/2.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("Root.Package >= 1.0.0") },
            frameworks: new JsonObject
            {
                ["net11.0"] = new JsonObject { ["dependencies"] = new JsonObject() },
            });

        RestoredProjectDependencyFacts facts = Available(RestoredProjectDependencyFactsQuery.Execute(bytes));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.False(graph.IsComplete);
        Assert.Contains(graph.Failures, f => f.Reason == RestoredProjectGraphFailureReason.UnresolvedRootEntry);
        Assert.DoesNotContain(graph.Edges, edge => edge.Parent is RestoredProjectGraphParentIdentity.Root);

        RestoredProjectPackageNode root =
            Assert.Single(graph.Packages, package => package.Identity.Coordinate.PackageId == "root.package");
        Assert.Equal(RestoredProjectDependencyRole.Direct, root.Role);
        RestoredProjectPackageNode child =
            Assert.Single(graph.Packages, package => package.Identity.Coordinate.PackageId == "child.package");
        Assert.Equal(RestoredProjectDependencyRole.Transitive, child.Role);
        Assert.Single(graph.Edges);
    }

    // ---- Deterministic semantic facts after JSON property reordering ------

    [Fact]
    public void Execute_SemanticallyReorderedJson_PreservesSemanticFactsButChangesContentProvenance()
    {
        byte[] original = ReadCopiedAssetsBytes();
        byte[] reordered = WithReversedPropertyOrder(original);

        Assert.NotEqual(original, reordered);

        RestoredProjectDependencyFacts originalFacts = Available(RestoredProjectDependencyFactsQuery.Execute(original));
        RestoredProjectDependencyFacts reorderedFacts = Available(RestoredProjectDependencyFactsQuery.Execute(reordered));

        Assert.NotEqual(originalFacts.ContentProvenance.Sha256, reorderedFacts.ContentProvenance.Sha256);

        // The public semantic projection, not only the digest, is property-order independent.
        Assert.Equal(Describe(originalFacts), Describe(reorderedFacts));
        Assert.Equal(originalFacts.SelectionIdentity, reorderedFacts.SelectionIdentity);
    }

    [Fact]
    public void Execute_SemanticallyReorderedIncompleteJson_PreservesFailureEvidenceOrderAndCounts()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                JsonObject dependencies = root["project"]!["frameworks"]!["net11.0"]!["dependencies"]!.AsObject();
                dependencies["NuGet.Packaging"]!["version"] = "not-a-range";
                dependencies.Add("Broken.One", new JsonObject { ["target"] = "Package", ["version"] = "also-bad" });
                dependencies.Add("Broken.Two", new JsonObject { ["target"] = "Package" });
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));
        RestoredProjectDependencyFacts reordered = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                WithReversedPropertyOrder(mutated),
                new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        RestoredProjectDeclarationFailure failure = Assert.Single(declaration.Failures);
        Assert.Equal(RestoredProjectDeclarationFailureReason.InvalidPackageDeclaration, failure.Reason);
        Assert.Equal(3, failure.Count);
        Assert.Equal(Describe(facts), Describe(reordered));
        Assert.Equal(facts.SelectionIdentity, reordered.SelectionIdentity);
    }

    // ---- Facts digest: typed canonical encoding, not concatenation --------

    [Fact]
    public void Execute_DifferentUnrecognizedGroupShapes_DoNotShareAFactsDigest()
    {
        // Under a delimiter-concatenated encoding these two documents produce the identical
        // "a:Unrecognized:[];b:Unrecognized:[];c:Unrecognized:[];" text and therefore one digest.
        byte[] first = DocumentWithFrameworks(new JsonObject
        {
            ["a"] = new JsonObject { ["dependencies"] = new JsonObject() },
            ["b:Unrecognized:[];c"] = new JsonObject { ["dependencies"] = new JsonObject() },
        });
        byte[] second = DocumentWithFrameworks(new JsonObject
        {
            ["a:Unrecognized:[];b"] = new JsonObject { ["dependencies"] = new JsonObject() },
            ["c"] = new JsonObject { ["dependencies"] = new JsonObject() },
        });

        RestoredProjectDependencyFacts firstFacts = Available(RestoredProjectDependencyFactsQuery.Execute(first));
        RestoredProjectDependencyFacts secondFacts = Available(RestoredProjectDependencyFactsQuery.Execute(second));

        Assert.Equal(2, Assert.IsType<RestoredProjectDeclarationResult.Available>(firstFacts.Declaration).Groups.Length);
        Assert.Equal(2, Assert.IsType<RestoredProjectDeclarationResult.Available>(secondFacts.Declaration).Groups.Length);
        Assert.NotEqual(Describe(firstFacts), Describe(secondFacts));
        Assert.NotEqual(firstFacts.SelectionIdentity.FactsDigest, secondFacts.SelectionIdentity.FactsDigest);
    }

    // ---- Bounded mutations proving non-vacuous evidence --------------------

    [Fact]
    public void Execute_MutatedDeclaredRange_ChangesProjectedCanonicalConstraint()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["project"]!["frameworks"]!["net11.0"]!["dependencies"]!["NuGet.Packaging"]!["version"] =
                "[8.0.0, )");

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        RestoredProjectDeclarationGroup group = Assert.Single(declaration.Groups, g => g.Identity.PivotIdentity == "net11.0");
        RestoredProjectDeclaredPackage package = Assert.Single(group.Packages, p => p.CanonicalPackageId == "nuget.packaging");
        Assert.Equal("[8.0.0, )", package.CanonicalVersionConstraint);
    }

    [Fact]
    public void Execute_MutatedResolvedCoordinateVersion_ChangesProjectedGraphCoordinate()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                JsonObject targetsNet11 = root["targets"]!["net11.0"]!.AsObject();
                JsonNode markdownNode = targetsNet11["MarkdownTable.Formatting/0.3.4"]!.DeepClone()!;
                targetsNet11.Remove("MarkdownTable.Formatting/0.3.4");
                targetsNet11.Add("MarkdownTable.Formatting/9.9.9", markdownNode);
                root["project"]!["frameworks"]!["net11.0"]!["dependencies"]!["MarkdownTable.Formatting"]!["version"] =
                    "[9.9.9, )";
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        RestoredProjectPackageNode package = Assert.Single(graph.Packages, p => p.Identity.Coordinate.PackageId == "markdowntable.formatting");
        Assert.Equal("9.9.9", package.Identity.Coordinate.Version);
    }

    [Fact]
    public void Execute_PromotingTransitivePackageToRootEntry_ChangesRoleToDirect()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root =>
            {
                root["projectFileDependencyGroups"]!["net11.0"]!.AsArray().Add("Newtonsoft.Json >= 13.0.3");
                root["project"]!["frameworks"]!["net11.0"]!["dependencies"]!["Newtonsoft.Json"] =
                    new JsonObject { ["target"] = "Package", ["version"] = "[13.0.3, )" };
            });

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        RestoredProjectPackageNode package = Assert.Single(graph.Packages, p => p.Identity.Coordinate.PackageId == "newtonsoft.json");
        Assert.Equal(RestoredProjectDependencyRole.Direct, package.Role);
        Assert.Contains(
            graph.Edges,
            e => e.Dependency.Coordinate.PackageId == "newtonsoft.json"
                && e.Parent is RestoredProjectGraphParentIdentity.Root);
    }

    // ---- Stable, content-free failure messages -----------------------------

    [Fact]
    public void Failures_MessagesNeverQuoteArtifactText()
    {
        byte[] mutated = WithReplacedNode(
            ReadCopiedAssetsBytes(),
            root => root["project"]!["frameworks"]!["net11.0"]!["dependencies"]!["NuGet.Packaging"]!["version"] =
                $"not{Bidi}a-version");

        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(mutated, new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        RestoredProjectDeclarationFailure failure = Assert.Single(
            declaration.Failures,
            f => f.Reason == RestoredProjectDeclarationFailureReason.InvalidPackageDeclaration);
        Assert.Equal(
            "A project.frameworks package declaration has an invalid identity or version constraint.",
            failure.Message);
        Assert.DoesNotContain("NuGet.Packaging", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\u202E', failure.Message);
    }

    [Theory]
    [InlineData(RestoredProjectDependencyFailureReason.MalformedOrDuplicateBearingJson)]
    [InlineData(RestoredProjectDependencyFailureReason.UnsupportedDocumentShape)]
    [InlineData(RestoredProjectDependencyFailureReason.UnsupportedSchemaVersion)]
    [InlineData(RestoredProjectDependencyFailureReason.ConfiguredLimitExceeded)]
    public void DependencyFailure_MessagesAreStableAndNonEmpty(RestoredProjectDependencyFailureReason reason)
    {
        var failure = new RestoredProjectDependencyFailure(reason);

        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
    }

    [Fact]
    public void PhaseFailures_HaveNonEmptyMessagesAndRejectNonPositiveCounts()
    {
        foreach (RestoredProjectDeclarationFailureReason reason
            in Enum.GetValues<RestoredProjectDeclarationFailureReason>())
        {
            Assert.False(string.IsNullOrWhiteSpace(new RestoredProjectDeclarationFailure(reason).Message));
        }

        foreach (RestoredProjectGraphFailureReason reason in Enum.GetValues<RestoredProjectGraphFailureReason>())
            Assert.False(string.IsNullOrWhiteSpace(new RestoredProjectGraphFailure(reason).Message));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RestoredProjectDeclarationFailure(RestoredProjectDeclarationFailureReason.InvalidGroupShape, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RestoredProjectGraphFailure(RestoredProjectGraphFailureReason.UnresolvedDependency, 0));
    }

    [Fact]
    public void PhaseResults_RejectInvalidCompletionCombinations()
    {
        Assert.Throws<ArgumentException>(() => new RestoredProjectDeclarationResult.Available(
            [],
            [],
            RestoredProjectPhaseCompletion.Incomplete));
        Assert.Throws<ArgumentException>(() => new RestoredProjectGraphResult.Available(
            [],
            [],
            [new RestoredProjectGraphFailure(RestoredProjectGraphFailureReason.UnresolvedDependency)],
            RestoredProjectPhaseCompletion.Complete));
    }

    [Fact]
    public void Execute_EveryIssuedIdentity_IsScopedToTheOneSelectionIdentity()
    {
        RestoredProjectDependencyFacts facts = Available(
            RestoredProjectDependencyFactsQuery.Execute(
                ReadCopiedAssetsBytes(),
                new RestoredProjectTargetRequest("net11.0")));

        RestoredProjectSelectionIdentity selection = facts.SelectionIdentity;
        Assert.Equal(selection, facts.Root.Selection);

        RestoredProjectDeclarationResult.Available declaration =
            Assert.IsType<RestoredProjectDeclarationResult.Available>(facts.Declaration);
        Assert.All(declaration.Groups, g => Assert.Equal(selection, g.Identity.Selection));

        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(facts.Graph);
        Assert.NotEmpty(graph.Edges);
        Assert.All(graph.Packages, p => Assert.Equal(selection, p.Identity.Selection));
        foreach (RestoredProjectGraphEdge edge in graph.Edges)
        {
            Assert.Equal(selection, edge.Dependency.Selection);
            Assert.Equal(selection, edge.Identity.Dependency.Selection);
            Assert.Equal(ParentSelection(edge.Parent), selection);
            Assert.Equal(ParentSelection(edge.Identity.Parent), selection);
        }

        static RestoredProjectSelectionIdentity? ParentSelection(RestoredProjectGraphParentIdentity parent) =>
            parent switch
            {
                RestoredProjectGraphParentIdentity.Root r => r.Identity.Selection,
                RestoredProjectGraphParentIdentity.Package p => p.Identity.Selection,
                RestoredProjectGraphParentIdentity.Project p => p.Identity.Selection,
                _ => null,
            };
    }

    // ---- Helpers -----------------------------------------------------------
    static RestoredProjectDependencyFacts Available(RestoredProjectDependencyFactsResult result) =>
        Assert.IsType<RestoredProjectDependencyFactsResult.Available>(result).Value;

    static byte[] ReadCopiedAssetsBytes() =>
        File.ReadAllBytes(FixtureCatalog.RestoredProjectDependencyFacts.AssetPath("project.assets.json"));

    /// <summary>A canonical, order-free description of every public semantic fact.</summary>
    static ImmutableArray<string> Describe(RestoredProjectDependencyFacts facts)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        lines.Add($"target={facts.SelectionIdentity.TargetIdentity}");
        switch (facts.Declaration)
        {
            case RestoredProjectDeclarationResult.Available available:
                lines.Add($"declaration={available.Completion}");
                foreach (RestoredProjectDeclarationGroup group in available.Groups)
                {
                    lines.Add(
                        $"group {group.Identity.PivotIdentity} {group.OrderKey} "
                        + $"{group.FrameworkIdentity.Kind}:{group.FrameworkIdentity.Identity}");
                    foreach (RestoredProjectDeclaredPackage package in group.Packages)
                    {
                        lines.Add(
                            $"  declared {package.CanonicalPackageId} {package.CanonicalVersionConstraint} "
                            + $"x{package.SourceOccurrenceCount} '{package.SourcePackageIdSpelling}' "
                            + $"'{package.SourceVersionConstraintSpelling}'");
                    }
                }

                foreach (RestoredProjectDeclarationFailure failure in available.Failures)
                    lines.Add($"declaration-failure {failure.Reason} x{failure.Count}");
                break;
            case RestoredProjectDeclarationResult.Unavailable:
                lines.Add("declaration=unavailable");
                break;
            case RestoredProjectDeclarationResult.Failed failed:
                lines.Add($"declaration=failed {failed.Failure.Reason}");
                break;
        }

        switch (facts.Graph)
        {
            case RestoredProjectGraphResult.Available available:
                lines.Add($"graph={available.Completion}");
                foreach (RestoredProjectPackageNode package in available.Packages)
                {
                    lines.Add(
                        $"package {package.Identity.Coordinate.PackageId}/"
                        + $"{package.Identity.Coordinate.Version} {package.Role}");
                }

                foreach (RestoredProjectGraphEdge edge in available.Edges)
                {
                    lines.Add(
                        $"edge {DescribeParent(edge.Parent)} -> {edge.Dependency.Coordinate.PackageId}/"
                        + $"{edge.Dependency.Coordinate.Version} {edge.CanonicalVersionConstraint} "
                        + $"'{edge.SourceVersionConstraintSpelling}' {edge.Role}");
                }

                foreach (RestoredProjectGraphFailure failure in available.Failures)
                    lines.Add($"graph-failure {failure.Reason} x{failure.Count}");
                break;
            case RestoredProjectGraphResult.Unavailable:
                lines.Add("graph=unavailable");
                break;
            case RestoredProjectGraphResult.Failed failed:
                lines.Add($"graph=failed {failed.Failure.Reason}");
                break;
        }

        return lines.ToImmutable();
    }

    static string DescribeParent(RestoredProjectGraphParentIdentity parent) => parent switch
    {
        RestoredProjectGraphParentIdentity.Root => "root",
        RestoredProjectGraphParentIdentity.Package p =>
            $"pkg:{p.Identity.Coordinate.PackageId}/{p.Identity.Coordinate.Version}",
        RestoredProjectGraphParentIdentity.Project p => $"proj:{p.Identity.SourceIdentity}",
        _ => "?",
    };

    /// <summary>
    /// Every public identity string is canonical target/coordinate text or an opaque digest, so no
    /// artifact-authored spelling — including a bidirectional override — can appear in one.
    /// </summary>
    static void AssertNoArtifactTextInIdentities(RestoredProjectDependencyFacts facts)
    {
        foreach (string identity in EnumerateIdentities(facts))
        {
            Assert.DoesNotContain('\u202E', identity);
            Assert.All(identity, c => Assert.True(char.IsAscii(c), $"Identity '{identity}' is not ASCII."));
        }
    }

    static IEnumerable<string> EnumerateIdentities(RestoredProjectDependencyFacts facts)
    {
        yield return facts.SelectionIdentity.TargetIdentity;
        yield return facts.SelectionIdentity.FactsDigest;
        yield return facts.Root.Selection.TargetIdentity;
        if (facts.SelectedTarget is { } selected)
        {
            yield return selected.FrameworkIdentity;
            if (selected.RuntimeIdentifierIdentity is { } rid)
                yield return rid;
        }

        if (facts.Declaration is RestoredProjectDeclarationResult.Available declaration)
        {
            foreach (RestoredProjectDeclarationGroup group in declaration.Groups)
            {
                yield return group.Identity.PivotIdentity;
                yield return group.OrderKey;
                yield return group.FrameworkIdentity.Identity;
                foreach (RestoredProjectDeclaredPackage package in group.Packages)
                {
                    yield return package.CanonicalPackageId;
                    yield return package.CanonicalVersionConstraint;
                }
            }
        }

        if (facts.Graph is RestoredProjectGraphResult.Available graph)
        {
            foreach (RestoredProjectPackageNode package in graph.Packages)
            {
                yield return package.Identity.Coordinate.PackageId;
                yield return package.Identity.Coordinate.Version;
            }

            foreach (RestoredProjectGraphEdge edge in graph.Edges)
            {
                yield return DescribeParent(edge.Parent);
                yield return edge.Dependency.Coordinate.PackageId;
                yield return edge.Dependency.Coordinate.Version;
                yield return edge.CanonicalVersionConstraint;
            }
        }
    }

    static byte[] DocumentWithFrameworks(JsonObject frameworks)
    {
        var document = new JsonObject
        {
            ["version"] = 4,
            ["project"] = new JsonObject { ["frameworks"] = frameworks },
        };
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    static byte[] SyntheticDocument(
        JsonObject targets,
        JsonObject rootGroups,
        JsonObject frameworks,
        int version = 4)
    {
        var document = new JsonObject
        {
            ["version"] = version,
            ["targets"] = targets,
            ["projectFileDependencyGroups"] = rootGroups,
            ["project"] = new JsonObject { ["frameworks"] = frameworks },
        };
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    /// <summary>
    /// A chain of <paramref name="projectNodes"/> project nodes reached from one root entry, ending
    /// at a single package leaf. Recursive traversal would exhaust the CLR stack long before the
    /// chain ends, and the leaf package proves the whole depth was actually walked.
    /// </summary>
    static byte[] ProjectChainDocument(int projectNodes)
    {
        var targets = new JsonObject();
        for (int index = 0; index < projectNodes; index++)
        {
            string next = index + 1 < projectNodes ? $"Chain.Project{index + 1}" : "Chain.Leaf.Package";
            targets.Add(
                $"Chain.Project{index}/1.0.0",
                new JsonObject
                {
                    ["type"] = "project",
                    ["dependencies"] = new JsonObject { [next] = "1.0.0" },
                });
        }

        targets.Add("Chain.Leaf.Package/1.0.0", new JsonObject { ["type"] = "package" });

        return SyntheticDocument(
            targets: new JsonObject { ["net11.0"] = targets },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("Chain.Project0 >= 1.0.0") },
            frameworks: new JsonObject { ["net11.0"] = new JsonObject { ["dependencies"] = new JsonObject() } });
    }

    static byte[] SchemaVersion3Document()
    {
        var document = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = new JsonObject
            {
                [".NETCoreApp,Version=v11.0"] = new JsonObject
                {
                    ["Foo/1.0.0"] = new JsonObject
                    {
                        ["type"] = "package",
                        ["dependencies"] = new JsonObject { ["Bar"] = "2.0.0" },
                    },
                    ["Bar/2.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            ["projectFileDependencyGroups"] = new JsonObject
            {
                [".NETCoreApp,Version=v11.0"] = new JsonArray("Foo >= 1.0.0"),
            },
            ["project"] = new JsonObject
            {
                ["frameworks"] = new JsonObject
                {
                    ["net11.0"] = new JsonObject
                    {
                        ["dependencies"] = new JsonObject
                        {
                            ["Foo"] = new JsonObject { ["target"] = "Package", ["version"] = "[1.0.0, )" },
                        },
                    },
                },
            },
        };
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    static byte[] WithReplacedNode(byte[] assetsBytes, Action<JsonNode> mutate)
    {
        JsonNode root = JsonNode.Parse(assetsBytes)!;
        mutate(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    static byte[] WithReversedPropertyOrder(byte[] assetsBytes)
    {
        JsonNode root = JsonNode.Parse(assetsBytes)!;
        JsonNode reversed = Reverse(root);
        return Encoding.UTF8.GetBytes(reversed.ToJsonString());
    }

    static JsonNode Reverse(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var reversedObject = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> property in obj.Reverse())
                    reversedObject.Add(property.Key, property.Value is null ? null : Reverse(property.Value));
                return reversedObject;
            case JsonArray array:
                var reversedArray = new JsonArray();
                foreach (JsonNode? element in array)
                    reversedArray.Add(element is null ? null : Reverse(element));
                return reversedArray;
            default:
                return node.DeepClone();
        }
    }
}
