using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;

namespace ILInspector.Decompiler.Tests;

// Contract guard for the migrated Built fixture (FixtureIds.DecompilerTypeIdentity),
// formerly the in-process CSharpCompilation.Create in CompileBackTypeIdentityTests.
// The migration is only sound if the fixture stays addressable, its exact source
// is retained in the source inventory, and the advertised metadata type shapes
// the consuming test keys on are not silently dropped from the built assembly.
public sealed class TypeIdentityFixtureContractTests
{
    [Fact]
    public void Fixture_IsAddressableAndBuilt()
    {
        var fixture = FixtureCatalog.Get(FixtureIds.DecompilerTypeIdentity);
        Assert.Equal(FixtureIds.DecompilerTypeIdentity, fixture.Id);
        Assert.Contains(FixtureBoundary.CompilerLowering, fixture.Boundaries);

        string path = FixtureCatalog.AssemblyPath(FixtureIds.DecompilerTypeIdentity);
        Assert.True(File.Exists(path), $"Expected built fixture assembly at {path}.");
    }

    [Fact]
    public void Fixture_RetainsExactSource()
    {
        var sources = FixtureCatalog.SourcePaths(FixtureIds.DecompilerTypeIdentity);
        string? fixtureSource = sources.FirstOrDefault(path =>
            Path.GetFileName(path) == "TypeIdentityFixtures.cs");
        Assert.NotNull(fixtureSource);
        Assert.True(File.Exists(fixtureSource), $"Expected retained source at {fixtureSource}.");

        string text = File.ReadAllText(fixtureSource!);
        // The exact source shapes the consuming test pins against.
        Assert.Contains("public class @class", text);
        Assert.Contains("public class Container<T>", text);
        Assert.Contains("public class Inner", text);
        Assert.Contains("file class Widget", text);
        Assert.Contains("namespace @for.@class", text);
    }

    [Fact]
    public void Fixture_AdvertisedTargetsArePresentInMetadata()
    {
        using var stream = File.OpenRead(FixtureCatalog.AssemblyPath(FixtureIds.DecompilerTypeIdentity));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var typeNames = reader.TypeDefinitions
            .Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name))
            .ToHashSet(StringComparer.Ordinal);

        // Each advertised metadata name the migrated test resolves must survive.
        Assert.Contains("class", typeNames);       // keyword type
        Assert.Contains("Container`1", typeNames);  // arity-stripped generic
        Assert.Contains("Outer", typeNames);        // nesting container
        Assert.Contains("Inner", typeNames);        // nested type
        Assert.Contains("Widget", typeNames);       // public type in @for.@class
        // File-local Widget is emitted with a compiler-mangled name that starts
        // with '<' and ends with "__Widget" — distinct from the always-present
        // <Module> — so this proves the file-local shape itself was not dropped.
        Assert.Contains(typeNames, name => name.StartsWith('<') && name.EndsWith("__Widget", StringComparison.Ordinal));
    }
}
