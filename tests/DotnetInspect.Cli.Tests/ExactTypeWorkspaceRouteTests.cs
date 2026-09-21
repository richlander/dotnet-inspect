using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ExactTypeWorkspaceRouteTests
{
    const string PackageId = "ilinspector.metadata.test";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string SourceUrl = "https://example.test/v3/index.json";

    static readonly PackageSource Source =
        new("test", SourceUrl);

    [Fact]
    public async Task EligiblePinnedPackageRouteUsesInjectedWorkspaceCapabilities()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));
        Assert.Equal(0, exitCode);
        Assert.Contains(
            "ILInspector.Metadata.ApiType",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "string? Accessibility { get; set; }",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceRouteUsesSelectedContextAndEmitsDerivedShare()
    {
        const string otherPackageId = "unrelated.package";
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            await File.ReadAllBytesAsync(
                typeof(ApiType).Assembly.Location,
                TestContext.Current.CancellationToken));
        await AddPackageAsync(
            store,
            otherPackageId,
            BuildPartiallyMalformedTypeAssembly());
        string packet = EncodePacket(
            format: 4,
            tabs:
            [
                (PackageId, Version, Framework),
                (otherPackageId, Version, Framework),
            ],
            contexts: [[0], [1]],
            focusedTab: 1,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiType).FullName,
            ShareFormat = WorkspaceShareFormat.Packet,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "ILInspector.Metadata.ApiType",
            output,
            StringComparison.Ordinal);
        string derivedPacket = Assert.Single(
            error.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries));
        WorkspaceSharePacket derived =
            WorkspaceSharePacketCodec.Decode(
                derivedPacket,
                TestContext.Current.CancellationToken);
        Assert.Equal(0, derived.FocusedTabIndex);
        Assert.Equal(0, derived.SelectedContextIndex);
        Assert.Equal(2, derived.Contexts.Count);
        Assert.Single(derived.Registrations);
        Assert.Equal(otherPackageId, derived.Tabs[1].Source);
        Assert.IsType<PortableSubjectRequest.Type>(
            derived.ViewStates[1].Subject);
        var context =
            Assert.IsType<PortableRetainedSubjectContext.EscapedType>(
                derived.ViewStates[1].Context);
        Assert.Equal(
            typeof(ApiType).FullName,
            context.EscapedTypeIdentity);
        Assert.Equal("type.api", derived.ViewStates[1].Facet);
        Assert.IsType<PortableSubjectRequest.Workspace>(
            derived.ViewStates[2].Subject);

        WorkspacePacketRestorationResult restored =
            await WorkspacePacketRestoration.RestoreAsync(
                derivedPacket,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        await using WorkspacePacketRestoration restoration =
            Assert.IsType<WorkspacePacketRestorationResult.Restored>(
                restored).Value;
        var resolved =
            Assert.IsType<CompleteRestorationResolvedState.Version4>(
                restoration.Workspace.Snapshot.Resolved);
        CompleteRestorationResolvedViewState state =
            resolved.States.Single(candidate =>
                candidate.NavigationId == "t0");
        Assert.Equal(
            StructuralSubjectKind.Type,
            state.Initialization!.Subject!.Kind);
    }

    [Fact]
    public async Task WorkspaceRouteRendersResolvedEscapedDefinition()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/LiteralDelimiter.dll",
                BuildLiteralDelimiterTypeAssembly()));
        string packet = EncodePacket(
            format: 4,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = @"N.Outer\.Inner",
            ShareFormat = WorkspaceShareFormat.Packet,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(0, exitCode);
        Assert.Contains("LiteralValue", output, StringComparison.Ordinal);
        Assert.DoesNotContain("NestedValue", output, StringComparison.Ordinal);
        string derivedPacket = Assert.Single(
            error.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries));
        WorkspaceSharePacket derived =
            WorkspaceSharePacketCodec.Decode(
                derivedPacket,
                TestContext.Current.CancellationToken);
        var active =
            Assert.Single(
                derived.ViewStates,
                state => state.Subject
                    is PortableSubjectRequest.Type);
        var context =
            Assert.IsType<PortableRetainedSubjectContext.EscapedType>(
                active.Context);
        Assert.Equal(@"N.Outer\.Inner", context.EscapedTypeIdentity);
    }

    [Fact]
    public async Task WorkspaceRouteSchema3RefusesOnlyShare()
    {
        var store = await CachedStoreAsync();
        string packet = EncodePacket(
            format: 3,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiType).FullName,
            ShareFormat = WorkspaceShareFormat.Packet,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "ILInspector.Metadata.ApiType",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "--share is not projectable",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            "schema version 4",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceRouteRejectsUrlInput()
    {
        string packet = EncodePacket(
            format: 4,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);

        await AssertWorkspaceFailureAsync(
            $"https://dotnet-inspect.net/?w={packet}",
            "URLs are not supported");
    }

    [Fact]
    public async Task WorkspaceRouteReportsAmbiguityWithoutShare()
    {
        const string secondPackageId = "second.package";
        var store = new InMemoryPackageStore();
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ApiType).Assembly.Location,
            TestContext.Current.CancellationToken);
        await AddPackageAsync(store, PackageId, assembly);
        await AddPackageAsync(
            store,
            secondPackageId,
            BuildSimpleTypeAssembly(
                "Second",
                typeof(ApiType).Namespace!,
                nameof(ApiType)));
        string packet = EncodePacket(
            format: 4,
            tabs:
            [
                (PackageId, Version, Framework),
                (secondPackageId, Version, Framework),
            ],
            contexts: [[0, 1]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiType).FullName,
            ShareFormat = WorkspaceShareFormat.Packet,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "more than one exact Metadata definition",
            error,
            StringComparison.Ordinal);
        Assert.Contains(PackageId, error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            secondPackageId,
            error,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            WorkspaceShareOutput.UrlPrefix,
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceRouteNonProjectableQueryPreservesOutput()
    {
        var store = await CachedStoreAsync();
        string packet = EncodePacket(
            format: 4,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiType).FullName,
            ShareFormat = WorkspaceShareFormat.Packet,
            MemberFilter = ["Name"],
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(1, exitCode);
        Assert.NotEmpty(output);
        Assert.Contains(
            "--share is not projectable at type/query",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rows")]
    [InlineData("columns")]
    [InlineData("fields")]
    [InlineData("discover")]
    [InlineData("schema")]
    public async Task WorkspaceRoutePresentationOptionsPreserveShare(
        string presentation)
    {
        var store = await CachedStoreAsync();
        string packet = EncodePacket(
            format: 4,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiType).FullName,
            ShareFormat = WorkspaceShareFormat.Packet,
            TipLevel = TipLevel.Quiet,
        };
        options = WithPresentation(options, presentation);

        (int baselineExitCode, string baselineOutput, _) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options with { ShareFormat = null },
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));
        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(baselineExitCode, exitCode);
        Assert.Equal(
            string.IsNullOrEmpty(baselineOutput),
            string.IsNullOrEmpty(output));
        Assert.DoesNotContain(
            "--share is not projectable",
            error,
            StringComparison.Ordinal);
        string[] errorLines = error.Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);
        Assert.True(
            errorLines.Length > 0
                && errorLines[^1].StartsWith(
                    "ey",
                    StringComparison.Ordinal),
            $"Expected the derived packet as the final stderr line:{Environment.NewLine}{error}");
        string derivedPacket = errorLines[^1];
        WorkspaceSharePacket derived =
            WorkspaceSharePacketCodec.Decode(
                derivedPacket,
                TestContext.Current.CancellationToken);
        Assert.Equal("type.api", derived.ViewStates[1].Facet);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("rows")]
    [InlineData("columns")]
    [InlineData("fields")]
    [InlineData("discover")]
    [InlineData("schema")]
    public void WorkspaceShareChoiceAllowsPresentationOptions(
        string presentation)
    {
        TypeOptions options = WithPresentation(
            new TypeOptions
            {
                ShareFormat = WorkspaceShareFormat.Packet,
            },
            presentation);

        TypeCommand.WorkspaceTypeShareChoice choice =
            TypeCommand.WorkspaceTypeShareChoice.From(options);

        Assert.Null(choice.Refusal);
        Assert.Equal("type.api", choice.Facet?.Value);
    }

    [Theory]
    [InlineData(SectionNames.Methods)]
    [InlineData(SectionNames.CustomAttributes)]
    [InlineData(SectionNames.SourceFiles)]
    [InlineData(SectionNames.PerformanceTriage)]
    public void WorkspaceShareChoiceRefusesUnboundSemanticSections(
        string section)
    {
        var options = new TypeOptions
        {
            ShareFormat = WorkspaceShareFormat.Packet,
            IncludeSections = [section],
        };

        TypeCommand.WorkspaceTypeShareChoice choice =
            TypeCommand.WorkspaceTypeShareChoice.From(options);

        Assert.Null(choice.Facet);
        Assert.NotNull(choice.Refusal);
        Assert.Equal("type/query", choice.Refusal.Path);
    }

    [Fact]
    public async Task WorkspaceRouteSectionSelectionRefusesOnlyShare()
    {
        var store = await CachedStoreAsync();
        string packet = EncodePacket(
            format: 4,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiType).FullName,
            IncludeSections = ["Performance Triage"],
            ShareFormat = WorkspaceShareFormat.Packet,
            Format = OutputFormat.Markdown,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Performance Triage",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "--share is not projectable at type/query",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            WorkspaceShareOutput.UrlPrefix,
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            Environment.NewLine + "ey",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FileNotFound",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceRoutePreservesRichTypeMemberFacts()
    {
        var store = await CachedStoreAsync();
        string packet = EncodePacket(
            format: 4,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiTypeShape).FullName,
            Verbosity = Verbosity.Detailed,
            Format = OutputFormat.Markdown,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, _) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "static",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceRoutePreservesStreamBackedXmlDocumentation()
    {
        const string summary =
            "Documentation retained from stream-backed Package content.";
        byte[] xml = Encoding.UTF8.GetBytes(
            $"""
            <?xml version="1.0"?>
            <doc>
              <assembly>
                <name>ILInspector.Metadata</name>
              </assembly>
              <members>
                <member name="T:ILInspector.Metadata.ApiTypeShape">
                  <summary>{summary}</summary>
                </member>
              </members>
            </doc>
            """);
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/ILInspector.Metadata.dll",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)),
            ($"lib/{Framework}/ILInspector.Metadata.xml", xml));
        string packet = EncodePacket(
            format: 4,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiTypeShape).FullName,
            ShowDocs = true,
            Verbosity = Verbosity.Normal,
            Format = OutputFormat.Markdown,
            MarkdownExplicitlySet = true,
            FormatExplicitlySet = true,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(0, exitCode);
        Assert.Contains(summary, output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Compiled XML documentation",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceRouteDoesNotFallBackToFocusedContext()
    {
        const string selectedPackageId = "selected.package";
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            await File.ReadAllBytesAsync(
                typeof(ApiType).Assembly.Location,
                TestContext.Current.CancellationToken));
        await AddPackageAsync(
            store,
            selectedPackageId,
            BuildSimpleTypeAssembly(
                "Selected",
                "Selected",
                "Only"));
        string packet = EncodePacket(
            format: 4,
            tabs:
            [
                (PackageId, Version, Framework),
                (selectedPackageId, Version, Framework),
            ],
            contexts: [[0], [1]],
            focusedTab: 0,
            selectedContext: 1);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiType).FullName,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("was not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceRouteIncompleteEvidenceEmitsNoShare()
    {
        var store = new InMemoryPackageStore();
        await AddPackageAsync(
            store,
            PackageId,
            BuildPartiallyMalformedTypeAssembly());
        string packet = EncodePacket(
            format: 4,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = "Exact.Type.Good",
            ShareFormat = WorkspaceShareFormat.Packet,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, store)));

        Assert.Equal(1, exitCode);
        Assert.Contains("Exact.Type.Good", output, StringComparison.Ordinal);
        Assert.Contains(
            "--share is not projectable at "
                + "selected-context-exact-type/incomplete",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            WorkspaceShareOutput.UrlPrefix,
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-packet", "packet could not be restored")]
    [InlineData("", "packet input is invalid")]
    [InlineData("  ", "packet input is invalid")]
    public async Task WorkspaceRouteReportsInvalidPacket(
        string packet,
        string expected)
    {
        await AssertWorkspaceFailureAsync(packet, expected);
    }

    [Fact]
    public async Task WorkspaceRouteReportsMissingSelectedContext()
    {
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.ParseJson(
            """
            {"f":4,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}
            """,
            TestContext.Current.CancellationToken);
        await AssertWorkspaceFailureAsync(
            WorkspaceSharePacketCodec.Encode(packet),
            "no selected context");
    }

    [Fact]
    public async Task WorkspaceRouteAppliesReceivingHostSourcePolicy()
    {
        var store = await CachedStoreAsync();
        string packet = EncodePacket(
            format: 4,
            tabs: [(PackageId, Version, Framework)],
            contexts: [[0]],
            focusedTab: 0,
            selectedContext: 0);
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiType).FullName,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new DenyingSourceAuthorization(),
                        PackageStore = store,
                    }));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "Package source policy denied the test package.",
            error,
            StringComparison.Ordinal);
    }

    static async Task AssertWorkspaceFailureAsync(
        string packet,
        string expected)
    {
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = typeof(ApiType).FullName,
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    LoadOptions(client, new InMemoryPackageStore())));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            nameof(ArgumentException),
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RicherViewsRemainOnCompatibilityPath()
    {
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
        };

        Assert.True(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options,
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    PlatformFramework = "net9.0",
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    WorkspacePacket = "packet",
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    Verbosity = Verbosity.Normal,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    Tree = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    PlainText = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    Schema = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    ShapeOutput = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    Print = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    Count = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    RequestAllTaste = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    TypeFilter = typeof(ApiType).FullName,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    IncludeSections = ["Summary"],
                },
                out _));
        Assert.True(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with
                {
                    JsonOutput = true,
                    FormatExplicitlySet = true,
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with { IncludeAll = true },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                options with { DocsExplicitlySet = true },
                out _));
        Assert.DoesNotContain(
            typeof(ExactTypeInspectionRequest).GetProperties(),
            property => property.Name == "IncludeAll");
    }

    [Fact]
    public async Task EnvelopeContentMatchesUnprojectedJson()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var baseline = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            TipLevel = TipLevel.Quiet,
            CompactJson = true,
        };
        WorkspaceContextLoadOptions capabilities = new()
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
        };

        (int jsonExit, string jsonOutput, string jsonError) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    baseline with
                    {
                        JsonOutput = true,
                        Format = OutputFormat.Json,
                        FormatExplicitlySet = true,
                        FormatFlagExplicitlySet = true,
                    },
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(baseline),
                    capabilities));
        (int envelopeExit, string envelopeOutput, string envelopeError) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    baseline with { EnvelopeOutput = true },
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(baseline),
                    capabilities));

        Assert.Equal(0, jsonExit);
        Assert.Equal(0, envelopeExit);
        Assert.Empty(jsonError);
        Assert.Empty(envelopeError);
        using JsonDocument contentDocument =
            JsonDocument.Parse(jsonOutput);
        using JsonDocument envelopeDocument =
            JsonDocument.Parse(envelopeOutput);
        JsonElement root = envelopeDocument.RootElement;
        Assert.Equal(
            "exact-type",
            root.GetProperty("result_kind").GetString());
        Assert.True(JsonElement.DeepEquals(
            contentDocument.RootElement,
            root.GetProperty("content")));
        Assert.Equal(
            "available",
            root.GetProperty("share").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task UnavailableContentJsonRemainsVisible()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/PartiallyMalformed.dll",
                BuildPartiallyMalformedTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "Exact.Type.Malformed",
            TipLevel = TipLevel.Quiet,
            JsonOutput = true,
            Format = OutputFormat.Json,
            FormatExplicitlySet = true,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.TryGetProperty("outcome", out _));
        Assert.Contains("MalformedMetadata", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EligibleRoutePreservesDiagnosticsAndIncompleteExit()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/PartiallyMalformed.dll",
                BuildPartiallyMalformedTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "Exact.Type.Good",
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));

        Assert.Equal(1, exitCode);
        Assert.Contains("Exact.Type.Good", output, StringComparison.Ordinal);
        Assert.Contains("Warning:", error, StringComparison.Ordinal);
        Assert.Contains(
            "MalformedMetadata",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EligibleRouteUsesUnavailableForInconclusiveLookup()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/PartiallyMalformed.dll",
                BuildPartiallyMalformedTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "Exact.Type.Malformed",
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains("Warning:", error, StringComparison.Ordinal);
        Assert.Contains(
            "MalformedMetadata",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            "Could not inspect Type 'Exact.Type.Malformed'.",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "was not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EligibleRoutePreservesConstraintDiagnosticAsNonfatal()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/Constraint.dll",
                BuildModuleConstraintAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "N.Holder<T>",
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));

        Assert.Equal(0, exitCode);
        Assert.Contains("N.Holder<T>", output, StringComparison.Ordinal);
        Assert.Contains("Warning:", error, StringComparison.Ordinal);
        Assert.Contains(
            "Generic-constraint classification was incomplete",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EligibleRoutePreservesEscapedDefinitionIdentity()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/LiteralDelimiter.dll",
                BuildLiteralDelimiterTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = @"N.Outer\.Inner",
            TipLevel = TipLevel.Quiet,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    new WorkspaceContextLoadOptions
                    {
                        HttpClient = client,
                        SourceAuthorization =
                            new UniformPackageSourceAuthorization([Source]),
                        PackageStore = store,
                    }));

        Assert.Equal(0, exitCode);
        Assert.Contains("Outer.Inner", output, StringComparison.Ordinal);
        Assert.Contains("LiteralValue", output, StringComparison.Ordinal);
        Assert.DoesNotContain("NestedValue", output, StringComparison.Ordinal);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    static async Task<IPackageStore> CachedStoreAsync(
        params (string EntryPath, byte[] Content)[] entries)
    {
        var store = new InMemoryPackageStore();
        if (entries.Length == 0)
        {
            entries =
            [
                ($"lib/{Framework}/ILInspector.Metadata.dll",
                    await File.ReadAllBytesAsync(
                        typeof(ApiType).Assembly.Location,
                        TestContext.Current.CancellationToken)),
            ];
        }
        byte[] package = Archive(entries);
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(package),
            TestContext.Current.CancellationToken);
        return store;
    }

    static TypeOptions WithPresentation(
        TypeOptions options,
        string presentation) =>
        presentation switch
        {
            "count" => options with { Count = true },
            "rows" => options with { Rows = RowWindow.Head(1) },
            "columns" => options with { Columns = ["Name"] },
            "fields" => options with { Fields = ["Name"] },
            "discover" => options with { Discover = [] },
            "schema" => options with { Discover = [], Schema = true },
            _ => throw new InvalidOperationException(
                $"Unknown presentation option '{presentation}'."),
        };

    static WorkspaceContextLoadOptions LoadOptions(
        HttpClient client,
        IPackageStore store) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
        };

    static async Task AddPackageAsync(
        InMemoryPackageStore store,
        string packageId,
        byte[] assembly)
    {
        byte[] package = Archive(
            ($"lib/{Framework}/{packageId}.dll", assembly));
        await store.CommitAsync(
            packageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(package),
            TestContext.Current.CancellationToken);
    }

    static string EncodePacket(
        int format,
        (string Package, string Version, string Framework)[] tabs,
        int[][] contexts,
        int focusedTab,
        int selectedContext)
    {
        string tabJson = string.Join(
            ',',
            tabs.Select(tab =>
                $"[\"{tab.Package}\",\"{tab.Version}\","
                    + $"\"{tab.Framework}\",null]"));
        string contextJson = string.Join(
            ',',
            contexts.Select(context =>
                $"[{string.Join(',', context)}]"));
        string viewJson = string.Join(
            ',',
            Enumerable.Range(0, tabs.Length + 1).Select(index =>
                index == 0
                    ? """{"t":null,"u":{"k":"workspace"}}"""
                    : "{\"t\":" + (index - 1)
                        + ",\"u\":{\"k\":\"workspace\"}}"));
        string json =
            "{\"f\":" + format
                + ",\"t\":[" + tabJson
                + "],\"g\":[" + contextJson
                + "],\"r\":[[\"p\",\"ILInspector.\"]],\"a\":"
                + focusedTab
                + ",\"x\":" + selectedContext
                + ",\"v\":[" + viewJson + "]}";
        return WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                json,
                TestContext.Current.CancellationToken));
    }

    static byte[] BuildPartiallyMalformedTypeAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString("PartiallyMalformed.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("PartiallyMalformed"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        TypeSpecificationHandle malformedBase =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(new byte[] { 0x15 }));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Exact.Type"),
            metadata.GetOrAddString("Good"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Exact.Type"),
            metadata.GetOrAddString("Malformed"),
            baseType: malformedBase,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildSimpleTypeAssembly(
        string assemblyName,
        string typeNamespace,
        string typeName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(assemblyName + ".dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString(typeNamespace),
            metadata.GetOrAddString(typeName),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildModuleConstraintAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Constraint.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Constraint"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        ModuleReferenceHandle module =
            metadata.AddModuleReference(
                metadata.GetOrAddString("Other.netmodule"));
        TypeReferenceHandle constraint =
            metadata.AddTypeReference(
                module,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Constraint"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle holder =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Holder`1"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                holder,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(parameter, constraint);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    sealed class DenyingSourceAuthorization
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId) =>
            PackageSourceAuthorization.Deny(
                "Package source policy denied the test package.");
    }

    static byte[] BuildLiteralDelimiterTypeAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("LiteralDelimiter.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("LiteralDelimiter"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle outer =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle nested =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Inner"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(nested, outer);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer.Inner"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(2),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("NestedValue"),
            metadata.GetOrAddBlob(
                new byte[] { 0x06, 0x08 }));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("LiteralValue"),
            metadata.GetOrAddBlob(
                new byte[] { 0x06, 0x08 }));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] Archive(
        params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string entryPath, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(entryPath).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"The eligible route bypassed injected Workspace capabilities: "
                + request.RequestUri);
    }
}
