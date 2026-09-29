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
            if (IsFrameworkAttributeType(
                    reader,
                    attribute.Constructor,
                    GeneratedCodeAttributeName,
                    SystemRuntimeAssemblyName,
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
                    && decoded.FixedArguments[0].Value is string tool
                    && decoded.FixedArguments[1].Value is string version)
                {
                    rows.Add(new(
                        PdbGenerationMarkerKind.GeneratedCode,
                        PdbGenerationMarkerDisposition.Valid,
                        new InertString(TextPolicy.Field, tool),
                        new InertString(TextPolicy.Field, version)));
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

            if (!IsFrameworkAttributeType(
                    reader,
                    attribute.Constructor,
                    KnownAttributeNames.CompilerGeneratedAttribute,
                    SystemRuntimeAssemblyName,
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
}
