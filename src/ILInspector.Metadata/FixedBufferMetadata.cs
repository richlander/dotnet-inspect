using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Compiler fixed-buffer source-field metadata decoded from <c>FixedBufferAttribute</c>.</summary>
public sealed record FixedBufferMetadataInfo(string ElementTypeFullName, int Length);

internal enum FixedBufferMetadataReadState
{
    Absent,
    Present,
    Malformed,
    Unavailable,
}

internal readonly record struct FixedBufferMetadataReadResult(
    FixedBufferMetadataReadState State,
    FixedBufferMetadataInfo? Info);

public static class FixedBufferMetadata
{
    const string AttributeName = "System.Runtime.CompilerServices.FixedBufferAttribute";

    public static FixedBufferMetadataInfo? Read(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => Read(
            reader,
            attributes,
            maxAttributeRows: int.MaxValue,
            beforeMaterialize: null).Info;

    internal static FixedBufferMetadataReadResult Read(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        int maxAttributeRows,
        Action<int>? beforeMaterialize)
    {
        if (attributes.Count > maxAttributeRows)
        {
            return new(
                FixedBufferMetadataReadState.Unavailable,
                Info: null);
        }

        FixedBufferMetadataInfo? found = null;
        bool malformed = false;
        foreach (var handle in attributes)
        {
            try
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (AttributeReader.GetAttributeTypeName(
                        reader,
                        attribute.Constructor,
                        beforeMaterialize) != AttributeName)
                {
                    continue;
                }
                if (!AttributeReader.IsPlatformAttributeType(
                        reader,
                        attribute.Constructor,
                        AttributeName,
                        beforeMaterialize))
                {
                    continue;
                }
                if (!AttributeReader.HasExpectedSystemTypeInt32Constructor(
                        reader,
                        attribute.Constructor,
                        beforeMaterialize)
                    || AttributeDecoder
                        .TryDecodePreservingSerializedTypeNames(
                            reader,
                            attribute,
                            beforeMaterialize) is not
                        {
                            FixedArguments:
                            [
                                {
                                    Type: "System.Type",
                                    Value: string serializedType,
                                },
                                {
                                    Type: "int",
                                    Value: int length,
                                },
                            ],
                            NamedArguments.Length: 0,
                        }
                    || length <= 0
                    || AuthenticElementTypeFullName(serializedType) is not
                        { } elementType)
                {
                    malformed = true;
                    continue;
                }

                var current =
                    new FixedBufferMetadataInfo(elementType, length);
                if (found is not null && found != current)
                {
                    malformed = true;
                    continue;
                }
                found = current;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or InvalidOperationException)
            {
                return new(
                    FixedBufferMetadataReadState.Unavailable,
                    Info: null);
            }
        }

        if (malformed)
        {
            return new(
                FixedBufferMetadataReadState.Malformed,
                Info: null);
        }
        return found is null
            ? new(
                FixedBufferMetadataReadState.Absent,
                Info: null)
            : new(
                FixedBufferMetadataReadState.Present,
                found);
    }

    /// <summary>
    /// Resolves the buffer element type from a serialized <c>System.Type</c>
    /// argument, authenticating any assembly qualifier it carries.
    /// <see cref="ElementTypeFullName"/> keeps only the text before the first
    /// comma, so on its own it reads
    /// <c>System.Int32, Attacker, Version=1.0.0.0, ...</c> as the platform
    /// <c>System.Int32</c> and lets an attacker-defined element type claim the
    /// compiler fixed-buffer shape. A qualified name must therefore name a core
    /// contract signed with a platform key; anything else is malformed rather
    /// than a supported element type. An omitted qualifier stays acceptable
    /// because that is what the C# compiler emits for these primitives.
    /// </summary>
    static string? AuthenticElementTypeFullName(string serializedType)
    {
        if (serializedType.Length
            > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            return null;
        }

        var options = new TypeNameParseOptions
        {
            MaxNodes = MetadataSafetyPolicy.MaxRelationshipNodes,
        };
        if (!TypeName.TryParse(serializedType, out TypeName? parsed, options))
            return null;
        if (parsed.IsArray
            || parsed.IsPointer
            || parsed.IsByRef
            || parsed.IsConstructedGenericType
            || parsed.IsNested)
        {
            return null;
        }

        if (parsed.AssemblyName is { } assembly
            && !IsPlatformCoreContract(assembly))
        {
            return null;
        }

        return IsSupportedElementType(parsed.FullName)
            ? parsed.FullName
            : null;
    }

    static bool IsPlatformCoreContract(AssemblyNameInfo assembly)
    {
        if (assembly.Name is not
            ("System.Private.CoreLib"
                or "System.Runtime"
                or "mscorlib"
                or "netstandard"))
        {
            return false;
        }

        ImmutableArray<byte> token = assembly.PublicKeyOrToken;
        if (token.IsDefaultOrEmpty)
            return false;

        string publicKeyToken =
            (assembly.Flags & AssemblyNameFlags.PublicKey) != 0
                ? AssemblyReferenceIdentity.ComputePublicKeyToken(
                    token.ToArray())
                : token.Length == 8
                    ? Convert.ToHexString(token.AsSpan()).ToLowerInvariant()
                    : string.Empty;
        return PlatformKeys.IsPlatform(publicKeyToken);
    }

    public static string? ElementTypeFullName(string? assemblyQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedName))
            return null;
        int comma = assemblyQualifiedName.IndexOf(',');
        string name = comma < 0 ? assemblyQualifiedName : assemblyQualifiedName[..comma];
        return IsSupportedElementType(name) ? name : null;
    }

    public static bool IsSupportedElementType(string fullName)
        => fullName is
            "System.Boolean" or
            "System.Byte" or
            "System.SByte" or
            "System.Char" or
            "System.Int16" or
            "System.UInt16" or
            "System.Int32" or
            "System.UInt32" or
            "System.Int64" or
            "System.UInt64" or
            "System.Single" or
            "System.Double";
}
