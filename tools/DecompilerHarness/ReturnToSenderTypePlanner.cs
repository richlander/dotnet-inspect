using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

static class CompileBackCSharpNames
{
    public static string Clean(string type)
    {
        if (type.Contains('!'))
            return "object";

        type = SanitizeGeneratedTypeSegments(type);

        type = type.Replace("modreq(", "", StringComparison.Ordinal)
            .Replace("modopt(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal)
            .Trim();

        type = CSharpFormatter.AliasPrimitiveTypeNames(type);

        return CSharpFormatter.EscapeTypeKeywords(type);
    }

    static string SanitizeGeneratedTypeSegments(string type)
    {
        var sb = new StringBuilder(type.Length);
        int i = 0;
        while (i < type.Length)
        {
            if (type[i] == '<' && IsGeneratedSegmentStart(type, i))
            {
                int close = i + 1;
                while (close < type.Length && IsGeneratedTypeSegmentChar(type[close]))
                    close++;
                if (close < type.Length && type[close] == '>')
                {
                    int end = close + 1;
                    while (end < type.Length && IsGeneratedTypeSuffixChar(type[end]))
                        end++;
                    sb.Append(CSharpNaming.SafeIdentifier(type[i..end]));
                    i = end;
                    continue;
                }
            }

            sb.Append(type[i]);
            i++;
        }

        return sb.ToString();
    }

    static bool IsGeneratedTypeSegmentChar(char c)
        => c != '>'
            && c != '.'
            && c != ','
            && c != '['
            && c != ']'
            && c != '*'
            && !char.IsWhiteSpace(c);

    static bool IsGeneratedTypeSuffixChar(char c)
        => char.IsLetterOrDigit(c) || c is '_' or '{' or '}';

    static bool IsGeneratedSegmentStart(string text, int index)
        => index == 0 || text[index - 1] is '.' or '+' or '<';

    public static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    public static string Identifier(string name) => CSharpNaming.SafeIdentifier(name);

    public static string EscapeNamespace(string ns)
        => CSharpFormatter.EscapeNamespace(ns);
}

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
        string displayName = CompileBackCSharpNames.Identifier(CompileBackCSharpNames.StripArity(metadataName));
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
        string displayNamespace = CompileBackCSharpNames.EscapeNamespace(ns);
        string fullName = displayNamespace.Length == 0 ? displayName : $"{displayNamespace}.{displayName}";
        string metadataFullName = ns.Length == 0 ? metadataName : $"{ns}.{metadataName}";
        return new CompileBackTypeIdentity(ns, metadataName, displayName, fullName, metadataFullName);
    }
}

public sealed record CompileBackTypeSignature(CompileBackTypeSignatureKind Kind, string DisplayName, CompileBackTypeIdentity? Identity)
{
    public static CompileBackTypeSignature Display(string text)
        => new(CompileBackTypeSignatureKind.Display, CompileBackCSharpNames.Clean(text), null);

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
    string? Variance = null);

public enum CompileBackStubBodyKind
{
    None,
    Throw,
    ThrowGetSet,
    TargetBody,
    TargetGetterWithSetter,
    TargetSetterWithGetter,
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
    string? ConstructorInitializer = null)
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

public static class CompileBackSourceComposer
{
    internal static ProductTargetBody CreateTargetBody(
        MetadataSource source,
        IrFunction function,
        string fullType,
        string methodName)
    {
        var printed = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
        if (printed.Output is null)
            throw new InvalidOperationException($"Could not print {fullType}::{methodName}.");
        // The printer lifts an explicit base(...)/this(...) constructor-chain call
        // out of the body into ConstructorChain (chain calls are invalid as body
        // statements). Carry it so the reconstructed target constructor re-emits
        // the initializer; dropping it silently compiles an empty body and loses
        // the constructor-chain opcodes (issue #2678).
        return new ProductTargetBody(printed.Output, printed.Decisions, printed.ConstructorChain);
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
            AddMemberRequirement(
                method.DeclaringType,
                root => TryCreateClosureMemberRequirement(reader, root, method),
                allowTargetRoot);
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
        string propertyName = Identifier(reader.GetString(property.Name));
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
                MetadataDeclarationQuery.GetMethod(reader, targetTypeDef, getter, getterSignature).Signature.ReturnAttributes)
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

