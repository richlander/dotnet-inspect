using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// A classification the method-row gate applies: a scope over types and
/// rows, and a unit class for each in-scope row. It reads only the Tier 1
/// fields it declares. It is part of the source, not a producer, so a producer
/// guarded by it has no dependency.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/producer-planning.md#the-source-gate-owns-safety</c>.
/// </remarks>
public abstract class MethodRowClassifier
{
    const MethodDefinitionLayers Tier1 =
        MethodDefinitionLayers.Flags
        | MethodDefinitionLayers.NameComparison
        | MethodDefinitionLayers.SignatureShape;

    private protected MethodRowClassifier(string identity, MethodDefinitionLayers fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if ((fields & ~Tier1) != 0)
        {
            throw new ArgumentException(
                "A gate classifier reads only Tier 1 fields.",
                nameof(fields));
        }

        Identity = identity;
        Fields = fields;
    }

    /// <summary>Internal code identity, used in failures and receipts.</summary>
    public string Identity { get; }

    /// <summary>The Tier 1 fields this classifier reads.</summary>
    public MethodDefinitionLayers Fields { get; }

    /// <summary>Whether a type's rows are in the gate's scope. Outside it, no guarded producer sees them.</summary>
    internal abstract bool TypeInScope(scoped MethodRowTypeView type);

    /// <summary>The row's class, 0 to 63, or -1 when the row is outside the gate's scope.</summary>
    internal abstract int Classify(scoped MethodDefinitionView row);

    public override string ToString() => Identity;
}

/// <summary>
/// A scope guard on the source gate: the producer sees only rows the
/// classifier puts in one of the accepted classes, in types it admits.
/// </summary>
public sealed record SourceGateGuard(
    MethodRowClassifier Classifier,
    ulong AcceptedClasses);

/// <summary>Identity text of one row, from the gate's budgeted decoder.</summary>
public sealed record MethodRowIdentity(
    InertString MethodName,
    InertString DeclaringType,
    InertString Namespace,
    InertString Signature,
    MemberAnchor? Anchor,
    InertString? ReturnType);

/// <summary>
/// A scoped, read-only view of the type being visited, for a gate
/// classifier's type scope. It exposes only the fields the classifier declared.
/// </summary>
public readonly ref struct MethodRowTypeView
{
    readonly MethodRowGate _gate;
    readonly TypeDefinition _type;
    readonly MethodRowClassifier _owner;

    internal MethodRowTypeView(MethodRowGate gate, TypeDefinition type, MethodRowClassifier owner)
    {
        _gate = gate;
        _type = type;
        _owner = owner;
    }

    /// <summary>The type's attributes. Requires <see cref="MethodDefinitionLayers.Flags"/>.</summary>
    public TypeAttributes Attributes
    {
        get
        {
            Require(MethodDefinitionLayers.Flags);
            return _type.Attributes;
        }
    }

    /// <summary>Whether the type's name starts with <paramref name="prefix"/>, compared in place.</summary>
    public bool NameStartsWith(string prefix)
    {
        Require(MethodDefinitionLayers.NameComparison);
        return _gate.Reader.StringComparer.StartsWith(_type.Name, prefix);
    }

    void Require(MethodDefinitionLayers field)
    {
        if ((_owner.Fields & field) == 0)
            throw MethodRowGate.Undeclared(_owner.Identity, field);
    }
}

/// <summary>
/// The typed signal of a critical failure. It derives from no exception type
/// the recoverable-failure path catches, so no producer or visit can contain it.
/// </summary>
internal sealed class ProducerAbortException(CriticalFailure failure)
    : Exception(failure.Message)
{
    public CriticalFailure Failure { get; } = failure;
}

/// <summary>
/// The method-row gate: part of the method-definition source. It is the only
/// path by which a producer reads a row's fields. Tier 1 accessors charge no
/// budget and memoize every structural walk per handle and per blob for the
/// execution, with a per-row cap that aborts. Tier 2 identity text goes
/// through one budget per execution, armed only when the plan declares
/// <see cref="MethodDefinitionLayers.IdentityText"/>.
/// </summary>
internal sealed class MethodRowGate
{
    internal const string Owner = "MethodRowGate";
    internal const string SignatureShapeCap = "SignatureShapeCap";
    internal const string TypeSpecificationGuard = "TypeSpecificationGuard";
    internal const string IdentityWork = "IdentityWork";
    internal const string IdentityDecodeFailures = "IdentityDecodeFailures";

