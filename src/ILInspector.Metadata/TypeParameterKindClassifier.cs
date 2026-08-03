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

    /// <param name="chain">
    /// The answers for the parameter list <paramref name="handle"/> belongs to. One
    /// instance is meant to serve a whole list: `where T : U` makes the list a graph, and
    /// this holds the graph's answers, so it is resolved once rather than re-resolved from
    /// every parameter that reaches it. There is deliberately no overload that allocates
    /// one per call, so that cost cannot be reintroduced by accident.
    /// </param>
    public static TypeParameterTypeKind Classify(
        MetadataReader reader,
        GenericParameterHandle handle,
        bool hasValueTypeConstraint,
        bool hasReferenceTypeConstraint,
        ChainState chain)
    {
        // The attribute flags are decisive on their own and need no constraint types.
        if (hasValueTypeConstraint)
            return TypeParameterTypeKind.ValueType;
        if (hasReferenceTypeConstraint)
            return TypeParameterTypeKind.ReferenceType;

        return chain.Answer(reader, handle);
    }

    /// <summary>
    /// Reads one parameter into the facts the two closures need: whether it is settled by
    /// its own flags or by a constraint that proves reference-ness on its own, whether
    /// anything about it was unreadable, and which sibling parameters it defers to.
    /// </summary>
    /// <remarks>
    /// Unreadability is recorded rather than returned, so that a constraint this assembly
    /// cannot read does not hide a later one that proves reference-ness outright. A proof
    /// is a proof wherever it sits in the list, and nothing unreadable can unprove it;
    /// answering otherwise would make the verdict depend on constraint order.
    /// </remarks>
    static Node Describe(MetadataReader reader, GenericParameterHandle handle)
    {
        var node = new Node(handle);
        GenericParameter parameter;
        try
        {
            parameter = reader.GetGenericParameter(handle);
        }
        catch (BadImageFormatException)
        {
            node.Unreadable = true;
            return node;
        }

        var special = parameter.Attributes & GenericParameterAttributes.SpecialConstraintMask;
        if ((special & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
        {
            node.IsValueType = true;
            return node;
        }

        if ((special & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
        {
            node.ProvesReference = true;
            return node;
        }

        try
        {
            foreach (var constraintHandle in parameter.GetConstraints())
            {
                GenericParameterConstraint constraint;
                try
                {
                    constraint = reader.GetGenericParameterConstraint(constraintHandle);
                }
                catch (BadImageFormatException)
                {
                    node.Unreadable = true;
                    continue;
                }

                switch (ClassifyConstraintType(reader, constraint.Type))
                {
                    case ConstraintClass.ProvesReferenceType:
                        node.ProvesReference = true;
                        break;
                    case ConstraintClass.Unreadable:
                        node.Unreadable = true;
                        break;
                    case ConstraintClass.ProvesNothing:
                        break;

                    // `where T : U` -- T is exactly as known as U, so record the edge.
                    case ConstraintClass.DeferToTypeParameter:
                        if (SiblingHandle(reader, parameter, constraint.Type) is { } target)
                            node.Defers.Add(target);
                        else
                            node.Unreadable = true;
                        break;
                }
            }
        }
        catch (BadImageFormatException)
        {
            node.Unreadable = true;
        }

        return node;
    }

    /// <summary>
    /// The generic parameter that <paramref name="constraintType"/> names, so that
    /// `where T : U` can be recorded as an edge to U. Both parameters belong to the same
    /// declaration, so U is found among the siblings of <paramref name="parameter"/>
    /// rather than by resolving anything -- a method type parameter among the owning
    /// method's, a type type parameter among the declaring type's.
    /// </summary>
    /// <remarks>
    /// Yields null, and so fails closed, on anything unexpected: a signature that does not
    /// decode to a single parameter index, an index outside the owning collection, or an
    /// owner this assembly cannot read.
    /// </remarks>
    static GenericParameterHandle? SiblingHandle(
        MetadataReader reader,
        GenericParameter parameter,
        EntityHandle constraintType)
    {
        if (constraintType.Kind != HandleKind.TypeSpecification)
            return null;

        var reference = GuardedProviderDecode.TypeSpec(
            reader,
            (TypeSpecificationHandle)constraintType,
            TypeParameterReferenceProvider.Instance,
            (GenericContext?)null,
            fallback: null);
        if (reference is not { } target)
            return null;

        try
        {
            var siblings = SiblingParameters(reader, parameter, target.IsMethodParameter);
            if (siblings is not { } handles || target.Index < 0 || target.Index >= handles.Count)
                return null;

            return handles[target.Index];
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// One parameter as the constraint graph sees it.
    /// </summary>
    sealed class Node(GenericParameterHandle handle)
    {
        public GenericParameterHandle Handle { get; } = handle;

        /// <summary>The sibling parameters this one defers to, one entry per constraint.</summary>
        public List<GenericParameterHandle> Defers { get; } = [];

        /// <summary>Reference-ness is settled by this parameter alone, with no edge followed.</summary>
        public bool ProvesReference { get; set; }

        /// <summary>The value-type flag, which settles the parameter and admits no constraints.</summary>
        public bool IsValueType { get; set; }

        /// <summary>
        /// Something about this parameter could not be read, so it can never be proven to
        /// constrain nothing. Left unanswered by both closures, which is what fails it
        /// closed to <see cref="TypeParameterTypeKind.Undetermined"/>.
        /// </summary>
        public bool Unreadable { get; set; }
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
    /// The answers for one declaration's type parameters, and the resolution that computes
    /// them. A caller classifying a parameter list reuses one instance across it; answers
    /// outlive a single resolution, since a handle identifies the same parameter for the
    /// whole module.
    /// </summary>
    /// <remarks>
    /// `where T : U` makes a declaration's parameters a directed graph rather than a tree,
    /// and metadata can make that graph cyclic even though C# rejects it (CS0454). This
    /// resolves it as a graph -- two closures over explicit worklists, no recursion and no
    /// walk order -- so every answer is a function of the graph alone and all of them can
    /// be cached unconditionally.
    /// <para>
    /// That property is the point. An earlier design walked depth-first and cut the walk at
    /// a parameter already on the path, which made an answer depend on where the walk
    /// started and so forced a rule about which answers were safe to keep. It also turned
    /// depth into stack frames and repeated work into a budget to be rationed, and valid
    /// metadata could reach both bounds: a long chain overflowed the stack, and a wide
    /// acyclic graph exhausted the budget and lost a clause it had already proven. Neither
    /// bound exists here, because neither quantity is consumed.
    /// </para>
    /// </remarks>
    internal sealed class ChainState
    {
        readonly Dictionary<GenericParameterHandle, TypeParameterTypeKind> _answers = [];

        internal TypeParameterTypeKind Answer(MetadataReader reader, GenericParameterHandle handle)
        {
            if (_answers.TryGetValue(handle, out var answer))
                return answer;

            Resolve(reader, handle);

            // Resolve answers everything it reached, so the miss below cannot happen; it
            // fails closed rather than asserting.
            return _answers.TryGetValue(handle, out var resolved)
                ? resolved
                : TypeParameterTypeKind.Undetermined;
        }

        /// <summary>
        /// Answers <paramref name="root"/> and everything reachable from it, by resolving
        /// the constraint graph that contains it.
        /// </summary>
        void Resolve(MetadataReader reader, GenericParameterHandle root)
        {
            var nodes = Discover(reader, root);
            var predecessors = Predecessors(nodes);

            ProveReferenceTypes(nodes, predecessors);
            ProveConstrainsNothing(nodes, predecessors);

            // Whatever neither closure could prove. A parameter lands here by deferring,
            // however indirectly, to something unreadable or to a cycle -- in both cases
            // its answer is genuinely unknown, and both wrong guesses are compile errors
            // in the consumer, so it stays unclassified.
            foreach (var handle in nodes.Keys)
            {
                if (!_answers.ContainsKey(handle))
                    _answers[handle] = TypeParameterTypeKind.Undetermined;
            }
        }

        /// <summary>
        /// The parameters reachable from <paramref name="root"/> that are not answered
        /// already, read once each. Cycles terminate because a parameter is described
        /// before its edges are followed.
        /// </summary>
        Dictionary<GenericParameterHandle, Node> Discover(MetadataReader reader, GenericParameterHandle root)
        {
            var nodes = new Dictionary<GenericParameterHandle, Node>();
            var pending = new Stack<GenericParameterHandle>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                var handle = pending.Pop();
                if (_answers.ContainsKey(handle) || nodes.ContainsKey(handle))
                    continue;

                var node = Describe(reader, handle);
                nodes[handle] = node;
                foreach (var target in node.Defers)
                {
                    if (!_answers.ContainsKey(target) && !nodes.ContainsKey(target))
                        pending.Push(target);
                }
            }

            return nodes;
        }

        /// <summary>
        /// Reverses the edges, so a proof can be pushed to everything that defers to the
        /// parameter it was proven about. Edges leaving the discovered graph point at
        /// parameters already answered, so they are folded into the deferring node here as
        /// the constants they are.
        /// </summary>
        Dictionary<GenericParameterHandle, List<Node>> Predecessors(Dictionary<GenericParameterHandle, Node> nodes)
        {
            var predecessors = new Dictionary<GenericParameterHandle, List<Node>>();
            foreach (var node in nodes.Values)
            {
                foreach (var target in node.Defers)
                {
                    if (!nodes.ContainsKey(target))
                    {
                        switch (_answers.TryGetValue(target, out var settled)
                            ? settled
                            : TypeParameterTypeKind.Undetermined)
                        {
                            case TypeParameterTypeKind.ReferenceType:
                                node.ProvesReference = true;
                                break;
                            case TypeParameterTypeKind.NeitherReferenceNorValue:
                                break;

                            // A value-type parameter cannot be a constraint in C#, so
                            // this is malformed rather than a row of the table.
                            default:
                                node.Unreadable = true;
                                break;
                        }

                        continue;
                    }

                    if (!predecessors.TryGetValue(target, out var waiting))
                        predecessors[target] = waiting = [];

                    waiting.Add(node);
                }
            }

            return predecessors;
        }

        /// <summary>
        /// Settles every parameter the value-type flag settles, then spreads reference-ness
        /// backwards: a parameter that defers to one known to be a reference type is itself
        /// known to be one, however long the chain and whether or not it rejoins itself.
        /// </summary>
        void ProveReferenceTypes(
            Dictionary<GenericParameterHandle, Node> nodes,
            Dictionary<GenericParameterHandle, List<Node>> predecessors)
        {
            var proven = new Queue<Node>();
            foreach (var node in nodes.Values)
            {
                if (node.IsValueType)
                {
                    _answers[node.Handle] = TypeParameterTypeKind.ValueType;
                    continue;
                }

                if (node.ProvesReference)
                {
                    _answers[node.Handle] = TypeParameterTypeKind.ReferenceType;
                    proven.Enqueue(node);
                }
            }

            while (proven.Count > 0)
            {
                var settled = proven.Dequeue();
                if (!predecessors.TryGetValue(settled.Handle, out var waiting))
                    continue;

                foreach (var node in waiting)
                {
                    if (_answers.ContainsKey(node.Handle))
                        continue;

                    _answers[node.Handle] = TypeParameterTypeKind.ReferenceType;
                    proven.Enqueue(node);
                }
            }
        }

        /// <summary>
        /// Proves the remaining parameters constrain nothing, which -- unlike reference-ness
        /// -- takes agreement from every edge rather than one witness: a parameter
        /// constrains nothing only once all of its own deferrals are known to.
        /// </summary>
        /// <remarks>
        /// Counting down outstanding deferrals is what makes a cycle answer correctly
        /// without being detected as one. A parameter on a cycle can never reach zero,
        /// because that would require the cycle to have already been proven through itself,
        /// so it is left for the caller to fail closed. A parameter that reaches a cycle,
        /// or anything unreadable, is stranded the same way and for the same reason.
        /// </remarks>
        void ProveConstrainsNothing(
            Dictionary<GenericParameterHandle, Node> nodes,
            Dictionary<GenericParameterHandle, List<Node>> predecessors)
        {
            var outstanding = new Dictionary<GenericParameterHandle, int>();
            var proven = new Queue<Node>();
            foreach (var node in nodes.Values)
            {
                if (_answers.ContainsKey(node.Handle))
                    continue;

                int pending = 0;
                foreach (var target in node.Defers)
                {
                    if (!nodes.ContainsKey(target))
                        continue;

                    // Settled already, and only the value-type flag can have settled it:
                    // a parameter deferring to a proven reference type was itself proven
                    // one, so it is not here.
                    if (_answers.ContainsKey(target))
                    {
                        node.Unreadable = true;
                        continue;
                    }

                    pending++;
                }

                outstanding[node.Handle] = pending;
                if (pending == 0 && !node.Unreadable)
                    proven.Enqueue(node);
            }

            while (proven.Count > 0)
            {
                var settled = proven.Dequeue();
                _answers[settled.Handle] = TypeParameterTypeKind.NeitherReferenceNorValue;
                if (!predecessors.TryGetValue(settled.Handle, out var waiting))
                    continue;

                foreach (var node in waiting)
                {
                    if (_answers.ContainsKey(node.Handle)
                        || !outstanding.TryGetValue(node.Handle, out var pending))
                    {
                        continue;
                    }

                    outstanding[node.Handle] = --pending;
                    if (pending == 0 && !node.Unreadable)
                        proven.Enqueue(node);
                }
            }
        }
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
