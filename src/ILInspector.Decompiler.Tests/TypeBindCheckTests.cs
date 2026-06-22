using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Fixtures for the whole-type source binding oracle (issue #1137). They drive
/// the pure detector <see cref="TypeBindCheck.Collisions"/> so each behaviour is
/// proven in isolation: a genuine cross-namespace ambiguity is caught, a clean
/// type is silent, an unparseable/unbindable body is stubbed away (so it cannot
/// manufacture a type-level collision), and the real composed
/// <c>System.AppDomain</c> listing reproduces the #1137 collision the product
/// path cannot detect without enumerating an external namespace.
/// </summary>
public class TypeBindCheckTests
{
    static string CoreLibPath => typeof(object).Assembly.Location;
    static ImmutableArray<MetadataReference> Refs => TypeBindCheck.ReferencesFor(CoreLibPath);

    [Fact]
    public void Detects_AmbiguousReference_AcrossTwoImportedNamespaces()
    {
        // Both System.Configuration.Assemblies and System.Reflection define
        // AssemblyHashAlgorithm; the unqualified use is ambiguous (CS0104).
        const string source = """
            using System.Configuration.Assemblies;
            using System.Reflection;

            namespace T;

            public class C
            {
                public void M(AssemblyHashAlgorithm a) { }
            }
            """;

        var collisions = TypeBindCheck.Collisions("T.C", source, Refs);
        Assert.Contains(collisions, c => c.SimpleName == "AssemblyHashAlgorithm");
    }

    [Fact]
    public void CleanType_HasNoCollision()
    {
        const string source = """
            using System.Text;

            namespace T;

            public class C
            {
                public void M(StringBuilder b) { }
            }
            """;

        Assert.Empty(TypeBindCheck.Collisions("T.C", source, Refs));
    }

    [Fact]
    public void UnbindableBody_IsStubbed_AndManufacturesNoCollision()
    {
        // A body riddled with synthesized names and undefined symbols would fail
        // to bind; stubbing erases it before binding, so only type/signature-level
        // ambiguities (here: none) can surface.
        const string source = """
            using System.Text;

            namespace T;

            public class C
            {
                public StringBuilder M() => new <Broken>d__0(undefined.Thing, 42);
            }
            """;

        Assert.Empty(TypeBindCheck.Collisions("T.C", source, Refs));
    }

    [Fact]
    public void ComposedAppDomain_ReproducesTheKnownCollision()
    {
        using var pe = new PEReader(File.OpenRead(CoreLibPath));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var appDomain = Assert.Single(surface.Types, t => t.FullName == "System.AppDomain");
        var source = TypeSourceComposer.Compose(appDomain, CoreLibPath, pdbPath: null);
        Assert.NotNull(source);

        var collisions = TypeBindCheck.Collisions("System.AppDomain", source!, Refs);
        Assert.Contains(collisions, c => c.SimpleName == "AssemblyHashAlgorithm");
    }

    [Fact]
    public void KnownAppDomainArtifact_IsAllowlisted()
    {
        Assert.Contains(("System.AppDomain", "AssemblyHashAlgorithm"), TypeBindCheck.KnownArtifacts);
    }
}