    readonly SignatureShapeWalker _signatures;
    readonly Dictionary<MethodRowClassifier, ClassifierCache> _classifiers = [];

    // The identity budget: legacy's per-scan budgets, per execution.
    int _identityWorkRemaining = MetadataSafetyPolicy.MaxClassificationScanWorkChars;
    int _identityDecodeFailures;

    TypeDefinitionHandle _typeHandle;
    TypeDefinition _typeDefinition;
    MethodDefinitionHandle _methodHandle;
    MethodDefinition _methodDefinition;
    int _rowToken;

    bool _identityRead;
    MethodRowIdentity? _identity;

    internal MethodRowGate(MetadataReader reader, bool identityBudgetArmed)
    {
        Reader = reader;
        IdentityBudgetArmed = identityBudgetArmed;
        _signatures = new SignatureShapeWalker(this);
    }

    public MetadataReader Reader { get; }

    public bool IdentityBudgetArmed { get; }

    public long IdentityWorkCharged =>
        MetadataSafetyPolicy.MaxClassificationScanWorkChars - (long)_identityWorkRemaining;

    public long SignatureShapeNodesWalked => _signatures.TotalNodes;

    internal void MoveTo(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition)
    {
        _typeHandle = typeHandle;
        _typeDefinition = typeDefinition;
        _methodHandle = methodHandle;
        _methodDefinition = methodDefinition;
        _rowToken = MetadataTokens.GetToken(methodHandle);
        _identityRead = false;
        _identity = null;
    }

    internal static ProducerContractException Undeclared(
        string owner,
        MethodDefinitionLayers field) =>
        new($"Producer '{owner}' did not declare the {field} field.");

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal void Abort(string budget, string message)
    {
        string unit = _methodHandle.IsNil
            ? "(type scope)"
            : LibraryMethodAnalysisRunner.MethodLabel(Reader, _typeHandle, _methodHandle);
        throw new ProducerAbortException(
            new CriticalFailure(Owner, budget, _rowToken, unit, message));
    }

    // ---- Classification ----

