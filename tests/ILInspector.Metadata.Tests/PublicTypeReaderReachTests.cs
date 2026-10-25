using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Every Metadata reader that means "public Type" admits a Type only when it
/// and every enclosing Type is public or nested public, the Public scope's
/// Type admission (docs/design/api-population-scope.md, Accessibility within
/// API visibility scope). A public nested Type of an internal Type is not
/// admitted by its own row.
/// </summary>
public sealed class PublicTypeReaderReachTests
{
    const string Source = """
        namespace Microsoft.AspNetCore.Reach
        {
            public class Outer
            {
                public class PublicNestedOptions : System.IDisposable { public void Dispose() { } }
            }

            internal class Hidden
            {
                public class InternalNestedOptions : System.IDisposable { public void Dispose() { } }
            }
        }

        namespace OpenTelemetry.Reach
        {
            public class Outer { public class PublicNestedExporter { } }

            internal class Hidden { public class InternalNestedExporter { } }
        }

        namespace Azure.Reach
        {
            public class Outer { public class PublicNestedClient { } }

            internal class Hidden { public class InternalNestedClient { } }
        }

        namespace Reach.Extensions
        {
            public static class PublicExtensions { public static int Twice(this int value) => value * 2; }

            internal static class InternalExtensions { public static int Thrice(this int value) => value * 3; }
        }
        """;

    // Only public nested Types of internal Types carry the scanners' names.
    // Opportunities keep one row per concept, so the public row would mask
    // an unreachable one in the mixed image.
    const string UnreachableOnlySource = """
        namespace Microsoft.AspNetCore.Reach
        {
            internal class Hidden { public class InternalNestedOptions { } }
        }

        namespace OpenTelemetry.Reach
        {
            internal class Hidden { public class InternalNestedExporter { } }
        }

        namespace Azure.Reach
        {
            internal class Hidden { public class InternalNestedClient { } }
        }
        """;

    static readonly Lazy<byte[]> Image = new(() => Compile("PublicTypeReaderReach", Source));

    static readonly Lazy<byte[]> UnreachableOnlyImage =
        new(() => Compile("PublicTypeReaderUnreachable", UnreachableOnlySource));

    [Fact]
    public void CountPublicTypes_CountsTheTypesThePublicScopeAdmits()
    {
        using TemporaryImage image = new(Image.Value);

        int count = AssemblyReader.CountPublicTypes(image.Path);

        using var reader = new PEReader(new MemoryStream(Image.Value));
        ApiSurface surface = ApiSurfaceExtractor.Extract(reader, ApiSurfaceExtractionScope.Public);
        Assert.Equal(7, count);
        Assert.Equal(surface.Types.Count, count);
    }

    [Fact]
    public void FindUniquePublicType_DoesNotFindAPublicNestedTypeOfAnInternalType()
    {
        using TemporaryImage image = new(Image.Value);

        Assert.NotNull(AssemblyReader.FindUniquePublicType(image.Path, "PublicNestedClient"));
        Assert.Null(AssemblyReader.FindUniquePublicType(image.Path, "InternalNestedClient"));
    }

    [Fact]
    public void IntegrationSignals_AdmitOnlyTypesPublicThroughEveryEnclosingType()
    {
        using var reader = new PEReader(new MemoryStream(Image.Value));

        List<EcosystemIntegrationSignalInfo> signals = EcosystemIntegrationScanner.Scan(reader);

        Assert.Contains(signals, signal => signal.Name.Contains("PublicNestedOptions", StringComparison.Ordinal));
        Assert.DoesNotContain(signals, signal => signal.Name.Contains("InternalNested", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenTelemetrySignals_AdmitOnlyTypesPublicThroughEveryEnclosingType()
    {
        using var reader = new PEReader(new MemoryStream(Image.Value));

        List<OpenTelemetrySignalInfo> signals = OpenTelemetryScanner.Scan(reader);

        Assert.Contains(signals, signal => signal.Name.Contains("PublicNestedExporter", StringComparison.Ordinal));
        Assert.DoesNotContain(signals, signal => signal.Name.Contains("InternalNested", StringComparison.Ordinal));
    }

    [Fact]
    public void Presence_IsNotEstablishedByPublicNestedTypesOfInternalTypes()
    {
        using var reader = new PEReader(new MemoryStream(UnreachableOnlyImage.Value));

        EcosystemIntegrationPresence presence =
            EcosystemIntegrationScanner.ScanPresence(reader.GetMetadataReader());

        Assert.False(OpenTelemetryScanner.HasSupport(reader));
        Assert.False(presence.HasOpenTelemetrySupport);
        Assert.False(presence.HasAspNetCoreSupport);
    }

    [Fact]
    public void IntegrationOpportunities_AdmitOnlyTypesPublicThroughEveryEnclosingType()
    {
        using var reader = new PEReader(new MemoryStream(Image.Value));
        using var unreachable = new PEReader(new MemoryStream(UnreachableOnlyImage.Value));

        List<IntegrationOpportunityInfo> opportunities =
            IntegrationOpportunityScanner.Scan(reader, new HashSet<string>());

        Assert.Contains(opportunities, opportunity => opportunity.Api.Contains("PublicNestedClient", StringComparison.Ordinal));
        Assert.Empty(IntegrationOpportunityScanner.Scan(unreachable, new HashSet<string>()));
    }

    [Fact]
    public void Implementers_AdmitOnlyTypesPublicThroughEveryEnclosingType()
    {
        using var reader = new PEReader(new MemoryStream(Image.Value));

        TypeRelationship implementer = Assert.Single(
            TypeHierarchyScanner.FindImplementers(reader, "System.IDisposable"));

        Assert.Equal("PublicNestedOptions", implementer.TypeName);
    }

    [Fact]
    public void DependencyIndex_DoesNotResolveAPublicNestedTypeOfAnInternalType()
    {
        using TemporaryImage image = new(Image.Value);

        Assert.True(
            TypeDependencyScanner.BuildDependencyTree(
                "Microsoft.AspNetCore.Reach.Outer.PublicNestedOptions",
                [image.Path]).Found);
        Assert.False(
            TypeDependencyScanner.BuildDependencyTree(
                "Microsoft.AspNetCore.Reach.Hidden.InternalNestedOptions",
                [image.Path]).Found);
    }

    [Fact]
    public void Extensions_RequireADeclaringTypePublicThroughEveryEnclosingType()
    {
        using var reader = new PEReader(new MemoryStream(Image.Value));

        Assert.Equal(
            ["Twice"],
            ExtensionMethodScanner.FindAllExtensions(reader).Select(static extension => extension.MethodName));
        Assert.Equal(
            ["Twice"],
            ExtensionMethodScanner.FindExtensions(reader, "System.Int32").Select(static extension => extension.MethodName));
        Assert.Equal(
            ["Thrice", "Twice"],
            ExtensionMethodScanner.FindAllExtensions(reader, includeAll: true)
                .Select(static extension => extension.MethodName)
                .Order(StringComparer.Ordinal));
    }

    static byte[] Compile(string name, string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    sealed class TemporaryImage : IDisposable
    {
        public TemporaryImage(byte[] image)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"public-type-reader-reach-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(Path, image);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
