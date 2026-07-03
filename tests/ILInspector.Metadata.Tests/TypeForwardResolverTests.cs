namespace ILInspector.Metadata.Tests;

public class TypeForwardResolverTests
{
    static string CoreLibPath => typeof(object).Assembly.Location;
    static string FrameworkDir => Path.GetDirectoryName(CoreLibPath)!;
    static string NetstandardFacade => Path.Combine(FrameworkDir, "netstandard.dll");

    static readonly IAssemblyReferenceResolver FrameworkResolver = new FrameworkStreamResolver(pathBacked: true);

    [Fact]
    public void LocateType_DefinedInStartingAssembly_ReturnsIt()
    {
        var location = TypeForwardResolver.LocateType(CoreLibPath, "System.String", FrameworkResolver);

        Assert.NotNull(location);
        Assert.Equal(CoreLibPath, location.AssemblyPath);
    }

    [Fact]
    public void LocateType_FollowsFacadeToDefiningAssembly()
    {
        // netstandard.dll defines nothing; every type is a forwarder.
        var location = TypeForwardResolver.LocateType(
            NetstandardFacade, "System.Collections.Generic.List`1", FrameworkResolver);

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
        var resolver = new FrameworkStreamResolver(pathBacked: false);

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
            NetstandardFacade, "System.Collections.Generic.List`1", new NullResolver());

        Assert.Null(location);
    }

    [Fact]
    public void LocateType_UnknownType_ReturnsNull()
    {
        var location = TypeForwardResolver.LocateType(
            CoreLibPath, "Definitely.Not.A.Type", FrameworkResolver);

        Assert.Null(location);
    }

    [Fact]
    public void LocateType_SelfLoop_Terminates()
    {
        // A locator that always points back at the facade would loop forever
        // without cycle detection.
        var location = TypeForwardResolver.LocateType(
            NetstandardFacade, "System.Collections.Generic.List`1", new SelfLoopResolver());

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

    sealed class FrameworkStreamResolver(bool pathBacked) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            string candidate = Path.Combine(FrameworkDir, identity.Name + ".dll");
            return File.Exists(candidate)
                ? new ResolvedAssemblyReference(identity, Path: pathBacked ? candidate : null, OpenRead: () => File.OpenRead(candidate), Provenance: "test")
                : null;
        }
    }

    sealed class NullResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope) => null;
    }

    sealed class SelfLoopResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
            => new(identity, NetstandardFacade, () => File.OpenRead(NetstandardFacade), Provenance: "test");
    }
}
