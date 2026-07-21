using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;

using ILInspector.CSharp;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

// The closure index (CompileBackTypeIdentity.FromDefinition) keys the FullTypes map on
// the identifier the product seam actually emits, so a missing type Roslyn reports —
// which reflects the seam's emitted spelling — matches the index key by construction
// (issue #2778). Each case pins index-key == CSharpIdentifier.Sanitize(<arity-stripped
// metadata name>) against the real compiled type-identity fixture assembly
// (FixtureIds.DecompilerTypeIdentity), whose source retains the exact type shapes.
public class CompileBackTypeIdentityTests
{
    [Fact]
    public void FromDefinition_KeywordType_KeysOnKeywordEscapedSpelling()
    {
        using var compiled = OpenFixture();
        var identity = IdentityOf(compiled.Reader, "class");

        Assert.Equal("@class", identity.DisplayName);
        Assert.Equal("Sample.@class", identity.FullName);
        Assert.Equal(Expected("class"), identity.DisplayName);
    }

    [Fact]
    public void FromDefinition_GenericType_KeysOnArityStrippedSpelling()
    {
        using var compiled = OpenFixture();
        var identity = IdentityOf(compiled.Reader, "Container`1");

        Assert.Equal("Container", identity.DisplayName);
        Assert.Equal("Sample.Container", identity.FullName);
        Assert.Equal(Expected("Container`1"), identity.DisplayName);
    }

    [Fact]
    public void FromDefinition_NestedType_QualifiesWithDotSeparators()
    {
        using var compiled = OpenFixture();
        var inner = IdentityOf(compiled.Reader, "Inner");

        Assert.Equal("Inner", inner.DisplayName);
        Assert.Equal("Sample.Outer.Inner", inner.FullName);
        Assert.DoesNotContain('+', inner.FullName);
    }

    [Fact]
    public void FromDefinition_KeywordNamespace_EscapesEachSegment()
    {
        using var compiled = OpenFixture();
        var identity = IdentityOf(compiled.Reader, "Widget");

        Assert.Equal("@for.@class.Widget", identity.FullName);
    }

    [Fact]
    public void FromDefinition_FileLocalType_KeysOnSanitizedSpellableIdentifier()
    {
        using var compiled = OpenFixture();
        var metadataName = FindMetadataName(compiled.Reader, name => name.StartsWith('<'));
        var identity = IdentityOf(compiled.Reader, metadataName);

        Assert.Equal(Expected(metadataName), identity.DisplayName);
        Assert.DoesNotContain('<', identity.DisplayName);
        Assert.DoesNotContain('>', identity.DisplayName);
        Assert.True(
            CSharpIdentifier.IsIdentifierLike(identity.DisplayName),
            $"Expected a spellable identifier, got '{identity.DisplayName}'.");
    }

    static string Expected(string metadataName)
        => CSharpIdentifier.Sanitize(StripArity(metadataName));

    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    static CompileBackTypeIdentity IdentityOf(MetadataReader reader, string metadataName)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (reader.GetString(typeDef.Name) == metadataName)
                return CompileBackTypeIdentity.FromDefinition(reader, typeDef);
        }

        throw new Xunit.Sdk.XunitException($"No type definition named '{metadataName}'.");
    }

    static string FindMetadataName(MetadataReader reader, Func<string, bool> predicate)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            string name = reader.GetString(reader.GetTypeDefinition(handle).Name);
            if (predicate(name))
                return name;
        }

        throw new Xunit.Sdk.XunitException("No matching type definition.");
    }

    static FixtureImage OpenFixture()
        => new(FixtureCatalog.AssemblyPath(FixtureIds.DecompilerTypeIdentity));

    sealed class FixtureImage : IDisposable
    {
        readonly PEReader _pe;

        public FixtureImage(string assemblyPath)
        {
            _pe = new PEReader(File.OpenRead(assemblyPath));
            Reader = _pe.GetMetadataReader();
        }

        public MetadataReader Reader { get; }

        public void Dispose() => _pe.Dispose();
    }
}
