using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using AttributeEnumFixtures;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class CustomAttributeFidelityTests
{
    public static IEnumerable<object[]> IndependentCases =>
        CustomAttributeFidelitySamples.IndependentCases;

    public static IEnumerable<object[]> EnumCases =>
        CustomAttributeFidelitySamples.EnumCases;

    [Theory]
    [MemberData(nameof(IndependentCases))]
    public void CompilerProducedValues_EqualIndependentSrm(Type sample)
    {
        using var pe = new PEReader(File.OpenRead(sample.Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        CustomAttribute attribute = SampleAttribute(reader, sample);
        var expected = attribute.DecodeValue(new IndependentProvider());

        var actual = AttributeDecoder.TryDecodePreservingSerializedTypeNames(reader, attribute);

        Assert.NotNull(actual);
        AssertValuesEqual(expected, actual.Value);
    }

    [Theory]
    [MemberData(nameof(EnumCases))]
    public void RetainedCrossAssemblyEnums_EqualProducerTruth(
        Type sample,
        CustomAttributeValue<string> expected)
    {
        var fixture = FixtureCatalog.Get(FixtureIds.MetadataAttributeEnums);
        Assert.Contains(FixtureBoundary.CrossAssemblyBoundary, fixture.Boundaries);
        ResolvedAssemblyReference producer = Descriptor(fixture.AssemblyPath());
        ResolvedAssemblyReference consumer = Descriptor(sample.Assembly.Location);
        Assert.NotEqual(producer.Identity.Name, consumer.Identity.Name);
        var requests = new[]
        {
            Request(ProducerTruth.WideName, producer.Identity.Name, consumer),
            Request(ProducerTruth.NarrowName, producer.Identity.Name, consumer),
        };
        using TypeResolutionContext context = TypeResolutionContext.Create(
            new FixtureBindingPolicy(
            [
                producer,
                Descriptor(typeof(object).Assembly.Location),
                Descriptor(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll")),
            ]),
            [consumer],
            requests);

        foreach (var request in requests)
        {
            var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(context.Resolve(request));
            Assert.Equal(producer.Identity, resolved.Definition.Assembly.Assembly.Identity);
        }
        Func<string, PrimitiveTypeCode> resolver =
            TypeResolutionEnumWidth.CreateResolver(context, requests);
        using var pe = new PEReader(File.OpenRead(sample.Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();

        var actual = AttributeDecoder.TryDecode(
            reader, SampleAttribute(reader, sample), beforeMaterialize: null, resolver);

        Assert.NotNull(actual);
        // The expected tree is declared beside the source samples, not computed
        // by SRM or by the retained-image adapter under test.
        AssertValuesEqual(expected, actual.Value);
    }

    static CustomAttribute SampleAttribute(MetadataReader reader, Type sample)
    {
        var handle = (TypeDefinitionHandle)MetadataTokens.EntityHandle(sample.MetadataToken);
        var attributes = reader.GetTypeDefinition(handle).GetCustomAttributes()
            .Select(reader.GetCustomAttribute)
            .Where(attribute => AttributeTypeName(reader, attribute).StartsWith(
                typeof(CustomAttributeFidelitySamples).FullName + ".", StringComparison.Ordinal));
        return Assert.Single(attributes);
    }

    static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        EntityHandle type = attribute.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetMethodDefinition(
                (MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            HandleKind.MemberReference => reader.GetMemberReference(
                (MemberReferenceHandle)attribute.Constructor).Parent,
            _ => throw new InvalidOperationException("Sample has no attribute constructor."),
        };
        return MetadataName(reader, type);
    }

    static string MetadataName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            return type.IsNested
                ? MetadataName(reader, type.GetDeclaringType()) + "." + reader.GetString(type.Name)
                : Join(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }
        if (handle.Kind == HandleKind.TypeReference)
        {
            var type = reader.GetTypeReference((TypeReferenceHandle)handle);
            return type.ResolutionScope.Kind == HandleKind.TypeReference
                ? MetadataName(reader, type.ResolutionScope) + "." + reader.GetString(type.Name)
                : Join(reader.GetString(type.Namespace), reader.GetString(type.Name));
        }
        throw new InvalidOperationException($"Unexpected sample type handle: {handle.Kind}.");
    }

    static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

    static void AssertValuesEqual(
        CustomAttributeValue<string> expected,
        CustomAttributeValue<string> actual)
    {
        Assert.Equal(expected.FixedArguments.Length, actual.FixedArguments.Length);
        for (int i = 0; i < expected.FixedArguments.Length; i++)
            AssertArgumentEqual(expected.FixedArguments[i], actual.FixedArguments[i]);
        Assert.Equal(expected.NamedArguments.Length, actual.NamedArguments.Length);
        for (int i = 0; i < expected.NamedArguments.Length; i++)
        {
            var left = expected.NamedArguments[i];
            var right = actual.NamedArguments[i];
            Assert.Equal(left.Name, right.Name);
            Assert.Equal(left.Kind, right.Kind);
            AssertArgumentEqual(new(left.Type, left.Value), new(right.Type, right.Value));
        }
    }

    static void AssertArgumentEqual(
        CustomAttributeTypedArgument<string> expected,
        CustomAttributeTypedArgument<string> actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        if (expected.Value is null)
        {
            Assert.Null(actual.Value);
            return;
        }
        Assert.IsType(expected.Value.GetType(), actual.Value);
        switch (expected.Value)
        {
            case ImmutableArray<CustomAttributeTypedArgument<string>> left:
                var right = (ImmutableArray<CustomAttributeTypedArgument<string>>)actual.Value!;
                Assert.Equal(left.IsDefault, right.IsDefault);
                if (left.IsDefault)
                    break;
                Assert.Equal(left.Length, right.Length);
                for (int i = 0; i < left.Length; i++)
                    AssertArgumentEqual(left[i], right[i]);
                break;
            case float left:
                Assert.Equal(BitConverter.SingleToInt32Bits(left),
                    BitConverter.SingleToInt32Bits((float)actual.Value!));
                break;
            case double left:
                Assert.Equal(BitConverter.DoubleToInt64Bits(left),
                    BitConverter.DoubleToInt64Bits((double)actual.Value!));
                break;
            default:
                Assert.Equal(expected.Value, actual.Value);
                break;
        }
    }

    static ResolvedAssemblyReference Descriptor(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        return ResolvedAssemblyReference.Create(
            AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()),
            path,
            () => File.OpenRead(path),
            AssemblyResolutionProvenance.Local("compiler-produced D3 fixture"));
    }

    static TypeResolutionRequest Request(
        string typeName, string assemblyName, ResolvedAssemblyReference consumer)
    {
        Assert.True(TypeResolutionEnumWidth.TryCreateRequest(
            $"{typeName}, {assemblyName}", consumer, AssemblyResolutionScope.Any, out var request));
        return request;
    }

    sealed class FixtureBindingPolicy(IReadOnlyList<ResolvedAssemblyReference> assemblies)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            var candidate = request.Target is AssemblyBindingTarget.AssemblyReference reference
                ? assemblies.SingleOrDefault(assembly =>
                    reference.Identity.MatchesCandidate(assembly.Identity))
                : null;
            return new(Version, candidate is null
                ? AssemblyBindingSelection.NotFound()
                : AssemblyBindingSelection.Found(candidate));
        }
    }

    sealed class IndependentProvider : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode code) => code switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            _ => throw new NotSupportedException($"Not a sample primitive: {code}."),
        };

        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string type) => type == GetSystemType();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromSerializedName(string name) => name;
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => MetadataName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => MetadataName(reader, handle);
        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
            => throw new NotSupportedException("Enum fidelity uses producer truth, not this SRM oracle.");
    }
}
