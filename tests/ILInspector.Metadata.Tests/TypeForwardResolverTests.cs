namespace ILInspector.Metadata.Tests;

public class TypeForwardResolverTests
{
    static string CoreLibPath => typeof(object).Assembly.Location;
    static string FrameworkDir => Path.GetDirectoryName(CoreLibPath)!;
    static string NetstandardFacade => Path.Combine(FrameworkDir, "netstandard.dll");

    /// <summary>Sibling-directory policy over the shared framework.</summary>
    static string? SiblingLocator(string assemblyName, AssemblyResolutionScope scope)
    {
        string candidate = Path.Combine(FrameworkDir, assemblyName + ".dll");
        return File.Exists(candidate) ? candidate : null;
    }

    [Fact]
    public void LocateType_DefinedInStartingAssembly_ReturnsIt()
    {
        var location = TypeForwardResolver.LocateType(CoreLibPath, "System.String", SiblingLocator);

        Assert.NotNull(location);
        Assert.Equal(CoreLibPath, location.AssemblyPath);
    }

    [Fact]
    public void LocateType_FollowsFacadeToDefiningAssembly()
    {
        // netstandard.dll defines nothing; every type is a forwarder.
        var location = TypeForwardResolver.LocateType(
            NetstandardFacade, "System.Collections.Generic.List`1", SiblingLocator);

        Assert.NotNull(location);
        Assert.NotEqual(NetstandardFacade, location.AssemblyPath);
        Assert.True(TypeForwardResolver.DefinesType(location.AssemblyPath!, "System.Collections.Generic.List`1"));
    }

    [Fact]
    public void LocateType_ResolverOverload_FollowsForwarderThroughOpenReadWithoutPath()
    {
        var start = new ResolvedAssemblyReference(
            new AssemblyReferenceIdentity("netstandard", Version: null, Culture: null, PublicKeyToken: null),
            Path: null,
            OpenRead: () => File.OpenRead(NetstandardFacade),
            Provenance: "test");
        var resolver = new FrameworkStreamResolver();

        var location = TypeForwardResolver.LocateType(
            start, "System.Collections.Generic.List`1", resolver);

        Assert.NotNull(location);
        Assert.Null(location.AssemblyPath);
        using var stream = location.OpenRead();
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void LocateType_LocatorCannotResolve_ReturnsNull()
    {
        var location = TypeForwardResolver.LocateType(
            NetstandardFacade, "System.Collections.Generic.List`1", (_, _) => null);

        Assert.Null(location);
    }

    [Fact]
    public void LocateType_UnknownType_ReturnsNull()
    {
        var location = TypeForwardResolver.LocateType(
            CoreLibPath, "Definitely.Not.A.Type", SiblingLocator);

        Assert.Null(location);
    }

    [Fact]
    public void LocateType_SelfLoop_Terminates()
    {
        // A locator that always points back at the facade would loop forever
        // without cycle detection.
        var location = TypeForwardResolver.LocateType(
            NetstandardFacade, "System.Collections.Generic.List`1", (_, _) => NetstandardFacade);

        Assert.Null(location);
    }

    [Fact]
    public void DefinesType_And_ForwardsType_DistinguishFacades()
    {
        Assert.True(TypeForwardResolver.DefinesType(CoreLibPath, "System.String"));
        Assert.False(TypeForwardResolver.ForwardsType(CoreLibPath, "System.String"));

        Assert.False(TypeForwardResolver.DefinesType(NetstandardFacade, "System.String"));
        Assert.True(TypeForwardResolver.ForwardsType(NetstandardFacade, "System.String"));
    }

    sealed class FrameworkStreamResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            string candidate = Path.Combine(FrameworkDir, identity.Name + ".dll");
            return File.Exists(candidate)
                ? new ResolvedAssemblyReference(identity, Path: null, OpenRead: () => File.OpenRead(candidate), Provenance: "test")
                : null;
        }
    }
}
