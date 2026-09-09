using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

// Issue #3196: the VB.NET compiler emits `Protected Overrides Sub Finalize()` as
// a virtual, reuse-slot, parameterless `void Finalize()` that carries NO
// `.override System.Object::Finalize` MethodImpl (unlike the Roslyn/C#
// destructor). `ApiSurfaceExtractor.IsImplicitObjectFinalizeOverride` must
// classify that shape as a finalizer so it renders as the compilable
// `void Finalize()` (retaining the explicit base call) instead of the
// uncompilable `override void Finalize()` (CS0249). These tests run against the
// permanent, compiler-produced VB fixture (`ILInspector.Decompiler.Fixtures.VbFinalizer`)
// resolved through FixtureCatalog, so the compiler-shape claim is proven by a
// real VB artifact in CI rather than a synthetic metadata fixture alone.
[Trait("Area", "RoundTrip")]
public class VbFinalizerFixtureTests
{
    static string FixturePath => FixtureCatalog.DecompilerVbFinalizer.AssemblyPath();

    [Theory]
    [InlineData("Handle")]
    [InlineData("VbBase")]
    [InlineData("VbDerived")]
    public void VbImplicitFinalize_IsClassifiedAsFinalizer(string typeName)
    {
        var member = ExtractMember(FixturePath, typeName, "Finalize");

        Assert.True(member.IsFinalizer, $"{typeName}.Finalize should be classified as a finalizer.");
    }

    [Theory]
    [InlineData("Handle")]
    [InlineData("VbBase")]
    [InlineData("VbDerived")]
    public void VbImplicitFinalize_RendersCompilableFinalize(string typeName)
    {
        string source = ComposeType(FixturePath, typeName);

        // Classifying as a finalizer drops the uncompilable `override` (CS0249)
        // and keeps the explicit base call; it does not (yet, #3232) raise to the
        // `~Type()` destructor scaffold.
        Assert.Contains("void Finalize()", source);
        Assert.Contains("base.Finalize();", source);
        Assert.DoesNotContain("override void Finalize()", source);
        Assert.DoesNotContain("~", source);
    }

    static ApiMember ExtractMember(string path, string typeName, string memberName)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: true);
        var type = Assert.Single(surface.Types, t => t.Name == typeName);
        return Assert.Single(type.Members, m => m.Name == memberName);
    }

    static string ComposeType(string path, string typeName)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: true);
        var type = Assert.Single(surface.Types, t => t.Name == typeName);
        var source = MemberBodyProducer.Project(type, path, pdbPath: null).Output;
        Assert.NotNull(source);
        return source!.ReplaceLineEndings("\n");
    }
}
