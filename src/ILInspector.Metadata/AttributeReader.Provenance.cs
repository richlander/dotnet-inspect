using System.Collections.Immutable;
using System.Reflection.Metadata;
using InertText;

namespace ILInspector.Metadata;

public static partial class AttributeReader
{
    internal static ImmutableArray<PdbGenerationMarkerEvidence>
        ReadSourceProvenanceMarkers(
            MetadataReader reader,
            CustomAttributeHandleCollection attributes)
    {
        ImmutableArray<PdbGenerationMarkerEvidence>.Builder rows =
            ImmutableArray.CreateBuilder<PdbGenerationMarkerEvidence>();

        foreach (CustomAttributeHandle handle in attributes)
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            if (IsSourceProvenanceFrameworkAttribute(
                    reader,
                    attribute.Constructor,
                    GeneratedCodeAttributeName,
                    PdbGenerationMarkerKind.GeneratedCode,
                    beforeMaterialize: null))
            {
                if (HasExpectedConstructor(
                        reader,
                        attribute.Constructor,
                        FrameworkConstructorKind.StringString,
                        beforeMaterialize: null)
                    && AttributeDecoder.TryDecode(reader, attribute) is
                    {
                        FixedArguments.Length: 2,
                        NamedArguments.Length: 0,
                    } decoded
                    && decoded.FixedArguments[0].Value is null or string
                    && decoded.FixedArguments[1].Value is null or string)
                {
                    string? tool =
                        decoded.FixedArguments[0].Value as string;
                    string? version =
                        decoded.FixedArguments[1].Value as string;
                    rows.Add(new(
                        PdbGenerationMarkerKind.GeneratedCode,
                        PdbGenerationMarkerDisposition.Valid,
                        tool is null
                            ? null
                            : new InertString(TextPolicy.Field, tool),
                        version is null
                            ? null
                            : new InertString(TextPolicy.Field, version)));
                }
                else
                {
                    rows.Add(new(
                        PdbGenerationMarkerKind.GeneratedCode,
                        PdbGenerationMarkerDisposition.Malformed,
                        null,
                        null));
                }
                continue;
            }

            if (!IsSourceProvenanceFrameworkAttribute(
                    reader,
                    attribute.Constructor,
                    KnownAttributeNames.CompilerGeneratedAttribute,
                    PdbGenerationMarkerKind.CompilerGenerated,
                    beforeMaterialize: null))
            {
                continue;
            }

            rows.Add(
                HasExpectedConstructor(
                        reader,
                        attribute.Constructor,
                        FrameworkConstructorKind.Marker,
                        beforeMaterialize: null)
                    && HasMarkerValueBlob(reader, attribute)
                    ? new(
                        PdbGenerationMarkerKind.CompilerGenerated,
                        PdbGenerationMarkerDisposition.Valid,
                        null,
                        null)
                    : new(
                        PdbGenerationMarkerKind.CompilerGenerated,
                        PdbGenerationMarkerDisposition.Malformed,
                        null,
                        null));
        }

        return rows.ToImmutable();
    }

    private static bool IsSourceProvenanceFrameworkAttribute(
        MetadataReader reader,
        EntityHandle constructor,
        string fullTypeName,
        PdbGenerationMarkerKind kind,
        Action<int>? beforeMaterialize)
    {
        try
        {
            return TryGetAuthenticAttributeAssembly(
                    reader,
                    constructor,
                    fullTypeName,
                    beforeMaterialize,
                    out ApiAssemblyIdentity? identity)
                && PlatformKeys.IsPlatform(identity.PublicKeyToken)
                && kind switch
                {
                    PdbGenerationMarkerKind.GeneratedCode =>
                        identity.Name is
                            "System.Runtime"
                            or "netstandard"
                            or "System",
                    PdbGenerationMarkerKind.CompilerGenerated =>
                        identity.Name is
                            "System.Runtime"
                            or "netstandard"
                            or "mscorlib",
                    _ => false,
                };
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
