using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The Public scope admits a Type only when it and every enclosing Type is
/// public or nested public (docs/design/api-population-scope.md, Accessibility
/// within API visibility scope). The compact counts admit the same Types.
/// </summary>
public sealed class ApiSurfacePublicScopeReachTests
{
    const string Source = """
        namespace Reach;

        public class PublicOuter
        {
            public class PublicNested { public void Value() { } }

            protected class ProtectedNested
            {
                public class PublicInProtected { public void Value() { } }
            }
        }

        internal class InternalOuter
        {
            public class PublicInInternal
            {
                public void Value() { }
                internal void Hidden() { }
            }
        }
        """;

    static readonly string[] Ordinary = ["PublicOuter", "PublicOuter.PublicNested"];

    [Fact]
    public void PublicScope_AdmitsOnlyTypesPublicThroughEveryEnclosingType()
    {
        ApiSurface surface = Extract(ApiSurfaceExtractionScope.Public);

        Assert.Equal(Ordinary, Names(surface));
    }

    [Fact]
    public void Summary_AndCompactCount_AgreeWithThePublicScope()
    {
        using var reader = new PEReader(new MemoryStream(Image.Value));

        ApiSurface summary = ApiSurfaceExtractor.ExtractSummary(reader);
        var counted = Assert.IsType<ApiTypeInventoryCountResult.Counted>(
            ApiSurfaceExtractor.CountSummaryTypes(reader));

        Assert.Equal(Ordinary, Names(summary));
        Assert.Equal(Ordinary.Length, summary.PublicTypeCount);
        Assert.Equal(Ordinary.Length, counted.Count.Total);
    }

    [Fact]
    public void PublicWithNonPublicTypes_GivesAnUnreachablePublicTypeItsCompleteMemberList()
    {
        ApiSurface surface = Extract(ApiSurfaceExtractionScope.PublicWithNonPublicTypes);

        ApiType unreachable = Assert.Single(
            surface.Types,
            type => type.Name == "InternalOuter.PublicInInternal");
        Assert.Contains(unreachable.Members, member => member.Name == "Hidden");
        ApiType ordinary = Assert.Single(
            surface.Types,
            type => type.Name == "PublicOuter.PublicNested");
        Assert.All(ordinary.Members, member => Assert.True(member.Accessibility is null or "public"));
    }

    [Fact]
    public void ApiAxis_StillAdmitsAPublicTypeReachableByDerivation()
    {
        // The protected-nested case leaves the Public scope's public bucket but
        // stays on the API axis, which compares the protected bucket too.
        ApiSurface surface = Extract(ApiSurfaceExtractionScope.ApiAxis);

        Assert.Equal(
            ["PublicOuter", "PublicOuter.ProtectedNested", "PublicOuter.ProtectedNested.PublicInProtected", "PublicOuter.PublicNested"],
            Names(surface));
    }

    [Fact]
    public void PublicScope_OmitsTheFixturePublicNestedTypeOfAnInternalType()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        foreach (string path in new[] { pair.OldAssemblyPath(), pair.NewAssemblyPath() })
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            ApiSurface surface = ApiSurfaceExtractor.Extract(reader, ApiSurfaceExtractionScope.Public);

            Assert.DoesNotContain(
                surface.Types,
                type => type.Name.StartsWith("InternalOuterNestedMemberChanged", StringComparison.Ordinal));
            Assert.Contains(surface.Types, type => type.Name == "Outer.Inner");
        }
    }

    static readonly Lazy<byte[]> Image = new(Compile);

    static ApiSurface Extract(ApiSurfaceExtractionScope scope)
    {
        using var reader = new PEReader(new MemoryStream(Image.Value));
        return ApiSurfaceExtractor.Extract(reader, scope);
    }

    static string[] Names(ApiSurface surface) =>
        [.. surface.Types.Select(type => type.Name).Order(StringComparer.Ordinal)];

    static byte[] Compile()
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "PublicScopeReach",
            [CSharpSyntaxTree.ParseText(Source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }
}
