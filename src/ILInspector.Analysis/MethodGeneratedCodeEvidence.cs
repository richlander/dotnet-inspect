using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>Generated-code facts from one declaring-type chain.</summary>
internal readonly record struct DeclaringTypeGeneratedCodeEvidence(
    bool HasGeneratedCodeAttribute,
    bool DeclaringTypeChainComplete);

/// <summary>Source work performed to read generated-code evidence.</summary>
public sealed record MethodGeneratedCodeEvidenceWork(
    int MethodAttributeRows,
    int TypeDefinitionRows,
    int TypeAttributeRows,
    int RelationshipNodes)
{
    public static MethodGeneratedCodeEvidenceWork Empty { get; } =
        new(0, 0, 0, 0);

    public MethodGeneratedCodeEvidenceWork Since(
        MethodGeneratedCodeEvidenceWork before) =>
        new(
            MethodAttributeRows - before.MethodAttributeRows,
            TypeDefinitionRows - before.TypeDefinitionRows,
            TypeAttributeRows - before.TypeAttributeRows,
            RelationshipNodes - before.RelationshipNodes);

    public MethodGeneratedCodeEvidenceWork Plus(
        MethodGeneratedCodeEvidenceWork other) =>
        new(
            checked(MethodAttributeRows + other.MethodAttributeRows),
            checked(TypeDefinitionRows + other.TypeDefinitionRows),
            checked(TypeAttributeRows + other.TypeAttributeRows),
            checked(RelationshipNodes + other.RelationshipNodes));
}

internal sealed class MethodGeneratedCodeEvidenceLookup(MetadataReader reader)
{
    int _methodAttributeRows;
    int _typeDefinitionRows;
    int _typeAttributeRows;
    int _relationshipNodes;

    internal MethodGeneratedCodeEvidenceWork RecordedWork =>
        new(
            Volatile.Read(ref _methodAttributeRows),
            Volatile.Read(ref _typeDefinitionRows),
            Volatile.Read(ref _typeAttributeRows),
            Volatile.Read(ref _relationshipNodes));

    internal bool MethodHasGeneratedCodeAttribute(
        MethodDefinition methodDefinition)
    {
        return LibraryBodyPrimaryMetadataResolver
            .HasGeneratedCodeAttribute(
                reader,
                methodDefinition.GetCustomAttributes(),
                RecordMethodAttribute);
    }

    internal bool MethodHasCompilerGeneratedAttribute(
        MethodDefinition methodDefinition)
    {
        return LibraryBodyPrimaryMetadataResolver
            .HasCompilerGeneratedAttribute(
                reader,
                methodDefinition.GetCustomAttributes(),
                RecordMethodAttribute);
    }

    internal DeclaringTypeGeneratedCodeEvidence ReadDeclaringTypeEvidence(
        TypeDefinitionHandle typeHandle)
    {
        bool typeHasGeneratedCode = false;
        TypeDefinitionHandle current = typeHandle;
        int remaining = MetadataSafetyPolicy.MaxRelationshipNodes;
        while (!current.IsNil && remaining-- > 0)
        {
            Interlocked.Increment(ref _typeDefinitionRows);
            TypeDefinition type = reader.GetTypeDefinition(current);
            if (LibraryBodyPrimaryMetadataResolver
                .HasGeneratedCodeAttribute(
                    reader,
                    type.GetCustomAttributes(),
                    RecordTypeAttribute))
            {
                typeHasGeneratedCode = true;
                break;
            }

            Interlocked.Increment(ref _relationshipNodes);
            current = type.GetDeclaringType();
        }

        return new(
            typeHasGeneratedCode,
            DeclaringTypeChainComplete: current.IsNil
                || typeHasGeneratedCode);
    }

    void RecordMethodAttribute() =>
        Interlocked.Increment(ref _methodAttributeRows);

    void RecordTypeAttribute() =>
        Interlocked.Increment(ref _typeAttributeRows);
}
