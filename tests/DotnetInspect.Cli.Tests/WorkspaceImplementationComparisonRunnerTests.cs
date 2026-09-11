using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public sealed class WorkspaceImplementationComparisonRunnerTests
    : IDisposable
{
    const string Framework = "net10.0";
    const string Facade = "Forwarded.Diff.Facade";
    const string Terminal = "Forwarded.Diff.Terminal";
    static readonly MetadataTypeDefinitionName TypeName =
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "N",
                ["Type"])).Name;
    readonly string _root = Directory.CreateTempSubdirectory(
        "forwarded-diff-runner-").FullName;

    public WorkspaceImplementationComparisonRunnerTests()
        => CoreCache.Initialize(
            "dotnet-inspect-test");

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
        string? dependencyGroupFramework = null)
    {
        byte[] terminal = BuildAssembly(
            terminalName,
            version,
            methodResult);
        AssemblyReferenceIdentity terminalIdentity =
            ReadIdentity(terminal);
        byte[] facade = BuildAssembly(
            Facade,
            version,
            methodResult: null,
            terminalIdentity);
        if (writeTerminalPackage)
        {
            WritePackage(
                terminalName,
                version,
                terminal,
                dependencies: null,
                targetFramework);
        }
        return CreateSide(
            version,
            facade,
            dependencies:
                $"<dependency id=\"{terminalName}\" version=\"{version}\" />",
            targetFramework,
            dependencyGroupFramework);
    }

    TestSide CreateDirectSide(
        string version,
        int methodResult)
        => CreateSide(
            version,
            BuildAssembly(
                Facade,
                version,
                methodResult),
            dependencies: null);

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
        string targetFramework = Framework)
    {
        string path = Path.Combine(
            _root,
            $"{id}.{version}.nupkg");
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
                targetFramework));
        ZipArchiveEntry library = archive.CreateEntry(
            $"lib/{targetFramework}/{id}.dll");
        using Stream stream = library.Open();
        stream.Write(assembly);
    }

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
        AssemblyReferenceIdentity? forwardsTo = null)
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
        Guid mvid = new(
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
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
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
        else
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
        Directory.Delete(
            _root,
            recursive: true);
    }

    sealed record TestSide(AssemblySet AssemblySet);
}
