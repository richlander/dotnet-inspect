using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

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
        | MethodDefinitionLayers.SignatureShape
        | MethodDefinitionLayers.AttributeTypeMatch;

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
/// <summary>
/// A gate classification as a struct, so a kernel specialized to it inlines
/// the classifier's scope and class tests. It reads rows only through the
/// views, which enforce its classifier's declared fields.
/// </summary>
public interface IMethodRowClassification
{
    bool TypeInScope(scoped MethodRowTypeView type);

    int Classify(scoped MethodDefinitionView row);
}

/// <summary>
/// A classifier whose tests are a struct classification: its identity and
/// declared fields are the classifier's, its answers the struct's.
/// </summary>
public abstract class MethodRowClassifier<TClassification> : MethodRowClassifier
    where TClassification : struct, IMethodRowClassification
{
    private protected MethodRowClassifier(string identity, MethodDefinitionLayers fields)
        : base(identity, fields)
    {
    }

    internal sealed override bool TypeInScope(scoped MethodRowTypeView type) =>
        default(TClassification).TypeInScope(type);

    internal sealed override int Classify(scoped MethodDefinitionView row) =>
        default(TClassification).Classify(row);
}

public sealed record SourceGateGuard(
    MethodRowClassifier Classifier,
    ulong AcceptedClasses);

/// <summary>Identity text of one row, from the gate's budgeted decoder.</summary>
public sealed record MethodRowIdentity(
    InertString MethodName,
    InertString DeclaringType,
    InertString Namespace,
    InertString Signature,
    MethodRowAnchor? Anchor,
    InertString? ReturnType);

