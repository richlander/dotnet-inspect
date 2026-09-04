using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class EcosystemPackAssemblyBoundaryTests
{
    [Fact]
    public void FriendsOnlyDedicatedTests()
    {
        string[] friends =
        [
            .. typeof(PackageSetId)
                .Assembly
                .GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(attribute => attribute.AssemblyName),
        ];

        Assert.Equal(["DotnetInspector.Ecosystems.Tests"], friends);
    }

    [Fact]
    public void OwnerContractsRequireNoFriendAccess()
    {
        Assembly ecosystems = typeof(PackageSetId).Assembly;
        Assembly[] repositoryReferences =
        [
            .. ecosystems
                .GetReferencedAssemblies()
                .Where(reference =>
                    reference.Name?.StartsWith(
                        "DotnetInspector.",
                        StringComparison.Ordinal) == true)
                .Select(Assembly.Load),
        ];

        Assert.NotEmpty(repositoryReferences);
        Assert.All(
            repositoryReferences,
            assembly => Assert.DoesNotContain(
                assembly.GetCustomAttributes<InternalsVisibleToAttribute>(),
                attribute => attribute.AssemblyName.StartsWith(
                    "DotnetInspector.Ecosystems",
                    StringComparison.Ordinal)));
    }
}
