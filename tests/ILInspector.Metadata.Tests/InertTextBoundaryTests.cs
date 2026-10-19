using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using InertText;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates the compiled references from Metadata into InertText, which can see InertText internals.
/// </summary>
public sealed class InertTextBoundaryTests
{
    // The encoded-length preflight is the one sanctioned internal reach. Range, reader, and
    // already-contained constructors are deliberately absent.
    private static readonly string[] AllowedInternalMembers =
    [
        "InertText.Encoding.VisualEncoder.MeasureEncodedLength",
    ];

    [Fact]
    public void Metadata_ReachesInertTextOnlyThroughItsPublicSurface()
    {
        Assembly inertText = typeof(InertString).Assembly;
        using FileStream stream =
            File.OpenRead(typeof(MetadataUtf8Name).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        var provider = new ReflectionTypeProvider(reader, inertText);
        List<string> violations = [];
        int inspected = 0;

        foreach (TypeReferenceHandle handle in reader.TypeReferences)
        {
            if (provider.Resolve(handle) is { } type
                && type.Assembly == inertText
                && !IsVisible(type))
            {
                violations.Add($"type {type.FullName}");
            }
        }

        foreach (MemberReferenceHandle handle in reader.MemberReferences)
        {
            MemberReference member = reader.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference
                || provider.Resolve((TypeReferenceHandle)member.Parent) is not { } owner
                || owner.Assembly != inertText)
            {
                continue;
            }

            inspected++;
            string name = reader.GetString(member.Name);
            MemberInfo resolved = ResolveMember(owner, name, member, provider);
            if (!IsPublic(resolved)
                && !AllowedInternalMembers.Contains($"{owner.FullName}.{name}"))
            {
                violations.Add($"{owner.FullName}.{name}");
            }
        }

        Assert.True(inspected > 0, "Metadata should reference InertText members.");
        Assert.Empty(violations);
    }

    private static MemberInfo ResolveMember(
        Type owner,
        string name,
        MemberReference member,
        ReflectionTypeProvider provider)
    {
        const BindingFlags all =
            BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (member.GetKind() == MemberReferenceKind.Field)
        {
            return owner.GetField(name, all)
                ?? throw new InvalidOperationException($"Unresolved field {owner}.{name}.");
        }

        MethodSignature<Type> signature =
            member.DecodeMethodSignature(provider, genericContext: null);
        Type[] parameters = [.. signature.ParameterTypes];
        MemberInfo? resolved = name == ".ctor"
            ? owner.GetConstructor(all, parameters)
            : owner.GetMethod(
                name,
                signature.GenericParameterCount,
                all,
                binder: null,
                parameters,
                modifiers: null);
        return resolved
            ?? throw new InvalidOperationException(
                $"Unresolved method {owner}.{name}({string.Join(", ", parameters.Select(p => p.Name))}).");
    }

    private static bool IsVisible(Type type) =>
        type.IsPublic || (type.IsNestedPublic && IsVisible(type.DeclaringType!));

    private static bool IsPublic(MemberInfo member) =>
        member switch
        {
            MethodBase method => method.IsPublic,
            FieldInfo field => field.IsPublic,
            _ => false,
        };

    private sealed class ReflectionTypeProvider(
        MetadataReader reader,
        Assembly inertText) : ISignatureTypeProvider<Type, object?>
    {
        public Type? Resolve(TypeReferenceHandle handle)
        {
            TypeReference reference = reader.GetTypeReference(handle);
            string name = reader.GetString(reference.Name);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return Resolve((TypeReferenceHandle)reference.ResolutionScope)
                    ?.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
                return null;

            string assemblyName = reader.GetString(
                reader.GetAssemblyReference(
                    (AssemblyReferenceHandle)reference.ResolutionScope).Name);
            string fullName = reference.Namespace.IsNil
                ? name
                : $"{reader.GetString(reference.Namespace)}.{name}";
            Assembly? assembly =
                assemblyName == inertText.GetName().Name
                    ? inertText
                    : TryLoad(assemblyName);
            return assembly?.GetType(fullName);
        }

        public Type GetTypeFromReference(
            MetadataReader metadata,
            TypeReferenceHandle handle,
            byte rawTypeKind) =>
            Resolve(handle)
                ?? throw new InvalidOperationException("Unresolved signature type.");

        public Type GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            typeCode switch
            {
                PrimitiveTypeCode.Boolean => typeof(bool),
                PrimitiveTypeCode.Byte => typeof(byte),
                PrimitiveTypeCode.Char => typeof(char),
                PrimitiveTypeCode.Double => typeof(double),
                PrimitiveTypeCode.Int16 => typeof(short),
                PrimitiveTypeCode.Int32 => typeof(int),
                PrimitiveTypeCode.Int64 => typeof(long),
                PrimitiveTypeCode.IntPtr => typeof(nint),
                PrimitiveTypeCode.Object => typeof(object),
                PrimitiveTypeCode.SByte => typeof(sbyte),
                PrimitiveTypeCode.Single => typeof(float),
                PrimitiveTypeCode.String => typeof(string),
                PrimitiveTypeCode.TypedReference => typeof(TypedReference),
                PrimitiveTypeCode.UInt16 => typeof(ushort),
                PrimitiveTypeCode.UInt32 => typeof(uint),
                PrimitiveTypeCode.UInt64 => typeof(ulong),
                PrimitiveTypeCode.UIntPtr => typeof(nuint),
                PrimitiveTypeCode.Void => typeof(void),
                _ => throw new NotSupportedException(typeCode.ToString()),
            };

        public Type GetGenericInstantiation(
            Type genericType,
            ImmutableArray<Type> typeArguments) =>
            genericType.MakeGenericType([.. typeArguments]);

        public Type GetGenericMethodParameter(object? genericContext, int index) =>
            Type.MakeGenericMethodParameter(index);

        public Type GetSZArrayType(Type elementType) => elementType.MakeArrayType();

        public Type GetByReferenceType(Type elementType) => elementType.MakeByRefType();

        public Type GetPointerType(Type elementType) => elementType.MakePointerType();

        public Type GetArrayType(Type elementType, ArrayShape shape) =>
            elementType.MakeArrayType(shape.Rank);

        public Type GetPinnedType(Type elementType) => elementType;

        public Type GetModifiedType(Type modifier, Type unmodifiedType, bool isRequired) =>
            unmodifiedType;

        public Type GetTypeFromDefinition(
            MetadataReader metadata,
            TypeDefinitionHandle handle,
            byte rawTypeKind) =>
            throw new NotSupportedException("InertText signatures reference no Metadata types.");

        public Type GetTypeFromSpecification(
            MetadataReader metadata,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            metadata.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public Type GetGenericTypeParameter(object? genericContext, int index) =>
            throw new NotSupportedException("InertText has no generic types.");

        public Type GetFunctionPointerType(MethodSignature<Type> signature) => typeof(nint);

        private static Assembly? TryLoad(string name)
        {
            try
            {
                return Assembly.Load(name);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }
}