    internal bool TypeInScope(
        MethodRowClassifier classifier,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition)
    {
        ClassifierCache cache = CacheFor(classifier);
        if (cache.TypeHandle != typeHandle || cache.TypeHandle.IsNil)
        {
            cache.TypeHandle = typeHandle;
            cache.TypeFailure = null;
            try
            {
                cache.TypeInScope = classifier.TypeInScope(
                    new MethodRowTypeView(this, typeDefinition, classifier));
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
            {
                cache.TypeFailure = ex;
            }
        }

        if (cache.TypeFailure is { } failure)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        return cache.TypeInScope;
    }

    internal int ClassOf(MethodRowClassifier classifier, ref MethodDefinitionUnit unit)
    {
        ClassifierCache cache = CacheFor(classifier);
        if (cache.RowToken != _rowToken || _rowToken == 0)
        {
            cache.RowToken = _rowToken;
            cache.RowFailure = null;
            try
            {
                cache.RowClass = classifier.Classify(
                    new MethodDefinitionView(ref unit, classifier));
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(ex))
            {
                cache.RowFailure = ex;
            }
        }

        if (cache.RowFailure is { } failure)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        return cache.RowClass;
    }

    ClassifierCache CacheFor(MethodRowClassifier classifier)
    {
        if (!_classifiers.TryGetValue(classifier, out ClassifierCache? cache))
        {
            cache = new ClassifierCache();
            _classifiers[classifier] = cache;
        }

        return cache;
    }

    sealed class ClassifierCache
    {
        public TypeDefinitionHandle TypeHandle;
        public bool TypeInScope;
        public Exception? TypeFailure;
        public int RowToken;
        public int RowClass;
        public Exception? RowFailure;
    }

    // ---- Tier 1 ----

    internal MethodAttributes Attributes => _methodDefinition.Attributes;

    internal MethodImplAttributes ImplAttributes => _methodDefinition.ImplAttributes;

    internal bool NameStartsWith(string prefix) =>
        Reader.StringComparer.StartsWith(_methodDefinition.Name, prefix);

    internal bool DeclaringTypeNameStartsWith(string prefix) =>
        Reader.StringComparer.StartsWith(_typeDefinition.Name, prefix);

    internal bool SignatureHasPointer() => _signatures.MethodHasPointer(_methodDefinition);

    // ---- Tier 2 ----

    /// <summary>
    /// The row's identity text, decoded once per row through the identity
    /// budget. A row whose identity cannot be decoded keeps its name and
    /// gets the legacy fallback signature, and the failure counts against the
    /// decode-failure budget. Exhausting either budget aborts.
    /// </summary>
    internal MethodRowIdentity Identity()
    {
        if (_identityRead)
            return _identity!;

        MethodAnchorInfo? anchor;
        int workBefore = _identityWorkRemaining;
        try
        {
            anchor = MethodRowProjection.TryCreateMethodIdentity(
                Reader,
                _typeHandle,
                _methodDefinition,
                ref _identityDecodeFailures,
                ref _identityWorkRemaining);
        }
        catch (BadImageFormatException ex)
        {
            // MethodRowProjection throws only when a budget is exhausted.
            _ = workBefore;
            Abort(
                _identityDecodeFailures >= MetadataSafetyPolicy.MaxClassificationIdentityDecodeFailures
                    ? IdentityDecodeFailures
                    : IdentityWork,
                ex.Message);
            return null!;
        }

        string methodName = Reader.GetString(_methodDefinition.Name);
        string signature = MethodRowProjection.FormatSignatureOrFallback(
            Reader,
            _typeDefinition,
            _methodDefinition,
            methodName,
            anchor);
        _identity = new MethodRowIdentity(
            new InertString(TextPolicy.Field, methodName),
            new InertString(
                TextPolicy.Field,
                MethodRowProjection.FormatDeclaringTypeName(Reader, _typeHandle)),
            new InertString(TextPolicy.Field, Reader.GetString(_typeDefinition.Namespace)),
            new InertString(TextPolicy.Field, signature),
            anchor?.Anchor,
            anchor is null ? null : new InertString(TextPolicy.Field, anchor.ReturnType));
        _identityRead = true;
        return _identity;
    }

    /// <summary>The row's P/Invoke import module, or null when it has none.</summary>
    internal InertString? PInvokeModuleName() =>
        MethodRowProjection.GetPInvokeModuleName(Reader, _methodHandle) is { } module
            ? new InertString(TextPolicy.Field, module)
            : null;

    /// <summary>
    /// A yes/no pointer walk of method signatures. Each signature blob and
    /// each TypeSpec is walked at most once per execution, so total nodes are
    /// at most the size of the <c>#Blob</c> heap. Refusals where
    /// <see cref="TypeSpecGuard"/> or <see cref="SignatureBlobGuard"/> would
    /// refuse, and a row over the per-row cap, abort. The answer equals the
    /// legacy classification scan's pointer probe whenever no guard refuses.
    /// </summary>
    sealed class SignatureShapeWalker(MethodRowGate gate) : ISignatureTypeProvider<bool, object?>
    {
        readonly Dictionary<BlobHandle, bool> _methods = [];
        readonly Dictionary<TypeSpecificationHandle, SpecificationShape> _specifications = [];
        readonly HashSet<TypeSpecificationHandle> _inProgress = [];
        readonly List<(int Chain, int Bytes)> _frames = [];
        int _depth;
        int _bytes;
        int _rowNodes;

        public long TotalNodes { get; private set; }

        MetadataReader Reader => gate.Reader;

        public bool MethodHasPointer(MethodDefinition method)
        {
            BlobHandle signature = method.Signature;
            if (_methods.TryGetValue(signature, out bool known))
                return known;

            _rowNodes = 0;
            if (!SignatureBlobGuard.IsSafeToDecode(Reader, signature, SignatureBlobGuard.Kind.Method))
            {
                gate.Abort(
                    SignatureShapeCap,
                    "The method signature exceeds the structural limit of "
                    + $"{MetadataSafetyPolicy.MaxSignatureTypeNodes} type nodes or "
                    + $"{SignatureBlobGuard.DefaultMaxDepth} levels.");
            }

            BlobReader blob = Reader.GetBlobReader(signature);
            MethodSignature<bool> decoded =
                new SignatureDecoder<bool, object?>(this, Reader, null).DecodeMethodSignature(ref blob);
            bool hasPointer = decoded.ReturnType;
            foreach (bool parameter in decoded.ParameterTypes)
                hasPointer |= parameter;
            _methods[signature] = hasPointer;
            return hasPointer;
        }

        void Node()
        {
            TotalNodes++;
            if (++_rowNodes > MetadataSafetyPolicy.MaxSignatureTypeNodes)
            {
                gate.Abort(
                    SignatureShapeCap,
                    "The method signature's walk exceeds "
                    + $"{MetadataSafetyPolicy.MaxSignatureTypeNodes} type nodes.");
            }
        }

        public bool GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            Node();
            return false;
        }

        public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            Node();
            return false;
        }

