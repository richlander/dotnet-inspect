using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotnetInspector.Tests;

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
/// The second is that acquisition is deliberately still allowed. Minting a participant requires
/// decoding the entry's real metadata identity, because the workspace validates every image
/// against its descriptor, so <c>PEReader</c> and <c>MetadataReader</c> must stay reachable.
/// <see cref="AcquisitionOnlyMetadataDecodingIsNotBanned"/> pins that exception, so a later
/// tightening cannot quietly make participant minting impossible.
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
    public void BanListForbidsEverySessionAndImageDoor()
    {
        IReadOnlyList<string> banned = BannedSymbols();

        Assert.Contains("T:ILInspector.Metadata.AssemblyInspectionSession", banned);
        Assert.Contains("T:ILInspector.Metadata.AssemblyImageSnapshot", banned);
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

        // #3932's streaming form releases the participant terminally, and this engine reuses one
        // workspace across exports, so a later whole-group query over the same group would find
        // the released participant unavailable.
        Assert.Contains(
            banned,
            symbol => symbol.StartsWith(
                "M:DotnetInspector.Queries.AssemblyContextIntegrationsQuery.ExecuteParticipantAsync",
                StringComparison.Ordinal));
    }

    [Fact]
    public void EveryBannedSymbolStillExists()
    {
        foreach (string symbol in BannedSymbols())
        {
            string qualified = symbol[2..];
            if (symbol.StartsWith("T:", StringComparison.Ordinal))
            {
                Assert.True(
                    Resolve(qualified) is not null,
                    $"Banned type '{qualified}' no longer exists, so the entry bans nothing.");
                continue;
            }

            Assert.StartsWith("M:", symbol, StringComparison.Ordinal);
            string withoutParameters = qualified.Split('(')[0].Split("``")[0];
            int lastDot = withoutParameters.LastIndexOf('.');
            string typeName = withoutParameters[..lastDot];
            string memberName = withoutParameters[(lastDot + 1)..];
            Type type = Resolve(typeName)
                ?? throw new InvalidOperationException(
                    $"Banned member '{qualified}' names type '{typeName}', which no longer exists.");
            Assert.True(
                type.GetMember(
                        memberName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Length
                    > 0,
                $"Banned member '{qualified}' no longer exists, so the entry bans nothing.");
        }
    }

    [Fact]
    public void AcquisitionOnlyMetadataDecodingIsNotBanned()
    {
        IReadOnlyList<string> banned = BannedSymbols();

        // Acquisition decodes an entry's real metadata identity before it mints a participant,
        // because the workspace refuses a placeholder identity. Banning these would make a
        // content-shaped acquisition owner impossible rather than making it safer.
        Assert.DoesNotContain("T:System.Reflection.PortableExecutable.PEReader", banned);
        Assert.DoesNotContain("T:System.Reflection.Metadata.MetadataReader", banned);
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