        var production = TypeProducer.Produce(reader, requirements, diagnostics);
        var declarations = production.Requests;
        var module = new CompileBackModuleRequirement(
            Usings: RequiredNamespaces(function)
                .Concat(DeclarationNamespaces(declarations))
                .Prepend("System")
                .Select(CompileBackCSharpNames.EscapeNamespace)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            AssemblyAttributes: [],
            ModuleAttributes: []);
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, signatureText),
            module,
            production.Requirements,
            declarations,
            diagnostics);
        return new CompileBackSourceResult(plan, WriteCompilationUnit(plan));
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
        string propertyName = Identifier(reader.GetString(property.Name));
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
                    : [new CompileBackFact("metadata", "target-property-setter", reader.GetString(setter.Name))])
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

        var production = TypeProducer.Produce(reader, requirements, diagnostics);
        var declarations = production.Requests;
        var module = new CompileBackModuleRequirement(
            Usings: RequiredNamespaces(function)
                .Concat(DeclarationNamespaces(declarations))
                .Prepend("System")
                .Select(CompileBackCSharpNames.EscapeNamespace)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            AssemblyAttributes: [],
            ModuleAttributes: []);
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, signatureText),
            module,
            production.Requirements,
            declarations,
            diagnostics);
        return new CompileBackSourceResult(plan, WriteCompilationUnit(plan));
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
        var primaryConstructor = isConstructor
            ? PrimaryConstructorFromPrologue(reader, method, function, targetBody)
            : PrimaryConstructorFromCapturedFields(reader, targetTypeDef, targetBody);

        var diagnostics = new List<CompileBackPlanningDiagnostic>();
        var targetRoot = TopLevelRootOf(reader, targetType);
        var targetFacts = new List<CompileBackFact>
        {
            new("metadata", "target-type", targetIdentity.FullName),
        };
        if (closureFacts.TryGetValue(targetType, out var targetClosureFacts))
            targetFacts.AddRange(targetClosureFacts);

        var chainCall = isConstructor ? ConstructorChainCall(function) : null;
        var chainCallee = chainCall?.Callee;
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
                ConstructorInitializer: targetConstructorInitializer)
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
            chainCallee is { } chainCtor
            // The chained-to constructor is reconstructed only when its signature
            // is fully supported (an unsupported parameter such as a function
            // pointer makes the planner drop it, mirroring MethodRequirement).
            && chainCtor.ParameterTypes.All(type => !IsUnsupportedChainParameterType(type))
            // A same-arity non-target constructor must actually be present in the
            // shell for `: this(args)` to have a binding target.
            && targetMembers.Any(member => member.Kind == CompileBackMemberKind.Constructor
                && member.StubBody != CompileBackStubBodyKind.TargetBody
                && member.Parameters.Count == chainCtor.ParameterTypes.Length);
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
            Usings: RequiredNamespaces(function)
                .Concat(DeclarationNamespaces(declarations))
                .Prepend("System")
                .Select(CompileBackCSharpNames.EscapeNamespace)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            AssemblyAttributes: [],
            ModuleAttributes: []);
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, signatureText),
            module,
            production.Requirements,
            declarations,
            diagnostics);
        return new CompileBackSourceResult(plan, WriteCompilationUnit(plan));
    }

    static string WriteCompilationUnit(CompileBackReconstructionPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#pragma warning disable");
        foreach (var attribute in plan.Module.AssemblyAttributes)
            sb.AppendLine($"[assembly: {attribute.Text}]");
        foreach (var attribute in plan.Module.ModuleAttributes)
            sb.AppendLine($"[module: {attribute.Text}]");
        foreach (var ns in plan.Module.Usings.Select(CompileBackCSharpNames.EscapeNamespace).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            sb.AppendLine($"using {ns};");

        var printed = new CSharpTypePrinter().PrintBatch(
            plan.PrintRequests,
            new CSharpTypePrintOptions
            {
                IncludeCustomAttributes = true,
                NamespaceStyle = CSharpNamespaceStyle.BlockScoped,
            });
        foreach (var unit in printed.Units)
            sb.AppendLine(unit.Source);

        return sb.ToString();
    }

    static IEnumerable<string> DeclarationNamespaces(IEnumerable<CSharpTypePrintRequest> requests)
    {
        var declaredTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in requests)
            AddDeclaredTypeNames(request, declaredTypes);

        foreach (var request in requests)
        {
            foreach (var ns in DeclarationNamespaces(request, declaredTypes))
                yield return ns;
        }
    }

    static void AddDeclaredTypeNames(CSharpTypePrintRequest request, HashSet<string> declaredTypes)
    {
        string name = CompileBackCSharpNames.StripArity(request.Type.Name);
        string fullName = string.IsNullOrEmpty(request.Type.Namespace)
            ? name
            : $"{request.Type.Namespace}.{name}";
        declaredTypes.Add(fullName);
        foreach (var nested in request.NestedTypes)
            AddDeclaredTypeNames(nested, declaredTypes);
    }

    static IEnumerable<string> DeclarationNamespaces(CSharpTypePrintRequest request, IReadOnlySet<string> declaredTypes)
    {
        if (!string.IsNullOrWhiteSpace(request.Type.Namespace))
            yield return request.Type.Namespace;

        foreach (var iface in request.Type.Interfaces)
            foreach (var ns in TypeNamespaces(iface, declaredTypes))
                yield return ns;
        if (request.Type.BaseType is { } baseType)
            foreach (var ns in TypeNamespaces(baseType, declaredTypes))
                yield return ns;
        foreach (var member in request.Members)
        {
            if (member.ReturnType is { } returnType)
                foreach (var ns in TypeNamespaces(returnType, declaredTypes))
                    yield return ns;
            if (member.SignatureModel is not { } signature)
                continue;
            foreach (var parameter in signature.Parameters)
                foreach (var ns in TypeNamespaces(parameter.Type, declaredTypes))
                    yield return ns;
        }
        foreach (var nested in request.NestedTypes)
            foreach (var ns in DeclarationNamespaces(nested, declaredTypes))
                yield return ns;
    }

    static IEnumerable<string> TypeNamespaces(string type, IReadOnlySet<string> declaredTypes)
    {
        foreach (var token in type.Split([',', '<', '>', '[', ']', '*', '&', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int dot = token.LastIndexOf('.');
            if (dot > 0 && !declaredTypes.Contains(token[..dot]))
                yield return token[..dot];
        }
    }
    static ApiMember ToApiMember(CompileBackMemberRequirement member)
    {
        string? returnType = member.ReturnType?.DisplayName;
        var apiMember = new ApiMember
        {
            Name = member.Identity.Method,
            Kind = member.Kind switch
            {
                CompileBackMemberKind.PropertyGet => "property",
                CompileBackMemberKind.PropertySet => "property",
                CompileBackMemberKind.Constructor => "constructor",
                CompileBackMemberKind.Method => "method",
                CompileBackMemberKind.Field => "field",
                _ => throw new NotSupportedException($"Unsupported member declaration kind '{member.Kind}'."),
            },
            ReturnType = returnType,
            IsStatic = member.IsStatic,
            IsAbstract = member.IsAbstract,
            IsVirtual = member.IsVirtual,
            IsOverride = member.IsOverride,
            IsSealed = member.IsSealed,
            Accessibility = AccessibilityText(member.Accessibility),
            Attributes = member.Attributes?.ToList() ?? [],
            IsUnsafe = RequiresUnsafe(member),
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
                    : member.Identity.Method;
                apiMember.SignatureModel.Accessors = PropertyAccessors(member);
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

    /// <summary>
    /// The base/this <c>.ctor</c> chain call in the entry block, or null when the
    /// constructor body has no chain call (a struct ctor, or a body that never
    /// chains). Mirrors the printer's chain-call detection so the shell can verify
    /// the chained-to constructor was reconstructed before emitting the initializer
    /// and letting the Roslyn oracle judge binding.
    /// </summary>
    static Call? ConstructorChainCall(IrFunction function)
    {
        if (function.Body.Blocks is not [{ } entry, ..])
            return null;
        foreach (var child in entry.Children)
        {
            if (child is ExpressionStatement
                {
                    Expression: Call { Callee: { Name: ".ctor", HasThis: true } } call
                }
                && call.Arguments is [_, ..])
            {
                return call;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a constructor-chain parameter type would be dropped from the
    /// reconstructed shell (mirrors <c>IsUnsupportedSurfaceSignature</c>): a
    /// function pointer or a compiler-generated/anonymous type has no faithful
    /// C# surface, so the chained-to constructor is not reconstructed and a
    /// <c>: this(args)</c> initializer targeting it cannot bind.
    /// </summary>
    static bool IsUnsupportedChainParameterType(TypeRef type)
        // Route surface-representability through the product seam (TypeShellProducer)
        // so every RTS consumer judges droppable surfaces identically, instead of
        // re-inlining the delegate*/<>/{ heuristic here.
        => TypeShellProducer.IsUnsupportedSurfaceSignature(type.ToDisplayString());

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
                parameter.Variance))
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
            || (member.TargetBody is { } body && CSharpFormatter.RequiresUnsafeModifier(body));


    static IReadOnlyList<CompileBackParameter> MethodParameters(
        MetadataReader reader,
        MethodDefinition method,
        MethodSignature<string> signature)
    {
        var declaringType = reader.GetTypeDefinition(method.GetDeclaringType());
        var parameters = ToCompileBackParameters(MetadataDeclarationQuery.GetMethod(
            reader,
            declaringType,
            method,
            signature).Signature.Parameters);
        return NormalizeSelfTypeParameters(reader, declaringType, parameters);
    }

    static IReadOnlyList<CompileBackParameter> NormalizeSelfTypeParameters(
        MetadataReader reader,
        TypeDefinition declaringType,
        IReadOnlyList<CompileBackParameter> parameters)
    {
        if (parameters.Count == 0 || declaringType.GetDeclaringType().IsNil)
            return parameters;

        string selfType = MetadataDeclarationQuery.SelfTypeSignature(reader, declaringType);
        string directSelfType = DirectSelfTypeSignature(reader, declaringType);
        if (directSelfType == selfType || parameters.All(parameter => parameter.Type.DisplayName != directSelfType))
            return parameters;

        return parameters
            .Select(parameter => parameter.Type.DisplayName == directSelfType
                ? parameter with { Type = CompileBackTypeSignature.Display(selfType) }
                : parameter)
            .ToArray();
    }

    static string DirectSelfTypeSignature(MetadataReader reader, TypeDefinition type)
    {
        var handles = type.GetGenericParameters();
        int inheritedCount = 0;
        var declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
            inheritedCount = reader.GetTypeDefinition(declaringType).GetGenericParameters().Count;
        var directNames = handles
            .Skip(inheritedCount)
            .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
            .ToArray();
        return TypeResolver.ApplyGenericArguments(TypeResolver.GetFullName(reader, type), directNames);
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

    static IReadOnlyList<CompileBackTypeParameter> TypeParameters(MetadataReader reader, TypeDefinition type)
    {
        var handles = type.GetGenericParameters().ToArray();
        int inheritedCount = 0;
        var declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
            inheritedCount = reader.GetTypeDefinition(declaringType).GetGenericParameters().Count;

        return TypeParameters(reader, handles.Skip(inheritedCount), GenericContext.ForType(reader, type));
    }

    static IReadOnlyList<CompileBackTypeParameter> TypeParameters(
        MetadataReader reader,
        IEnumerable<GenericParameterHandle> handles,
        GenericContext context)
    {
        var parameters = new List<CompileBackTypeParameter>();
        foreach (var handle in handles)
        {
            var parameter = reader.GetGenericParameter(handle);
            var constraints = new List<string>();
            var attributes = parameter.Attributes;
            bool isStruct = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
            if (GenericConstraintKeywords.PrimaryKeyword(attributes, nullableFlag: 0, isUnmanaged: false) is { } primaryKeyword)
                constraints.Add(primaryKeyword);

            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                if (ConstraintTypeName(reader, constraint.Type, context) is { Length: > 0 } constraintName)
                {
                    if (isStruct && constraintName is "System.ValueType" or "ValueType")
                        continue;
                    constraints.Add(constraintName);
                }
            }

            if (GenericConstraintKeywords.NewConstraintKeyword(attributes) is { } newConstraint)
                constraints.Add(newConstraint);

            string? variance = GenericConstraintKeywords.VarianceKeyword(attributes);
            parameters.Add(new CompileBackTypeParameter(
                reader.GetString(parameter.Name),
                constraints,
                variance));
        }

        return parameters;
    }

    static string? ConstraintTypeName(MetadataReader reader, EntityHandle handle, GenericContext context)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => CompileBackTypeIdentity.FromDefinition(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)).FullName,
            HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)handle)),
            HandleKind.TypeSpecification => GuardedSignatureText.TypeSpecText(reader, (TypeSpecificationHandle)handle, context),
            _ => null,
        };

    static CompileBackPrimaryConstructor? PrimaryConstructorFromPrologue(
        MetadataReader reader,
        MethodDefinition method,
        IrFunction function,
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
        if (function.Body.Blocks is not [{ } entry, ..])
            return null;

        int? chainIndex = null;
        for (int i = 0; i < entry.Children.Count; i++)
        {
            if (entry.Children[i] is ExpressionStatement
                {
                    Expression: Call { Callee: { Name: ".ctor", HasThis: true }, Arguments: [LoadArgument { Index: 0 }] }
                })
            {
                chainIndex = i;
                break;
            }
        }

        if (chainIndex is not > 0)
            return null;
        if (entry.Children.Skip(chainIndex.Value + 1).Any(node => node is not Return))
            return null;

        var parameterNames = ParameterNames(reader, method);
        if (parameterNames.Count == 0)
            return null;

        var fieldInitializers = new List<CompileBackMemberRequirement>();
        var initializerTexts = new List<(string Field, string Value)>();
        foreach (var node in entry.Children.Take(chainIndex.Value))
        {
            if (node is not StoreField
                {
                    HasInstance: true,
                    Instance: LoadArgument { Index: 0 },
                    Value: LoadArgument { Index: > 0 } value
                } store)
                return null;
            if (!parameterNames.TryGetValue(value.Index - 1, out string? parameterName))
                return null;
            if (FindField(reader, declaringType, store.Field.Name) is not { } fieldHandle)
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

            string fieldName = store.Field.BackingPropertyName
                ?? store.Field.Name;
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
                [new CompileBackFact("metadata", "primary-constructor-field-initializer", fieldName)]));
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

        foreach (var fieldRef in TargetBackingFieldRefs(function))
        {
            if (fieldRef.BackingPropertyName is not { Length: > 0 } propertyName)
                continue;
            if (!IsSelfType(fieldRef.DeclaringType, targetIdentity))
                continue;
            if (FindField(reader, typeDef, fieldRef.Name) is not { } fieldHandle)
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
                [new CompileBackFact("metadata", "target-backing-field-read", fieldRef.Name)]));
        }

        return members;
    }

    static IEnumerable<FieldRef> TargetBackingFieldRefs(IrFunction function)
    {
        foreach (var load in function.Descendants.OfType<LoadField>())
            yield return load.Field;
        foreach (var address in function.Descendants.OfType<LoadFieldAddress>())
            yield return address.Field;
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

        foreach (var store in function.Descendants.OfType<StoreField>())
        {
            if (store is not { HasInstance: true, Instance: LoadArgument { Index: 0 } }
                && !(allowStaticStores && store is { HasInstance: false }))
            {
                continue;
            }
            var fieldRef = store.Field;
            if (fieldRef.BackingPropertyName is not { Length: > 0 } propertyName)
                continue;
            if (!IsSelfType(fieldRef.DeclaringType, targetIdentity))
                continue;
            if (FindField(reader, typeDef, fieldRef.Name) is not { } fieldHandle)
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
                [new CompileBackFact("metadata", "target-backing-field-write", fieldRef.Name)]));
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

    static IReadOnlySet<string> RequiredNamespaces(IrFunction function)
    {
        var namespaces = new SortedSet<string>(StringComparer.Ordinal);

        void Add(TypeRef? type)
        {
            switch (type?.Kind)
            {
                case TypeRefKind.Definition:
                    if (type.Namespace.Length > 0)
                        namespaces.Add(type.Namespace);
                    break;
                case TypeRefKind.GenericInstance:
                    Add(type.ElementType);
                    foreach (var argument in type.TypeArguments)
                        Add(argument);
                    break;
                case TypeRefKind.SzArray or TypeRefKind.Array
                    or TypeRefKind.ByRef or TypeRefKind.Pointer or TypeRefKind.Pinned:
                    Add(type.ElementType);
                    break;
            }
        }

        foreach (var node in function.Descendants.Prepend(function))
        {
            foreach (var type in node.DirectTypes)
                Add(type);
            if (node is IrExpression expression)
                Add(expression.ResultType);
        }

        return namespaces;
    }

    static string Clean(string type) => CompileBackCSharpNames.Clean(type);

    static string StripArity(string name) => CompileBackCSharpNames.StripArity(name);

    static string Identifier(string name) => CompileBackCSharpNames.Identifier(name);

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

            if (IsUnsupportedSurfaceSignature(fieldType))
                return null;

            string fieldName = reader.GetString(field.Name);
            if (fieldName.Contains('.', StringComparison.Ordinal))
            {
                return null;
            }

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
                [new CompileBackFact("metadata", "typed-closure-field", fieldName)]);
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

                requests.Add(TypeRequest(
                    reader,
                    rootHandle,
                    rootRequirement,
                    requirementsByMetadataName,
                    producedRequirements,
                    diagnostics));
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

        static MethodDefinitionHandle? TryFindMethod(
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

        static CompileBackMemberRequirement? PropertyRequirement(
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
            if (propertyName.Contains('<', StringComparison.Ordinal)
                || propertyName.Contains('.', StringComparison.Ordinal))
                return null;

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
                IsVirtual: !accessor.IsNil && propertyDeclaration.IsVirtual);
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

        static CSharpTypePrintRequest TypeRequest(
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
            var type = new ApiType
            {
                Namespace = requirement.Type.Namespace,
                Name = requirement.Type.MetadataName,
                MetadataName = requirement.Type.MetadataName,
                Kind = TypeKindText(kind),
                BaseType = BaseTypeSignature(reader, typeDef, kind)?.DisplayName,
                TypeParameters = TypeParameters(reader, typeDef)
                    .Select(ToApiTypeParameter)
                    .ToList(),
                Interfaces = InterfaceSignatures(reader, typeDef)
                    .Select(signature => signature.DisplayName)
                    .ToList(),
                Members = policies.Select(policy => policy.Member).ToList(),
                Attributes = TypeAttributeList(reader, typeDef).ToList(),
                IsAbstract = (typeDef.Attributes & TypeAttributes.Abstract) != 0
                    && (typeDef.Attributes & TypeAttributes.Interface) == 0,
                IsSealed = (typeDef.Attributes & TypeAttributes.Sealed) != 0,
                IsStatic = TypeShellProducer.IsStaticType(typeDef),
            };
            return new CSharpTypePrintRequest(
                type,
                members: type.Members,
                memberPolicyOverrides: policies,
                primaryConstructorParameters: primaryConstructorParameters,
                nestedTypes: NestedTypes(
                    reader,
                    typeDef,
                    requirementsByMetadataName,
                    includeMemberSurface,
                    producedRequirements,
                    diagnostics));
        }

        static string TypeKindText(CompileBackTypeKind kind)
            => kind switch
            {
                CompileBackTypeKind.Class => "class",
                CompileBackTypeKind.Record => "record",
                CompileBackTypeKind.Struct => "struct",
                CompileBackTypeKind.Interface => "interface",
                CompileBackTypeKind.Enum => "enum",
                CompileBackTypeKind.Delegate => "delegate",
                _ => throw new NotSupportedException($"Unsupported RTS type kind '{kind}'."),
            };

        static TypeParameter ToApiTypeParameter(CompileBackTypeParameter parameter)
            => new()
            {
                Name = parameter.Name,
                Constraints = parameter.Constraints.ToList(),
                Variance = parameter.Variance,
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

        static IReadOnlyList<CSharpTypePrintRequest> NestedTypes(
            MetadataReader reader,
            TypeDefinition typeDef,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName,
            bool includeMemberSurface,
            List<CompileBackTypeRequirement> producedRequirements,
            List<CompileBackPlanningDiagnostic> diagnostics)
        {
            var nestedTypes = new List<CSharpTypePrintRequest>();
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
                nestedTypes.Add(TypeRequest(
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
                    if (nestedTypes.Any(type => type.Type.Name == identity.MetadataName))
                        continue;

                    nestedTypes.Add(TypeRequest(
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

        static IReadOnlyList<string> TypeAttributeList(MetadataReader reader, TypeDefinition typeDef)
            => AttributeReader.RenderAttributes(reader, typeDef.GetCustomAttributes(), qualifyNames: true);

        static CompileBackTypeSignature? BaseTypeSignature(MetadataReader reader, TypeDefinition typeDef, CompileBackTypeKind kind)
        {
            // The product skeleton (TypeShellProducer) owns the metadata-level
            // gates that decide which base is reconstructable (interface/nil/same-
            // assembly/non-generic/object-family). The harness applies its own C#
            // surface-representability gate on the display form here, where the Clean
            // normalization lives: Clean strips generated <> segments (so a <>-named
            // base is kept) but not { or delegate*, matching origin/main exactly.
            var baseType = TypeShellProducer.ReconstructedBaseTypeName(reader, typeDef, kind == CompileBackTypeKind.Class);
            if (baseType is null)
                return null;
            if (IsUnsupportedSurfaceSignature(baseType))
                return null;
            return CompileBackTypeSignature.Display(baseType);
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
            if (BaseTypeSignature(reader, typeDef, kind) is null)
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
                return;

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
                if (IsUnsupportedSurfaceSignature(fieldType)
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
                    [new CompileBackFact("metadata", "closure-field", fieldName)]));
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

        static TypeDefinitionHandle? FindType(MetadataReader reader, string metadataFullName)
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