        public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            Node();
            return false;
        }

        public bool GetTypeFromSpecification(
            MetadataReader reader,
            object? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            Node();
            if (_specifications.TryGetValue(handle, out SpecificationShape shape))
            {
                // Reuse is valid only where legacy's guard would admit the
                // whole expansion from the current re-entry context.
                if (_depth + shape.Chain > TypeSpecGuard.MaxDepth
                    || (long)_bytes + shape.Bytes > TypeSpecGuard.MaxCumulativeBytes)
                {
                    AbortGuard();
                }

                Report(shape);
                return shape.HasPointer;
            }

            if (_inProgress.Contains(handle) || _depth >= TypeSpecGuard.MaxDepth)
                AbortGuard();

            BlobHandle signature = reader.GetTypeSpecification(handle).Signature;
            int length = reader.GetBlobReader(signature).Length;
            if ((long)_bytes + length > TypeSpecGuard.MaxCumulativeBytes)
                AbortGuard();
            if (!SignatureBlobGuard.IsSafeToDecode(reader, signature, SignatureBlobGuard.Kind.TypeSpecification))
            {
                gate.Abort(
                    SignatureShapeCap,
                    "A TypeSpec signature exceeds the structural limit.");
            }

            _inProgress.Add(handle);
            _depth++;
            _bytes += length;
            _frames.Add((0, 0));
            bool hasPointer;
            (int Chain, int Bytes) children;
            try
            {
                BlobReader blob = reader.GetBlobReader(signature);
                hasPointer = new SignatureDecoder<bool, object?>(this, reader, context).DecodeType(ref blob);
            }
            finally
            {
                _inProgress.Remove(handle);
                _depth--;
                _bytes -= length;
                children = _frames[^1];
                _frames.RemoveAt(_frames.Count - 1);
            }

            shape = new SpecificationShape(hasPointer, 1 + children.Chain, length + children.Bytes);
            _specifications[handle] = shape;
            Report(shape);
            return hasPointer;
        }

        void Report(SpecificationShape shape)
        {
            if (_frames.Count == 0)
                return;
            (int chain, int bytes) = _frames[^1];
            _frames[^1] = (Math.Max(chain, shape.Chain), Math.Max(bytes, shape.Bytes));
        }

        void AbortGuard() =>
            gate.Abort(
                TypeSpecificationGuard,
                "The TypeSpec re-entry exceeds the depth or cumulative-byte "
                + "limit, or repeats a TypeSpec.");

        public bool GetSZArrayType(bool elementType)
        {
            Node();
            return elementType;
        }

        public bool GetArrayType(bool elementType, ArrayShape shape)
        {
            Node();
            return elementType;
        }

        public bool GetByReferenceType(bool elementType)
        {
            Node();
            return elementType;
        }

        public bool GetPointerType(bool elementType)
        {
            Node();
            return true;
        }

        public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments)
        {
            Node();
            if (genericType)
                return true;
            foreach (bool argument in typeArguments)
            {
                if (argument)
                    return true;
            }

            return false;
        }

        public bool GetGenericMethodParameter(object? context, int index)
        {
            Node();
            return false;
        }

        public bool GetGenericTypeParameter(object? context, int index)
        {
            Node();
            return false;
        }

        public bool GetFunctionPointerType(MethodSignature<bool> signature)
        {
            Node();
            return true;
        }

        public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired)
        {
            Node();
            return modifier || unmodifiedType;
        }

        public bool GetPinnedType(bool elementType)
        {
            Node();
            return elementType;
        }
    }

    readonly record struct SpecificationShape(bool HasPointer, int Chain, int Bytes);
}
