using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace InspectWeb.Engine.Tests;

/// <summary>
/// Pins the browser engine's layering rule: every interaction that inspects an assembly runs
/// inside a workspace, through a public product query that owns the session, the metadata source,
/// and the analysis index.
/// </summary>
/// <remarks>
/// <para>
/// The rule itself is not enforced here. It is enforced by the C# compiler, through
/// <c>Microsoft.CodeAnalysis.BannedApiAnalyzers</c> and
/// <c>prototypes/inspect-web/engine/BannedSymbols.txt</c>, which fails the engine build at the
/// offending line — a scan of the source text would have to model C# to know what a name binds to,
/// and every spelling it does not model is a hole. This class makes sure that enforcement is
/// switched on, and says the two things the analyzer cannot.
/// </para>
/// <para>
/// The first is that the ban list is not vacuous: a banned identifier that no longer resolves
/// bans nothing, and the analyzer reports no such entry. <see cref="EveryBannedSymbolStillExists"/>
/// resolves each entry against the product assemblies so a rename fails here rather than silently
/// reopening the door.
/// </para>
/// <para>
/// The second is that the engine retains the typed identity and descriptor currency needed to
/// mint participants, while raw <c>PEReader</c>/<c>MetadataReader</c> decoding stays isolated in
/// <c>InspectWeb.Acquisition</c>.
/// </para>
/// </remarks>
public sealed class BrowserEngineLayeringTests
{
    [Fact]
    public void EngineProjectDeclaresItsBanList()
    {
        XDocument project = XDocument.Load(EngineProjectPath);
        Assert.Contains(
            project.Descendants("AdditionalFiles"),
            item => item.Attribute("Include")?.Value == "BannedSymbols.txt");
        Assert.True(File.Exists(BanListPath), $"{BanListPath} is missing.");
    }

