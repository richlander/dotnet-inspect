using System.Reflection;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageAssemblyContextRolesTests
{
    [Fact]
    public void SeparateRoles_PreserveExactSurfaceImplementationCorrespondence()
    {
        ResolvedAssemblyReference surface = Assembly("Sample", marker: 1);
        ResolvedAssemblyReference implementation =
            Assembly("Sample", marker: 2);
        ResolvedAssemblyReference implementationOnly =
            Assembly("Sample.Helper", marker: 3);
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRoles roles =
            workspace.CreatePackageAssemblyContextRoles(
                [surface],
                [implementation, implementationOnly],
                [new(surface, implementation)]);

        Assert.NotSame(roles.SurfaceGroup, roles.ImplementationGroup);
        AssemblyContextParticipant surfaceParticipant =
            Assert.Single(roles.SurfaceParticipants);
        Assert.Same(
            implementation,
            roles.ImplementationParticipant(surfaceParticipant)?.Assembly);
        Assert.Equal(2, roles.ImplementationParticipants.Length);
        Assert.IsType<AssemblyBindingSelection.Missing>(
            surfaceParticipant.BindingPolicy.Select(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(
                        implementationOnly.Identity),
                    AssemblyBindingOrigin.FromAssembly(surface),
                    AssemblyResolutionScope.Any)).Selection);
    }

    [Fact]
    public void SharedRole_ReusesGroupAndLeavesReferenceOnlySurfaceUnpaired()
    {
        ResolvedAssemblyReference library = Assembly("Library", marker: 1);
        ResolvedAssemblyReference referenceOnly =
            Assembly("Reference.Only", marker: 2);
        ResolvedAssemblyReference[] assemblies = [library, referenceOnly];
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRoles roles =
            workspace.CreatePackageAssemblyContextRoles(
                assemblies,
                assemblies,
                [new(library, library)],
                shareImplementationGroup: true);

        Assert.True(roles.SharesGroup);
        Assert.Same(roles.SurfaceGroup, roles.ImplementationGroup);
        Assert.Same(
            roles.SurfaceParticipants[0],
            roles.ImplementationParticipant(
                roles.SurfaceParticipants[0]));
        Assert.Null(
            roles.ImplementationParticipant(
                roles.SurfaceParticipants[1]));
    }

    [Fact]
    public void PackageRole_DoesNotSatisfyPlatformScopedReference()
    {
        ResolvedAssemblyReference assembly =
            Assembly("System.Confusable", marker: 1);
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRoles roles =
            workspace.CreatePackageAssemblyContextRoles(
                [assembly],
                [assembly],
                [new(assembly, assembly)],
                shareImplementationGroup: true);
        IAssemblyBindingPolicy policy =
            Assert.Single(roles.SurfaceParticipants).BindingPolicy;

        AssemblyBindingSelection any = policy.Select(
            new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(assembly.Identity),
                AssemblyBindingOrigin.FromAssembly(assembly),
                AssemblyResolutionScope.Any)).Selection;
        AssemblyBindingSelection platform = policy.Select(
            new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(assembly.Identity),
                AssemblyBindingOrigin.FromAssembly(assembly),
                AssemblyResolutionScope.Platform)).Selection;

        Assert.Same(
            assembly,
            Assert.IsType<AssemblyBindingSelection.Selected>(any).Assembly);
        Assert.IsType<AssemblyBindingSelection.Missing>(platform);
        var intrinsic =
            Assert.IsType<AssemblyBindingSelection.Unavailable>(
                policy.Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.CoreLibrary(),
                        AssemblyBindingOrigin.FromAssembly(assembly),
                        AssemblyResolutionScope.Platform)).Selection);
        Assert.Equal(
            AssemblyBindingFailureKind.UnsupportedScope,
            intrinsic.Failure.Kind);
    }

    [Fact]
    public void SharedRole_RequiresExactDescriptorsAndOneLimitPolicy()
    {
        ResolvedAssemblyReference surface = Assembly("Shared", marker: 1);
        ResolvedAssemblyReference equivalent =
            Assembly("Shared", marker: 2);
        using var workspace = new InspectionWorkspace();

        ArgumentException descriptors = Assert.Throws<ArgumentException>(
            () => workspace.CreatePackageAssemblyContextRoles(
                [surface],
                [equivalent],
                [new(surface, equivalent)],
                shareImplementationGroup: true));
        Assert.Contains(
            "exact surface descriptor sequence",
            descriptors.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, GroupCount(workspace));

        ArgumentException limits = Assert.Throws<ArgumentException>(
            () => workspace.CreatePackageAssemblyContextRoles(
                [surface],
                [surface],
                [new(surface, surface)],
                shareImplementationGroup: true,
                surfaceOptions: new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes = 1,
                },
                implementationOptions: new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes = 2,
                }));
        Assert.Contains(
            "one resource-limit policy",
            limits.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public void InvalidRoles_CreateNoPartialGroup()
    {
        ResolvedAssemblyReference surface = Assembly("Surface", marker: 1);
        ResolvedAssemblyReference mismatched =
            Assembly("Implementation", marker: 2);
        using var workspace = new InspectionWorkspace();

        InvalidOperationException mismatch =
            Assert.Throws<InvalidOperationException>(
                () => workspace.CreatePackageAssemblyContextRoles(
                    [surface],
                    [mismatched],
                    [new(surface, mismatched)]));
        Assert.Contains(
            "different assembly identities",
            mismatch.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, GroupCount(workspace));

        ResolvedAssemblyReference collision =
            Assembly("Surface", marker: 3);
        InvalidOperationException duplicate =
            Assert.Throws<InvalidOperationException>(
                () => workspace.CreatePackageAssemblyContextRoles(
                    [surface, collision],
                    implementationAssemblies: null,
                    correspondences: []));
        Assert.Contains(
            "same assembly identity",
            duplicate.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, GroupCount(workspace));

        ResolvedAssemblyReference implementation =
            Assembly("Surface", marker: 4);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => workspace.CreatePackageAssemblyContextRoles(
                [surface],
                [implementation],
                [new(surface, implementation)],
                implementationOptions: new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes = -1,
                }));
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public void Dispose_ContinuesAfterBothRoleGroupsFail()
    {
        ResolvedAssemblyReference surface = Assembly("Dispose", marker: 1);
        ResolvedAssemblyReference implementation =
            Assembly("Dispose", marker: 2);
        using var workspace = new InspectionWorkspace();
        PackageAssemblyContextRoles roles =
            workspace.CreatePackageAssemblyContextRoles(
                [surface],
                [implementation],
                [new(surface, implementation)]);
        roles.SurfaceGroup.RegisterOwnedResource(
            new ThrowingResource("surface disposal failed"));
        roles.ImplementationGroup!.RegisterOwnedResource(
            new ThrowingResource("implementation disposal failed"));

        AggregateException failure =
            Assert.Throws<AggregateException>(roles.Dispose);

        IReadOnlyCollection<Exception> failures =
            failure.Flatten().InnerExceptions;
        Assert.Contains(
            failures,
            ex => ex.Message == "surface disposal failed");
        Assert.Contains(
            failures,
            ex => ex.Message == "implementation disposal failed");
        Assert.Equal(0, GroupCount(workspace));
        Assert.Throws<ObjectDisposedException>(
            () => roles.SurfaceGroup.UseAssemblyImage(
                surface,
                static image => image.Content.Length));
        Assert.Throws<ObjectDisposedException>(
            () => roles.ImplementationGroup.UseAssemblyImage(
                implementation,
                static image => image.Content.Length));
    }

    static ResolvedAssemblyReference Assembly(
        string name,
        byte marker) =>
        ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream([marker], writable: false),
            AssemblyResolutionProvenance.Package(
                "Role.Tests",
                "1.0.0",
                "net10.0",
                rid: null));

    static int GroupCount(InspectionWorkspace workspace)
    {
        FieldInfo field =
            typeof(InspectionWorkspace).GetField(
                "_groups",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "InspectionWorkspace._groups was not found.");
        return ((System.Collections.ICollection)field.GetValue(workspace)!)
            .Count;
    }

    sealed class ThrowingResource(string message) : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException(message);
    }
}
