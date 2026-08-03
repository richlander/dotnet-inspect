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
        => Classify(reader, parameter, hasValueTypeConstraint, hasReferenceTypeConstraint, new ChainState());

    /// <param name="chain">
    /// State for following `where T : U`, which makes T only as known as U. Carries the
    /// parameters on the *current* path, so a cyclic or self-referential chain -- which
    /// metadata can express even though C# cannot -- terminates, and the answers already
    /// computed, so a chain that reconverges is answered rather than re-walked.
    /// </param>
    internal static TypeParameterTypeKind Classify(
        MetadataReader reader,
        GenericParameter parameter,
        bool hasValueTypeConstraint,
        bool hasReferenceTypeConstraint,
        ChainState chain)
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

                // `where T : U` -- T is exactly as known as U, so follow the chain.
                case ConstraintClass.DeferToTypeParameter:
                    switch (ClassifySibling(reader, parameter, constraint.Type, chain))
                    {
                        case TypeParameterTypeKind.ReferenceType:
                            return TypeParameterTypeKind.ReferenceType;

                        // A value-type parameter cannot be a constraint in C#, so this
                        // is malformed rather than a row of the table; fail closed.
                        case TypeParameterTypeKind.ValueType:
                        case TypeParameterTypeKind.Undetermined:
                            kind = TypeParameterTypeKind.Undetermined;
                            break;
                        case TypeParameterTypeKind.NeitherReferenceNorValue:
                            break;
                    }

                    break;
            }
        }

        return kind;
    }

    /// <summary>
    /// Classifies the generic parameter that <paramref name="constraintType"/> names,
    /// so that `where T : U` inherits U's answer. Both parameters belong to the same
    /// declaration, so U is found among the siblings of <paramref name="parameter"/>
    /// rather than by resolving anything -- a method type parameter among the owning
    /// method's, a type type parameter among the declaring type's.
    /// </summary>
    /// <remarks>
    /// Fails closed on anything unexpected: a signature that does not decode to a single
    /// parameter index, an index outside the owning collection, an owner this assembly
    /// cannot read, or a chain that revisits a parameter it is already classifying.
    /// </remarks>
    static TypeParameterTypeKind ClassifySibling(
        MetadataReader reader,
        GenericParameter parameter,
        EntityHandle constraintType,
        ChainState chain)
    {
        if (constraintType.Kind != HandleKind.TypeSpecification)
            return TypeParameterTypeKind.Undetermined;

        var reference = GuardedProviderDecode.TypeSpec(
            reader,
            (TypeSpecificationHandle)constraintType,
            TypeParameterReferenceProvider.Instance,
            (GenericContext?)null,
            fallback: null);
        if (reference is not { } target)
            return TypeParameterTypeKind.Undetermined;

        try
        {
            var siblings = SiblingParameters(reader, parameter, target.IsMethodParameter);
            if (siblings is not { } handles || target.Index < 0 || target.Index >= handles.Count)
                return TypeParameterTypeKind.Undetermined;

            var siblingHandle = handles[target.Index];
            // Already answered on another branch. Reusing it is what keeps a chain that
            // reconverges from being mistaken for a cycle, and what keeps a long chain
            // linear instead of quadratic.
            if (chain.Answers.TryGetValue(siblingHandle, out var answered))
                return answered;

            // Only the current path guards against cycles, so a parameter is released
            // once its own subtree is done and stays reachable from a sibling branch.
            // A cyclic chain answers Undetermined, and caching that can carry the
            // cycle's verdict to a parameter that merely reaches it -- fail-closed in
            // the same direction the rest of this classifier takes, and unreachable
            // from C#, which cannot express a cyclic constraint chain at all.
            if (!chain.Path.Add(siblingHandle))
                return TypeParameterTypeKind.Undetermined;

            try
            {
                var sibling = reader.GetGenericParameter(siblingHandle);
                var special = sibling.Attributes & GenericParameterAttributes.SpecialConstraintMask;
                var kind = Classify(
                    reader,
                    sibling,
                    (special & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0,
                    (special & GenericParameterAttributes.ReferenceTypeConstraint) != 0,
                    chain);
                chain.Answers[siblingHandle] = kind;
                return kind;
            }
            finally
            {
                chain.Path.Remove(siblingHandle);
            }
        }
        catch (BadImageFormatException)
        {
            return TypeParameterTypeKind.Undetermined;
        }
    }

    /// <summary>
    /// The generic parameters a sibling reference indexes into: the owning method's when
    /// the reference is to a method type parameter, otherwise the declaring type's.
    /// </summary>
    static GenericParameterHandleCollection? SiblingParameters(
        MetadataReader reader,
        GenericParameter parameter,
        bool isMethodParameter)
    {
        switch (parameter.Parent.Kind)
        {
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)parameter.Parent);
                return isMethodParameter
                    ? method.GetGenericParameters()
                    : reader.GetTypeDefinition(method.GetDeclaringType()).GetGenericParameters();

            case HandleKind.TypeDefinition:
                // A type's own parameter cannot name a method parameter.
                return isMethodParameter
                    ? null
                    : reader.GetTypeDefinition((TypeDefinitionHandle)parameter.Parent).GetGenericParameters();

            default:
                return null;
        }
    }

    /// <summary>
    /// The two things following a `where T : U` chain needs: the parameters on the path
    /// currently being walked, and the answers already reached. Answers outlive a single
    /// walk -- a handle identifies the same parameter for the whole module -- so a caller
    /// classifying a parameter list reuses one instance across it.
    /// </summary>
    internal sealed class ChainState
    {
        public HashSet<GenericParameterHandle> Path { get; } = [];

        public Dictionary<GenericParameterHandle, TypeParameterTypeKind> Answers { get; } = [];
    }

    readonly record struct TypeParameterReference(int Index, bool IsMethodParameter);

    /// <summary>
    /// Decodes a constraint signature that is expected to be exactly one generic
    /// parameter reference, yielding null for every other shape so the caller fails
    /// closed rather than mistaking a composed type for a bare parameter.
    /// </summary>
    sealed class TypeParameterReferenceProvider
        : ISignatureTypeProvider<TypeParameterReference?, GenericContext?>
    {
        internal static readonly TypeParameterReferenceProvider Instance = new();

        public TypeParameterReference? GetGenericMethodParameter(GenericContext? context, int index)
            => new TypeParameterReference(index, IsMethodParameter: true);

        public TypeParameterReference? GetGenericTypeParameter(GenericContext? context, int index)
            => new TypeParameterReference(index, IsMethodParameter: false);

        public TypeParameterReference? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => null;
        public TypeParameterReference? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => null;
        public TypeParameterReference? GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind) => null;
        public TypeParameterReference? GetGenericInstantiation(TypeParameterReference? genericType, ImmutableArray<TypeParameterReference?> typeArguments) => null;
        public TypeParameterReference? GetModifiedType(TypeParameterReference? modifier, TypeParameterReference? unmodifiedType, bool isRequired) => null;
        public TypeParameterReference? GetPinnedType(TypeParameterReference? elementType) => null;
        public TypeParameterReference? GetPrimitiveType(PrimitiveTypeCode typeCode) => null;
        public TypeParameterReference? GetSZArrayType(TypeParameterReference? elementType) => null;
        public TypeParameterReference? GetArrayType(TypeParameterReference? elementType, ArrayShape shape) => null;
        public TypeParameterReference? GetByReferenceType(TypeParameterReference? elementType) => null;
        public TypeParameterReference? GetPointerType(TypeParameterReference? elementType) => null;
        public TypeParameterReference? GetFunctionPointerType(MethodSignature<TypeParameterReference?> signature) => null;
    }

    enum ConstraintClass
    {
        ProvesNothing,
        ProvesReferenceType,
        Unreadable,

        /// <summary>
        /// The constraint names another generic parameter, so the answer is that
        /// parameter's answer. Resolved by <see cref="ClassifySibling"/> rather than
        /// here, because the provider that decodes the signature sees only an index.
        /// </summary>
        DeferToTypeParameter,
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
            // it: an unknown external type could be either. The three core types that
            // prove nothing are the one exception, and even they are accepted only on
            // typed identity -- an assembly may declare its own `System.Enum`, and
            // treating that as the real one would emit `default` for a type parameter
            // that is genuinely known to be a reference type (CS8822).
            case HandleKind.TypeReference:
                var typeReference = reader.GetTypeReference((TypeReferenceHandle)handle);
                return IsClassThatProvesNothing(TypeReferenceFullName(reader, (TypeReferenceHandle)handle))
                    && ApiSurfaceExtractor.ResolvesThroughCoreLibrary(reader, typeReference.ResolutionScope)
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

        // Same-module, so the name is checked against the module's own identity rather
        // than against a resolution scope: an ordinary assembly may declare a type
        // called `System.Enum`, and that type is a plain class, so a parameter
        // constrained to it IS known to be a reference type.
        return IsClassThatProvesNothing(fullName) && DeclaresCoreLibraryRoot(reader)
            ? ConstraintClass.ProvesNothing
            : ConstraintClass.ProvesReferenceType;
    }

    /// <summary>
    /// True when the module being read is itself a core library — the only module whose
    /// own <c>System.Object</c>, <c>System.ValueType</c> and <c>System.Enum</c> are the
    /// special types C# treats as proving nothing about a type parameter.
    /// </summary>
    /// <remarks>
    /// A core library is recognized the way <c>ApiSurfaceExtractor</c> already
    /// recognizes the genuine root object: it declares a <c>System.Object</c> with no
    /// base type, which only the root can have. An assembly that merely declares
    /// <c>System.Enum</c> inherits its object from elsewhere and is rejected. An
    /// assembly that declares a nil-base <c>System.Object</c> is structurally a core
    /// library, and a compilation against it really does treat its <c>System.Enum</c> as
    /// the special type, so accepting that case tracks the compiler rather than
    /// trusting the assembly.
    /// </remarks>
    static bool DeclaresCoreLibraryRoot(MetadataReader reader)
        => s_coreLibraryRoots.GetValue(reader, static key => ScanForCoreLibraryRoot(key) ? s_true : s_false) == s_true;

    /// <summary>
    /// Memoized because the answer is a pure function of the module and the scan walks the
    /// whole type table: without this, a module pairing many constrained parameters with a
    /// large type table costs the product of the two. Measured on a synthesized module of
    /// 2,000 constrained parameters and 20,001 types, extraction fell from 0.74s to 0.21s.
    /// That cost reduction is measured, not gated -- no test pins it, because a module
    /// large enough to separate the two reliably would dominate the suite's run time. The
    /// classification itself is gated by
    /// <c>ConstraintRestatement_RejectsASameModuleCoreLibraryLookalike</c>.
    /// </summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MetadataReader, object> s_coreLibraryRoots = new();

    static readonly object s_true = new();

    static readonly object s_false = new();

    static bool ScanForCoreLibraryRoot(MetadataReader reader)
    {
        try
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                var candidate = reader.GetTypeDefinition(handle);
                if (candidate.BaseType.IsNil
                    && (candidate.Attributes & TypeAttributes.Interface) == 0
                    && string.Equals(reader.GetString(candidate.Namespace), "System", StringComparison.Ordinal)
                    && string.Equals(reader.GetString(candidate.Name), "Object", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (BadImageFormatException)
        {
            return false;
        }

        return false;
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

        // A constraint naming another type parameter is only as known as that parameter.
        // The index alone cannot be resolved here, so the answer is deferred to
        // ClassifySibling, which has the owning parameter and can find its siblings.
        public ConstraintClass GetGenericMethodParameter(GenericContext? context, int index) => ConstraintClass.DeferToTypeParameter;
        public ConstraintClass GetGenericTypeParameter(GenericContext? context, int index) => ConstraintClass.DeferToTypeParameter;

        public ConstraintClass GetPrimitiveType(PrimitiveTypeCode typeCode) => ConstraintClass.Unreadable;
        public ConstraintClass GetSZArrayType(ConstraintClass elementType) => ConstraintClass.Unreadable;
        public ConstraintClass GetArrayType(ConstraintClass elementType, ArrayShape shape) => ConstraintClass.Unreadable;
        public ConstraintClass GetByReferenceType(ConstraintClass elementType) => ConstraintClass.Unreadable;
        public ConstraintClass GetPointerType(ConstraintClass elementType) => ConstraintClass.Unreadable;
        public ConstraintClass GetFunctionPointerType(MethodSignature<ConstraintClass> signature) => ConstraintClass.Unreadable;
    }
}