    [Fact]
    public void RuntimeAsyncDisableCoversBrowserProjectGraph()
    {
        XDocument project = XDocument.Load(EngineProjectPath);
        XDocument engineProps = XDocument.Load(EnginePropsPath);
        XDocument repositoryProps = XDocument.Load(RepositoryPropsPath);
        XDocument repositoryTargets = XDocument.Load(RepositoryTargetsPath);

        Assert.Equal(
            "off",
            Assert.Single(engineProps.Descendants("RuntimeAsync")).Value);
        Assert.True(
            engineProps.Root!.Elements().First().Name.LocalName == "PropertyGroup",
            "RuntimeAsync must be set before the repository props import.");

        XElement projectReferenceDefaults = Assert.Single(
            project.Descendants("ProjectReference"),
            item => item.Parent?.Name.LocalName == "ItemDefinitionGroup");
        Assert.Equal(
            "RuntimeAsync=off",
            Assert.Single(projectReferenceDefaults.Elements("AdditionalProperties")).Value);
        Assert.Equal(
            "Configuration=$(Configuration);RuntimeAsync=off",
            Assert.Single(projectReferenceDefaults.Elements("SetConfiguration")).Value);
        List<XElement> projectChildren = [.. project.Root!.Elements()];
        Assert.True(
            projectChildren.IndexOf(projectReferenceDefaults.Parent!)
                < projectChildren.FindIndex(
                    item => item.Descendants("ProjectReference")
                        .Any(reference => reference.Attribute("Include") is not null)),
            "ProjectReference defaults must be declared before every included reference.");

        XElement isolatedOutput = Assert.Single(
            repositoryProps.Descendants("PropertyGroup"),
            item => item.Attribute("Condition")?.Value == "'$(RuntimeAsync)' == 'off'");
        Assert.Contains(
            "artifacts/runtime-async-off/bin",
            Assert.Single(isolatedOutput.Elements("BaseOutputPath")).Value,
            StringComparison.Ordinal);
        Assert.Contains(
            "artifacts/runtime-async-off/obj",
            Assert.Single(isolatedOutput.Elements("IntermediateOutputPath")).Value,
            StringComparison.Ordinal);

        XElement disabledFeatures = Assert.Single(
            repositoryTargets.Descendants("PropertyGroup"),
            item => item.Attribute("Condition")?.Value ==
                "'$(TargetFramework)' == 'net11.0' and '$(RuntimeAsync)' == 'off'");
        Assert.Equal(
            "$(Features);runtime-async=off",
            Assert.Single(disabledFeatures.Elements("Features")).Value);

        Assert.True(File.Exists(RuntimeAsyncGatePath), $"{RuntimeAsyncGatePath} is missing.");
        foreach (string workflowPath in new[] { CiWorkflowPath, DeployWorkflowPath })
        {
            string workflow = File.ReadAllText(workflowPath);
            Assert.Contains(
                "eng/validate-inspect-web-runtime-async.cs",
                workflow,
                StringComparison.Ordinal);
            Assert.Contains(
                "artifacts/runtime-async-off/bin",
                workflow,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BanListForbidsEverySessionAndImageDoor()
    {
        IReadOnlyList<string> banned = BannedSymbols();

        Assert.Contains("T:ILInspector.Metadata.AssemblyInspectionSession", banned);
        Assert.Contains("T:ILInspector.Metadata.AssemblyImageSnapshot", banned);
        Assert.Contains("T:ILInspector.Metadata.AssemblyReader", banned);
        Assert.Contains("T:ILInspector.Metadata.ApiSurfaceExtractor", banned);
        Assert.Contains("P:ILInspector.Metadata.ResolvedAssemblyReference.OpenRead", banned);
        Assert.Contains("T:System.Reflection.PortableExecutable.PEReader", banned);
        Assert.Contains("T:System.Reflection.Metadata.MetadataReader", banned);
        Assert.Contains("T:ILInspector.Decompiler.Pipeline.MetadataSource", banned);
        Assert.Contains("T:ILInspector.Analysis.LibraryBodyIndex", banned);
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextGroup.UseAssemblyImage",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextGroup.GetAssemblyImageSpan",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextGroup.RetainAssemblyReference",
                StringComparison.Ordinal));

        // A package load is not an explicit request for unbounded work: the whole-group and
        // participant API-surface entry points are declared InspectionCost.Unbounded, so the
        // browser can reach only the bounded overload.
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextApiSurfaceQuery.Execute(",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextApiSurfaceQuery.ExecuteParticipant(",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextApiSurfaceQuery.ExecuteBounded",
                StringComparison.Ordinal));

        // #3932's streaming form releases the participant terminally, and this engine reuses one
        // workspace across exports, so a later whole-group query over the same group would find
        // the released participant unavailable.
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextIntegrationsQuery.ExecuteParticipantAsync",
                StringComparison.Ordinal));
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextIntegrationOpportunitiesQuery.ExecuteParticipantAsync",
                StringComparison.Ordinal));
    }

    [Fact]
    public void EveryBannedSymbolStillExists()
    {
        foreach (string symbol in BannedSymbols())
        {
            ISymbol? resolved = DocumentationCommentId.GetFirstSymbolForDeclarationId(
                symbol,
                ProductCompilation);
            Assert.True(
                resolved is not null,
                $"Banned symbol '{symbol}' no longer resolves exactly, so the entry bans nothing.");
        }
    }

    [Fact]
    public void AcquisitionCurrencyRemainsAvailableWithoutRawReaders()
    {
        IReadOnlyList<string> banned = BannedSymbols();

        // Acquisition decodes an entry's real metadata identity in the isolated acquisition
        // project before it mints a participant. The engine itself receives only that identity.
        Assert.DoesNotContain("T:ILInspector.Metadata.AssemblyReferenceIdentity", banned);
        Assert.DoesNotContain("T:ILInspector.Metadata.ResolvedAssemblyReference", banned);
    }

    static Type? Resolve(string fullName) => ProductAssemblies
        .Select(assembly => assembly.GetType(fullName, throwOnError: false))
        .OfType<Type>()
        .FirstOrDefault();

    static IReadOnlyList<Assembly> ProductAssemblies { get; } =
    [
        typeof(ILInspector.Metadata.AssemblyInspectionSession).Assembly,
        typeof(ILInspector.Decompiler.Pipeline.MetadataSource).Assembly,
        typeof(ILInspector.Analysis.LibraryBodyIndex).Assembly,
        typeof(DotnetInspector.Queries.AssemblyContextGroup).Assembly,
    ];

    static CSharpCompilation ProductCompilation { get; } = CSharpCompilation.Create(
        "BrowserEngineBannedSymbols",
        references:
        [
            .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Concat(ProductAssemblies.Select(assembly => assembly.Location))
                .Distinct(StringComparer.Ordinal)
                .Select(path => MetadataReference.CreateFromFile(path)),
        ]);

    static IReadOnlyList<string> BannedSymbols() =>
    [
        .. File.ReadAllLines(BanListPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith(';'))
            .Select(line => line.Split(';')[0].Trim()),
    ];

    static string EngineProjectPath => Path.Combine(
        RepositoryRoot(),
        "prototypes",
        "inspect-web",
        "engine",
        "InspectWeb.Engine.csproj");

    static string BanListPath => Path.Combine(
        Path.GetDirectoryName(EngineProjectPath)!,
        "BannedSymbols.txt");

    static string EnginePropsPath => Path.Combine(
        Path.GetDirectoryName(EngineProjectPath)!,
        "Directory.Build.props");

    static string RepositoryPropsPath => Path.Combine(
        RepositoryRoot(),
        "Directory.Build.props");

    static string RepositoryTargetsPath => Path.Combine(
        RepositoryRoot(),
        "Directory.Build.targets");

    static string RuntimeAsyncGatePath => Path.Combine(
        RepositoryRoot(),
        "eng",
        "validate-inspect-web-runtime-async.cs");

    static string CiWorkflowPath => Path.Combine(
        RepositoryRoot(),
        ".github",
        "workflows",
        "ci.yml");

    static string DeployWorkflowPath => Path.Combine(
        RepositoryRoot(),
        ".github",
        "workflows",
        "deploy-inspect-web.yml");

    static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }
}
