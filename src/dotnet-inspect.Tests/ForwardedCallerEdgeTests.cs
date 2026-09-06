using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml;
using DotnetInspector.Inspectors;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

/// <summary>
/// End-to-end gate for callers compiled through a framework type-forwarding
/// facade.
/// </summary>
public class ForwardedCallerEdgeTests
{
    static string FrameworkDirectory =>
        Path.GetDirectoryName(typeof(object).Assembly.Location)!;

    static string SelfPath =>
        typeof(ForwardedCallerEdgeTests).Assembly.Location;

    internal static bool ReadThroughFacade(string path)
    {
        using XmlReader reader = XmlReader.Create(path);
        return reader.Read();
    }

    internal static XmlReader WrapThroughFacade(XmlReader reader) =>
        XmlReader.Create(reader, new XmlReaderSettings());

    static string? PrivateXmlPath()
    {
        string path = Path.Combine(
            FrameworkDirectory,
            "System.Private.Xml.dll");
        return File.Exists(path) ? path : null;
    }

    static string? DataContractSerializationPath()
    {
        string path = Path.Combine(
            FrameworkDirectory,
            "System.Private.DataContractSerialization.dll");
        return File.Exists(path) ? path : null;
    }

    static int CreateToken(
        string targetPath,
        params string[] parameterTypes) =>
        ILInspector.Analysis.LibraryBodyIndex.Open(targetPath).Methods
            .First(method =>
                method.DeclaringType.Name == "XmlReader"
                && method.Name == "Create"
                && method.ParameterTypes
                    .Select(parameter => parameter.Name)
                    .SequenceEqual(parameterTypes))
            .MetadataToken;

    static ApiMemberAnalysisInspection CreateForCallers(
        string assemblyPath) =>
        new(
            assemblyPath,
            [],
            new HashSet<string> { SectionNames.Callers },
            [SelfPath],
            options: null);

    [Fact]
    public void FixtureNamesXmlReaderThroughANonDefiningFacade()
    {
        Assert.SkipWhen(
            PrivateXmlPath() is null,
            "System.Private.Xml not in the runtime directory.");

        using var stream = File.OpenRead(SelfPath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        string? scope = null;
        foreach (TypeReferenceHandle handle in reader.TypeReferences)
        {
            TypeReference type = reader.GetTypeReference(handle);
            if (reader.GetString(type.Name) != "XmlReader"
                || reader.GetString(type.Namespace) != "System.Xml"
                || type.ResolutionScope.Kind
                    != HandleKind.AssemblyReference)
            {
                continue;
            }

            scope = reader.GetString(
                reader.GetAssemblyReference(
                    (AssemblyReferenceHandle)type.ResolutionScope).Name);
            break;
        }

        Assert.NotNull(scope);
        Assert.NotEqual("System.Private.Xml", scope);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerEdges_ReportCallerCompiledThroughFacade()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(
            target is null,
            "System.Private.Xml not in the runtime directory.");

        ImmutableArray<CallerEdge> edges =
            CreateForCallers(target!).CallerEdges(
                CreateToken(target!, "String"));

        Assert.Contains(
            edges,
            edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerEdges_StillDiscriminateFacadeOverloads()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(
            target is null,
            "System.Private.Xml not in the runtime directory.");

        ImmutableArray<CallerEdge> edges =
            CreateForCallers(target!).CallerEdges(
                CreateToken(target!, "Stream"));

        Assert.DoesNotContain(
            edges,
            edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerEdges_ResolveForwardedParameterTypes()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(
            target is null,
            "System.Private.Xml not in the runtime directory.");

        ImmutableArray<CallerEdge> edges =
            CreateForCallers(target!).CallerEdges(
                CreateToken(
                    target!,
                    "XmlReader",
                    "XmlReaderSettings"));

        Assert.Contains(
            edges,
            edge => edge.Call.Caller.Name == nameof(WrapThroughFacade));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerEdges_ForwardedParameterTypesRejectCloseOverload()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(
            target is null,
            "System.Private.Xml not in the runtime directory.");

        ImmutableArray<CallerEdge> edges =
            CreateForCallers(target!).CallerEdges(
                CreateToken(
                    target!,
                    "Stream",
                    "XmlReaderSettings"));

        Assert.DoesNotContain(
            edges,
            edge => edge.Call.Caller.Name == nameof(WrapThroughFacade));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CallerEdges_ReportPlatformFacadeCaller()
    {
        string? target = PrivateXmlPath();
        string? caller = DataContractSerializationPath();
        Assert.SkipWhen(
            target is null || caller is null,
            "Required XML framework assemblies are not in the runtime directory.");
        var inspection = new ApiMemberAnalysisInspection(
            target!,
            [],
            new HashSet<string> { SectionNames.Callers },
            [caller!],
            options: null);

        ImmutableArray<CallerEdge> edges = inspection.CallerEdges(
            CreateToken(target!, "TextReader"));

        Assert.Contains(
            edges,
            edge => edge.Source
                    == "System.Private.DataContractSerialization"
                && edge.Call.Caller.Name == "ExportSurrogateData");
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerEdges_AreIndependentOfGraphScopeOrder(
        bool includeAllocations)
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(
            target is null,
            "System.Private.Xml not in the runtime directory.");
        ApiMemberAnalysisInspection inspection =
            CreateForCallers(target!);

        inspection.CallerScopes(includeAllocations);
        ImmutableArray<CallerEdge> edges = inspection.CallerEdges(
            CreateToken(target!, "String"));

        Assert.Contains(
            edges,
            edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));
    }
}
