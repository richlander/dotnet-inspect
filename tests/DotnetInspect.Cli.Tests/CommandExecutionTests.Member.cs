using DotnetInspect.Cli.Commands;
using System.Globalization;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Theory]
    [InlineData("--format=mermaid", false)]
    [InlineData("--tree", false)]
    [InlineData("--mermaid", true)]
    public async Task Member_NonGraphScalarCount_IgnoresGraphPresentationFormat(
        string format,
        bool markdown)
    {
        var arguments = new List<string>
        {
            "member",
            typeof(MemberCallGraphFixture).FullName!,
            nameof(MemberCallGraphFixture.RootCall),
            "--library", TestAssemblyPath,
            "-S", "Calls",
            "--count",
        };
        if (markdown)
            arguments.Add("--format=markdown");
        arguments.Add(format);
        arguments.AddRange(["--tips", "q"]);

        var (exit, output, error) = await RunAppAsync(arguments.ToArray());

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(
            int.TryParse(output.Trim(), CultureInfo.InvariantCulture, out var count),
            output);
        Assert.True(count > 0);
    }

    [Theory]
    [InlineData("--mermaid", "multiple sections")]
    [InlineData("--tree", "exactly one")]
    public async Task Member_MultiSectionCount_RejectsGraphPresentationFormat(
        string format,
        string expectedError)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(MemberCallGraphFixture).FullName!,
            nameof(MemberCallGraphFixture.RootCall),
            "--library", TestAssemblyPath,
            "-S", "Calls,Safety Facts",
            "--count",
            format,
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public async Task Member_MultiSectionCount_RejectsEmbeddedMermaidPresentation()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(MemberCallGraphFixture).FullName!,
            nameof(MemberCallGraphFixture.RootCall),
            "--library", TestAssemblyPath,
            "-S", "Calls,Safety Facts",
            "--count",
            "--format=markdown",
            "--mermaid",
            "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("multiple sections as Mermaid", error);
    }

    [Fact]
    public async Task Member_ImplicitCallerSection_CountRejectsTreePresentation()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(MemberCallGraphFixture).FullName!,
            nameof(MemberCallGraphFixture.RootCall),
            "--library",
            TestAssemblyPath,
            "-S",
            "Call Graph",
            "--bin",
            Path.GetDirectoryName(TestAssemblyPath)!,
            "--count",
            "--tree",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("exactly one selected shape", error);
    }

    [Theory]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests.NestedDrillTarget")]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests+NestedDrillTarget")]
    public async Task MemberCommand_AllowsDrillingNonPublicNestedConstructors(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeName, ".ctor:1",
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains("Value = value", output);
    }

    [Theory]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests.NestedDrillTarget..ctor")]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests.NestedDrillTarget..ctor:1")]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests+NestedDrillTarget..ctor")]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests+NestedDrillTarget..ctor:1")]
    public async Task MemberCommand_AllowsCopiedNestedConstructorSelector(string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", selector,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains("Value = value", output);
    }

    [Theory]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests.NestedDrillTarget")]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests+NestedDrillTarget")]
    public async Task MemberCommand_AllowsCopiedNestedConstructorDigestSelector(string typeName)
    {
        var index = await RunAppAsync(
            "type", typeName,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Member Index",
            "-n", "80", "--lines");
        Assert.Equal(0, index.Exit);
        var stable = index.Output
            .Split('\n')
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 3 && parts[1] == "`.ctor`")
            .Select(parts => parts[2].Trim('`'))
            .Single();

        var selector = $"{typeName}.{stable}";
        var (exit, output, error) = await RunAppAsync(
            "member", selector,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains("Value = value", output);
    }

    [Fact]
    public async Task MemberCommand_PlacesTypedConstructorChainOnDeclaration()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "DotnetInspect.Cli.Tests.CommandExecutionTests+ConstructorChainTarget", ".ctor:1",
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("ConstructorChainTarget(int value) : base(value)", output);
        Assert.DoesNotContain("\n    : base(value)", output);
    }

    [Fact]
    public async Task
        MemberCommand_DecompiledSourceRetainsInvalidAdjacentPdbFailure()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-member-decompilation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string dllPath =
                Path.Combine(tempDir, "MemberDecompilation.dll");
            File.Copy(TestAssemblyPath, dllPath);
            await File.WriteAllTextAsync(
                Path.ChangeExtension(dllPath, ".pdb"),
                "not-a-portable-pdb",
                TestContext.Current.CancellationToken);

            var (exit, output, error) =
                await RunAppAsync(
                    "member",
                    "DotnetInspect.Cli.Tests.CommandExecutionTests+ConstructorChainTarget",
                    ".ctor:1",
                    "--library",
                    dllPath,
                    "--all",
                    "-S",
                    "Decompiled Source",
                    "--tips",
                    "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(
                "terminal Library admission: Portable PDB companion rejected",
                output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "ConstructorChainTarget(int value) : base(value)",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task
        MemberCommand_SharedAccessorProjectionPreservesPhysicalModifiers()
    {
        var readOnly =
            await RunAppAsync(
                "member",
                "ILInspector.Decompiler.Fixtures.ReadonlyPropertySamples",
                "--library",
                FixtureCatalog.DecompilerUnsafeLegacy
                    .AssemblyPath(),
                "Count:1",
                "-S",
                "Decompiled Source",
                "--tips",
                "q");
        var unsafeAccessor =
            await RunAppAsync(
                "member",
                "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyExplicitAccessorFixture",
                "--library",
                FixtureCatalog.DecompilerUnsafeNew
                    .AssemblyPath(),
                "explicit:ILInspector.Decompiler.Fixtures.NewUnsafe.IMemorySafetyAccessorContract.get_Value:1",
                "--all",
                "-S",
                "Decompiled Source",
                "--tips",
                "q");

        Assert.Equal(0, readOnly.Exit);
        Assert.Empty(readOnly.Error);
        Assert.Contains(
            "public readonly int Count => _count;",
            readOnly.Output);
        Assert.Equal(0, unsafeAccessor.Exit);
        Assert.Empty(unsafeAccessor.Error);
        Assert.Contains(
            "unsafe int ILInspector.Decompiler.Fixtures.NewUnsafe.IMemorySafetyAccessorContract.Value => 42;",
            unsafeAccessor.Output);
    }

    [Fact]
    public async Task InitializerOnlyConstructor_ProjectsThroughMemberAndTypeCommands()
    {
        string typeName = typeof(CommandInitializerOnlyFixture).FullName!;
        var member = await RunAppAsync(
            "member", typeName, ".ctor:1",
            "--library", TestAssemblyPath,
            "-S", "Decompiled Source",
            "--tips", "q");
        var type = await RunAppAsync(
            "type", typeName,
            "--library", TestAssemblyPath,
            "-S", "Decompiled Source",
            "--tips", "q");

        Assert.Equal(0, member.Exit);
        Assert.Empty(member.Error);
        Assert.Contains("CommandInitializerOnlyFixture()", member.Output);
        Assert.DoesNotContain("Value = 42", member.Output);
        Assert.DoesNotContain("DEC0003", member.Output);

        Assert.Equal(0, type.Exit);
        Assert.Empty(type.Error);
        Assert.Contains("public int Value = 42;", type.Output);
        Assert.DoesNotContain("DEC0003", type.Output);
    }

    [Fact]
    public async Task MemberCommand_DoesNotInferAsyncFromRenderedText()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "DotnetInspect.Cli.Tests.CommandExecutionTests+AwaitTextTarget", "AwaitText:1",
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Decompiled Source",
            "-n", "80", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("\"await\"", output);
        Assert.DoesNotContain(" async ", output);
    }

    [Fact]
    public async Task Member_TypeQualifiedMemberGlob_PeelsResolvedType()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "JsonSerializer.Deser*",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("Deserialize", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_RouterDeferredTarget_UsesMetadataMemberBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Collections.Immutable.ImmutableArray<T>.Builder.Capacity",
            "--platform",
            "System.Collections.Immutable",
            "--format=markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Properties", output);
        Assert.Contains("| Capacity |", output);
    }

    [Fact]
    public async Task Member_RouterDeferredTarget_ExactTypeKeepsTypeRendering()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Collections.Immutable.ImmutableArray<T>.Builder",
            "--platform",
            "System.Collections.Immutable",
            "--format=markdown",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "# System.Collections.Immutable.ImmutableArray&lt;T&gt;.Builder",
            output);
        Assert.Contains("Kind: class", output);
    }

    [Fact]
    public async Task Member_RouterDeferredTarget_ExactTypePreservesVerbosity()
    {
        string target =
            "System.Collections.Immutable.ImmutableArray<T>.Builder";
        var direct = await RunAppAsync(
            "type",
            target,
            "--platform",
            "System.Collections.Immutable",
            "-v:n",
            "--tips",
            "q");
        var deferred = await RunAppAsync(
            target,
            "--platform",
            "System.Collections.Immutable",
            "-v:n",
            "--tips",
            "q");

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
    }

    [Fact]
    public async Task Member_DottedTargetRejectsUniversallyInvalidSectionBeforeAcquisition()
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var ambiguous = await RunAppAsync(
            "member",
            "Missing.Type.Member",
            "--library",
            missingAssembly,
            "-S",
            "DefinitelyNotASection",
            "--tips",
            "q");
        var explicitMember = await RunAppAsync(
            "member",
            "Missing.Type",
            "--library",
            missingAssembly,
            "-m",
            "Member",
            "-S",
            "DefinitelyNotASection",
            "--tips",
            "q");

        Assert.Equal(1, ambiguous.Exit);
        Assert.Equal(1, explicitMember.Exit);
        Assert.Empty(ambiguous.Output);
        Assert.Empty(explicitMember.Output);
        Assert.Contains(
            "Select value 'DefinitelyNotASection' not found.",
            ambiguous.Error);
        Assert.Contains(
            "Select value 'DefinitelyNotASection' not found.",
            explicitMember.Error);
        Assert.DoesNotContain("File not found", ambiguous.Error);
        Assert.DoesNotContain("File not found", explicitMember.Error);
        Assert.DoesNotContain("  Classes", ambiguous.Error);
        Assert.DoesNotContain("  Inspection Failures", ambiguous.Error);
        Assert.DoesNotContain("  Method Groups", explicitMember.Error);
    }

    [Fact]
    public async Task Member_DottedTargetDefersAlternativeSpecificSectionUntilResolution()
    {
        string missingAssembly = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var result = await RunAppAsync(
            "member",
            "Missing.Type.Member",
            "--library",
            missingAssembly,
            "-S",
            "Signature",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("File not found", result.Error);
        Assert.DoesNotContain(
            "Select value 'Signature' not found.",
            result.Error);
    }

    [Theory]
    [InlineData("System.String")]
    [InlineData("String")]
    public async Task Member_SuppliedTypeAcceptsFullyQualifiedMemberFilter(
        string typeName)
    {
        string[] tail =
        [
            "--platform",
            "System.Runtime",
            "-S",
            "Methods",
            "--count",
            "--tips",
            "q"
        ];
        var simple = await RunAppAsync(
            ["member", typeName, "-m", "String.Contains", .. tail]);
        var qualified = await RunAppAsync(
            [
                "member",
                typeName,
                "-m",
                "System.String.Contains",
                .. tail
            ]);

        Assert.Equal(simple, qualified);
        Assert.Equal(0, qualified.Exit);
        Assert.Equal("6", qualified.Output.Trim());
        Assert.Empty(qualified.Error);
    }

    [Fact]
    public async Task Member_SimpleSuppliedTypeRetainsDifferentQualifiedType()
    {
        string[] tail =
        [
            "--platform",
            "System.Text.Json",
            "-m",
            "Other.Namespace.JsonElement.GetProperty:1",
            "-S",
            "Signature",
            "--count",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            ["member", "System.Text.Json.JsonElement", .. tail]);
        var simple = await RunAppAsync(
            ["member", "JsonElement", .. tail]);

        Assert.Equal(direct, simple);
        Assert.Equal(1, simple.Exit);
        Assert.Contains(
            "No members matched filter "
                + "'Other.Namespace.JsonElement.GetProperty'",
            simple.Error);
    }

    [Theory]
    [InlineData(
        "System.Collections.Generic.Dictionary`2.KeyCollection.CopyTo")]
    [InlineData(
        "System.Collections.Generic.Dictionary`2+KeyCollection.CopyTo")]
    public async Task Member_GlobTypeTargetNormalizesQualifiedOwner(
        string memberFilter)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Collections.Generic.Dictionary*.KeyCollection",
            "--platform",
            "System.Private.CoreLib",
            "-m",
            memberFilter,
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
    public async Task Member_QualifiedNullableGenericOptionSelectorInfersType()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "--platform",
            "System.Collections",
            "-m",
            "System.Collections.Generic.List<T>"
                + ".ConvertAll<TOutput?>",
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
    public async Task Member_SuppliedTypeRetainsQualifiedOptionSelector()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Collections.Generic.List<T>",
            "--platform",
            "System.Collections",
            "-m",
            "System.Collections.IList.IsReadOnly",
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
    public async Task Member_UserCannotActivateRouterDeferredTarget()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Collections.Immutable.ImmutableArray<T>.Builder",
            "--router-deferred-type-or-member",
            "forged",
            "--platform",
            "System.Collections.Immutable",
            "--format=markdown",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Invalid internal router state", error);
    }

    [Fact]
    public async Task Member_FullyQualifiedPlatformMember_UsesPlatformMemberFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.String.IndexOf", "--format=table", "-S", "Member Index", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("IndexOf:1", output);
        Assert.Contains("IndexOf~", output);
        Assert.Contains("M:System.String.IndexOf(char)", output);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("explicit:Abort:1")]
    [InlineData("extension:Abort:1")]
    public async Task Member_PlatformFindIfMiss_PreservesSelectorKind(
        string selector)
    {
        string[] tail =
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
                "Microsoft.AspNetCore.Http.HttpContext",
                "--platform",
                "Microsoft.AspNetCore.Http.Abstractions",
                "-m",
                selector,
                .. tail
            ]);
        var found = await RunAppAsync(
            ["member", $"HttpContext.{selector}", .. tail]);

        Assert.Equal(direct, found);
        Assert.Equal(1, found.Exit);
        Assert.Empty(found.Output);
        Assert.Contains("No members matched selector 'Abort'", found.Error);
    }

    [Fact]
    public async Task Member_PlatformFindIfMiss_PreservesSelectorDigest()
    {
        var inventory = await RunAppAsync(
            "member",
            "System.Text.Json.JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-m",
            "Serialize",
            "-S",
            SectionNames.MemberIndex,
            "--columns",
            "Stable",
            "--format=tsv",
            "--tips",
            "q");
        Assert.Equal(0, inventory.Exit);
        var stableSelector = inventory.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .First();
        Assert.Contains('~', stableSelector);

        string[] tail =
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
                "System.Text.Json.JsonSerializer",
                "--platform",
                "System.Text.Json",
                "-m",
                stableSelector,
                .. tail
            ]);
        var found = await RunAppAsync(
            ["member", $"JsonSerializer.{stableSelector}", .. tail]);

        Assert.Equal(direct, found);
        Assert.Equal(0, found.Exit);
        Assert.Equal("1", found.Output.Trim());
        Assert.Empty(found.Error);
    }

    [Theory]
    [InlineData("StatusCode")]
    [InlineData("StatusCode:1")]
    public async Task Member_PlatformFindIfMiss_PreservesExplicitIndex(
        string selector)
    {
        string[] tail =
        [
            "--index",
            "2",
            "-S",
            SectionNames.Signature,
            "--format=tsv",
            "--columns",
            "canonical_signature",
            "--tips",
            "q"
        ];
        var direct = await RunAppAsync(
            [
                "member",
                "ControllerBase",
                "--platform",
                "Microsoft.AspNetCore.Mvc.Core",
                "-m",
                selector,
                .. tail
            ]);
        var found = await RunAppAsync(
            ["member", $"ControllerBase.{selector}", .. tail]);

        Assert.Equal(direct.Output, found.Output);
        Assert.Equal(0, found.Exit);
        Assert.Contains("StatusCode", found.Output);
        Assert.Empty(found.Error);
    }

    [Fact]
    public async Task Member_GenericMemberSelector_NormalizesGenericTypeArguments()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "-m", "Deserialize<TValue>", "--format=table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Deserialize", output);
        Assert.Contains("Deserialize<TValue>", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_MethodsTable_OmitsDecodeColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "-m", "Serialize", "--format=table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Serialize", output);
        // The Decode degradation marker is never a default table column; it surfaces on stderr.
        Assert.DoesNotContain("Decode", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_MethodsTable_ShowsAlwaysOnDigestColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "-m", "Serialize", "--tips", "q");

        Assert.Equal(0, exit);
        // The durable ~digest handle is always shown as a Digest column in the default member table.
        Assert.Contains("| Name | Digest | Signature | Description |", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_CompactSummaryTables_OmitDecodeColumn()
    {
        // The compact per-kind summary tables (Constructors, Properties, Fields, Method
        // Groups) also drop the empty Decode degradation column; degradation surfaces on stderr.
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Constructors", output);
        Assert.Contains("## Properties", output);
        Assert.DoesNotContain("Decode", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_Json_CarriesDigestAndCanonicalSignature()
    {
        // The agent-facing JSON must expose the durable overload handle (digest) and the
        // doc-ID canonical signature, not just the human-facing Markdown Digest column.
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json.JsonSerializer.Serialize:1", "--format=json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("\"digest\": \"1dc14dd1fb\"", output);
        Assert.Contains("\"canonical_signature\": \"M:System.Text.Json.JsonSerializer.Serialize", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_NumericMemberFilter_MatchesLongSelector()
    {
        var shortSelector = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-m", "5", "--format=table", "--tips", "q");
        var longSelector = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "--member", "5", "--format=table", "--tips", "q");

        Assert.Equal(longSelector, shortSelector);
        Assert.Equal(1, shortSelector.Exit);
        Assert.Empty(shortSelector.Output);
        Assert.Contains("No members matched filter '5'", shortSelector.Error);
    }

    [Fact]
    public async Task Member_OverloadDetail_ShowsDigestAndCanonicalSignature()
    {
        // Drilling into a single overload (even via the non-durable positional :N) must surface
        // the durable Digest and the Canonical Signature so the reference can be upgraded.
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json.JsonSerializer.Serialize:6", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("| Signature | Digest | Canonical Signature | Description |", output);
        Assert.Contains("M:System.Text.Json.JsonSerializer.Serialize", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_ConstructorSelector_NormalizesCtorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "-m", "ctor", "--format=table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(".ctor", output);
        Assert.Contains("public String(", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_BareSimpleTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "Regex", "-m", "Match", "-S", "Member Index", "--rows", "4", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.RegularExpressions.Regex", output);
        Assert.Contains("`Match:1`", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_NamespacePrefixInput_PrintsPrefixBrowseHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("member requires a type name", error);
        Assert.Contains("looks like a namespace prefix", error);
        Assert.Contains("type System.Text", error);
        Assert.Contains("find \"System.Text*\" --platform", error);
    }

    [Fact]
    public async Task Member_MixedPipelineCountMap_RetainsRequestedZeroRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SampleKeywordParameterHost).FullName!,
            "--library", TestAssemblyPath,
            nameof(SampleKeywordParameterHost.Instance),
            "-S", "Methods,Member Index,Signature",
            "--count", "--format=json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var counts = document.RootElement
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("section").GetString()!,
                row => row.GetProperty("count").GetInt32());
        Assert.Equal(0, counts["Member Index"]);
        Assert.Equal(0, counts["Methods"]);
        Assert.Equal(1, counts["Signature"]);
    }

    [Fact]
    public async Task MemberList_BareSelect_KeepsTheInfoSet()
    {
        // `type` and `member` share ApiCommand's preamble and both run in singleTypeMode, so the
        // fixed overview is scoped by the options record and member view rather than by that flag.
        // This is the negative case for the detail-view conversion: a broad member list retains its
        // compact summary preset and must not pick up Type Info. See #3547.
        var (exit, output, _) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--platform", "System.Text.Json",
            "-S", "--tips", "q");

        Assert.Equal(0, exit);

        var sections = SectionHeadings(output);
        Assert.DoesNotContain(SectionNames.TypeInfo, sections);
        Assert.NotEmpty(sections);
    }

    [Fact]
    public async Task Member_DiscoverSection_ShowIndex_ListsMemberIndexColumns()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Member Index"],
            Select = ["Member Index"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Selecting the dedicated Member Index section renders its selector/identity columns.
        Assert.Contains("| Stable | column |", output);
        Assert.Contains("| Canonical Signature | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_HidesSelectColumn()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Methods"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // The historical Select column is not queryable, so effective discovery
        // must not list it (regression: member effective used to leak it).
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_ListsMethodsAlternate()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Methods | section |", output);
    }

    [Fact]
    public async Task Member_DiscoverEffective_ListsCategoriesBeforeSections()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        var rows = ExtractDiscoveryRows(output);
        int lastCategoryIndex = rows.FindLastIndex(row => row.Kind == "category");
        int firstSectionIndex = rows.FindIndex(
            row => row.Kind.StartsWith("section", StringComparison.Ordinal));
        Assert.True(lastCategoryIndex >= 0 && firstSectionIndex > lastCategoryIndex);
    }

    [Fact]
    public async Task Member_DiscoveryUsesAuthoredCategoriesWithoutComputedPoles()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "-D",
            "--format=table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith(SectionCategoryNames.Audit, output, StringComparison.Ordinal);
        Assert.Contains(SectionCategoryNames.Calls, output, StringComparison.Ordinal);
        Assert.Contains(SectionCategoryNames.Decompiler, output, StringComparison.Ordinal);
        Assert.Contains(SectionCategoryNames.Member, output, StringComparison.Ordinal);
        Assert.Contains(SectionCategoryNames.Performance, output, StringComparison.Ordinal);
        Assert.Contains(SectionCategoryNames.Source, output, StringComparison.Ordinal);
        Assert.Contains(SectionCategoryNames.SourceLink, output, StringComparison.Ordinal);
        Assert.DoesNotContain("@All", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@Default", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@Hidden", output, StringComparison.Ordinal);
        string[] rows = output.Split(
            '\n',
            StringSplitOptions.TrimEntries
            | StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(
            rows,
            row => row.StartsWith(
                SectionNames.Signature,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            rows,
            row => row.StartsWith(
                SectionNames.CallGraph,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            rows,
            row => row.StartsWith(
                SectionNames.PerformanceTriage,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            rows,
            row => row.StartsWith(
                SectionNames.SourceDiff,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Member_CommandCatalogCategory_CombinesApplicableRoutes()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "-D",
            SectionCategoryNames.Calls,
            "--format=table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        foreach (string section in new[]
                 {
                     SectionNames.CalledTypes,
                     SectionNames.Calls,
                     SectionNames.Callers,
                     SectionNames.CallGraph,
                 })
        {
            Assert.Contains(
                output.Split(
                    '\n',
                    StringSplitOptions.TrimEntries
                    | StringSplitOptions.RemoveEmptyEntries),
                row => row.StartsWith(
                    section,
                    StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("@Member", "Methods")]
    [InlineData("@Calls", "Called Types")]
    public async Task Member_BroadCategoryDiscovery_UsesBroadRoute(
        string category,
        string expectedSection)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Text.Json.JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-D",
            category,
            "--format=table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(expectedSection, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_OverloadMemberCategoryDiscovery_UsesInventoryRoute()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Text.Json.JsonSerializer",
            "Serialize",
            "--platform",
            "System.Text.Json",
            "-D",
            SectionCategoryNames.Member,
            "--format=table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(SectionNames.Methods, output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "requires exactly one member name",
            output,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Member_PerformanceCategory_DoesNotSelectExactOnlySections()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(CostOverlayFixture).FullName!,
            "--library",
            TestAssemblyPath,
            $"{nameof(CostOverlayFixture.Caller)}:1",
            "-S",
            SectionCategoryNames.Performance,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Cost Facts", output);
        Assert.DoesNotContain("## Implementation Profiles", output);
        Assert.DoesNotContain("## Clone Candidates", output);
    }

    [Fact]
    public async Task Member_BroadGlob_DoesNotSelectExactOnlySections()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.String",
            "Clone",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            "*",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Methods", output);
        Assert.DoesNotContain("## Member Index", output);
        Assert.DoesNotContain("## Signature", output);
        Assert.DoesNotContain("## Custom Attributes", output);
    }

    [Theory]
    [InlineData("Clone*", "Clone Candidates")]
    [InlineData("Implementation*", "Implementation Profiles")]
    public async Task Member_SingleMatchGlob_DoesNotSelectExactOnlySection(
        string selector,
        string exactOnlySection)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.String",
            "Contains",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            $"{selector},{SectionCategoryNames.Member}",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Methods", output);
        Assert.DoesNotContain($"## {exactOnlySection}", output);
    }

    [Theory]
    [InlineData("Member I*", "Member Index")]
    [InlineData("Clone*", "Clone Candidates")]
    public async Task Member_ResolvedType_ReappliesExactOnlyPolicy(
        string selector,
        string exactOnlySection)
    {
        var qualified = await RunAppAsync(
            "member",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            selector,
            "--format=markdown",
            "--tips",
            "q");
        var resolved = await RunAppAsync(
            "member",
            "String",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            selector,
            "--format=markdown",
            "--tips",
            "q");

        Assert.Equal(qualified.Exit, resolved.Exit);
        Assert.Equal(qualified.Output, resolved.Output);
        Assert.Equal(qualified.Error, resolved.Error);
        Assert.DoesNotContain($"## {exactOnlySection}", resolved.Output);
        Assert.DoesNotContain(
            $"Section '{exactOnlySection}' requires",
            resolved.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Member_DiscoveryGlob_DoesNotExposeExactOnlySection(
        bool schema)
    {
        var args = new List<string>
        {
            "member",
            "System.String",
            "Contains",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            "Clone*",
        };
        if (schema)
            args.Add("--schema");
        args.AddRange(["--format=table", "--tips", "q"]);

        var (exit, output, error) = await RunAppAsync([.. args]);

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("Rank", output);
        Assert.Contains("Section 'Clone*' not found", error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Member_DiscoveryExactName_RetainsExactOnlySection(
        bool schema)
    {
        var args = new List<string>
        {
            "member",
            "System.String",
            "Contains:1",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            SectionNames.CloneCandidates,
        };
        if (schema)
            args.Add("--schema");
        args.AddRange(["--format=table", "--tips", "q"]);

        var (exit, output, error) = await RunAppAsync([.. args]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Rank", output);
    }

    [Theory]
    [InlineData("@Calls")]
    [InlineData("@Source")]
    [InlineData("@Audit")]
    public async Task Member_OverloadDomainCategory_PreservesInventoryRoute(
        string category)
    {
        var (exit, _, error) = await RunAppAsync(
            "member",
            typeof(MemberCallsFixture).FullName!,
            "--library",
            TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded),
            "-S",
            category,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(
            "requires a single selected overload",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("@All")]
    [InlineData("@Default")]
    [InlineData("@Hidden")]
    public async Task Member_ComputedCategoryPolesAreRejected(string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.Text.Json.JsonSerializer",
            "Serialize:1",
            "--platform",
            "System.Text.Json",
            "-S",
            selector,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Select value '{selector}' not found.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_DiscoverEffective_ShowIndexAtNormal_ListsMemberIndexColumns()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Member Index"],
            Select = ["Member Index"],
            Verbosity = Verbosity.Normal
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Selecting the Member Index section renders it at Normal verbosity too.
        Assert.Contains("| Stable | column |", output);
        Assert.Contains("| Canonical Signature | column |", output);
    }

    [Fact]
    public void MemberBodyState_CrossImageOverloadOrderMismatch_IsUnknown()
    {
        var fixtureDir = Path.Combine(
            Path.GetTempPath(),
            $"body-state-order-{Guid.NewGuid():N}");

        try
        {
            string referenceAssembly = CompileBodyStateFixture(
                fixtureDir,
                "BodyStateReference",
                """
                using System.IO;
                using System.Runtime.CompilerServices;

                [assembly: ReferenceAssembly]

                namespace BodyStateOrder;

                public abstract class ReorderedOverloads
                {
                    public void Load() => throw null!;
                    public abstract void Load(Stream stream);
                }
                """);
            string runtimeAssembly = CompileBodyStateFixture(
                fixtureDir,
                "BodyStateRuntime",
                """
                using System.IO;

                namespace BodyStateOrder;

                public abstract class ReorderedOverloads
                {
                    public abstract void Load(Stream stream);
                    public void Load() { }
                }
                """);
            int selectedToken = FindMethodToken(
                referenceAssembly,
                "BodyStateOrder.ReorderedOverloads",
                "Load",
                parameterCount: 0);

            using (var referenceContext = PdbContext.OpenMetadataOnly(referenceAssembly))
                Assert.Null(referenceContext.MethodHasBody(selectedToken));
            using (var runtimeContext = PdbContext.OpenMetadataOnly(runtimeAssembly))
            {
                Assert.False(
                    runtimeContext.MethodHasBody(
                        "BodyStateOrder.ReorderedOverloads",
                        "Load",
                        overloadIndex: 0,
                        publicOnly: true));
            }

            bool? bodyState = ApiCommand.ResolveMemberBodyState(
                runtimeAssembly,
                "BodyStateOrder.ReorderedOverloads",
                "Load",
                overloadIndex: 0,
                publicOnly: true,
                referenceAssembly,
                selectedToken,
                log: null);

            Assert.Null(bodyState);
        }
        finally
        {
            if (Directory.Exists(fixtureDir))
                Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_ColumnProjectionWithJson_IsRejected()
    {
        // #3386: the member path shares the single-type writer, so it inherits the rejection.
        var (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime", "-S", "Methods", "--fields", "Name", "--format=json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --format json", error);
    }

    [Fact]
    public async Task Member_TabularUnknownFieldDoesNotPublishRowsBeforeFailure()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "--fields",
            "NoSuchField",
            "--format=tsv",
            "--rows",
            "1",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "No fields matched projection: NoSuchField",
            error);
    }

    [Fact]
    public async Task Member_LibraryNetmoduleExactMemberPreservesExecutionAndDiscovery()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-{Guid.NewGuid():N}-Widget.dll");
        WriteNetmodule(path);
        try
        {
            var execution = await RunAppAsync(
                "member", "N.Widget", "--library", path,
                "Value:1", "-S", "Signature", "--tips", "q");
            var discovery = await RunAppAsync(
                "member", "N.Widget", "--library", path,
                "Value:1", "-D", "Signature", "--tips", "q");

            Assert.Equal(0, execution.Exit);
            Assert.Empty(execution.Error);
            Assert.Contains("public int Value", execution.Output);
            Assert.Equal(0, discovery.Exit);
            Assert.Empty(discovery.Error);
            Assert.Contains("| Signature |", discovery.Output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Member_SelectedOverload_DefaultShowsSignatureOnly()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("public static System.Text.Json.JsonElement SerializeToElement<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)", output);
        Assert.Contains("Type: System.Text.Json.JsonSerializer", output);
        Assert.DoesNotContain("## Methods", output);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("## PDB Source", output);
        Assert.DoesNotContain("## IL", output);
    }

    [Fact]
    public async Task Member_ListDefault_RendersCompactSummaryRows()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("## Method Groups", output);
        Assert.Contains("| Name | Return Type | Overloads |", output);
        Assert.Contains("| SerializeToNode | System.Text.Json.Nodes.JsonNode? | 5 |", output);
        Assert.DoesNotContain("| Name | Signature | Description |", output);
    }

    [Fact]
    public async Task Member_FilteredDefault_RendersOverloadRows()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToNode" },
            Select = ["Member Index"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Member Index", output);
        Assert.Contains("| Selector | Stable | Canonical Signature |", output);
        Assert.Contains("`SerializeToNode:1`", output);
        Assert.Contains("SerializeToNode~", output);
        Assert.Contains("M:System.Text.Json.JsonSerializer.SerializeToNode(", output);
        Assert.DoesNotContain("## Method Groups", output);
        Assert.DoesNotContain("| Name | Return Type | Overloads |", output);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_DefaultStaysOverloadInventory()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            Select = ["Member Index"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Member Index", output);
        Assert.Contains("| Selector | Stable | Canonical Signature |", output);
        Assert.Contains("`GetConverter`", output);
        Assert.DoesNotContain("## Signature", output);

        options = options with { Select = ["Methods"] };
        (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Methods", output);
        Assert.Contains("| Name | Digest | Signature |", output);
        Assert.DoesNotContain("## Signature", output);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_MixedInfoAndDetailSelect_RendersDetail()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            Select = ["Info", "IL"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## IL", output);
        Assert.Contains("IL_0000:", output);
    }

    [Fact]
    public async Task Member_SingleOverloadFilter_SelectSignature_RendersSignature()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializerOptions",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GetConverter" },
            Select = ["Signature"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("public System.Text.Json.Serialization.JsonConverter GetConverter(System.Type typeToConvert)", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectCustomAttributes_RendersCustomAttributes()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToNode" },
            OverloadIndex = 1,
            Select = ["Custom Attributes"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Custom Attributes", output);
        Assert.Contains("RequiresUnreferencedCode", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverEffective_ListsMemberBaseSections()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Signature | section |", output);
        Assert.Contains("| Decompiled Source | section |", output);
        Assert.Contains("| PDB Source | section |", output);
        Assert.Contains("| IL | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
        Assert.DoesNotContain("| Annotated Source | section |", output);
        Assert.DoesNotContain("| Calls | section |", output);
        Assert.DoesNotContain("| Unsafe Operations | section |", output);
        Assert.DoesNotContain("| Facts | section |", output);
        Assert.DoesNotContain("IR (Stages)", output);
        Assert.DoesNotContain("| Methods | section |", output);
    }

    [Fact]
    public async Task Member_DumpStages_IsNotRegistered()
    {
        var (exit, _, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json", "--dump-stages", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.Contains("Unrecognized option '--dump-stages'", error);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverSchema_ListsDetailSectionsWithCallGraphOptIn()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            Discover = [],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| PDB Source | section |", output);
        Assert.Contains("| Calls | section |", output);
        Assert.Contains("| Callers | section |", output);
        Assert.Contains("| Call Graph | section |", output);
        Assert.Contains("| Facts | section |", output);
        Assert.Contains("| Unsafe Operations | section |", output);
    }

    [Fact]
    public async Task Member_BareNameCallGraph_AutoSelectsSingleOverload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "-S", "Call Graph", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Call Graph", output);
        Assert.Contains(nameof(MemberCallGraphFixture.RootCall), output);
        Assert.DoesNotContain("Select value 'Call Graph' not found", error);
    }

    [Theory]
    [InlineData("markdown")]
    [InlineData("table")]
    [InlineData("mermaid")]
    public async Task Member_CallGraphTree_OverridesEnvironmentFormat(string environmentFormat)
    {
        string? originalFormat = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", environmentFormat);
            var (exit, output, error) = await RunAppAsync(
                "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
                nameof(MemberCallGraphFixture.Inner), "-S", "Call Graph", "--tree", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(nameof(MemberCallGraphFixture.RootCall), output);
            Assert.DoesNotContain("| From |", output);
            Assert.DoesNotContain("graph TD", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
        }
    }

    [Fact]
    public async Task Member_EnvironmentMermaid_AppliesOnlyToCallGraphSelection()
    {
        string? originalFormat = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "mermaid");
            var graph = await RunAppAsync(
                "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
                nameof(MemberCallGraphFixture.Inner), "-S", "Call Graph", "--tips", "q");
            var calls = await RunAppAsync(
                "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
                nameof(MemberCallGraphFixture.Inner), "-S", "Calls", "--tips", "q");

            Assert.Equal(0, graph.Exit);
            Assert.Empty(graph.Error);
            Assert.StartsWith("graph TD", graph.Output, StringComparison.Ordinal);
            Assert.Equal(0, calls.Exit);
            Assert.Empty(calls.Error);
            Assert.Contains("## Calls", calls.Output);
            Assert.DoesNotContain("graph TD", calls.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
        }
    }

    [Fact]
    public async Task Member_BareNameCallGraph_AmbiguousOverloadReportsSelectorHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "-S", "Call Graph", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Call Graph' requires a single selected overload", error);
        Assert.Contains("Overloaded~<digest>", error);
        Assert.Contains("Overloaded:1 through Overloaded:2", error);
        Assert.DoesNotContain("Select value 'Call Graph' not found", error);
    }

    [Fact]
    public async Task Member_CallGraph_VersionSkewedCallerScopeWarns()
    {
        string directory = Directory.CreateTempSubdirectory(
            "dotnet-inspect-version-skew-").FullName;
        try
        {
            string caller =
                FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
            File.Copy(
                caller,
                Path.Combine(directory, Path.GetFileName(caller)));

            var (exit, output, error) = await RunAppAsync(
                "member",
                "--library",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath(),
                "-m",
                "Api.Ping",
                "--index",
                "1",
                "-S",
                "Call Graph",
                "--bin",
                directory,
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Contains("## Call Graph", output);
            Assert.DoesNotContain("Shared.Entry.Run", output);
            Assert.Contains(
                "Warning: Call graph results are incomplete because one or more assembly bindings could not be completely reconciled within the selected graph scope.",
                error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Member_CallGraph_VersionSkewedCallerScopeWithExactTargetBindingIsCompleteAndEmpty()
    {
        string directory = Directory.CreateTempSubdirectory(
            "dotnet-inspect-version-skew-").FullName;
        try
        {
            string caller =
                FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
            File.Copy(
                caller,
                Path.Combine(directory, Path.GetFileName(caller)));
            string targetV1 =
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
            File.Copy(
                targetV1,
                Path.Combine(directory, Path.GetFileName(targetV1)));

            var (exit, output, error) = await RunAppAsync(
                "member",
                "--library",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath(),
                "-m",
                "Api.Ping",
                "--index",
                "1",
                "-S",
                "Call Graph",
                "--bin",
                directory,
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Contains("## Call Graph", output);
            Assert.DoesNotContain("Shared.Entry.Run", output);
            Assert.Empty(error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Member_CallGraph_ScopeTraversesCalleesAcrossAssembly()
    {
        string caller =
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
        string target =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();

        var scoped = await RunAppAsync(
            "member",
            "Shared.Entry",
            "RunAcrossBoundary:1",
            "--library",
            caller,
            "-S",
            "Call Graph",
            "--bin",
            Path.GetDirectoryName(target)!,
            "--tips",
            "q");
        var unscoped = await RunAppAsync(
            "member",
            "Shared.Entry",
            "RunAcrossBoundary:1",
            "--library",
            caller,
            "-S",
            "Call Graph",
            "--tips",
            "q");

        Assert.Equal(0, scoped.Exit);
        Assert.Empty(scoped.Error);
        Assert.Contains("Target.Api.Forward()", scoped.Output);
        Assert.Contains("Target.Api.Leaf()", scoped.Output);
        Assert.DoesNotContain("(external)", scoped.Output);

        Assert.Equal(0, unscoped.Exit);
        Assert.Empty(unscoped.Error);
        Assert.Contains(
            "Target.Api.Forward() (external)",
            unscoped.Output);
        Assert.DoesNotContain("Target.Api.Leaf()", unscoped.Output);
    }

    [Fact]
    public async Task Member_BareNameCallersWithCallerScope_AutoSelectsSingleOverload()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "-S", "Callers", "--bin", testDirectory, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Callers", output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), output);
    }

    [Fact]
    public async Task Member_CategoryWithCallerScope_AutoSelectsSingleOverload()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "-S", SectionCategoryNames.Member,
            "--bin", testDirectory, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Callers", output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), output);
    }

    [Fact]
    public async Task Member_BareNameCallersWithCallerScope_AmbiguousOverloadReportsSelectorHint()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "-S", "Callers", "--bin", testDirectory, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Callers' requires a single selected overload", error);
        Assert.Contains("Overloaded~<digest>", error);
        Assert.Contains("Overloaded:1 through Overloaded:2", error);
    }

    [Fact]
    public async Task Member_CategoryWithCallerScope_AmbiguousOverloadReportsSelectorHint()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "-S", SectionCategoryNames.Member,
            "--bin", testDirectory, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Callers' requires a single selected overload", error);
        Assert.Contains("Overloaded~<digest>", error);
        Assert.Contains("Overloaded:1 through Overloaded:2", error);
    }

    [Fact]
    public async Task Member_BareNameCallerScope_AutoSelectsSingleOverloadWithoutExplicitSection()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallGraphFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallGraphFixture.Inner), "--bin", testDirectory, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Callers", output);
        Assert.Contains(nameof(MemberCallGraphFixture.Mid), output);
    }

    [Fact]
    public async Task Member_BareNameCallerScope_AmbiguousOverloadReportsSelectorHint()
    {
        var testDirectory = Path.GetDirectoryName(TestAssemblyPath)!;
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded), "--bin", testDirectory, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("section 'Callers' requires a single selected overload", error);
        Assert.Contains("Overloaded~<digest>", error);
        Assert.Contains("Overloaded:1 through Overloaded:2", error);
    }

    [Fact]
    public async Task Member_DisposedOnlyUsing_RendersVariableLessInMarkdownAndStructuredDocument()
    {
        string[] member =
        [
            "member",
            typeof(CommandCaretGestureFixture).FullName!,
            "--library",
            TestAssemblyPath,
            nameof(CommandCaretGestureFixture.DisposedOnlyUsingResource),
        ];
        var markdown = await RunAppAsync(
            [.. member, "-S", "Decompiled Source", "--tips", "q"]);
        var structured = await RunAppAsync(
            [.. member, "-S", "Annotated Source Document", "--format=json", "--tips", "q"]);

        Assert.Equal(0, markdown.Exit);
        Assert.Empty(markdown.Error);
        Assert.Contains("using (new MemoryStream())", markdown.Output);
        Assert.DoesNotContain("using (MemoryStream V_", markdown.Output);

        Assert.Equal(0, structured.Exit);
        Assert.Empty(structured.Error);
        using var document = JsonDocument.Parse(structured.Output);
        string text = document.RootElement.GetProperty("text").GetString()!;
        Assert.Contains("using (new MemoryStream())", text);
        Assert.DoesNotContain("using (MemoryStream V_", text);
        Assert.Contains(
            document.RootElement.GetProperty("nodes").EnumerateArray(),
            node => node.GetProperty("medium").GetString() == "CSharp"
                && node.GetProperty("kind").GetString() == "UsingStatement");
    }

    [Theory]
    [InlineData("@Decompiler")]
    [InlineData("Annotated*")]
    [InlineData("Annotated Source D*")]
    public async Task Member_ExpandedSectionsJson_DoesNotTreatMapAsExplicitComposition(string selection)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", selection, "--format=json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("namespace", out _));
        Assert.False(document.RootElement.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task Member_HostileIlOperand_StaysInsideMarkdownAndJsonCodeSections()
    {
        const string injected = "public int Injected() => 42; //";
        const string unsafeOperand = "field\n    public int Injected() => 42; //";
        const string safeOperand = "field     public int Injected() => 42; //";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-hostile-il-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "HostileIlOperand.dll");
            WriteHostileIlOperandAssembly(dllPath);

            var (markdownExit, markdown, markdownError) = await RunAppAsync(
                "member", "Hostile.Target", "--library", dllPath,
                "GetCount:1", "-S", "Annotated Source,IL", "--tips", "q");

            Assert.Equal(0, markdownExit);
            Assert.Empty(markdownError);
            Assert.DoesNotContain(unsafeOperand, markdown, StringComparison.Ordinal);
            Assert.Equal(2, markdown.Split(safeOperand, StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain(
                markdown.ReplaceLineEndings("\n").Split('\n'),
                line => line.TrimStart().StartsWith(injected, StringComparison.Ordinal));

            foreach (string section in new[] { "Annotated Source", "IL" })
            {
                var (jsonExit, json, jsonError) = await RunAppAsync(
                    "member", "Hostile.Target", "--library", dllPath,
                    "GetCount:1", "-S", section, "--print", "--json-array", "--tips", "q");

                Assert.Equal(0, jsonExit);
                Assert.Empty(jsonError);
                using var document = JsonDocument.Parse(json);
                var stringValues = string.Join("\n", JsonStrings(document.RootElement));
                Assert.DoesNotContain(unsafeOperand, stringValues, StringComparison.Ordinal);
                Assert.Contains(safeOperand, stringValues, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// The <c>--focus</c> caret gesture is a fact renderer, so it inherits the
    /// fold in <c>AnnotationText.Format</c> — including across the line wrapping
    /// it applies, where each wrapped chunk gets its own <c>//</c> gutter. This
    /// pins the interaction between the caret gesture and the fact fold, which
    /// arrived on separate branches and first met here.
    /// </summary>
    [Fact]
    public async Task Member_HostileFactDetail_StaysInsideCommentsUnderFocus()
    {
        const string injected = "public int Injected() => 42; //";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-hostile-fact-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "HostileFactDetail.dll");
            WriteHostileFactDetailAssembly(dllPath);

            var (exit, output, error) = await RunAppAsync(
                "member", "Hostile.Target", "--library", dllPath,
                "Make", "-S", "Annotated Source", "--focus", "allocation", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);

            // Non-vacuity: the caret gesture and the fact must both render, or
            // there would be no untrusted text on the surface under test.
            Assert.Contains("^^^^", output);
            Assert.Contains("alloc.new(", output);

            foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
            {
                int payload = line.IndexOf(injected, StringComparison.Ordinal);
                if (payload < 0)
                    continue;
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                Assert.InRange(comment, 0, payload);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Every projection that can carry a caret block, each through a different
    /// formatting call. Two properties per case: the caret actually renders (so
    /// the case is not vacuous), and the hoist marker — an in-band control
    /// character — never survives into output.
    /// </summary>
    [Theory]
    [InlineData("CommandCaretGestureFixture", "Pump:1", "Annotated Source", "allocation")]
    [InlineData("CommandCaretGestureFixture", "Pump:1", "Annotated Source", "alloc")]
    [InlineData("CommandCaretGestureFixture", "Make:1", "Annotated Source", "allocation")]
    [InlineData("CostOverlayFixture", "Caller", "Cost Overlay", "cost")]
    [InlineData("CostOverlayFixture", "CallsExceptionOnly", "Semantics Overlay", "semantics")]
    public async Task Member_Focus_RendersCaretsAndNeverLeaksTheHoistMarker(
        string fixture, string selector, string section, string focus)
    {
        string typeName = fixture == "CostOverlayFixture"
            ? typeof(CostOverlayFixture).FullName!
            : typeof(CommandCaretGestureFixture).FullName!;

        var (exit, output, _) = await RunAppAsync(
            "member", typeName, "--library", TestAssemblyPath,
            selector, "--index", "1", "--all", "-S", section, "--focus", focus, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("^^^^", output);
        Assert.DoesNotContain(ILInspector.Decompiler.Annotations.AnnotationCaret.HoistMarker, output);

        var lines = output.ReplaceLineEndings("\n").Split('\n');
        int narrowed = 0;
        int examined = 0;
        foreach (int i in Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains("^^^^", StringComparison.Ordinal)))
        {
            string statement = lines[i - 1];
            Assert.StartsWith("//", lines[i], StringComparison.Ordinal);

            int caretStart = lines[i].IndexOf('^');
            int caretLength = lines[i].AsSpan(caretStart).IndexOfAnyExcept('^');
            caretLength = caretLength < 0 ? lines[i].Length - caretStart : caretLength;
            examined++;

            // The underline is contained in the statement it points at. This is
            // the whole placement contract: an expression-narrowed caret and a
            // statement-wide fallback both satisfy it, and a caret drawn left of
            // the code or running off its end does not.
            int statementStart = statement.Length - statement.AsSpan().TrimStart().Length;
            int statementEnd = statement.AsSpan().TrimEnd().Length;
            Assert.InRange(caretStart, statementStart, statementEnd);
            Assert.InRange(caretStart + caretLength, caretStart + 1, statementEnd);

            // ...and it lands on printed characters, not on the whitespace
            // between them. Trimming the extent is what makes this hold.
            Assert.False(char.IsWhiteSpace(statement[caretStart]));
            Assert.False(char.IsWhiteSpace(statement[caretStart + caretLength - 1]));

            if (caretLength < statementEnd - statementStart)
                narrowed++;
        }

        // Non-vacuity. Every assertion above is also satisfied by a caret that
        // spans the whole statement, which is what this view rendered before
        // extents existed, so the gate has to insist that narrowing actually
        // happened on every one of these cases.
        Assert.True(examined > 0, "no caret lines were examined");
        Assert.Equal(examined, narrowed);
    }

    [Fact]
    public async Task Member_NonPublicMethod_UnderIncludeAll_RendersBodyAndIL()
    {
        // #1323: a non-public method selected under --all must render its body and IL, not
        // report "has no IL body". The body-load path counts overloads on the same
        // visibility basis the member index numbered them on (all overloads under --all).
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "--all", "InternalHelper:1", "-S", "Decompiled Source,IL", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## IL", output);
        Assert.DoesNotContain("has no IL body", output);
        Assert.Contains("InternalHelper", output);
    }

    [Fact]
    public async Task Member_IL_Default_RendersNativePayload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "CallsInterfaceItem:1", "-S", "IL", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## IL", output);
        Assert.DoesNotContain("```", output);
        Assert.Contains("IL_", output);
        Assert.Contains("ret", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_RendersHiddenFactSection()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Facts" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Facts is explicitly selected, so the section renders even when the
        // method body has no hidden facts (positive-only: the empty-state notes
        // absence of findings, never asserts the method is fact-free).
        Assert.Contains("## Facts", output);
        Assert.Contains("No hidden facts found", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_RendersStructuredResearchRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-S", "Facts", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Facts", output);
        Assert.Contains(
            "| Member | IL | Cs Line | Anchor | Category | Id | Detail | Evidence Subject | Evidence State | Evidence Locations | Conditionality | Census Receipt | Instance Key |",
            output);
        Assert.Contains("FactsTableFixture::BoxInt", output);
        Assert.Contains("`IL_", output);
        Assert.Matches(
            @"\| offset \| Allocation \| alloc\.box \| `int; alloc=boxed System\.Int32; path=straight-line; path-confidence=dominates-return; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return; multiplicity=once` \|  \|  \|  \| Always \| [0-9a-f-]{36} \| 1 \|",
            output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_TsvIncludesStructuredColumns()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-S", "Facts", "--format=tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        string row = Assert.Single(
            output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        string[] columns = row.Split('\t');
        Assert.Equal(13, columns.Length);
        Assert.EndsWith("FactsTableFixture::BoxInt", columns[0]);
        Assert.StartsWith("IL_", columns[1]);
        Assert.Equal("offset", columns[3]);
        Assert.Equal("Allocation", columns[4]);
        Assert.Equal("alloc.box", columns[5]);
        Assert.Equal("", columns[7]);
        Assert.Equal("", columns[8]);
        Assert.Equal("", columns[9]);
        Assert.Equal("Always", columns[10]);
        Assert.True(Guid.TryParse(columns[11], out Guid receipt));
        Assert.NotEqual(Guid.Empty, receipt);
        Assert.Equal("1", columns[12]);
    }

    [Fact]
    public async Task Member_SelectedOverload_FindingCensusMarkdown_RendersEnvelope()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt),
            "-S", "Finding Census", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Finding Census", output);
        Assert.Contains("```json", output);
        Assert.Contains("\"fact_census_receipt\":", output);
        Assert.Contains("\"annotated_source_document\":", output);
        Assert.Contains("\"source_fact_instances\":", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_FindingCensusDefault_RendersEnvelope()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
            "-S", "Finding Census", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument envelope = JsonDocument.Parse(output);
        Assert.True(
            Guid.TryParse(
                envelope.RootElement
                    .GetProperty("fact_census_receipt")
                    .GetString(),
                out Guid receipt));
        Assert.NotEqual(Guid.Empty, receipt);
    }

    [Fact]
    public async Task Member_FindingCensus_RejectsBodylessMember()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(IGenericExplicitInterfaceFixture<>).FullName!,
            "--library", TestAssemblyPath,
            "Map:1", "-S", "Finding Census", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Finding Census", error);
    }

    [Theory]
    [InlineData("Run")]
    [InlineData("Run:1")]
    public async Task Member_FindingCensusDiscovery_OmitsBodylessMember(
        string memberSelector)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(IBodylessFindingCensusFixture).FullName!,
            "--library", TestAssemblyPath,
            memberSelector, "-D", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("| Finding Census |", output);
    }

    [Fact]
    public async Task Member_FindingCensus_UsesSelectedAccessorBodyAvailability()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-runtime-accessor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "RuntimeAccessor.dll");
            WriteRuntimeAccessorAssembly(dllPath);

            foreach (var memberName in (string[])
            [
                "Value",
                "Changed",
                "MixedValue",
                "MixedChanged",
            ])
            {
                var (bodylessExit, bodylessOutput, bodylessError) =
                    await RunAppAsync(
                        "member", "RuntimeAccessor.Target", "--library", dllPath,
                        $"{memberName}:1", "-S", "Finding Census", "--format=json",
                        "--tips", "q");

                Assert.Equal(1, bodylessExit);
                Assert.Empty(bodylessOutput);
                Assert.Contains("Finding Census", bodylessError);

                var (bodyExit, bodyOutput, bodyError) = await RunAppAsync(
                    "member", "RuntimeAccessor.Target", "--library", dllPath,
                    $"{memberName}:2", "-S", "Finding Census", "--format=json",
                    "--tips", "q");

                Assert.Equal(0, bodyExit);
                Assert.Empty(bodyError);
                using var envelope = JsonDocument.Parse(bodyOutput);
                Assert.NotEqual(
                    Guid.Empty,
                    envelope.RootElement
                        .GetProperty("fact_census_receipt")
                        .GetGuid());
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_FindingCensus_UsesResolvedIndexerAccessor()
    {
        var result = await RunAppAsync(
            "member",
            "Cases.Lookup",
            "--library",
            FixtureCatalog.CloneSearchMembers.AssemblyPath(),
            "Item:2",
            "-S",
            "Finding Census",
            "--format=json",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        using JsonDocument envelope = JsonDocument.Parse(result.Output);
        Assert.NotEqual(
            Guid.Empty,
            envelope.RootElement
                .GetProperty("fact_census_receipt")
                .GetGuid());
    }

    [Theory]
    [InlineData("MixedValue", "set_MixedValue")]
    [InlineData("MixedChanged", "remove_MixedChanged")]
    public async Task Member_BodySections_PreserveAccessorOrdinalWhenSiblingIsAbstract(
        string memberName,
        string concreteAccessor)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-mixed-accessor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dllPath = Path.Combine(tempDir, "RuntimeAccessor.dll");
            WriteRuntimeAccessorAssembly(dllPath);

            var (abstractExit, abstractOutput, abstractError) = await RunAppAsync(
                "member", "RuntimeAccessor.Target", "--library", dllPath,
                $"{memberName}:1", "-S", "Decompiled Source", "--tips", "q");

            Assert.Equal(0, abstractExit);
            Assert.Empty(abstractError);
            Assert.DoesNotContain("## Decompiled Source", abstractOutput);
            Assert.DoesNotContain(concreteAccessor, abstractOutput);

            var (concreteExit, concreteOutput, concreteError) = await RunAppAsync(
                "member", "RuntimeAccessor.Target", "--library", dllPath,
                $"{memberName}:2", "-S", "Decompiled Source", "--tips", "q");

            Assert.Equal(0, concreteExit);
            Assert.Empty(concreteError);
            Assert.Contains("## Decompiled Source", concreteOutput);
            Assert.Contains($"public void {concreteAccessor}(", concreteOutput);
            Assert.DoesNotContain("virtual ", concreteOutput);
            Assert.DoesNotContain("abstract ", concreteOutput);
            Assert.DoesNotContain("override ", concreteOutput);
            Assert.DoesNotContain("sealed ", concreteOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_ExtensionMethod_FindingCensusRenders()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:AsMemory:1", "-S", "Finding Census",
            "--format=json", "--compact", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument envelope = JsonDocument.Parse(output);
        Assert.True(
            Guid.TryParse(
                envelope.RootElement
                    .GetProperty("fact_census_receipt")
                    .GetString(),
                out Guid receipt));
        Assert.NotEqual(Guid.Empty, receipt);
    }

    [Fact]
    public async Task Member_FindingCensus_RejectsUnnarrowedOverloads()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(MemberCallsFixture.Overloaded),
            "-S", "Finding Census", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires a single selected overload", error);
    }

    [Theory]
    [InlineData("--format=table")]
    [InlineData("--format=tsv")]
    [InlineData("--format=jsonl")]
    [InlineData("--count")]
    [InlineData("-n", "1")]
    [InlineData("-n", "1", "--tail")]
    [InlineData("--rows", "1")]
    [InlineData("--fields", "facts")]
    [InlineData("--columns", "facts")]
    [InlineData("--print")]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    public async Task Member_FindingCensus_RejectsRowProjection(
        params string[] projection)
    {
        var (exit, output, error) = await RunAppAsync(
        [
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
            "-S", "Finding Census",
            .. projection,
            "--tips", "q",
        ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Member_FindingCensusJson_RejectsSectionComposition()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
            "-S", "Finding Census,Facts", "--format=json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "section 'Finding Census' must be the only selected section under --format json",
            error);
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("-n", "5", "--lines")]
    [InlineData("--rows", "1")]
    [InlineData("--fields", "Member")]
    [InlineData("--columns", "Member")]
    public async Task Member_DecompilerCategoryProjectionDoesNotBecomeFindingCensusProjection(
        params string[] projection)
    {
        var (exit, _, error) = await RunAppAsync(
        [
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
            "-S", SectionCategoryNames.Decompiler,
            .. projection,
            "--tips", "q",
        ]);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("indivisible document payload", error);
    }

    [Fact]
    public async Task Member_DecompilerCategory_OmitsFindingCensus()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
            "-S", SectionCategoryNames.Decompiler, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Finding Census", output);
        Assert.DoesNotContain("\"fact_census_receipt\":", output);
    }

    [Fact]
    public async Task Member_AllSectionsWildcard_OmitsFindingCensus()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
            "-S", "*", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Facts", output);
        Assert.DoesNotContain("## Finding Census", output);
        Assert.DoesNotContain("\"fact_census_receipt\":", output);
    }

    [Fact]
    public async Task Member_DecompilerDiscovery_OmitsFindingCensus()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
            "-D", SectionCategoryNames.Decompiler, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("| Finding Census |", output);
    }

    [Theory]
    [InlineData()]
    [InlineData("--format=json")]
    public async Task Member_FindingCensusGlob_DoesNotSelectExactOnlySection(
        params string[] format)
    {
        var (exit, output, error) = await RunAppAsync(
        [
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
            "-S", "Finding*",
            .. format,
            "--tips", "q",
        ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Finding Census", output);
    }

    [Theory]
    [InlineData()]
    [InlineData("--count")]
    [InlineData("--format=json")]
    public async Task Member_CategoryPlusFindingCensusGlob_RetainsCategorySections(
        params string[] format)
    {
        var (exit, output, error) = await RunAppAsync(
        [
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
            "-S", $"{SectionCategoryNames.Member},Finding*",
            .. format,
            "--tips", "q",
        ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.NotEmpty(output);
        Assert.DoesNotContain("Finding Census", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_ReportsCompleteEmptyCensus()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-S", "Fidelity Causes", "--format=table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Complete", output);
        Assert.Contains("decompiler fidelity is Full", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_EmptyBodyIsComplete()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FidelityCauseFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FidelityCauseFixture.EmptyBody),
            "-S", "Decompiled Source,Fidelity Causes,Annotated Source,Cost Overlay,Semantics Overlay",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## Annotated Source", output);
        Assert.Contains("## Cost Overlay", output);
        Assert.Contains("## Semantics Overlay", output);
        Assert.Contains("public static void EmptyBody()", output);
        Assert.Contains("Complete", output);
        Assert.Contains("decompiler fidelity is Full", output);
        Assert.DoesNotContain(ILInspector.Decompiler.DiagnosticIds.EmptyOutput, output);
        Assert.DoesNotContain("Failed", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_ImporterCrashIsFailed()
    {
        var assemblyPath = Path.Combine(
            Path.GetTempPath(),
            $"fidelity-failed-{Guid.NewGuid():N}.dll");
        try
        {
            WriteFidelityFailureAssembly(assemblyPath);

            var (exit, output, error) = await RunAppAsync(
                "member", "FidelityFailedFixture.Malformed", "InvalidCall",
                "--library", assemblyPath,
                "-S", "Fidelity Causes", "--format=table", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Failed", output);
            Assert.Contains(ILInspector.Decompiler.DiagnosticIds.InternalError, output);
            Assert.Contains("importer crash", output);
            Assert.DoesNotContain("Absent", output);
        }
        finally
        {
            File.Delete(assemblyPath);
        }
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_ReportsAbsentBody()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SamplePInvokeClass).FullName!, "--library", TestAssemblyPath,
            nameof(SamplePInvokeClass.GetCurrentProcessId), "--all",
            "-S", "Fidelity Causes", "--format=table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Absent", output);
        Assert.Contains("no decompiler IR body", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFidelityCauses_RendersTypedCause()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FidelityCauseFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FidelityCauseFixture.TypedReferenceType),
            "-S", "Fidelity Causes", "--format=tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("state\tcode\tlocation\tnode_kind\tnode\tdiscriminator\treason", output);
        Assert.Contains("Complete", output);
        Assert.Contains("DEC0004", output);
        Assert.Matches(@"IL_[0-9A-F]{4}", output);
        Assert.Contains("mkrefany", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverSchema_ListsFidelityCauses()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FidelityCauseFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FidelityCauseFixture.EmptyBody), "-D", "--schema");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Fidelity Causes | section |", output);
    }

    [Fact]
    public async Task Member_FidelityCauses_IsExplicitOnly_NotShownAtDetailed()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Fidelity Causes", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectAppliedTaste_DefaultRenderReportsNoChoices()
    {
        // The byte-divergent style lenses are opt-in (off by shipped default), so a
        // default member render applies no configurable style choice: the explicitly
        // selected Applied Taste section renders its empty-state note, not a row.
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-S", "Applied Taste", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Applied Taste", output);
        Assert.Contains("No recorded style choices were applied to this member.", output);
        Assert.DoesNotContain("byte-divergent", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoverSchema_ListsAppliedTaste()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-D", "--schema");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Applied Taste | section |", output);
    }

    [Fact]
    public async Task Member_AppliedTaste_IsExplicitOnly_NotShownAtDetailed()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsTableFixture.BoxInt), "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Applied Taste", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_IncludesResearchHeaderFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsHeaderFixture).FullName!, "--library", TestAssemblyPath,
            nameof(FactsHeaderFixture.Hot), "--all", "-S", "Facts", "--format=tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("FactsHeaderFixture::Hot\t\t\tmember-header\tCost\tcost.method\t", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectFacts_IncludesExplicitSemanticsFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsExceptionOnly), "--index", "1", "--all", "-S", "Facts", "--format=table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("semantics.callee", output);
        Assert.Contains("may-throw FormatException", output);
    }

    [Fact]
    public async Task Member_FactsTable_DistinguishesCallerFromCalleeEvidence()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsStackalloc),
            "--index", "1", "--all", "-S", "Facts", "--format=table",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Evidence Subject", output);
        Assert.Contains("Evidence State", output);
        Assert.Contains("Evidence Locations", output);
        Assert.Contains(nameof(CostOverlayFixture.CallsStackalloc), output);
        Assert.Contains(nameof(CostOverlayFixture.Stackalloc), output);
        Assert.Contains("instruction", output);
    }

    [Fact]
    public async Task Member_FactsJson_RetainsInstructionEvidenceIdentity()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsStackalloc),
            "--index", "1", "--all", "-S", "Facts", "--format=json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement fact = Assert.Single(
            document.RootElement.GetProperty("facts").EnumerateArray(),
            candidate =>
                candidate.GetProperty("id").GetString()
                    == "safety.callee");
        Assert.False(fact.TryGetProperty("remote_evidence", out _));
        JsonElement evidence = fact.GetProperty("callee_evidence");
        Assert.Equal(
            nameof(CostOverlayFixture.Stackalloc),
            evidence.GetProperty("subject").GetProperty("name").GetString());
        Assert.Equal(
            "instruction",
            evidence.GetProperty("state").GetString());
        JsonElement location = Assert.Single(
            evidence.GetProperty("locations").EnumerateArray());
        Assert.Equal(
            nameof(CostOverlayFixture.Stackalloc),
            location.GetProperty("method").GetProperty("name").GetString());
        Assert.NotEqual(
            Guid.Empty,
            location.GetProperty("method")
                .GetProperty("module_version_id")
                .GetGuid());
        Assert.True(
            location.GetProperty("method")
                .GetProperty("metadata_token")
                .GetInt32() > 0);
        Assert.True(location.GetProperty("il_offset").GetInt32() >= 0);
        Assert.True(fact.GetProperty("il_offset").GetInt32() >= 0);
    }

    [Fact]
    public async Task Member_FactsJson_RetainsMethodOnlyEvidenceWithoutOffset()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller),
            "--index", "1", "--all", "-S", "Facts", "--format=json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement fact = Assert.Single(
            document.RootElement.GetProperty("facts").EnumerateArray(),
            candidate =>
                candidate.GetProperty("id").GetString()
                    == "cost.callee");
        JsonElement evidence = fact.GetProperty("callee_evidence");
        Assert.Equal("method", evidence.GetProperty("state").GetString());
        JsonElement location = Assert.Single(
            evidence.GetProperty("locations").EnumerateArray());
        Assert.False(location.TryGetProperty("il_offset", out _));
        Assert.Equal(
            nameof(CostOverlayFixture.HotCallee),
            location.GetProperty("method").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Member_FactsJson_ShowsUnavailableInstructionEvidence()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsPointerDeref),
            "--index", "1", "--all", "-S", "Facts", "--format=json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement fact = Assert.Single(
            document.RootElement.GetProperty("facts").EnumerateArray(),
            candidate =>
                candidate.GetProperty("id").GetString()
                    == "safety.callee");
        JsonElement evidence = fact.GetProperty("callee_evidence");
        Assert.Equal(
            "instruction-unavailable",
            evidence.GetProperty("state").GetString());
        Assert.Equal(
            nameof(CostOverlayFixture.PointerDeref),
            evidence.GetProperty("subject").GetProperty("name").GetString());
        Assert.Empty(evidence.GetProperty("locations").EnumerateArray());
    }

    [Theory]
    [InlineData("--columns")]
    [InlineData("--fields")]
    public async Task Member_FactsProjectedJson_UsesLoweredEvidenceRows(
        string projection)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsStackalloc),
            "--index", "1", "--all", "-S", "Facts", "--format=json",
            projection, "Id,Evidence Subject,Evidence State",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row = Assert.Single(
            document.RootElement.GetProperty("facts").EnumerateArray(),
            candidate =>
                candidate.GetProperty("id").GetString()
                    == "safety.callee");
        Assert.Equal(
            "DotnetInspect.Cli.Tests.CostOverlayFixture.Stackalloc(int)",
            row.GetProperty("evidence_subject").GetString());
        Assert.Equal(
            "instruction",
            row.GetProperty("evidence_state").GetString());
        if (projection == "--columns")
            Assert.Equal(3, row.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("-n", "1", "--head", 0)]
    [InlineData("-n", "1", "--tail", -1)]
    [InlineData("--rows", "2..2", null, 1)]
    public async Task Member_FactsProjectedJson_AppliesItemWindowBeforeSerialization(
        string window,
        string value,
        string? direction,
        int selectedIndex)
    {
        string[] common =
        [
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(FactsTableFixture.MultipleFacts),
            "--index", "1", "--all", "-S", "Facts", "--format=json",
            "--columns", "Id", "--tips", "q",
        ];
        var (allExit, allOutput, allError) =
            await RunAppAsync(common);
        string[] selection = direction is null
            ? [window, value]
            : [window, value, direction];
        var (windowExit, windowOutput, windowError) =
            await RunAppAsync([.. common, .. selection]);

        Assert.Equal(0, allExit);
        Assert.Empty(allError);
        using JsonDocument allDocument =
            JsonDocument.Parse(allOutput);
        string[] allIds = allDocument.RootElement
            .GetProperty("facts")
            .EnumerateArray()
            .Select(row => row.GetProperty("id").GetString()!)
            .ToArray();
        Assert.True(allIds.Length > 1);

        Assert.True(windowExit == 0, windowError);
        Assert.Empty(windowError);
        using JsonDocument windowDocument =
            JsonDocument.Parse(windowOutput);
        JsonElement selected = Assert.Single(
            windowDocument.RootElement
                .GetProperty("facts")
                .EnumerateArray());
        Assert.Equal(
            selectedIndex < 0
                ? allIds[^1]
                : allIds[selectedIndex],
            selected.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Member_FactsProjectedJson_RejectsUnavailableWindow()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(FactsTableFixture.MultipleFacts),
            "--index", "1", "--all", "-S", "Facts", "--format=json",
            "--columns", "Id", "--rows", "999..999", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "requires fact row 999",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_FactsProjectedJson_DeduplicatesEquivalentSelectors()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(FactsTableFixture.MultipleFacts),
            "--index", "1", "--all", "-S", "Facts,Facts", "--format=json",
            "--columns", "Id", "-n", "1", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Single(
            document.RootElement
                .GetProperty("facts")
                .EnumerateArray());
    }

    [Fact]
    public async Task Member_FactsCount_DoesNotActivateProjectedJsonAdoption()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(FactsTableFixture.MultipleFacts),
            "--index", "1", "--all", "-S", "Facts", "--format=json",
            "--columns", "Id", "--count", "--rows", "999..999",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(
            "0",
            output.Trim());
    }

    [Theory]
    [InlineData("1..1", "--head", "1")]
    [InlineData("1..1", "--tail", "1")]
    [InlineData("999..999", "--head", "0")]
    [InlineData("999..999", "--tail", "0")]
    public async Task Member_FactsCount_LegacyRowsComposeWithInferredLines(
        string rows,
        string direction,
        string expectedCount)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(FactsTableFixture.MultipleFacts),
            "--index", "1", "--all", "-S", "Facts", "--count",
            "--rows", rows, "-n", "1", direction, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(expectedCount, output.Trim());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Member_FactsDiscovery_DoesNotActivateProjectedJsonAdoption(
        bool schema)
    {
        string[] discovery = schema
            ? ["-D", "--schema"]
            : ["-D"];
        string[] arguments =
        [
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(FactsTableFixture.MultipleFacts),
            .. discovery,
            "-S", "Facts", "--format=json",
            "--columns", "Name", "-n", "1", "--tips", "q",
        ];
        var (exit, output, error) = await RunAppAsync(arguments);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--head")]
    [InlineData("--tail")]
    public async Task Member_FactsJson_DirectionRequiresCount(
        string direction)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!,
            "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsStackalloc),
            "--index", "1", "--all", "-S", "Facts", "--format=json",
            direction, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"{direction} requires -n", error);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectCostOverlay_RendersExplicitCostFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-S", "Cost Overlay", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Cost Overlay", output);
        Assert.Contains("cost.callee", output);
        Assert.Contains("alloc-loop", output);
        Assert.DoesNotContain("cost.method(root-reach 1", output);
    }

    [Fact]
    public async Task Member_CostOverlay_IsExplicitOnly_NotShownAtDetailed()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Cost Overlay", output);
        Assert.DoesNotContain("cost.callee", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_CostOverlay_DefaultRendersPayload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all", "-S", "Cost Overlay", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Cost Overlay", output);
        Assert.Contains("cost.callee", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoveryListsCostOverlayAsOptIn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.Caller), "--index", "1", "--all",
            "-D", SectionCategoryNames.Performance, "--format=table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"Cost Overlay\s+section", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectSemanticsOverlay_RendersExplicitFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsExceptionOnly), "--index", "1", "--all", "-S", "Semantics Overlay", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Semantics Overlay", output);
        Assert.Contains("semantics.callee", output);
        Assert.Contains("may-throw FormatException", output);
        Assert.DoesNotContain("cost.callee", output);
    }

    [Fact]
    public async Task Member_SemanticsOverlay_IsExplicitOnly_NotShownAtDetailed()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsExceptionOnly), "--index", "1", "--all", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Semantics Overlay", output);
        Assert.DoesNotContain("semantics.callee", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SemanticsOverlay_DefaultRendersPayload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsStackalloc), "--index", "1", "--all", "-S", "Semantics Overlay", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Semantics Overlay", output);
        Assert.Contains("safety.callee", output);
        Assert.Contains("stackalloc", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DiscoveryListsSemanticsOverlayAsOptIn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CostOverlayFixture.CallsExceptionOnly), "--index", "1", "--all",
            "-D", SectionCategoryNames.Audit, "--format=table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"Semantics Overlay\s+section", output);
    }

    [Fact]
    public async Task Member_Facts_IsExplicitOnly_NotShownAtDetailed()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            Verbosity = Verbosity.Detailed
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Facts is opt-in: the inline Annotated Source view shows the same facts
        // for humans, so the structured table never auto-renders, even at -v:d.
        Assert.DoesNotContain("## Facts", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_NormalShowsSignatureOnly()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToNode" },
            OverloadIndex = 1,
            Verbosity = Verbosity.Normal
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.DoesNotContain("## Custom Attributes", output);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("## PDB Source", output);
        Assert.DoesNotContain("## IL", output);
        Assert.DoesNotContain("## Annotated Source", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_DetailedShowsLocalImplementationSections()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToNode" },
            OverloadIndex = 1,
            Verbosity = Verbosity.Detailed
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signature", output);
        Assert.Contains("## Custom Attributes", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## IL", output);
        Assert.DoesNotContain("## Annotated Source", output);
        Assert.DoesNotContain("## PDB Source", output);
        // WriteNode<TValue>'s first parameter is `in TValue`; the call site
        // passes it without a keyword (an explicit `ref` here is CS1615). The
        // operand renders bare, not as the old `ref value` spelling.
        Assert.Contains("WriteNode<TValue>(value", output);
        Assert.DoesNotContain("WriteNode<TValue>(ref value", output);
        Assert.DoesNotContain("ref ref", output);
    }

    [Fact]
    public async Task Member_TsvWithMultipleSelectedSections_ReturnsError()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods", "Properties"],
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("Selection matches 2 sections", error);
        Assert.Contains("--format table, --format tsv, and --format jsonl display one section at a time", error);
    }

    [Fact]
    public async Task Member_NarrowedMethods_TsvProjectsOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable;Canonical Signature", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("stable\tcanonical_signature", output);
        Assert.Contains("Serialize~", output);
        Assert.Contains("M:System.Text.Json.JsonSerializer.Serialize<TValue>", output);
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("return_type", output);
        Assert.DoesNotContain("overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_TableRendersOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index", "--format=table");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Stable", output);
        Assert.Contains("Canonical Signature", output);
        Assert.Contains("Serialize:1", output);
        Assert.Contains("Serialize~", output);
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("Return Type", output);
        Assert.DoesNotContain("Overloads", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_JsonlProjectsOverloadRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable;Canonical Signature", "--format=jsonl");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);

        using var first = JsonDocument.Parse(lines[0]);
        Assert.True(first.RootElement.TryGetProperty("stable", out var selector));
        Assert.True(first.RootElement.TryGetProperty("canonical_signature", out var signature));
        Assert.Contains("M:System.Text.Json.JsonSerializer.Serialize", signature.GetString());
        Assert.StartsWith("Serialize~", selector.GetString());
        Assert.DoesNotContain('`', output);
        Assert.DoesNotContain("return_type", output);
        Assert.DoesNotContain("overloads", output);
    }

    [Theory]
    [InlineData("--format=table")]
    [InlineData("--format=tsv")]
    [InlineData("--format=jsonl")]
    public async Task Member_OverloadInventory_TabularOutputContainsOnlyRows(string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", format, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.TrimEnd('\r', '\n').Split('\n');
        Assert.NotEmpty(lines);
        if (format == "--format=jsonl")
        {
            Assert.All(lines, line =>
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal("Serialize", document.RootElement.GetProperty("name").GetString());
            });
        }
        else if (format == "--format=tsv")
        {
            Assert.StartsWith("name\tdigest\tsignature", lines[0]);
            Assert.NotEmpty(lines.Skip(1));
            Assert.All(lines.Skip(1), line => Assert.StartsWith("Serialize\t", line));
        }
        else
        {
            Assert.StartsWith("Name", lines[0]);
            Assert.Contains("Digest", lines[0]);
            Assert.Contains("Signature", lines[0]);
            Assert.Contains("Serialize", output);
        }
    }

    [Theory]
    [InlineData("--format=tsv", "--rows", "2", 2)]
    [InlineData("--format=tsv", "--rows", "1..3", 3)]
    [InlineData("--format=tsv", "-n", "2", 1)]
    [InlineData("--format=jsonl", "--rows", "2", 2)]
    [InlineData("--format=jsonl", "--rows", "1..3", 3)]
    [InlineData("--format=jsonl", "-n", "2", 2)]
    public async Task Member_OverloadInventory_TabularWindowsRetainRows(
        string format, string window, string value, int expectedRows)
    {
        string[] lineSelection = window == "-n" ? ["--lines"] : [];
        var (exit, output, error) = await RunAppInDirectoryAsync(
            Environment.CurrentDirectory,
            [
                "member", "JsonSerializer", "--platform", "System.Text.Json",
                "-m", "Serialize", format, window, value, .. lineSelection, "--tips", "q",
            ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.TrimEnd('\r', '\n').Split('\n');
        if (format == "--format=jsonl")
        {
            Assert.Equal(expectedRows, lines.Length);
            Assert.All(lines, line =>
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal("Serialize", document.RootElement.GetProperty("name").GetString());
            });
        }
        else
        {
            Assert.StartsWith("name\tdigest\tsignature", lines[0]);
            Assert.Equal(expectedRows + 1, lines.Length);
            Assert.All(lines.Skip(1), line => Assert.StartsWith("Serialize\t", line));
        }
    }

    [Fact]
    public async Task Member_NarrowedMethods_StableSelectorRoundTripsToSignature()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var stableSelector = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .First();
        Assert.StartsWith("Serialize~", stableSelector);

        (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            stableSelector, "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Signature", output);
        Assert.Contains("public static string Serialize", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_UnknownProjectedColumnWarnsWithDiscoveryHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-S", "Member Index",
            "--columns", "Stable;Canonical Signature;Obsolete", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.StartsWith("stable\tcanonical_signature", output);
        Assert.Contains("Warning: column 'Obsolete' not found in section 'Member Index'", error);
        Assert.Contains("Run -D \"Member Index\" to list available columns.", error);
    }

    [Fact]
    public async Task Member_NarrowedMethods_OptionGatedProjectedColumnWarnsWithDiscoveryHint()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize",
            "--columns", "Select;Signature", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.StartsWith("signature", output);
        Assert.Contains("Warning: column 'Select' not found in section 'Methods'", error);
        Assert.Contains("Run -D \"Methods\" to list available columns.", error);
    }

    [Fact]
    public async Task Member_NonMethodRows_RenderFullDeclarations()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.IO.Stream", "--platform", "System.Runtime",
            "-m", "CanRead", "-S", "Properties",
            "--columns", "Name;Signature", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("CanRead\tpublic abstract bool CanRead { get; }", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime",
            "-m", "Empty", "-S", "Fields",
            "--columns", "Name;Signature", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Empty\tpublic static readonly string Empty", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.Math", "--platform", "System.Runtime",
            "-m", "DivRem", "-S", "Methods",
            "--columns", "Name;Signature", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("DivRem\tpublic static", output);
        Assert.DoesNotContain("System.Runtime.Versioning.NonVersionable", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.AppDomain", "--platform", "System.Runtime",
            "-m", "AssemblyLoad", "-S", "Events",
            "--columns", "Name;Signature", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("AssemblyLoad\tpublic event System.AssemblyLoadEventHandler? AssemblyLoad", output);

        (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime",
            "-m", "Chars", "-S", "Properties",
            "--columns", "Name;Signature;Description", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Chars\tpublic char this[int index] { get; }", output);
        Assert.Contains("Gets the Char object at a specified position in the current String object.", output);
    }

    [Fact]
    public async Task Member_OutParameter_RendersOutModifier()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonElement", "--platform", "System.Text.Json",
            "-m", "TryGetBytesFromBase64", "-S", "Methods",
            "--columns", "Name;Signature", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("TryGetBytesFromBase64\tpublic bool TryGetBytesFromBase64([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value)", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_DescriptionsMatchOverloads()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json@10.0.0",
            "-m", "Serialize", "-S", "Methods",
            "--columns", "Signature;Description", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static string Serialize<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)\tConverts the value of a type specified by a generic type parameter into a JSON string.", output);
        Assert.Contains("public static void Serialize<TValue>(System.IO.Stream utf8Json, TValue value, System.Text.Json.JsonSerializerOptions? options = null)\tConverts the provided value to UTF-8 encoded JSON text and write it to the Stream.", output);
    }

    [Fact]
    public async Task Member_NarrowedMethods_DescriptionsPreserveGenericAndArrayOverloadShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.String", "--platform", "System.Runtime",
            "-m", "Join", "-S", "Methods",
            "--columns", "Signature;Description", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static string Join(char separator, params System.ReadOnlySpan<object?> values)\tConcatenates the string representations of a span of objects", output);
        Assert.Contains("public static string Join(char separator, params System.ReadOnlySpan<string?> value)\tConcatenates a span of strings", output);
        Assert.Contains("public static string Join(char separator, string?[] value, int startIndex, int count)\tConcatenates an array of strings", output);
    }

    [Fact]
    public async Task Member_ObsoleteMethod_RendersObsoleteAttributeInlineInSignature()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "SampleObsoleteHost", "--library", TestAssemblyPath,
            "-m", "OldMethod", "-S", "Methods",
            "--columns", "Name;Signature", "--format=tsv");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("OldMethod\t[Obsolete(\"Use NewMethod instead.\")] public void OldMethod()", output);
    }

    [Fact]
    public async Task Member_MixedKindFilter_TsvUsesUnifiedTableRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "-m", "IsReflectionEnabledByDefault", "--format=tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("kind\tname\treturn_type\tdetail", output);
        Assert.Contains("property\tIsReflectionEnabledByDefault\tbool\tget", output);
        Assert.Contains("method\tSerialize\tvoid\t15", output);
        Assert.DoesNotContain("\n\n", output);
    }

    [Fact]
    public async Task Member_SelectMethodsAndEvents_PreservesPipelineOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "AppDomain", "--platform", "System.Private.CoreLib",
            "-S", "Methods,Events", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(
            output.IndexOf("## Methods", StringComparison.Ordinal)
            < output.IndexOf("## Events", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Member_StringBareSelect_RendersLearnMemberOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib", "-S", "--tips", "q", "--rows", "3");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        string[] headings =
        [
            "## Constructors",
            "## Fields",
            "## Properties",
            "## Method Groups",
            "## Operators",
            "## Explicit Interface Implementations",
            "## Extension Methods"
        ];

        var previous = -1;
        foreach (var heading in headings)
        {
            var current = output.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(current > previous, $"{heading} was not after the previous heading.");
            previous = current;
        }
    }

    [Fact]
    public async Task Member_StringSelectSpecialMemberKinds_RendersRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-S", "Operators,Explicit Interface Implementations,Extension Methods",
            "--tips", "q", "--rows", "3");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Operators", output);
        Assert.Contains("operator ==", output);
        Assert.Contains("## Explicit Interface Implementations", output);
        Assert.Contains("System.Collections.IEnumerable.GetEnumerator", output);
        Assert.Contains("## Extension Methods", output);
        Assert.Contains("AsMemory", output);
    }

    [Fact]
    public async Task Member_StringSupplementalSelectors_RoundTrip()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-S", "Explicit Interface Implementations,Member Index", "--tips", "q", "--rows", "4");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`explicit:System.IConvertible.ToBoolean`", output);
        Assert.Contains("explicit:System.IConvertible.ToBoolean~", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1", "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("bool System.IConvertible.ToBoolean", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1", "-S", "IL", "--tips", "q", "-n", "12", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## IL", output);
        Assert.Contains("System.Convert::ToBoolean", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1", "-S", "Decompiled Source", "--tips", "q", "-n", "12", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("bool System.IConvertible.ToBoolean", output);
        Assert.DoesNotContain("public bool System.IConvertible.ToBoolean", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "-S", "Extension Methods,Member Index", "--tips", "q", "--rows", "4");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`extension:AsMemory:1`", output);
        Assert.Contains("extension:AsMemory~", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:AsMemory:1", "-S", "IL", "--tips", "q", "-n", "12", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## IL", output);
        Assert.Contains("IL_0000:", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:Normalize:1", "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static string Normalize(this string strInput)", output);
        Assert.DoesNotContain("string Normalize()", output);

        (exit, output, error) = await RunAppAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "extension:Normalize", "--format=json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var doc = JsonDocument.Parse(output);
        foreach (var member in doc.RootElement.GetProperty("members").EnumerateArray())
            Assert.Equal("extension-method", member.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Member_BareStringAliasWithMemberFilter_RendersStringMembers()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "string", "-m", "Normalize", "-S", "Methods", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.String", output);
        Assert.Contains("## Methods", output);
        Assert.Contains("Normalize(", output);
    }

    [Fact]
    public async Task Member_EnumValueFilter_TsvAppliesMemberFilter()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "DayOfWeek", "--platform", "System.Private.CoreLib",
            "-m", "Friday", "--format=tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("name\tvalue", output);
        Assert.Contains("Friday\t5", output);
        Assert.DoesNotContain("Sunday", output);
        Assert.DoesNotContain("Saturday", output);
    }

    [Fact]
    public async Task Member_PackageLibrarySelector_ResolvesBareLibraryName()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime", "System.Text.RegularExpressions");
        try
        {
            var options = new MemberOptions
            {
                TypeName = "RegexOptions",
                PackagePath = packagePath,
                AssemblyPath = "System.Text.RegularExpressions"
            };

            var (exit, output, _) = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("RegexOptions", output);
            Assert.Contains("None", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_PackageTypeResolution_SearchesAcrossPackageLibraries()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime", "System.Text.RegularExpressions");
        try
        {
            var options = new MemberOptions
            {
                TypeName = "RegexOptions",
                PackagePath = packagePath,
                Verbosity = Verbosity.Minimal
            };

            var (exit, output, _) = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(options));

            Assert.Equal(0, exit);
            Assert.Contains("RegexOptions", output);
            Assert.Contains("Compiled", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_SingleSectionCount_WithPlainText_WritesInteger()
    {
        var options = new MemberOptions
        {
            AssemblyPath = TestAssemblyPath,
            TypeName = typeof(MemberCallGraphFixture).FullName!,
            MemberFilter = [nameof(MemberCallGraphFixture.RootCall)],
            Select = ["Calls"],
            Count = true,
            PlainText = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task MemberDiscovery_MultiOverload_DoesNotListSingleOverloadSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, nameof(MemberCallsFixture.Overloaded),
            "--library", TestAssemblyPath, "-D", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Tip:", error);

        var sections = ExtractDiscoveryRows(output)
            .Where(row => row.Kind.StartsWith("section", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Name)
            .ToArray();

        foreach (var section in SingleOverloadDiscoverySections)
            Assert.DoesNotContain(section, sections);
    }

    [Fact]
    public async Task MemberDetail_BareSelect_RendersFixedOverview()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer.SerializeToNode:1", "-S");
        var (countExit, countOutput, countError) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer.SerializeToNode:1",
            "-S", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal([SectionNames.Signature], SectionHeadings(output));
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("## IL", output);
        Assert.DoesNotContain("## PDB Source", output);
        Assert.True(
            output.Split('\n').Length <= 8,
            $"Member detail overview grew to {output.Split('\n').Length} lines.");
        Assert.DoesNotContain("Tip:", error);
        Assert.Equal(0, countExit);
        Assert.Equal("1", countOutput.Trim());
        Assert.Empty(countError);
    }

    [Fact]
    public async Task MemberList_BareSelect_RendersCompactSummaryPreset()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "-S");

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("## Method Groups", output);
        Assert.Contains("| Name | Return Type | Overloads |", output);
        Assert.Contains("| SerializeToNode | System.Text.Json.Nodes.JsonNode? | 5 |", output);
        Assert.DoesNotContain("| Name | Signature | Description |", output);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task MemberOverloadInventory_BareSelect_KeepsMethodsPreset()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer",
            "-m", "SerializeToNode", "-S", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal([SectionNames.Methods], SectionHeadings(output));
        Assert.DoesNotContain("## Signature", output);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task MemberList_ExplicitPackageQualifiedType_DoesNotSplitTrailingTypeName()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Type 'System.Text.Json' not found", error);
        Assert.Contains("# System.Text.Json.JsonSerializer", output);
        Assert.Contains("## Method Groups", output);
    }

    [Fact]
    public async Task MemberList_QualifiedPlatformTypeTypo_SuggestsType()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializizer", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Type 'JsonSerializizer' not found.", error);
        Assert.Contains("System.Text.Json.JsonSerializer", error);
        Assert.DoesNotContain("member requires a type name", error);
    }

    [Fact]
    public async Task Member_FilteredGenericMethod_RendersMethodTypeParameters()
    {
        var (exit, output, _) = await RunAppAsync(
            "member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json",
            "-m", "Serialize", "--rows", "10", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Serialize<TValue>(TValue value", output);
    }

    [Fact]
    public async Task Member_GenericMethodSelector_FiltersByMethodArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberGenericSelectorFixture).FullName!, "--library", TestAssemblyPath,
            "GenericChoice<T>", "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Fact]
    public async Task Member_GenericMethodSelector_FiltersInventoryByMethodArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberGenericSelectorFixture).FullName!, "--library", TestAssemblyPath,
            "GenericChoice<T>", "--rows", "10", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Fact]
    public async Task Member_GenericMethodSelector_MemberIndexSelectorRoundTrips()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberGenericSelectorFixture).FullName!, "--library", TestAssemblyPath,
            "GenericChoice<T>", "-S", "Member Index", "--format=table");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var selector = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("GenericChoice", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .Single();
        Assert.Contains(':', selector);

        (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberGenericSelectorFixture).FullName!, "--library", TestAssemblyPath,
            selector, "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Theory]
    [InlineData("GenericChoice<T>")]
    [InlineData("GenericChoice<T>:1")]
    public async Task Member_DottedGenericMethodSelector_FiltersByMethodArity(string memberSelector)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", $"{typeof(MemberGenericSelectorFixture).FullName!}.{memberSelector}",
            "--library", TestAssemblyPath,
            "-S", "Signature", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Fact]
    public async Task Member_DottedGenericMethodSelector_FiltersInventoryByMethodArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", $"{typeof(MemberGenericSelectorFixture).FullName!}.GenericChoice<T>",
            "--library", TestAssemblyPath,
            "--rows", "10", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("GenericChoice<T>(T value)", output);
        Assert.DoesNotContain("GenericChoice(string value)", output);
    }

    [Fact]
    public async Task Member_ImpliedGenericSelector_RejectsNoncanonicalOptionArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "MemoryExtensions.AsSpan<T>", "--platform", "System.Memory",
            "-m", "AsSpan`0", "--format=table", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires exactly one member name", error);
    }

    [Fact]
    public async Task Member_ImpliedGenericSelector_RejectsSecondMemberName()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "List<T>.ConvertAll<TOutput>", "--platform", "System.Private.CoreLib",
            "-m", "Add", "--format=table", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires exactly one member name", error);
    }

    [Fact]
    public async Task Member_GenericContainingTypeAndGenericMethod_ResolvesTheMember()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Collections.Generic.List<T>.ConvertAll<TOutput>",
            "--platform", "System.Collections",
            "-S", "Signature", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("ConvertAll<TOutput><>")]
    [InlineData("ConvertAll<<TOutput>>")]
    [InlineData("ConvertAll<?>")]
    public async Task Member_MalformedGenericMethodSelectorDoesNotBroaden(
        string memberSelector)
    {
        var target =
            $"System.Collections.Generic.List<T>.{memberSelector}";
        var (exit, output, error) = await RunAppAsync(
            "member",
            target,
            "--platform",
            "System.Collections",
            "-S",
            "Signature",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Member_QualifiedExplicitInterfaceGenericSelectorUsesMethodArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(GenericExplicitInterfaceFixture<>).FullName!,
            "--library",
            TestAssemblyPath,
            "-m",
            "explicit:DotnetInspect.Cli.Tests.IGenericExplicitInterfaceFixture<T>.Map<U,V>",
            "-S",
            SectionNames.Signature,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task Member_GenericTypeName_DoesNotAdmitMemberDetailSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Collections.Generic.List<T>",
            "--platform", "System.Private.CoreLib",
            "-S", "Signature", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Exact-member section selection requires exactly one member name.",
            error);
    }

    [Fact]
    public async Task AsyncMethods_DeclaringTypeAndSignature_RenderAsCodeSpansWithExpandedArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--section", "Async Methods", "-v:d", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        // Generic declaring types expand arity to a C#-friendly code span (no raw `2, no escaped angle brackets).
        Assert.Contains(
            "`System.Text.Json.Serialization.Converters.IAsyncEnumerableOfTConverter<T1, T2>.BufferedAsyncEnumerable`",
            output);
        // Generic signatures render as code spans with literal angle brackets.
        Assert.Contains("`System.Collections.Generic.IAsyncEnumerator<TElement> GetAsyncEnumerator", output);
        Assert.DoesNotContain("&#96;", output);
        Assert.DoesNotContain("&lt;", output);
        Assert.DoesNotContain("&gt;", output);
    }

    [Fact]
    public async Task AsyncMethods_MachineOutput_KeepsRawValuesWithoutCodeMarkup()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--section", "Async Methods", "--format=jsonl");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Contains(
            "\"declaring_type\":\"System.Text.Json.Serialization.Converters.IAsyncEnumerableOfTConverter<T1, T2>.BufferedAsyncEnumerable\"",
            output);
        Assert.DoesNotContain("<code>", output);
        Assert.DoesNotContain("`", output);
    }

    [Fact]
    public async Task PInvokeMethods_LocalFixture_RendersMarkdownMachineRowAndCount()
    {
        var (markdownExit, markdown, markdownError) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--section", "P/Invoke Methods", "--tips", "q");
        var (jsonlExit, jsonl, jsonlError) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--section", "P/Invoke Methods", "--format=jsonl", "--tips", "q");
        var (countExit, count, countError) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--section", "P/Invoke Methods", "--count", "--tips", "q");

        Assert.Equal(0, markdownExit);
        Assert.Equal(0, jsonlExit);
        Assert.Equal(0, countExit);
        Assert.Empty(markdownError);
        Assert.Empty(jsonlError);
        Assert.Empty(countError);
        Assert.Contains(
            "| GetCurrentProcessId | `DotnetInspect.Cli.Tests.SamplePInvokeClass` | kernel32.dll | `int GetCurrentProcessId()` |",
            markdown);
        Assert.Equal(
            "{\"name\":\"GetCurrentProcessId\",\"declaring_type\":\"DotnetInspect.Cli.Tests.SamplePInvokeClass\",\"module\":\"kernel32.dll\",\"signature\":\"int GetCurrentProcessId()\"}",
            jsonl.Trim());
        Assert.Equal("1", count.Trim());
    }

    [Fact]
    public async Task PInvokeMethods_DeclaringTypeAndSignature_RenderAsCodeSpansWithFunctionPointerPunctuation()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Interop.User32.EnumWindows P/Invokes exist only in the Windows build of System.Diagnostics.Process.");
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Diagnostics.Process",
            "--section", "P/Invoke Methods", "-v:d", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Contains("`Interop.User32`", output);
        // Function-pointer signature renders as a code span with literal punctuation (no escaped angle brackets).
        Assert.Contains(
            "`Interop.BOOL EnumWindows(delegate* unmanaged<nint, nint, Interop.BOOL> callback, nint extraData)`",
            output);
        Assert.DoesNotContain("&#96;", output);
        Assert.DoesNotContain("&lt;", output);
        Assert.DoesNotContain("&gt;", output);
    }

    [Fact]
    public async Task PInvokeMethods_MachineOutput_KeepsRawValuesWithoutCodeMarkup()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Interop.User32.EnumWindows P/Invokes exist only in the Windows build of System.Diagnostics.Process.");
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Diagnostics.Process",
            "--section", "P/Invoke Methods", "--format=jsonl");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Contains("\"declaring_type\":\"Interop.User32\"", output);
        Assert.Contains(
            "\"signature\":\"Interop.BOOL EnumWindows(delegate* unmanaged<nint, nint, Interop.BOOL> callback, nint extraData)\"",
            output);
        Assert.DoesNotContain("<code>", output);
        Assert.DoesNotContain("`", output);
    }
}
