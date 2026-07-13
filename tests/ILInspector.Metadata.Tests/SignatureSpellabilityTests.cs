using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata.Tests.SpellabilityConsumer;
using ILInspector.Metadata.Tests.SpellabilityReference;

namespace ILInspector.Metadata.Tests;

public sealed class SignatureSpellabilityTests
{
    [Fact]
    public void CanSpellField_RejectsReferencedInternalType()
    {
        using var fixture = OpenFixture();

        var hidden = fixture.Spellability.InspectField(
            fixture.Reader,
            GetField(fixture.Reader, fixture.Type, "HiddenField"),
            GenericContext.ForType(fixture.Reader, fixture.Type));
        var visible = fixture.Spellability.InspectField(
            fixture.Reader,
            GetField(fixture.Reader, fixture.Type, "VisibleField"),
            GenericContext.ForType(fixture.Reader, fixture.Type));

        Assert.False(hidden.CanSpell);
        Assert.Null(hidden.DecodeStatus);
        Assert.True(visible.CanSpell);
        Assert.Null(visible.DecodeStatus);
    }

    [Fact]
    public void CanSpellProperty_RejectsReferencedInternalType()
    {
        using var fixture = OpenFixture();

        Assert.False(fixture.Spellability.CanSpellProperty(
            fixture.Reader,
            GetProperty(fixture.Reader, fixture.Type, "HiddenProperty"),
            GenericContext.ForType(fixture.Reader, fixture.Type)));
        Assert.True(fixture.Spellability.CanSpellProperty(
            fixture.Reader,
            GetProperty(fixture.Reader, fixture.Type, "VisibleProperty"),
            GenericContext.ForType(fixture.Reader, fixture.Type)));
    }

    [Fact]
    public void CanSpellProperty_RejectsReferencedInternalIndexerParameterType()
    {
        using var fixture = OpenFixture();

        Assert.False(fixture.Spellability.CanSpellProperty(
            fixture.Reader,
            GetProperty(fixture.Reader, fixture.Type, "Item"),
            GenericContext.ForType(fixture.Reader, fixture.Type)));
    }

    [Fact]
    public void CanSpellMethod_RejectsReferencedInternalType()
    {
        using var fixture = OpenFixture();

        Assert.False(fixture.Spellability.CanSpellMethod(
            fixture.Reader,
            GetMethod(fixture.Reader, fixture.Type, "HiddenMethod"),
            GenericContext.ForMethod(fixture.Reader, fixture.Type, GetMethod(fixture.Reader, fixture.Type, "HiddenMethod"))));
        Assert.True(fixture.Spellability.CanSpellMethod(
            fixture.Reader,
            GetMethod(fixture.Reader, fixture.Type, "VisibleMethod"),
            GenericContext.ForMethod(fixture.Reader, fixture.Type, GetMethod(fixture.Reader, fixture.Type, "VisibleMethod"))));
    }

    [Fact]
    public void CanSpellField_RelaxesVersionWhenResolverUnifiesReferences()
    {
        using var fixture = OpenFixture(new VersionRelaxingResolver(typeof(VisibleReferenceType).Assembly.Location));

        Assert.False(fixture.Spellability.CanSpellField(
            fixture.Reader,
            GetField(fixture.Reader, fixture.Type, "HiddenField"),
            GenericContext.ForType(fixture.Reader, fixture.Type)));
    }

    static Fixture OpenFixture(IAssemblyReferenceResolver? resolver = null)
    {
        var stream = File.OpenRead(typeof(SignatureSpellabilityConsumerFixtures).Assembly.Location);
        var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var type = GetTypeDefinition(reader, typeof(SignatureSpellabilityConsumerFixtures));
        resolver ??= new MapResolver(typeof(VisibleReferenceType).Assembly.Location);
        return new Fixture(stream, pe, reader, type, new SignatureSpellability(resolver));
    }

    static TypeDefinition GetTypeDefinition(MetadataReader reader, Type type)
    {
        var expected = type.FullName!.Replace('+', '.');
        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (reader.GetFullTypeName(typeDef) == expected)
                return typeDef;
        }

        throw new InvalidOperationException($"Type '{expected}' was not found.");
    }

    static FieldDefinition GetField(MetadataReader reader, TypeDefinition type, string name)
    {
        foreach (var handle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            if (reader.GetString(field.Name) == name)
                return field;
        }

        throw new InvalidOperationException($"Field '{name}' was not found.");
    }

    static PropertyDefinition GetProperty(MetadataReader reader, TypeDefinition type, string name)
    {
        foreach (var handle in type.GetProperties())
        {
            var property = reader.GetPropertyDefinition(handle);
            if (reader.GetString(property.Name) == name)
                return property;
        }

        throw new InvalidOperationException($"Property '{name}' was not found.");
    }

    static MethodDefinition GetMethod(MetadataReader reader, TypeDefinition type, string name)
    {
        foreach (var handle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (reader.GetString(method.Name) == name)
                return method;
        }

        throw new InvalidOperationException($"Method '{name}' was not found.");
    }

    sealed class MapResolver(params string[] paths) : IAssemblyReferenceResolver
    {
        readonly Dictionary<string, string> _paths = paths.ToDictionary(
            path => Path.GetFileNameWithoutExtension(path)!,
            Path.GetFullPath,
            StringComparer.OrdinalIgnoreCase);

        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
            => _paths.TryGetValue(identity.Name, out var path)
                ? new ResolvedAssemblyReference(identity, path, () => File.OpenRead(path), "TestMap")
                : null;
    }

    sealed class VersionRelaxingResolver(string path) : IAssemblyReferenceResolver
    {
        readonly string _assemblyName = Path.GetFileNameWithoutExtension(path);
        readonly string _path = Path.GetFullPath(path);

        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
            => identity.Version is null && identity.Name.Equals(_assemblyName, StringComparison.OrdinalIgnoreCase)
                ? new ResolvedAssemblyReference(identity, _path, () => File.OpenRead(_path), "VersionRelaxing")
                : null;
    }

    sealed record Fixture(
        Stream Stream,
        PEReader Pe,
        MetadataReader Reader,
        TypeDefinition Type,
        SignatureSpellability Spellability) : IDisposable
    {
        public void Dispose()
        {
            Pe.Dispose();
            Stream.Dispose();
        }
    }
}
