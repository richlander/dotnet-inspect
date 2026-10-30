using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
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
using DotnetInspector.PackageQueries;
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
    public async Task EligiblePinnedPackageRouteUsesInjectedPackageHouse()
    {
        var store = await CachedStoreAsync();
        var authorization =
            new UniformPackageSourceAuthorization([Source]);
        ConfiguredPackageAuthority authority = Assert.Single(
            authorization.AuthorizeSourcesFor(PackageId).Authorities);
        using IPackageSourceClient client = PackageSourceClientFactory.Create(
            authority.Source,
            authority.Association,
            new FailingHandler());
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var house = new PackageHouse(
            authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store));
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    house,
                    cancellationToken =>
                        root.IssueOperationLease(cancellationToken)));
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
    public async Task EligiblePinnedPackageRouteUsesLegacyApplicationCacheOffline()
    {
        using var cache = new IsolatedOfflineCache();
        byte[] package = Archive(
            ($"lib/{Framework}/ILInspector.Metadata.dll",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        using (var stream = new MemoryStream(package))
        {
            IPackageContent legacy =
                await new FileSystemPackageStore().CommitAsync(
                    PackageId,
                    Version,
                    NuGetCache.GetSourceKey(SourceUrl),
                    stream,
                    TestContext.Current.CancellationToken);
            Assert.NotEqual(
                FixtureProducerKeys()[1],
                legacy.ProducerKey);
        }
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            CompanionOutput = CompanionOutput.None,
            EnvelopeOutput = true,
            CompactJson = true,
            SourceOptions = new NuGetSourceOptions
            {
                Sources = [SourceUrl],
            },
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options)));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "exact-type",
            document.RootElement
                .GetProperty("result_kind")
                .GetString());
        Assert.Equal(
            typeof(ApiType).FullName,
            document.RootElement
                .GetProperty("content")
                .GetProperty("type")
                .GetProperty("fullName")
                .GetString());
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
            CompanionOutput = CompanionOutput.Tips,
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
        string[] errorLines = error.Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);
        string derivedPacket = errorLines[0];
        Assert.DoesNotContain("Tips:", error, StringComparison.Ordinal);
        int firstTip = error.IndexOf("member ApiType", StringComparison.Ordinal);
        Assert.True(firstTip >= 0);
        Assert.True(
            error.IndexOf(derivedPacket, StringComparison.Ordinal)
                < firstTip);
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
        WorkspacePacketRestoration restoration =
            Assert.IsType<WorkspacePacketRestorationResult.Restored>(
                restored).Value;
        await restoration.ExecuteAsync(activeRestoration =>
        {
            var resolved =
                Assert.IsType<CompleteRestorationResolvedState.Version4>(
                    activeRestoration.Activation.Snapshot.Resolved);
            CompleteRestorationResolvedViewState state =
                resolved.States.Single(candidate =>
                    candidate.NavigationId == "t0");
            Assert.Equal(
                StructuralSubjectKind.Type,
                state.Initialization!.Subject!.Kind);
            return Task.FromResult(0);
        });
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.Tips,
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
        Assert.DoesNotContain("Tips:", error, StringComparison.Ordinal);
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.None,
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
            CompanionOutput = CompanionOutput.None,
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
    public void ExactTypeRouteAdmitsOnlyLockedDemandShapes()
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
                    AnnotatedRelationIds = ["all"],
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
        TypeOptions exceptionRegions = options with
        {
            IncludeSections = [SectionNames.ExceptionRegions],
        };
        Assert.True(
            TypeCommand.TryCreateSharedExactTypeRequest(
                exceptionRegions,
                ResolvedMemberInspectionPlan
                    .FromCompatibilityOptions(exceptionRegions),
                out _,
                out TypeCommand.SharedExactTypePackageDemand demand));
        Assert.Equal(
            TypeCommand.SharedExactTypePackageDemand
                .DefiningImplementation,
            demand);
        TypeOptions calledTypes = options with
        {
            IncludeSections = [SectionNames.CalledTypes],
        };
        Assert.True(
            TypeCommand.TryCreateSharedExactTypeRequest(
                calledTypes,
                ResolvedMemberInspectionPlan
                    .FromCompatibilityOptions(calledTypes),
                out _,
                out demand));
        Assert.Equal(
            TypeCommand.SharedExactTypePackageDemand
                .DefiningImplementation,
            demand);
        TypeOptions allocationFacts = options with
        {
            IncludeSections = [SectionNames.AllocationFacts],
        };
        Assert.True(
            TypeCommand.TryCreateSharedExactTypeRequest(
                allocationFacts,
                ResolvedMemberInspectionPlan
                    .FromCompatibilityOptions(allocationFacts),
                out _,
                out demand));
        Assert.Equal(
            TypeCommand.SharedExactTypePackageDemand
                .DefiningImplementation,
            demand);
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                exceptionRegions with
                {
                    IncludeSections =
                    [
                        SectionNames.AllocationFacts,
                        SectionNames.ExceptionRegions,
                        SectionNames.CalledTypes,
                    ],
                },
                out _));
        Assert.False(
            TypeCommand.TryCreateSharedExactTypeRequest(
                exceptionRegions with
                {
                    FormatExplicitlySet = true,
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
        var baseline = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            CompanionOutput = CompanionOutput.None,
            CompactJson = true,
        };
        (int jsonExit, string jsonOutput, string jsonError) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    baseline with
                    {
                        JsonOutput = true,
                        Format = OutputFormat.Json,
                        FormatExplicitlySet = true,
                        FormatFlagExplicitlySet = true,
                    },
                    store));
        (int envelopeExit, string envelopeOutput, string envelopeError) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    baseline with { EnvelopeOutput = true },
                    store));

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredOutputPreservesExplicitTips(
        bool envelopeOutput)
    {
        var store = await CachedStoreAsync();
        var baseline = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            CompanionOutput = CompanionOutput.None,
            JsonOutput = !envelopeOutput,
            EnvelopeOutput = envelopeOutput,
            CompactJson = true,
            Format = OutputFormat.Json,
            FormatExplicitlySet = true,
            FormatFlagExplicitlySet = !envelopeOutput,
        };
        (int withoutExit, string withoutOutput, string withoutError) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    baseline,
                    store));
        TypeOptions withTips = baseline with
        {
            CompanionOutput = CompanionOutput.Tips,
        };
        (int withExit, string withOutput, string withError) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    withTips,
                    store));

        Assert.Equal(0, withoutExit);
        Assert.Equal(0, withExit);
        Assert.Equal(withoutOutput, withOutput);
        Assert.Empty(withoutError);
        Assert.DoesNotContain("Tips:", withError, StringComparison.Ordinal);
        Assert.Contains(
            "member ApiType",
            withError,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(withOutput);
        if (envelopeOutput)
        {
            Assert.Equal(
                "exact-type",
                document.RootElement
                    .GetProperty("result_kind")
                    .GetString());
        }
        else
        {
            Assert.Equal(
                typeof(ApiType).FullName,
                document.RootElement
                    .GetProperty("type")
                    .GetProperty("fullName")
                    .GetString());
        }
    }

    [Fact]
    public async Task UnavailableContentJsonRemainsVisible()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/PartiallyMalformed.dll",
                BuildPartiallyMalformedTypeAssembly()));
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "Exact.Type.Malformed",
            CompanionOutput = CompanionOutput.None,
            JsonOutput = true,
            Format = OutputFormat.Json,
            FormatExplicitlySet = true,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    options,
                    store));

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.TryGetProperty("outcome", out _));
        Assert.Contains("MalformedMetadata", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EligibleRoutePreservesDiagnosticsAndIncompleteExit(
        bool jsonOutput)
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/PartiallyMalformed.dll",
                BuildPartiallyMalformedTypeAssembly()));
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "Exact.Type.Good",
            CompanionOutput = CompanionOutput.Tips,
            JsonOutput = jsonOutput,
            Format = jsonOutput
                ? OutputFormat.Json
                : OutputFormat.Markdown,
            FormatExplicitlySet = jsonOutput,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    options,
                    store));

        Assert.Equal(1, exitCode);
        Assert.Contains("Exact.Type.Good", output, StringComparison.Ordinal);
        Assert.Contains("Warning:", error, StringComparison.Ordinal);
        Assert.Contains(
            "MalformedMetadata",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Tips:", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EligibleRouteUsesUnavailableForInconclusiveLookup()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/PartiallyMalformed.dll",
                BuildPartiallyMalformedTypeAssembly()));
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "Exact.Type.Malformed",
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    options,
                    store));

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
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = "N.Holder<T>",
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    options,
                    store));

        Assert.Equal(0, exitCode);
        Assert.Contains("N.Holder<T>", output, StringComparison.Ordinal);
        Assert.Contains("Warning:", error, StringComparison.Ordinal);
        Assert.Contains(
            "Generic-constraint classification was incomplete",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceExactTypeExecutesHierarchySections()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/Hierarchy.dll",
                BuildHierarchyTypeAssembly()));
        using var client = new HttpClient(new FailingHandler());
        string packet = EncodePacket(
            format: 4,
            [(PackageId, Version, Framework)],
            [[0]],
            focusedTab: 0,
            selectedContext: 0,
            registerPackagePrefix: false);
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = "Exact.Hierarchy.IContract",
            IncludeSections =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    SectionNames.Implementers,
                },
            Count = true,
            CompanionOutput = CompanionOutput.None,
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

        Assert.True(
            exitCode == 0,
            $"Expected success. Output: {output} Error: {error}");
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task WorkspacePackagePrefixExecutesHierarchySections()
    {
        const string implementationPackage =
            "ILInspector.Implementation";
        (byte[] contract, byte[] implementation) =
            BuildHierarchyPackageAssemblies();
        SearchResult[] matches =
        [
            new(PackageId, Version),
            new(implementationPackage, Version),
        ];
        var prefixSource = new PrefixPackageSource(matches);
        var store = (InMemoryPackageStore)await CachedStoreAsync(
            ($"lib/{Framework}/Hierarchy.Contract.dll", contract));
        await AddPackageAsync(
            store,
            PackageId,
            contract,
            PackageSource.NuGetOrg.Url);
        await AddPackageAsync(
            store,
            implementationPackage,
            implementation,
            PackageSource.NuGetOrg.Url);
        using var client = new HttpClient(new FailingHandler());
        string packet = EncodePacket(
            format: 4,
            [(PackageId, Version, Framework)],
            [[0]],
            focusedTab: 0,
            selectedContext: 0);
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = "Exact.Hierarchy.IContract",
            IncludeSections =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    SectionNames.Implementers,
                },
            Count = true,
            CompanionOutput = CompanionOutput.None,
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
                            new UniformPackageSourceAuthorization(
                                [Source, PackageSource.NuGetOrg]),
                        PackageStore = store,
                    },
                    new(
                        () => prefixSource,
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task
        WorkspacePackagePrefixReportsCandidateFailureWithPartialRows()
    {
        const string implementationPackage =
            "ILInspector.Implementation";
        const string missingPackage = "ILInspector.Missing";
        (byte[] contract, byte[] implementation) =
            BuildHierarchyPackageAssemblies();
        SearchResult[] matches =
        [
            new(PackageId, Version),
            new(implementationPackage, Version),
            new(missingPackage, Version),
        ];
        var prefixSource = new PrefixPackageSource(matches);
        var store = (InMemoryPackageStore)await CachedStoreAsync(
            ($"lib/{Framework}/Hierarchy.Contract.dll", contract));
        await AddPackageAsync(
            store,
            PackageId,
            contract,
            PackageSource.NuGetOrg.Url);
        await AddPackageAsync(
            store,
            implementationPackage,
            implementation,
            PackageSource.NuGetOrg.Url);
        using var client = new HttpClient(new NotFoundHandler());
        string packet = EncodePacket(
            format: 4,
            [(PackageId, Version, Framework)],
            [[0]],
            focusedTab: 0,
            selectedContext: 0);
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = "Exact.Hierarchy.IContract",
            IncludeSections =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    SectionNames.Implementers,
                },
            CompanionOutput = CompanionOutput.None,
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
                            new UniformPackageSourceAuthorization(
                                [Source, PackageSource.NuGetOrg]),
                        PackageStore = store,
                    },
                    new(
                        () => prefixSource,
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Exact.Hierarchy.Implementation",
            output,
            StringComparison.Ordinal);
        string diagnostic =
            Assert.Single(
                error.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries),
                line => line.Contains(
                        $"Package-prefix candidate '{missingPackage}@{Version}' "
                            + "could not be prepared:",
                        StringComparison.Ordinal));
        Assert.StartsWith("Warning: ", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspacePackagePrefixReportsScopeDeadlineFailure()
    {
        const string implementationPackage =
            "ILInspector.Implementation";
        (byte[] contract, byte[] implementation) =
            BuildHierarchyPackageAssemblies();
        SearchResult[] matches =
        [
            new(PackageId, Version),
            new(implementationPackage, Version),
        ];
        var prefixSource = new PrefixPackageSource(matches);
        var store = (InMemoryPackageStore)await CachedStoreAsync(
            ($"lib/{Framework}/Hierarchy.Contract.dll", contract));
        await AddPackageAsync(
            store,
            PackageId,
            contract,
            PackageSource.NuGetOrg.Url);
        await AddPackageAsync(
            store,
            implementationPackage,
            implementation,
            PackageSource.NuGetOrg.Url);
        using var client = new HttpClient(new FailingHandler());
        string packet = EncodePacket(
            format: 4,
            [(PackageId, Version, Framework)],
            [[0]],
            focusedTab: 0,
            selectedContext: 0);
        var options = new TypeOptions
        {
            WorkspacePacket = packet,
            TypeName = "Exact.Hierarchy.IContract",
            IncludeSections =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    SectionNames.Implementers,
                },
            Count = true,
            Rows = RowWindow.Head(1),
            CompanionOutput = CompanionOutput.None,
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
                            new UniformPackageSourceAuthorization(
                                [Source, PackageSource.NuGetOrg]),
                        PackageStore = store,
                    },
                    new(
                        () => prefixSource,
                        DateTimeOffset.UtcNow.AddMinutes(-1)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "Package-prefix Workspace Scope update was rejected: "
                + nameof(WorkspaceScopeRejection.DeadlineExpired),
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "requires a committed or no-effect",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EligibleRoutePreservesEscapedDefinitionIdentity()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/LiteralDelimiter.dll",
                BuildLiteralDelimiterTypeAssembly()));
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = @"N.Outer\.Inner",
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    options,
                    store));

        Assert.Equal(0, exitCode);
        Assert.Contains("Outer.Inner", output, StringComparison.Ordinal);
        Assert.Contains("LiteralValue", output, StringComparison.Ordinal);
        Assert.DoesNotContain("NestedValue", output, StringComparison.Ordinal);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EligibleRouteFindsTypeInNonNamesakeLibrary()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/ILInspector.Metadata.dll",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(
                    options,
                    store));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            typeof(ApiType).FullName!,
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultMemberRouteFindsTypeInNonNamesakeLibrary()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/ILInspector.Metadata.dll",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        var authorization =
            new UniformPackageSourceAuthorization([Source]);
        ConfiguredPackageAuthority authority = Assert.Single(
            authorization.AuthorizeSourcesFor(PackageId).Authorities);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association,
                new FailingHandler());
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var house = new PackageHouse(
            authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store));
        var options = new MemberOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            ShowDocs = true,
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    house,
                    cancellationToken =>
                        root.IssueOperationLease(cancellationToken)));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("Accessibility", output, StringComparison.Ordinal);
        Assert.Contains(
            $"Library: lib/{Framework}/ILInspector.Metadata.dll",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "selected.dll",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "inspect-type-workspace",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        DefaultMemberRouteDoesNotReadImplicitCompiledDocumentation()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/ILInspector.Metadata.dll",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)),
            ($"lib/{Framework}/ILInspector.Metadata.xml",
                Encoding.UTF8.GetBytes("<doc><members>")));
        var authorization =
            new UniformPackageSourceAuthorization([Source]);
        ConfiguredPackageAuthority authority = Assert.Single(
            authorization.AuthorizeSourcesFor(PackageId).Authorities);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association,
                new FailingHandler());
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var house = new PackageHouse(
            authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store));
        var options = new MemberOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            ShowDocs = true,
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    house,
                    cancellationToken =>
                        root.IssueOperationLease(cancellationToken)));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            nameof(ApiType.Accessibility),
            output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultMemberPositionalSelectorStaysOnPackageHouseRoute(
        bool explicitMemberFilter)
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/ILInspector.Metadata.dll",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        var authorization =
            new UniformPackageSourceAuthorization([Source]);
        ConfiguredPackageAuthority authority = Assert.Single(
            authorization.AuthorizeSourcesFor(PackageId).Authorities);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association,
                new FailingHandler());
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var house = new PackageHouse(
            authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store));
        var options = new MemberOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName =
                $"{typeof(ApiType).FullName}.{nameof(ApiType.Accessibility)}",
            MemberFilter = explicitMemberFilter
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        nameof(ApiType.Accessibility),
                    }
                : [],
            ShowDocs = true,
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    house,
                    cancellationToken =>
                        root.IssueOperationLease(cancellationToken)));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            nameof(ApiType.Accessibility),
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(ApiType.DefinitionName),
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"Library: lib/{Framework}/ILInspector.Metadata.dll",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "selected.dll",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultMemberRoutePreservesPublicTypeBoundary()
    {
        const string typeNamespace = "Hidden";
        const string typeName = "InternalType";
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/Hidden.dll",
                BuildSimpleTypeAssembly(
                    "Hidden",
                    typeNamespace,
                    typeName,
                    TypeAttributes.NotPublic)));
        var authorization =
            new UniformPackageSourceAuthorization([Source]);
        ConfiguredPackageAuthority authority = Assert.Single(
            authorization.AuthorizeSourcesFor(PackageId).Authorities);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association,
                new FailingHandler());
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var house = new PackageHouse(
            authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store));
        var options = new MemberOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = $"{typeNamespace}.{typeName}",
            MemberFilter = ["Anything"],
            ShowDocs = true,
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(
                    options,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(options),
                    house,
                    cancellationToken =>
                        root.IssueOperationLease(cancellationToken)));

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            $"Type '{typeNamespace}.{typeName}' was not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        DefiningImplementationTypeRouteRendersAllocationFactsFromNonNamesakeLibrary()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(SemanticFactsFixture).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = await CachedStoreAsync(
            ($"ref/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"ref/{Framework}/DotnetInspect.Cli.Tests.dll", assembly),
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(SemanticFactsFixture).FullName,
            Select = [SectionNames.AllocationFacts],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(options, store));

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        Assert.StartsWith(
            "member\til_offset\tallocation_kind\tallocated_type",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(SemanticFactsFixture.AllSignals),
            output,
            StringComparison.Ordinal);
        Assert.Contains("array", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        DefiningImplementationTypeRouteRendersCalledTypesFromNonNamesakeLibrary()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(CalledTypesFixture).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = await CachedStoreAsync(
            ($"ref/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"ref/{Framework}/DotnetInspect.Cli.Tests.dll", assembly),
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(CalledTypesFixture).FullName,
            Select = [SectionNames.CalledTypes],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(options, store));

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        Assert.StartsWith(
            "type\tassembly\tcalls\tmembers\tcall_kinds",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"{typeof(CalledTypesDependency).FullName}"
                + "\tDotnetInspect.Cli.Tests\t3\t2\tdirect",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        DefiningImplementationTypeRouteRendersExceptionRegionsFromNonNamesakeLibrary()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(MemberExceptionRegionsFixture).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = await CachedStoreAsync(
            ($"ref/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"ref/{Framework}/DotnetInspect.Cli.Tests.dll", assembly),
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        var options = new TypeOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(MemberExceptionRegionsFixture).FullName,
            Select = [SectionNames.ExceptionRegions],
            CompanionOutput = CompanionOutput.None,
            Verbosity = Verbosity.Minimal,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleRouteAsync(options, store));

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        Assert.Contains("\tcatch\t", output, StringComparison.Ordinal);
        Assert.Contains(
            nameof(DivideByZeroException),
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        DefiningImplementationMemberRouteRendersIlFromNonNamesakeLibrary()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ApiType).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = await CachedStoreAsync(
            ($"ref/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"ref/{Framework}/ILInspector.Metadata.dll", assembly),
            ($"lib/{Framework}/ILInspector.Metadata.dll", assembly));
        var options = new MemberOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName =
                $"{typeof(ApiType).FullName}."
                    + $"{nameof(ApiType.Accessibility)}:1",
            Select = [SectionNames.IL],
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleMemberRouteAsync(options, store));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("IL_0000: ldarg.0", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        DefiningImplementationMemberRouteRendersExceptionRegions()
    {
        byte[] assembly = await ReadTestAssemblyAsync();
        var store = await CachedStoreAsync(
            ($"ref/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"ref/{Framework}/DotnetInspect.Cli.Tests.dll", assembly),
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        var options = new MemberOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(SingleTypeSurfaceFixture).FullName,
            MemberFilter = [nameof(SingleTypeSurfaceFixture.Overloaded)],
            OverloadIndex = 1,
            Select = [SectionNames.ExceptionRegions],
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleMemberRouteAsync(options, store));

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        Assert.Contains("\tcatch\t", output, StringComparison.Ordinal);
        Assert.Contains(
            nameof(OverflowException),
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        DefiningImplementationMemberRouteRendersCustomAttributes()
    {
        byte[] assembly = await ReadTestAssemblyAsync();
        var store = await CachedStoreAsync(
            ($"ref/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"ref/{Framework}/DotnetInspect.Cli.Tests.dll", assembly),
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        var options = new MemberOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(SingleTypeSurfaceFixture).FullName,
            MemberFilter = [nameof(SingleTypeSurfaceFixture.Overloaded)],
            OverloadIndex = 1,
            Select = [SectionNames.CustomAttributes],
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleMemberRouteAsync(options, store));

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        Assert.Contains(
            "Obsolete\tfirst",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        DefiningImplementationMemberRouteRendersDecompiledSource()
    {
        byte[] assembly = await ReadTestAssemblyAsync();
        var store = await CachedStoreAsync(
            ($"ref/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"ref/{Framework}/DotnetInspect.Cli.Tests.dll", assembly),
            ($"lib/{Framework}/DotnetInspect.Cli.Tests.dll", assembly));
        var options = new MemberOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(SingleTypeSurfaceFixture).FullName,
            MemberFilter = [nameof(SingleTypeSurfaceFixture.Overloaded)],
            OverloadIndex = 1,
            Select = [SectionNames.DecompiledSource],
            CompanionOutput = CompanionOutput.None,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleMemberRouteAsync(options, store));

        Assert.True(exitCode == 0, error);
        Assert.Empty(error);
        Assert.Contains(
            "public static int Overloaded(int value)",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "catch (OverflowException)",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        PackageLibraryNamespaceRouteUsesOneSelectedSurfaceLibrary()
    {
        var store = await CachedStoreAsync(
            ($"lib/{Framework}/{PackageId}.dll",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/ILInspector.Metadata.dll",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        var options = new LibraryOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            AssemblyName = "ILInspector.Metadata.dll",
            TypeNamespace = typeof(ApiType).Namespace,
        };

        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => ExecuteEligibleLibraryNamespaceRouteAsync(
                    options,
                    store));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            typeof(ApiType).FullName!,
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Outer.Inner",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "## Library Info",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        PackageLibraryNamespaceRouteDefersRuntimeOnlyPopulation()
    {
        var store = await CachedStoreAsync(
            ($"runtimes/win-x64/lib/{Framework}/RuntimeOnly.dll",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        var options = new LibraryOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            AssemblyName = "RuntimeOnly.dll",
            TypeNamespace = typeof(ApiType).Namespace,
        };

        int? result =
            await TryExecuteEligibleLibraryNamespaceRouteAsync(
                options,
                store);

        Assert.Null(result);
    }

    [Fact]
    public async Task
        PackageLibraryNamespaceRouteDefersImplementationAssetPath()
    {
        const string Library = "Selected.dll";
        var store = await CachedStoreAsync(
            ($"ref/{Framework}/{Library}",
                BuildLiteralDelimiterTypeAssembly()),
            ($"lib/{Framework}/{Library}",
                await File.ReadAllBytesAsync(
                    typeof(ApiType).Assembly.Location,
                    TestContext.Current.CancellationToken)));
        var options = new LibraryOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            AssemblyName = $"lib/{Framework}/{Library}",
            TypeNamespace = typeof(ApiType).Namespace,
        };

        int? result =
            await TryExecuteEligibleLibraryNamespaceRouteAsync(
                options,
                store);

        Assert.Null(result);
    }

    [Fact]
    public void PackageLibraryNamespaceRouteAdmitsOnlyExactPinnedRequests()
    {
        var options = new LibraryOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            AssemblyName = "ILInspector.Metadata.dll",
            TypeNamespace = typeof(ApiType).Namespace,
        };

        Assert.True(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options,
                out PackageEndpointScopeRequest? request));
        Assert.Equal(PackageAssetDemand.Surface, request.AssetDemand);
        Assert.Equal(
            "ILInspector.Metadata.dll",
            request.LibrarySelector!.Value);
        Assert.False(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options with { PackagePath = PackageId },
                out _));
        Assert.False(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options with
                {
                    PackagePath = Path.Combine(
                        Path.GetTempPath(),
                        "Example.1.0.0.nupkg"),
                },
                out _));
        Assert.False(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options with { Tfm = null },
                out _));
        Assert.False(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options with { AssemblyName = null },
                out _));
        Assert.False(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options with { TypeNamespace = null },
                out _));
        Assert.False(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options with
                {
                    AssemblyName = "ILInspector.*",
                },
                out _));
        Assert.False(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options with { NamesakeLibrary = true },
                out _));
        Assert.False(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options with { FirstLibrary = true },
                out _));
    }

    [Fact]
    public void MemberRouteAdmitsOnlyLockedDemandShapes()
    {
        var options = new MemberOptions
        {
            PackagePath = $"{PackageId}@{Version}",
            Tfm = Framework,
            TypeName = typeof(ApiType).FullName,
            ShowDocs = true,
            CompanionOutput = CompanionOutput.None,
        };
        ResolvedMemberInspectionPlan plan =
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(options);

        Assert.True(
            MemberCommand.TryCreateSharedMemberRequest(
                options,
                plan,
                out _,
                out MemberCommand.SharedMemberPackageDemand defaultDemand));
        Assert.Equal(
            MemberCommand.SharedMemberPackageDemand.Surface,
            defaultDemand);
        MemberOptions exact = options with
        {
            MemberFilter = ["Accessibility"],
            OverloadIndex = 1,
        };
        Assert.True(
            MemberCommand.TryCreateSharedMemberRequest(
                exact,
                ResolvedMemberInspectionPlan
                    .FromCompatibilityOptions(exact),
                out _,
                out _));
        foreach (string section in (string[])
            [
                SectionNames.IL,
                SectionNames.CustomAttributes,
                SectionNames.DecompiledSource,
                SectionNames.ExceptionRegions,
            ])
        {
            MemberOptions implementation = exact with
            {
                Select = [section],
            };
            Assert.True(
                MemberCommand.TryCreateSharedMemberRequest(
                    implementation,
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(implementation),
                    out _,
                    out MemberCommand.SharedMemberPackageDemand demand));
            Assert.Equal(
                MemberCommand.SharedMemberPackageDemand
                    .DefiningImplementation,
                demand);
        }
        Assert.False(
            MemberCommand.TryCreateSharedMemberRequest(
                options with
                {
                    DocsExplicitlySet = true,
                },
                plan,
                out _,
                out _));
        MemberOptions annotated = exact with
        {
            Select = [SectionNames.AnnotatedSource],
        };
        Assert.False(
            MemberCommand.TryCreateSharedMemberRequest(
                annotated,
                ResolvedMemberInspectionPlan
                    .FromCompatibilityOptions(annotated),
                out _,
                out _));
        Assert.False(
            MemberCommand.TryCreateSharedMemberRequest(
                options with
                {
                    JsonOutput = true,
                    FormatExplicitlySet = true,
                },
                plan,
                out _,
                out _));
        Assert.False(
            MemberCommand.TryCreateSharedMemberRequest(
                options with
                {
                    PackagePath = PackageId,
                },
                plan,
                out _,
                out _));
        Assert.False(
            MemberCommand.TryCreateSharedMemberRequest(
                options with
                {
                    Tfm = null,
                },
                plan,
                out _,
                out _));
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
        foreach (string producerKey in FixtureProducerKeys())
        {
            await store.CommitAsync(
                PackageId,
                Version,
                producerKey,
                new MemoryStream(package),
                TestContext.Current.CancellationToken);
        }
        return store;
    }

    static Task<byte[]> ReadTestAssemblyAsync()
    {
        string assemblyPath =
            typeof(SingleTypeSurfaceFixture).Assembly.Location;
        if (!File.Exists(assemblyPath))
        {
            assemblyPath = Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    Path.GetFileName(assemblyPath)));
        }
        return File.ReadAllBytesAsync(
            assemblyPath,
            TestContext.Current.CancellationToken);
    }

    static string[] FixtureProducerKeys()
    {
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                Source,
                PackageSourceAssociation.Create(),
                new FailingHandler());
        return
        [
            NuGetCache.GetSourceKey(SourceUrl),
            client.Source.Producer.Key,
        ];
    }

    static async Task<int> ExecuteEligibleRouteAsync(
        TypeOptions options,
        IPackageStore store)
    {
        var authorization =
            new UniformPackageSourceAuthorization([Source]);
        ConfiguredPackageAuthority authority = Assert.Single(
            authorization.AuthorizeSourcesFor(PackageId).Authorities);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association,
                new FailingHandler());
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var house = new PackageHouse(
            authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store));
        return await TypeCommand.ExecuteAsync(
            options,
            ResolvedMemberInspectionPlan.FromCompatibilityOptions(options),
            house,
            cancellationToken =>
                root.IssueOperationLease(cancellationToken));
    }

    static async Task<int> ExecuteEligibleMemberRouteAsync(
        MemberOptions options,
        IPackageStore store)
    {
        var authorization =
            new UniformPackageSourceAuthorization([Source]);
        ConfiguredPackageAuthority authority = Assert.Single(
            authorization.AuthorizeSourcesFor(PackageId).Authorities);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association,
                new FailingHandler());
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var house = new PackageHouse(
            authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store));
        return await MemberCommand.ExecuteAsync(
            options,
            ResolvedMemberInspectionPlan.FromCompatibilityOptions(options),
            house,
            cancellationToken =>
                root.IssueOperationLease(cancellationToken));
    }

    static async Task<int> ExecuteEligibleLibraryNamespaceRouteAsync(
        LibraryOptions options,
        IPackageStore store)
    {
        int? result =
            await TryExecuteEligibleLibraryNamespaceRouteAsync(
                options,
                store);
        return Assert.IsType<int>(result);
    }

    static async Task<int?> TryExecuteEligibleLibraryNamespaceRouteAsync(
        LibraryOptions options,
        IPackageStore store)
    {
        Assert.True(
            LibraryCommand.TryCreatePackageNamespaceRequest(
                options,
                out PackageEndpointScopeRequest? request));
        var authorization =
            new UniformPackageSourceAuthorization([Source]);
        ConfiguredPackageAuthority authority = Assert.Single(
            authorization.AuthorizeSourcesFor(PackageId).Authorities);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association,
                new FailingHandler());
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var house = new PackageHouse(
            authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store));
        int? result =
            await LibraryCommand.TryExecutePackageNamespaceAsync(
                options,
                request,
                house,
                cancellationToken =>
                    root.IssueOperationLease(cancellationToken),
                TestContext.Current.CancellationToken);
        return result;
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
        byte[] assembly,
        string sourceUrl = SourceUrl)
    {
        byte[] package = Archive(
            ($"lib/{Framework}/{packageId}.dll", assembly));
        await store.CommitAsync(
            packageId,
            Version,
            NuGetCache.GetSourceKey(sourceUrl),
            new MemoryStream(package),
            TestContext.Current.CancellationToken);
    }

    static string EncodePacket(
        int format,
        (string Package, string Version, string Framework)[] tabs,
        int[][] contexts,
        int focusedTab,
        int selectedContext,
        bool registerPackagePrefix = true)
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
                + "],\"r\":"
                + (registerPackagePrefix
                    ? "[[\"p\",\"ILInspector.\"]]"
                    : "[]")
                + ",\"a\":"
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
        string typeName,
        TypeAttributes visibility = TypeAttributes.Public)
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
            visibility,
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

    static byte[] BuildHierarchyTypeAssembly()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Hierarchy"),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule("Hierarchy");
        TypeBuilder contract = module.DefineType(
            "Exact.Hierarchy.IContract",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        Type contractType = contract.CreateType();
        TypeBuilder implementation = module.DefineType(
            "Exact.Hierarchy.Implementation",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract);
        implementation.AddInterfaceImplementation(contractType);
        implementation.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    static (byte[] Contract, byte[] Implementation)
        BuildHierarchyPackageAssemblies()
    {
        var contractAssembly = new PersistedAssemblyBuilder(
            new AssemblyName("Hierarchy.Contract"),
            typeof(object).Assembly);
        ModuleBuilder contractModule =
            contractAssembly.DefineDynamicModule("Hierarchy.Contract");
        TypeBuilder contract = contractModule.DefineType(
            "Exact.Hierarchy.IContract",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        Type contractType = contract.CreateType();
        using var contractStream = new MemoryStream();
        contractAssembly.Save(contractStream);

        var implementationAssembly = new PersistedAssemblyBuilder(
            new AssemblyName("Hierarchy.Implementation"),
            typeof(object).Assembly);
        ModuleBuilder implementationModule =
            implementationAssembly.DefineDynamicModule(
                "Hierarchy.Implementation");
        TypeBuilder implementation = implementationModule.DefineType(
            "Exact.Hierarchy.Implementation",
            TypeAttributes.Public
                | TypeAttributes.Class
                | TypeAttributes.Abstract);
        implementation.AddInterfaceImplementation(contractType);
        implementation.CreateType();
        using var implementationStream = new MemoryStream();
        implementationAssembly.Save(implementationStream);
        return (
            contractStream.ToArray(),
            implementationStream.ToArray());
    }

    static PackageSourceResultFactory CreateResultFactory()
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateCustom(
                PackageSourceDescriptor.NuGetGallery,
                PackageSourceAssociation.Create(),
                factory =>
                {
                    captured = factory;
                    return new FactoryOnlyPackageSourceClient(
                        factory.Source);
                });
        return Assert.IsType<PackageSourceResultFactory>(captured);
    }

    sealed class PrefixPackageSource(
        IReadOnlyList<SearchResult> matches) : IPackageSourceClient
    {
        readonly PackageSourceResultFactory _results =
            CreateResultFactory();

        public PackageSourceResultIdentity Source => _results.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.Search;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _results.SucceededSearch(
                    _results.Search(
                        matches,
                        PackageSearchTruncationReason.None)));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    sealed class FactoryOnlyPackageSourceClient(
        PackageSourceResultIdentity source) : IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchAsync(
                string query,
                int take = 20,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>>
            GetVersionsAsync(
                string packageId,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            GetPackageAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>>
            TryGetSymbolsAsync(
                string packageId,
                string version,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
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

    sealed class IsolatedOfflineCache : IDisposable
    {
        readonly string _root = Path.Combine(
            Directory.GetCurrentDirectory(),
            "artifacts",
            $"exact-type-legacy-cache-{Guid.NewGuid():N}");

        public IsolatedOfflineCache()
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions
                {
                    Offline = true,
                });
            DotnetInspector.Networking.HttpClientFactory
                .ResetSharedForTesting();
            NuGetCache.Initialize(
                "dotnet-inspect-test",
                _root,
                skipNuGetCache: true);
        }

        public void Dispose()
        {
            DotnetInspector.Networking.HttpClientFactory.Initialize(
                new DotnetInspector.Networking.HttpClientFactoryOptions());
            DotnetInspector.Networking.HttpClientFactory
                .ResetSharedForTesting();
            NuGetCache.Initialize("dotnet-inspect-test");
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(
                    System.Net.HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
    }
}
