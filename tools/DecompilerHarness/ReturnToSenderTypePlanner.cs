using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.DecompilerHarness;

internal abstract record ArtifactRequest(
    string AssemblyPath,
    MetadataReader Reader,
    IrFunction Function,
    TypeDefinitionHandle TargetType,
    MethodDefinitionHandle TargetMethod,
    ProductTargetBody TargetBody,
    string FullType,
    string MethodName,
    int Overload,
    string SignatureText,
    IReadOnlySet<TypeDefinitionHandle> ClosureRoots,
    IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> ClosureFacts);

internal sealed record MethodArtifactRequest(
    string AssemblyPath,
    MetadataReader Reader,
    IrFunction Function,
    TypeDefinitionHandle TargetType,
    MethodDefinitionHandle TargetMethod,
    ProductTargetBody TargetBody,
    string FullType,
    string MethodName,
    int Overload,
    string SignatureText,
    IReadOnlySet<TypeDefinitionHandle> ClosureRoots,
    IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> ClosureFacts)
    : ArtifactRequest(
        AssemblyPath,
        Reader,
        Function,
        TargetType,
        TargetMethod,
        TargetBody,
        FullType,
        MethodName,
        Overload,
        SignatureText,
        ClosureRoots,
        ClosureFacts);

internal abstract record PropertyAccessorArtifactRequest(
    string AssemblyPath,
    MetadataReader Reader,
    IrFunction Function,
    TypeDefinitionHandle TargetType,
    MethodDefinitionHandle TargetMethod,
    PropertyDefinitionHandle TargetProperty,
    ProductTargetBody TargetBody,
    string FullType,
    string MethodName,
    int Overload,
    string SignatureText,
    IReadOnlySet<TypeDefinitionHandle> ClosureRoots,
    IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> ClosureFacts)
    : ArtifactRequest(
        AssemblyPath,
        Reader,
        Function,
        TargetType,
        TargetMethod,
        TargetBody,
        FullType,
        MethodName,
        Overload,
        SignatureText,
        ClosureRoots,
        ClosureFacts);

internal sealed record PropertyGetterArtifactRequest(
    string AssemblyPath,
    MetadataReader Reader,
    IrFunction Function,
    TypeDefinitionHandle TargetType,
    MethodDefinitionHandle TargetMethod,
    PropertyDefinitionHandle TargetProperty,
    ProductTargetBody TargetBody,
    string FullType,
    string MethodName,
    int Overload,
    string SignatureText,
    IReadOnlySet<TypeDefinitionHandle> ClosureRoots,
    IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> ClosureFacts)
    : PropertyAccessorArtifactRequest(
        AssemblyPath,
        Reader,
        Function,
        TargetType,
        TargetMethod,
        TargetProperty,
        TargetBody,
        FullType,
        MethodName,
        Overload,
        SignatureText,
        ClosureRoots,
        ClosureFacts);

internal sealed record PropertySetterArtifactRequest(
    string AssemblyPath,
    MetadataReader Reader,
    IrFunction Function,
    TypeDefinitionHandle TargetType,
    MethodDefinitionHandle TargetMethod,
    PropertyDefinitionHandle TargetProperty,
    ProductTargetBody TargetBody,
    string FullType,
    string MethodName,
    int Overload,
    string SignatureText,
    IReadOnlySet<TypeDefinitionHandle> ClosureRoots,
    IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> ClosureFacts)
    : PropertyAccessorArtifactRequest(
        AssemblyPath,
        Reader,
        Function,
        TargetType,
        TargetMethod,
        TargetProperty,
        TargetBody,
        FullType,
        MethodName,
        Overload,
        SignatureText,
        ClosureRoots,
        ClosureFacts);

internal sealed record EventAccessorArtifactRequest(
    string AssemblyPath,
    MetadataReader Reader,
    IrFunction Function,
    TypeDefinitionHandle TargetType,
    MethodDefinitionHandle TargetMethod,
    EventDefinitionHandle TargetEvent,
    ProductTargetBody TargetBody,
    string FullType,
    string MethodName,
    int Overload,
    string SignatureText,
    IReadOnlySet<TypeDefinitionHandle> ClosureRoots,
    IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> ClosureFacts,
    ProductTargetBody? SiblingAccessorBody = null)
    : ArtifactRequest(
        AssemblyPath,
        Reader,
        Function,
        TargetType,
        TargetMethod,
        TargetBody,
        FullType,
        MethodName,
        Overload,
        SignatureText,
        ClosureRoots,
        ClosureFacts);

internal sealed record ProductArtifact(
    ArtifactRequest Request,
    ProductTargetBody TargetBody,
    string Source,
    IReadOnlyList<CompileBackFact> SourceFacts,
    IReadOnlyList<CompileBackPlanningDiagnostic> Diagnostics,
    IReadOnlySet<TypeDefinitionHandle> ClosureRoots,
    CompileBackReconstructionPlan Plan)
{
    internal static ProductArtifact From(
        ArtifactRequest request,
        CompileBackSourceResult result,
        IReadOnlySet<TypeDefinitionHandle> closureRoots)
        => new(
            request,
            request.TargetBody,
            result.Source,
            result.Plan.Types
                .SelectMany(type => type.SourceFacts
                    .Concat(type.PrimaryConstructor?.FieldInitializers.SelectMany(member => member.SourceFacts) ?? [])
                    .Concat(type.RequiredMembers.SelectMany(member => member.SourceFacts)))
                .ToArray(),
            result.Plan.Diagnostics,
            closureRoots,
            result.Plan);
}

public sealed record CompileBackSourceResult(CompileBackReconstructionPlan Plan, string Source);

public sealed record CompileBackReconstructionPlan(
    string AssemblyPath,
    CompileBackMethodIdentity TargetMethod,
    CompileBackModuleRequirement Module,
    IReadOnlyList<CompileBackTypeRequirement> Types,
    IReadOnlyList<CSharpTypePrintRequest> PrintRequests,
    IReadOnlyList<CompileBackPlanningDiagnostic> Diagnostics);

public sealed record CompileBackMethodIdentity(
    string Type,
    string Method,
    int Overload,
    string Signature);

public sealed record CompileBackModuleRequirement(
    IReadOnlyList<string> Usings,
    IReadOnlyList<CompileBackAttributeRequirement> AssemblyAttributes,
    IReadOnlyList<CompileBackAttributeRequirement> ModuleAttributes);

public sealed record CompileBackAttributeRequirement(string Text, string Reason);

public enum CompileBackTypeKind
{
    Class,
    Record,
    Struct,
    Interface,
    Enum,
    Delegate,
}

public enum CompileBackMemberKind
{
    PropertyGet,
    PropertySet,
    EventAdd,
    EventRemove,
    Constructor,
    Method,
    Field,
}

public enum CompileBackAccessibility
{
    Public,
    Protected,
}

public enum CompileBackTypeSignatureKind
{
    Display,
    Definition,
}

public sealed record CompileBackTypeIdentity(string Namespace, string MetadataName, string DisplayName, string FullName, string MetadataFullName)
{
    public static CompileBackTypeIdentity FromDefinition(MetadataReader reader, TypeDefinition typeDef)
    {
        string metadataName = reader.GetString(typeDef.Name);
        string displayName = CSharpIdentifier.Sanitize(CSharpFormatter.StripArity(metadataName));
        if (!typeDef.GetDeclaringType().IsNil)
        {
            var declaring = FromDefinition(reader, reader.GetTypeDefinition(typeDef.GetDeclaringType()));
            return new CompileBackTypeIdentity(
                declaring.Namespace,
                metadataName,
                displayName,
                $"{declaring.FullName}.{displayName}",
                $"{declaring.MetadataFullName}.{metadataName}");
        }

        string ns = reader.GetString(typeDef.Namespace);
        string displayNamespace = CSharpFormatter.EscapeNamespace(ns);
        string fullName = displayNamespace.Length == 0 ? displayName : $"{displayNamespace}.{displayName}";
        string metadataFullName = ns.Length == 0 ? metadataName : $"{ns}.{metadataName}";
        return new CompileBackTypeIdentity(ns, metadataName, displayName, fullName, metadataFullName);
    }
}

public sealed record CompileBackTypeSignature(CompileBackTypeSignatureKind Kind, string DisplayName, CompileBackTypeIdentity? Identity)
{
    public static CompileBackTypeSignature Display(string text)
        => new(CompileBackTypeSignatureKind.Display, CSharpFormatter.CleanTypeDisplay(text), null);

    public static CompileBackTypeSignature Definition(CompileBackTypeIdentity identity)
        => new(CompileBackTypeSignatureKind.Definition, identity.FullName, identity);
}

public sealed record CompileBackParameter(
    string Name,
    CompileBackTypeSignature Type,
    string? Modifier = null,
    IReadOnlyList<string>? Attributes = null,
    bool HasDefault = false,
    string? DefaultValueText = null);

public sealed record CompileBackTypeParameter(
    string Name,
    IReadOnlyList<string> Constraints,
    string? Variance = null,
    IReadOnlyList<TypeParameterConstraint>? StructuredConstraints = null);

public enum CompileBackStubBodyKind
{
    None,
    Throw,
    ThrowGetSet,
    TargetBody,
    TargetGetterWithSetter,
    TargetSetterWithGetter,
    TargetEventAccessorWithSibling,
    AutoProperty,
    AutoPropertyGetSet,
    FieldInitializer,
}

public sealed record CompileBackFact(string Producer, string Id, string Detail);

public sealed record CompileBackPrimaryConstructor(
    string Parameters,
    IReadOnlyList<CompileBackParameter> ParameterList,
    IReadOnlyList<CompileBackMemberRequirement> FieldInitializers);

public sealed record CompileBackTypeRequirement(
    CompileBackTypeIdentity Type,
    CompileBackTypeKind RequiredKind,
    IReadOnlyList<CompileBackMemberRequirement> RequiredMembers,
    CompileBackPrimaryConstructor? PrimaryConstructor,
    IReadOnlyList<CompileBackFact> SourceFacts)
{
    public string Namespace => Type.Namespace;
    public string Name => Type.DisplayName;
    public CompileBackTypeKind Kind => RequiredKind;
    public IReadOnlyList<CompileBackMemberRequirement> Members => RequiredMembers;
    public bool IncludeMemberSurface { get; init; }
}

public sealed record CompileBackMemberRequirement(
    CompileBackMethodIdentity Identity,
    CompileBackMemberKind Kind,
    bool IsStatic,
    IReadOnlyList<CompileBackParameter> Parameters,
    CompileBackTypeSignature? ReturnType,
    IReadOnlyList<CompileBackTypeParameter> TypeParameters,
    CompileBackStubBodyKind StubBody,
    string? TargetBody,
    IReadOnlyList<CompileBackFact> SourceFacts,
    IReadOnlyList<string>? Attributes = null,
    IReadOnlyList<string>? ReturnAttributes = null,
    bool IsAbstract = false,
    bool IsVirtual = false,
    bool IsOverride = false,
    bool IsSealed = false,
    bool IsAsync = false,
    bool IsExtension = false,
    CompileBackAccessibility Accessibility = CompileBackAccessibility.Public,
    string? ConstructorInitializer = null,
    string? ExplicitInterfaceMemberName = null,
    string? DeclarationSignature = null,
    bool RequiresUnsafeModifier = false,
    string? SiblingTargetBody = null)
{
    public string Name => Identity.Method;
    public string Type => ReturnType?.DisplayName ?? "";
    public string Body => TargetBody ?? "";
}

public sealed record CompileBackPlanningDiagnostic(string Layer, string Reason, string Detail);

internal sealed record ProductTargetBody(
    string Source,
    IReadOnlyList<DecompilerDecision> Decisions,
    string? ConstructorChain = null);

internal sealed record ExplicitInterfaceEventInfo(
    TypeDefinitionHandle InterfaceType,
    EventDefinitionHandle InterfaceEvent,
    string QualifiedName,
    string AccessorName);

public static class CompileBackSourceComposer
{
    internal static ProductTargetBody CreateTargetBody(
        MetadataSource source,
        MethodDefinitionHandle methodHandle,
        string fullType,
        string methodName,
        out IrFunction function)
    {
        var produced = MemberBodyProducer.ProduceBody(
            source,
            MetadataMethodAddress.Create(source.Reader, methodHandle));
        if (produced.Status != MemberBodyProductionStatus.Complete
            || produced.Body is null
            || produced.RaisedFunction is null)
        {
            string detail = produced.Projection.Diagnostics.Count == 0
                ? produced.Status.ToString()
                : string.Join("; ", produced.Projection.Diagnostics);
            throw new InvalidOperationException($"Could not produce {fullType}::{methodName}: {detail}.");
        }
        function = produced.RaisedFunction;
        // The printer lifts an explicit base(...)/this(...) constructor-chain call
        // out of the body into ConstructorChain (chain calls are invalid as body
        // statements). Carry it so the reconstructed target constructor re-emits
        // the initializer; dropping it silently compiles an empty body and loses
        // the constructor-chain opcodes (issue #2678).
        return new ProductTargetBody(
            produced.Body.Source,
            produced.Projection.Decisions,
            produced.Projection.ConstructorChain);
    }

    // ReferencedNamespaces already returns an ordinal-sorted set; route "System"
    // through the same set instead of an unconditional Prepend so a body that
    // already references a System namespace doesn't emit a duplicate using
    // (issue #2848). Ordinal ordering is preserved for the merged result.
    static string[] BuildUsings(IrFunction function)
    {
        var namespaces = new SortedSet<string>(MemberBodyFacts.ReferencedNamespaces(function), StringComparer.Ordinal)
        {
            "System",
        };
        return namespaces.ToArray();
    }

    internal static ProductArtifact Compose(ArtifactRequest request)
    {
        var closure = CreateClosureInputs(request);
        var result = request switch
        {
            PropertyGetterArtifactRequest getter => ComposePropertyGetter(
                request.AssemblyPath,
                request.Reader,
                request.Function,
                request.TargetType,
                getter.TargetProperty,
                request.TargetMethod,
                request.TargetBody.Source,
                request.FullType,
                request.MethodName,
                request.Overload,
                request.SignatureText,
                closure.Roots,
                closure.Facts,
                closure.MemberRequirements),
            PropertySetterArtifactRequest setter => ComposePropertySetter(
                request.AssemblyPath,
                request.Reader,
                request.Function,
                request.TargetType,
                setter.TargetProperty,
                request.TargetMethod,
                request.TargetBody.Source,
                request.FullType,
                request.MethodName,
                request.Overload,
                request.SignatureText,
                closure.Roots,
                closure.Facts,
                closure.MemberRequirements),
            EventAccessorArtifactRequest eventAccessor => ComposeEventAccessor(
                request.AssemblyPath,
                request.Reader,
                request.Function,
                request.TargetType,
                eventAccessor.TargetEvent,
                request.TargetMethod,
                request.TargetBody.Source,
                request.FullType,
                request.MethodName,
                request.Overload,
                request.SignatureText,
                closure.Roots,
                closure.Facts,
                closure.MemberRequirements,
                eventAccessor.SiblingAccessorBody?.Source),
            MethodArtifactRequest => ComposeMethod(
                request.AssemblyPath,
                request.Reader,
                request.Function,
                request.TargetType,
                request.TargetMethod,
                request.TargetBody.Source,
                request.FullType,
                request.MethodName,
                request.Overload,
                request.SignatureText,
                closure.Roots,
                closure.Facts,
                closure.MemberRequirements,
                request.TargetBody.ConstructorChain),
            _ => throw new ArgumentException($"Unknown artifact request type '{request.GetType().FullName}'.", nameof(request)),
        };

        return ProductArtifact.From(request, result, closure.Roots);
    }

