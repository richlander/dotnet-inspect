using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Theory]
    [InlineData("-S")]
    [InlineData("-s")]
    [InlineData("--select")]
    [InlineData("--section")]
    public async Task SectionSelection_RequiresTarget(string option)
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Text.Json",
            option);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Required argument missing for option",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SectionSelection_RepeatedAliasRequiresEveryTarget(
        bool includeTrailingOption)
    {
        string[] trailing = includeTrailingOption ? ["--json"] : [];
        var (exit, output, error) = await RunAppAsync(
            [
                "library",
                "System.Text.Json",
                "-S",
                "Library Info",
                "--section",
                .. trailing,
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.DoesNotContain(
            nameof(InvalidOperationException),
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "at DotnetInspect",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SectionSelection_PackagePreparseRejectsRepeatedMissingTargetWithoutStack()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.Text.Json",
            "-S",
            "Package Info",
            "--section",
            "--json",
            "--offline");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--select requires at least one name.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(InvalidOperationException),
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "at DotnetInspect",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SectionSelection_PackagePreparseRejectsRepeatedColonEmptyTargetWithoutStack()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.Text.Json",
            "-S",
            "Package Info",
            "--section:",
            "--json",
            "--offline");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--select requires at least one name.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(InvalidOperationException),
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "at DotnetInspect",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SectionSelection_PackagePreparseMergesColonAttachedTarget()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "System.Text.Json@10.0.0",
            "-S",
            "Package Info",
            "--section:Manifest",
            "--tips",
            "q");

        Assert.True(
            exit == 0,
            $"Expected exit code 0, got {exit}.{Environment.NewLine}{error}");
        Assert.Empty(error);
        Assert.Contains(
            "## Package Info",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "## Manifest",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SectionSelection_AcceptsColonAttachedCategory()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Text.Json",
            "--section:@Library",
            "--tips",
            "q");

        Assert.True(
            exit == 0,
            $"Expected exit code 0, got {exit}.{Environment.NewLine}{error}");
        Assert.Empty(error);
        Assert.Contains(
            "## Library Info",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SectionSelection_RejectsSeparatorOnlyRepeatedTarget()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-S",
            "Library Info",
            "--section",
            ";",
            "--offline",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--select requires at least one name.",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-S", "")]
    [InlineData("-s", ",")]
    [InlineData("--select", ";")]
    [InlineData("--section", ",;")]
    public async Task SectionSelection_RejectsValuesWithoutNames(
        string option,
        string value)
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Text.Json",
            option,
            value);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--select requires at least one name.",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-S=")]
    [InlineData("-s=")]
    [InlineData("--select=")]
    [InlineData("--section=")]
    public async Task SectionSelection_RejectsInlineEmptyValues(
        string option)
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Text.Json",
            option);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--select requires at least one name.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SectionSelection_OptionLookingValueFailsWithoutStack()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-S",
            "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.DoesNotContain(
            nameof(InvalidOperationException),
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "at DotnetInspect",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vocabulary_EnvironmentMermaidRejectsMultiSectionCount()
    {
        string? originalFormat = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "mermaid");
            var (exit, output, error) = await RunAppAsync(
                "vocabulary",
                "-S",
                "C# *",
                "--count",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "cannot render multiple sections as Mermaid",
                error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
        }
    }

    /// <summary>
    /// The gate for <see cref="SectionPipeline{TModel}.BareSelectSectionNames"/> matching the
    /// authored base fixed overview. Fixed, network-free metadata sections are domain-owned, so
    /// they do not enter either set merely because they are cheap.
    /// </summary>
    [Fact]
    public void BareSelect_MatchesAuthoredBaseFixedOverview()
    {
        var library = LibrarySections.CreatePipeline();

        Assert.Equal(library.FixedOverviewSectionNames, library.BareSelectSectionNames);
        Assert.DoesNotContain(MetadataSectionNames.Image, library.FixedOverviewSectionNames);
        Assert.DoesNotContain(MetadataSectionNames.Heap, library.FixedOverviewSectionNames);

        var package = PackageSectionDescriptors.CreatePipeline();
        Assert.Equal(package.FixedOverviewSectionNames, package.BareSelectSectionNames);
    }

    // ── bare router ───────────────────────────────────────────────────

    [Fact]
    public async Task BareName_PlatformLibrary_RoutesToLibrary()
    {
        var (exit, output, error) = await RunAppAsync("System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Text.Json.dll", output);
        Assert.Contains("## Library Info", output);
    }

    [Fact]
    public async Task BareName_PlatformNamespacePrefix_RoutesToTypePrefixBrowse()
    {
        var (exit, output, error) = await RunAppAsync("System.Text", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Showing best-effort platform prefix matches for 'System.Text'", error);
        Assert.Contains("# System.Text", output);

        // Platform provenance moved off the default view: the compact fields list is now the -v:q
        // view only, and this browse path floors verbosity at Minimal, so it never renders that
        // line at all. The same fact is carried by the bounded API Info section, which is where the
        // claim is asserted. The claim here is about ROUTING, so it is moved to where the evidence
        // lives rather than dropped.
        var (factExit, factOutput, _) = await RunAppAsync(
            "System.Text", "--tips", "q", "-S", SectionNames.ApiInfo);

        Assert.Equal(0, factExit);
        Assert.Contains("| Source | Platform |", factOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareName_ExactNuGetPackageId_RoutesToPackage()
    {
        var (exit, output, error) = await RunAppAsync("System.CommandLine", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.CommandLine", output);
        Assert.Contains("## Package Info", output);
        Assert.DoesNotContain("Library: System.CommandLine.dll | Types:", output);
    }

    [Fact]
    public async Task BareName_CommandTypo_SuggestsCommandWithoutNuGetLookup()
    {
        var (exit, output, error) = await RunAppAsync("packag", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.Empty(output);
        Assert.Contains("Error: Unknown command 'packag'.", error);
        Assert.Contains("Did you mean:", error);
        Assert.Contains("  package", error);
        Assert.DoesNotContain("Package 'packag' not found", error);
        Assert.DoesNotContain("Network traffic", error);
    }

    // ── removed commands ─────────────────────────────────────────────

    [Fact]
    public async Task ApiCommand_RemovedFromRoot()
    {
        var (exit, output, error) = await RunAppAsync("api", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Unrecognized command or argument 'api'", error);
        Assert.DoesNotContain("Package 'api' not found", error);
        Assert.DoesNotContain("Network traffic", error);
    }

    [Fact]
    public async Task DependencyEvidenceCommand_ReportsDependsReplacement()
    {
        var (exit, output, error) = await RunAppAsync(
            "--tips",
            "q",
            "dependency-evidence",
            "--package",
            "Definitely.Does.Not.Exist");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "'dependency-evidence' is no longer valid.",
            error);
        Assert.Contains("Use 'depends'", error);
        Assert.Contains("-S Dependencies", error);
        Assert.DoesNotContain(
            "Package 'dependency-evidence' not found",
            error);
        Assert.DoesNotContain("Network traffic", error);
    }

    [Theory]
    [InlineData("--tips")]
    [InlineData("-T")]
    public async Task DependencyEvidenceCommand_AfterBareTipsReportsReplacement(
        string tipsOption)
    {
        var (exit, output, error) = await RunAppAsync(
            tipsOption,
            "dependency-evidence",
            "--package",
            "Definitely.Does.Not.Exist");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "'dependency-evidence' is no longer valid.",
            error);
        Assert.Contains("Use 'depends'", error);
        Assert.DoesNotContain(
            "Package 'dependency-evidence' not found",
            error);
        Assert.DoesNotContain("Network traffic", error);
    }

    [Theory]
    [InlineData("--tips")]
    [InlineData("-T")]
    public async Task BareTips_PreservesExplicitDependencyEvidencePackageSubject(
        string tipsOption)
    {
        var (exit, output, error) = await RunAppAsync(
            tipsOption,
            "package",
            "dependency-evidence",
            "-D",
            "--schema");

        Assert.Equal(0, exit);
        Assert.NotEmpty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_PrefixBrowse_InferredPlatformTypo_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Runtime.CompilerService", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("System.Runtime.CompilerService", error);
        Assert.Contains("System.Runtime.CompilerServices.CompilerGeneratedAttribute", output);
    }

    [Fact]
    public async Task Router_BareSimpleType_UsesTargetBoundPlatformCatalog()
    {
        var (exit, output, error) = await RunAppAsync(
            "Regex", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.RegularExpressions.Regex", output);
        AssertLibraryAsset(output, "System.Text.RegularExpressions");
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("String", "System.String")]
    [InlineData("Object", "System.Object")]
    [InlineData("Boolean", "System.Boolean")]
    [InlineData("Void", "System.Void")]
    public async Task Router_BareBclTypeName_IgnoresNestedSimpleNameCollisions(
        string query,
        string expectedType)
    {
        var (exit, output, error) = await RunAppAsync(
            query, "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains($"# {expectedType}", output);
        Assert.DoesNotContain("## Package Info", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_NestedPlatformTypePlusSyntax_RemainsResolvable()
    {
        var (exit, output, error) = await RunAppAsync(
            "JSType+String", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(
            "# System.Runtime.InteropServices.JavaScript.JSType.String",
            output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_AmbiguousTargetBoundPlatformCatalog_ReportsAmbiguity()
    {
        var (exit, output, error) = await RunAppAsync(
            "Timer", "--markdown", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Type 'Timer' matched multiple platform types.",
            error);
        Assert.DoesNotContain("Package 'timer'", error);
    }

    [Fact]
    public async Task Router_AmbiguousTargetBoundPlatformMember_ReportsAmbiguity()
    {
        var (exit, output, error) = await RunAppAsync(
            "Timer.Start", "--markdown", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Type 'Timer' matched multiple platform types.",
            error);
        Assert.DoesNotContain("No members matched", error);
    }

    [Fact]
    public async Task Router_BareGenericType_UsesTargetBoundPlatformCatalog()
    {
        var (exit, output, error) = await RunAppAsync(
            "List<T>", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Collections.Generic.List&lt;T&gt;", output);
        AssertLibraryAsset(output, "System.Collections");
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<T>", "System.Collections")]
    [InlineData("System.Span<T>", "System.Runtime")]
    [InlineData("System.Threading.Tasks.Task<TResult>", "System.Runtime")]
    [InlineData("System.Collections.Generic.IEnumerable<T>", "System.Runtime")]
    public async Task Router_FullyQualifiedGenericPlatformType_PreservesContractSource(
        string typeName,
        string expectedLibrary)
    {
        var (exit, output, error) = await RunAppAsync(
            typeName, "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        AssertLibraryAsset(output, expectedLibrary);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<T>.Add")]
    [InlineData("List<T>.Add")]
    public async Task Router_GenericPlatformMember_UsesPlatformHouseDocumentation(
        string target)
    {
        var (exit, output, error) = await RunAppAsync(
            target, "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "Represents a strongly typed list of objects",
            output);
        Assert.Contains(
            "Adds an object to the end of the List.",
            output);
    }

    [Theory]
    [InlineData("List<T,U>.Add")]
    [InlineData("System.Collections.Generic.List<T,U>.Add")]
    [InlineData("System.Collections.Generic.List`2.Add")]
    [InlineData("System.Collections.Generic.List`0.Add")]
    [InlineData("System.Collections.Generic.List`.Add")]
    [InlineData("System.Collections.Generic.List`999999999999999999999.Add")]
    [InlineData("System.Collections.Generic.Dictionary<TKey,>")]
    [InlineData("System.Collections.Generic.Dictionary<,TValue>")]
    [InlineData("System.Collections.Generic.Dictionary<List<>,TValue>")]
    [InlineData("System.Collections.Generic.Dictionary<TKey,<TValue>>")]
    [InlineData("System.Collections.Generic.Dictionary<List<T>U,TValue>")]
    [InlineData("System.Collections.Generic.Dictionary<List<T><U>,TValue>")]
    [InlineData("System.Collections.Generic.Dictionary<List<T>[,TValue>")]
    [InlineData("System.Collections.Generic.List<?>")]
    [InlineData("System.Collections.Generic.List<.T>")]
    [InlineData("System.Collections.Generic.List<T?*>")]
    [InlineData("System.Collections.Generic.List<T U?>")]
    [InlineData("System.Threading.Tasks.Task<T1,T2>")]
    public async Task Router_ExplicitMissingGenericArity_DoesNotBroaden(
        string target)
    {
        var (exit, output, error) = await RunAppAsync(
            target, "--markdown", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.True(
            error.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || error.Contains("valid package ID", StringComparison.OrdinalIgnoreCase),
            error);
    }

    [Theory]
    [InlineData("System.Collections.Generic.Dictionary<List<T>?,string>")]
    [InlineData("System.Collections.Generic.Dictionary<List<T>[,],string>")]
    [InlineData("System.Collections.Generic.List<(int,string)>")]
    [InlineData("System.Action<(int,string)>")]
    public async Task Router_ValidNestedGenericSuffixResolvesExactType(
        string target)
    {
        string[] tail =
        [
            "--platform",
            "System.Private.CoreLib",
            "-S",
            "Type Info",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var deferred = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
    }

    [Theory]
    [InlineData(
        "System.Collections.Concurrent.ConcurrentDictionary<TKey,TValue>.AlternateLookup<TAlternateKey>",
        "TryAdd",
        false)]
    [InlineData(
        "System.Collections.Concurrent.ConcurrentDictionary<TKey,TValue>.AlternateLookup<TAlternateKey>",
        "TryAdd",
        true)]
    [InlineData(
        "System.Delegate.InvocationListEnumerator<TDelegate>",
        "MoveNext",
        false)]
    [InlineData(
        "System.Delegate.InvocationListEnumerator<TDelegate>",
        "MoveNext",
        true)]
    public async Task Router_ExactCSharpInnerGenericTypePreservesSharedMemberFilter(
        string target,
        string member,
        bool explicitPlatform)
    {
        string[] tail =
        [
            "-m",
            member,
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];
        string[] scopedTail = explicitPlatform
            ? ["--platform", "System.Private.CoreLib", .. tail]
            : tail;

        var direct = await RunAppAsync(
            ["type", target, .. scopedTail]);
        var routed = await RunAppAsync(
            [target, .. scopedTail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("## Type Info", routed.Output);
    }

    [Fact]
    public async Task Router_ExplicitMemberOptionUsesNestedMetadataTypeIdentity()
    {
        const string target =
            "System.Collections.Generic.List`1.Enumerator";
        string[] tail =
        [
            "-m",
            "MoveNext",
            "--platform",
            "System.Private.CoreLib",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains(
            "System.Collections.Generic.List<T>.Enumerator",
            routed.Output);
    }

    [Fact]
    public async Task Router_SourcelessMemberOptionUsesNestedMetadataTypeIdentity()
    {
        const string target =
            "System.Collections.Generic.List`1.Enumerator";
        string[] tail =
        [
            "-m",
            "MoveNext",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains(
            "System.Collections.Generic.List<T>.Enumerator",
            routed.Output);
    }

    [Fact]
    public async Task Router_UnboundGenericTypePreservesMemberArity()
    {
        const string target =
            "System.Collections.Generic.Dictionary<,>";
        string[] tail =
        [
            "--platform",
            "System.Collections",
            "-m",
            "TryGetValue",
            "-S",
            "Signature",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["member", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Equal("1", routed.Output.Trim());
    }

    [Fact]
    public async Task Router_OperatorLikeGenericArgumentRemainsTypeTarget()
    {
        const string target =
            "System.Collections.Generic.List<System.op_Addition>";
        string[] tail =
        [
            "--platform",
            "System.Private.CoreLib",
            "-D",
            SectionNames.TypeInfo,
            "--schema",
            "--table",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("Type", routed.Output);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<>")]
    [InlineData("System.Collections.Generic.Dictionary<,>")]
    public async Task Router_UnboundGenericTypeResolvesItsDeclaredArity(
        string target)
    {
        var (exit, output, error) = await RunAppAsync(
            target,
            "-S",
            "Type Info",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count) && count > 0);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("Span<T>", "System.Span")]
    [InlineData("Task<TResult>", "System.Threading.Tasks.Task")]
    [InlineData("IEnumerable<T>", "System.Collections.Generic.IEnumerable")]
    public async Task Router_UnqualifiedGenericSameIdentity_UsesRuntimeContract(
        string target,
        string expectedType)
    {
        var (exit, output, error) = await RunAppAsync(
            target, "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains($"# {expectedType}", output);
        Assert.DoesNotContain(
            "Platform type lookup is ambiguous",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Router_UnqualifiedGenericPlatformMember_UsesSelectedRuntimeCatalog()
    {
        var (exit, output, error) = await RunAppAsync(
            "SequenceReader<T>.TryRead", "--all", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Buffers.SequenceReader&lt;T&gt;", output);
        Assert.Contains("TryRead", output);
    }

    [Fact]
    public async Task Router_UnqualifiedGenericPlatformMember_ExplicitSourceDisambiguates()
    {
        var (exit, output, error) = await RunAppAsync(
            "SequenceReader<T>.TryRead",
            "--platform",
            "System.Memory",
            "--all",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "# System.Buffers.SequenceReader&lt;T&gt;",
            output);
        Assert.Contains("TryRead", output);
    }

    [Fact]
    public async Task Router_GenericPlatformType_UserFrameworkIsNotDuplicated()
    {
        string[] args =
        [
            "System.Collections.Generic.List<T>",
            "--framework",
            "runtime",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var (exit, output, error) = await RunAppAsync(args);
        Assert.Equal(0, exit);
        Assert.Empty(error);
        AssertPlatformTypeInfo(output, "System.Collections");

        var count = await RunAppAsync([.. args, "--count"]);
        Assert.Equal(0, count.Exit);
        Assert.Empty(count.Error);
        Assert.Equal(
            CountRenderedMarkdownTableRowsBySection(output)["Type Info"]
                .ToString(CultureInfo.InvariantCulture),
            count.Output.Trim());
    }

    [Theory]
    [InlineData("System.Threading.Tasks.Task<T>")]
    [InlineData("System.Threading.Tasks.Task<T>.Result")]
    public async Task Router_GenericPlatformTarget_UsesExplicitFrameworkOwner(
        string target)
    {
        var (exit, output, error) = await RunAppAsync(
            target,
            "--framework",
            "netstandard",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("System.Threading.Tasks.Task", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_NetStandardForwardedMember_UsesContractDocumentation()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Threading.Tasks.Task<T>.Result",
            "--framework",
            "netstandard",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "Gets the result value of this Task.",
            output);
    }

    [Fact]
    public async Task Router_DeferredExactTypePreservesBodyKindQuery()
    {
        string[] arguments =
        [
            "System.Collections.Immutable.ImmutableArray<T>.Builder",
            "--platform",
            "System.Collections.Immutable",
            "--where",
            "Kind=InvocationExpression",
            "--table",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", .. arguments]);
        var deferred = await RunAppAsync(arguments);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
        Assert.Contains("InvocationExpression", deferred.Output);
    }

    [Fact]
    public async Task Router_DeferredStaticDiscoveryWithBodyKindStaysOffline()
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        var (exit, output, error) = await RunAppAsync(
            [
                "Missing.Generic<T>.Add",
                "--library",
                missingAssembly,
                "--where",
                "Kind=InvocationExpression",
                "-D",
                "--schema",
                "--tips",
                "q"
            ]);

        Assert.Equal(0, exit);
        Assert.Contains(
            "| [member/member-target/ApiMemberDetail] Body Shapes | section |",
            output);
        Assert.Contains(
            "| [type/type/ApiMember] Body Shapes | section |",
            output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_DeferredMemberStaticDiscoveryKeepsBodyKindSection()
    {
        string[] common =
        [
            "--platform",
            "System.Collections.Immutable",
            "--where",
            "Kind=InvocationExpression",
            "-D",
            "--schema",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "member",
                "System.Collections.Immutable.ImmutableArray<T>.Builder",
                "-m",
                "Add",
                .. common
            ]);
        var deferred = await RunAppAsync(
            [
                "System.Collections.Immutable.ImmutableArray<T>.Builder.Add",
                .. common
            ]);

        Assert.Equal(0, direct.Exit);
        Assert.Equal(0, deferred.Exit);
        Assert.Contains("| Body Shapes | section |", direct.Output);
        Assert.Contains(
            "| [member/member-target/ApiMemberDetail] Body Shapes | section |",
            deferred.Output);
        Assert.Empty(direct.Error);
        Assert.Empty(deferred.Error);
    }

    [Theory]
    [InlineData("System.String.IndexOf:1", "IndexOf:2")]
    [InlineData("System.String.IndexOf~aaaaaaaaaa", "IndexOf~bbbbbbbbbb")]
    [InlineData("System.String.IndexOf:1", "IndexOf~aaaaaaaaaa")]
    public async Task Router_DeferredMemberRejectsConflictingOverloadSelectors(
        string target,
        string explicitSelector)
    {
        var (exit, output, error) = await RunAppAsync(
            target,
            "--platform",
            "System.Runtime",
            "-m",
            explicitSelector,
            "-S",
            "Signature",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "cannot combine different overload selectors",
            error);
    }

    [Fact]
    public async Task Router_DeferredMemberAcceptsMatchingOverloadSelectors()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.String.IndexOf:1",
            "--platform",
            "System.Runtime",
            "-m",
            "IndexOf:1",
            "-S",
            "Signature",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(
        "System.Collections.Generic.List<T>.Add~590da2",
        "Add~590da203f0")]
    [InlineData(
        "System.Collections.Generic.List<T>.Add~590da203f0",
        "Add~590da2")]
    public async Task Router_DeferredMemberAcceptsCompatibleDigestPrefixes(
        string target,
        string explicitSelector)
    {
        var (exit, output, error) = await RunAppAsync(
            target,
            "--platform",
            "System.Collections",
            "-m",
            explicitSelector,
            "-S",
            "Signature",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("Add", output);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(
        "System.Collections.Generic.List<T>",
        "Type Info")]
    [InlineData(
        "System.Collections.Generic.List<T>.Add:1",
        "Signature")]
    public async Task Router_GenericStaticSchemaDoesNotResolveFramework(
        string target,
        string expectedSchemaItem)
    {
        var (exit, output, error) = await RunAppAsync(
            target,
            "--framework",
            "runtime@0.0.0",
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains(expectedSchemaItem, output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_GenericPlatformMethod_UserFrameworkIsNotDuplicated()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Threading.Tasks.Task.FromResult<TResult>:1",
            "--framework",
            "runtime",
            "-S",
            "Signature",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_DeferredVersionedPlatformMember_PreservesRequestedTarget()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.String.IndexOf",
            "--framework",
            "runtime@10.0.10",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Version: 10.0.10", output);
        Assert.Contains(
            "Reports the zero-based index of the first occurrence",
            output);
    }

    [Fact]
    public async Task Router_DeferredLegacyRuntimeMember_PreservesReferencePackTfm()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.String.IndexOf",
            "--framework",
            "runtime@3.1.0",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Version: 3.1.0", output);
        Assert.Contains("TFM: netcoreapp3.1", output);
        Assert.Contains(
            "Reports the zero-based index of the first occurrence",
            output);
    }

    [Fact]
    public async Task Router_DeferredVersionedMember_UsesTargetCatalogLibrary()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.AppDomain.FriendlyName",
            "--framework",
            "runtime@3.1.0",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Version: 3.1.0", output);
        Assert.Contains("TFM: netcoreapp3.1", output);
        Assert.Contains("System.Runtime.Extensions.dll", output);
        Assert.Contains(
            "Gets the friendly name of this application domain.",
            output);
    }

    [Fact]
    public async Task Type_VersionedPlatformTarget_UsesTargetCatalogLibrary()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.AppDomain",
            "--framework",
            "runtime@3.1.0",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Version: 3.1.0", output);
        Assert.Contains("TFM: netcoreapp3.1", output);
        Assert.Contains("System.Runtime.Extensions.dll", output);
    }

    [Fact]
    public async Task Member_VersionedCoreType_UsesReferenceDocumentation()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.String",
            "--framework",
            "runtime@10.0.10",
            "-S",
            "Methods",
            "--markdown",
            "--verbose",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains(
            "Using platform ref library: runtime 10.0.10",
            error);
        Assert.Contains(
            "Extracting API from: System.Runtime.dll",
            error);
        Assert.Contains(
            "Represents text as a sequence of UTF-16 code units.",
            output);
        Assert.Contains(
            "Reports the zero-based index of the first occurrence",
            output);
    }

    [Fact]
    public async Task Type_VersionedPlatformMiss_DoesNotBrowseCurrentCatalog()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.TimeProvider",
            "--framework",
            "runtime@3.1.0",
            "--markdown",
            "--verbose",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.DoesNotContain("Showing best-effort platform prefix", error);
        Assert.DoesNotContain("runtime 11", error);
        Assert.DoesNotContain("# System.TimeProvider", output);
    }

    [Fact]
    public async Task Type_VersionedPlatformPrefixBrowse_UsesRequestedCatalogIdentity()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.Time",
            "--framework",
            "runtime@3.1.0",
            "--json",
            "--verbose",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains(
            "Resolved from installed packs: runtime 3.1.0",
            error);
        Assert.Contains(
            "libraries in runtime@3.1.0",
            error);
        Assert.DoesNotContain(
            "runtime@3.1.0@3.1.0",
            error);
        Assert.DoesNotContain("runtime 11", error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "3.1.0",
            document.RootElement.GetProperty("version").GetString());
        Assert.Equal(
            "netcoreapp3.1",
            document.RootElement.GetProperty("tfm").GetString());
        Assert.Contains("System.TimeSpan", output);
        Assert.DoesNotContain("System.TimeProvider", output);
    }

    [Fact]
    public async Task Router_DeferredExactTypeRejectsUniversallyInvalidSectionBeforeAcquisition()
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        string[] arguments =
        [
            "Missing.Generic<T>",
            "--library",
            missingAssembly,
            "-S",
            "DefinitelyNotASection",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var deferred = await RunAppAsync(arguments);

        Assert.Equal(1, direct.Exit);
        Assert.Equal(1, deferred.Exit);
        Assert.Empty(direct.Output);
        Assert.Empty(deferred.Output);
        Assert.Contains("Select value 'DefinitelyNotASection' not found.", direct.Error);
        Assert.Contains("Select value 'DefinitelyNotASection' not found.", deferred.Error);
        Assert.DoesNotContain("File not found", deferred.Error);
        Assert.DoesNotContain("  Classes", deferred.Error);
        Assert.DoesNotContain("  Inspection Failures", deferred.Error);
    }

    [Fact]
    public async Task Router_DeferredPartialSectionDiagnosticIsEmittedOnce()
    {
        string[] arguments =
        [
            "System.Collections.Immutable.ImmutableArray<T>.Builder",
            "--platform",
            "System.Collections.Immutable",
            "-S",
            "Methods,DefinitelyNotASection",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", .. arguments]);
        var deferred = await RunAppAsync(arguments);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
        Assert.Equal(
            1,
            deferred.Error.Split('\n').Count(
                static line => line.Contains(
                    "Select value 'DefinitelyNotASection' not found.",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Router_DeferredOptionShapeRejectsBeforeAcquisition()
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        string[] arguments =
        [
            "Missing.Generic<T>",
            "--library",
            missingAssembly,
            "--json-array",
            "--json",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", .. arguments]);
        var deferred = await RunAppAsync(arguments);

        Assert.Equal(direct, deferred);
        Assert.Equal(1, deferred.Exit);
        Assert.Empty(deferred.Output);
        Assert.Contains(
            "--json-array requires --value, --urls, --paths, or --print.",
            deferred.Error);
        Assert.DoesNotContain("File not found", deferred.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SectionNames.CallGraph)]
    public async Task Router_DeferredTreeFormatConflictRejectsBeforeAcquisition(
        string? section)
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        List<string> tail =
        [
            "--library",
            missingAssembly,
            "--tree",
            "--json",
            "--tips",
            "q"
        ];
        if (section is not null)
            tail.InsertRange(2, ["-S", section]);

        var direct = await RunAppAsync(
            ["member", "Missing.Generic<T>", "-m", "Member", .. tail]);
        var deferred = await RunAppAsync(
            ["Missing.Generic<T>.Member", .. tail]);

        Assert.Equal(direct, deferred);
        Assert.Equal(1, deferred.Exit);
        Assert.Empty(deferred.Output);
        Assert.Contains(
            "--tree is a standalone output format and cannot combine with another output format.",
            deferred.Error);
        Assert.DoesNotContain("File not found", deferred.Error);
        Assert.DoesNotContain("Document --json cannot represent", deferred.Error);
    }

    [Fact]
    public async Task Router_DottedOverloadStaticDiscoveryUsesDetailPipeline()
    {
        string[] arguments =
        [
            "List<T>.Add:1",
            "--platform",
            "System.Collections",
            "-D",
            "Signature",
            "--schema",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "member",
                "List<T>",
                "--platform",
                "System.Collections",
                "-m",
                "Add:1",
                .. arguments[3..]
            ]);
        var deferred = await RunAppAsync(arguments);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
        Assert.Contains("| Signature | column |", deferred.Output);
    }

    [Fact]
    public async Task Router_DottedDigestStaticDiscoveryUsesDetailPipeline()
    {
        const string typeName =
            "DotnetInspect.Cli.Tests.Operators<T>";
        var inventory = await RunAppAsync(
            "member",
            typeName,
            "--library",
            TestAssemblyPath,
            "-m",
            "Convert",
            "-S",
            "Member Index",
            "--columns",
            "Stable",
            "--tsv",
            "--tips",
            "q");
        Assert.Equal(0, inventory.Exit);
        var stableSelector = inventory.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .First();

        string[] discovery =
        [
            "-D",
            "Signature",
            "--schema",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "member",
                typeName,
                "--library",
                TestAssemblyPath,
                "-m",
                stableSelector,
                .. discovery
            ]);
        var deferred = await RunAppAsync(
            [
                $"{typeName}.{stableSelector}",
                "--library",
                TestAssemblyPath,
                .. discovery
            ]);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
        Assert.Contains("| Signature | column |", deferred.Output);
    }

    [Fact]
    public async Task Router_DeferredExactTypeReusesResolvedApiSurface()
    {
        string[] arguments =
        [
            "System.Collections.Immutable.ImmutableArray<T>.Builder",
            "--platform",
            "System.Collections.Immutable",
            "--markdown",
            "--verbose",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var deferred = await RunAppAsync(arguments);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
        Assert.Equal(
            1,
            deferred.Error.Split('\n').Count(
                static line => line.Contains(
                    "Extracting API from:",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Router_DeferredExactTypePreservesTypeDocumentationDefaults()
    {
        string[] arguments =
        [
            "SequenceReader<T>",
            "--platform",
            "System.Memory",
            "--markdown",
            "-S",
            "Methods",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", .. arguments]);
        var deferred = await RunAppAsync(arguments);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
    }

    [Theory]
    [InlineData("--tree")]
    public async Task Router_DeferredExactTypePreservesTypeOnlyOutput(
        string outputOption)
    {
        const string target = "SequenceReader<T>";
        var direct = await RunAppAsync(
            "type",
            target,
            "--platform",
            "System.Memory",
            outputOption,
            "--tips",
            "q");
        var deferred = await RunAppAsync(
            target,
            "--platform",
            "System.Memory",
            outputOption,
            "--tips",
            "q");

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
    }

    [Theory]
    [InlineData("--focus", "allocation")]
    [InlineData("--index", "1")]
    public async Task Router_DeferredExactTypeRejectsMemberOnlyOption(
        params string[] memberOnlyOption)
    {
        string[] arguments =
        [
            "System.Collections.Immutable.ImmutableArray<T>.Builder",
            "--platform",
            "System.Collections.Immutable",
            .. memberOnlyOption,
            "--tips",
            "q"
        ];
        var (exit, output, error) = await RunAppAsync(arguments);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Unrecognized option '{memberOnlyOption[0]}'",
            error);
    }

    [Fact]
    public async Task Router_DeferredExactTypePreservesProjectSourceConflict()
    {
        string[] tail =
        [
            "--platform",
            "System.Text.Json",
            "--project",
            "/tmp/missing.csproj",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            ["type", "System.Text.Json.JsonSerializer", .. tail]);
        var routed = await RunAppAsync(
            ["System.Text.Json.JsonSerializer", .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains(
            "--project cannot be combined",
            routed.Error);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task Router_DeferredProjectSourcePreservesRepeatedOptionArity(
        int projectCount,
        bool explicitPlatform)
    {
        var repositoryRoot =
            CommandErrorOwnershipTests.RepositoryRoot();
        string[] projects =
        [
            Path.Combine(
                repositoryRoot,
                "src",
                "DotnetInspect.Cli",
                "DotnetInspect.Cli.csproj"),
            Path.Combine(
                repositoryRoot,
                "src",
                "CSharpText",
                "CSharpText.csproj"),
            Path.Combine(
                repositoryRoot,
                "tests",
                "DotnetInspect.Cli.Tests",
                "DotnetInspect.Cli.Tests.csproj")
        ];
        List<string> tail = explicitPlatform
            ? ["--platform", "System.Text.Json"]
            : [];
        for (var i = 0; i < projectCount; i++)
        {
            tail.Add("--project");
            tail.Add(projects[i]);
        }
        tail.AddRange(["--tips", "q"]);

        var target = explicitPlatform
            ? "System.Text.Json.JsonSerializer"
            : "Markout.MarkoutSerializer";
        var direct = await RunAppAsync(
            ["type", target, .. tail]);
        var routed = await RunAppAsync(
            [target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains(
            $"expects a single argument but {projectCount} were provided",
            routed.Error);
    }

    [Fact]
    public async Task Router_DeferredExactTypePreservesSharedMemberFilter()
    {
        const string target =
            "System.Collections.Immutable.ImmutableArray<T>.Builder";
        string[] tail =
        [
            "--platform",
            "System.Collections.Immutable",
            "-m",
            "Add",
            "--tree",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var deferred = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
    }

    [Fact]
    public async Task Router_ExplicitTypeFilterSelectsTypeParser()
    {
        var target = typeof(SampleGenericClass<>).FullName!
            .Replace("`1", "<T>", StringComparison.Ordinal);
        string[] tail =
        [
            "--library",
            TestAssemblyPath,
            "-t",
            "*SampleGenericClass*",
            "-S",
            "API Info",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Fact]
    public async Task Router_ExplicitLibraryQualifiedMemberUsesAssemblySource()
    {
        const string typeName =
            "DotnetInspect.Cli.Tests.TypeTargetedDecodeTests";
        const string memberName =
            "TypeTargetedBuild_MatchesFullBuild_ForEveryMethodOfTheType";
        string[] tail =
        [
            "--library",
            TestAssemblyPath,
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            ["member", typeName, "-m", memberName, .. tail]);
        var routed = await RunAppAsync(
            [$"{typeName}.{memberName}", .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Equal("1", routed.Output.Trim());
    }

    [Fact]
    public async Task Router_GenericTypeFilterPreservesPlatformOwner()
    {
        SkipUnlessAspNetCoreAvailable();
        const string target =
            "Microsoft.AspNetCore.Components.Endpoints.FormMapping"
            + ".ArrayPoolBufferAdapter<T1,T2,T3>";

        string[] args =
        [
            target,
            "-t",
            "5",
            "--all",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var (exit, output, error) = await RunAppAsync(args);
        Assert.Equal(0, exit);
        Assert.Empty(error);
        AssertPlatformTypeInfo(output, "Microsoft.AspNetCore.Components.Endpoints");

        var count = await RunAppAsync([.. args, "--count"]);
        Assert.Equal(0, count.Exit);
        Assert.Empty(count.Error);
        Assert.Equal(
            CountRenderedMarkdownTableRowsBySection(output)["Type Info"]
                .ToString(CultureInfo.InvariantCulture),
            count.Output.Trim());
    }

    [Fact]
    public async Task Router_GenericMemberFilterPreservesPlatformOwner()
    {
        SkipUnlessAspNetCoreAvailable();
        const string target =
            "Microsoft.AspNetCore.Components.Endpoints.FormMapping"
            + ".ArrayPoolBufferAdapter<T1,T2,T3>";

        var (exit, output, error) = await RunAppAsync(
            target,
            "-m",
            "explicit:ToResult",
            "--framework",
            "aspnetcore",
            "--all",
            "-S",
            "Member Index",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_BareQualifiedExplicitInterfaceKeepsLongestTypePrefix()
    {
        const string typeName =
            "System.Collections.Generic.List<T>";
        const string memberName =
            "System.Collections.IList.IsReadOnly";
        string[] tail =
        [
            "--all",
            "-S",
            "Member Index",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
        [
            "member",
            typeName,
            "--platform",
            "System.Collections",
            "-m",
            memberName,
            .. tail
        ]);
        var routed = await RunAppAsync(
            [$"{typeName}.{memberName}", .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Equal("1", routed.Output.Trim());
    }

    [Fact]
    public async Task Router_GlobalAliasWhitespaceGenericArgumentResolvesExactType()
    {
        const string target =
            "System.Action<global :: System.String>";
        string[] tail =
        [
            "--platform",
            "System.Private.CoreLib",
            "-S",
            "Type Info",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            ["type", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Fact]
    public async Task Router_DeferredExactTypeRejectsEmbeddedMermaid()
    {
        var target = typeof(SampleGenericClass<>).FullName!
            .Replace("`1", "<T>", StringComparison.Ordinal);
        string[] tail =
        [
            "--library",
            TestAssemblyPath,
            "--markdown",
            "--mermaid",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains("Unrecognized option '--mermaid'", routed.Error);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<T>")]
    [InlineData("List<T>")]
    public async Task Router_GenericTypeTargetWithMemberFilterUsesMemberCommand(
        string target)
    {
        string[] tail =
        [
            "--platform",
            "System.Collections",
            "-m",
            "ConvertAll<TOutput>",
            "-S",
            "Signature",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["member", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Equal("1", routed.Output.Trim());
    }

    [Fact]
    public async Task Router_QualifiedGenericMemberFilterUsesTopLevelBoundary()
    {
        var target = typeof(MemberGenericSelectorFixture).FullName!;
        string[] tail =
        [
            "--library",
            TestAssemblyPath,
            "-m",
            "GenericChoice<System.String>",
            "-S",
            "Signature",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["member", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Equal("1", routed.Output.Trim());
    }

    [Fact]
    public async Task Router_GenericTypeTargetPreservesDigestMemberSelector()
    {
        const string target = "System.Collections.Generic.List<T>";
        var inventory = await RunAppAsync(
            "member",
            target,
            "--platform",
            "System.Collections",
            "-m",
            "ConvertAll<TOutput>",
            "-S",
            "Member Index",
            "--table",
            "--tips",
            "q");
        Assert.Equal(0, inventory.Exit);
        var selector = inventory.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("ConvertAll", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
            .Single();
        Assert.Contains('~', selector);

        string[] tail =
        [
            "--platform",
            "System.Collections",
            "-m",
            selector,
            "-S",
            "Signature",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["member", target, .. tail]);
        var routed = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Equal("1", routed.Output.Trim());
    }

    [Fact]
    public async Task Router_DeferredExactTypePreservesNumericMemberFilter()
    {
        const string target =
            "System.Collections.Immutable.ImmutableArray<T>.Builder";
        string[] tail =
        [
            "--platform",
            "System.Collections.Immutable",
            "-m",
            "1",
            "-S",
            "Member Index",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var deferred = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, deferred);
        Assert.Equal(1, deferred.Exit);
        Assert.Empty(deferred.Output);
        Assert.Contains(
            "No members matched filter '1'",
            deferred.Error);
    }

    [Theory]
    [InlineData("Add:1")]
    [InlineData("Add~ffffffff")]
    public async Task Router_DeferredExactTypePreservesLiteralTypeMemberFilter(
        string memberFilter)
    {
        const string target =
            "System.Collections.Immutable.ImmutableArray<T>.Builder";
        string[] tail =
        [
            "--platform",
            "System.Collections.Immutable",
            "-m",
            memberFilter,
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var deferred = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, deferred);
        Assert.Equal(1, deferred.Exit);
    }

    [Fact]
    public async Task Router_DeferredExactTypeRejectsGenericArityMemberFilter()
    {
        const string target =
            "System.Collections.Immutable.ImmutableArray<T>.Builder";
        string[] tail =
        [
            "--platform",
            "System.Collections.Immutable",
            "-m",
            "Add<X>",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["type", target, .. tail]);
        var deferred = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, deferred);
        Assert.Equal(1, deferred.Exit);
    }

    [Fact]
    public async Task Router_DeferredStaticSchemaDoesNotResolveSource()
    {
        var missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.dll");
        var (exit, output, error) = await RunAppAsync(
            "Missing.Generic<T>",
            "--library",
            missingAssembly,
            "-D",
            "--schema",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("Type Info", output);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("DotnetInspect.Cli.Tests.SampleGenericClass<T>")]
    [InlineData("SampleGenericClass<T>")]
    public async Task Router_ExplicitLibraryGenericTypeUsesAssemblySource(
        string target)
    {
        string[] arguments =
        [
            target,
            "--library",
            TestAssemblyPath,
            "-S",
            "Type Info",
            "--count",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Fact]
    public async Task Router_DeferredMemberOverloadSuffixSurvivesMetadataBoundary()
    {
        const string typeName =
            "DotnetInspect.Cli.Tests.Operators<T>";
        const string target =
            $"{typeName}.Convert<TResult>:1";
        string[] projection =
        [
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
        [
            "member",
            typeName,
            "--library",
            TestAssemblyPath,
            "-m",
            "Convert<TResult>:1",
            .. projection
        ]);
        var routed = await RunAppAsync(
        [
            target,
            "--library",
            TestAssemblyPath,
            .. projection
        ]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Equal("1", routed.Output.Trim());
    }

    [Fact]
    public async Task Router_DeferredMemberDigestSurvivesMetadataBoundary()
    {
        const string typeName =
            "DotnetInspect.Cli.Tests.Operators<T>";
        var inventory = await RunAppAsync(
            "member",
            typeName,
            "--library",
            TestAssemblyPath,
            "-m",
            "Convert",
            "-S",
            "Member Index",
            "--columns",
            "Stable",
            "--tsv",
            "--tips",
            "q");

        Assert.Equal(0, inventory.Exit);
        Assert.Empty(inventory.Error);
        var stableSelector = inventory.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .First();
        Assert.StartsWith("Convert~", stableSelector);

        var (exit, output, error) = await RunAppAsync(
            $"{typeName}.{stableSelector}",
            "--library",
            TestAssemblyPath,
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(".ctor")]
    [InlineData(".ctor:1")]
    public async Task Router_DeferredConstructorSuffixUsesMemberRendering(
        string memberSelector)
    {
        const string typeName =
            "DotnetInspect.Cli.Tests.Operators<T>";
        string[] projection = ["--table", "--tips", "q"];
        var direct = await RunAppAsync(
        [
            "member",
            typeName,
            "--library",
            TestAssemblyPath,
            "-m",
            memberSelector,
            .. projection
        ]);
        var routed = await RunAppAsync(
        [
            $"{typeName}.{memberSelector}",
            "--library",
            TestAssemblyPath,
            .. projection
        ]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Fact]
    public async Task Router_DeferredConstructorDigestSurvivesMetadataBoundary()
    {
        const string typeName =
            "DotnetInspect.Cli.Tests.Operators<T>";
        var inventory = await RunAppAsync(
            "member",
            typeName,
            "--library",
            TestAssemblyPath,
            "-m",
            ".ctor",
            "-S",
            "Member Index",
            "--columns",
            "Stable",
            "--tsv",
            "--tips",
            "q");

        Assert.Equal(0, inventory.Exit);
        Assert.Empty(inventory.Error);
        var stableSelector = inventory.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .First();
        Assert.StartsWith(".ctor~", stableSelector);

        var (exit, output, error) = await RunAppAsync(
            $"{typeName}.{stableSelector}",
            "--library",
            TestAssemblyPath,
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(".cctor")]
    [InlineData(".CCTOR")]
    public async Task Router_DeferredStaticConstructorPreservesCaseInsensitiveParity(
        string memberSelector)
    {
        const string typeName =
            "System.Collections.Generic.EqualityComparer<T>";
        string[] tail =
        [
            "--platform",
            "System.Private.CoreLib",
            "--all",
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
        [
            "member",
            typeName,
            "-m",
            memberSelector,
            .. tail
        ]);
        var deferred = await RunAppAsync(
        [
            $"{typeName}.{memberSelector}",
            .. tail
        ]);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
        Assert.Equal("1", deferred.Output.Trim());
    }

    [Fact]
    public async Task Router_ExplicitPackageBoundaryUsesAcquiredMetadata()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Collections.Concurrent.ConcurrentDictionary<TKey,TValue>.AlternateLookup<TAlternateKey>",
            "--package",
            "System.Collections.Concurrent@4.3.0",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("AlternateLookup", error);
    }

    [Theory]
    [InlineData("Dictionary<TKey,TValue>.KeyCollection")]
    [InlineData("Dictionary`2.KeyCollection")]
    public async Task Router_UnqualifiedNestedGenericType_RoutesAsExactType(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            typeName, "--tree", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(
            "sealed class System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection",
            output);
        Assert.DoesNotContain("No members matched", error);
    }

    [Theory]
    [InlineData("ConcurrentDictionary<TKey,TValue>.AlternateLookup<TAlternateKey>.TryAdd")]
    [InlineData("ConcurrentDictionary`2.AlternateLookup`1.TryAdd")]
    public async Task Router_GenericNestedTypeMember_UsesLongestExactTypePrefix(string target)
    {
        var (exit, output, error) = await RunAppAsync(
            target, "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("TryAdd", output);
        Assert.Contains("TAlternateKey key", output);
    }

    [Fact]
    public async Task Router_VoidKeyword_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "void", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Package 'void'", error);
    }

    [Fact]
    public async Task Router_FullyQualifiedPlatformMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.String.IndexOf", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Contains("public int IndexOf(char value)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_SimplePlatformMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.IndexOf", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Contains("public int IndexOf(char value)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_PrimitiveKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "string.IndexOf", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf", output);
        Assert.Contains("public int IndexOf(char value)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_NumericKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "int.Parse", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Parse", output);
        Assert.Contains("Return Type", output);
        Assert.Contains("int", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_BooleanKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "bool.TryParse", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("TryParse", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_ObjectKeywordMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "object.GetType", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("GetType", output);
        Assert.Contains("System.Type GetType()", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_GenericMemberSelector_NormalizesGenericTypeArguments()
    {
        var (exit, output, error) = await RunAppAsync(
            "JsonSerializer.Deserialize<TValue>", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Deserialize", output);
        Assert.Contains("Deserialize<TValue>", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_ConstructorSelector_NormalizesCtorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.ctor", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("public String(", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_DoubleDotConstructorSelector_NormalizesCtorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "List<T>..ctor", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("public List(", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_DoubleDotConstructorSelector_PreservesOverloadIndex()
    {
        var (exit, output, error) = await RunAppAsync(
            "List<T>..ctor:3", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("(int)", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_IndexerSelector_NormalizesThisAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.this[]", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Chars", output);
        Assert.Contains("this[int index]", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_IndexerSelector_NormalizesThisAliasWithIllustrativeArgument()
    {
        var (exit, output, error) = await RunAppAsync(
            "String.this[0]", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Chars", output);
        Assert.Contains("this[int index]", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_GenericIndexerSelector_NormalizesThisAliasWithTypeArgument()
    {
        var (exit, output, error) = await RunAppAsync(
            "Dictionary<TKey,TValue>.this[TKey]", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Item", output);
        Assert.Contains("this[TKey key]", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_OperatorSelector_NormalizesOperatorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "DateTime.operator+", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("operator +", output);
        Assert.Contains("public static System.DateTime operator +", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_ConversionSelector_NormalizesImplicitAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "Decimal.implicit", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("implicit operator", output);
        Assert.Contains("public static implicit operator System.Decimal", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Router_PlatformPrefixBrowse_UnresolvedNamespace_ListsPlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Text", error);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("System.Text.Json.JsonSerializer", output);
        Assert.DoesNotContain("Package 'system.text' not found", error);
    }

    [Fact]
    public async Task Router_ExactPlatformAssembly_StillRoutesToLibrary()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Runtime", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Field", output);
        Assert.Contains("Value", output);
        Assert.Contains("Name", output);
        Assert.Contains("System.Runtime", output);
        Assert.Contains("Type Forwarders", output);
    }

    [Fact]
    public async Task SourceCommand_RemovedFromRoot()
    {
        var (exit, _, error) = await RunAppAsync("source", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Unrecognized command or argument 'source'", error);
    }

    [Fact]
    public async Task BrowsableUrlsAlias_Removed()
    {
        var (exit, _, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--browsable-urls", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Unrecognized option '--browsable-urls'", error);
    }

    [Fact]
    public async Task Router_PlatformPrefixBrowse_NarrowSourceMissFallsBackToWidePlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Collections.Frozen", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Collections.Frozen", error);
        Assert.Contains("System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("System.Collections.Frozen.FrozenSet", output);
    }

    [Fact]
    public async Task Router_PrefixBrowse_ExplicitPlatformNamespace_MatchesTypeCommand()
    {
        string[] arguments =
        [
            "System.Text.Json.Serialization",
            "--platform",
            "System.Text.Json",
            "--table",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("best-effort prefix matches", routed.Error);
    }

    [Fact]
    public async Task Router_ExplicitPlatformIdentity_PreservesLibraryInspection()
    {
        string[] arguments =
        [
            "System.Text.Json",
            "--platform",
            "System.Text.Json",
            "-S",
            "Library Info"
        ];

        var direct = await RunAppAsync(["library", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# System.Text.Json.dll", routed.Output);
        Assert.DoesNotContain("best-effort prefix matches", routed.Error);
    }

    [Fact]
    public async Task Router_ExplicitPlatformIdentity_WithTypePositional_PreservesTypeInspection()
    {
        string[] arguments =
        [
            "System.Text.Json",
            "JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "type",
            "JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-S",
            "Type Info",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains(
            "# System.Text.Json.JsonSerializer",
            routed.Output);
        Assert.Contains("## Type Info", routed.Output);
        Assert.Contains("| Source | Platform |", routed.Output);
        Assert.DoesNotContain("| Package |", routed.Output);
        Assert.Empty(routed.Error);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("-k", "class")]
    public async Task Router_ExplicitPlatformIdentity_OptionsBeforeTypePositional_PreserveTypeInspection(
        params string[] leadingOptions)
    {
        string[] tail =
        [
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "type",
                "JsonSerializer",
                "--platform",
                "System.Text.Json",
                .. leadingOptions,
                .. tail
            ]);
        var routed = await RunAppAsync(
            [
                "System.Text.Json",
                "--platform",
                "System.Text.Json",
                .. leadingOptions,
                "JsonSerializer",
                .. tail
            ]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Fact]
    public async Task Router_ExplicitPlatformIdentity_UnknownOptionBeforeTypePositional_PreservesTypeDiagnostic()
    {
        var direct = await RunAppAsync(
            "type",
            "JsonSerializer",
            "--platform",
            "System.Text.Json",
            "--bogus",
            "--tips",
            "q");
        var routed = await RunAppAsync(
            "System.Text.Json",
            "--platform",
            "System.Text.Json",
            "--bogus",
            "JsonSerializer",
            "--tips",
            "q");

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains(
            "Unrecognized option '--bogus'",
            routed.Error);
    }

    [Theory]
    [InlineData(
        "System.Collections.Concurrent.ConcurrentDictionary<TKey,TValue>.AlternateLookup<TAlternateKey>",
        "TryAdd")]
    [InlineData(
        "System.Delegate.InvocationListEnumerator<TDelegate>",
        "MoveNext")]
    public async Task Router_ExplicitPlatformIdentity_CSharpInnerGenericTypePreservesSharedMemberFilter(
        string target,
        string member)
    {
        string[] tail =
        [
            "-m",
            member,
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "type",
                target,
                "--platform",
                "System.Private.CoreLib",
                .. tail
            ]);
        var routed = await RunAppAsync(
            [
                "System.Private.CoreLib",
                "--platform",
                "System.Private.CoreLib",
                target,
                .. tail
            ]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("## Type Info", routed.Output);
    }

    [Fact]
    public async Task Router_ExplicitPlatformIdentity_SourceBeforeTypePositional_PreservesTypeInspection()
    {
        string[] arguments =
        [
            "System.Text.Json",
            "--platform",
            "System.Text.Json",
            "JsonSerializer",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "type",
            "JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-S",
            "Type Info",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains(
            "# System.Text.Json.JsonSerializer",
            routed.Output);
        Assert.Empty(routed.Error);
    }

    [Fact]
    public async Task Router_ExplicitPlatformIdentity_QualifiedMemberOptionSuppliesTarget()
    {
        string[] tail =
        [
            "-m",
            "JsonSerializer.Serialize",
            "-S",
            "Member Index",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "member",
                "--platform",
                "System.Text.Json",
                .. tail
            ]);
        var routed = await RunAppAsync(
            [
                "System.Text.Json",
                "--platform",
                "System.Text.Json",
                .. tail
            ]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.NotEqual("0", routed.Output.Trim());
        Assert.Empty(routed.Error);
    }

    [Fact]
    public async Task Router_ExplicitPlatformIdentity_WithDottedTarget_PreservesDeferredMemberRouting()
    {
        string[] arguments =
        [
            "System.Text.Json",
            "JsonSerializer.Deserialize",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "JsonSerializer.Deserialize",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains(
            "# System.Text.Json.JsonSerializer",
            routed.Output);
        Assert.Contains("## Methods", routed.Output);
    }

    [Fact]
    public async Task Router_ExplicitPlatformIdentity_WithTypeOption_PreservesTypeInspection()
    {
        string[] arguments =
        [
            "System.Text.Json",
            "JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-t",
            "JsonSerializer",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "type",
            "JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-t",
            "JsonSerializer",
            "-S",
            "Type Info",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("| Source | Platform |", routed.Output);
        Assert.DoesNotContain("| Package |", routed.Output);
    }

    [Fact]
    public async Task Router_PrefixBrowse_ExplicitLibrarySection_MatchesTypeCommand()
    {
        string[] arguments =
        [
            "DotnetInspect.Cli.Tests.Sample",
            "--library",
            TestAssemblyPath,
            "-S",
            "Classes",
            "--count",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Fact]
    public async Task Router_ExplicitLibraryExactTypeRejectsListingSection()
    {
        string[] arguments =
        [
            "DotnetInspect.Cli.Tests.CommandExecutionTests",
            "--library",
            TestAssemblyPath,
            "-S",
            "Classes",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.DoesNotContain("best-effort prefix matches", routed.Error);
    }

    [Fact]
    public async Task Router_ExplicitLibraryQualifiedMemberRejectsListingSection()
    {
        const string target =
            "DotnetInspect.Cli.Tests.TypeTargetedDecodeTests"
            + ".TypeTargetedBuild_MatchesFullBuild_ForEveryMethodOfTheType";

        var (exit, output, error) = await RunAppAsync(
            target,
            "--library",
            TestAssemblyPath,
            "-S",
            "Classes",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Select value 'Classes' not found", error);
        Assert.DoesNotContain("best-effort prefix matches", error);
    }

    [Fact]
    public async Task Router_PrefixBrowse_ExplicitPackageNamespace_MatchesTypeCommand()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            string[] arguments =
            [
                "DotnetInspect.Cli.Tests.Sample",
                "--package",
                packagePath,
                "--library",
                "Test.Primary.dll",
                "--table",
                "--tips",
                "q"
            ];

            var direct = await RunAppAsync(["type", .. arguments]);
            var routed = await RunAppAsync(arguments);

            Assert.Equal(direct, routed);
            Assert.Equal(0, routed.Exit);
            Assert.Contains("best-effort prefix matches", routed.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("--library=", null)]
    [InlineData("--library:", null)]
    [InlineData("--library", "")]
    [InlineData("--library", " ")]
    public async Task Router_EmptyLibraryValue_RoutesPackageAggregate(
        string libraryOption,
        string? libraryValue)
    {
        string target = $"Missing.Package.{Guid.NewGuid():N}";
        string[] libraryTokens = libraryValue is null
            ? [libraryOption]
            : [libraryOption, libraryValue];
        string[] executionTail =
            [.. libraryTokens, "--offline", "--tips", "q"];
        string[] schemaTail =
            [.. libraryTokens, "-D", "--schema", "--offline", "--tips", "q"];

        var directExecution = await RunAppAsync(
            ["package", target, .. executionTail]);
        var routedExecution = await RunAppAsync(
            [target, .. executionTail]);
        var directSchema = await RunAppAsync(
            ["package", target, .. schemaTail]);
        var routedSchema = await RunAppAsync(
            [target, .. schemaTail]);

        Assert.Equal(directExecution, routedExecution);
        Assert.Equal(1, routedExecution.Exit);
        Assert.DoesNotContain("File not found: --tips", routedExecution.Error);
        Assert.Equal(directSchema, routedSchema);
        Assert.DoesNotContain("File not found: --tips", routedSchema.Error);
    }

    [Theory]
    [InlineData("-n1")]
    [InlineData("-1")]
    public async Task Router_LineWindowUsesRewrittenRequiredValueOwnership(
        string libraryValue)
    {
        var direct = await RunAppAsync(
            "member",
            "System.String.ToString",
            "--library",
            libraryValue,
            "--help");
        var routed = await RunAppAsync(
            "System.String.ToString",
            "--library",
            libraryValue,
            "--help");

        Assert.Equal(0, direct.Exit);
        Assert.Equal(0, routed.Exit);
        Assert.Equal(direct.Output, routed.Output);
        Assert.True(
            routed.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length > 1);
    }

    [Theory]
    [InlineData("-n1")]
    [InlineData("-1")]
    public async Task Router_LineWindowUsesRewrittenActiveWindow(
        string lineWindow)
    {
        var routed = await RunAppAsync(
            "System.String.ToString",
            "--focus",
            "--rows",
            "--lines",
            lineWindow,
            "--help");

        Assert.Equal(0, routed.Exit);
        Assert.Single(
            routed.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("--library=-1")]
    [InlineData("--library:-1")]
    [InlineData("-o=-1")]
    [InlineData("-o:-1")]
    [InlineData("-o-1")]
    public async Task Router_ShorthandAfterInlineRequiredValueUsesRawOccurrence(
        string requiredValue)
    {
        var direct = await RunAppInDirectoryAsync(
            Path.GetTempPath(),
            "member",
            "System.String.ToString",
            requiredValue,
            "--lines",
            "-1",
            "--help");
        var routed = await RunAppInDirectoryAsync(
            Path.GetTempPath(),
            "System.String.ToString",
            requiredValue,
            "--lines",
            "-1",
            "--help");

        Assert.Equal(0, direct.Exit);
        Assert.Equal(0, routed.Exit);
        Assert.Single(
            direct.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(direct.Output, routed.Output);
    }

    [Fact]
    public async Task Router_ConcatenatedOptionalValueRemainsOwned()
    {
        var direct = await RunAppAsync(
            "member",
            "System.String.ToString",
            "-T-n1",
            "--help");
        var routed = await RunAppAsync(
            "System.String.ToString",
            "-T-n1",
            "--help");

        Assert.Equal(0, direct.Exit);
        Assert.Equal(0, routed.Exit);
        Assert.Equal(direct.Output, routed.Output);
        Assert.True(
            routed.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries).Length > 1);
    }

    [Theory]
    [InlineData("--library=-missing.dll")]
    [InlineData("--library:-missing.dll")]
    public async Task Router_AttachedLibraryValue_UsesTypeParser(
        string libraryOption)
    {
        string[] arguments =
        [
            "System.String",
            libraryOption,
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
    }

    [Fact]
    public async Task Router_ColonAttachedLibraryValue_MatchesTypeCommand()
    {
        string[] arguments =
        [
            "DotnetInspect.Cli.CommandLineBuilder",
            $"--library:{typeof(CommandLineBuilder).Assembly.Location}",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# DotnetInspect.Cli.CommandLineBuilder", routed.Output);
    }

    [Fact]
    public async Task Router_ColonAttachedPlatformValue_MatchesTypeCommand()
    {
        string[] arguments =
        [
            "System.String",
            "--platform:System.Runtime",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# System.String", routed.Output);
    }

    [Fact]
    public async Task Router_ColonAttachedPackageValue_MatchesTypeCommand()
    {
        string[] arguments =
        [
            "JsonConvert",
            "--package:Newtonsoft.Json@13.0.4",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# Newtonsoft.Json.JsonConvert", routed.Output);
    }

    [Fact]
    public async Task Router_SeparateDashPrefixedLibraryValue_UsesTypeParser()
    {
        string[] arguments =
        [
            "System.String",
            "--library",
            "-missing.dll",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains("File not found:", routed.Error);
        Assert.DoesNotContain("Package", routed.Error);
    }

    [Fact]
    public async Task Router_WindowsDriveLibraryValue_UsesTypeParser()
    {
        const string libraryPath = @"C:\missing.dll";
        string[] arguments =
        [
            "System.String",
            "--library",
            libraryPath,
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains("File not found:", routed.Error);
        Assert.DoesNotContain("Package", routed.Error);
    }

    [Fact]
    public async Task Router_ExplicitPlatformWithLibraryValue_UsesTypeParser()
    {
        string[] arguments =
        [
            "System.String",
            "--platform",
            "System.Runtime",
            "--library",
            "System.Private.CoreLib.dll",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains("File not found:", routed.Error);
        Assert.DoesNotContain("Unrecognized option '--platform'", routed.Error);
    }

    [Fact]
    public async Task BareQualifiedPlatformType_WithLeafCollision_RoutesExactly()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.IO.File", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("ambiguous", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System.IO.File", output);
    }

    [Fact]
    public async Task BareQualifiedAspNetCoreType_RoutesAcrossPlatformFrameworks()
    {
        SkipUnlessAspNetCoreAvailable();

        var (exit, output, error) = await RunAppAsync(
            "Microsoft.AspNetCore.Builder.WebApplication", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("ambiguous", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("# Microsoft.AspNetCore.Builder.WebApplication", output);
        AssertLibraryAsset(output, "Microsoft.AspNetCore");
        Assert.Contains("Source: Platform", output);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Components.Endpoints.FormMapping.ArrayPoolBufferAdapter<T1,T2,T3>.PooledBuffer")]
    [InlineData("Microsoft.AspNetCore.Components.Endpoints.FormMapping.ArrayPoolBufferAdapter`3.PooledBuffer")]
    public async Task BareQualifiedGenericAspNetCoreType_RoutesAcrossPlatformFrameworks(string target)
    {
        SkipUnlessAspNetCoreAvailable();

        var (exit, output, error) = await RunAppAsync(
            target, "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ArrayPoolBufferAdapter&lt;", output);
        Assert.Contains(".PooledBuffer", output);
        AssertLibraryAsset(output, "Microsoft.AspNetCore.Components.Endpoints");
    }

    [Fact]
    public async Task BareQualifiedGenericAspNetCoreType_PreservesResolvedSource()
    {
        SkipUnlessAspNetCoreAvailable();

        var (exit, output, error) = await RunAppAsync(
            "Microsoft.AspNetCore.Http.HttpResults.Results<T1,T2>",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "# Microsoft.AspNetCore.Http.HttpResults.Results&lt;",
            output);
        AssertLibraryAsset(output, "Microsoft.AspNetCore.Http.Results");
    }

    [Fact]
    public async Task BareQualifiedGenericAspNetCoreMember_PreservesDocumentation()
    {
        SkipUnlessAspNetCoreAvailable();

        var (exit, output, error) = await RunAppAsync(
            "Microsoft.AspNetCore.Http.HttpResults.Results<T1,T2>.ExecuteAsync",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "An IResult that could be one of two different IResult types.",
            output);
        Assert.Contains("ExecuteAsync", output);
    }

    [Fact]
    public async Task BareQualifiedAspNetCoreType_RoutesPastNonOwningAssemblyPrefix()
    {
        SkipUnlessAspNetCoreAvailable();

        var (exit, output, error) = await RunAppAsync(
            "Microsoft.AspNetCore.Http.HttpContext", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("best-effort prefix", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("# Microsoft.AspNetCore.Http.HttpContext", output);
        AssertLibraryAsset(output, "Microsoft.AspNetCore.Http.Abstractions");
        Assert.Contains("Source: Platform", output);
    }

    [Fact]
    public async Task BareQualifiedAspNetCoreMember_RoutesAcrossPlatformFrameworks()
    {
        SkipUnlessAspNetCoreAvailable();

        var (exit, output, error) = await RunAppAsync(
            "Microsoft.AspNetCore.Builder.WebApplication.Run", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public void Run(", output);
    }

    [Fact]
    public async Task BareQualifiedPlatformType_TrueAmbiguityStillFails()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Numerics.Enumerator", "--markdown", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Platform type lookup is ambiguous", error);
    }

    [Fact]
    public async Task BareQualifiedPlatformMember_TrueAmbiguityStillFails()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Numerics.Enumerator.X", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Platform type lookup is ambiguous", error);
    }

    [Theory]
    [InlineData(true, new string[0], new string[0], true)]
    [InlineData(true, new string[0], new[] { "Type Info" }, false)]
    [InlineData(false, new string[0], new string[0], false)]
    [InlineData(true, new[] { "Fields" }, new string[0], false)]
    public void BareSelect_WithNoOverviewSections_IsRejected(
        bool selectDefault, string[] select, string[] overview, bool expected)
    {
        // An empty overview set must not reach SelectResolver: it hands back an empty-but-non-null
        // include set, which IsRequested reads as "no filter" and answers with the full verbosity
        // ladder -- more output than asked for, exit 0, no diagnostic. Explicit -S values are a
        // legitimate fallback, so they must not trip the guard.
        var options = new TypeOptions
        {
            TypeName = "System.String",
            SelectDefault = selectDefault,
            Select = select.Length == 0 ? null : select
        };

        Assert.Equal(expected, ApiCommand.HasNoBareSelectOverview(options, overview));
    }

    [Fact]
    public async Task Router_RewrittenCommand_IsAudited()
    {
        // The router captures projection flags as raw tokens, so the outer invocation records
        // nothing. It used to invoke the rewritten parse directly, bypassing the audit, which
        // left every bare-mode invocation unguarded. It now goes through the choke point.
        var (exit, _, error) = await RunAppAsync("Regex", "--count", "--print", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--count cannot be combined with --print", error);
    }

    [Fact]
    public async Task Router_BareHelp_ExplainsPackageRouting()
    {
        var (exit, output, error) = await RunAppAsync("frobnicate", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("Inspect a NuGet package", output);
        Assert.Contains("interpreting bare token 'frobnicate' as a package or platform target", error);
        Assert.Contains("dotnet-inspect --help", error);
    }

    [Fact]
    public async Task Router_HelpBeforeBareToken_StillExplainsPackageRouting()
    {
        var (exit, output, error) = await RunAppAsync("--help", "frobnicate");

        Assert.Equal(0, exit);
        Assert.Contains("Inspect a NuGet package", output);
        Assert.DoesNotContain("Auto-route bare input", output);
        Assert.Contains("interpreting bare token 'frobnicate' as a package or platform target", error);
    }

    [Fact]
    public async Task Router_BareDiscoveryHelp_DoesNotCallOptionABareToken()
    {
        var (exit, output, error) = await RunAppAsync("-S", "Methods", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("Discover types in a package or library", output);
        Assert.DoesNotContain("interpreting bare token '-S'", error);
    }

    [Fact]
    public async Task Router_OutputFlagBeforeBareToken_KeepsBareTokenAsRouteTarget()
    {
        var (exit, output, error) = await RunAppAsync("--json", "frobnicate");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Package 'frobnicate' selection 'latest'", error);
        Assert.DoesNotContain("Package '--json' selection", error);
    }

    [Fact]
    public async Task Router_ValueOptionBeforeBareToken_SkipsOptionValueWhenFindingRouteTarget()
    {
        var (exit, output, error) = await RunAppAsync("--type", "Widget", "frobnicate");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Package 'frobnicate' selection 'latest'", error);
        Assert.DoesNotContain("Package 'Widget' selection", error);
    }

    [Fact]
    public async Task Router_MemberOptionBeforeBareToken_SkipsMemberValueWhenFindingRouteTarget()
    {
        var (exit, output, error) = await RunAppAsync("--member", "Keep", "frobnicate");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.DoesNotContain("Package 'Keep' not found", error);
        Assert.DoesNotContain("Unrecognized option '--member'", error);
    }

    [Fact]
    public async Task NoArguments_Commands_ReportMissingInputConsistently()
    {
        // Issue #1690: every command reports a missing required argument the Unix way —
        // a concise error on stderr with a non-zero exit, not full help with exit 0.
        foreach (var command in new[] { "type", "member", "find", "depends", "extensions", "implements" })
        {
            var (exit, output, error) = await RunAppAsync(command, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("Error:", error);
            Assert.Contains($"dotnet-inspect {command} --help", error);
        }
    }

    [Fact]
    public async Task BareName_DecompilerCategory_IsNotPublished()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "-S", "@Decompiler", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Select value '@Decompiler' not found.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Body Shapes\" requires", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliDiscoverySections_AreSelectable()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        var (noSourceLinkAssemblyPath, _, noSourceLinkFixtureDir) =
            CreateNoSourceLinkDiscoveryAssembly();
        try
        {
            var diffV1 = FixtureCatalog.DiffPair.OldAssemblyPath();
            var diffV2 = FixtureCatalog.DiffPair.NewAssemblyPath();

            List<string[]> commands =
            [
                ["library", TestAssemblyPath],
                ["type", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath],
                ["type", typeof(EmptyDiscoveryFixture).FullName!, "--library", TestAssemblyPath],
                ["member", typeof(MemberCallsFixture).FullName!, nameof(MemberCallsFixture.CallsInterfaceItem), "--library", TestAssemblyPath],
                ["member", "DiscoveryFixtures.NoSourceLink", "Overloaded", "--library", noSourceLinkAssemblyPath],
                ["package", packagePath],
                ["diff", "--library", $"{diffV1}..{diffV2}", "-t", "DiffFixtureSample.DiffSample"]
            ];

            foreach (var command in commands)
            {
                var discoveryArgs = command.Concat(["-D", "--tips", "q"]).ToArray();
                var (discoverExit, discoverOutput, discoverError) = await RunAppAsync(discoveryArgs);
                Assert.Equal(0, discoverExit);

                var sections = ExtractDiscoveryRows(discoverOutput)
                    .Where(row => row.Kind.StartsWith("section", StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.Name)
                    .ToArray();
                if (!IsNoMemberTypeDiscoveryCommand(command))
                    Assert.NotEmpty(sections);

                foreach (var section in sections)
                {
                    var selectArgs = BuildDiscoverySelectionArgs(command, section);
                    if (section != SectionNames.FindingCensus)
                        selectArgs = [.. selectArgs, "--lines"];
                    var (selectExit, selectOutput, selectError) = await RunAppAsync(selectArgs);
                    var selectionSucceeded = selectExit == 0
                        || IsSourceIntegrityStatusResult(command, section, selectExit, selectOutput);
                    Assert.True(selectionSucceeded,
                        $"{command[0]} -S '{section}' failed after being listed by -D. Discovery stderr: {discoverError}. Selection stderr: {selectError}");
                    if (command.Contains(noSourceLinkAssemblyPath, StringComparer.Ordinal)
                        && section == SectionNames.SourceLocations)
                    {
                        Assert.True(string.IsNullOrWhiteSpace(selectOutput));
                    }
                    if (RequiresNonEmptyDiscoveryResult(command, section))
                    {
                        Assert.False(string.IsNullOrWhiteSpace(selectOutput),
                            $"{command[0]} -S '{section}' produced no data after being listed by -D.");
                        Assert.DoesNotContain("has no data", selectOutput, StringComparison.OrdinalIgnoreCase);
                        Assert.DoesNotContain("has no data", selectError, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
            Directory.Delete(noSourceLinkFixtureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Router_LibraryFlag_RoutesPackageToLibraryInspection()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(packagePath, "--library", "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Primary 1.0.0", output);
            Assert.Contains(
                "## Library Info (lib/net10.0/Test.Primary.dll)",
                output);
            Assert.DoesNotContain("## Package Info", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Newtonsoft.Json.dll")]
    [InlineData("Newtonsoft.Json")]
    public async Task Router_LibraryValue_RoutesPackageToLibraryInspection(
        string library)
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "--library",
            library,
            "-S",
            "Library Info",
            "--tips",
            "q",
        ];
        var direct = await RunAppAsync(["package", .. arguments]);
        var routed = await RunAppAsync(arguments);
        string[] schemaArguments =
        [
            "Newtonsoft.Json@13.0.4",
            "--library",
            library,
            "-D",
            "--schema",
            "--offline",
            "--tips",
            "q",
        ];
        var directSchema = await RunAppAsync(
            ["package", .. schemaArguments]);
        var routedSchema = await RunAppAsync(schemaArguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# Newtonsoft.Json.dll", routed.Output);
        Assert.Contains("## Library Info", routed.Output);
        Assert.DoesNotContain("## Package Info", routed.Output);
        Assert.DoesNotContain("best-effort prefix matches", routed.Error);
        Assert.Equal(directSchema, routedSchema);
    }

    [Theory]
    [InlineData("Newtonsoft.Json", "lib/net6.0/Newtonsoft.Json.dll")]
    [InlineData("Newtonsoft.Json", @"lib\net6.0\Newtonsoft.Json.dll")]
    [InlineData("Newtonsoft.Json@13.0.4", "lib/net6.0/Newtonsoft.Json.dll")]
    [InlineData("Newtonsoft.Json@13.0.4", @"lib\net6.0\Newtonsoft.Json.dll")]
    public async Task Router_PackageLibrarySubpath_PreservesPackageInspection(
        string package,
        string libraryPath)
    {
        string[] arguments =
        [
            package,
            "--library",
            libraryPath,
            "-S",
            "Library Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["package", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# Newtonsoft.Json.dll", routed.Output);
        Assert.Contains("## Library Info", routed.Output);
    }

    [Theory]
    [InlineData(
        "Newtonsoft.Json",
        "lib/net6.0/Newtonsoft.Json.dll",
        "Newtonsoft.Json.dll")]
    [InlineData(
        "Newtonsoft.Json@13.0.4",
        "lib/net6.0/Newtonsoft.Json.dll",
        "Newtonsoft.Json.dll")]
    [InlineData(
        "Microsoft.CodeAnalysis.BannedApiAnalyzers@5.6.0",
        "analyzers/dotnet/cs/Microsoft.CodeAnalysis.BannedApiAnalyzers.dll",
        "Microsoft.CodeAnalysis.BannedApiAnalyzers.dll")]
    public async Task Router_PackageLibrarySubpath_IsIndependentOfCurrentDirectory(
        string package,
        string libraryPath,
        string libraryName)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"router-library-subpath-{Guid.NewGuid():N}");
        var localLibrary = Path.Combine(
            tempDir,
            libraryPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(localLibrary)!);
        File.WriteAllText(localLibrary, "not an assembly");
        try
        {
            string[] arguments =
            [
                package,
                "--library",
                libraryPath,
                "-S",
                "Library Info",
                "--tips",
                "q"
            ];

            var direct = await RunAppInDirectoryAsync(
                tempDir,
                ["package", .. arguments]);
            var routed = await RunAppInDirectoryAsync(
                tempDir,
                arguments);

            Assert.Equal(direct, routed);
            Assert.Equal(0, routed.Exit);
            Assert.Contains($"# {libraryName}", routed.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Router_UnpinnedPackageColonAttachedLibrarySubpath_PreservesPackageInspection()
    {
        string[] arguments =
        [
            "Newtonsoft.Json",
            "--library:lib/net6.0/Newtonsoft.Json.dll",
            "-S",
            "Library Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["package", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# Newtonsoft.Json.dll", routed.Output);
        Assert.Contains("## Library Info", routed.Output);
    }

    [Theory]
    [InlineData("lib/Debug/Missing.dll")]
    [InlineData("lib/monoandroid/Missing.dll")]
    [InlineData("lib/uap10.0/Missing.dll")]
    [InlineData("lib/portable-net45+win8/Missing.dll")]
    [InlineData("lib/x/lib/net8.0/Missing.dll")]
    [InlineData("tools/netstandard/Missing.dll")]
    [InlineData("lib/netcoreapp/Missing.dll")]
    [InlineData("lib/net4.0/Missing.dll")]
    [InlineData("ref/net6.0/Missing.dll")]
    [InlineData("tools/net6.0/Missing.dll")]
    [InlineData("runtimes/linux-x64/lib/net6.0/Missing.dll")]
    [InlineData("analyzers/dotnet/cs/Missing.dll")]
    [InlineData("build/net8.0/Missing.dll")]
    [InlineData("tasks/Missing.dll")]
    [InlineData("runtimes/linux-x64/native/Missing.dll")]
    [InlineData("directory/lib/net8.0/Missing.dll")]
    [InlineData(@"directory\Missing.dll")]
    [InlineData("runtimes/lib/net8.0/Missing.dll")]
    public async Task Router_PackageRelativeLibraryPath_RoutesPackage(
        string libraryPath)
    {
        string[] arguments =
        [
            "Newtonsoft.Json",
            "--library",
            libraryPath,
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["package", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains("not found in package", routed.Error);
    }

    [Theory]
    [InlineData("./missing/Newtonsoft.Json.dll")]
    [InlineData(@"..\missing\Newtonsoft.Json.dll")]
    [InlineData("/missing/Newtonsoft.Json.dll")]
    [InlineData(@"\missing\Newtonsoft.Json.dll")]
    [InlineData(@"C:\missing\Newtonsoft.Json.dll")]
    public async Task Router_VersionedPackageRootedLibraryPath_UsesTypeParser(
        string libraryPath)
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "--library",
            libraryPath,
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains("File not found:", routed.Error);
    }

    [Theory]
    [InlineData("lib/net8bogus/missing.dll")]
    [InlineData("lib/net-8.0/missing.dll")]
    [InlineData("lib/net.8.0/missing.dll")]
    [InlineData("lib/net+8.0/missing.dll")]
    [InlineData("lib//net8.0/missing.dll")]
    [InlineData("lib/./missing.dll")]
    [InlineData("lib/net8.0/../missing.dll")]
    [InlineData("runtimes/linux-x64/lib/../missing.dll")]
    public async Task Router_MalformedPackageLibraryPath_UsesTypeParser(
        string libraryPath)
    {
        string[] arguments =
        [
            "System.String",
            "--library",
            libraryPath,
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains("File not found:", routed.Error);
    }

    [Theory]
    [InlineData("lib/net/Newtonsoft.Json.dll")]
    [InlineData("runtimes/linux-x64/lib/net/Newtonsoft.Json.dll")]
    public async Task Router_IncompleteFrameworkLibraryPath_DoesNotEnterPackageFallback(
        string libraryPath)
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.3",
            "--library",
            libraryPath,
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Empty(routed.Output);
        Assert.Contains("File not found:", routed.Error);
    }

    [Fact]
    public async Task Router_PinnedPackage_DoesNotOverrideExplicitLibraryPath()
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "--library",
            "lib/net8.0/../Newtonsoft.Json.dll",
            "-S",
            "Library Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(["type", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(1, direct.Exit);
        Assert.Equal(direct.Exit, routed.Exit);
        Assert.DoesNotContain("# Newtonsoft.Json.dll", routed.Output);
        Assert.DoesNotContain("not found in package", routed.Error);
    }

    [Fact]
    public async Task Router_LibraryValue_IsIndependentOfCurrentDirectory()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"router-library-selector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(
            Path.Combine(tempDir, "Newtonsoft.Json.dll"),
            "not an assembly");
        try
        {
            string[] arguments =
            [
                "Newtonsoft.Json@13.0.4",
                "--library",
                "Newtonsoft.Json.dll",
                "-S",
                "Library Info",
                "--tips",
                "q"
            ];

            var direct = await RunAppInDirectoryAsync(
                tempDir,
                ["package", .. arguments]);
            var routed = await RunAppInDirectoryAsync(
                tempDir,
                arguments);

            Assert.Equal(direct, routed);
            Assert.Equal(0, routed.Exit);
            Assert.Contains("# Newtonsoft.Json.dll", routed.Output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Router_BareLibraryFollowedByColonOption_PreservesPackageInspection()
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "--library",
            "-v:q",
            "-S",
            "Library Info"
        ];

        var direct = await RunAppAsync(["package", .. arguments]);
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# newtonsoft.json 13.0.4", routed.Output);
        Assert.Contains(
            "## Library Info (lib/net6.0/Newtonsoft.Json.dll)",
            routed.Output);
    }

    [Fact]
    public async Task Router_ExplicitPackageIdentity_PreservesPackageInspection()
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "-S",
            "Package Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "package",
            "Newtonsoft.Json@13.0.4",
            "-S",
            "Package Info",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# Newtonsoft.Json", routed.Output);
        Assert.Contains("## Package Info", routed.Output);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("-k", "class")]
    public async Task Router_ExplicitPackageIdentity_OptionsBeforeTypePositional_PreserveTypeInspection(
        params string[] leadingOptions)
    {
        string[] tail =
        [
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "type",
                "JsonSerializer",
                "--package",
                "System.Text.Json",
                .. leadingOptions,
                .. tail
            ]);
        var routed = await RunAppAsync(
            [
                "System.Text.Json",
                "--package",
                "System.Text.Json",
                .. leadingOptions,
                "JsonSerializer",
                .. tail
            ]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("JsonSerializer", routed.Output);
    }

    [Fact]
    public async Task Router_ColonAttachedPackageIdentity_PreservesPackageInspection()
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "--package:Newtonsoft.Json@13.0.4",
            "-S",
            "Package Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "package",
            "Newtonsoft.Json@13.0.4",
            "-S",
            "Package Info",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# Newtonsoft.Json", routed.Output);
        Assert.Contains("## Package Info", routed.Output);
    }

    [Fact]
    public async Task Router_ColonAttachedPackageIdentity_WithTypePositional_PreservesTypeInspection()
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "JsonConvert",
            "--package:Newtonsoft.Json@13.0.4",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "type",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "-S",
            "Type Info",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# Newtonsoft.Json.JsonConvert", routed.Output);
        Assert.Contains("| Source | NuGet |", routed.Output);
    }

    [Theory]
    [InlineData("13.0.4")]
    [InlineData("13")]
    [InlineData("13-beta")]
    public async Task Router_ExplicitPackageIdentity_WithVersionPositional_PreservesPackageRouting(
        string version)
    {
        string[] arguments =
        [
            "Newtonsoft.Json",
            version,
            "--package",
            "Newtonsoft.Json",
            "-S",
            "Package Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "package",
            "Newtonsoft.Json",
            version,
            "-S",
            "Package Info",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains(
            "Multiple package output requires --json or a row format",
            routed.Error);
        Assert.DoesNotContain("Type Info", routed.Error);
    }

    [Fact]
    public async Task Router_ColonAttachedPackageIdentity_WithMemberOption_PreservesMemberInspection()
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "JsonConvert",
            "--package:Newtonsoft.Json@13.0.4",
            "-m",
            "SerializeObject",
            "--index",
            "1",
            "-S",
            "Signature",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "member",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "-m",
            "SerializeObject",
            "--index",
            "1",
            "-S",
            "Signature",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("## Signature", routed.Output);
        Assert.Contains("Package: Newtonsoft.Json", routed.Output);
    }

    [Fact]
    public async Task Router_DuplicatePackageIdentity_PreservesMalformedOption()
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "--package",
            "--version",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "package",
            "Newtonsoft.Json@13.0.4",
            "--package",
            "--version",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(1, routed.Exit);
        Assert.Contains("Unrecognized option '--package'", routed.Error);
    }

    [Fact]
    public async Task Router_VersionedPackageTypeShorthand_PreservesTypeInspection()
    {
        string[] arguments =
        [
            "Newtonsoft.Json@13.0.4",
            "JsonConvert",
            "-S",
            "Type Info",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "type",
            "JsonConvert",
            "--package",
            "Newtonsoft.Json@13.0.4",
            "-S",
            "Type Info",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Contains("# Newtonsoft.Json.JsonConvert", routed.Output);
    }

    [Fact]
    public async Task Router_FacadePlatformBareName_RoutesToForwardedType()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime.CompilerServices.Unsafe");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime.CompilerServices.Unsafe not available: {error}");
            return;
        }

        Assert.SkipUnless(IsFacadeAssembly(assemblyPath),
            "System.Runtime.CompilerServices.Unsafe is not facade-only in this runtime.");

        var (exit, output, runError) = await RunAppAsync(
            "System.Runtime.CompilerServices.Unsafe", "--markdown", "-v:q", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("# System.Runtime.CompilerServices.Unsafe", output);
        Assert.DoesNotContain("# System.Runtime.CompilerServices.Unsafe.dll", output);
        Assert.Contains("Kind: class", output);
    }

    [Fact]
    public async Task Router_NonFacadePlatformBareName_RoutesToLibrary()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        Assert.False(IsFacadeAssembly(assemblyPath));

        var (exit, output, runError) = await RunAppAsync(
            "System.Text.Json", "--markdown", "-v:q", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("# System.Text.Json.dll", output);
        Assert.Contains("Name: System.Text.Json", output);
    }

    [Fact]
    public async Task Router_RepeatedNullableGenericArgumentDoesNotResolveType()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Collections.Generic.List<T??>",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            "Type Info",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("Clear`0")]
    [InlineData("ConvertAll`01")]
    public async Task Router_NoncanonicalMemberArityDoesNotBroaden(
        string memberSelector)
    {
        var (exit, output, error) = await RunAppAsync(
            $"System.Collections.Generic.List<T>.{memberSelector}",
            "--platform",
            "System.Collections",
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Router_DottedExplicitInterfaceSelectorUsesMetadataBoundary()
    {
        const string target =
            "System.Collections.Generic.EqualityComparer<T>"
            + ".explicit:System.Collections.IEqualityComparer.Equals:1";
        string[] tail =
        [
            "--platform",
            "System.Private.CoreLib",
            "--all",
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(["member", target, .. tail]);
        var deferred = await RunAppAsync([target, .. tail]);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
        Assert.Equal("1", deferred.Output.Trim());
    }

    [Theory]
    [InlineData("operator<<")]
    [InlineData("Operator<<")]
    public async Task Router_ExplicitSourceGenericShiftOperator_ResolvesTheMember(
        string memberSelector)
    {
        var (exit, output, error) = await RunAppAsync(
            $"System.Numerics.Vector<T>.{memberSelector}",
            "--platform",
            "System.Numerics.Vectors",
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("operator<")]
    [InlineData("operator>")]
    public async Task Router_ExplicitSourceAngleOperator_StaticSchemaUsesMemberPipeline(
        string memberSelector)
    {
        string[] tail =
        [
            "--platform",
            "System.Runtime",
            "-D",
            SectionNames.Signature,
            "--schema",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "member",
                "System.DateTime",
                "-m",
                memberSelector,
                .. tail
            ]);
        var routed = await RunAppAsync(
            [$"System.DateTime.{memberSelector}", .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Fact]
    public async Task Router_LocalLibraryAngleOperator_StaticSchemaUsesMemberPipeline()
    {
        string[] tail =
        [
            "--library",
            typeof(DateTime).Assembly.Location,
            "-D",
            SectionNames.Signature,
            "--schema",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "member",
                "System.DateTime",
                "-m",
                "operator>",
                .. tail
            ]);
        var routed = await RunAppAsync(
            ["System.DateTime.operator>", .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Fact]
    public async Task Router_LocalLibraryCanonicalOperator_UsesMemberPipeline()
    {
        string[] tail =
        [
            "--library",
            typeof(DateTime).Assembly.Location,
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "member",
                "System.DateTime",
                "-m",
                "op_Addition",
                .. tail
            ]);
        var routed = await RunAppAsync(
            ["System.DateTime.op_Addition", .. tail]);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
        Assert.Equal("1", routed.Output.Trim());
    }

    [Fact]
    public async Task Router_ExplicitSourceOperatorPrefixedIdentifierUsesMetadataBoundary()
    {
        const string target =
            "DotnetInspect.Cli.Tests.Operators<T>.Apply";
        string[] arguments =
        [
            target,
            "--library",
            TestAssemblyPath,
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q"
        ];

        var direct = await RunAppAsync(
            "member",
            "DotnetInspect.Cli.Tests.Operators<T>",
            "--library",
            TestAssemblyPath,
            "-m",
            "Apply",
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q");
        var routed = await RunAppAsync(arguments);

        Assert.Equal(direct, routed);
        Assert.Equal(0, routed.Exit);
    }

    [Theory]
    [InlineData("operatorApply")]
    [InlineData("OperatorApply")]
    public async Task Router_UnrecognizedOperatorPrefixDoesNotBroaden(
        string memberName)
    {
        var (exit, output, error) = await RunAppAsync(
            $"DotnetInspect.Cli.Tests.Operators<T>.{memberName}",
            "--library",
            TestAssemblyPath,
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(memberName, error);
    }

}
