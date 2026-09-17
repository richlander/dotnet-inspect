using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;

namespace ILInspector.Metadata.Tests;

public sealed class PublicMethodRootInventoryTests
{
    static readonly PublicMethodRootInventoryLimits FullLimits =
        new(
            MaximumTypeDefinitions: 1_000,
            MaximumMethodDefinitions: 10_000,
            MaximumRoots: 10_000);

    [Fact]
    public void Read_ReturnsEveryExactPublicMethodDef()
    {
        using FixtureImage fixture = OpenFixture();

        PublicMethodRootInventory inventory =
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                FullLimits);

        Assert.True(inventory.IsComplete);
        Assert.Null(inventory.Boundary);
        Assert.Equal(
            inventory.Roots
                .OrderBy(static root => root.Token),
            inventory.Roots);
        Assert.All(
            inventory.Roots,
            root => Assert.Equal(
                inventory.ModuleVersionId,
                root.ModuleVersionId));
        Assert.Equal(
            [
                "IPublicContract.InterfaceMethod",
                "PublicAbstract.Bodiless",
                "PublicNested..ctor",
                "PublicNested.PublicNestedMethod",
                "PublicTopLevel..ctor",
                "PublicTopLevel.CompilerGeneratedPublic",
                "PublicTopLevel.HiddenByPresentation",
                "PublicTopLevel.PublicMethod",
                "PublicTopLevel.add_Changed",
                "PublicTopLevel.get_Value",
                "PublicTopLevel.op_Addition",
                "PublicTopLevel.remove_Changed",
            ],
            DisplayRoots(fixture.Reader, inventory)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Read_RequiresPublicDeclaringTypeChain()
    {
        using FixtureImage fixture = OpenFixture();

        PublicMethodRootInventory inventory =
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                FullLimits);
        string[] roots = DisplayRoots(
            fixture.Reader,
            inventory);

        Assert.DoesNotContain(
            roots,
            root => root.Contains(
                "HiddenByInternal",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.Contains(
                "HiddenByProtected",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.Contains(
                "HiddenByPrivate",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.EndsWith(
                ".ProtectedMethod",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.EndsWith(
                ".InternalMethod",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.EndsWith(
                ".PrivateMethod",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.EndsWith(
                ".set_Value",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Read_DoesNotApplyPresentationFilters()
    {
        using FixtureImage fixture = OpenFixture();

        string[] roots = DisplayRoots(
            fixture.Reader,
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                FullLimits));

        Assert.Contains(
            "PublicTopLevel.HiddenByPresentation",
            roots);
        Assert.Contains(
            "PublicTopLevel.CompilerGeneratedPublic",
            roots);
    }

    [Fact]
    public void Read_ReportsIndependentExactCapacityBounds()
    {
        using FixtureImage fixture = OpenFixture();
        PublicMethodRootInventory full =
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                FullLimits);

        PublicMethodRootInventory exact =
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                new(
                    full.Receipt.VisitedTypeDefinitions,
                    full.Receipt.VisitedMethodDefinitions,
                    full.Receipt.RetainedRoots));
        Assert.True(exact.IsComplete);
        Assert.Equal(full.Roots, exact.Roots);

        AssertBound(
            fixture.Reader,
            full,
            new(
                full.Receipt.VisitedTypeDefinitions - 1,
                FullLimits.MaximumMethodDefinitions,
                FullLimits.MaximumRoots),
            PublicMethodRootInventoryLimit.TypeDefinitions);
        AssertBound(
            fixture.Reader,
            full,
            new(
                FullLimits.MaximumTypeDefinitions,
                full.Receipt.VisitedMethodDefinitions - 1,
                FullLimits.MaximumRoots),
            PublicMethodRootInventoryLimit.MethodDefinitions);
        AssertBound(
            fixture.Reader,
            full,
            new(
                FullLimits.MaximumTypeDefinitions,
                FullLimits.MaximumMethodDefinitions,
                full.Receipt.RetainedRoots - 1),
            PublicMethodRootInventoryLimit.Roots);
    }

    static void AssertBound(
        MetadataReader reader,
        PublicMethodRootInventory full,
        PublicMethodRootInventoryLimits limits,
        PublicMethodRootInventoryLimit expected)
    {
        PublicMethodRootInventory inventory =
            PublicMethodRootInventoryReader.Read(
                reader,
                limits);

        Assert.False(inventory.IsComplete);
        Assert.Equal(expected, inventory.Boundary?.Limit);
        Assert.Equal(
            expected switch
            {
                PublicMethodRootInventoryLimit.TypeDefinitions =>
                    limits.MaximumTypeDefinitions,
                PublicMethodRootInventoryLimit.MethodDefinitions =>
                    limits.MaximumMethodDefinitions,
                PublicMethodRootInventoryLimit.Roots =>
                    limits.MaximumRoots,
                _ => throw new UnreachableException(),
            },
            inventory.Boundary?.Maximum);
        Assert.Equal(
            full.Roots.Take(inventory.Roots.Length),
            inventory.Roots);

        int charged = expected switch
        {
            PublicMethodRootInventoryLimit.TypeDefinitions =>
                inventory.Receipt.VisitedTypeDefinitions,
            PublicMethodRootInventoryLimit.MethodDefinitions =>
                inventory.Receipt.VisitedMethodDefinitions,
            PublicMethodRootInventoryLimit.Roots =>
                inventory.Receipt.RetainedRoots,
            _ => throw new UnreachableException(),
        };
        Assert.Equal(inventory.Boundary?.Maximum, charged);
    }

    static string[] DisplayRoots(
        MetadataReader reader,
        PublicMethodRootInventory inventory) =>
        [
            .. inventory.Roots.Select(root =>
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(root.Handle);
                TypeDefinition type =
                    reader.GetTypeDefinition(
                        method.GetDeclaringType());
                return $"{reader.GetString(type.Name)}."
                    + reader.GetString(method.Name);
            }),
        ];

    static FixtureImage OpenFixture() =>
        new(
            FixtureCatalog.MetadataPublicMethodRoots
                .AssemblyPath());

    sealed class FixtureImage : IDisposable
    {
        readonly FileStream _stream;
        readonly PEReader _image;

        internal FixtureImage(string path)
        {
            _stream = File.OpenRead(path);
            _image = new PEReader(_stream);
            Reader = _image.GetMetadataReader();
        }

        internal MetadataReader Reader { get; }

        public void Dispose()
        {
            _image.Dispose();
            _stream.Dispose();
        }
    }
}
