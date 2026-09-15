using DotnetInspector.Services;
using ILInspector.DecompilerHarness;
using Xunit;

namespace ILInspector.Decompiler.Tests;

// Regression guard for #3015 (sibling of #2942). The AspNetCore.App shared
// framework ships the *native* aspnetcorev2_inprocess.dll, which the product
// AssemblyDependencyResolver surfaces as a SharedFramework dependency. Handing a
// native PE to Roslyn as a metadata reference fails every compile-back Emit with
// CS0009 ("PE image doesn't contain managed metadata"), which previously broke a
// cluster of ~54 Windows-local compile-back tests. The managed-PE guard shared by
// every compile-back reference builder must keep it out of the reference set.
//
// The existing RoslynTestReferencesTests only feed the filter a malformed "MZ"
// stub (which fails PEReader construction); this exercises a genuine native PE —
// a valid image whose CorHeader is null — which is the actual #3015 shape.
public class CompileBackNativeReferenceTests
{
    [Fact]
    public void SharedFrameworkNativeModule_SurfacedByResolver_IsRejectedByManagedGuards()
    {
        // Resolve dependencies exactly as the compile-back reference builders do:
        // the default options include the AspNetCore.App shared framework.
        string target = typeof(object).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(target));

        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(target) { ExcludeTargetAssembly = true });

        var native = resolver.ResolveAll().FirstOrDefault(dependency =>
            Path.GetFileName(dependency.Path)
                .Equals("aspnetcorev2_inprocess.dll", StringComparison.OrdinalIgnoreCase));

        // Absent on layouts without the AspNetCore.App shared framework beside the
        // runtime (e.g. Linux CI, where the in-process module is not a .dll), so the
        // CS0009 hazard cannot arise and there is nothing to guard.
        if (native is null)
            return;

        // The resolver does surface the native module, as a shared-framework dependency.
        Assert.Equal(AssemblyDependencyProvenance.SharedFramework, native.Provenance);

        // Both managed-PE guards — the harness-side ManagedReferenceFilter applied by
        // every compile-back reference builder, and the test-side RoslynTestReferences
        // filter — must reject it. Either regressing reintroduces the CS0009 cluster.
        Assert.False(ManagedReferenceFilter.IsManagedAssembly(native.Path));
        Assert.False(RoslynTestReferences.IsManagedAssembly(native.Path));

        // A real managed assembly still passes both guards.
        Assert.True(ManagedReferenceFilter.IsManagedAssembly(target));
        Assert.True(RoslynTestReferences.IsManagedAssembly(target));
    }
}
