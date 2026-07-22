using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public enum MethodCorrespondenceStatus
{
    Exact,
    Absent,
    Ambiguous,
    Failed,
}

/// <summary>
/// Total cross-reader correspondence for one metadata method definition.
/// Exact correspondence requires exactly one target method with the same
/// structural cross-module identity (see <see cref="MethodStructuralSignature"/>);
/// display names and metadata row numbers are never used as cross-module
/// identity.
/// </summary>
public sealed record MethodCorrespondenceResult(
    MethodCorrespondenceStatus Status,
    MemberAnchor? Anchor,
    MetadataMethodAddress? Target,
    IReadOnlyList<MetadataMethodAddress> Candidates,
    string? Failure)
{
    public bool IsExact => Status == MethodCorrespondenceStatus.Exact;
}

public static class MethodCorrespondenceResolver
{
    public static MethodCorrespondenceResult Resolve(
        MetadataReader sourceReader,
        MetadataMethodAddress source,
        MetadataReader targetReader)
    {
        try
        {
            if (!source.BelongsTo(sourceReader))
                return Failed("source method address belongs to a different metadata module");
            if (!IsValid(sourceReader, source.Handle))
                return Failed("source method handle is outside its metadata module");

            var sourceMethod = sourceReader.GetMethodDefinition(source.Handle);
            var sourceTypeHandle = sourceMethod.GetDeclaringType();
            var sourceType = sourceReader.GetTypeDefinition(sourceTypeHandle);
            var anchor = ApiMemberIdentity.CreateMethodAnchor(
                sourceReader,
                sourceTypeHandle,
                sourceMethod,
                IsExtensionMethod(sourceReader, sourceType, sourceMethod));

            // Correspondence is decided by a structural cross-module identity, not
            // the display-oriented canonical signature: the structural key carries
            // return type, calling convention, generic arity (positionally), custom
            // modifiers, and the nested-versus-namespace boundary, so genuinely
            // distinct CLR methods cannot collide and a renamed generic parameter
            // cannot break a real match.
            string sourceKey = MethodStructuralSignature.Build(sourceReader, sourceMethod);

            List<MetadataMethodAddress> candidates = [];
            foreach (var targetHandle in targetReader.MethodDefinitions)
            {
                var targetMethod = targetReader.GetMethodDefinition(targetHandle);
                string targetKey = MethodStructuralSignature.Build(targetReader, targetMethod);
                if (string.Equals(sourceKey, targetKey, StringComparison.Ordinal))
                {
                    candidates.Add(MetadataMethodAddress.Create(targetReader, targetHandle));
                }
            }

            return candidates.Count switch
            {
                0 => new MethodCorrespondenceResult(
                    MethodCorrespondenceStatus.Absent,
                    anchor,
                    Target: null,
                    Candidates: [],
                    Failure: "no target method has the same structural identity"),
                1 => new MethodCorrespondenceResult(
                    MethodCorrespondenceStatus.Exact,
                    anchor,
                    candidates[0],
                    candidates,
                    Failure: null),
                _ => new MethodCorrespondenceResult(
                    MethodCorrespondenceStatus.Ambiguous,
                    anchor,
                    Target: null,
                    candidates,
                    Failure: $"{candidates.Count} target methods have the same structural identity"),
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return Failed($"{ex.GetType().Name}: {ex.Message}");
        }

        static MethodCorrespondenceResult Failed(string failure)
            => new(
                MethodCorrespondenceStatus.Failed,
                Anchor: null,
                Target: null,
                Candidates: [],
                failure);
    }

    static bool IsValid(MetadataReader reader, MethodDefinitionHandle handle)
    {
        if (handle.IsNil)
            return false;
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0 && row <= reader.GetTableRowCount(TableIndex.MethodDef);
    }

    static bool IsExtensionMethod(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method)
        => type.Attributes.HasFlag(TypeAttributes.Abstract)
           && type.Attributes.HasFlag(TypeAttributes.Sealed)
           && method.Attributes.HasFlag(MethodAttributes.Static)
           && AttributeReader.HasExtensionAttribute(reader, type.GetCustomAttributes())
           && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());
}