/// <summary>
/// A row's member anchor. <see cref="Key"/> is the semantic identity, the key
/// used for correspondence and Findings, and is never display text. Every text
/// the gate returns for the anchor is inert.
/// </summary>
public sealed record MethodRowAnchor(
    MemberAnchor Key,
    InertString StableSelector,
    InertString CanonicalSignature,
    InertString TypeFullName,
    InertString MemberName);

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
    internal const string AttributeTypeChain = "AttributeTypeChain";

    readonly SignatureShapeWalker _signatures;
    readonly Dictionary<MethodRowClassifier, ClassifierCache> _classifiers = [];
    readonly Dictionary<(EntityHandle Constructor, MetadataTypeNameTarget Target), bool> _attributeConstructors = [];
    readonly Dictionary<(EntityHandle Type, MetadataTypeNameTarget Target), bool> _attributeTypes = [];
    readonly Dictionary<(BlobHandle Signature, MetadataTypeNameTarget Target), bool> _attributeTypeSpecs = [];

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

    readonly PEReader _peReader;
    MetadataReader? _identityReader;
    BudgetedStringDecoder? _identityDecoder;
    bool _identityExhausted;

    internal MethodRowGate(PEReader peReader, MetadataReader reader, bool identityBudgetArmed)
    {
        _peReader = peReader;
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

    /// <summary>
    /// Raises the critical failure. It is built from content-free coordinates
    /// only, the row's token and the budget's identity, so raising it reads no
    /// metadata after containment has already failed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal void Abort(string budget, string message)
    {
        string unit = _methodHandle.IsNil
            ? $"TypeDef 0x{MetadataTokens.GetToken(_typeHandle):X8}"
            : $"MethodDef 0x{_rowToken:X8}";
        throw new ProducerAbortException(
            new CriticalFailure(Owner, budget, _rowToken, unit, message));
    }

    // ---- Classification ----

    internal bool TypeInScope(
        MethodRowClassifier classifier,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition) =>
        TypeInScope(CacheFor(classifier), typeHandle, typeDefinition);

    /// <summary>
    /// The classifier's per-execution cache. A consumer resolves it once and
    /// passes it to <see cref="TypeInScope(ClassifierCache, TypeDefinitionHandle, TypeDefinition)"/>
    /// and <see cref="ClassOf(ClassifierCache, ref MethodDefinitionUnit)"/>, so
    /// no per-unit call looks the classifier up. Every consumer of one
    /// classifier resolves the same cache, so their answers are shared.
    /// </summary>
    internal ClassifierCache Resolve(MethodRowClassifier classifier) => CacheFor(classifier);

    /// <summary>How many times a classifier's cache was looked up in this execution.</summary>
    internal int CacheLookups { get; private set; }

    internal bool TypeInScope(
        ClassifierCache cache,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition)
    {
        MethodRowClassifier classifier = cache.Classifier;
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

    internal int ClassOf(MethodRowClassifier classifier, ref MethodDefinitionUnit unit) =>
        ClassOf(CacheFor(classifier), ref unit);

    internal int ClassOf(ClassifierCache cache, ref MethodDefinitionUnit unit)
    {
        MethodRowClassifier classifier = cache.Classifier;
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
        CacheLookups++;
        if (!_classifiers.TryGetValue(classifier, out ClassifierCache? cache))
        {
            cache = new ClassifierCache(classifier);
            _classifiers[classifier] = cache;
        }

        return cache;
    }

    internal sealed class ClassifierCache(MethodRowClassifier classifier)
    {
        public readonly MethodRowClassifier Classifier = classifier;
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

    /// <summary>
    /// Whether one of the row's custom attributes has the target type,
    /// checked in attribute order and stopping at the first match, as
    /// <c>AttributeReader.HasAttribute</c> does, but compared in place with no
    /// name materialized. Each constructor's answer and each attribute type's
    /// answer is memoized for the execution, and a TypeSpec parent's answer
    /// per signature blob. A nested chain that repeats a handle or exceeds its
    /// bound, or a TypeSpec blob over legacy's guard, aborts; an unreadable
    /// name is a recoverable failure.
    /// </summary>
    internal bool HasAttributeOfType(MetadataTypeNameTarget target)
    {
        foreach (CustomAttributeHandle handle in _methodDefinition.GetCustomAttributes())
        {
            EntityHandle constructor = Reader.GetCustomAttribute(handle).Constructor;
            if (!_attributeConstructors.TryGetValue((constructor, target), out bool matches))
            {
                matches = ConstructorTypeMatches(constructor, target);
                _attributeConstructors[(constructor, target)] = matches;
            }

            if (matches)
                return true;
        }

        return false;
    }

    bool ConstructorTypeMatches(EntityHandle constructor, MetadataTypeNameTarget target) =>
        constructor.Kind switch
        {
            HandleKind.MemberReference => TypeMatches(
                Reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
                target),
            HandleKind.MethodDefinition => TypeMatches(
                Reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
                target),
            _ => false,
        };

    bool TypeMatches(EntityHandle type, MetadataTypeNameTarget target)
    {
        if (type.IsNil)
            return false;
        if (_attributeTypes.TryGetValue((type, target), out bool known))
            return known;

        // TypeSpec aliases share a blob, and the answer depends only on the
        // blob, so each blob is read once however many TypeSpec rows name it.
        BlobHandle signature = type.Kind == HandleKind.TypeSpecification
            ? Reader.GetTypeSpecification((TypeSpecificationHandle)type).Signature
            : default;
        if (!signature.IsNil && _attributeTypeSpecs.TryGetValue((signature, target), out known))
        {
            _attributeTypes[(type, target)] = known;
            return known;
        }

        bool matches = MetadataTypeNameMatch.Matches(Reader, type, target) switch
        {
            MetadataTypeNameMatchResult.Match => true,
            MetadataTypeNameMatchResult.NoMatch => false,
            MetadataTypeNameMatchResult.Malformed => throw new BadImageFormatException(
                "An attribute type's name could not be read."),
            _ => AbortChain(),
        };
        _attributeTypes[(type, target)] = matches;
        if (!signature.IsNil)
            _attributeTypeSpecs[(signature, target)] = matches;
        return matches;
    }

    bool AbortChain()
    {
        Abort(
            AttributeTypeChain,
            "An attribute type's nested chain repeats a handle or exceeds "
            + $"{MetadataSafetyPolicy.MaxRelationshipNodes} nodes, or its TypeSpec "
            + $"blob exceeds {TypeSpecGuard.MaxCumulativeBytes} bytes or the signature shape bounds.");
        return false;
    }

    // ---- Tier 2 ----

    /// <summary>
    /// The row's identity text, decoded once per row through the identity
    /// budget. A row whose identity cannot be decoded keeps its name and
    /// gets the legacy fallback signature, and the failure counts against the
    /// decode-failure budget. Exhausting either budget aborts.
    /// </summary>
    /// <summary>
    /// A second reader over the same metadata whose string decoder charges
    /// every handle-to-text conversion to the identity budget before it
    /// allocates. Every identity-text path reads through it: method, type,
    /// namespace, parameter, generic-parameter, and module names, and the type
    /// names inside anchors and display signatures.
    /// </summary>
    MetadataReader IdentityReader
    {
        get
        {
            if (_identityReader is null)
            {
                // Building the reader decodes only the metadata root's
                // version string, which identity text never reads. The
                // decoder is armed after construction and skips that string,
                // so the header costs no identity budget and is not decoded
                // a second time. Any exhaustion is still translated here, so
                // every caller aborts.
                var decoder = new BudgetedStringDecoder(this);
                try
                {
                    _identityReader = _peReader.GetMetadataReader(
                        MetadataReaderOptions.Default,
                        decoder);
                }
                catch (Exception) when (_identityExhausted)
                {
                    AbortIfIdentityExhausted();
                    throw;
                }

                decoder.Armed = true;
                _identityDecoder = decoder;
            }

            return _identityReader;
        }
    }

    /// <summary>
    /// Charges <paramref name="amount"/> of identity text before it is
    /// materialized. Exhaustion is recorded, so it aborts even when a
    /// formatter's catch-all swallows the signal.
    /// </summary>
    void ChargeIdentityText(int amount)
    {
        if (_identityExhausted || amount >= _identityWorkRemaining)
        {
            _identityWorkRemaining = 0;
            _identityExhausted = true;
            throw new IdentityBudgetExhaustedException();
        }

        _identityWorkRemaining -= amount;
    }

    bool LegacyIdentityBudgetExhausted =>
        _identityWorkRemaining <= 0
        || _identityDecodeFailures >= MetadataSafetyPolicy.MaxClassificationIdentityDecodeFailures;

    /// <summary>Aborts if any identity-text read exhausted the budget.</summary>
    void AbortIfIdentityExhausted()
    {
        if (_identityExhausted)
            Abort(IdentityWork, "The identity work budget is exhausted.");
    }

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

        MetadataReader reader = IdentityReader;
        MethodDefinition method = reader.GetMethodDefinition(_methodHandle);
        TypeDefinition type = reader.GetTypeDefinition(_typeHandle);
        MethodAnchorInfo? anchor;
        try
        {
            anchor = MethodRowProjection.TryCreateMethodIdentity(
                reader,
                _typeHandle,
                method,
                ref _identityDecodeFailures,
                ref _identityWorkRemaining);
        }
        catch (Exception) when (_identityExhausted)
        {
            AbortIfIdentityExhausted();
            throw;
        }
        catch (BadImageFormatException) when (LegacyIdentityBudgetExhausted)
        {
            // MethodRowProjection throws when its own work or decode-failure
            // budget is exhausted; any other malformed metadata is a
            // recoverable failure of the producer reading identity text.
            bool failures = _identityDecodeFailures
                >= MetadataSafetyPolicy.MaxClassificationIdentityDecodeFailures;
            Abort(
                failures ? IdentityDecodeFailures : IdentityWork,
                failures
                    ? "The identity decode-failure budget is exhausted."
                    : "The identity work budget is exhausted.");
            throw;
        }

        AbortIfIdentityExhausted();
        string methodName;
        string signature;
        string declaringType;
        string @namespace;
        try
        {
            methodName = reader.GetString(method.Name);
            signature = MethodRowProjection.FormatSignatureOrFallback(
                reader,
                type,
                method,
                methodName,
                anchor);
            AbortIfIdentityExhausted();

            // The display signature is composed from parts the decoder
            // charged; composing it is identity work too.
            ChargeIdentityText(signature.Length);
            declaringType = MethodRowProjection.FormatDeclaringTypeName(reader, _typeHandle);
            AbortIfIdentityExhausted();
            @namespace = reader.GetString(type.Namespace);
        }
        catch (Exception) when (_identityExhausted)
        {
            // The decoder's signal, or whatever a Metadata formatter turned it
            // into: exhaustion is recorded, so it always aborts.
            AbortIfIdentityExhausted();
            throw;
        }

        _identity = new MethodRowIdentity(
            Inert(methodName),
            Inert(declaringType),
            Inert(@namespace),
            Inert(signature),
            anchor is null
                ? null
                : new MethodRowAnchor(
                    anchor.Anchor,
                    Inert(anchor.Anchor.StableSelector),
                    Inert(anchor.Anchor.CanonicalSignature),
                    Inert(anchor.Anchor.TypeFullName),
                    Inert(anchor.Anchor.MemberName)),
            anchor is null ? null : Inert(anchor.ReturnType));
        _identityRead = true;
        return _identity;
    }

    readonly Dictionary<StringHandle, InertString> _moduleNames = [];

    /// <summary>
    /// The row's P/Invoke import module, or null when it has none. Its text is
    /// identity text, read through the identity reader, so its length is
    /// charged before it is allocated; each string handle is decoded once.
    /// </summary>
    internal InertString? PInvokeModuleName()
    {
        MetadataReader reader = IdentityReader;
        ModuleReferenceHandle module = reader.GetMethodDefinition(_methodHandle).GetImport().Module;
        if (module.IsNil)
            return null;

        StringHandle name = reader.GetModuleReference(module).Name;
        if (_moduleNames.TryGetValue(name, out InertString known))
            return known;

        string text;
        try
        {
            text = reader.GetString(name);
        }
        catch (Exception) when (_identityExhausted)
        {
            AbortIfIdentityExhausted();
            throw;
        }

        InertString inert = Inert(text);
        _moduleNames[name] = inert;
        return inert;
    }

    /// <summary>Spells identity text inert, charging the spelled copy before it is built.</summary>
    InertString Inert(string text)
    {
        try
        {
            ChargeIdentityText(text.Length);
        }
        catch (IdentityBudgetExhaustedException)
        {
            AbortIfIdentityExhausted();
        }

        return new InertString(TextPolicy.Field, text);
    }

    /// <summary>The signal the budgeted decoder raises; the gate turns it into the abort.</summary>
    sealed class IdentityBudgetExhaustedException()
        : Exception("The identity work budget is exhausted.");

    /// <summary>
    /// Charges each string's encoded length to the identity budget before
    /// SRM decodes it.
    /// </summary>
    sealed class BudgetedStringDecoder(MethodRowGate gate)
        : MetadataStringDecoder(System.Text.Encoding.UTF8)
    {
        /// <summary>False while the reader is built: the header's version string is skipped.</summary>
        public bool Armed { get; set; }

        public override unsafe string GetString(byte* bytes, int byteCount)
        {
            if (!Armed)
                return string.Empty;
            gate.ChargeIdentityText(byteCount);
            return base.GetString(bytes, byteCount);
        }
    }

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
        readonly Dictionary<BlobHandle, SpecificationShape> _specificationBlobs = [];
        readonly HashSet<TypeSpecificationHandle> _inProgress = [];
        readonly HashSet<BlobHandle> _blobsInProgress = [];
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
            CheckShape(signature, SignatureBlobGuard.Kind.Method);

            BlobReader blob = Reader.GetBlobReader(signature);
            MethodSignature<bool> decoded =
                new SignatureDecoder<bool, object?>(this, Reader, null).DecodeMethodSignature(ref blob);
            bool hasPointer = decoded.ReturnType;
            foreach (bool parameter in decoded.ParameterTypes)
                hasPointer |= parameter;
            _methods[signature] = hasPointer;
            return hasPointer;
        }

        /// <summary>
        /// Exhausting a structural bound aborts; a malformed blob is a
        /// recoverable failure of the producer that read it.
        /// </summary>
        void CheckShape(BlobHandle signature, SignatureBlobGuard.Kind kind)
        {
            switch (SignatureBlobGuard.CheckShape(Reader, signature, kind))
            {
                case SignatureBlobGuard.ShapeCheck.Safe:
                    return;
                case SignatureBlobGuard.ShapeCheck.Malformed:
                    throw new BadImageFormatException("The signature is malformed.");
                default:
                    gate.Abort(
                        SignatureShapeCap,
                        "A signature exceeds the structural limit of "
                        + $"{MetadataSafetyPolicy.MaxSignatureTypeNodes} type nodes or "
                        + $"{SignatureBlobGuard.DefaultMaxDepth} levels.");
                    return;
            }
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
                return Reuse(shape);

            // Aliases share a blob, and a blob's expansion does not depend on
            // which handle reached it, so each blob is decoded once. The
            // handle-specific cycle check and the contextual limits still apply.
            BlobHandle signature = reader.GetTypeSpecification(handle).Signature;
            if (_inProgress.Contains(handle) || _blobsInProgress.Contains(signature))
                AbortGuard();
            if (_specificationBlobs.TryGetValue(signature, out shape))
            {
                _specifications[handle] = shape;
                return Reuse(shape);
            }

            if (_depth >= TypeSpecGuard.MaxDepth)
                AbortGuard();
            int length = reader.GetBlobReader(signature).Length;
            if ((long)_bytes + length > TypeSpecGuard.MaxCumulativeBytes)
                AbortGuard();
            CheckShape(signature, SignatureBlobGuard.Kind.TypeSpecification);

            _inProgress.Add(handle);
            _blobsInProgress.Add(signature);
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
                _blobsInProgress.Remove(signature);
                _depth--;
                _bytes -= length;
                children = _frames[^1];
                _frames.RemoveAt(_frames.Count - 1);
            }

            shape = new SpecificationShape(hasPointer, 1 + children.Chain, length + children.Bytes);
            _specifications[handle] = shape;
            _specificationBlobs[signature] = shape;
            Report(shape);
            return hasPointer;
        }

        /// <summary>
        /// Reuse is valid only where legacy's guard would admit the whole
        /// expansion from the current re-entry context.
        /// </summary>
        bool Reuse(SpecificationShape shape)
        {
            if (_depth + shape.Chain > TypeSpecGuard.MaxDepth
                || (long)_bytes + shape.Bytes > TypeSpecGuard.MaxCumulativeBytes)
            {
                AbortGuard();
            }

            Report(shape);
            return shape.HasPointer;
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
