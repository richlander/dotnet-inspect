using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.Cache;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
using CoreHttpClientFactory =
    DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class WorkspaceImplementationComparisonRunnerTests
    : IDisposable
{
    const string Framework = "net10.0";
    const string Facade = "Forwarded.Diff.Facade";
    const string Terminal = "Forwarded.Diff.Terminal";
    const string Feed =
        "https://forwarded-diff.invalid/v3/index.json";
    static readonly MetadataTypeDefinitionName TypeName =
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "N",
                ["Type"])).Name;
    readonly string _root = Directory.CreateTempSubdirectory(
        "forwarded-diff-runner-").FullName;

    public WorkspaceImplementationComparisonRunnerTests()
    {
        PersistentCache.Initialize(
            "dotnet-inspect-test");
        CoreHttpClientFactory.Initialize(
            new HttpClientFactoryOptions());
        CoreHttpClientFactory.ResetSharedForTesting();
        CoreHttpClientFactory.SetAuthenticationDecorator(
            null);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            null);
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            Path.Combine(
                _root,
                "cache"),
            skipNuGetCache: true);
    }

    [Fact]
    public async Task ForwardedPackageTargets_RetainRouteAndExactBodies()
    {
        TestSide before = CreateForwardedSide(
            "1.0.0",
            methodResult: 1);
        TestSide after = CreateForwardedSide(
            "2.0.0",
            methodResult: 2);
        var messages = new List<string>();

        WorkspaceImplementationComparisonResult result =
            await WorkspaceImplementationComparisonRunner.ExecuteAsync(
                before.AssemblySet,
                after.AssemblySet,
                Facade,
                TypeName,
                MemberTargetSelector.Parse("Value"),
                new HttpClient(),
                new NuGetSourceOptions
                {
                    Sources = [_root],
                },
                log: messages.Add,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(
            result
                is WorkspaceImplementationComparisonResult
                    .Published,
            string.Join(
                Environment.NewLine,
                messages));
        var published =
            Assert.IsType<
                WorkspaceImplementationComparisonResult.Published>(
                result).Publication;
        Assert.Equal(2, published.Forwarders.Length);
        Assert.Equal(
            new Version(1, 0, 0, 0),
            TerminalIdentity(published.Before).Version);
        Assert.Equal(
            new Version(2, 0, 0, 0),
            TerminalIdentity(published.After).Version);
        var view =
            DiffOutputFormatter.BuildWorkspaceImplementationDiffView(
                Facade,
                "N.Type.Value",
                result,
                "1.0.0",
                "2.0.0");
        List<ImplementationDiffRow> rows =
            Assert.IsType<List<ImplementationDiffRow>>(
                view.Rows);
        Assert.False(DiffOutputFormatter.IsIncomplete(result));
        Assert.Equal(
            2,
            rows.Count(row =>
                row.Mechanism == "Type Forwarder"));
        Assert.Equal(
            2,
            rows.Count(row =>
                row.Mechanism == "Effective Target"));
        Assert.Contains(
            rows,
            row => row.Mechanism == "C#"
                && row.Evidence.Contains(
                    "1",
                    StringComparison.Ordinal));
        Assert.Contains(
            rows,
            row => row.Mechanism == "IL"
                && row.Change == "changed");
    }

    [Fact]
    public async Task LongFormDependencyFramework_MatchesSelectedShortTfm()
    {
        TestSide before = CreateForwardedSide(
            "1.0.0",
            methodResult: 1,
            targetFramework: "netstandard2.0",
            dependencyGroupFramework:
                ".NETStandard2.0");
        TestSide after = CreateForwardedSide(
            "2.0.0",
            methodResult: 2,
            targetFramework: "netstandard2.0",
            dependencyGroupFramework:
                ".NETStandard2.0");

        WorkspaceImplementationComparisonResult result =
            await WorkspaceImplementationComparisonRunner.ExecuteAsync(
                before.AssemblySet,
                after.AssemblySet,
                Facade,
                TypeName,
                MemberTargetSelector.Parse("Value"),
                new HttpClient(),
                new NuGetSourceOptions
                {
                    Sources = [_root],
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var published =
            Assert.IsType<
                WorkspaceImplementationComparisonResult.Published>(
                result).Publication;
        Assert.Equal(
            new Version(1, 0, 0, 0),
            TerminalIdentity(published.Before).Version);
        Assert.Equal(
            new Version(2, 0, 0, 0),
            TerminalIdentity(published.After).Version);
    }

    [Fact]
    public async Task PreResolvedTerminal_DoesNotOverrideDeclaredPackageCoordinate()
    {
        Guid declaredMvid =
            new("00000000-0000-0000-0000-000000000501");
        Guid promotedMvid =
            new("00000000-0000-0000-0000-000000000502");
        TestSide before = CreateForwardedSide(
            "1.0.0",
            methodResult: 1,
            terminalMvid: declaredMvid);
        TestSide after = CreateForwardedSide(
            "3.0.0",
            methodResult: 1,
            writeTerminalPackage: false,
            dependencyVersion: "1.0.0",
            terminalAssemblyVersion: "1.0.0",
            terminalMvid: declaredMvid);
        AssemblySetEntry afterRoot =
            Assert.Single(after.AssemblySet.Assemblies);
        File.WriteAllBytes(
            Path.Combine(
                Path.GetDirectoryName(
                    afterRoot.Path)!,
                Terminal + ".dll"),
            BuildAssembly(
                Terminal,
                "1.0.0",
                methodResult: 2,
                moduleVersionId: promotedMvid));
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [_root],
        };

        string afterPackageDirectory =
            Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(
                        afterRoot.Path)!,
                    "..",
                    ".."));
        using (var initialResolution =
            new TypeDefinitionResolutionSession(
                afterRoot.Path,
                isPlatformAssembly: false,
                projectAssetsPath: null,
                targetFramework: afterRoot.Tfm,
                packageDirectory:
                    afterPackageDirectory,
                sourceOptions: sourceOptions,
                usePackageSourcePolicy: true,
                allowPlatformAssemblyVersionRollForward:
                    false))
        {
            var promoted =
                Assert.IsType<
                    TypeResolutionOutcome.Resolved>(
                    initialResolution.Resolve(
                        TypeName));
            Assert.Equal(
                promotedMvid,
                promoted.Definition.Address
                    .ModuleVersionId);
        }

        WorkspaceImplementationComparisonResult result =
            await WorkspaceImplementationComparisonRunner.ExecuteAsync(
                before.AssemblySet,
                after.AssemblySet,
                Facade,
                TypeName,
                MemberTargetSelector.Parse("Value"),
                new HttpClient(),
                sourceOptions,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var publication =
            Assert.IsType<
                WorkspaceImplementationComparisonResult.Published>(
                result).Publication;
        Assert.Equal(
            declaredMvid,
            publication.Before.EffectiveAttempt
                .Address!.Value.ModuleVersionId);
        Assert.Equal(
            declaredMvid,
            publication.After.EffectiveAttempt
                .Address!.Value.ModuleVersionId);
    }

    [Fact]
    public async Task DirectPackageTargets_DoNotInventForwarderRows()
    {
        TestSide before = CreateDirectSide(
            "1.0.0",
            methodResult: 1);
        TestSide after = CreateDirectSide(
            "2.0.0",
            methodResult: 2);

        WorkspaceImplementationComparisonResult result =
            await WorkspaceImplementationComparisonRunner.ExecuteAsync(
                before.AssemblySet,
                after.AssemblySet,
                Facade,
                TypeName,
                MemberTargetSelector.Parse("Value"),
                new HttpClient(),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var published =
            Assert.IsType<
                WorkspaceImplementationComparisonResult.Published>(
                result).Publication;
        Assert.Empty(published.Forwarders);
        var view =
            DiffOutputFormatter.BuildWorkspaceImplementationDiffView(
                Facade,
                "N.Type.Value",
                result,
                "1.0.0",
                "2.0.0");
        List<ImplementationDiffRow> rows =
            Assert.IsType<List<ImplementationDiffRow>>(
                view.Rows);
        Assert.DoesNotContain(
            rows,
            row => row.Mechanism == "Type Forwarder");
        Assert.Contains(
            rows,
            row => row.Mechanism == "C#");
        Assert.Contains(
            rows,
            row => row.Mechanism == "IL");
    }

    [Fact]
    public async Task MissingTerminal_RemainsVisibleTypedFailure()
    {
        TestSide before = CreateForwardedSide(
            "1.0.0",
            methodResult: 1,
            writeTerminalPackage: false);
        TestSide after = CreateForwardedSide(
            "2.0.0",
            methodResult: 2);

        WorkspaceImplementationComparisonResult result =
            await WorkspaceImplementationComparisonRunner.ExecuteAsync(
                before.AssemblySet,
                after.AssemblySet,
                Facade,
                TypeName,
                MemberTargetSelector.Parse("Value"),
                new HttpClient(),
                new NuGetSourceOptions
                {
                    Sources = [_root],
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<
                WorkspaceImplementationComparisonResult
                    .CompositionUnavailable>(result);
        Assert.Equal(
            QueryComparisonSide.Before,
            unavailable.Side);
        Assert.True(
            DiffOutputFormatter.IsIncomplete(result));
        var view =
            DiffOutputFormatter.BuildWorkspaceImplementationDiffView(
                Facade,
                "N.Type.Value",
                result,
                "1.0.0",
                "2.0.0");
        Assert.Contains(
            view.Rows!,
            row => row.Mechanism == "Query"
                && row.Change
                    == unavailable.Result.Reason.ToString());
    }

    [Fact]
    public async Task DivergentEffectiveDomains_RemainVisibleTypedFailure()
    {
        TestSide before = CreateForwardedSide(
            "1.0.0",
            methodResult: 1);
        TestSide after = CreateForwardedSide(
            "2.0.0",
            methodResult: 2,
            terminalName:
                "Forwarded.Diff.OtherTerminal");

        WorkspaceImplementationComparisonResult result =
            await WorkspaceImplementationComparisonRunner.ExecuteAsync(
                before.AssemblySet,
                after.AssemblySet,
                Facade,
                TypeName,
                MemberTargetSelector.Parse("Value"),
                new HttpClient(),
                new NuGetSourceOptions
                {
                    Sources = [_root],
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<
                WorkspaceImplementationComparisonResult
                    .HandoffFailed>(result);
        Assert.Equal(
            WorkspaceImplementationComparisonHandoffFailureKind
                .Unavailable,
            failed.Kind);
        Assert.True(
            DiffOutputFormatter.IsIncomplete(result));
        var view =
            DiffOutputFormatter.BuildWorkspaceImplementationDiffView(
                Facade,
                "N.Type.Value",
                result,
                "1.0.0",
                "2.0.0");
        Assert.Contains(
            view.Rows!,
            row => row.Mechanism == "Query"
                && row.Change
                    == failed.Kind.ToString());
    }

    [Fact]
    public async Task PackageCommand_ShortForwardedTypeAndQualifiedMemberUseWorkspaceRoute()
    {
        _ = CreateForwardedSide(
            "1.0.0",
            methodResult: 1);
        _ = CreateForwardedSide(
            "2.0.0",
            methodResult: 2);

        var (exitCode, output, error) =
            await RunPackageCommandAsync(
                type: "Type",
                member: "Type.Value",
                sections:
                    "Implementation Diff");

        Assert.True(
            exitCode == 0,
            error);
        Assert.True(
            string.IsNullOrEmpty(error),
            error);
        Assert.Contains(
            "N.Type.Value",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"mechanism\": \"Type Forwarder\"",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"mechanism\": \"IL\"",
            output,
            StringComparison.Ordinal);

        (exitCode, output, error) =
            await RunPackageCommandAsync(
                type: "N.Type",
                member: "Type.Value",
                sections:
                    "Implementation Diff");

        Assert.True(
            exitCode == 0,
            error);
        Assert.True(
            string.IsNullOrEmpty(error),
            error);
        Assert.Contains(
            "N.Type.Value",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"mechanism\": \"IL\"",
            output,
            StringComparison.Ordinal);

    }

    [Fact]
    public async Task PackageCommand_ComposedFailureRetainsWorkspaceQueryRow()
    {
        _ = CreateForwardedSide(
            "1.0.0",
            methodResult: 1);
        _ = CreateForwardedSide(
            "2.0.0",
            methodResult: 2);

        var (exitCode, output, error) =
            await RunPackageCommandAsync(
                type: "N.Type",
                member: "Nonexistent",
                sections:
                    "Changes;Implementation Diff");

        Assert.Equal(
            1,
            exitCode);
        Assert.True(
            string.IsNullOrEmpty(error),
            error);
        Assert.Contains(
            "Changes selection is incomplete",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"mechanism\": \"Query\"",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "TerminalAttemptUnavailable",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageCommand_ComposedAnalysisFailureRetainsWorkspaceRows()
    {
        _ = CreateForwardedSide(
            "1.0.0",
            methodResult: 1);
        _ = CreateForwardedSide(
            "2.0.0",
            methodResult: 2);

        var (exitCode, output, error) =
            await RunPackageCommandAsync(
                type: "Type",
                member: "Value",
                sections:
                    "Analysis Diff;Implementation Diff");

        Assert.Equal(
            1,
            exitCode);
        Assert.True(
            string.IsNullOrEmpty(error),
            error);
        Assert.Contains(
            "Analysis selection is incomplete",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"mechanism\": \"Type Forwarder\"",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"mechanism\": \"IL\"",
            output,
            StringComparison.Ordinal);

        (exitCode, output, error) =
            await RunPackageCommandAsync(
                type: "Type",
                member: "Value",
                sections:
                    "Analysis Diff;Implementation Diff",
                json: false);

        Assert.Equal(
            1,
            exitCode);
        Assert.True(
            string.IsNullOrEmpty(error),
            error);
        Assert.Contains(
            "Analysis selection is incomplete",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "Type Forwarder",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "| IL |",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageCommand_DirectNestedTypeRetainsStructuredIdentity()
    {
        _ = CreateDirectSide(
            "1.0.0",
            methodResult: 1,
            nested: true,
            writePackage: true);
        _ = CreateDirectSide(
            "2.0.0",
            methodResult: 2,
            nested: true,
            writePackage: true);

        var (exitCode, output, error) =
            await RunPackageCommandAsync(
                type: "N.Outer+Type",
                member: "Type.Value",
                sections:
                    "Implementation Diff");

        Assert.True(
            exitCode == 0,
            error);
        Assert.True(
            string.IsNullOrEmpty(error),
            error);
        Assert.Contains(
            "\"mechanism\": \"IL\"",
            output,
            StringComparison.Ordinal);

        (exitCode, output, error) =
            await RunPackageCommandAsync(
                type: "N.Outer+Type",
                member: "Type.Value",
                sections:
                    "Implementation Diff",
                json: false,
                allocRegressions: true,
                table: true);

        Assert.Equal(
            0,
            exitCode);
        Assert.True(
            string.IsNullOrEmpty(error),
            error);
        Assert.NotEmpty(output);
    }

    Task<(int ExitCode, string Output, string Error)>
        RunPackageCommandAsync(
        string type,
        string member,
        string sections,
        bool json = true,
        bool allocRegressions = false,
        bool table = false)
    {
        Dictionary<string, byte[]> packages =
            PackagePayloads();
        CoreHttpClientFactory.SetAuthenticationDecorator(
            _ => new FixtureFeedHandler(
                packages));
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            _ => new FixtureFeedHandler(
                packages));
        return ConsoleCapture.RunAsync(
            async () =>
            {
                var arguments = new List<string>
                {
                    "diff",
                    "--package",
                    $"{Facade}@1.0.0..2.0.0",
                    "--type",
                    type,
                    "--member",
                    member,
                    "-S",
                    sections,
                };
                if (json)
                    arguments.Add("--json");
                if (allocRegressions)
                    arguments.Add("--alloc-regressions");
                if (table)
                    arguments.Add("--table");
                arguments.AddRange(
                    [
                        "--source",
                        Feed,
                    ]);
                var parsed =
                    CommandLineBuilder.CreateRootCommand().Parse(
                        CommandLineBuilder.PreprocessArgs(
                            [.. arguments]));
                Assert.Empty(parsed.Errors);
                return await CommandLineBuilder.InvokeAsync(
                    parsed);
            });
    }

    Dictionary<string, byte[]> PackagePayloads()
    {
        var packages =
            new Dictionary<string, byte[]>(
                StringComparer.Ordinal);
        AddPackage(
            packages,
            Facade,
            "1.0.0");
        AddPackage(
            packages,
            Facade,
            "2.0.0");
        AddPackage(
            packages,
            Terminal,
            "1.0.0");
        AddPackage(
            packages,
            Terminal,
            "2.0.0");
        return packages;
    }

    void AddPackage(
        Dictionary<string, byte[]> packages,
        string id,
        string version)
    {
        string path = PackagePath(
            id,
            version);
        if (File.Exists(path))
        {
            packages.Add(
                PackageUrl(
                    id,
                    version),
                File.ReadAllBytes(path));
        }
    }

    static AssemblyReferenceIdentity TerminalIdentity(
        WorkspaceResearchTargetCompositionReceipt receipt)
        => Assert.IsType<
                WorkspaceMetadataEvidence.Outcome.Resolved>(
                receipt.Evidence.Outcome)
            .Definition.Assembly.Assembly.Identity;

    TestSide CreateForwardedSide(
        string version,
        int methodResult,
        bool writeTerminalPackage = true,
        string terminalName = Terminal,
        string targetFramework = Framework,
        string? dependencyGroupFramework = null,
        string? dependencyVersion = null,
        string? terminalAssemblyVersion = null,
        Guid? terminalMvid = null)
    {
        string selectedDependencyVersion =
            dependencyVersion
            ?? version;
        byte[] terminal = BuildAssembly(
            terminalName,
            terminalAssemblyVersion
                ?? selectedDependencyVersion,
            methodResult,
            moduleVersionId: terminalMvid);
        AssemblyReferenceIdentity terminalIdentity =
            ReadIdentity(terminal);
        byte[] facade = BuildAssembly(
            Facade,
            version,
            methodResult: null,
            terminalIdentity);
        string dependencies =
            $"<dependency id=\"{terminalName}\" version=\"{selectedDependencyVersion}\" />";
        if (writeTerminalPackage)
        {
            WritePackage(
                terminalName,
                selectedDependencyVersion,
                terminal,
                dependencies: null,
                targetFramework);
        }
        WritePackage(
            Facade,
            version,
            facade,
            dependencies,
            targetFramework,
            dependencyGroupFramework);
        return CreateSide(
            version,
            facade,
            dependencies,
            targetFramework,
            dependencyGroupFramework);
    }

    TestSide CreateDirectSide(
        string version,
        int methodResult,
        bool nested = false,
        bool writePackage = false)
    {
        byte[] assembly = BuildAssembly(
                Facade,
                version,
                methodResult,
                nested: nested);
        if (writePackage)
        {
            WritePackage(
                Facade,
                version,
                assembly,
                dependencies: null);
        }
        return CreateSide(
            version,
            assembly,
            dependencies: null);
    }

    TestSide CreateSide(
        string version,
        byte[] assembly,
        string? dependencies,
        string targetFramework = Framework,
        string? dependencyGroupFramework = null)
    {
        string directory = Path.Combine(
            _root,
            "root-" + version);
        string lib = Path.Combine(
            directory,
            "lib",
            targetFramework);
        Directory.CreateDirectory(lib);
        string assemblyPath = Path.Combine(
            lib,
            Facade + ".dll");
        File.WriteAllBytes(
            assemblyPath,
            assembly);
        File.WriteAllText(
            Path.Combine(
                directory,
                Facade + ".nuspec"),
            Nuspec(
                Facade,
                version,
                dependencies,
                dependencyGroupFramework
                    ?? targetFramework));
        return new(
            new AssemblySet(
                [
                    new(
                        assemblyPath,
                        Facade,
                        version,
                        AssemblySetSourceKind.Package,
                        targetFramework),
                ],
                [],
                []));
    }

    void WritePackage(
        string id,
        string version,
        byte[] assembly,
        string? dependencies,
        string targetFramework = Framework,
        string? dependencyGroupFramework = null)
    {
        string path = PackagePath(
            id,
            version);
        using var archive = ZipFile.Open(
            path,
            ZipArchiveMode.Create);
        Write(
            archive,
            $"{id}.nuspec",
            Nuspec(
                id,
                version,
                dependencies,
                dependencyGroupFramework
                    ?? targetFramework));
        ZipArchiveEntry library = archive.CreateEntry(
            $"lib/{targetFramework}/{id}.dll");
        using Stream stream = library.Open();
        stream.Write(assembly);
    }

    string PackagePath(
        string id,
        string version)
        => Path.Combine(
            _root,
            $"{id.ToLowerInvariant()}.{version}.nupkg");

    static string PackageUrl(
        string id,
        string version)
        => new Uri(
            new Uri(
                Feed),
            $"flat2/{id.ToLowerInvariant()}/{version}/"
                + $"{id.ToLowerInvariant()}.{version}.nupkg")
            .AbsoluteUri;

    static void Write(
        ZipArchive archive,
        string path,
        string content)
    {
        using var writer = new StreamWriter(
            archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    static string Nuspec(
        string id,
        string version,
        string? dependencies,
        string targetFramework)
        => $"""
            <?xml version="1.0"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>dotnet-inspect tests</authors>
                <description>Forwarded diff fixture.</description>
                {(dependencies is null ? "" : $"<dependencies><group targetFramework=\"{targetFramework}\">{dependencies}</group></dependencies>")}
              </metadata>
            </package>
            """;

    static AssemblyReferenceIdentity ReadIdentity(
        byte[] image)
    {
        using var pe = new PEReader(
            new MemoryStream(image));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    static byte[] BuildAssembly(
        string name,
        string version,
        int? methodResult,
        AssemblyReferenceIdentity? forwardsTo = null,
        bool nested = false,
        Guid? moduleVersionId = null)
    {
        var metadata = new MetadataBuilder();
        var bodyStream = new BlobBuilder();
        Version packageVersion = Version.Parse(version);
        var parsedVersion = new Version(
            packageVersion.Major,
            packageVersion.Minor,
            Math.Max(
                0,
                packageVersion.Build),
            Math.Max(
                0,
                packageVersion.Revision));
        Guid mvid =
            moduleVersionId
            ?? new Guid(
                parsedVersion.Major,
                (short)parsedVersion.Minor,
                (short)parsedVersion.Build,
                [0, 0, 0, 0, 0, 0, 0, 1]);
        metadata.AddModule(
            0,
            metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(mvid),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            parsedVersion,
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        if (methodResult is { } result)
        {
            if (nested)
            {
                TypeDefinitionHandle outer =
                    metadata.AddTypeDefinition(
                        TypeAttributes.Public,
                        metadata.GetOrAddString("N"),
                        metadata.GetOrAddString("Outer"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(1));
                TypeDefinitionHandle inner =
                    metadata.AddTypeDefinition(
                        TypeAttributes.NestedPublic,
                        default,
                        metadata.GetOrAddString("Type"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(1));
                metadata.AddNestedType(
                    inner,
                    outer);
            }
            else
            {
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Type"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            }
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature()
                .Parameters(
                    0,
                    returnType => returnType.Type().Int32(),
                    parameters => { });
            var instructions = new BlobBuilder();
            var encoder = new InstructionEncoder(
                instructions);
            encoder.LoadConstantI4(result);
            encoder.OpCode(ILOpCode.Ret);
            int bodyOffset =
                new MethodBodyStreamEncoder(bodyStream)
                    .AddMethodBody(encoder);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(signature),
                bodyOffset,
                MetadataTokens.ParameterHandle(1));
        }
        else if (forwardsTo is not null)
        {
            AssemblyReferenceHandle target =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString(
                        forwardsTo!.Name),
                    forwardsTo.Version!,
                    default,
                    default,
                    default,
                    default);
            metadata.AddExportedType(
                TypeAttributes.Public
                    | (TypeAttributes)0x00200000,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                target,
                0);
        }

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodyStream,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    public void Dispose()
    {
        CoreHttpClientFactory.SetAuthenticationDecorator(
            null);
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(
            null);
        CoreHttpClientFactory.Initialize(
            new HttpClientFactoryOptions());
        CoreHttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize(
            "dotnet-inspect");
        Directory.Delete(
            _root,
            recursive: true);
    }

    sealed record TestSide(AssemblySet AssemblySet);

    sealed class FixtureFeedHandler(
        IReadOnlyDictionary<string, byte[]> packages)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            HttpResponseMessage response;
            if (url == Feed)
            {
                string flat = new Uri(
                    new Uri(Feed),
                    "flat2/").AbsoluteUri;
                response = new(
                    HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {"version":"3.0.0","resources":[
                          {"@id":"{{flat}}","@type":"PackageBaseAddress/3.0.0"}
                        ]}
                        """),
                };
            }
            else if (packages.TryGetValue(
                url,
                out byte[]? package))
            {
                response = new(
                    HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(
                        package),
                };
            }
            else
            {
                response = new(
                    HttpStatusCode.NotFound);
            }

            response.RequestMessage = request;
            return Task.FromResult(
                response);
        }
    }
}