    public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodRef method)
        => TypeProducer.TryCreateClosureMemberRequirement(reader, typeHandle, method);

    public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        FieldRef field)
        => TypeProducer.TryCreateClosureMemberRequirement(reader, typeHandle, field);

    sealed class ArtifactClosureInputs
    {
        public HashSet<TypeDefinitionHandle> Roots { get; } = [];
        public Dictionary<TypeDefinitionHandle, List<CompileBackFact>> Facts { get; } = [];
        public Dictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> MemberRequirements { get; } = [];
    }

    static ArtifactClosureInputs CreateClosureInputs(ArtifactRequest request)
    {
        var closure = new ArtifactClosureInputs();
        var targetRoot = TopLevelRootOf(request.Reader, request.TargetType);
        closure.Roots.Add(targetRoot);
        SeedTypedClosureRoots(
            request.Reader,
            request.Function,
            request.TargetType,
            request.TargetMethod,
            targetRoot,
            closure.Roots,
            closure.Facts,
            closure.MemberRequirements);
        foreach (var root in request.ClosureRoots)
            closure.Roots.Add(root);
        foreach (var (root, facts) in request.ClosureFacts)
        {
            foreach (var fact in facts)
                AddClosureFact(closure.Facts, root, fact);
        }

        return closure;
    }

    static void SeedTypedClosureRoots(
        MetadataReader reader,
        IrFunction function,
        TypeDefinitionHandle targetType,
        MethodDefinitionHandle targetMethod,
        TypeDefinitionHandle targetRoot,
        HashSet<TypeDefinitionHandle> closureRoots,
        Dictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        Dictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements)
    {
        // Canonicalize the local assembly name so TryResolveHandle's same-assembly
        // gate matches TypeRef.Assembly, which TypeRefDecoder canonicalizes (corelib
        // facades collapse to one identity). Without this, a target assembly whose
        // own name is a canonicalized facade (System.Runtime, mscorlib, ...) would
        // fail to resolve its own definitions and drop their closure roots/facts.
        string assemblyName = TypeRefDecoder.Canonical(reader.GetString(reader.GetAssemblyDefinition().Name));
        var definitions = TypeDefinitionsByTypeRefIdentity(reader);
        var consumedMemberEvidence = new List<ConsumedMemberEvidence>();
        AddTargetInterfaceRoots(targetType);
        foreach (var node in function.Descendants.Prepend(function))
        {
            foreach (var type in node.DirectTypes)
                AddTypedClosureRoot(type);
            if (node is IrExpression expression)
                AddTypedClosureRoot(expression.ResultType);
            AddTypedClosureMemberFacts(node);
        }

        void AddTypedClosureRoot(TypeRef? type)
        {
            switch (type?.Kind)
            {
                case TypeRefKind.Definition:
                    if (TryResolveRoot(type) is not { } root)
                        return;
                    if (root == targetRoot)
                        return;
                    closureRoots.Add(root);
                    AddClosureFact(
                        closureFacts,
                        root,
                        new CompileBackFact("metadata", "body-type", TypeRefIdentityKey(type.Namespace, type.Name, separator: ".")));
                    break;
                case TypeRefKind.GenericInstance:
                    AddTypedClosureRoot(type.ElementType);
                    foreach (var argument in type.TypeArguments)
                        AddTypedClosureRoot(argument);
                    break;
                case TypeRefKind.SzArray or TypeRefKind.Array
                    or TypeRefKind.ByRef or TypeRefKind.Pointer or TypeRefKind.Pinned:
                    AddTypedClosureRoot(type.ElementType);
                    break;
                case TypeRefKind.FunctionPointer:
                    AddTypedClosureRoot(type.ElementType);
                    foreach (var argument in type.TypeArguments)
                        AddTypedClosureRoot(argument);
                    break;
            }
        }

        void AddTargetInterfaceRoots(TypeDefinitionHandle handle)
        {
            // Interface discovery is product knowledge: the decompiler decodes the
            // target type's same-assembly interface definitions to typed refs. RTS
            // keeps only closure-root bookkeeping — resolve each interface definition
            // (TryResolveHandle applies the same-assembly + supported-root gates) and
            // seed it as a root, matching the prior TypeDefinition-only walk.
            foreach (var interfaceType in IrImporter.ImportImplementedInterfaces(reader, handle))
            {
                if (TryResolveHandle(interfaceType) is not { } interfaceHandle)
                    continue;

                var root = TopLevelRootOf(reader, interfaceHandle);
                if (root == targetRoot)
                    continue;

                var interfaceDef = reader.GetTypeDefinition(interfaceHandle);
                closureRoots.Add(root);
                AddClosureFact(
                    closureFacts,
                    root,
                    new CompileBackFact("metadata", "target-interface", reader.GetFullTypeName(interfaceDef)));
            }
        }

        TypeDefinitionHandle? TryResolveRoot(TypeRef? type)
            => TryResolveHandle(type) is { } handle
                ? TopLevelRootOf(reader, handle)
                : null;

        TypeDefinitionHandle? TryResolveHandle(TypeRef? type)
        {
            if (type is not { Kind: TypeRefKind.Definition } || type.Assembly != assemblyName)
                return null;
            string key = TypeRefIdentityKey(type.Namespace, type.Name);
            if (!definitions.TryGetValue(key, out var handle))
                return null;
            return IsSupportedTypedClosureRoot(reader, reader.GetTypeDefinition(handle)) ? handle : null;
        }

        void AddMemberFact(TypeRef declaringType, string kind, string name)
        {
            var definition = declaringType.Kind == TypeRefKind.GenericInstance
                ? declaringType.ElementType ?? declaringType
                : declaringType;
            if (TryResolveHandle(definition) is not { } handle)
                return;
            var root = TopLevelRootOf(reader, handle);
            if (root == targetRoot && handle == root)
                return;
            AddClosureFact(
                closureFacts,
                handle,
                new CompileBackFact("metadata", "typed-member-ref", $"{kind}: {TypeRefIdentityKey(definition.Namespace, definition.Name, separator: ".")}.{name}"));
        }

        void AddMethodFact(MethodRef method, bool allowTargetRoot = false)
        {
            AddSingleMethodFact(method, allowTargetRoot);
            if (OperatorNames.UncheckedOperator(method.Name) is { } siblingName)
                AddSingleMethodFact(method with { Name = siblingName }, allowTargetRoot);
        }

        void AddSingleMethodFact(MethodRef method, bool allowTargetRoot)
        {
            AddMemberFact(method.DeclaringType, "method", method.Name);
            // A self-reference to the target method itself (e.g. a recursive call)
            // must never spawn a closure member requirement: the target method is
            // already emitted with its real body, and a second hollow `throw null`
            // stub of the same signature produces a CS0111 duplicate-member break.
            // Sibling members on the target type are still reconstructed (they are
            // distinct handles); only the target method's own handle is excluded.
            if (ResolvesToTargetMethod(method))
                return;
            AddMemberRequirement(
                method.DeclaringType,
                root => TryCreateClosureMemberRequirement(reader, root, method),
                allowTargetRoot);
        }

        bool ResolvesToTargetMethod(MethodRef method)
        {
            var definition = method.DeclaringType.Kind == TypeRefKind.GenericInstance
                ? method.DeclaringType.ElementType ?? method.DeclaringType
                : method.DeclaringType;
            if (TryResolveHandle(definition) is not { } handle || handle != targetType)
                return false;
            return TypeProducer.TryFindMethod(reader, reader.GetTypeDefinition(targetType), method) == targetMethod;
        }

        void AddFieldFact(FieldRef field)
        {
            AddTypedClosureRoot(field.Type);
            AddMemberFact(field.DeclaringType, "field", field.Name);
            AddMemberRequirement(
                field.DeclaringType,
                root => TryCreateClosureMemberRequirement(reader, root, field),
                allowTargetRoot: true);
        }

        void AddRecordShellFact(TypeRef? type)
        {
            var definition = type?.Kind == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
            if (TryResolveHandle(definition) is not { } handle)
                return;
            var root = TopLevelRootOf(reader, handle);
            closureRoots.Add(root);
            AddClosureFact(
                closureFacts,
                handle,
                new CompileBackFact("metadata", "record-shell", TypeRefIdentityKey(definition!.Namespace, definition.Name, separator: ".")));
        }

        void AddMemberRequirement(TypeRef declaringType, Func<TypeDefinitionHandle, CompileBackMemberRequirement?> create, bool allowTargetRoot)
        {
            var definition = declaringType.Kind == TypeRefKind.GenericInstance
                ? declaringType.ElementType ?? declaringType
                : declaringType;
            if (TryResolveHandle(definition) is not { } handle)
                return;
            var root = TopLevelRootOf(reader, handle);
            if (root == targetRoot && handle == root && !allowTargetRoot)
                return;
            if (create(handle) is not { } requirement)
                return;
            if (!closureMemberRequirements.TryGetValue(handle, out var requirements))
                closureMemberRequirements[handle] = requirements = [];
            if (!requirements.Any(existing => existing.Kind == requirement.Kind && existing.Identity == requirement.Identity))
                requirements.Add(requirement);
        }

        void AddTypedClosureMemberFacts(IrNode node)
        {
            consumedMemberEvidence.Clear();
            ConsumedMemberEvidence.AddFrom(node, consumedMemberEvidence);
            foreach (var evidence in consumedMemberEvidence)
            {
                if (evidence.Method is { } method)
                    AddMethodFact(method, evidence.EffectiveAllowTargetRoot);
                if (evidence.Field is { } field)
                    AddFieldFact(field);
                if (evidence.RecordShellType is { } recordShell)
                    AddRecordShellFact(recordShell);
            }
        }
    }

    static Dictionary<string, TypeDefinitionHandle> TypeDefinitionsByTypeRefIdentity(MetadataReader reader)
    {
        var definitions = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (!IsSupportedTypedClosureRoot(reader, typeDef))
                continue;
            var (ns, name) = TypeRefIdentity(reader, handle);
            definitions.TryAdd(TypeRefIdentityKey(ns, name), handle);
        }

        return definitions;
    }

    static (string Namespace, string Name) TypeRefIdentity(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        string name = reader.GetString(typeDef.Name);
        if (!typeDef.IsNested)
            return (reader.GetString(typeDef.Namespace), name);

        var declaring = TypeRefIdentity(reader, typeDef.GetDeclaringType());
        return (declaring.Namespace, $"{declaring.Name}+{name}");
    }

    static bool IsSupportedTypedClosureRoot(MetadataReader reader, TypeDefinition typeDef)
    {
        string name = reader.GetString(typeDef.Name);
        return name is not "<Module>"
            && !name.Contains('<', StringComparison.Ordinal)
            && !IsDelegate(reader, typeDef);
    }

    static string TypeRefIdentityKey(string ns, string name, string separator = "|")
        => ns.Length == 0 ? name : $"{ns}{separator}{name}";

    static void AddClosureFact(
        Dictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        TypeDefinitionHandle root,
        CompileBackFact fact)
    {
        if (!closureFacts.TryGetValue(root, out var facts))
            closureFacts[root] = facts = [];
        if (!facts.Contains(fact))
            facts.Add(fact);
    }

    public static CompileBackSourceResult ComposePropertyGetter(
        string assemblyPath,
        MetadataReader reader,
        IrFunction function,
        TypeDefinitionHandle targetType,
        PropertyDefinitionHandle targetProperty,
        MethodDefinitionHandle targetGetter,
        string targetBody,
        string fullType,
        string methodName,
        int overload,
        string signatureText,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements)
    {
        var targetTypeDef = reader.GetTypeDefinition(targetType);
        var property = reader.GetPropertyDefinition(targetProperty);
        var getter = reader.GetMethodDefinition(targetGetter);
        var signature = GuardedSignatureText.PropertyText(reader, property, GenericContext.ForType(reader, targetTypeDef));
        var getterSignature = GuardedSignatureText.MethodText(reader, getter, GenericContext.ForMethod(reader, targetTypeDef, getter));
        var propertyDeclaration = MetadataDeclarationQuery.GetProperty(reader, targetTypeDef, property);
        var accessors = property.GetAccessors();
        var targetIdentity = CompileBackTypeIdentity.FromDefinition(reader, targetTypeDef);
        string metadataPropertyName = reader.GetString(property.Name);
        string propertyName = Identifier(metadataPropertyName);
        string? explicitInterfaceMemberName = ExplicitInterfaceMemberName(reader, metadataPropertyName);
        var returnType = CompileBackTypeSignature.Display(signature.ReturnType);
        bool targetIsAutoProperty = IsAutoProperty(reader, targetTypeDef, property, targetGetter, returnType.DisplayName);

        var diagnostics = new List<CompileBackPlanningDiagnostic>();
        var targetRoot = TopLevelRootOf(reader, targetType);
        var targetFacts = new List<CompileBackFact>
        {
            new("metadata", "target-type", targetIdentity.FullName),
        };
        if (closureFacts.TryGetValue(targetType, out var targetClosureFacts))
            targetFacts.AddRange(targetClosureFacts);

        var targetMembers = new List<CompileBackMemberRequirement>
        {
            new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(targetIdentity.FullName, propertyName, overload, signatureText),
                CompileBackMemberKind.PropertyGet,
                getter.Attributes.HasFlag(MethodAttributes.Static),
                ToCompileBackParameters(propertyDeclaration.Signature.Parameters),
                returnType,
                TypeParameters: [],
                targetIsAutoProperty
                    ? CompileBackStubBodyKind.AutoProperty
                    : accessors.Setter.IsNil
                        ? CompileBackStubBodyKind.TargetBody
                        : CompileBackStubBodyKind.TargetGetterWithSetter,
                targetIsAutoProperty ? null : targetBody,
                targetIsAutoProperty
                    ? [
                        new CompileBackFact("metadata", "target-property-getter", reader.GetString(reader.GetMethodDefinition(targetGetter).Name)),
                        new CompileBackFact("metadata", "auto-property", propertyName)
                    ]
                    : [new CompileBackFact("metadata", "target-property-getter", reader.GetString(reader.GetMethodDefinition(targetGetter).Name))],
                propertyDeclaration.Attributes,
                MetadataDeclarationQuery.GetMethod(reader, targetTypeDef, getter, getterSignature).Signature.ReturnAttributes,
                ExplicitInterfaceMemberName: explicitInterfaceMemberName)
        };
        AddRequiredMembers(targetMembers, closureMemberRequirements, targetType);

        var requirements = new List<CompileBackTypeRequirement>
        {
            new(
                targetIdentity,
                ShellKind(reader, targetTypeDef, targetFacts),
                targetMembers,
                PrimaryConstructor: null,
                targetFacts)
            {
                IncludeMemberSurface = targetFacts.Any(fact => fact.Id == "closure-member")
            }
        };
        AddRequiredMembers(targetMembers, closureMemberRequirements, targetRoot);
        AddClosureTypeRequirements(requirements, reader, targetRoot, closureFacts, closureMemberRequirements);

        foreach (var dependency in closureRoots.OrderBy(handle => MetadataTokens.GetToken(handle)))
        {
            if (dependency == targetRoot)
                continue;

            AddClosureTypeRequirements(requirements, reader, dependency, closureFacts, closureMemberRequirements);
        }
        AddExplicitInterfacePropertyDeclaration(requirements, reader, targetTypeDef, targetGetter);

        var production = TypeProducer.Produce(reader, requirements, diagnostics);
        var declarations = production.Requests;
        var module = new CompileBackModuleRequirement(
            Usings: BuildUsings(function),
            AssemblyAttributes: [],
            ModuleAttributes: []);
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, signatureText),
            module,
            production.Requirements,
            declarations,
            diagnostics);
        return new CompileBackSourceResult(plan, ComposeCompilationUnit(plan));
    }

    static void AddRequiredMembers(
        List<CompileBackMemberRequirement> members,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> requirementsByRoot,
        TypeDefinitionHandle root,
        CompileBackPrimaryConstructor? primaryConstructor = null)
    {
        if (!requirementsByRoot.TryGetValue(root, out var requiredMembers))
            return;
        foreach (var required in requiredMembers)
        {
            if (primaryConstructor is not null
                && required.Kind == CompileBackMemberKind.Constructor
                && SameParameters(required.Parameters, primaryConstructor.ParameterList))
            {
                continue;
            }
            if (!members.Any(existing => SameMemberDeclaration(existing, required)))
                members.Add(required);
        }
    }

    static bool SameParameters(IReadOnlyList<CompileBackParameter> left, IReadOnlyList<CompileBackParameter> right)
        => left.Count == right.Count
            && left.Zip(right).All(pair =>
                string.Equals(pair.First.Type.DisplayName, pair.Second.Type.DisplayName, StringComparison.Ordinal)
                && string.Equals(pair.First.Modifier, pair.Second.Modifier, StringComparison.Ordinal));

    static bool SameMemberDeclaration(CompileBackMemberRequirement left, CompileBackMemberRequirement right)
        => left.Kind == right.Kind
            && left.Identity.Type == right.Identity.Type
            && left.Identity.Method == right.Identity.Method
            && left.Identity.Signature == right.Identity.Signature;

    static void AddClosureTypeRequirements(
        List<CompileBackTypeRequirement> requirements,
        MetadataReader reader,
        TypeDefinitionHandle root,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements)
    {
        AddClosureTypeRequirement(root);
        foreach (var handle in closureMemberRequirements.Keys.Concat(closureFacts.Keys)
            .Where(handle => handle != root && TopLevelRootOf(reader, handle) == root)
            .Distinct()
            .OrderBy(handle => MetadataTokens.GetToken(handle)))
        {
            AddClosureTypeRequirement(handle);
        }

        void AddClosureTypeRequirement(TypeDefinitionHandle handle)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var identity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            if (requirements.Any(requirement => requirement.Type.MetadataFullName == identity.MetadataFullName))
                return;
            var facts = closureFacts.TryGetValue(handle, out var foundFacts) ? foundFacts : [];

            var requirement = new CompileBackTypeRequirement(
                identity,
                ShellKind(reader, typeDef, facts),
                RequiredMembers: closureMemberRequirements.TryGetValue(handle, out var requiredMembers)
                    ? requiredMembers.ToArray()
                    : [],
                PrimaryConstructor: null,
                SourceFacts: facts.Count != 0
                    ? facts.ToArray()
                    : handle == root
                        ? [new CompileBackFact("closure", "closure-root", identity.FullName)]
                        : [new CompileBackFact("metadata", "nested-closure-member-owner", identity.FullName)])
            {
                IncludeMemberSurface = facts.Any(fact => fact.Id == "closure-member")
            };
            requirements.Add(requirement);
        }
    }

    static void AddExplicitInterfacePropertyDeclaration(
        List<CompileBackTypeRequirement> requirements,
        MetadataReader reader,
        TypeDefinition targetType,
        MethodDefinitionHandle targetAccessor)
    {
        foreach (var implementationHandle in targetType.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody != targetAccessor
                || implementation.MethodDeclaration.Kind != HandleKind.MethodDefinition)
            {
                continue;
            }

            var declarationHandle = (MethodDefinitionHandle)implementation.MethodDeclaration;
            var declaration = reader.GetMethodDefinition(declarationHandle);
            var interfaceHandle = declaration.GetDeclaringType();
            var interfaceDef = reader.GetTypeDefinition(interfaceHandle);
            var propertyHandle = interfaceDef.GetProperties().FirstOrDefault(handle =>
            {
                var accessors = reader.GetPropertyDefinition(handle).GetAccessors();
                return accessors.Getter == declarationHandle || accessors.Setter == declarationHandle;
            });
            if (propertyHandle.IsNil)
                continue;

            var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
            var member = TypeProducer.PropertyRequirement(
                reader,
                interfaceDef,
                interfaceIdentity,
                propertyHandle,
                reader.GetString(declaration.Name),
                "explicit-interface-target-property");
            if (member is null)
                continue;

            int requirementIndex = requirements.FindIndex(
                requirement => requirement.Type.MetadataFullName == interfaceIdentity.MetadataFullName);
            if (requirementIndex < 0)
                continue;

            var requirement = requirements[requirementIndex];
            if (!requirement.RequiredMembers.Any(existing => TypeProducer.SameMemberShape(existing, member)))
            {
                requirements[requirementIndex] = requirement with
                {
                    RequiredMembers = requirement.RequiredMembers.Append(member).ToArray()
                };
            }
            return;
        }
    }

    static ExplicitInterfaceEventInfo? ExplicitInterfaceEvent(
        MetadataReader reader,
        TypeDefinition targetType,
        MethodDefinitionHandle targetAccessor)
    {
        foreach (var implementationHandle in targetType.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody != targetAccessor
                || implementation.MethodDeclaration.Kind != HandleKind.MethodDefinition)
            {
                continue;
            }

            var declarationHandle = (MethodDefinitionHandle)implementation.MethodDeclaration;
            var declaration = reader.GetMethodDefinition(declarationHandle);
            var interfaceHandle = declaration.GetDeclaringType();
            var interfaceDef = reader.GetTypeDefinition(interfaceHandle);
            foreach (var eventHandle in interfaceDef.GetEvents())
            {
                var eventDefinition = reader.GetEventDefinition(eventHandle);
                var accessors = eventDefinition.GetAccessors();
                if (accessors.Adder != declarationHandle && accessors.Remover != declarationHandle)
                    continue;

                var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
                string eventName = Identifier(reader.GetString(eventDefinition.Name));
                return new ExplicitInterfaceEventInfo(
                    interfaceHandle,
                    eventHandle,
                    $"{interfaceIdentity.FullName}.{eventName}",
                    reader.GetString(declaration.Name));
            }
        }

        return null;
    }

    static void AddExplicitInterfaceEventDeclaration(
        List<CompileBackTypeRequirement> requirements,
        MetadataReader reader,
        ExplicitInterfaceEventInfo? explicitEvent)
    {
        if (explicitEvent is null)
            return;

        var interfaceDef = reader.GetTypeDefinition(explicitEvent.InterfaceType);
        var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
        var member = TypeProducer.EventRequirement(
            reader,
            interfaceDef,
            interfaceIdentity,
            explicitEvent.InterfaceEvent,
            explicitEvent.AccessorName,
            "explicit-interface-target-event");
        if (member is null)
            return;

        int requirementIndex = requirements.FindIndex(
            requirement => requirement.Type.MetadataFullName == interfaceIdentity.MetadataFullName);
        if (requirementIndex < 0)
            return;

        var requirement = requirements[requirementIndex];
        if (requirement.RequiredMembers.Any(existing => TypeProducer.SameMemberShape(existing, member)))
            return;

        requirements[requirementIndex] = requirement with
        {
            RequiredMembers = requirement.RequiredMembers.Append(member).ToArray()
        };
    }

    public static CompileBackSourceResult ComposePropertySetter(
        string assemblyPath,
        MetadataReader reader,
        IrFunction function,
        TypeDefinitionHandle targetType,
        PropertyDefinitionHandle targetProperty,
        MethodDefinitionHandle targetSetter,
        string targetBody,
        string fullType,
        string methodName,
        int overload,
        string signatureText,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements)
    {
        var targetTypeDef = reader.GetTypeDefinition(targetType);
        var property = reader.GetPropertyDefinition(targetProperty);
        var setter = reader.GetMethodDefinition(targetSetter);
        var propertySignature = GuardedSignatureText.PropertyText(reader, property, GenericContext.ForType(reader, targetTypeDef));
        var propertyDeclaration = MetadataDeclarationQuery.GetProperty(reader, targetTypeDef, property);
        var targetIdentity = CompileBackTypeIdentity.FromDefinition(reader, targetTypeDef);
        string metadataPropertyName = reader.GetString(property.Name);
        string propertyName = Identifier(metadataPropertyName);
        string? explicitInterfaceMemberName = ExplicitInterfaceMemberName(reader, metadataPropertyName);
        var returnType = CompileBackTypeSignature.Display(propertySignature.ReturnType);
        bool targetIsAutoProperty = IsAutoPropertySetter(reader, targetTypeDef, property, targetSetter, returnType.DisplayName);

        var diagnostics = new List<CompileBackPlanningDiagnostic>();
        var targetRoot = TopLevelRootOf(reader, targetType);
        var targetFacts = new List<CompileBackFact>
        {
            new("metadata", "target-type", targetIdentity.FullName),
        };
        if (closureFacts.TryGetValue(targetType, out var targetClosureFacts))
            targetFacts.AddRange(targetClosureFacts);

        var indexerParameterCount = propertySignature.ParameterTypes.Length;
        var targetMembers = new List<CompileBackMemberRequirement>
        {
            new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(targetIdentity.FullName, propertyName, overload, signatureText),
                CompileBackMemberKind.PropertySet,
                setter.Attributes.HasFlag(MethodAttributes.Static),
                ToCompileBackParameters(propertyDeclaration.Signature.Parameters).Take(indexerParameterCount).ToArray(),
                returnType,
                TypeParameters: [],
                targetIsAutoProperty
                    ? CompileBackStubBodyKind.AutoPropertyGetSet
                    : property.GetAccessors().Getter.IsNil
                        ? CompileBackStubBodyKind.TargetBody
                        : CompileBackStubBodyKind.TargetSetterWithGetter,
                targetIsAutoProperty ? null : targetBody,
                targetIsAutoProperty
                    ? [
                        new CompileBackFact("metadata", "target-property-setter", reader.GetString(setter.Name)),
                        new CompileBackFact("metadata", "auto-property", propertyName)
                    ]
                    : [new CompileBackFact("metadata", "target-property-setter", reader.GetString(setter.Name))],
                ExplicitInterfaceMemberName: explicitInterfaceMemberName)
        };
        AddRequiredMembers(targetMembers, closureMemberRequirements, targetType);

        var requirements = new List<CompileBackTypeRequirement>
        {
            new(
                targetIdentity,
                ShellKind(reader, targetTypeDef, targetFacts),
                targetMembers,
                PrimaryConstructor: null,
                targetFacts)
            {
                IncludeMemberSurface = targetFacts.Any(fact => fact.Id == "closure-member")
            }
        };
        AddClosureTypeRequirements(requirements, reader, targetRoot, closureFacts, closureMemberRequirements);

        foreach (var dependency in closureRoots.OrderBy(handle => MetadataTokens.GetToken(handle)))
        {
            if (dependency == targetRoot)
                continue;

            AddClosureTypeRequirements(requirements, reader, dependency, closureFacts, closureMemberRequirements);
        }
        AddExplicitInterfacePropertyDeclaration(requirements, reader, targetTypeDef, targetSetter);

        var production = TypeProducer.Produce(reader, requirements, diagnostics);
        var declarations = production.Requests;
        var module = new CompileBackModuleRequirement(
            Usings: BuildUsings(function),
            AssemblyAttributes: [],
            ModuleAttributes: []);
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, signatureText),
            module,
            production.Requirements,
            declarations,
            diagnostics);
        return new CompileBackSourceResult(plan, ComposeCompilationUnit(plan));
    }

    public static CompileBackSourceResult ComposeEventAccessor(
        string assemblyPath,
        MetadataReader reader,
        IrFunction function,
        TypeDefinitionHandle targetType,
        EventDefinitionHandle targetEvent,
        MethodDefinitionHandle targetAccessor,
        string targetBody,
        string fullType,
        string methodName,
        int overload,
        string signatureText,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements,
        string? siblingAccessorBody = null)
    {
        var targetTypeDef = reader.GetTypeDefinition(targetType);
        var eventDefinition = reader.GetEventDefinition(targetEvent);
        var accessors = eventDefinition.GetAccessors();
        var accessor = reader.GetMethodDefinition(targetAccessor);
        var signature = GuardedSignatureText.MethodText(
            reader,
            accessor,
            GenericContext.ForMethod(reader, targetTypeDef, accessor));
        var parameters = MethodParameters(reader, accessor, signature);
        if (parameters.Count != 1)
            throw new InvalidOperationException("Event accessors must have exactly one value parameter.");

        var kind = targetAccessor == accessors.Adder
            ? CompileBackMemberKind.EventAdd
            : targetAccessor == accessors.Remover
                ? CompileBackMemberKind.EventRemove
                : throw new InvalidOperationException("Target method is not an accessor of the selected event.");
        var targetIdentity = CompileBackTypeIdentity.FromDefinition(reader, targetTypeDef);
        string metadataEventName = reader.GetString(eventDefinition.Name);
        string eventName = Identifier(metadataEventName[(metadataEventName.LastIndexOf('.') + 1)..]);
        var explicitEvent = ExplicitInterfaceEvent(
            reader,
            targetTypeDef,
            targetAccessor);

        var diagnostics = new List<CompileBackPlanningDiagnostic>();
        var targetRoot = TopLevelRootOf(reader, targetType);
        var targetFacts = new List<CompileBackFact>
        {
            new("metadata", "target-type", targetIdentity.FullName),
        };
        if (closureFacts.TryGetValue(targetType, out var targetClosureFacts))
            targetFacts.AddRange(targetClosureFacts);

        var targetMembers = new List<CompileBackMemberRequirement>
        {
            new(
                new CompileBackMethodIdentity(targetIdentity.FullName, eventName, overload, signatureText),
                kind,
                accessor.Attributes.HasFlag(MethodAttributes.Static),
                [],
                parameters[0].Type,
                [],
                siblingAccessorBody is not null
                    ? CompileBackStubBodyKind.TargetEventAccessorWithSibling
                    : CompileBackStubBodyKind.TargetBody,
                targetBody,
                [new CompileBackFact("metadata", "target-event-accessor", reader.GetString(accessor.Name))],
                MemberAttributes(reader, eventDefinition.GetCustomAttributes()),
                RequiresUnsafeModifier: ContainsFixedBufferElementAccess(function),
                ExplicitInterfaceMemberName: explicitEvent?.QualifiedName,
                SiblingTargetBody: siblingAccessorBody)
        };
        AddRequiredMembers(targetMembers, closureMemberRequirements, targetType);

        var requirements = new List<CompileBackTypeRequirement>
        {
            new(
                targetIdentity,
                ShellKind(reader, targetTypeDef, targetFacts),
                targetMembers,
                PrimaryConstructor: null,
                targetFacts)
        };
        AddClosureTypeRequirements(requirements, reader, targetRoot, closureFacts, closureMemberRequirements);
        foreach (var dependency in closureRoots.OrderBy(handle => MetadataTokens.GetToken(handle)))
        {
            if (dependency != targetRoot)
                AddClosureTypeRequirements(requirements, reader, dependency, closureFacts, closureMemberRequirements);
        }
        AddExplicitInterfaceEventDeclaration(requirements, reader, explicitEvent);

        var production = TypeProducer.Produce(reader, requirements, diagnostics);
        var module = new CompileBackModuleRequirement(
            Usings: BuildUsings(function),
            AssemblyAttributes: [],
            ModuleAttributes: []);
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, signatureText),
            module,
            production.Requirements,
            production.Requests,
            diagnostics);
        return new CompileBackSourceResult(plan, ComposeCompilationUnit(plan));
    }

    public static CompileBackSourceResult ComposeMethod(
        string assemblyPath,
        MetadataReader reader,
        IrFunction function,
        TypeDefinitionHandle targetType,
        MethodDefinitionHandle targetMethod,
        string targetBody,
        string fullType,
        string methodName,
        int overload,
        string signatureText,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements,
        string? constructorChain = null)
    {
        var targetTypeDef = reader.GetTypeDefinition(targetType);
        var method = reader.GetMethodDefinition(targetMethod);
        var signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, targetTypeDef, method));
        var targetIdentity = CompileBackTypeIdentity.FromDefinition(reader, targetTypeDef);
        string targetMethodName = Identifier(methodName);
        bool isConstructor = function.MethodKind is IrMethodKind.Constructor or IrMethodKind.StaticConstructor;
        var bodyFacts = isConstructor ? MemberBodyFacts.Constructor(function) : ConstructorBodyFacts.None;
        var primaryConstructor = isConstructor
            ? PrimaryConstructorFromPrologue(reader, method, bodyFacts.PrimaryConstructorPrologue, targetBody)
            : PrimaryConstructorFromCapturedFields(reader, targetTypeDef, targetBody);

        var diagnostics = new List<CompileBackPlanningDiagnostic>();
        var targetRoot = TopLevelRootOf(reader, targetType);
        var targetFacts = new List<CompileBackFact>
        {
            new("metadata", "target-type", targetIdentity.FullName),
        };
        if (closureFacts.TryGetValue(targetType, out var targetClosureFacts))
            targetFacts.AddRange(targetClosureFacts);

        var chainParameterTypes = bodyFacts.ChainParameterTypes;
        // Only this(...) self-chains are re-emitted as constructor initializers.
        // RTS shells are flat (object-based, no reconstructed base class), so a
        // base(args) initializer has no base constructor to bind to and would
        // fail to compile (CS1729). Leaving those bodies empty keeps the prior
        // implicit-base() behavior instead of introducing a recompile failure.
        string? targetConstructorInitializer =
            constructorChain is { } chain && chain.StartsWith("this(", StringComparison.Ordinal)
                ? constructorChain
                : null;

        var targetMembers = isConstructor && primaryConstructor is not null
            ? primaryConstructor.FieldInitializers.ToList()
            :
        [
            new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(targetIdentity.FullName, targetMethodName, overload, signatureText),
                isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method,
                method.Attributes.HasFlag(MethodAttributes.Static),
                MethodParameters(reader, method, signature),
                isConstructor ? null : CompileBackTypeSignature.Display(MethodReturnType(reader, targetTypeDef, method)),
                MethodTypeParameters(reader, method),
                CompileBackStubBodyKind.TargetBody,
                targetBody,
                [new CompileBackFact("metadata", isConstructor ? "target-constructor" : "target-method", reader.GetString(method.Name))],
                isConstructor ? null : MemberAttributes(reader, method.GetCustomAttributes()),
                isConstructor ? null : MethodReturnAttributes(reader, method),
                IsAbstract: !isConstructor && IsAbstractMethod(method),
                IsVirtual: !isConstructor && IsVirtualMethod(method),
                IsOverride: false,
                IsSealed: false,
                IsAsync: !isConstructor
                    && (function.RequiresAsyncBodyModifier
                        || function.IsRuntimeAsync == MetadataFactState.Yes),
                ConstructorInitializer: targetConstructorInitializer,
                RequiresUnsafeModifier: ContainsFixedBufferElementAccess(function))
        ];
        bool includeRecordSurface = false;
        if (!isConstructor && IsRecordGeneratedFieldReadHelper(reader, targetTypeDef, targetIdentity, methodName, signature, function))
        {
            if (TypeProducer.TryCreateRecordEqualityContractRequirement(reader, targetType) is { } equalityContract)
                targetMembers.Add(equalityContract);
            targetMembers.AddRange(TargetBackingFieldReadMembers(reader, targetTypeDef, targetIdentity, function));
        }
        else if (!isConstructor && IsRecordGeneratedSurfaceHelper(reader, targetTypeDef, targetIdentity, methodName, signature))
        {
            // ToString / PrintMembers delegate to the record's other synthesized members
            // rather than reading backing fields directly, so reconstruct the full record
            // member surface (faithful `protected virtual` helpers, EqualityContract, and the
            // record properties) via the closure-member surface path instead of field shells.
            targetFacts.Add(new CompileBackFact("metadata", "record-generated-helper", "full record surface required"));
            includeRecordSurface = true;
        }
        if (isConstructor && primaryConstructor is null)
            targetMembers.AddRange(TargetBackingFieldWriteMembers(reader, targetTypeDef, targetIdentity, function, allowStaticStores: false));
        if (function.MethodKind is IrMethodKind.StaticConstructor)
            targetMembers.AddRange(TargetBackingFieldWriteMembers(reader, targetTypeDef, targetIdentity, function, allowStaticStores: true));
        if (!isConstructor
            && EqualityOperatorSibling(reader, targetTypeDef, targetIdentity, methodName, signature) is { } equalitySibling)
        {
            targetMembers.Add(equalitySibling);
        }
        if (!isConstructor
            && CheckedOperatorSibling(reader, targetTypeDef, targetIdentity, methodName, signature) is { } checkedOperatorSibling)
        {
            targetMembers.Add(checkedOperatorSibling);
        }
        if (!isConstructor
            && TypedEqualsSibling(reader, targetTypeDef, targetIdentity, methodName, signature) is { } typedEqualsSibling)
        {
            targetMembers.Add(typedEqualsSibling);
        }
        AddRequiredMembers(targetMembers, closureMemberRequirements, targetType, primaryConstructor);
        // RTS is an orchestrator: it reconstructs the shell and re-emits the
        // product's constructor-chain source, then lets the Roslyn oracle judge
        // binding by recompiling and comparing IL. It deliberately does NOT model
        // C# overload resolution to predict whether `: this(args)` binds to the
        // chained-to constructor — that knowledge belongs to the product (which
        // prints the chain) and to Roslyn (which validates it). A mis-binding
        // cannot produce a false Exact: a wrong bind changes the emitted call
        // token (OpcodeDiff) or fails to compile (RecompileFail), both of which
        // the oracle surfaces honestly. The only preconditions are structural:
        // the chained-to constructor must be reconstructable in the shell, and a
        // same-arity sibling must be present to serve as its binding target.
        bool chainedConstructorReconstructed =
            chainParameterTypes is { } chainParams
            // The chained-to constructor is reconstructed only when its signature
            // is fully supported (an unsupported parameter such as a function
            // pointer makes the planner drop it, mirroring MethodRequirement).
            && chainParams.All(type => !TypeShellProducer.IsUnsupportedSurfaceSignature(type))
            // A same-arity non-target constructor must actually be present in the
            // shell for `: this(args)` to have a binding target.
            && targetMembers.Any(member => member.Kind == CompileBackMemberKind.Constructor
                && member.StubBody != CompileBackStubBodyKind.TargetBody
                && member.Parameters.Count == chainParams.Count);
        if (targetConstructorInitializer is not null && !chainedConstructorReconstructed)
        {
            // The chained-to constructor was not reconstructed in the shell: either
            // an unsupported parameter dropped it, or no same-arity sibling is
            // present to bind to. Drop the initializer and keep the body rather
            // than emit a `: this(args)` with no binding target in the shell.
            int targetIndex = targetMembers.FindIndex(member =>
                member.Kind == CompileBackMemberKind.Constructor
                && member.StubBody == CompileBackStubBodyKind.TargetBody);
            if (targetIndex >= 0)
                targetMembers[targetIndex] = targetMembers[targetIndex] with { ConstructorInitializer = null };
        }
        if (includeRecordSurface)
        {
            // AddRequiredMembers above preserves every IR-gathered dependency (including a user
            // generic `PrintMembers<T>` overload the surface enumeration would skip). Drop the
            // synthesized record-helper stubs so AddClosureMemberSurface re-emits them with
            // faithful `protected virtual` accessibility — but only when no differently-shaped
            // same-name member remains: the surface dedups methods by name, so removing a stub
            // shadowed by a same-name overload would leave the synthesized shape unre-emitted.
            // In that (pathological) case the public stub is kept, still yielding an Exact build.
            var shadowedHelpers = targetMembers
                .Where(member => !IsSynthesizedRecordHelperStub(member))
                .Select(member => (member.Kind, member.Identity.Method))
                .ToHashSet();
            targetMembers.RemoveAll(member =>
                member.StubBody != CompileBackStubBodyKind.TargetBody
                && IsSynthesizedRecordHelperStub(member)
                && !shadowedHelpers.Contains((member.Kind, member.Identity.Method)));
        }

        var requirements = new List<CompileBackTypeRequirement>
        {
            new(
                targetIdentity,
                ShellKind(reader, targetTypeDef, targetFacts),
                targetMembers,
                primaryConstructor,
                targetFacts)
            {
                IncludeMemberSurface = includeRecordSurface
                    || targetFacts.Any(fact => fact.Id == "closure-member")
            }
        };
        AddClosureTypeRequirements(requirements, reader, targetRoot, closureFacts, closureMemberRequirements);

        foreach (var dependency in closureRoots.OrderBy(handle => MetadataTokens.GetToken(handle)))
        {
            if (dependency == targetRoot)
                continue;

            AddClosureTypeRequirements(requirements, reader, dependency, closureFacts, closureMemberRequirements);
        }

        var production = TypeProducer.Produce(reader, requirements, diagnostics);
        var declarations = production.Requests;
        var module = new CompileBackModuleRequirement(
            Usings: BuildUsings(function),
            AssemblyAttributes: [],
            ModuleAttributes: []);
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, signatureText),
            module,
            production.Requirements,
            declarations,
            diagnostics);
        return new CompileBackSourceResult(plan, ComposeCompilationUnit(plan));
    }

    static string ComposeCompilationUnit(CompileBackReconstructionPlan plan)
        => new CSharpTypePrinter().PrintBatch(
            plan.PrintRequests,
            new CSharpTypePrintOptions
            {
                IncludeCustomAttributes = true,
                EmitPragmaWarningDisable = true,
                AssemblyAttributes = plan.Module.AssemblyAttributes.Select(attribute => attribute.Text).ToArray(),
                ModuleAttributes = plan.Module.ModuleAttributes.Select(attribute => attribute.Text).ToArray(),
                Usings = plan.Module.Usings,
            }).Source;

    static ApiMember ToApiMember(CompileBackMemberRequirement member)
    {
        string? returnType = member.ReturnType?.DisplayName;
        bool isExplicitInterfaceProperty = member.ExplicitInterfaceMemberName is not null
            && member.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet;
        bool isEvent = member.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove;
        bool isExplicitInterfaceEvent = member.ExplicitInterfaceMemberName is not null && isEvent;
        var apiMember = new ApiMember
        {
            Name = member.ExplicitInterfaceMemberName ?? member.Identity.Method,
            Kind = isExplicitInterfaceProperty || isExplicitInterfaceEvent
                ? "explicit-interface-implementation"
                : member.Kind switch
                {
                    CompileBackMemberKind.PropertyGet => "property",
                    CompileBackMemberKind.PropertySet => "property",
                    CompileBackMemberKind.EventAdd => "event",
                    CompileBackMemberKind.EventRemove => "event",
                    CompileBackMemberKind.Constructor => "constructor",
                    CompileBackMemberKind.Method => "method",
                    CompileBackMemberKind.Field => "field",
                    _ => throw new NotSupportedException($"Unsupported member declaration kind '{member.Kind}'."),
                },
            ReturnType = returnType,
            Signature = member.DeclarationSignature,
            IsStatic = member.IsStatic,
            IsAbstract = member.IsAbstract,
            IsVirtual = member.IsVirtual,
            IsOverride = member.IsOverride,
            IsSealed = member.IsSealed,
            Accessibility = AccessibilityText(member.Accessibility),
            Attributes = member.Attributes?.ToList() ?? [],
            IsUnsafe = member.RequiresUnsafeModifier || RequiresUnsafe(member),
            IsAsync = member.IsAsync,
            IsExtension = member.IsExtension,
            IsConst = member.Kind == CompileBackMemberKind.Field
                && member.StubBody == CompileBackStubBodyKind.TargetBody,
        };
        if (member.Kind != CompileBackMemberKind.Field)
        {
            apiMember.SignatureModel = new ApiSignature
            {
                ReturnType = returnType,
                ReturnAttributes = member.Kind == CompileBackMemberKind.Method
                    ? member.ReturnAttributes?.ToList() ?? []
                    : [],
                MemberName = member.TypeParameters.Count == 0
                    ? member.Identity.Method
                    : $"{member.Identity.Method}<{string.Join(", ", member.TypeParameters.Select(parameter => parameter.Name))}>",
                TypeParameters = member.TypeParameters
                    .Select(parameter => new TypeParameter
                    {
                        Name = parameter.Name,
                        Constraints = parameter.Constraints.ToList(),
                        StructuredConstraints = parameter.StructuredConstraints,
                    })
                    .ToList(),
                Parameters = member.Parameters
                    .Select(parameter =>
                    {
                        var (type, modifier) = NormalizeParameter(parameter);
                        return new ApiParameter
                        {
                            Attributes = parameter.Attributes?.ToList() ?? [],
                            Name = parameter.Name,
                            Type = type,
                            Modifier = modifier,
                            HasDefault = parameter.HasDefault,
                            DefaultValueText = parameter.DefaultValueText,
                        };
                    })
                    .ToList(),
            };
            if (member.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet)
            {
                apiMember.SignatureModel.MemberName = member.Parameters.Count > 0
                    ? "this[]"
                    : apiMember.Name;
                apiMember.SignatureModel.Accessors = PropertyAccessors(member);
            }
            else if (isEvent)
            {
                apiMember.SignatureModel.MemberName = apiMember.Name;
                apiMember.SignatureModel.Accessors =
                [
                    new ApiAccessor { Kind = "add" },
                    new ApiAccessor { Kind = "remove" },
                ];
            }
        }
        return apiMember;
    }

    static List<ApiAccessor> PropertyAccessors(CompileBackMemberRequirement member)
    {
        bool hasGetter = member.Kind == CompileBackMemberKind.PropertyGet
            || member.StubBody is CompileBackStubBodyKind.AutoPropertyGetSet
                or CompileBackStubBodyKind.ThrowGetSet
                or CompileBackStubBodyKind.TargetSetterWithGetter;
        bool hasSetter = member.Kind == CompileBackMemberKind.PropertySet
            || member.StubBody is CompileBackStubBodyKind.AutoPropertyGetSet
                or CompileBackStubBodyKind.ThrowGetSet
                or CompileBackStubBodyKind.TargetGetterWithSetter;
        var accessors = new List<ApiAccessor>();
        if (hasGetter)
        {
            accessors.Add(new ApiAccessor
            {
                Kind = "get",
                ReturnAttributes = member.ReturnAttributes?.ToList() ?? [],
            });
        }
        if (hasSetter)
            accessors.Add(new ApiAccessor { Kind = "set" });
        return accessors;
    }

    static CSharpMemberPolicy ToMemberPolicy(
        CompileBackMemberRequirement requirement,
        int primaryConstructorParameterCount)
    {
        var member = ToApiMember(requirement);
        return requirement.StubBody switch
        {
            CompileBackStubBodyKind.None
                => new(member, CSharpBodyPolicy.Skeleton),
            CompileBackStubBodyKind.AutoProperty
                => new(member, CSharpBodyPolicy.Skeleton),
            CompileBackStubBodyKind.AutoPropertyGetSet
                => new(member, CSharpBodyPolicy.Skeleton),
            CompileBackStubBodyKind.Throw when requirement.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet
                => new(member, CSharpBodyPolicy.Stub, PropertyBody(requirement, CSharpAccessorBody.Throw)),
            CompileBackStubBodyKind.Throw when requirement.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove
                => new(
                    member,
                    CSharpBodyPolicy.Stub,
                    new CSharpEventBody(CSharpAccessorBody.Throw, CSharpAccessorBody.Throw)),
            CompileBackStubBodyKind.Throw when requirement.Kind == CompileBackMemberKind.Constructor
                && primaryConstructorParameterCount > 0
                => new(
                    member,
                    CSharpBodyPolicy.Stub,
                    new CSharpBlockBody(
                        "throw null;",
                        new CSharpConstructorInitializer(
                            CSharpConstructorInitializerKind.This,
                            Enumerable.Repeat("default", primaryConstructorParameterCount).ToArray()))),
            CompileBackStubBodyKind.Throw
                => new(member, CSharpBodyPolicy.Stub),
            CompileBackStubBodyKind.ThrowGetSet
                => new(
                    member,
                    CSharpBodyPolicy.Stub,
                    new CSharpPropertyBody(CSharpAccessorBody.Throw, CSharpAccessorBody.Throw)),
            CompileBackStubBodyKind.TargetBody when requirement.Kind == CompileBackMemberKind.Field
                => new(member, CSharpBodyPolicy.Full, new CSharpFieldInitializer(requirement.TargetBody!)),
            CompileBackStubBodyKind.TargetBody when requirement.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    PropertyBody(requirement, CSharpAccessorBody.Block(requirement.TargetBody!))),
            CompileBackStubBodyKind.TargetBody when requirement.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    EventBody(requirement, CSharpAccessorBody.Block(requirement.TargetBody!))),
            CompileBackStubBodyKind.TargetEventAccessorWithSibling
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    EventBody(
                        requirement,
                        CSharpAccessorBody.Block(requirement.TargetBody!),
                        CSharpAccessorBody.Block(requirement.SiblingTargetBody!))),
            CompileBackStubBodyKind.TargetBody when requirement.Kind == CompileBackMemberKind.Constructor
                && primaryConstructorParameterCount > 0
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    new CSharpBlockBody(
                        requirement.TargetBody!,
                        new CSharpConstructorInitializer(
                            CSharpConstructorInitializerKind.This,
                            Enumerable.Repeat("default", primaryConstructorParameterCount).ToArray()))),
            CompileBackStubBodyKind.TargetBody when requirement.Kind == CompileBackMemberKind.Constructor
                && CSharpFormatter.ParseConstructorInitializer(requirement.ConstructorInitializer) is { } capturedInitializer
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    new CSharpBlockBody(requirement.TargetBody!, capturedInitializer)),
            CompileBackStubBodyKind.TargetBody
                => new(member, CSharpBodyPolicy.Full, new CSharpBlockBody(requirement.TargetBody!)),
            CompileBackStubBodyKind.TargetGetterWithSetter
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    new CSharpPropertyBody(
                        CSharpAccessorBody.Block(requirement.TargetBody!),
                        CSharpAccessorBody.Throw)),
            CompileBackStubBodyKind.TargetSetterWithGetter
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    new CSharpPropertyBody(
                        CSharpAccessorBody.Throw,
                        CSharpAccessorBody.Block(requirement.TargetBody!))),
            CompileBackStubBodyKind.FieldInitializer
                => new(member, CSharpBodyPolicy.Full, new CSharpFieldInitializer(requirement.TargetBody!)),
            _ => throw new NotSupportedException(
                $"Unsupported RTS member body shape '{requirement.StubBody}'."),
        };
    }

    static CSharpPropertyBody PropertyBody(
        CompileBackMemberRequirement requirement,
        CSharpAccessorBody body)
        => requirement.Kind == CompileBackMemberKind.PropertyGet
            ? new CSharpPropertyBody(body, null)
            : new CSharpPropertyBody(null, body);

    static CSharpEventBody EventBody(
        CompileBackMemberRequirement requirement,
        CSharpAccessorBody body,
        CSharpAccessorBody? siblingBody = null)
        => requirement.Kind == CompileBackMemberKind.EventAdd
            ? new CSharpEventBody(body, siblingBody ?? CSharpAccessorBody.Throw)
            : new CSharpEventBody(siblingBody ?? CSharpAccessorBody.Throw, body);

    static CompileBackParameter ToCompileBackParameter(ApiParameter parameter)
        => new(
            Identifier(parameter.Name),
            CompileBackTypeSignature.Display(parameter.Type),
            parameter.Modifier,
            parameter.Attributes,
            parameter.HasDefault,
            parameter.DefaultValueText);

    static ApiParameter ToApiParameter(CompileBackParameter parameter)
    {
        var (type, modifier) = NormalizeParameter(parameter);
        return new ApiParameter
        {
            Attributes = parameter.Attributes?.ToList() ?? [],
            Name = parameter.Name,
            Type = type,
            Modifier = modifier,
            HasDefault = parameter.HasDefault,
            DefaultValueText = parameter.DefaultValueText,
        };
    }

    static IReadOnlyList<CompileBackParameter> ToCompileBackParameters(IEnumerable<ApiParameter> parameters)
        => parameters.Select(ToCompileBackParameter).ToArray();

    static IReadOnlyList<CompileBackTypeParameter> ToCompileBackTypeParameters(IEnumerable<TypeParameter> parameters)
        => parameters
            .Select(parameter => new CompileBackTypeParameter(
                parameter.Name,
                parameter.Constraints,
                parameter.Variance,
                parameter.StructuredConstraints))
            .ToArray();

    static string AccessibilityText(CompileBackAccessibility accessibility)
        => accessibility switch
        {
            CompileBackAccessibility.Public => "public",
            CompileBackAccessibility.Protected => "protected",
            _ => "public",
        };

    static bool RequiresUnsafe(CompileBackMemberRequirement member)
        => (member.ReturnType is { } returnType
                && CSharpFormatter.TypeRequiresUnsafeModifier(returnType.DisplayName))
            || member.Parameters.Any(parameter =>
                CSharpFormatter.TypeRequiresUnsafeModifier(parameter.Type.DisplayName))
            || (member.TargetBody is { } body && CSharpFormatter.RequiresUnsafeModifier(body))
            || (member.DeclarationSignature?.StartsWith("fixed ", StringComparison.Ordinal) == true);


    static IReadOnlyList<CompileBackParameter> MethodParameters(
        MetadataReader reader,
        MethodDefinition method,
        MethodSignature<string> signature)
    {
        var declaringType = reader.GetTypeDefinition(method.GetDeclaringType());
        return ToCompileBackParameters(MetadataDeclarationQuery.GetMethod(
            reader,
            declaringType,
            method,
            signature).Signature.Parameters);
    }

    static IReadOnlyList<string> MethodReturnAttributes(MetadataReader reader, MethodDefinition method)
        => MetadataDeclarationQuery.GetMethod(
            reader,
            reader.GetTypeDefinition(method.GetDeclaringType()),
            method).Signature.ReturnAttributes;

    static string MethodReturnType(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
        => MetadataDeclarationQuery.GetMethodReturnType(reader, typeDef, method);

    static CompileBackMemberRequirement? EqualityOperatorSibling(
        MetadataReader reader,
        TypeDefinition typeDef,
        CompileBackTypeIdentity typeIdentity,
        string methodName,
        MethodSignature<string> targetSignature)
    {
        var siblingName = methodName switch
        {
            "op_Equality" => "op_Inequality",
            "op_Inequality" => "op_Equality",
            _ => null,
        };
        if (siblingName is null)
            return null;

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != siblingName)
            {
                continue;
            }

            var signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
            if (!OperatorSignaturesMatch(targetSignature, signature))
                continue;

            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, siblingName, 0, MethodSignatureText(siblingName, signature)),
                CompileBackMemberKind.Method,
                method.Attributes.HasFlag(MethodAttributes.Static),
                MethodParameters(reader, method, signature),
                CompileBackTypeSignature.Display(signature.ReturnType),
                MethodTypeParameters(reader, method),
                CompileBackStubBodyKind.Throw,
                TargetBody: null,
                [new CompileBackFact("metadata", "operator-pair-sibling", siblingName)],
                MemberAttributes(reader, method.GetCustomAttributes()),
                MethodReturnAttributes(reader, method),
                IsAbstract: IsAbstractMethod(method),
                IsVirtual: IsVirtualMethod(method),
                IsOverride: false,
                IsSealed: false);
        }

        return null;
    }

    static bool OperatorSignaturesMatch(MethodSignature<string> left, MethodSignature<string> right)
        => left.ReturnType == right.ReturnType
            && left.ParameterTypes.SequenceEqual(right.ParameterTypes, StringComparer.Ordinal);

    static CompileBackMemberRequirement? CheckedOperatorSibling(
        MetadataReader reader,
        TypeDefinition typeDef,
        CompileBackTypeIdentity typeIdentity,
        string methodName,
        MethodSignature<string> targetSignature)
    {
        var siblingName = OperatorNames.UncheckedOperator(methodName);
        if (siblingName is null)
            return null;

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != siblingName)
                continue;

            var signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
            if (!OperatorSignaturesMatch(targetSignature, signature))
                continue;

            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, siblingName, 0, MethodSignatureText(siblingName, signature)),
                CompileBackMemberKind.Method,
                method.Attributes.HasFlag(MethodAttributes.Static),
                MethodParameters(reader, method, signature),
                CompileBackTypeSignature.Display(signature.ReturnType),
                MethodTypeParameters(reader, method),
                CompileBackStubBodyKind.Throw,
                TargetBody: null,
                [new CompileBackFact("metadata", "operator-pair-sibling", siblingName)],
                MemberAttributes(reader, method.GetCustomAttributes()),
                MethodReturnAttributes(reader, method),
                IsAbstract: IsAbstractMethod(method),
                IsVirtual: IsVirtualMethod(method),
                IsOverride: false,
                IsSealed: false);
        }

        return null;
    }

    static bool IsRecordGeneratedFieldReadHelper(
        MetadataReader reader,
        TypeDefinition typeDef,
        CompileBackTypeIdentity typeIdentity,
        string methodName,
        MethodSignature<string> signature,
        IrFunction function)
    {
        if (!HasRecordHelperShell(reader, typeDef, typeIdentity))
            return false;

        if (methodName == "GetHashCode"
            && signature.ReturnType == "int"
            && signature.ParameterTypes.Length == 0)
        {
            return true;
        }

        return methodName == "Equals"
            && signature.ReturnType == "bool"
            && function.Signature.Parameters is [{ Type: var parameterType }]
            && IsSelfType(parameterType, typeIdentity);
    }

    // ToString / PrintMembers are record-generated helpers that delegate to the record's
    // other synthesized members rather than reading backing fields directly, so they need
    // the full record surface (see IsRecordGeneratedFieldReadHelper for the field-read helpers).
    static bool IsRecordGeneratedSurfaceHelper(
        MetadataReader reader,
        TypeDefinition typeDef,
        CompileBackTypeIdentity typeIdentity,
        string methodName,
        MethodSignature<string> signature)
    {
        if (!HasRecordHelperShell(reader, typeDef, typeIdentity))
            return false;

        return (methodName == "ToString"
                && signature.ReturnType == "string"
                && signature.ParameterTypes.Length == 0)
            || (methodName == "PrintMembers"
                && signature.ReturnType == "bool"
                && signature.ParameterTypes is ["System.Text.StringBuilder"]);
    }

    // Matches only the exact compiler-synthesized record-helper stubs so the record surface
    // path can replace them with faithful `protected virtual` declarations without deleting a
    // differently-shaped same-name member (e.g. a user generic `PrintMembers<T>` overload).
    static bool IsSynthesizedRecordHelperStub(CompileBackMemberRequirement member)
        => (member.Kind == CompileBackMemberKind.PropertyGet
                && member.Identity.Method == "EqualityContract")
            || (member.Kind == CompileBackMemberKind.Method
                && member.Identity.Method == "PrintMembers"
                && member.TypeParameters.Count == 0
                && member.Parameters is [{ Type.DisplayName: "System.Text.StringBuilder" }]);

    static bool HasRecordHelperShell(MetadataReader reader, TypeDefinition typeDef, CompileBackTypeIdentity typeIdentity)
    {
        bool hasEqualityContract = false;
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            if (reader.GetString(property.Name) == "EqualityContract")
            {
                hasEqualityContract = true;
                break;
            }
        }

        bool hasPrintMembers = false;

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) == "PrintMembers")
            {
                hasPrintMembers = true;
                break;
            }
        }

        if (!hasPrintMembers)
            return false;

        return hasEqualityContract
            || (ShellKind(reader, typeDef) == CompileBackTypeKind.Struct
                && HasTypedEqualsMethod(reader, typeDef, typeIdentity));
    }

    static bool HasTypedEqualsMethod(MetadataReader reader, TypeDefinition typeDef, CompileBackTypeIdentity typeIdentity)
    {
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != "Equals")
                continue;

            MethodSignature<string> signature;
            IReadOnlyList<TypeRef> parameterTypes;
            try
            {
                signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
                parameterTypes = MethodParameterTypes(reader, typeDef, method);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                continue;
            }

            if (signature.ReturnType == "bool"
                && parameterTypes is [var parameterType]
                && IsSelfType(parameterType, typeIdentity))
            {
                return true;
            }
        }

        return false;
    }

    static CompileBackMemberRequirement? TypedEqualsSibling(
        MetadataReader reader,
        TypeDefinition typeDef,
        CompileBackTypeIdentity typeIdentity,
        string methodName,
        MethodSignature<string> targetSignature)
    {
        if (methodName != "Equals"
            || targetSignature.ReturnType != "bool"
            || targetSignature.ParameterTypes is not ["object"])
        {
            return null;
        }

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != "Equals")
                continue;

            MethodSignature<string> signature;
            IReadOnlyList<TypeRef> parameterTypes;
            try
            {
                signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
                parameterTypes = MethodParameterTypes(reader, typeDef, method);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                continue;
            }

            if (signature.ReturnType != "bool"
                || parameterTypes is not [var parameterType]
                || !IsSelfType(parameterType, typeIdentity))
            {
                continue;
            }

            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, "Equals", 0, MethodSignatureText("Equals", signature)),
                CompileBackMemberKind.Method,
                method.Attributes.HasFlag(MethodAttributes.Static),
                MethodParameters(reader, method, signature),
                CompileBackTypeSignature.Display(signature.ReturnType),
                MethodTypeParameters(reader, method),
                CompileBackStubBodyKind.Throw,
                TargetBody: null,
                [new CompileBackFact("metadata", "record-equals-sibling", "Equals")],
                MemberAttributes(reader, method.GetCustomAttributes()),
                MethodReturnAttributes(reader, method),
                IsAbstract: IsAbstractMethod(method),
                IsVirtual: IsVirtualMethod(method),
                IsOverride: false,
                IsSealed: false);
        }

        return null;
    }

    static IReadOnlyList<TypeRef> MethodParameterTypes(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
        => GuardedDecode.MethodSignature(reader, method, IrImporter.CallerScope(reader, typeDef, method)).ParameterTypes;

    static bool IsSelfType(TypeRef type, CompileBackTypeIdentity identity)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        if (definition.Kind != TypeRefKind.Definition)
            return false;
        if (definition.Namespace != identity.Namespace)
            return false;

        return definition.Name == IdentityTypeRefName(identity);
    }

    // The product already unwrapped the declaring type to a definition and captured its
    // namespace/name, so the self-type check is a plain identity string compare here.
    // Named distinctly from IsSelfType(TypeRef, ...) so reflection-by-name stays unambiguous.
    static bool DeclaredBySelfType(BackingFieldReference reference, CompileBackTypeIdentity identity)
        => reference.DeclaringNamespace == identity.Namespace
            && reference.DeclaringName == IdentityTypeRefName(identity);

    static string IdentityTypeRefName(CompileBackTypeIdentity identity)
    {
        string localPath = identity.Namespace.Length > 0
            && identity.MetadataFullName.StartsWith(identity.Namespace + ".", StringComparison.Ordinal)
                ? identity.MetadataFullName[(identity.Namespace.Length + 1)..]
                : identity.MetadataFullName;
        return localPath == identity.MetadataName
            ? identity.MetadataName
            : localPath.Replace('.', '+');
    }

    static string SelfTypeSignature(MetadataReader reader, TypeDefinition typeDef, CompileBackTypeIdentity typeIdentity)
        => MetadataDeclarationQuery.SelfTypeSignature(reader, typeDef);

    static string MethodSignatureText(string name, MethodSignature<string> signature)
        => $"{signature.ReturnType} {name}({string.Join(", ", signature.ParameterTypes)})";

    static bool IsAbstractMethod(MethodDefinition method)
        => MetadataDeclarationQuery.IsAbstractMethod(method);

    static bool IsVirtualMethod(MethodDefinition method)
        => MetadataDeclarationQuery.IsVirtualMethod(method);

    static bool IsProtectedMethod(MethodDefinition method)
        => MetadataDeclarationQuery.AccessibilityKeyword(method) is "protected" or "protected internal";

    static CompileBackAccessibility MethodAccessibility(MethodDefinition method)
        => IsProtectedMethod(method) ? CompileBackAccessibility.Protected : CompileBackAccessibility.Public;

    static IReadOnlyList<string> MemberAttributes(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => MetadataDeclarationQuery.RenderMemberAttributes(reader, attributes);

    static IReadOnlyList<CompileBackTypeParameter> MethodTypeParameters(MetadataReader reader, MethodDefinition method)
        => ToCompileBackTypeParameters(MetadataDeclarationQuery.GetMethod(
            reader,
            reader.GetTypeDefinition(method.GetDeclaringType()),
            method).Signature.TypeParameters);

    static CompileBackPrimaryConstructor? PrimaryConstructorFromPrologue(
        MetadataReader reader,
        MethodDefinition method,
        IReadOnlyList<PrimaryConstructorFieldStore>? prologue,
        string renderedBody)
    {
        if (reader.GetString(method.Name) != ".ctor"
            || method.Attributes.HasFlag(MethodAttributes.Static))
            return null;
        var declaringHandle = method.GetDeclaringType();
        var declaringType = reader.GetTypeDefinition(declaringHandle);
        if (CountInstanceConstructors(reader, declaringType) != 1
            || HasInAssemblyDerivedType(reader, declaringHandle))
            return null;
        // The IR-shape detection (leading arg->field stores before a parameterless
        // chain call, then only returns) lives in the product extractor; a null
        // prologue means the body is not primary-constructor shaped.
        if (prologue is null)
            return null;

        var parameterNames = ParameterNames(reader, method);
        if (parameterNames.Count == 0)
            return null;

        var fieldInitializers = new List<CompileBackMemberRequirement>();
        var initializerTexts = new List<(string Field, string Value)>();
        foreach (var fieldStore in prologue)
        {
            if (!parameterNames.TryGetValue(fieldStore.SourceArgumentIndex - 1, out string? parameterName))
                return null;
            if (FindField(reader, declaringType, fieldStore.FieldName) is not { } fieldHandle)
                return null;

            var field = reader.GetFieldDefinition(fieldHandle);
            string fieldType;
            try
            {
                fieldType = GuardedSignatureText.FieldText(reader, field, GenericContext.ForType(reader, declaringType));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }

            string fieldName = fieldStore.BackingPropertyName
                ?? fieldStore.FieldName;
            initializerTexts.Add((fieldName, parameterName));
            fieldInitializers.Add(new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(
                    CompileBackTypeIdentity.FromDefinition(reader, declaringType).FullName,
                    Identifier(fieldName),
                    0,
                    $"field {fieldType}"),
                CompileBackMemberKind.Field,
                field.Attributes.HasFlag(FieldAttributes.Static),
                Parameters: [],
                CompileBackTypeSignature.Display(fieldType),
                TypeParameters: [],
                CompileBackStubBodyKind.FieldInitializer,
                parameterName,
                [new CompileBackFact("metadata", "primary-constructor-field-initializer", fieldName)],
                DeclarationSignature: FixedBufferDeclarationSignature(reader, field, fieldName)));
        }

        if (fieldInitializers.Count == 0)
            return null;
        if (!RenderedBodyMatchesPrimaryConstructorInitializers(renderedBody, initializerTexts))
            return null;

        var parameters = MethodParameters(reader, method, GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, declaringType, method)));
        return new CompileBackPrimaryConstructor(
            string.Join(", ", parameters.Select(RenderParameter)),
            parameters,
            fieldInitializers);
    }

    static CompileBackPrimaryConstructor? PrimaryConstructorFromCapturedFields(
        MetadataReader reader,
        TypeDefinition typeDef,
        string renderedBody)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return null;

        var parameters = new List<CompileBackParameter>();
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (field.Attributes.HasFlag(FieldAttributes.Static))
                continue;

            string fieldName = reader.GetString(field.Name);
            if (!TryPrimaryConstructorParameterName(fieldName, out var parameterName)
                || !renderedBody.Contains(parameterName, StringComparison.Ordinal))
            {
                continue;
            }

            string fieldType;
            try
            {
                fieldType = GuardedSignatureText.FieldText(reader, field, GenericContext.ForType(reader, typeDef));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }

            if (fieldType.Contains("delegate*", StringComparison.Ordinal)
                || fieldType.Contains("@delegate*", StringComparison.Ordinal))
                return null;

            parameters.Add(new CompileBackParameter(
                Identifier(parameterName),
                CompileBackTypeSignature.Display(fieldType),
                Modifier: null,
                Attributes: [],
                HasDefault: false,
                DefaultValueText: null));
        }

        return parameters.Count == 0
            ? null
            : new CompileBackPrimaryConstructor(
                string.Join(", ", parameters.Select(RenderParameter)),
                parameters,
                FieldInitializers: []);
    }

    static bool TryPrimaryConstructorParameterName(string fieldName, out string parameterName)
    {
        if (fieldName is ['<', ..]
            && fieldName.EndsWith(">P", StringComparison.Ordinal)
            && fieldName.IndexOf('>') == fieldName.Length - 2)
        {
            parameterName = fieldName[1..^2];
            return parameterName.Length > 0;
        }

        parameterName = "";
        return false;
    }

    static string RenderParameter(CompileBackParameter parameter)
    {
        var (type, modifier) = NormalizeParameter(parameter);
        var declaration = string.IsNullOrWhiteSpace(modifier)
            ? $"{type} {parameter.Name}"
            : $"{modifier} {type} {parameter.Name}";
        if (parameter.HasDefault && parameter.DefaultValueText is { Length: > 0 })
            declaration = $"{declaration} = {parameter.DefaultValueText}";
        return parameter.Attributes is { Count: > 0 }
            ? $"[{string.Join(", ", parameter.Attributes)}] {declaration}"
            : declaration;
    }

    static (string Type, string? Modifier) NormalizeParameter(CompileBackParameter parameter)
    {
        string type = parameter.Type.DisplayName;
        string? modifier = parameter.Modifier;
        if (type.StartsWith("ref ", StringComparison.Ordinal))
        {
            type = type["ref ".Length..];
            modifier ??= "ref";
        }

        return (type, modifier);
    }

    static Dictionary<int, string> ParameterNames(MetadataReader reader, MethodDefinition method)
    {
        var names = new Dictionary<int, string>();
        foreach (var parameterHandle in method.GetParameters())
        {
            var parameter = reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber >= 1)
                names[parameter.SequenceNumber - 1] = Identifier(reader.GetString(parameter.Name));
        }
        return names;
    }

    static bool RenderedBodyMatchesPrimaryConstructorInitializers(
        string renderedBody,
        IReadOnlyList<(string Field, string Value)> initializers)
    {
        var lines = renderedBody
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length != initializers.Count)
            return false;

        for (int i = 0; i < initializers.Count; i++)
        {
            var (field, value) = initializers[i];
            string fieldName = Identifier(field);
            string expectedBare = $"{fieldName} = {value};";
            string expectedThis = $"this.{fieldName} = {value};";
            if (lines[i] != expectedBare && lines[i] != expectedThis)
                return false;
        }
        return true;
    }

    static FieldDefinitionHandle? FindField(MetadataReader reader, TypeDefinition typeDef, string name)
    {
        foreach (var fieldHandle in typeDef.GetFields())
        {
            if (reader.GetString(reader.GetFieldDefinition(fieldHandle).Name) == name)
                return fieldHandle;
        }
        return null;
    }

    static IReadOnlyList<CompileBackMemberRequirement> TargetBackingFieldReadMembers(
        MetadataReader reader,
        TypeDefinition typeDef,
        CompileBackTypeIdentity targetIdentity,
        IrFunction function)
    {
        var members = new List<CompileBackMemberRequirement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reference in MemberBodyFacts.BackingFieldReferences(function)
            .Where(reference => reference.Access == BackingFieldAccess.Read))
        {
            if (reference.BackingPropertyName is not { Length: > 0 } propertyName)
                continue;
            if (!DeclaredBySelfType(reference, targetIdentity))
                continue;
            if (FindField(reader, typeDef, reference.FieldName) is not { } fieldHandle)
                continue;

            var field = reader.GetFieldDefinition(fieldHandle);
            if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, field.GetCustomAttributes()))
                continue;

            string memberName = Identifier(propertyName);
            if (!seen.Add(memberName))
                continue;

            string fieldType;
            try
            {
                fieldType = GuardedSignatureText.FieldText(reader, field, GenericContext.ForType(reader, typeDef));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                continue;
            }

            members.Add(new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(targetIdentity.FullName, memberName, 0, $"field {fieldType}"),
                CompileBackMemberKind.Field,
                field.Attributes.HasFlag(FieldAttributes.Static),
                Parameters: [],
                CompileBackTypeSignature.Display(fieldType),
                TypeParameters: [],
                CompileBackStubBodyKind.None,
                TargetBody: null,
                [new CompileBackFact("metadata", "target-backing-field-read", reference.FieldName)],
                DeclarationSignature: FixedBufferDeclarationSignature(reader, field, propertyName)));
        }

        return members;
    }

    static IReadOnlyList<CompileBackMemberRequirement> TargetBackingFieldWriteMembers(
        MetadataReader reader,
        TypeDefinition typeDef,
        CompileBackTypeIdentity targetIdentity,
        IrFunction function,
        bool allowStaticStores)
    {
        var members = new List<CompileBackMemberRequirement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reference in MemberBodyFacts.BackingFieldReferences(function)
            .Where(reference => reference.Access == BackingFieldAccess.InstanceWrite
                || (allowStaticStores && reference.Access == BackingFieldAccess.StaticWrite)))
        {
            if (reference.BackingPropertyName is not { Length: > 0 } propertyName)
                continue;
            if (!DeclaredBySelfType(reference, targetIdentity))
                continue;
            if (FindField(reader, typeDef, reference.FieldName) is not { } fieldHandle)
                continue;

            var field = reader.GetFieldDefinition(fieldHandle);
            if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, field.GetCustomAttributes()))
                continue;

            string memberName = Identifier(propertyName);
            if (!seen.Add(memberName))
                continue;

            string fieldType;
            try
            {
                fieldType = GuardedSignatureText.FieldText(reader, field, GenericContext.ForType(reader, typeDef));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                continue;
            }

            members.Add(new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(targetIdentity.FullName, memberName, 0, $"property {fieldType}"),
                CompileBackMemberKind.PropertyGet,
                field.Attributes.HasFlag(FieldAttributes.Static),
                Parameters: [],
                CompileBackTypeSignature.Display(fieldType),
                TypeParameters: [],
                CompileBackStubBodyKind.AutoProperty,
                TargetBody: null,
                [new CompileBackFact("metadata", "target-backing-field-write", reference.FieldName)]));
        }

        return members;
    }

    static int CountInstanceConstructors(MetadataReader reader, TypeDefinition typeDef)
    {
        int count = 0;
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) == ".ctor"
                && !method.Attributes.HasFlag(MethodAttributes.Static))
                count++;
        }
        return count;
    }

    static bool HasInAssemblyDerivedType(MetadataReader reader, TypeDefinitionHandle baseHandle)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (typeHandle == baseHandle)
                continue;
            var type = reader.GetTypeDefinition(typeHandle);
            if (type.BaseType.Kind == HandleKind.TypeDefinition
                && (TypeDefinitionHandle)type.BaseType == baseHandle)
                return true;
        }
        return false;
    }

    static bool IsAutoProperty(
        MetadataReader reader,
        TypeDefinition typeDef,
        PropertyDefinition property,
        MethodDefinitionHandle getterHandle,
        string propertyType)
    {
        var accessors = property.GetAccessors();
        if (accessors.Getter.IsNil || accessors.Getter != getterHandle)
            return false;
        var getter = reader.GetMethodDefinition(getterHandle);
        if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, getter.GetCustomAttributes()))
            return false;

        string propertyName = reader.GetString(property.Name);
        string backingName = $"<{propertyName}>k__BackingField";
        bool isStatic = getter.Attributes.HasFlag(MethodAttributes.Static);
        var context = GenericContext.ForType(reader, typeDef);
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (reader.GetString(field.Name) != backingName)
                continue;
            if (field.Attributes.HasFlag(FieldAttributes.Static) != isStatic)
                continue;
            if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, field.GetCustomAttributes()))
                return false;
            try
            {
                return CompileBackTypeSignature.Display(GuardedSignatureText.FieldText(reader, field, context)).DisplayName == propertyType;
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return false;
            }

        }

        return false;
    }

    static bool IsAutoPropertySetter(
        MetadataReader reader,
        TypeDefinition typeDef,
        PropertyDefinition property,
        MethodDefinitionHandle setterHandle,
        string propertyType)
    {
        var accessors = property.GetAccessors();
        if (accessors.Setter.IsNil || accessors.Setter != setterHandle)
            return false;
        var setter = reader.GetMethodDefinition(setterHandle);
        if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, setter.GetCustomAttributes()))
            return false;

        string propertyName = reader.GetString(property.Name);
        string backingName = $"<{propertyName}>k__BackingField";
        bool isStatic = setter.Attributes.HasFlag(MethodAttributes.Static);
        var context = GenericContext.ForType(reader, typeDef);
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (reader.GetString(field.Name) != backingName)
                continue;
            if (field.Attributes.HasFlag(FieldAttributes.Static) != isStatic)
                continue;
            if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, field.GetCustomAttributes()))
                return false;
            try
            {
                return CompileBackTypeSignature.Display(GuardedSignatureText.FieldText(reader, field, context)).DisplayName == propertyType;
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }

        return false;
    }

    static CompileBackTypeKind ShellKind(MetadataReader reader, TypeDefinition typeDef, IReadOnlyList<CompileBackFact>? facts = null)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return CompileBackTypeKind.Interface;
        if (IsGeneratedDynamicDelegate(reader, typeDef))
            return CompileBackTypeKind.Delegate;

        string baseName = typeDef.BaseType.Kind switch
        {
            HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType)),
            HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType)),
            _ => "",
        };

        if (baseName == "System.Enum")
            return CompileBackTypeKind.Enum;
        if (baseName == "System.ValueType")
            return CompileBackTypeKind.Struct;
        if (facts?.Any(fact => fact.Producer == "metadata" && fact.Id == "record-shell") == true)
            return CompileBackTypeKind.Record;
        return CompileBackTypeKind.Class;
    }

    static bool IsSupportedClosureRoot(MetadataReader reader, TypeDefinition typeDef)
    {
        string name = reader.GetString(typeDef.Name);
        if (IsGeneratedDynamicDelegate(reader, typeDef))
            return true;

        return name is not "<Module>"
            && !name.Contains('<', StringComparison.Ordinal)
            && !name.Contains('`', StringComparison.Ordinal)
            && !IsDelegate(reader, typeDef);
    }

    static bool IsDelegate(MetadataReader reader, TypeDefinition typeDef)
    {
        if (typeDef.BaseType.IsNil)
            return false;

        string baseName = typeDef.BaseType.Kind switch
        {
            HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType)),
            HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType)),
            _ => "",
        };

        return baseName is "System.MulticastDelegate" or "System.Delegate";
    }

    static bool IsGeneratedDynamicDelegate(MetadataReader reader, TypeDefinition typeDef)
        => IsDelegate(reader, typeDef)
            && reader.GetString(typeDef.Name).StartsWith("<>A{", StringComparison.Ordinal);

    static string FullName(MetadataReader reader, TypeReference type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static string FullName(MetadataReader reader, TypeDefinition type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static TypeDefinitionHandle TopLevelRootOf(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var declaring = reader.GetTypeDefinition(handle).GetDeclaringType();
        return declaring.IsNil ? handle : TopLevelRootOf(reader, declaring);
    }

    static string Clean(string type) => CSharpFormatter.CleanTypeDisplay(type);

    static string StripArity(string name) => CSharpFormatter.StripArity(name);

    static string Identifier(string name) => CSharpIdentifier.Sanitize(name);

    static string? FixedBufferDeclarationSignature(MetadataReader reader, FieldDefinition field, string fieldName)
        => TypeShellProducer.FixedBufferField(reader, field)?.DeclarationSignature(Identifier(fieldName));

    static bool ContainsFixedBufferElementAccess(IrFunction function)
        => function.Descendants.OfType<FixedBufferElementAddress>().Any();

    static string? ExplicitInterfaceMemberName(
        MetadataReader reader,
        string metadataPropertyName)
    {
        int separator = metadataPropertyName.LastIndexOf('.');
        if (separator <= 0 || separator == metadataPropertyName.Length - 1)
            return null;

        string interfaceMetadataName = metadataPropertyName[..separator];
        if (TypeProducer.FindType(reader, interfaceMetadataName) is not { } interfaceHandle)
            return null;
        var interfaceDef = reader.GetTypeDefinition(interfaceHandle);
        if (interfaceDef.GetGenericParameters().Count != 0
            || !IsSupportedClosureRoot(reader, interfaceDef))
        {
            return null;
        }

        string interfaceName = Clean(interfaceMetadataName);
        string memberName = CSharpIdentifier.Sanitize(metadataPropertyName[(separator + 1)..]);
        return $"{interfaceName}.{memberName}";
    }

    sealed class TypeProducer
    {
        public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            MethodRef methodRef)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var typeIdentity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            if (TryFindPropertyForAccessor(reader, typeDef, methodRef) is { } propertyHandle)
                return PropertyRequirement(reader, typeDef, typeIdentity, propertyHandle, methodRef.Name);
            if (TryFindMethod(reader, typeDef, methodRef) is { } methodHandle)
                return MethodRequirement(reader, typeDef, typeIdentity, methodHandle);
            return null;
        }

        public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            FieldRef fieldRef)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var typeIdentity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            if (FindField(reader, typeDef, fieldRef.Name) is not { } fieldHandle)
                return null;
            return FieldRequirement(reader, typeDef, typeIdentity, fieldHandle);
        }

        public static CompileBackMemberRequirement? TryCreateRecordEqualityContractRequirement(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var typeIdentity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                if (reader.GetString(property.Name) != "EqualityContract")
                    continue;
                var accessors = property.GetAccessors();
                if (accessors.Getter.IsNil)
                    continue;
                var getter = reader.GetMethodDefinition(accessors.Getter);
                if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, getter.GetCustomAttributes()))
                    continue;
                return PropertyRequirement(reader, typeDef, typeIdentity, propertyHandle, reader.GetString(getter.Name), factId: "record-equality-contract");
            }

            return null;
        }

        static CompileBackMemberRequirement? FieldRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            FieldDefinitionHandle fieldHandle)
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            string fieldType;
            try
            {
                fieldType = GuardedSignatureText.FieldText(reader, field, GenericContext.ForType(reader, typeDef));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }

            string fieldName = reader.GetString(field.Name);
            if (fieldName.Contains('.', StringComparison.Ordinal))
            {
                return null;
            }

            string? fixedBufferSignature = FixedBufferDeclarationSignature(reader, field, fieldName);
            if (fixedBufferSignature is null && IsUnsupportedSurfaceSignature(fieldType))
                return null;

            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, Identifier(fieldName), 0, $"field {fieldType}"),
                CompileBackMemberKind.Field,
                field.Attributes.HasFlag(FieldAttributes.Static),
                [],
                CompileBackTypeSignature.Display(fieldType),
                [],
                TryFormatConstantField(reader, field, out var constant)
                    ? CompileBackStubBodyKind.TargetBody
                    : CompileBackStubBodyKind.None,
                constant,
                [new CompileBackFact("metadata", "typed-closure-field", fieldName)],
                DeclarationSignature: fixedBufferSignature);
        }

        public static TypeProduction Produce(
            MetadataReader reader,
            IReadOnlyList<CompileBackTypeRequirement> requirements,
            List<CompileBackPlanningDiagnostic> diagnostics)
        {
            var requests = new List<CSharpTypePrintRequest>();
            var producedRequirements = new List<CompileBackTypeRequirement>();
            var requirementsByMetadataName = requirements.ToDictionary(
                requirement => requirement.Type.MetadataFullName,
                requirement => requirement,
                StringComparer.Ordinal);
            var emittedRoots = new HashSet<TypeDefinitionHandle>();
            foreach (var requirement in requirements)
            {
                if (FindType(reader, requirement.Type.MetadataFullName) is not { } handle)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("type identity", "type-not-found", requirement.Type.MetadataFullName));
                    continue;
                }

                var rootHandle = TopLevelRootOf(reader, handle);
                if (!emittedRoots.Add(rootHandle))
                    continue;

                var rootDef = reader.GetTypeDefinition(rootHandle);
                var rootIdentity = CompileBackTypeIdentity.FromDefinition(reader, rootDef);
                if (!requirementsByMetadataName.TryGetValue(rootIdentity.MetadataFullName, out var rootRequirement))
                {
                    rootRequirement = new CompileBackTypeRequirement(
                        rootIdentity,
                        ShellKind(reader, rootDef),
                        RequiredMembers: [],
                        PrimaryConstructor: null,
                        SourceFacts: [new CompileBackFact("metadata", "declaring-closure-type", rootIdentity.FullName)]);
                }

                var rootSpec = BuildSpec(
                    reader,
                    rootHandle,
                    rootRequirement,
                    requirementsByMetadataName,
                    producedRequirements,
                    diagnostics);
                requests.Add(TypeShellProducer.BuildPrintRequest(reader, rootSpec));
            }

            return new TypeProduction(requests, producedRequirements);
        }

        public sealed record TypeProduction(
            IReadOnlyList<CSharpTypePrintRequest> Requests,
            IReadOnlyList<CompileBackTypeRequirement> Requirements);

        static PropertyDefinitionHandle? TryFindPropertyForAccessor(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodRef methodRef)
        {
            if (!methodRef.Name.StartsWith("get_", StringComparison.Ordinal)
                && !methodRef.Name.StartsWith("set_", StringComparison.Ordinal))
                return null;

            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                var accessorHandle = methodRef.Name.StartsWith("get_", StringComparison.Ordinal)
                    ? accessors.Getter
                    : accessors.Setter;
                if (accessorHandle.IsNil)
                    continue;
                var accessor = reader.GetMethodDefinition(accessorHandle);
                if (!MethodMatches(reader, typeDef, accessor, methodRef))
                    continue;
                return propertyHandle;
            }

            return null;
        }

        public static MethodDefinitionHandle? TryFindMethod(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodRef methodRef)
        {
            var matches = new List<MethodDefinitionHandle>();
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!MethodMatches(reader, typeDef, method, methodRef))
                    continue;
                matches.Add(methodHandle);
            }

            return matches.Count == 1 ? matches[0] : null;
        }

        static bool MethodMatches(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodDefinition method,
            MethodRef methodRef)
        {
            if (reader.GetString(method.Name) != methodRef.Name)
                return false;
            if (method.GetGenericParameters().Count != methodRef.TypeArguments.Length)
                return false;
            try
            {
                var signature = GuardedDecode.MethodSignature(reader, method, IrImporter.CallerScope(reader, typeDef, method));
                return signature.ParameterTypes.Length == methodRef.ParameterTypes.Length
                    && signature.ParameterTypes.SequenceEqual(methodRef.ParameterTypes);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }

        internal static CompileBackMemberRequirement? PropertyRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            PropertyDefinitionHandle propertyHandle,
            string accessorName,
            string factId = "typed-closure-property")
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var accessors = property.GetAccessors();
            string propertyName = reader.GetString(property.Name);
            if (propertyName.Contains('<', StringComparison.Ordinal))
                return null;
            string? explicitInterfaceMemberName = ExplicitInterfaceMemberName(reader, propertyName);

            MetadataPropertyDeclaration propertyDeclaration;
            try
            {
                propertyDeclaration = MetadataDeclarationQuery.GetProperty(reader, typeDef, property);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }

            if (propertyDeclaration.Signature.ReturnType is not { } propertyReturnType
                || IsUnsupportedSurfaceSignature(propertyReturnType))
                return null;

            var accessor = accessorName.StartsWith("get_", StringComparison.Ordinal) ? accessors.Getter : accessors.Setter;
            var accessorMethod = accessor.IsNil ? default : reader.GetMethodDefinition(accessor);
            bool isStatic = !accessor.IsNil && accessorMethod.Attributes.HasFlag(MethodAttributes.Static);
            var returnType = CompileBackTypeSignature.Display(propertyReturnType);
            bool isAutoProperty = !accessors.Getter.IsNil
                && IsAutoProperty(reader, typeDef, property, accessors.Getter, returnType.DisplayName);
            bool hasSetter = !accessors.Setter.IsNil;
            bool isAbstractAccessor = !accessor.IsNil && propertyDeclaration.IsAbstract;
            var noBodyProperty = (typeDef.Attributes & TypeAttributes.Interface) != 0 || isAbstractAccessor;
            var stubBody = noBodyProperty
                ? hasSetter
                    ? CompileBackStubBodyKind.AutoPropertyGetSet
                    : CompileBackStubBodyKind.None
                : hasSetter && isAutoProperty
                    ? CompileBackStubBodyKind.AutoPropertyGetSet
                    : isAutoProperty
                        ? CompileBackStubBodyKind.AutoProperty
                        : hasSetter
                            ? CompileBackStubBodyKind.ThrowGetSet
                            : CompileBackStubBodyKind.Throw;
            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, Identifier(propertyName), 0, $"property {propertyReturnType}"),
                CompileBackMemberKind.PropertyGet,
                isStatic,
                ToCompileBackParameters(propertyDeclaration.Signature.Parameters),
                returnType,
                [],
                stubBody,
                null,
                [new CompileBackFact("metadata", factId, accessorName)],
                propertyDeclaration.Attributes,
                propertyDeclaration.Signature.ReturnAttributes,
                IsAbstract: isAbstractAccessor,
                IsVirtual: !accessor.IsNil && propertyDeclaration.IsVirtual,
                ExplicitInterfaceMemberName: explicitInterfaceMemberName);
        }

        internal static CompileBackMemberRequirement? EventRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            EventDefinitionHandle eventHandle,
            string accessorName,
            string factId = "typed-closure-event")
        {
            var eventDefinition = reader.GetEventDefinition(eventHandle);
            var accessors = eventDefinition.GetAccessors();
            MethodDefinitionHandle accessorHandle;
            if (!accessors.Adder.IsNil
                && reader.GetString(reader.GetMethodDefinition(accessors.Adder).Name) == accessorName)
            {
                accessorHandle = accessors.Adder;
            }
            else if (!accessors.Remover.IsNil
                && reader.GetString(reader.GetMethodDefinition(accessors.Remover).Name) == accessorName)
            {
                accessorHandle = accessors.Remover;
            }
            else
            {
                return null;
            }

            var accessor = reader.GetMethodDefinition(accessorHandle);
            MethodSignature<string> signature;
            IReadOnlyList<CompileBackParameter> parameters;
            try
            {
                signature = GuardedSignatureText.MethodText(
                    reader,
                    accessor,
                    GenericContext.ForMethod(reader, typeDef, accessor));
                parameters = MethodParameters(reader, accessor, signature);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }
            if (parameters.Count != 1)
                return null;

            string eventName = Identifier(reader.GetString(eventDefinition.Name));
            bool isAbstract = IsAbstractMethod(accessor);
            bool hasNoBody = (typeDef.Attributes & TypeAttributes.Interface) != 0 || isAbstract;
            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(
                    typeIdentity.FullName,
                    eventName,
                    0,
                    $"event {parameters[0].Type.DisplayName}"),
                accessorHandle == accessors.Adder
                    ? CompileBackMemberKind.EventAdd
                    : CompileBackMemberKind.EventRemove,
                accessor.Attributes.HasFlag(MethodAttributes.Static),
                [],
                parameters[0].Type,
                [],
                hasNoBody ? CompileBackStubBodyKind.None : CompileBackStubBodyKind.Throw,
                null,
                [new CompileBackFact("metadata", factId, accessorName)],
                MemberAttributes(reader, eventDefinition.GetCustomAttributes()),
                IsAbstract: isAbstract,
                IsVirtual: IsVirtualMethod(accessor));
        }

        static CompileBackMemberRequirement? MethodRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            MethodDefinitionHandle methodHandle)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            string name = reader.GetString(method.Name);
            bool isConstructor = name == ".ctor";
            if (name == ".cctor"
                || (name.Contains('<', StringComparison.Ordinal)
                    && CSharpNaming.MethodName(name) == name)
                || (!isConstructor && name.Contains('.', StringComparison.Ordinal)))
                return null;

            if (!isConstructor
                && method.Attributes.HasFlag(MethodAttributes.SpecialName)
                && !name.StartsWith("op_", StringComparison.Ordinal))
            {
                return null;
            }

            MethodSignature<string> signature;
            try
            {
                signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }

            var generatedLocalFunction = IsGeneratedLocalFunctionName(name);
            var methodDeclaration = generatedLocalFunction
                ? null
                : MetadataDeclarationQuery.GetMethod(reader, typeDef, method, signature);
            var parameters = generatedLocalFunction
                ? Parameters(reader, method, signature)
                : ToCompileBackParameters(methodDeclaration!.Signature.Parameters);
            var methodReturnType = generatedLocalFunction
                ? signature.ReturnType
                : methodDeclaration!.Signature.ReturnType;
            if (methodReturnType is null
                || IsUnsupportedSurfaceSignature(methodReturnType)
                || parameters.Any(parameter => IsUnsupportedSurfaceSignature(parameter.Type.DisplayName)))
            {
                return null;
            }

            string identifierName = CSharpNaming.SourceMethodName(name);
            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, identifierName, DeclaringOverloadIndex(reader, typeDef, methodHandle, name), MethodSignatureText(identifierName, signature)),
                isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method,
                method.Attributes.HasFlag(MethodAttributes.Static),
                parameters,
                isConstructor ? null : CompileBackTypeSignature.Display(methodReturnType),
                generatedLocalFunction ? [] : ToCompileBackTypeParameters(methodDeclaration!.Signature.TypeParameters),
                (typeDef.Attributes & TypeAttributes.Interface) != 0 || IsAbstractMethod(method)
                    ? CompileBackStubBodyKind.None
                    : CompileBackStubBodyKind.Throw,
                null,
                [new CompileBackFact("metadata", isConstructor ? "typed-closure-constructor" : "typed-closure-method", name)],
                isConstructor ? null : methodDeclaration?.Attributes,
                isConstructor ? null : methodDeclaration?.Signature.ReturnAttributes,
                IsAbstract: !isConstructor && IsAbstractMethod(method),
                IsVirtual: !isConstructor && IsVirtualMethod(method),
                IsOverride: false,
                IsSealed: false,
                IsExtension: IsExtensionMethod(reader, typeDef, method));
        }

        static bool IsExtensionMethod(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
            => typeDef.Attributes.HasFlag(TypeAttributes.Abstract)
               && typeDef.Attributes.HasFlag(TypeAttributes.Sealed)
               && method.Attributes.HasFlag(MethodAttributes.Static)
               && AttributeReader.HasExtensionAttribute(reader, typeDef.GetCustomAttributes())
               && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());

        static int DeclaringOverloadIndex(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle target, string name)
        {
            int index = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) != name)
                    continue;
                if (methodHandle == target)
                    return index;
                index++;
            }

            return index;
        }

        static CSharpTypeShellSpec BuildSpec(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            CompileBackTypeRequirement requirement,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName,
            List<CompileBackTypeRequirement> producedRequirements,
            List<CompileBackPlanningDiagnostic> diagnostics)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var kind = requirement.RequiredKind;
            var members = kind == CompileBackTypeKind.Delegate
                ? [DelegateInvokeRequirement(reader, typeDef, requirement.Type)]
                : RequiredMemberRequirements(requirement);
            bool includeMemberSurface = requirement.IncludeMemberSurface;
            if (includeMemberSurface && kind != CompileBackTypeKind.Delegate)
                AddClosureMemberSurface(reader, typeDef, requirement, members, diagnostics);
            if (kind is CompileBackTypeKind.Class or CompileBackTypeKind.Record or CompileBackTypeKind.Struct)
            {
                AddRequiredInterfaceProperties(
                    reader,
                    typeDef,
                    requirement,
                    requirementsByMetadataName,
                    members);
            }
            // When this class is reconstructed as the base of another shell type, a
            // derived stub constructor emits an implicit `: base()`. If the class has
            // only parameterized constructors (no accessible parameterless one), that
            // implicit call fails to bind (CS7036/CS1729). Synthesize a parameterless
            // constructor so base-class reconstruction never breaks the derived shell;
            // at worst the derived constructor stays at its pre-existing opcode diff.
            if (kind == CompileBackTypeKind.Class
                && members.Any(member => member.Kind == CompileBackMemberKind.Constructor)
                && !members.Any(member => member.Kind == CompileBackMemberKind.Constructor && member.Parameters.Count == 0)
                && IsReconstructedBaseOfAnotherType(reader, requirement, requirementsByMetadataName))
            {
                members.Add(SyntheticParameterlessConstructor(requirement.Type));
            }
            var producedRequirement = requirement with { RequiredMembers = members };
            producedRequirements.Add(producedRequirement);

            var primaryConstructorParameters = requirement.PrimaryConstructor?.ParameterList
                .Select(ToApiParameter)
                .ToArray() ?? [];
            var policies = members
                .Select(member => ToMemberPolicy(member, primaryConstructorParameters.Length))
                .ToArray();

            return new CSharpTypeShellSpec(
                Handle: handle,
                Namespace: requirement.Type.Namespace,
                MetadataName: requirement.Type.MetadataName,
                Kind: ToShellKind(kind),
                InterfaceDisplayNames: InterfaceSignatures(reader, typeDef)
                    .Select(signature => signature.DisplayName)
                    .ToList(),
                MemberPolicies: policies,
                PrimaryConstructorParameters: primaryConstructorParameters,
                NestedTypes: NestedSpecs(
                    reader,
                    typeDef,
                    requirementsByMetadataName,
                    includeMemberSurface,
                    producedRequirements,
                    diagnostics));
        }

        static void AddRequiredInterfaceProperties(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeRequirement requirement,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName,
            List<CompileBackMemberRequirement> members)
        {
            foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = reader.GetInterfaceImplementation(implementationHandle);
                if (implementation.Interface.Kind != HandleKind.TypeDefinition)
                    continue;

                var interfaceDef = reader.GetTypeDefinition(
                    (TypeDefinitionHandle)implementation.Interface);
                var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
                if (!requirementsByMetadataName.TryGetValue(
                        interfaceIdentity.MetadataFullName,
                        out var interfaceRequirement))
                {
                    continue;
                }

                var interfaceMembers = RequiredMemberRequirements(interfaceRequirement);
                if (interfaceRequirement.IncludeMemberSurface)
                {
                    // The interface's own BuildSpec call reports surface diagnostics.
                    AddClosureMemberSurface(
                        reader,
                        interfaceDef,
                        interfaceRequirement,
                        interfaceMembers,
                        diagnostics: []);
                }

                foreach (var interfaceMember in interfaceMembers.Where(
                    member => member.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet))
                {
                    var propertyHandle = ImplementingProperty(
                        reader,
                        typeDef,
                        interfaceDef,
                        interfaceMember);
                    if (propertyHandle.IsNil)
                        continue;
                    var propertyDef = reader.GetPropertyDefinition(propertyHandle);
                    var accessors = propertyDef.GetAccessors();
                    var accessor = interfaceMember.Kind == CompileBackMemberKind.PropertyGet
                        ? accessors.Getter
                        : accessors.Setter;
                    if (accessor.IsNil)
                        continue;

                    var property = PropertyRequirement(
                        reader,
                        typeDef,
                        requirement.Type,
                        propertyHandle,
                        reader.GetString(reader.GetMethodDefinition(accessor).Name),
                        "required-interface-property");
                    if (property is not null
                        && !members.Any(existing => SameMemberShape(existing, property)))
                    {
                        members.Add(property);
                    }
                }
            }
        }

        static PropertyDefinitionHandle ImplementingProperty(
            MetadataReader reader,
            TypeDefinition typeDef,
            TypeDefinition interfaceDef,
            CompileBackMemberRequirement interfaceMember)
        {
            var interfaceProperty = interfaceDef.GetProperties().FirstOrDefault(handle =>
                Identifier(reader.GetString(reader.GetPropertyDefinition(handle).Name))
                    == interfaceMember.Identity.Method);
            if (interfaceProperty.IsNil)
                return default;

            var interfaceAccessors = reader.GetPropertyDefinition(interfaceProperty).GetAccessors();
            var declaration = interfaceMember.Kind == CompileBackMemberKind.PropertyGet
                ? interfaceAccessors.Getter
                : interfaceAccessors.Setter;
            foreach (var implementationHandle in typeDef.GetMethodImplementations())
            {
                var implementation = reader.GetMethodImplementation(implementationHandle);
                if (implementation.MethodDeclaration == declaration
                    && implementation.MethodBody.Kind == HandleKind.MethodDefinition)
                {
                    return PropertyForAccessor(
                        reader,
                        typeDef,
                        (MethodDefinitionHandle)implementation.MethodBody);
                }
            }

            string interfaceName = CompileBackTypeIdentity.FromDefinition(
                reader,
                interfaceDef).MetadataFullName;
            string propertyName = reader.GetString(
                reader.GetPropertyDefinition(interfaceProperty).Name);
            return typeDef.GetProperties().FirstOrDefault(handle =>
            {
                string candidateName = reader.GetString(reader.GetPropertyDefinition(handle).Name);
                return candidateName == propertyName
                    || candidateName == $"{interfaceName}.{propertyName}";
            });
        }

        static PropertyDefinitionHandle PropertyForAccessor(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodDefinitionHandle accessor)
        {
            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
                if (accessors.Getter == accessor || accessors.Setter == accessor)
                    return propertyHandle;
            }
            return default;
        }

        internal static bool SameMemberShape(
            CompileBackMemberRequirement left,
            CompileBackMemberRequirement right)
        {
            if (SameMemberDeclaration(left, right))
                return true;
            bool bothProperties = left.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet
                && right.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet;
            bool bothEvents = left.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove
                && right.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove;
            if (!bothProperties && !bothEvents)
            {
                return false;
            }

            string leftName = left.ExplicitInterfaceMemberName ?? left.Identity.Method;
            string rightName = right.ExplicitInterfaceMemberName ?? right.Identity.Method;
            return leftName == rightName
                && left.ReturnType == right.ReturnType
                && SameParameters(left.Parameters, right.Parameters);
        }

        static CSharpTypeShellKind ToShellKind(CompileBackTypeKind kind)
            => kind switch
            {
                CompileBackTypeKind.Class => CSharpTypeShellKind.Class,
                CompileBackTypeKind.Record => CSharpTypeShellKind.Record,
                CompileBackTypeKind.Struct => CSharpTypeShellKind.Struct,
                CompileBackTypeKind.Interface => CSharpTypeShellKind.Interface,
                CompileBackTypeKind.Enum => CSharpTypeShellKind.Enum,
                CompileBackTypeKind.Delegate => CSharpTypeShellKind.Delegate,
                _ => throw new NotSupportedException($"Unsupported RTS type kind '{kind}'."),
            };
        static CompileBackMemberRequirement DelegateInvokeRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity)
        {
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != "Invoke")
                    continue;

                var signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
                return new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(typeIdentity.FullName, "Invoke", 0, MethodSignatureText("Invoke", signature)),
                    CompileBackMemberKind.Method,
                    IsStatic: false,
                    Parameters: Parameters(reader, method, signature),
                    ReturnType: CompileBackTypeSignature.Display(signature.ReturnType),
                    TypeParameters: [],
                    StubBody: CompileBackStubBodyKind.None,
                    TargetBody: null,
                    [new CompileBackFact("metadata", "generated-dynamic-delegate-invoke", reader.GetString(typeDef.Name))]);
            }

            throw new InvalidOperationException($"Generated dynamic delegate '{typeIdentity.MetadataFullName}' has no Invoke method.");
        }

        static List<CompileBackMemberRequirement> RequiredMemberRequirements(CompileBackTypeRequirement requirement)
            => requirement.RequiredMembers
                .Select(member => member with { Accessibility = CompileBackAccessibility.Public })
                .ToList();

        static IReadOnlyList<CSharpTypeShellSpec> NestedSpecs(
            MetadataReader reader,
            TypeDefinition typeDef,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName,
            bool includeMemberSurface,
            List<CompileBackTypeRequirement> producedRequirements,
            List<CompileBackPlanningDiagnostic> diagnostics)
        {
            var nestedTypes = new List<CSharpTypeShellSpec>();
            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                var nestedDef = reader.GetTypeDefinition(nestedHandle);
                string name = reader.GetString(nestedDef.Name);
                if (IsDelegate(reader, nestedDef) && !IsGeneratedDynamicDelegate(reader, nestedDef))
                {
                    continue;
                }

                var identity = CompileBackTypeIdentity.FromDefinition(reader, nestedDef);
                requirementsByMetadataName.TryGetValue(identity.MetadataFullName, out var requirement);
                var kind = requirement?.RequiredKind ?? ShellKind(reader, nestedDef);
                requirement ??= new CompileBackTypeRequirement(
                    identity,
                    kind,
                    RequiredMembers: [],
                    PrimaryConstructor: null,
                    SourceFacts: [new CompileBackFact("metadata", "nested-closure-type", identity.FullName)]);
                bool includeNestedMemberSurface = includeMemberSurface
                    || requirement.IncludeMemberSurface
                    || IsGeneratedMetadataName(name);
                var nestedRequirement = includeNestedMemberSurface
                    ? requirement with { IncludeMemberSurface = true }
                    : requirement;
                nestedTypes.Add(BuildSpec(
                    reader,
                    nestedHandle,
                    nestedRequirement,
                    requirementsByMetadataName,
                    producedRequirements,
                    diagnostics));
            }

            if (HasGeneratedCallSiteCache(reader, typeDef))
            {
                foreach (var delegateHandle in GeneratedDynamicDelegates(reader))
                {
                    var delegateDef = reader.GetTypeDefinition(delegateHandle);
                    var identity = CompileBackTypeIdentity.FromDefinition(reader, delegateDef);
                    if (nestedTypes.Any(spec => spec.MetadataName == identity.MetadataName))
                        continue;

                    nestedTypes.Add(BuildSpec(
                        reader,
                        delegateHandle,
                        new CompileBackTypeRequirement(
                            identity,
                            CompileBackTypeKind.Delegate,
                            RequiredMembers: [],
                            PrimaryConstructor: null,
                            SourceFacts: [new CompileBackFact("metadata", "generated-dynamic-delegate", identity.FullName)]),
                        requirementsByMetadataName,
                        producedRequirements,
                        diagnostics));
                }
            }

            return nestedTypes;
        }

        static bool HasGeneratedCallSiteCache(MetadataReader reader, TypeDefinition typeDef)
        {
            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                var nestedDef = reader.GetTypeDefinition(nestedHandle);
                if (reader.GetString(nestedDef.Name).StartsWith("<>o__", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static IEnumerable<TypeDefinitionHandle> GeneratedDynamicDelegates(MetadataReader reader)
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                if (IsGeneratedDynamicDelegate(reader, reader.GetTypeDefinition(handle)))
                    yield return handle;
            }
        }

        // True when some other reconstructed shell type — top-level or nested —
        // derives from this class via a reconstructed (same-assembly) base
        // declaration, so its implicit `: base()` depends on this class exposing an
        // accessible parameterless constructor. Nested types are emitted by
        // NestedTypes() from their enclosing requirement and are not present in
        // requirementsByMetadataName, so each requirement's nested tree is walked.
        static bool IsReconstructedBaseOfAnotherType(
            MetadataReader reader,
            CompileBackTypeRequirement requirement,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName)
        {
            string metadataFullName = requirement.Type.MetadataFullName;
            foreach (var other in requirementsByMetadataName.Values)
            {
                if (FindType(reader, other.Type.MetadataFullName) is not { } otherHandle)
                    continue;
                if (TypeOrNestedDerivesFrom(reader, otherHandle, metadataFullName))
                    return true;
            }

            return false;
        }

        static bool TypeOrNestedDerivesFrom(MetadataReader reader, TypeDefinitionHandle handle, string baseMetadataFullName)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (CompileBackTypeIdentity.FromDefinition(reader, typeDef).MetadataFullName != baseMetadataFullName
                && ReconstructedSameAssemblyBaseName(reader, handle, ShellKind(reader, typeDef)) == baseMetadataFullName)
            {
                return true;
            }

            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                if (TypeOrNestedDerivesFrom(reader, nestedHandle, baseMetadataFullName))
                    return true;
            }

            return false;
        }

        // The metadata full name of the class's reconstructed same-assembly base, or
        // null when the base is not reconstructed (external base, or a kind that keeps
        // its compiler-implied base).
        static string? ReconstructedSameAssemblyBaseName(MetadataReader reader, TypeDefinitionHandle handle, CompileBackTypeKind kind)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (typeDef.BaseType.Kind != HandleKind.TypeDefinition)
                return null;
            if (TypeShellProducer.ReconstructedBaseTypeDisplay(reader, typeDef, kind == CompileBackTypeKind.Class) is null)
                return null;
            var baseDef = reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType);
            return CompileBackTypeIdentity.FromDefinition(reader, baseDef).MetadataFullName;
        }

        static CompileBackMemberRequirement SyntheticParameterlessConstructor(CompileBackTypeIdentity typeIdentity)
            => new(
                new CompileBackMethodIdentity(typeIdentity.FullName, ".ctor", 0, "synthetic-base-parameterless-constructor()"),
                CompileBackMemberKind.Constructor,
                IsStatic: false,
                Parameters: [],
                ReturnType: null,
                TypeParameters: [],
                CompileBackStubBodyKind.Throw,
                TargetBody: null,
                [new CompileBackFact("synthetic", "base-parameterless-constructor", typeIdentity.MetadataFullName)]);

        static IReadOnlyList<CompileBackTypeSignature> InterfaceSignatures(MetadataReader reader, TypeDefinition typeDef)
        {
            if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
                return [];

            var interfaces = new List<CompileBackTypeSignature>();
            foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = reader.GetInterfaceImplementation(implementationHandle);
                if (implementation.Interface.Kind != HandleKind.TypeDefinition)
                    continue;

                var interfaceDef = reader.GetTypeDefinition((TypeDefinitionHandle)implementation.Interface);
                if (interfaceDef.GetGenericParameters().Count != 0 || !IsSupportedClosureRoot(reader, interfaceDef))
                    continue;

                interfaces.Add(CompileBackTypeSignature.Definition(CompileBackTypeIdentity.FromDefinition(reader, interfaceDef)));
            }

            return interfaces;
        }

        static void AddClosureMemberSurface(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeRequirement requirement,
            List<CompileBackMemberRequirement> members,
            List<CompileBackPlanningDiagnostic> diagnostics,
            bool allowUnsafeSurface = false)
        {
            if (requirement.RequiredKind == CompileBackTypeKind.Enum)
            {
                // An enum reconstructed as a closure supporting type must carry its
                // named members: the target body can reference any of them by name, and
                // a member-less `enum { }` shell fails to bind those references (CS0117).
                // Emit each literal member with its constant value so references resolve
                // and keep their numeric identity. The special `value__` storage field
                // (not a literal) and any name-mangled fields are not enum members.
                foreach (var enumFieldHandle in typeDef.GetFields())
                {
                    var enumField = reader.GetFieldDefinition(enumFieldHandle);
                    if (!enumField.Attributes.HasFlag(FieldAttributes.Literal))
                        continue;
                    string enumMemberName = reader.GetString(enumField.Name);
                    if (enumMemberName.Contains('.', StringComparison.Ordinal))
                        continue;
                    if (!TryFormatConstantField(reader, enumField, out var enumConstant))
                        continue;
                    if (members.Any(member => member.Kind == CompileBackMemberKind.Field
                            && member.Identity.Method == Identifier(enumMemberName)))
                        continue;
                    members.Add(new CompileBackMemberRequirement(
                        new CompileBackMethodIdentity(requirement.Type.FullName, Identifier(enumMemberName), 0, "enum-member"),
                        CompileBackMemberKind.Field,
                        IsStatic: true,
                        Parameters: [],
                        ReturnType: CompileBackTypeSignature.Display(requirement.Type.FullName),
                        TypeParameters: [],
                        StubBody: CompileBackStubBodyKind.TargetBody,
                        TargetBody: enumConstant,
                        [new CompileBackFact("metadata", "enum-member", enumMemberName)]));
                }
                return;
            }

            allowUnsafeSurface = allowUnsafeSurface
                || requirement.RequiredMembers.Count != 0
                || requirement.IncludeMemberSurface;
            var accessorMethods = new HashSet<MethodDefinitionHandle>();
            var typeContext = GenericContext.ForType(reader, typeDef);
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                string fieldName = reader.GetString(field.Name);
                if (fieldName.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }
                if (members.Any(member => member.Kind == CompileBackMemberKind.Field && member.Identity.Method == Identifier(fieldName)))
                    continue;

                string fieldType;
                try
                {
                    fieldType = GuardedSignatureText.FieldText(reader, field, typeContext);
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "field-signature-decode-failed", fieldName));
                    continue;
                }
                string? fixedBufferSignature = FixedBufferDeclarationSignature(reader, field, fieldName);
                if ((fixedBufferSignature is null && IsUnsupportedSurfaceSignature(fieldType))
                    || (!allowUnsafeSurface && IsPointerSignature(fieldType)))
                    continue;

                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(requirement.Type.FullName, Identifier(fieldName), 0, $"field {fieldType}"),
                    CompileBackMemberKind.Field,
                    IsStatic: field.Attributes.HasFlag(FieldAttributes.Static),
                    Parameters: [],
                    ReturnType: CompileBackTypeSignature.Display(fieldType),
                    TypeParameters: [],
                    StubBody: TryFormatConstantField(reader, field, out var constant)
                        ? CompileBackStubBodyKind.TargetBody
                        : CompileBackStubBodyKind.None,
                    TargetBody: constant,
                    [new CompileBackFact("metadata", "closure-field", fieldName)],
                    DeclarationSignature: fixedBufferSignature));
            }

            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (!accessors.Getter.IsNil)
                    accessorMethods.Add(accessors.Getter);
                if (!accessors.Setter.IsNil)
                    accessorMethods.Add(accessors.Setter);

                string propertyName = reader.GetString(property.Name);
                if (propertyName.Contains('<', StringComparison.Ordinal)
                    || propertyName.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }
                if (members.Any(member =>
                        (member.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet or CompileBackMemberKind.Field)
                        && member.Identity.Method == Identifier(propertyName)))
                    continue;

                MetadataPropertyDeclaration propertyDeclaration;
                try
                {
                    propertyDeclaration = MetadataDeclarationQuery.GetProperty(reader, typeDef, property);
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "property-signature-decode-failed", propertyName));
                    continue;
                }

                if (propertyDeclaration.Signature.Parameters.Count != 0)
                    continue;
                if (propertyDeclaration.Signature.ReturnType is not { } propertyReturnType
                    || IsUnsupportedSurfaceSignature(propertyReturnType)
                    || (!allowUnsafeSurface && IsPointerSignature(propertyReturnType)))
                    continue;

                var accessor = accessors.Getter.IsNil ? accessors.Setter : accessors.Getter;
                var accessorMethod = accessor.IsNil ? default : reader.GetMethodDefinition(accessor);
                bool isStatic = !accessor.IsNil && accessorMethod.Attributes.HasFlag(MethodAttributes.Static);
                if (requirement.RequiredKind == CompileBackTypeKind.Interface && isStatic)
                    continue;
                var returnType = CompileBackTypeSignature.Display(propertyReturnType);
                bool isAutoProperty = !accessors.Getter.IsNil
                    && IsAutoProperty(reader, typeDef, property, accessors.Getter, returnType.DisplayName);
                bool hasSetter = !accessors.Setter.IsNil;
                bool isAbstractAccessor = !accessor.IsNil && propertyDeclaration.IsAbstract;
                var noBodyProperty = requirement.RequiredKind == CompileBackTypeKind.Interface || isAbstractAccessor;
                var stubBody = noBodyProperty
                    ? hasSetter
                        ? CompileBackStubBodyKind.AutoPropertyGetSet
                        : CompileBackStubBodyKind.None
                    : hasSetter && isAutoProperty
                        ? CompileBackStubBodyKind.AutoPropertyGetSet
                        : isAutoProperty
                            ? CompileBackStubBodyKind.AutoProperty
                            : hasSetter
                                ? CompileBackStubBodyKind.ThrowGetSet
                                : CompileBackStubBodyKind.Throw;
                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(requirement.Type.FullName, Identifier(propertyName), 0, $"property {propertyReturnType}"),
                    CompileBackMemberKind.PropertyGet,
                    IsStatic: isStatic,
                    Parameters: [],
                    ReturnType: returnType,
                    TypeParameters: [],
                    StubBody: stubBody,
                    TargetBody: null,
                    [new CompileBackFact("metadata", "closure-property", propertyName)],
                    propertyDeclaration.Attributes,
                    propertyDeclaration.Signature.ReturnAttributes,
                    IsAbstract: isAbstractAccessor,
                    IsVirtual: !accessor.IsNil && propertyDeclaration.IsVirtual,
                    Accessibility: accessor.IsNil
                        ? CompileBackAccessibility.Public
                        : MethodAccessibility(accessorMethod)));
            }

            int overload = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                string name = reader.GetString(method.Name);
                if (accessorMethods.Contains(methodHandle)
                    || name == ".cctor"
                    || (name.Contains('<', StringComparison.Ordinal)
                        && CSharpNaming.MethodName(name) == name)
                    || name.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }

                bool isConstructor = name == ".ctor";
                string identifierName = CSharpNaming.SourceMethodName(name);
                if (members.Any(member =>
                        member.Kind == (isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method)
                        && member.Identity.Method == identifierName))
                    continue;
                if (requirement.RequiredKind == CompileBackTypeKind.Interface && method.Attributes.HasFlag(MethodAttributes.Static))
                    continue;
                if (method.GetGenericParameters().Count != 0)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "generic-method-skipped", name));
                    continue;
                }
                if (!isConstructor && method.Attributes.HasFlag(MethodAttributes.SpecialName))
                    continue;

                MethodSignature<string> signature;
                try
                {
                    signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "method-signature-decode-failed", name));
                    continue;
                }

                var generatedLocalFunction = IsGeneratedLocalFunctionName(name);
                var methodDeclaration = generatedLocalFunction
                    ? null
                    : MetadataDeclarationQuery.GetMethod(reader, typeDef, method, signature);
                var parameters = generatedLocalFunction
                    ? Parameters(reader, method, signature)
                    : ToCompileBackParameters(methodDeclaration!.Signature.Parameters);
                var methodReturnType = generatedLocalFunction
                    ? signature.ReturnType
                    : methodDeclaration!.Signature.ReturnType;
                if (methodReturnType is null
                    || IsUnsupportedSurfaceSignature(methodReturnType)
                    || parameters.Any(parameter => IsUnsupportedSurfaceSignature(parameter.Type.DisplayName))
                    || (!allowUnsafeSurface
                        && (IsPointerSignature(methodReturnType)
                            || parameters.Any(parameter => IsPointerSignature(parameter.Type.DisplayName)))))
                {
                    continue;
                }
                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(requirement.Type.FullName, identifierName, overload++, MethodSignatureText(identifierName, signature)),
                    isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method,
                    IsStatic: method.Attributes.HasFlag(MethodAttributes.Static),
                    Parameters: parameters,
                    ReturnType: isConstructor ? null : CompileBackTypeSignature.Display(methodReturnType),
                    TypeParameters: generatedLocalFunction ? [] : ToCompileBackTypeParameters(methodDeclaration!.Signature.TypeParameters),
                    StubBody: requirement.RequiredKind == CompileBackTypeKind.Interface || IsAbstractMethod(method)
                        ? CompileBackStubBodyKind.None
                        : CompileBackStubBodyKind.Throw,
                    TargetBody: null,
                    [new CompileBackFact("metadata", isConstructor ? "closure-constructor" : "closure-method", name)],
                    isConstructor ? null : methodDeclaration?.Attributes,
                    isConstructor ? null : methodDeclaration?.Signature.ReturnAttributes,
                    IsAbstract: !isConstructor && IsAbstractMethod(method),
                    IsVirtual: !isConstructor && IsVirtualMethod(method),
                    IsOverride: false,
                    IsSealed: false,
                    Accessibility: MethodAccessibility(method)));
            }

            if (requirement.RequiredKind == CompileBackTypeKind.Class
                && !TypeShellProducer.IsStaticType(typeDef)
                && requirement.PrimaryConstructor is null
                && !members.Any(member => member.Kind == CompileBackMemberKind.Constructor && member.Parameters.Count == 0)
                && !HasParameterlessInstanceConstructor(reader, typeDef))
            {
                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(requirement.Type.FullName, ".ctor", overload, "void .ctor()"),
                    CompileBackMemberKind.Constructor,
                    IsStatic: false,
                    ReturnType: null,
                    Parameters: [],
                    TypeParameters: [],
                    StubBody: CompileBackStubBodyKind.Throw,
                    TargetBody: null,
                    SourceFacts: [new CompileBackFact("metadata", "synthetic-parameterless-ctor", "same-assembly closure root")]));
            }
        }

        static bool IsUnsupportedSurfaceSignature(string signature)
            // Normalize the raw signature text to its C# display form first (the
            // harness's own naming concern: strips modreq/modopt, maps `!`-typed and
            // generated `<>` segments), then defer the representability heuristic to
            // the product skeleton so every consumer judges surfaces the same way.
            => TypeShellProducer.IsUnsupportedSurfaceSignature(CompileBackTypeSignature.Display(signature).DisplayName);

        static bool IsGeneratedMetadataName(string name)
            => name.Contains('<', StringComparison.Ordinal) || name.Contains('>', StringComparison.Ordinal);

        static bool IsGeneratedLocalFunctionName(string name)
            => name.Contains('<', StringComparison.Ordinal) && CSharpNaming.MethodName(name) != name;

        static bool IsPointerSignature(string signature)
            => signature.Contains('*', StringComparison.Ordinal);

        static bool TryFormatConstantField(MetadataReader reader, FieldDefinition field, out string? constant)
        {
            constant = null;
            if (!field.Attributes.HasFlag(FieldAttributes.Literal))
                return false;

            var constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil)
                return false;

            var value = reader.GetConstant(constantHandle);
            var blob = reader.GetBlobReader(value.Value);
            constant = value.TypeCode switch
            {
                ConstantTypeCode.Boolean => blob.ReadBoolean() ? "true" : "false",
                ConstantTypeCode.Char => $"'{EscapeCharLiteral(blob.ReadChar())}'",
                ConstantTypeCode.SByte => blob.ReadSByte().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Byte => blob.ReadByte().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Int16 => blob.ReadInt16().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Int32 => blob.ReadInt32().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.UInt32 => blob.ReadUInt32().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Int64 => blob.ReadInt64().ToString(CultureInfo.InvariantCulture) + "L",
                ConstantTypeCode.UInt64 => blob.ReadUInt64().ToString(CultureInfo.InvariantCulture) + "UL",
                ConstantTypeCode.Single => FormatSingleConstant(blob.ReadSingle()),
                ConstantTypeCode.Double => FormatDoubleConstant(blob.ReadDouble()),
                ConstantTypeCode.String => StringLiteral(blob.ReadUTF16(blob.Length)),
                ConstantTypeCode.NullReference => "null",
                _ => null,
            };

            return constant is not null;
        }

        static string FormatSingleConstant(float value)
        {
            if (float.IsNaN(value))
                return "float.NaN";
            if (float.IsPositiveInfinity(value))
                return "float.PositiveInfinity";
            if (float.IsNegativeInfinity(value))
                return "float.NegativeInfinity";
            return value.ToString("R", CultureInfo.InvariantCulture) + "f";
        }

        static string FormatDoubleConstant(double value)
        {
            if (double.IsNaN(value))
                return "double.NaN";
            if (double.IsPositiveInfinity(value))
                return "double.PositiveInfinity";
            if (double.IsNegativeInfinity(value))
                return "double.NegativeInfinity";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static string StringLiteral(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char ch in value)
                sb.Append(EscapeCharLiteral(ch));
            sb.Append('"');
            return sb.ToString();
        }

        static string EscapeCharLiteral(char ch)
            => ch switch
            {
                '\'' => "\\'",
                '"' => "\\\"",
                '\\' => "\\\\",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when char.IsControl(ch) => $"\\u{(int)ch:x4}",
                _ => ch.ToString(),
            };

        static IReadOnlyList<CompileBackParameter> Parameters(
            MetadataReader reader,
            MethodDefinition method,
            MethodSignature<string> signature)
        {
            var names = new Dictionary<int, string>();
            foreach (var parameterHandle in method.GetParameters())
            {
                var parameter = reader.GetParameter(parameterHandle);
                if (parameter.SequenceNumber > 0)
                    names[parameter.SequenceNumber - 1] = Identifier(reader.GetString(parameter.Name));
            }

            var parameters = new List<CompileBackParameter>();
            for (int i = 0; i < signature.ParameterTypes.Length; i++)
            {
                string name = names.TryGetValue(i, out var metadataName) && metadataName.Length > 0
                    ? metadataName
                    : $"arg{i}";
                parameters.Add(new CompileBackParameter(name, CompileBackTypeSignature.Display(signature.ParameterTypes[i])));
            }

            return parameters;
        }

        static string MethodSignatureText(string name, MethodSignature<string> signature)
            => $"{signature.ReturnType} {name}({string.Join(", ", signature.ParameterTypes)})";

        internal static TypeDefinitionHandle? FindType(MetadataReader reader, string metadataFullName)
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(handle);
                if (reader.GetFullTypeName(typeDef) == metadataFullName)
                    return handle;
            }

            return null;
        }

        static bool HasParameterlessInstanceConstructor(MetadataReader reader, TypeDefinition typeDef)
        {
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != ".ctor" || method.Attributes.HasFlag(MethodAttributes.Static))
                    continue;

                try
                {
                    var signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
                    if (signature.ParameterTypes.Length == 0)
                        return true;
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
