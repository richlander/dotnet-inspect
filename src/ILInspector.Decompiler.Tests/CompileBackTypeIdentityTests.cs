using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.CSharp;
using ILInspector.DecompilerHarness;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

// The closure index (CompileBackTypeIdentity.FromDefinition) keys the FullTypes map on
// the identifier the product seam actually emits, so a missing type Roslyn reports —
// which reflects the seam's emitted spelling — matches the index key by construction
// (issue #2778). Each case pins index-key == CSharpIdentifier.Sanitize(<arity-stripped
// metadata name>) against a real compiled assembly.
public class CompileBackTypeIdentityTests
{
    [Fact]
    public void FromDefinition_KeywordType_KeysOnKeywordEscapedSpelling()
    {
        using var compiled = Compile("namespace Sample { public class @class {} }");
        var identity = IdentityOf(compiled.Reader, "class");

        Assert.Equal("@class", identity.DisplayName);
        Assert.Equal("Sample.@class", identity.FullName);
        Assert.Equal(Expected("class"), identity.DisplayName);
    }

    [Fact]
    public void FromDefinition_GenericType_KeysOnArityStrippedSpelling()
    {
        using var compiled = Compile("namespace Sample { public class Container<T> {} }");
        var identity = IdentityOf(compiled.Reader, "Container`1");

        Assert.Equal("Container", identity.DisplayName);
        Assert.Equal("Sample.Container", identity.FullName);
        Assert.Equal(Expected("Container`1"), identity.DisplayName);
    }

    [Fact]
    public void FromDefinition_NestedType_QualifiesWithDotSeparators()
    {
        using var compiled = Compile(
            "namespace Sample { public class Outer { public class Inner {} } }");
        var inner = IdentityOf(compiled.Reader, "Inner");

        Assert.Equal("Inner", inner.DisplayName);
        Assert.Equal("Sample.Outer.Inner", inner.FullName);
        Assert.DoesNotContain('+', inner.FullName);
    }

    [Fact]
    public void FromDefinition_KeywordNamespace_EscapesEachSegment()
    {
        using var compiled = Compile("namespace @for.@class { public class Widget {} }");
        var identity = IdentityOf(compiled.Reader, "Widget");

        Assert.Equal("@for.@class.Widget", identity.FullName);
    }

    [Fact]
    public void FromDefinition_FileLocalType_KeysOnSanitizedSpellableIdentifier()
    {
        using var compiled = Compile("namespace Sample { file class Widget {} }");
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

    static Compiled Compile(string source)
    {
        var compilation = CSharpCompilation.Create(
            "CompileBackTypeIdentityTests",
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Latest),
                cancellationToken: TestContext.Current.CancellationToken)],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        var errors = emit.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        Assert.True(emit.Success, string.Join(Environment.NewLine, errors));
        stream.Position = 0;
        return new Compiled(stream, new PEReader(stream, PEStreamOptions.LeaveOpen));
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

    sealed class Compiled(MemoryStream stream, PEReader pe) : IDisposable
    {
        public MetadataReader Reader { get; } = pe.GetMetadataReader();

        public void Dispose()
        {
            pe.Dispose();
            stream.Dispose();
        }
    }
}
