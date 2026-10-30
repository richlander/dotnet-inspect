using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;

using DotnetInspect.Web.Interop.Analysis;
using DotnetInspect.Web.Interop.Package;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    [Theory]
    [InlineData("Generated", 0)]
    [InlineData("<Rejected>g__Local", 1)]
    public async Task StructuralSalience_SeparatesRoutineAndRejectedOwnership(
        string methodName,
        int rejectedBodies)
    {
        string packageId = $"Browser.Ownership.{rejectedBodies}";
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(packageId), typeof(object).Assembly);
        TypeBuilder type = assembly.DefineDynamicModule(packageId)
            .DefineType("Example.Host", TypeAttributes.Public);
        MethodBuilder method = type.DefineMethod(methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), Type.EmptyTypes);
        method.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!, []));
        method.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);

        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(packageId, "1.0.0",
                PackageEntries(($"lib/net11.0/{packageId}.dll", stream.ToArray())),
                fromCache: false));
        BrowserLibraryStructuralSalience result =
            Assert.IsType<BrowserLibraryStructuralSalience>(JsonSerializer.Deserialize(
                await AnalysisExports.QueryPackageLibraryStructuralSalience(
                    packageId, "1.0.0", "net11.0", packageId),
                BrowserAnalysisJsonContext.Default.BrowserLibraryStructuralSalience));

        Assert.Equal(3, result.SchemaVersion);
        Assert.Equal("available", result.Surface.Outcome);
        Assert.Equal("complete", result.Surface.NamespaceIndex!.Disposition);
        var shard = Assert.Single(result.Implementation.TypeLeverageShards);
        Assert.Equal("qualified", shard.Disposition);
        Assert.Equal(1, shard.BodyCoverage!.BodiesPhysicalOnly);
        Assert.Equal(rejectedBodies, shard.BodyCoverage.BodiesRejectedOwnership);
        Assert.Equal(0, shard.BodyCoverage.BodiesUnavailable);
        Assert.Equal(0, shard.BodyCoverage.BodiesLimited);
        Assert.Contains(shard.Diagnostics, detail => rejectedBodies == 1
            ? detail.Contains("ownership evidence was rejected", StringComparison.Ordinal)
            : detail.Contains("physical-only evidence", StringComparison.Ordinal));
    }
}
