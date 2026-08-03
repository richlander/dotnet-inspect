using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Decides whether a generic parameter's constraints prove it is a reference type, a
/// value type, or neither — C#'s "known to be a reference type" question, which the
/// constraint keywords alone cannot answer. A named class constraint proves
/// reference-ness with no keyword present, and <c>System.Enum</c> is the trap in the
/// other direction: it is a class, yet a parameter constrained to it may still be a
/// value type, so it proves nothing.
/// </summary>
/// <remarks>
/// Classification is fail-closed. Anything this assembly cannot read for itself — an
/// external <see cref="TypeReference"/> whose interface flag lives in another module, a
/// constraint naming another type parameter, or a signature the blob guards refused —
/// yields <see cref="TypeParameterTypeKind.Undetermined"/> rather than a guess, because
/// both wrong answers are compile errors in the consumer (CS8822 one way, CS8665 the
/// other).
/// </remarks>
internal static class TypeParameterKindClassifier
{
    /// <summary>
    /// Class types that are spellable as a constraint yet do not prove the parameter is
    /// a reference type. <c>System.Object</c> and <c>System.ValueType</c> are dropped
    /// from the constraint list before it reaches here; <c>System.Enum</c> survives and
    /// is the one that matters.
    /// </summary>
    static readonly string[] s_classesThatProveNothing =
        ["System.Object", "System.ValueType", "System.Enum"];

    public static TypeParameterTypeKind Classify(
        MetadataReader reader,
        GenericParameter parameter,
        bool hasValueTypeConstraint,
        bool hasReferenceTypeConstraint)
    {
        // The attribute flags are decisive on their own and need no constraint types.
        if (hasValueTypeConstraint)
            return TypeParameterTypeKind.ValueType;
        if (hasReferenceTypeConstraint)
            return TypeParameterTypeKind.ReferenceType;

        var kind = TypeParameterTypeKind.NeitherReferenceNorValue;
        foreach (var constraintHandle in parameter.GetConstraints())
        {
            GenericParameterConstraint constraint;
            try
            {
                constraint = reader.GetGenericParameterConstraint(constraintHandle);
            }
            catch (BadImageFormatException)
            {
                return TypeParameterTypeKind.Undetermined;
            }

            switch (ClassifyConstraintType(reader, constraint.Type))
            {
                // One class constraint settles it; nothing later can unprove it.
                case ConstraintClass.ProvesReferenceType:
                    return TypeParameterTypeKind.ReferenceType;
                case ConstraintClass.Unreadable:
                    kind = TypeParameterTypeKind.Undetermined;
                    break;
                case ConstraintClass.ProvesNothing:
                    break;
            }
        }

        return kind;
    }

    enum ConstraintClass
    {
        ProvesNothing,
        ProvesReferenceType,
        Unreadable,
    }

    static ConstraintClass ClassifyConstraintType(MetadataReader reader, EntityHandle handle)
    {
        if (handle.IsNil)
            return ConstraintClass.Unreadable;

        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return ClassifyDefinition(reader, (TypeDefinitionHandle)handle);

            // Another module owns the interface flag, and a name is not a substitute for
            // it: an unknown external type could be either.
            case HandleKind.TypeReference:
                return IsClassThatProvesNothing(TypeReferenceFullName(reader, (TypeReferenceHandle)handle))
                    ? ConstraintClass.ProvesNothing
                    : ConstraintClass.Unreadable;

            // A generic instantiation constrains to the instantiated type, so the
            // question is about its generic type definition.
            case HandleKind.TypeSpecification:
                return GuardedProviderDecode.TypeSpec(
                    reader,
                    (TypeSpecificationHandle)handle,
                    ConstraintRootProvider.Instance,
                    (GenericContext?)null,
                    fallback: ConstraintClass.Unreadable);

            default:
                return ConstraintClass.Unreadable;
        }
    }

    static ConstraintClass ClassifyDefinition(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition definition;
        string fullName;
        try
        {
            definition = reader.GetTypeDefinition(handle);
            fullName = TypeResolver.GetFullName(reader, definition);
        }
        catch (BadImageFormatException)
        {
            return ConstraintClass.Unreadable;
        }

        if ((definition.Attributes & TypeAttributes.Interface) != 0)
            return ConstraintClass.ProvesNothing;

        return IsClassThatProvesNothing(fullName)
            ? ConstraintClass.ProvesNothing
            : ConstraintClass.ProvesReferenceType;
    }

    static bool IsClassThatProvesNothing(string? fullName)
        => fullName is not null && Array.IndexOf(s_classesThatProveNothing, fullName) >= 0;

    static string? TypeReferenceFullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        try
        {
            return TypeResolver.GetFullName(reader, reader.GetTypeReference(handle));
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Classifies the type at the root of a constraint signature. Only the named-type
    /// and instantiation callbacks can be reached by a well-formed constraint; every
    /// other shape is not a legal constraint and is reported unreadable rather than
    /// guessed at.
    /// </summary>
    sealed class ConstraintRootProvider : ISignatureTypeProvider<ConstraintClass, GenericContext?>
    {
        public static ConstraintRootProvider Instance { get; } = new();

        public ConstraintClass GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => ClassifyDefinition(reader, handle);

        public ConstraintClass GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => IsClassThatProvesNothing(TypeReferenceFullName(reader, handle))
                ? ConstraintClass.ProvesNothing
                : ConstraintClass.Unreadable;

        public ConstraintClass GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
            => GuardedProviderDecode.TypeSpec(reader, handle, this, context, fallback: ConstraintClass.Unreadable);

        // A generic instantiation is classified by the type being instantiated.
        public ConstraintClass GetGenericInstantiation(ConstraintClass genericType, ImmutableArray<ConstraintClass> typeArguments)
            => genericType;

        public ConstraintClass GetModifiedType(ConstraintClass modifier, ConstraintClass unmodifiedType, bool isRequired)
            => unmodifiedType;

        public ConstraintClass GetPinnedType(ConstraintClass elementType) => elementType;

        // A constraint naming another type parameter is only as known as that parameter,
        // which this pass does not resolve.
        public ConstraintClass GetGenericMethodParameter(GenericContext? context, int index) => ConstraintClass.Unreadable;
        public ConstraintClass GetGenericTypeParameter(GenericContext? context, int index) => ConstraintClass.Unreadable;

        public ConstraintClass GetPrimitiveType(PrimitiveTypeCode typeCode) => ConstraintClass.Unreadable;
        public ConstraintClass GetSZArrayType(ConstraintClass elementType) => ConstraintClass.Unreadable;
        public ConstraintClass GetArrayType(ConstraintClass elementType, ArrayShape shape) => ConstraintClass.Unreadable;
        public ConstraintClass GetByReferenceType(ConstraintClass elementType) => ConstraintClass.Unreadable;
        public ConstraintClass GetPointerType(ConstraintClass elementType) => ConstraintClass.Unreadable;
        public ConstraintClass GetFunctionPointerType(MethodSignature<ConstraintClass> signature) => ConstraintClass.Unreadable;
    }
}
