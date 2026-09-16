using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

internal static class CustomAttributeFidelityOracle
{
    internal static string MetadataName(MetadataReader reader, EntityHandle handle)
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
        throw new InvalidOperationException($"Unexpected oracle type handle: {handle.Kind}.");
    }

    static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

    internal static void AssertValuesEqual(
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

    internal static ResolvedAssemblyReference Descriptor(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        return ResolvedAssemblyReference.Create(
            AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()),
            path,
            () => File.OpenRead(path),
            AssemblyResolutionProvenance.Local("D3 defining image"));
    }

    internal sealed class FixtureBindingPolicy(IReadOnlyList<ResolvedAssemblyReference> assemblies)
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

    internal sealed class IndependentProvider(
        IReadOnlyDictionary<string, PrimitiveTypeCode>? sourceEnumWidths = null)
        : ICustomAttributeTypeProvider<string>
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
            _ => throw new NotSupportedException($"Not an attribute primitive: {code}."),
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
            => sourceEnumWidths is not null && sourceEnumWidths.TryGetValue(type, out var width)
                ? width
                : throw new NotSupportedException($"No source-owned enum width: {type}");
    }
}
