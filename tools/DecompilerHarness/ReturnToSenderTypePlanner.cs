using System.Globalization;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DotnetInspector.Services;
using DotnetInspector.RoundTripCompilation;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.DecompilerHarness;

internal sealed class CompileBackMemberSurfaceIndex
{
    readonly IReadOnlyDictionary<MetadataTypeDefinitionName, ImmutableArray<ApiType>> surfaces;

    internal CompileBackMemberSurfaceIndex(IEnumerable<ApiType> types)
    {
        surfaces = types
            .Where(type => type.DefinitionName is not null)
            .GroupBy(type => type.DefinitionName!)
            .ToImmutableDictionary(
                group => group.Key,
                group => group.ToImmutableArray());
    }

    internal bool TryGetValue(MetadataTypeDefinitionName name, out ApiType surface)
    {
        if (!surfaces.TryGetValue(name, out var candidates))
        {
            surface = null!;
            return false;
        }
        if (candidates.Length != 1)
        {
            throw new InvalidOperationException(
                $"Product member surface contains {candidates.Length} definitions named '{name.ToMetadataFullName()}'.");
        }

        surface = candidates[0];
        return true;
    }
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
    IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> ClosureFacts)
{
    internal RoundTripBodyPolicy BodyPolicy { get; init; } = RoundTripBodyPolicy.Selected;
    internal MetadataSource? BodySource { get; init; }
    internal ReturnToSender.CompilationClosure? CompilationClosure
        { get; set; }
    internal required CompileBackMemberSurfaceIndex MemberSurfaceByDefinitionName { get; init; }
}

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
    CSharpSourceArtifact SourceArtifact,
    IReadOnlyList<CompileBackFact> SourceFacts,
    IReadOnlyList<CompileBackPlanningDiagnostic> Diagnostics,
    IReadOnlySet<TypeDefinitionHandle> ClosureRoots,
    CompileBackReconstructionPlan Plan,
    IReadOnlyList<FullBodyProduction> FullBodies)
{
    internal string Source => SourceArtifact.Source;

    internal static ProductArtifact From(
        ArtifactRequest request,
        CompileBackSourceResult result,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyList<FullBodyProduction>? fullBodies = null)
        => new(
            request,
            request.TargetBody,
            result.SourceArtifact,
            result.Plan.Types
                .SelectMany(type => type.SourceFacts
                    .Concat(type.PrimaryConstructor?.FieldInitializers.SelectMany(member => member.SourceFacts) ?? [])
                    .Concat(type.RequiredMembers.SelectMany(member => member.SourceFacts)))
                .ToArray(),
            result.Plan.Diagnostics,
            closureRoots,
            result.Plan,
            fullBodies ?? []);
}

public sealed record FullBodyProduction(
    MetadataMethodAddress Method,
    string Member,
    MemberBodyProductionStatus Status,
    string? Failure);

public sealed record CompileBackSourceResult(
    CompileBackReconstructionPlan Plan,
    CSharpSourceArtifact SourceArtifact)
{
    public string Source => SourceArtifact.Source;
}

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
    Operator,
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
    IReadOnlyList<TypeParameterConstraint>? StructuredConstraints = null,
    TypeParameterTypeKind TypeKind = TypeParameterTypeKind.Undetermined);

public enum CompileBackStubBodyKind
{
    None,
    Throw,
    ThrowInit,
    ThrowGetSet,
    ThrowGetInit,
    TargetBody,
    TargetGetterWithSetter,
    TargetGetterWithInitSetter,
    TargetSetterWithGetter,
    TargetInitSetterWithGetter,
    TargetInitBody,
    TargetEventAccessorWithSibling,
    AutoProperty,
    AutoPropertyGetSet,
    AutoPropertyGetInit,
    InitOnlyProperty,
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
    public bool IncludeOperatorSurface { get; init; }
    public IReadOnlyList<string> ExternalInterfaces { get; init; } = [];
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
    bool IsOperator = false,
    bool IsFinalizer = false,
    CompileBackAccessibility Accessibility = CompileBackAccessibility.Public,
    string? ConstructorInitializer = null,
    string? ExplicitInterfaceMemberName = null,
    string? DeclarationSignature = null,
    bool RequiresUnsafeModifier = false,
    string? SiblingTargetBody = null,
    int? MetadataToken = null,
    int? GetterToken = null,
    int? SetterToken = null,
    int? AdderToken = null,
    int? RemoverToken = null,
    bool SuppressDestructorSyntax = false,
    string? OperatorPairingKey = null,
    bool HasOperatorPairingKey = false,
    string? MetadataName = null)
{
    public string Name => Identity.Method;
    public string Type => ReturnType?.DisplayName ?? "";
    public string Body => TargetBody ?? "";
}

public sealed record CompileBackPlanningDiagnostic(string Layer, string Reason, string Detail);

internal sealed record ProductTargetBody(
    string Source,
    IReadOnlyList<DecompilerDecision> Decisions,
    string? ConstructorChain = null,
    bool RequiresAsyncModifier = false,
    bool RequiresUnsafeModifier = false,
    bool SuppressDestructorSyntax = false);

internal sealed record ExplicitInterfaceEventInfo(
    TypeDefinitionHandle InterfaceType,
    EventDefinitionHandle InterfaceEvent,
    string QualifiedName,
    string AccessorName);

internal sealed record ExternalExplicitInterfaceEventInfo(
    string InterfaceDisplayName,
    string QualifiedName);

internal sealed record ExternalExplicitInterfaceMethodInfo(
    string InterfaceDisplayName,
    string ExplicitInterfaceMemberName,
    IReadOnlyList<ExternalInterfaceMemberRequirement> AdditionalInterfaceMembers);

internal sealed record ExternalInterfaceMemberRequirement(
    TypeDefinitionHandle DeclaringType,
    CompileBackMemberRequirement Member);

internal readonly record struct ImplicitInterfaceMethodLookup(
    MethodDefinitionHandle Method,
    bool SearchComplete);

internal sealed record ExternalInterfaceReferenceInfo(
    string MetadataFullName,
    string DisplayFullName,
    AssemblyReferenceIdentity AssemblyIdentity);

internal sealed record ExternalInterfaceRequiredMethod(
    string Name,
    int GenericArity,
    string ReturnType,
    ImmutableArray<string> ParameterTypes,
    bool IsOperator);

public static class CompileBackSourceComposer
{
    internal static CompileBackMemberSurfaceIndex CreateMemberSurfaceIndex(
        ApiSurface surface)
        => new(surface.Types);

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
            produced.Projection.ConstructorChain,
            produced.Body.RequiresAsyncModifier,
            produced.Body.RequiresUnsafeModifier,
            produced.Body.SuppressDestructorSyntax);
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
        using var operatorMetadataContext =
            request.CompilationClosure is null
                ? null
                : new MetadataContext(
                    (IAssemblyReferenceResolver)
                    request.CompilationClosure.Resolver);
        CrossAssemblyTypeResolver? operatorTypeResolver =
            request.CompilationClosure is null
                ? null
                : new CrossAssemblyTypeResolver(
                    request.Reader,
                    request.CompilationClosure.TargetAssembly,
                    operatorMetadataContext!);
        IOperatorTypeRelationshipResolver? operatorResolver =
            operatorTypeResolver?.CreateOperatorRelationshipResolver();
        var closure = CreateClosureInputs(
            request,
            operatorResolver);
        var result = request switch
        {
            PropertyGetterArtifactRequest getter => ComposePropertyGetter(
                request.AssemblyPath,
                request.CompilationClosure,
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
                closure.MemberRequirements,
                request.MemberSurfaceByDefinitionName,
                request.BodyPolicy,
                operatorResolver),
            PropertySetterArtifactRequest setter => ComposePropertySetter(
                request.AssemblyPath,
                request.CompilationClosure,
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
                closure.MemberRequirements,
                request.MemberSurfaceByDefinitionName,
                operatorResolver),
            EventAccessorArtifactRequest eventAccessor => ComposeEventAccessor(
                request.AssemblyPath,
                request.CompilationClosure,
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
                request.MemberSurfaceByDefinitionName,
                eventAccessor.SiblingAccessorBody?.Source,
                request.BodyPolicy,
                operatorResolver),
            MethodArtifactRequest => ComposeMethod(
                request.AssemblyPath,
                request.CompilationClosure,
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
                request.MemberSurfaceByDefinitionName,
                request.BodyPolicy,
                request.TargetBody.ConstructorChain,
                operatorResolver,
                operatorTypeResolver,
                request.TargetBody.SuppressDestructorSyntax),
            _ => throw new ArgumentException($"Unknown artifact request type '{request.GetType().FullName}'.", nameof(request)),
        };

        IReadOnlyList<FullBodyProduction> fullBodies;
        if (request.BodyPolicy == RoundTripBodyPolicy.Full)
            result = ApplyFullBodies(request, result, closure.Roots, out fullBodies);
        else
            fullBodies = [];
        return ProductArtifact.From(request, result, closure.Roots, fullBodies);
    }

    static CompileBackSourceResult ApplyFullBodies(
        ArtifactRequest artifact,
        CompileBackSourceResult result,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        out IReadOnlyList<FullBodyProduction> evidence)
    {
        var source = artifact.BodySource
            ?? throw new InvalidOperationException("Full body production requires a live metadata source.");
        var rows = new List<FullBodyProduction>();
        var attempted = new HashSet<MethodDefinitionHandle>();
        var diagnostics = result.Plan.Diagnostics.ToList();
        var targetAddress = MetadataMethodAddress.Create(artifact.Reader, artifact.TargetMethod);
        rows.Add(new FullBodyProduction(
            targetAddress,
            $"{artifact.FullType}.{artifact.MethodName}",
            MemberBodyProductionStatus.Complete,
            Failure: null));
        attempted.Add(artifact.TargetMethod);
        var requests = result.Plan.PrintRequests.Select(Enrich).ToArray();
        foreach (var typeHandle in artifact.Reader.TypeDefinitions)
        {
            var type = artifact.Reader.GetTypeDefinition(typeHandle);
            if (!closureRoots.Contains(TopLevelRootOf(artifact.Reader, typeHandle)))
                continue;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = artifact.Reader.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0 || attempted.Contains(methodHandle))
                    continue;
                string member = $"{artifact.Reader.GetFullTypeName(type)}.{artifact.Reader.GetString(method.Name)}";
                var address = MetadataMethodAddress.Create(artifact.Reader, methodHandle);
                const string failure = "method body is concrete but its declaration is not represented in the typed artifact";
                rows.Add(new FullBodyProduction(address, member, MemberBodyProductionStatus.Failed, failure));
                diagnostics.Add(new CompileBackPlanningDiagnostic("full body", "declaration-not-represented", member));
            }
        }
        var plan = result.Plan with
        {
            PrintRequests = requests,
            Diagnostics = diagnostics,
        };
        evidence = rows;
        return ComposeCompilationUnit(plan);

        CSharpTypePrintRequest Enrich(CSharpTypePrintRequest request)
        {
            IEqualityComparer<ApiMember> memberIdentity = ReferenceEqualityComparer.Instance;
            var policies = new Dictionary<ApiMember, CSharpMemberPolicy>(memberIdentity);
            foreach (var policy in request.MemberPolicyOverrides)
                policies[policy.Member] = policy;
            foreach (var member in request.Members
                         .Concat(request.MemberPolicyOverrides.Select(policy => policy.Member))
                         .Distinct(memberIdentity))
            {
                if (member.MetadataToken is { } methodToken)
                {
                    if (!HasConcreteBody(methodToken))
                        continue;
                    var produced = Produce(methodToken, $"{request.Type.FullName}.{member.Name}");
                    if (produced.Body is { } body)
                        policies[member] = new CSharpMemberPolicy(member, CSharpBodyPolicy.Full, body);
                    continue;
                }

                if (member.AdderToken is not null || member.RemoverToken is not null)
                {
                    bool concreteAdder = member.AdderToken is { } adderHandle && HasConcreteBody(adderHandle);
                    bool concreteRemover = member.RemoverToken is { } removerHandle && HasConcreteBody(removerHandle);
                    if (!concreteAdder && !concreteRemover)
                        continue;
                    // Symmetric with the property branch below: when one accessor is the request
                    // target, its body is applied by the base ComposeEventAccessor path (baked into
                    // this member's TargetEventAccessorWithSibling policy). Producing it here would
                    // clobber the real body with `throw null`, so skip Produce for the target and
                    // preserve the base body; only the sibling is produced (which also credits it
                    // into `attempted` so its declaration is represented, issue #3007).
                    bool adderIsTarget = member.AdderToken is { } adderTargetHandle && IsTargetToken(adderTargetHandle);
                    bool removerIsTarget = member.RemoverToken is { } removerTargetHandle && IsTargetToken(removerTargetHandle);
                    var adder = member.AdderToken is { } adderToken && concreteAdder && !adderIsTarget
                        ? Produce(adderToken, $"{request.Type.FullName}.add_{member.Name}")
                        : default;
                    var remover = member.RemoverToken is { } removerToken && concreteRemover && !removerIsTarget
                        ? Produce(removerToken, $"{request.Type.FullName}.remove_{member.Name}")
                        : default;
                    bool adderReady = !concreteAdder || adderIsTarget || adder.Body is not null;
                    bool removerReady = !concreteRemover || removerIsTarget || remover.Body is not null;
                    if (adderReady && removerReady)
                    {
                        bool targetInvolved = adderIsTarget || removerIsTarget;
                        var baseEventBody = policies.TryGetValue(member, out var existingEventPolicy)
                            ? existingEventPolicy.Body as CSharpEventBody
                            : null;
                        // The target accessor's real body lives only in the base policy; if it is
                        // missing there is nothing to preserve, so leave the base policy untouched.
                        if (baseEventBody is null && targetInvolved)
                            continue;
                        CSharpAccessorBody adderBody = adderIsTarget
                            ? baseEventBody!.Adder
                            : adder.Body is { } producedAdder
                                ? CSharpAccessorBody.Block(producedAdder.Source)
                                : baseEventBody?.Adder ?? CSharpAccessorBody.Throw;
                        CSharpAccessorBody removerBody = removerIsTarget
                            ? baseEventBody!.Remover
                            : remover.Body is { } producedRemover
                                ? CSharpAccessorBody.Block(producedRemover.Source)
                                : baseEventBody?.Remover ?? CSharpAccessorBody.Throw;
                        policies[member] = new CSharpMemberPolicy(
                            member,
                            CSharpBodyPolicy.Full,
                            new CSharpEventBody(adderBody, removerBody));
                    }
                    continue;
                }

                if (member.GetterToken is null && member.SetterToken is null)
                    continue;
                bool concreteGetter = member.GetterToken is { } getterHandle && HasConcreteBody(getterHandle);
                bool concreteSetter = member.SetterToken is { } setterHandle && HasConcreteBody(setterHandle);
                if (!concreteGetter && !concreteSetter)
                    continue;
                bool getterIsTarget = member.GetterToken is { } getterTargetHandle && IsTargetToken(getterTargetHandle);
                bool setterIsTarget = member.SetterToken is { } setterTargetHandle && IsTargetToken(setterTargetHandle);
                var getter = member.GetterToken is { } getterToken && concreteGetter && !getterIsTarget
                    ? Produce(getterToken, $"{request.Type.FullName}.get_{member.Name}")
                    : default;
                var setter = member.SetterToken is { } setterToken && concreteSetter && !setterIsTarget
                    ? Produce(setterToken, $"{request.Type.FullName}.set_{member.Name}")
                    : default;
                // The target accessor's body is applied by the base Compose* path, not by Produce here;
                // treat it as ready and preserve its already-applied body so the sibling's produced body
                // is incorporated instead of clobbering the target with a null accessor.
                bool getterReady = !concreteGetter || getterIsTarget || getter.Body is not null;
                bool setterReady = !concreteSetter || setterIsTarget || setter.Body is not null;
                if (getterReady && setterReady)
                {
                    bool targetInvolved = getterIsTarget || setterIsTarget;
                    var basePropertyBody = policies.TryGetValue(member, out var existingPropertyPolicy)
                        ? existingPropertyPolicy.Body as CSharpPropertyBody
                        : null;
                    // When the target accessor belongs to an auto/skeleton property, the base
                    // Compose path already emitted the property's compiler-synthesized accessors
                    // and there is no explicit target body to extend. Leave that policy untouched
                    // rather than replacing it with empty accessor bodies, which would delete the
                    // property's accessors (e.g. auto-properties -> `int Value {  }`, CS0548).
                    //
                    // The same applies to a NON-target auto-property sibling: producing explicit
                    // bodies for it decompiles the compiler-synthesized accessors, which read/write
                    // the unspeakable backing field. The decompiler renders that field access as the
                    // property itself, yielding recursive `get { return this.P; }` / `init { this.P
                    // = value; }`. That compiles but is semantically wrong while still reporting the
                    // accessors Complete. Preserve the skeleton so the compiler re-synthesizes
                    // faithful auto-property accessors.
                    bool isAutoSkeleton = existingPropertyPolicy is { BodyPolicy: CSharpBodyPolicy.Skeleton };
                    if (basePropertyBody is null && (targetInvolved || isAutoSkeleton))
                        continue;
                    policies[member] = new CSharpMemberPolicy(
                        member,
                        CSharpBodyPolicy.Full,
                        new CSharpPropertyBody(
                            getterIsTarget
                                ? basePropertyBody?.Getter
                                : getter.Body is { } getterBody ? CSharpAccessorBody.Block(getterBody.Source) : basePropertyBody?.Getter,
                            setterIsTarget
                                ? basePropertyBody?.Setter
                                : setter.Body is { } setterBody ? CSharpAccessorBody.Block(setterBody.Source) : basePropertyBody?.Setter));
                }
            }

            return new CSharpTypePrintRequest(
                request.Type,
                request.BodyPolicy,
                request.Members,
                policies.Values.ToArray(),
                request.PrimaryConstructorParameters,
                request.NestedTypes.Select(Enrich).ToArray());
        }

        (CSharpBlockBody? Body, MemberBodyProductionStatus Status) Produce(int token, string member)
        {
            var handle = MetadataTokens.MethodDefinitionHandle(token & 0x00ffffff);
            attempted.Add(handle);
            if (handle == artifact.TargetMethod)
                return (null, MemberBodyProductionStatus.Complete);
            var address = MetadataMethodAddress.Create(artifact.Reader, handle);
            var produced = MemberBodyProducer.ProduceBody(source, address);
            string? failure = produced.Projection.Diagnostics.Count == 0
                ? null
                : string.Join("; ", produced.Projection.Diagnostics);
            rows.Add(new FullBodyProduction(address, member, produced.Status, failure));
            if (produced.Status != MemberBodyProductionStatus.Complete)
            {
                diagnostics.Add(new CompileBackPlanningDiagnostic(
                    "full body",
                    produced.Status == MemberBodyProductionStatus.Absent ? "body-absent" : "body-failed",
                    $"{member}: {failure}"));
            }
            return (produced.Body, produced.Status);
        }

        bool HasConcreteBody(int token)
        {
            var handle = MetadataTokens.MethodDefinitionHandle(token & 0x00ffffff);
            return artifact.Reader.GetMethodDefinition(handle).RelativeVirtualAddress != 0;
        }

        bool IsTargetToken(int token)
            => MetadataTokens.MethodDefinitionHandle(token & 0x00ffffff) == artifact.TargetMethod;
    }

    public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodRef method,
        IOperatorTypeRelationshipResolver? relationshipResolver = null)
        => TypeProducer.TryCreateClosureMemberRequirement(
            reader,
            typeHandle,
            method,
            relationshipResolver);

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

    static ArtifactClosureInputs CreateClosureInputs(
        ArtifactRequest request,
        IOperatorTypeRelationshipResolver? relationshipResolver)
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
            closure.MemberRequirements,
            relationshipResolver);
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
        Dictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements,
        IOperatorTypeRelationshipResolver? relationshipResolver)
    {
        // Canonicalize the local assembly name so TryResolveHandle's same-assembly
        // gate matches TypeRef.Assembly, which TypeRefDecoder canonicalizes (corelib
        // facades collapse to one identity). Without this, a target assembly whose
        // own name is a canonicalized facade (System.Runtime, mscorlib, ...) would
        // fail to resolve its own definitions and drop their closure roots/facts.
        string assemblyName = reader.IsAssembly ? TypeRefDecoder.CanonicalSelf(reader) : "";
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
                root => TryCreateClosureMemberRequirement(
                    reader,
                    root,
                    method,
                    relationshipResolver),
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

    internal static CompileBackSourceResult ComposePropertyGetter(
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
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
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements,
        CompileBackMemberSurfaceIndex memberSurfaceByDefinitionName,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected,
        IOperatorTypeRelationshipResolver? relationshipResolver = null)
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
                // A read-write auto-property targeted at its getter must render both accessors so
                // the compiler-synthesized sibling accessor faithfully reproduces the original
                // setter rather than being silently dropped while still reported Complete (issue
                // #3000 class). An init-only setter renders a get/init auto-property under Full.
                // Selected keeps ordinary properties minimal (records rely on this), but explicit
                // implementations must retain init to satisfy the interface contract.
                targetIsAutoProperty
                    ? accessors.Setter.IsNil
                        ? CompileBackStubBodyKind.AutoProperty
                        : SetterIsInitOnly(reader, accessors.Setter)
                            ? bodyPolicy == RoundTripBodyPolicy.Full
                                || explicitInterfaceMemberName is not null
                                ? CompileBackStubBodyKind.AutoPropertyGetInit
                                : CompileBackStubBodyKind.AutoProperty
                            : CompileBackStubBodyKind.AutoPropertyGetSet
                    : accessors.Setter.IsNil
                        ? CompileBackStubBodyKind.TargetBody
                        : SetterIsInitOnly(reader, accessors.Setter)
                            ? CompileBackStubBodyKind.TargetGetterWithInitSetter
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
                IsVirtual: IsVirtualSlotDeclaration(getter),
                IsOverride: !targetTypeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(getter),
                IsSealed: !targetTypeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(getter)
                    && getter.Attributes.HasFlag(MethodAttributes.Final),
                ExplicitInterfaceMemberName: explicitInterfaceMemberName,
                GetterToken: MetadataTokens.GetToken(targetGetter),
                SetterToken: accessors.Setter.IsNil
                    ? null
                    : MetadataTokens.GetToken(accessors.Setter),
                MetadataName: metadataPropertyName)
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
                IncludeMemberSurface = targetFacts.Any(fact => fact.Id == "closure-member"),
                IncludeOperatorSurface = targetMembers.Any(member => member.IsOperator),
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

        var production = TypeProducer.Produce(
            reader,
            requirements,
            memberSurfaceByDefinitionName,
            diagnostics,
            relationshipResolver);
        AddImplicitInterfaceTargetDiagnostic(
            diagnostics,
            assemblyPath,
            compilationClosure,
            reader,
            targetTypeDef,
            getter,
            production.Requirements);
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
        return ComposeCompilationUnit(plan);
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
            && left.IsStatic == right.IsStatic
            && left.TypeParameters.Count == right.TypeParameters.Count
            && left.Identity.Signature == right.Identity.Signature;

    static void AddClosureTypeRequirements(
        List<CompileBackTypeRequirement> requirements,
        MetadataReader reader,
        TypeDefinitionHandle root,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements,
        bool includeFullMemberSurface = false)
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
            if (requirements.Any(requirement => requirement.Type == identity))
                return;
            var facts = closureFacts.TryGetValue(handle, out var foundFacts) ? foundFacts : [];
            var requiredMembers = closureMemberRequirements.TryGetValue(
                handle,
                out var foundMembers)
                ? foundMembers.ToArray()
                : [];

            var requirement = new CompileBackTypeRequirement(
                identity,
                ShellKind(reader, typeDef, facts),
                RequiredMembers: requiredMembers,
                PrimaryConstructor: null,
                SourceFacts: facts.Count != 0
                    ? facts.ToArray()
                    : handle == root
                        ? [new CompileBackFact("closure", "closure-root", identity.FullName)]
                        : [new CompileBackFact("metadata", "nested-closure-member-owner", identity.FullName)])
            {
                IncludeMemberSurface = includeFullMemberSurface
                    || (facts.Any(fact => fact.Id == "closure-member")
                        && (requiredMembers.Length == 0
                            || requiredMembers.Any(member => !member.IsOperator))),
                IncludeOperatorSurface = requiredMembers.Any(member => member.IsOperator),
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
                requirement => requirement.Type == interfaceIdentity);
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

    // The explicit-interface method analog of AddExplicitInterfacePropertyDeclaration:
    // a class method target whose metadata name is an explicit-interface spelling
    // (e.g. `System.Collections.IEnumerable.GetEnumerator`) is reconstructed as
    // `IType.Member() { ... }`. That declaration only binds when the reconstructed
    // interface shell actually declares the interface member, so append the interface's
    // own method declaration (`void GetEnumerator();`) to the interface requirement.
    // Without it Roslyn reports CS0539 (no member to implement) and, when RTS instead
    // sanitizes the dotted name to a plain method, the recompiled method carries the
    // wrong name and the fidelity lookup fails as ContextFail/method-not-found (#3112).
    // Returns true when the interface's own method declaration is present on the
    // reconstructed interface shell (either appended here or already required), so the
    // caller can keep the target's explicit-interface spelling. Returns false when the
    // declaration cannot be supplied — an unsupported interface-member signature, or an
    // interface that is not a standalone requirement in the closure (e.g. a nested
    // interface reached only through its enclosing root) — so the caller reverts the
    // target to the plain sanitized shape instead of emitting an unbindable `IType.Member()`.
    static bool AddExplicitInterfaceMethodDeclaration(
        List<CompileBackTypeRequirement> requirements,
        MetadataReader reader,
        TypeDefinition targetType,
        MethodDefinitionHandle targetMethod,
        IOperatorTypeRelationshipResolver? relationshipResolver)
    {
        foreach (var implementationHandle in targetType.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody != targetMethod
                || implementation.MethodDeclaration.Kind != HandleKind.MethodDefinition)
            {
                continue;
            }

            var declarationHandle = (MethodDefinitionHandle)implementation.MethodDeclaration;
            var declaration = reader.GetMethodDefinition(declarationHandle);
            string declarationName = reader.GetString(declaration.Name);
            // Operators and non-abstract default interface methods cannot be reconstructed
            // faithfully here, so fall back to the plain sanitized shape (main's behavior)
            // rather than emit an unbindable explicit implementation:
            //  - Operators: the printer renders the interface member with C#
            //    `operator`/`implicit`/`explicit` syntax (exactly when
            //    OperatorNames.FormatDisplayName rewrites the metadata name), which the
            //    explicit target spelling — the raw sanitized `op_*` name — cannot match
            //    (CS0539). A non-operator `op_*`-named method (FormatDisplayName returns it
            //    unchanged, matching the printer) stays on the normal path and can still
            //    reconstruct Exact.
            //  - Default interface methods (virtual, non-abstract): the interface member
            //    reconstructs bodyless (StubBody.None) while remaining non-abstract, which is
            //    invalid because a non-abstract interface method requires a body (CS0501).
            //    Key off the declaration's Abstract flag directly rather than IsVirtualMethod:
            //    a C# 11 `static virtual` interface method carries Virtual without NewSlot, so
            //    IsVirtualMethod (which requires NewSlot) would miss it. Any non-abstract
            //    interface declaration (default method, `static virtual`, or `sealed`) has a
            //    body and cannot be reconstructed as a bodyless declaration here; only an
            //    abstract declaration (including `static abstract`) can, so it stays on the
            //    normal path and can still reconstruct Exact.
            if (IsOperatorMethod(
                    reader,
                    declaration,
                    relationshipResolver))
            {
                return false;
            }
            if ((declaration.Attributes & MethodAttributes.Abstract) == 0)
            {
                return false;
            }
            var interfaceHandle = declaration.GetDeclaringType();
            var interfaceDef = reader.GetTypeDefinition(interfaceHandle);
            var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
            var member = TypeProducer.MethodRequirement(
                reader,
                interfaceDef,
                interfaceIdentity,
                declarationHandle,
                "explicit-interface-target-method",
                relationshipResolver);
            if (member is null)
                return false;

            int requirementIndex = requirements.FindIndex(
                requirement => requirement.Type == interfaceIdentity);
            if (requirementIndex < 0)
                return false;

            var requirement = requirements[requirementIndex];
            if (!requirement.RequiredMembers.Any(existing => TypeProducer.SameMemberShape(existing, member)))
            {
                requirements[requirementIndex] = requirement with
                {
                    RequiredMembers = requirement.RequiredMembers.Append(member).ToArray()
                };
            }
            return true;
        }
        return false;
    }

    // External explicit-interface implementations must also name the interface in the
    // containing type's base list (CS0540) and satisfy its complete required surface
    // (CS0535). Engage only for non-generic external interfaces whose transitive
    // required surface is exactly the target method; every uncertain case keeps the
    // previous plain sanitized shape and its ContextFail floor (#3112).
    static ExternalExplicitInterfaceMethodInfo? ExternalExplicitInterfaceMethod(
        MetadataReader reader,
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
        TypeDefinition targetType,
        MethodDefinitionHandle targetMethod,
        string metadataMethodName,
        int targetMethodGenericArity,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        CrossAssemblyTypeResolver? operatorTypeResolver,
        out string? declineReason)
    {
        declineReason = null;
        if (!TrySplitExplicitInterfaceMetadataName(metadataMethodName, out var interfaceMetadataName, out var targetMemberName))
            return null;

        foreach (var implementationHandle in targetType.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody != targetMethod)
                continue;

            if (implementation.MethodDeclaration.Kind == HandleKind.TypeSpecification)
                return null;
            if (implementation.MethodDeclaration.Kind != HandleKind.MemberReference)
                continue;

            var declaration = reader.GetMemberReference((MemberReferenceHandle)implementation.MethodDeclaration);
            if (declaration.Parent.Kind == HandleKind.TypeSpecification)
                return null;
            if (declaration.Parent.Kind != HandleKind.TypeReference)
                return null;

            string declarationName = reader.GetString(declaration.Name);
            if (!string.Equals(declarationName, targetMemberName, StringComparison.Ordinal))
                continue;
            // The explicit-member spelling emits Identifier(declarationName) =
            // CSharpIdentifier.Sanitize(declarationName). A keyword member name is escaped
            // losslessly (`class` -> `@class`, which binds back to `class`), but a member name
            // that does not round-trip through a C# identifier — a compiler-unspeakable name
            // (`<Bad>` -> lossily sanitized `__Bad_`), or one carrying a Unicode format
            // character that Roslyn strips when binding (`M\u200C` -> `M`) — reconstructs an
            // `IType.<member>()` that binds to no interface member (CS0539 = RecompileFail).
            // Only engage when the raw member name round-trips; else decline to the floor.
            if (!MetadataIdentifierRoundTrips(declarationName))
                return null;

            if (ExternalInterfaceReference(reader, (TypeReferenceHandle)declaration.Parent) is not { } interfaceReference)
                return null;
            if (!string.Equals(interfaceReference.MetadataFullName, interfaceMetadataName, StringComparison.Ordinal))
                continue;

            // The full transitive required surface of the external interface. #3112 Increment 1
            // engaged only when this was exactly the single target member; Increment 2 also
            // engages on multi-member interfaces by synthesizing `throw null` explicit-interface
            // stubs for every non-target member (below). An empty surface cannot contain the
            // target member, so decline to the ContextFail floor.
            if (!TryReadExternalInterfaceSurface(
                    assemblyPath,
                    compilationClosure,
                    interfaceReference,
                    operatorTypeResolver,
                    out var requiredMethods)
                || requiredMethods.Count == 0)
            {
                return null;
            }

            // The reconstructed C# spelling emits the interface's DISPLAY name
            // (Clean(metadataFullName)) in both the base-list entry and the explicit-member
            // qualifier. Clean keyword-escapes an identifier-like segment losslessly
            // (`class` -> `@class`), but rewrites a segment that is not a legal C# identifier
            // through a lossy sanitizing branch (`<Bad>` -> `__Bad_`), which then references a
            // type that does not exist (CS0246 = RecompileFail). Only engage when the raw
            // metadata name round-trips through the display name; otherwise decline to the
            // plain sanitized shape (the pre-#3112 ContextFail floor).
            if (!ExternalInterfaceNameIsRepresentable(interfaceReference.MetadataFullName))
                return null;

            // Name + arity alone are signature-blind: a resolved interface method whose
            // parameter or return types differ from what the target actually implements
            // (e.g. a reference resolved to a different build than the target was compiled
            // against) would still match here, and the reconstructed explicit member would
            // bind to no interface member (CS0539 = RecompileFail). Require the full decoded
            // signatures to agree. SignatureDecoder renders by-ref kinds, custom modifiers,
            // and multidimensional arrays ambiguously, so the decoded-string comparison is
            // only sound when neither signature carries such detail; the interface surface
            // already declined any required method that does, so decline the target here too.
            var targetMethodDefinition = reader.GetMethodDefinition(targetMethod);
            if (SignatureHasUnrepresentableDetail(reader, targetMethodDefinition))
                return null;

            MethodSignature<string> targetSignature;
            try
            {
                targetSignature = targetMethodDefinition.DecodeSignature(
                    SignatureDecoder.Instance,
                    GenericContext.ForMethod(reader, targetType, targetMethodDefinition));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }

            // Identify which required interface method the target implements: it must agree by
            // name, generic arity, and full decoded signature. Interface methods are unique by
            // signature, so exactly one required method may match; zero (the target's spelling
            // is not part of the resolved surface -> CS0539) or more than one (ambiguous)
            // declines to the ContextFail floor.
            int matchIndex = -1;
            for (int index = 0; index < requiredMethods.Count; index++)
            {
                var candidate = requiredMethods[index];
                if (string.Equals(candidate.Name, declarationName, StringComparison.Ordinal)
                    && candidate.GenericArity == targetMethodGenericArity
                    && string.Equals(candidate.ReturnType, targetSignature.ReturnType, StringComparison.Ordinal)
                    && candidate.ParameterTypes.SequenceEqual(targetSignature.ParameterTypes, StringComparer.Ordinal))
                {
                    if (matchIndex >= 0)
                        return null;
                    matchIndex = index;
                }
            }
            if (matchIndex < 0)
                return null;
            if (requiredMethods[matchIndex].IsOperator)
                return null;

            // A reconstructed sibling type (or sibling sub-namespace) in the recompile
            // closure can intercept the leading identifier of the external interface
            // spelling when the target lives inside a namespace, binding the clean
            // `Namespace.IType.Member` spelling against the sibling instead of the external
            // interface (CS0426/CS0535/CS0540 = RecompileFail). Roslyn cannot author such a
            // shape (a shadowing sibling forces `global::` into the explicit override's
            // metadata name, which the equality check above already declines), but
            // hand-rolled IL can, and RoundTripScope.All reconstructs every sibling into its
            // namespace. When any closure type would shadow a spelling segment in a namespace
            // in scope of the target, decline to the plain sanitized shape (the pre-#3112
            // ContextFail floor) rather than emit a new RecompileFail.
            if (ExternalInterfaceSpellingShadowedByClosure(
                    reader,
                    targetType,
                    closureRoots,
                    interfaceReference.MetadataFullName))
            {
                return null;
            }

            // Naming the interface in the base list forces the reconstructed type to satisfy
            // its ENTIRE required surface (CS0535). The target method reconstructs the matched
            // member with its real body. Preserve a matching local or inherited public
            // implementation when one exists; otherwise synthesize a `throw null`
            // explicit-interface stub. If any member cannot be spelled as valid, bindable C#,
            // decline the whole engagement rather than emit a partial surface.
            var additionalInterfaceMembers = new List<ExternalInterfaceMemberRequirement>(
                requiredMethods.Count - 1);
            var targetTypeHandle = targetMethodDefinition.GetDeclaringType();
            for (int index = 0; index < requiredMethods.Count; index++)
            {
                if (index == matchIndex)
                    continue;
                var requiredMethod = requiredMethods[index];
                if (!MetadataIdentifierRoundTrips(requiredMethod.Name))
                    return null;
                var implicitImplementation = FindImplicitInterfaceMethod(
                    reader,
                    targetTypeHandle,
                    requiredMethod);
                if (implicitImplementation.Method.IsNil
                    && !implicitImplementation.SearchComplete)
                {
                    declineReason =
                        $"{reader.GetFullTypeName(targetType)}::{metadataMethodName}: "
                        + $"required interface member '{requiredMethod.Name}' may be inherited "
                        + "from a base that the reconstructed shell cannot name";
                    return null;
                }
                var member = implicitImplementation.Method.IsNil
                    ? SynthesizeExternalInterfaceStub(
                        interfaceReference.DisplayFullName,
                        requiredMethod)
                    : ImplicitInterfaceMethodRequirement(
                        reader,
                        implicitImplementation.Method);
                if (member is null)
                    return null;
                additionalInterfaceMembers.Add(new ExternalInterfaceMemberRequirement(
                    implicitImplementation.Method.IsNil
                        ? targetTypeHandle
                        : reader.GetMethodDefinition(implicitImplementation.Method).GetDeclaringType(),
                    member));
            }

            string explicitInterfaceMemberName =
                $"{interfaceReference.DisplayFullName}.{Identifier(declarationName)}";
            return new ExternalExplicitInterfaceMethodInfo(
                interfaceReference.DisplayFullName,
                explicitInterfaceMemberName,
                additionalInterfaceMembers);
        }

        return null;
    }

    static ImplicitInterfaceMethodLookup FindImplicitInterfaceMethod(
        MetadataReader reader,
        TypeDefinitionHandle targetType,
        ExternalInterfaceRequiredMethod requiredMethod)
    {
        TypeDefinitionHandle currentType = targetType;
        while (!currentType.IsNil)
        {
            MethodDefinitionHandle match = default;
            foreach (var methodHandle in reader.GetTypeDefinition(currentType).GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                    || method.Attributes.HasFlag(MethodAttributes.Static)
                    || reader.GetString(method.Name) != requiredMethod.Name
                    || method.GetGenericParameters().Count != requiredMethod.GenericArity
                    || SignatureHasUnrepresentableDetail(reader, method))
                {
                    continue;
                }

                MethodSignature<string> signature;
                try
                {
                    signature = method.DecodeSignature(
                        SignatureDecoder.Instance,
                        GenericContext.ForMethod(
                            reader,
                            reader.GetTypeDefinition(currentType),
                            method));
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    continue;
                }

                if (signature.ReturnType != requiredMethod.ReturnType
                    || !signature.ParameterTypes.SequenceEqual(
                        requiredMethod.ParameterTypes,
                        StringComparer.Ordinal))
                {
                    continue;
                }
                if (!match.IsNil)
                    return new(default, SearchComplete: true);
                match = methodHandle;
            }

            if (!match.IsNil)
                return new(match, SearchComplete: true);

            var currentDef = reader.GetTypeDefinition(currentType);
            var baseType = currentDef.BaseType;
            if (baseType.IsNil || IsCompilerImpliedBase(reader, currentDef))
                return new(default, SearchComplete: true);
            if (baseType.Kind != HandleKind.TypeDefinition
                || TypeShellProducer.ReconstructedBaseTypeDisplay(
                    reader,
                    currentDef,
                    ShellKind(reader, currentDef) == CompileBackTypeKind.Class) is null)
            {
                return new(default, SearchComplete: false);
            }
            currentType = (TypeDefinitionHandle)baseType;
        }

        return new(default, SearchComplete: true);
    }

    static bool IsCompilerImpliedBase(MetadataReader reader, TypeDefinition typeDef)
    {
        string? baseType;
        try
        {
            baseType = TypeResolver.GetTypeName(
                reader,
                typeDef.BaseType,
                GenericContext.ForType(reader, typeDef));
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }

        return baseType is "System.Object" or "System.ValueType" or "System.Enum"
            or "System.Delegate" or "System.MulticastDelegate";
    }

    static CompileBackMemberRequirement? ImplicitInterfaceMethodRequirement(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle)
    {
        var typeDef = reader.GetTypeDefinition(
            reader.GetMethodDefinition(methodHandle).GetDeclaringType());
        return TypeProducer.MethodRequirement(
            reader,
            typeDef,
            CompileBackTypeIdentity.FromDefinition(reader, typeDef),
            methodHandle,
            "external-interface-implicit-method");
    }

    // Synthesizes a `throw null` explicit-interface stub for a NON-target member of an external
    // interface, so the reconstructed type satisfies the interface's full required surface
    // (CS0535) after the interface is named in the base list. Returns null when the member
    // cannot be spelled as valid, bindable C#, in which case the caller declines the whole
    // engagement and keeps the ContextFail floor. Only arity-0 methods reach here
    // (TryCollectRequiredInterfaceMethods declines every generic/property/event/non-public
    // member and every by-ref/pointer/array/function-pointer/modifier signature), so the stub
    // has no type parameters and its decoded signature strings are a faithful C# spelling — the
    // same strings the target signature match above already trusts.
    static CompileBackMemberRequirement? SynthesizeExternalInterfaceStub(
        string interfaceDisplayName,
        ExternalInterfaceRequiredMethod method)
    {
        // The stub names the member via `IType.<member>()`; a name that does not round-trip
        // through a C# identifier binds to no interface member (CS0539), leaving the interface
        // member unimplemented (CS0535). Re-defend arity here even though the surface is
        // arity-0: a generic stub would need to restate constraints it cannot see.
        if (method.GenericArity != 0 || !MetadataIdentifierRoundTrips(method.Name))
            return null;

        // SignatureDecoder spells nested types with the metadata separator (`Outer+Inner`),
        // which is not bindable C#, and degrades a generic-parameter-bearing type to a bare
        // `object` (via CleanTypeDisplay's `!` guard) that would silently mis-satisfy the
        // member. The required surface already declined generics and unrepresentable detail, so
        // neither marker should survive; decline defensively if one does rather than emit an
        // unbindable (CS0246) or drifted (CS0535) stub.
        if (SignatureTypeIsUnspellable(method.ReturnType)
            || method.ParameterTypes.Any(SignatureTypeIsUnspellable))
        {
            return null;
        }

        string explicitName = $"{interfaceDisplayName}.{Identifier(method.Name)}";
        var returnType = CompileBackTypeSignature.Display(method.ReturnType);
        var parameters = method.ParameterTypes
            .Select((type, index) => new CompileBackParameter(
                $"arg{index}",
                CompileBackTypeSignature.Display(type)))
            .ToArray();
        string declarationSignature = ExplicitInterfaceMethodDeclarationSignature(
            explicitName,
            returnType,
            [],
            parameters);
        return new CompileBackMemberRequirement(
            new CompileBackMethodIdentity(interfaceDisplayName, method.Name, 0, declarationSignature),
            CompileBackMemberKind.Method,
            false,
            parameters,
            returnType,
            [],
            CompileBackStubBodyKind.Throw,
            null,
            [new CompileBackFact("metadata", "external-interface-stub", explicitName)],
            ExplicitInterfaceMemberName: explicitName);
    }

    // A decoded signature type is unspellable when it carries a metadata artifact that
    // SignatureDecoder/CleanTypeDisplay cannot turn into bindable C#: `+` (a nested type's
    // metadata separator, e.g. `Outer+Inner`) or `!` (a generic parameter, which CleanTypeDisplay
    // collapses to `object`). Neither character can appear in a legal C# type spelling, so
    // rejecting them never declines a spellable type — it only preserves the floor.
    static bool SignatureTypeIsUnspellable(string decodedType)
        => decodedType.Contains('+', StringComparison.Ordinal)
           || decodedType.Contains('!', StringComparison.Ordinal);

    // True when every dotted segment of the external interface's raw metadata full name
    // round-trips through the display spelling the reconstruction emits. Clean(metadataFullName)
    // keyword-escapes an identifier-like segment losslessly (`class` -> `@class`, which C#
    // resolves back to `class`). A segment that does not round-trip — a compiler-unspeakable
    // name (`<Bad>`, rewritten by Clean's sanitizing branch to a different identifier
    // `__Bad_`) or one carrying a Unicode format character Roslyn strips when binding
    // (`G\u200Cood` -> `Good`) — names no real type, so the emitted `using`/qualifier fails to
    // bind (CS0246 = RecompileFail). Nested external types are qualified with `.` (Outer.Inner),
    // so an ordinary nested interface passes; only genuinely unrepresentable names are declined
    // here to the sanitized ContextFail floor.
    static bool ExternalInterfaceNameIsRepresentable(string metadataFullName)
    {
        foreach (var segment in metadataFullName.Split('.'))
        {
            if (!MetadataIdentifierRoundTrips(segment))
                return false;
        }

        return true;
    }

    // True when a raw metadata identifier round-trips through the C# identifier the
    // reconstruction emits (via CSharpIdentifier.Sanitize / Clean). Lexical identifier-likeness
    // is necessary but not sufficient: Roslyn's identifier binding additionally removes Unicode
    // format (Cf) characters, so a name carrying a Cf character (e.g. U+200C) binds to a
    // DIFFERENT name than its exact metadata spelling (CS0246/CS0539 = RecompileFail). Roslyn
    // does NOT apply Unicode normalization to identifiers: a decomposed (non-NFC) metadata name
    // such as `e` + U+0301 is emitted and bound verbatim, so it round-trips exactly and must
    // NOT be declined. Require the name to be identifier-like and free of Cf characters; keyword
    // names still round-trip (Escape only prepends `@`, which binding strips).
    static bool MetadataIdentifierRoundTrips(string name)
    {
        if (!CSharpIdentifier.IsIdentifierLike(name))
            return false;

        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
                return false;
        }

        return true;
    }

    // True when a type declared in the recompile closure would intercept the leading
    // identifier of the external interface spelling as spelled from inside the target type's
    // namespace. The external-interface spelling appears in two positions —
    // the base-list entry and the explicit-member qualifier — and only its FIRST segment can
    // ever be shadowed into a compile error. The explicit-member qualifier is always emitted
    // fully qualified (e.g. `System.Collections.IEnumerable.GetEnumerator`), so its head is
    // the first segment. The base-list entry is either emitted fully qualified (same first
    // segment) or shortened by the using-collapser to the bare type name (e.g. `IEnumerable`);
    // the collapser is collision-aware (CSharpDeclarationWriter.TypeNamePlan keeps a name
    // qualified when its simple name is ambiguous), so it only shortens to a simple name that
    // does NOT collide with a sibling — a sibling matching the final type name forces the base
    // list to stay fully qualified (leading `System`) and still compiles. No partial
    // qualification (`Collections.IEnumerable`) is ever emitted, so middle and final segments
    // never lead into a failure and must not trigger a decline (that over-declines a
    // compiler-authored Exact). <paramref name="closureRoots"/> already reflects the active
    // scope, so under RoundTripScope.Cluster (which does not reconstruct the shadowing sibling)
    // this returns false and engagement proceeds; under RoundTripScope.All it declines the
    // crafted-IL first-segment shadow shape.
    //
    // The leading segment is taken from the raw METADATA full name, not the C# display name,
    // so it compares raw-to-raw against the closure types' metadata names/namespaces. A
    // namespace segment that is a C# keyword (e.g. `class`) is escaped to `@class` in the
    // display name but stored raw in metadata; deriving the segment from the display name here
    // would miss a real `N.class`-shadows-`class.IProbe` collision and emit a new RecompileFail.
    static bool ExternalInterfaceSpellingShadowedByClosure(
        MetadataReader reader,
        TypeDefinition targetType,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        string interfaceMetadataFullName)
    {
        string leadingSegment = interfaceMetadataFullName.Split('.', 2)[0];
        string targetNamespace = reader.GetString(targetType.Namespace);

        foreach (var handle in closureRoots)
        {
            var candidate = reader.GetTypeDefinition(handle);
            string candidateNamespace = reader.GetString(candidate.Namespace);

            // Type collision: a reconstructed top-level type whose simple name matches the
            // leading spelling identifier and that sits in the target's namespace, an ancestor
            // namespace, or the global namespace is nearer than the external namespace
            // chain and intercepts the identifier.
            if (string.Equals(reader.GetString(candidate.Name), leadingSegment, StringComparison.Ordinal)
                && NamespaceIsInScope(candidateNamespace, targetNamespace))
            {
                return true;
            }

            // Sub-namespace collision: a reconstructed type declared beneath a non-global
            // in-scope namespace introduces a nearer namespace whose leading segment can
            // intercept the identifier. Types beneath the global namespace merge with the
            // external namespace chain instead of shadowing it, so global is excluded here.
            if (LeadingSegmentBelowInScopeNonGlobalNamespace(candidateNamespace, targetNamespace) is { } childSegment
                && string.Equals(childSegment, leadingSegment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // True when a type declared in <paramref name="candidateNamespace"/> is visible by its
    // simple name from inside <paramref name="targetNamespace"/>: the same namespace, an
    // ancestor namespace, or the global namespace.
    static bool NamespaceIsInScope(string candidateNamespace, string targetNamespace)
        => candidateNamespace.Length == 0
           || string.Equals(candidateNamespace, targetNamespace, StringComparison.Ordinal)
           || targetNamespace.StartsWith(candidateNamespace + ".", StringComparison.Ordinal);

    // When <paramref name="candidateNamespace"/> is strictly nested under the target
    // namespace or one of its non-global ancestors, returns the single namespace segment
    // introduced directly beneath the nearest such in-scope namespace (which becomes a name
    // visible unqualified from the target). Returns null otherwise.
    static string? LeadingSegmentBelowInScopeNonGlobalNamespace(string candidateNamespace, string targetNamespace)
    {
        if (targetNamespace.Length == 0 || candidateNamespace.Length == 0)
            return null;

        for (string scope = targetNamespace; scope.Length > 0;)
        {
            if (candidateNamespace.StartsWith(scope + ".", StringComparison.Ordinal))
            {
                string rest = candidateNamespace[(scope.Length + 1)..];
                int dot = rest.IndexOf('.');
                return dot < 0 ? rest : rest[..dot];
            }

            int lastDot = scope.LastIndexOf('.');
            scope = lastDot < 0 ? "" : scope[..lastDot];
        }

        return null;
    }

    static bool TryReadExternalInterfaceSurface(
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
        ExternalInterfaceReferenceInfo interfaceReference,
        CrossAssemblyTypeResolver? operatorTypeResolver,
        out IReadOnlyList<ExternalInterfaceRequiredMethod> requiredMethods)
    {
        // Read the interface surface from the SAME frozen, filename-deduplicated dependency
        // closure the recompile references (ReturnToSender.CreateCompilationClosure:
        // resolver.ResolveAll() with ExcludeTargetAssembly, deduplicated by simple assembly
        // name). Reading from that exact acquisition generation — rather than reopening paths
        // or selecting a different identity/platform candidate — guarantees the validated
        // members are precisely those C# requires against the reconstructed `: DisplayName`,
        // and lets us prove the interface is defined by exactly one assembly in the closure
        // (otherwise the unqualified base-list name is ambiguous, CS0433).
        // Memoize per (target assembly, interface identity, interface full name): the same
        // interface recurs across many targets and rescanning the closure per target is an
        // unbounded slowdown. Negative results (unresolvable, ambiguous, or unrepresentable)
        // are cached so they are not retried.
        ReturnToSender.CompilationClosure closure =
            compilationClosure
            ?? ReturnToSender.CreateCompilationClosure(assemblyPath);
        AssemblyDependencyResolver resolver = closure.Resolver;
        var cacheKey = new ExternalInterfaceSurfaceCacheKey(
            interfaceReference.AssemblyIdentity,
            interfaceReference.MetadataFullName);
        var surfaces = _externalInterfaceSurfaces.GetValue(
            resolver,
            static _ => []);
        var cached = surfaces.GetOrAdd(cacheKey, _ =>
        {
            (ResolvedAssemblyReference Assembly, MetadataTypeDefinitionAddress Address)?
                resolvedDefinition = null;
            if (PlatformKeys.IsPlatform(
                    interfaceReference.AssemblyIdentity.PublicKeyToken))
            {
                resolvedDefinition = ResolveExternalTypeDefinition(
                    closure.TargetAssembly,
                    interfaceReference.AssemblyIdentity,
                    interfaceReference.MetadataFullName,
                    resolver);
                if (resolvedDefinition is null)
                    return null;
            }

            // Locate the single closure assembly that defines the interface as a
            // TypeDefinition. Type forwarders are ExportedType rows (FindType returns null),
            // so a BCL interface defined once in CoreLib and forwarded elsewhere resolves to
            // exactly one definition. Zero, or more than one, definition declines.
            ResolvedAssemblyReference? definitionAssembly = null;
            foreach (var dependency in resolver.ResolveAll())
            {
                ResolvedAssemblyReference? candidate =
                    resolver.Acquire(dependency);
                if (candidate is null)
                    continue;
                try
                {
                    using Stream probeStream = candidate.OpenRead();
                    using var probeReader = new PEReader(probeStream);
                    if (!probeReader.HasMetadata)
                        continue;
                    if (TypeProducer.FindType(probeReader.GetMetadataReader(), interfaceReference.MetadataFullName) is null)
                        continue;
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or ArgumentException)
                {
                    // A dependency we cannot inspect cannot be shown to define the type; skip it.
                    continue;
                }

                if (definitionAssembly is not null)
                    return null;
                definitionAssembly = candidate;
            }

            if (definitionAssembly is null)
                return null;

            try
            {
                using Stream stream = definitionAssembly.OpenRead();
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                    return null;
                var reader = peReader.GetMetadataReader();
                if (TypeProducer.FindType(reader, interfaceReference.MetadataFullName) is not { } interfaceHandle)
                    return null;
                if (resolvedDefinition is { } definition
                    && (!definition.Address.TryResolve(
                            reader,
                            out TypeDefinitionHandle resolvedHandle)
                        || resolvedHandle != interfaceHandle
                        || !HaveSameImageContent(
                            definition.Assembly,
                            definitionAssembly)))
                {
                    return null;
                }

                var collected = new List<ExternalInterfaceRequiredMethod>();
                return TryCollectRequiredInterfaceMethods(
                        reader,
                        definitionAssembly,
                        interfaceHandle,
                        resolver,
                        definitionAssembly.Path
                            ?? definitionAssembly.Identity.ToString(),
                        operatorTypeResolver,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        collected)
                    ? collected
                    : null;
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                return null;
            }
        });

        if (cached is null)
        {
            requiredMethods = [];
            return false;
        }

        requiredMethods = cached;
        return true;
    }

    readonly record struct ExternalInterfaceSurfaceCacheKey(
        AssemblyReferenceIdentity AssemblyIdentity,
        string MetadataFullName);

    static readonly ConditionalWeakTable<
        AssemblyDependencyResolver,
        ConcurrentDictionary<
            ExternalInterfaceSurfaceCacheKey,
            IReadOnlyList<ExternalInterfaceRequiredMethod>?>>
        _externalInterfaceSurfaces = new();

    // Probes whether a method signature carries detail that SignatureDecoder renders
    // ambiguously, making a decoded-string comparison unsound: by-ref parameters (in/out/ref
    // all decode to "ref T"), custom modifiers (dropped by GetModifiedType), multidimensional
    // arrays (a rank-1 MDArray decodes identically to an SZArray), function pointers
    // (calling-convention modopts), and generic parameters (spelled by their metadata name,
    // so a reordered/renamed drift can compare equal while binding to a different position).
    // When a signature is free of all of these, its decoded string form is a faithful
    // identity and string equality is an exact signature check.
    sealed class UnrepresentableSignatureProbe : ISignatureTypeProvider<bool, object?>
    {
        public static readonly UnrepresentableSignatureProbe Instance = new();

        // `typedref` (System.TypedReference) is a restricted primitive that cannot be a method
        // return type in C# (CS1599) — nor a field, type argument, or array element — so an
        // interface member mentioning it cannot be reconstructed as a bindable explicit
        // implementation or `throw null` stub (the surface would emit CS1599 = RecompileFail).
        // SignatureDecoder spells it as the bare token `TypedReference`, which carries no `+`/`!`
        // marker for SignatureTypeIsUnspellable to catch, and it is not emittable from C# source
        // (it appears only on hand-authored / Reflection.Emit IL), so treat it as unrepresentable
        // detail here and decline the whole surface to the ContextFail floor (#3112). Every other
        // primitive is a faithful, bindable C# spelling.
        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode == PrimitiveTypeCode.TypedReference;
        public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;
        public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => false;
        public bool GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            // Mirror SignatureDecoder.GetTypeFromSpecification: a cross-handle TypeSpec
            // re-entry (including a cycle back to this row) must be bounded or a crafted
            // dependency could overflow the stack — uncatchable, crashing the harness. A
            // TypeSpec too deep or structurally unsafe to decode is, for our purposes,
            // unrepresentable: treat it as such and decline.
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return true;
            using (scope)
                return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
        public bool GetSZArrayType(bool elementType) => elementType;
        public bool GetArrayType(bool elementType, ArrayShape shape) => true;
        public bool GetByReferenceType(bool elementType) => true;
        public bool GetPointerType(bool elementType) => elementType;
        public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments)
            => genericType || typeArguments.Any(argument => argument);
        public bool GetGenericMethodParameter(object? genericContext, int index) => true;
        public bool GetGenericTypeParameter(object? genericContext, int index) => true;
        public bool GetFunctionPointerType(MethodSignature<bool> signature) => true;
        public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => true;
        public bool GetPinnedType(bool elementType) => elementType;
    }

    static bool SignatureHasUnrepresentableDetail(MetadataReader reader, MethodDefinition method)
    {
        try
        {
            var signature = method.DecodeSignature(UnrepresentableSignatureProbe.Instance, (object?)null);
            // The decoded return/parameter strings do not carry the method's calling
            // convention, so a VarArgs (`__arglist`) method is spelled identically to a
            // fixed-arity one. C# cannot express `__arglist` in a reconstructed explicit
            // member, so any non-default calling convention is unrepresentable and declines.
            return signature.Header.CallingConvention != SignatureCallingConvention.Default
                || signature.ReturnType
                || signature.ParameterTypes.Any(parameter => parameter);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return true;
        }
    }

    // Whether a type (and every enclosing type) is accessible to an assembly that references
    // the defining assembly without InternalsVisibleTo: a top-level type must be public, and a
    // nested type must be nested-public with a publicly accessible declaring type.
    static bool IsPubliclyAccessible(MetadataReader reader, TypeDefinition type)
    {
        while (true)
        {
            var visibility = type.Attributes & TypeAttributes.VisibilityMask;
            var declaring = type.GetDeclaringType();
            if (declaring.IsNil)
                return visibility == TypeAttributes.Public;
            if (visibility != TypeAttributes.NestedPublic)
                return false;
            type = reader.GetTypeDefinition(declaring);
        }
    }

    static bool TryCollectExternalInterfaceMethods(
        ResolvedAssemblyReference requestingAssembly,
        ExternalInterfaceReferenceInfo interfaceReference,
        AssemblyDependencyResolver resolver,
        CrossAssemblyTypeResolver? operatorTypeResolver,
        HashSet<string> visited,
        List<ExternalInterfaceRequiredMethod> methods)
    {
        try
        {
            if (ResolveExternalTypeDefinition(
                    requestingAssembly,
                    interfaceReference.AssemblyIdentity,
                    interfaceReference.MetadataFullName,
                    resolver)
                is not { } definition)
            {
                return false;
            }

            using var stream = definition.Assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return false;

            var externalReader = peReader.GetMetadataReader();
            if (!definition.Address.TryResolve(
                    externalReader,
                    out TypeDefinitionHandle interfaceHandle))
            {
                return false;
            }

            string assemblyKey = definition.Assembly.Path is { Length: > 0 } path
                ? Path.GetFullPath(path)
                : $"{definition.Assembly.Identity}|{definition.Address.ModuleVersionId}";
            return TryCollectRequiredInterfaceMethods(
                externalReader,
                definition.Assembly,
                interfaceHandle,
                resolver,
                assemblyKey,
                operatorTypeResolver,
                visited,
                methods);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static (
        ResolvedAssemblyReference Assembly,
        MetadataTypeDefinitionAddress Address)?
        ResolveExternalTypeDefinition(
            ResolvedAssemblyReference requestingAssembly,
            AssemblyReferenceIdentity assemblyIdentity,
            string metadataFullName,
            AssemblyDependencyResolver resolver)
    {
        if (!PlatformKeys.IsPlatform(assemblyIdentity.PublicKeyToken))
        {
            ResolvedAssemblyReference? selected =
                resolver.Resolve(
                    assemblyIdentity,
                    AssemblyResolutionScope.Any)
                ?? resolver.Resolve(
                    assemblyIdentity,
                    AssemblyResolutionScope.Platform);
            return selected is null
                ? null
                : ResolveExternalTypeDefinition(
                    selected,
                    metadataFullName,
                    resolver);
        }

        return ResolveExternalTypeDefinition(
            requestingAssembly,
            metadataFullName,
            resolver,
            validName => TypeResolutionRequest.FromReference(
                assemblyIdentity,
                AssemblyBindingOrigin.FromAssembly(requestingAssembly),
                AssemblyResolutionScope.Platform,
                validName));
    }

    internal static (
        ResolvedAssemblyReference Assembly,
        MetadataTypeDefinitionAddress Address)?
        ResolveExternalTypeDefinition(
            ResolvedAssemblyReference assembly,
            string metadataFullName,
            AssemblyDependencyResolver resolver)
    {
        foreach (AssemblyResolutionScope scope in
            new[] { AssemblyResolutionScope.Any, AssemblyResolutionScope.Platform })
        {
            var resolved = ResolveExternalTypeDefinition(
                assembly,
                metadataFullName,
                resolver,
                validName => TypeResolutionRequest.FromAssembly(
                    assembly,
                    scope,
                    validName));
            if (resolved is not null)
                return resolved;
        }

        return null;
    }

    static (
        ResolvedAssemblyReference Assembly,
        MetadataTypeDefinitionAddress Address)?
        ResolveExternalTypeDefinition(
            ResolvedAssemblyReference rootAssembly,
            string metadataFullName,
            AssemblyDependencyResolver resolver,
            Func<MetadataTypeDefinitionName, TypeResolutionRequest> createRequest)
    {
        int separator = metadataFullName.LastIndexOf('.');
        string @namespace = separator < 0
            ? ""
            : metadataFullName[..separator];
        string name = metadataFullName[(separator + 1)..];
        if (MetadataTypeDefinitionName.Create(@namespace, [name])
            is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            return null;
        }

        TypeResolutionRequest request = createRequest(valid.Name);
        using TypeResolutionContext structuredContext =
            TypeResolutionContext.Create(
                resolver,
                [rootAssembly],
                [request]);
        if (structuredContext.Resolve(request)
            is not TypeResolutionOutcome.Resolved resolved)
        {
            return null;
        }

        // Replay the complete initial binding and forwarding walk through
        // Roslyn's sibling-first closure. Engage only when both paths reach the
        // same defining image and durable TypeDef address.
        using TypeResolutionContext compilationContext =
            TypeResolutionContext.Create(
                new CompilationClosureBindingPolicy(resolver),
                [rootAssembly],
                [request]);
        if (compilationContext.Resolve(request)
                is not TypeResolutionOutcome.Resolved compilationResolved
            || compilationResolved.Definition.Assembly.Assembly.Identity
                != resolved.Definition.Assembly.Assembly.Identity
            || compilationResolved.Definition.Address
                != resolved.Definition.Address
            || !HaveSameImageContent(
                compilationResolved.Definition.Assembly.Assembly,
                resolved.Definition.Assembly.Assembly))
        {
            return null;
        }

        return (
            resolved.Definition.Assembly.Assembly,
            resolved.Definition.Address);
    }

    static bool HaveSameImageContent(
        ResolvedAssemblyReference left,
        ResolvedAssemblyReference right)
    {
        if (left.Registration == right.Registration)
            return true;

        using Stream leftStream = left.OpenRead();
        using Stream rightStream = right.OpenRead();
        byte[] leftHash = SHA256.HashData(leftStream);
        byte[] rightHash = SHA256.HashData(rightStream);
        return leftHash.AsSpan().SequenceEqual(rightHash);
    }

    sealed class CompilationClosureBindingPolicy : IAssemblyBindingPolicy
    {
        readonly AssemblyDependencyResolver _resolver;
        readonly Dictionary<string, ResolvedAssemblyReference> _references =
            new(StringComparer.OrdinalIgnoreCase);

        public CompilationClosureBindingPolicy(
            AssemblyDependencyResolver resolver)
        {
            _resolver = resolver;
            foreach (ResolvedAssemblyDependency dependency in resolver.ResolveAll())
            {
                ResolvedAssemblyReference? reference =
                    resolver.Acquire(dependency);
                if (reference is not null)
                {
                    _references.TryAdd(
                        Path.GetFileNameWithoutExtension(dependency.Path),
                        reference);
                }
            }
        }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(AssemblyBindingRequest request)
        {
            if (request.Target
                    is AssemblyBindingTarget.AssemblyReference reference)
            {
                return _references.TryGetValue(
                    reference.Identity.Name,
                    out ResolvedAssemblyReference? selected)
                        ? AssemblyBindingSelection.Found(selected)
                        : AssemblyBindingSelection.NotFound();
            }

            return _resolver.Select(
                new AssemblyBindingRequest(
                    request.Target,
                    request.Origin,
                    AssemblyResolutionScope.Any));
        }
    }

    static bool TryCollectRequiredInterfaceMethods(
        MetadataReader reader,
        ResolvedAssemblyReference assembly,
        TypeDefinitionHandle interfaceHandle,
        AssemblyDependencyResolver resolver,
        string assemblyKey,
        CrossAssemblyTypeResolver? operatorTypeResolver,
        HashSet<string> visited,
        List<ExternalInterfaceRequiredMethod> methods)
    {
        IOperatorTypeRelationshipResolver? relationshipResolver =
            operatorTypeResolver?.CreateOperatorRelationshipResolver(
                assembly);
        var interfaceDef = reader.GetTypeDefinition(interfaceHandle);
        string interfaceName = reader.GetFullTypeName(interfaceDef);
        if (!visited.Add($"{assemblyKey}|{interfaceName}"))
            return false;
        if ((interfaceDef.Attributes & TypeAttributes.Interface) == 0
            || interfaceDef.GetGenericParameters().Count != 0)
        {
            return false;
        }
        // The reconstructed assembly references the interface's defining assembly but is not
        // granted InternalsVisibleTo, so it can only name a publicly accessible interface.
        // Engaging on an internal (or nested non-public) interface would emit `: DisplayName`
        // against a type the recompile cannot see (CS0122 = RecompileFail). A public interface
        // also guarantees, by C#'s consistent-accessibility rule, that its method signature
        // types are at least as accessible, so the emitted members reference only public types.
        if (!IsPubliclyAccessible(reader, interfaceDef))
            return false;

        // The reconstructed explicit member names the interface twice — in the base list
        // (`: DisplayName`) and in the member qualifier (`void DisplayName.M()`). If the resolved
        // interface is marked `[Obsolete(..., error: true)]`, naming it is a hard CS0619 error,
        // turning the sanitized ContextFail floor (which never names the interface) into a
        // RecompileFail. `#pragma warning disable` in the emitted source suppresses warning-level
        // obsolescence but not the error form, so decline any real (non-compiler-compat) obsolete
        // interface. TryGetObsoleteAttribute already excludes Roslyn's synthetic compiler-compat
        // [Obsolete] markers (required members, ref structs), which do not error when referenced.
        if (AttributeReader.TryGetObsoleteAttribute(reader, interfaceDef.GetCustomAttributes(), out _))
            return false;

        // Naming the interface in the base list (`: DisplayName`) forces the recompile to bind to
        // it, which requires every feature the interface demands via [CompilerFeatureRequired]. If
        // the resolved interface carries an unsatisfiable feature marker, binding it is a hard
        // CS9041 (feature not supported), turning the sanitized ContextFail floor (which never
        // names the interface, so never triggers the requirement) into a RecompileFail. This
        // attribute is not emittable from C# source and appears only on hand-authored or
        // future/downlevel-drifted IL, so decline any interface that carries it and keep the floor.
        if (AttributeReader.HasAttribute(reader, interfaceDef.GetCustomAttributes(), KnownAttributeNames.CompilerFeatureRequiredAttribute))
            return false;

        if (interfaceDef.GetProperties().Count != 0 || interfaceDef.GetEvents().Count != 0)
            return false;

        foreach (var methodHandle in interfaceDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            string methodName = reader.GetString(method.Name);
            if (method.Attributes.HasFlag(MethodAttributes.Static)
                || (method.Attributes & MethodAttributes.Abstract) == 0
                || IsOperatorMethod(
                    reader,
                    method,
                    relationshipResolver))
            {
                return false;
            }

            // A public interface may still declare a non-public member (C# 8+ allows explicit
            // accessibility on interface members). The reconstructed assembly references the
            // interface's defining assembly but is not granted InternalsVisibleTo, so it can only
            // name a public member; emitting `void IProbe.M()` for an `internal`/`protected`
            // member would be inaccessible (CS0122 = RecompileFail). Decline any non-public
            // required method and keep the ContextFail floor.
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                return false;

            // A generic interface method carries constraints that the reconstructed explicit
            // member cannot restate (C# inherits them from the interface). A type parameter can
            // appear only in a constraint — invisible to the return/parameter signature probe —
            // so a constraint drift between the target and the resolved interface would emit a
            // body that no longer satisfies the interface's constraint (CS1061/CS0535). The
            // signature comparison also cannot distinguish generic parameters by position (they
            // are spelled by name). Decline every generic method and keep the ContextFail floor.
            if (method.GetGenericParameters().Count != 0)
                return false;

            // A required method whose signature SignatureDecoder cannot represent faithfully
            // (by-ref kinds, custom modifiers, multidimensional arrays, function pointers)
            // cannot be safely signature-compared against the target, so decline the whole
            // surface and keep the ContextFail floor rather than risk a CS0535/CS0539 emit.
            if (SignatureHasUnrepresentableDetail(reader, method))
                return false;
            var requiredSignature = method.DecodeSignature(
                SignatureDecoder.Instance,
                GenericContext.ForMethod(reader, interfaceDef, method));
            methods.Add(new ExternalInterfaceRequiredMethod(
                methodName,
                method.GetGenericParameters().Count,
                requiredSignature.ReturnType,
                requiredSignature.ParameterTypes,
                IsOperatorMethod(
                    reader,
                    method,
                    relationshipResolver)));
        }

        foreach (var implementationHandle in interfaceDef.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(implementationHandle);

            // A member inherited from a base interface is collected here with no record of the
            // interface that DECLARED it: ExternalInterfaceRequiredMethod carries only name,
            // arity, and signature, and the caller qualifies every synthesized stub with the
            // ROOT interface's display name. C# requires an inherited member to be spelled with
            // its declaring interface (`void IBase.M()`), so a stub emitted as `void IRoot.M()`
            // is CS0539 (not a member of IRoot) and leaves the base member unimplemented
            // (CS0535 = RecompileFail). A base interface that contributes NO required members
            // (an empty marker, or one carrying only already-declined property/event/generic
            // members) is harmless. So allow base interfaces that add nothing to the surface,
            // but decline the whole engagement to the ContextFail floor the moment any base
            // interface contributes a required member (#3112).
            int methodsBefore = methods.Count;

            if (implementation.Interface.Kind == HandleKind.TypeDefinition)
            {
                if (!TryCollectRequiredInterfaceMethods(
                        reader,
                        assembly,
                        (TypeDefinitionHandle)implementation.Interface,
                        resolver,
                        assemblyKey,
                        operatorTypeResolver,
                        visited,
                        methods))
                {
                    return false;
                }
                if (methods.Count != methodsBefore)
                    return false;
                continue;
            }

            if (implementation.Interface.Kind == HandleKind.TypeReference)
            {
                if (ExternalInterfaceReference(reader, (TypeReferenceHandle)implementation.Interface) is not { } baseReference)
                    return false;
                if (!TryCollectExternalInterfaceMethods(
                        assembly,
                        baseReference,
                        resolver,
                        operatorTypeResolver,
                        visited,
                        methods))
                {
                    return false;
                }
                if (methods.Count != methodsBefore)
                    return false;
                continue;
            }

            return false;
        }

        return true;
    }

    static ExternalInterfaceReferenceInfo? ExternalInterfaceReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        bool allowGenericMetadataName = false)
    {
        var typeRef = reader.GetTypeReference(handle);
        if (typeRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return null;

        string metadataFullName = reader.GetFullTypeName(typeRef);
        if (!allowGenericMetadataName
            && metadataFullName.Contains('`', StringComparison.Ordinal))
            return null;

        return new ExternalInterfaceReferenceInfo(
            metadataFullName,
            Clean(metadataFullName),
            AssemblyReferenceIdentity.From(reader, (AssemblyReferenceHandle)typeRef.ResolutionScope));
    }

    // Compatibility declaration text for an explicit-interface method target. The C#
    // printer keeps method-shaped explicit-interface implementations on compatibility
    // text (TryRenderSignatureModel only models plain methods), so RTS supplies the
    // `<return> <IType.Member><type-params>(<params>)` spelling the printer escapes and
    // emits. Parameter names/types mirror the reconstructed body's references.
    static string ExplicitInterfaceMethodDeclarationSignature(
        string explicitInterfaceMemberName,
        CompileBackTypeSignature returnType,
        IReadOnlyList<CompileBackTypeParameter> typeParameters,
        IReadOnlyList<CompileBackParameter> parameters)
    {
        string typeParametersText = typeParameters.Count == 0
            ? ""
            : $"<{string.Join(", ", typeParameters.Select(parameter => parameter.Name))}>";
        string parametersText = string.Join(", ", parameters.Select(RenderParameter));
        return $"{returnType.DisplayName} {explicitInterfaceMemberName}{typeParametersText}({parametersText})";
    }

    // Which interface event an explicit accessor implements comes from the accessor's own
    // canonical declaration identity — its explicit-interface metadata name (`IA.add_E`) —
    // not from MethodImpl table order. One body can carry several `.override` rows, and the
    // first row is simply whichever the producer emitted first: selecting it names the wrong
    // interface in the reconstructed declaration and silently moves the member to a different
    // slot. When the accessor carries no resolvable canonical name, only an unambiguous single
    // row is usable; several rows are declined rather than guessed.
    static ExplicitInterfaceEventInfo? ExplicitInterfaceEvent(
        MetadataReader reader,
        TypeDefinition targetType,
        MethodDefinitionHandle targetAccessor)
    {
        TypeDefinitionHandle? canonicalInterface = null;
        string? canonicalMemberName = null;
        if (TrySplitExplicitInterfaceMetadataName(
                reader.GetString(reader.GetMethodDefinition(targetAccessor).Name),
                out string interfaceMetadataName,
                out string memberMetadataName)
            && TypeProducer.FindType(reader, interfaceMetadataName) is { } canonicalHandle)
        {
            canonicalInterface = canonicalHandle;
            canonicalMemberName = memberMetadataName;
        }

        ExplicitInterfaceEventInfo? fallback = null;
        bool fallbackIsAmbiguous = false;
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
            ExplicitInterfaceEventInfo? candidate = null;
            foreach (var eventHandle in interfaceDef.GetEvents())
            {
                var eventDefinition = reader.GetEventDefinition(eventHandle);
                var accessors = eventDefinition.GetAccessors();
                if (accessors.Adder != declarationHandle && accessors.Remover != declarationHandle)
                    continue;

                var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
                string eventName = Identifier(reader.GetString(eventDefinition.Name));
                candidate = new ExplicitInterfaceEventInfo(
                    interfaceHandle,
                    eventHandle,
                    $"{interfaceIdentity.FullName}.{eventName}",
                    reader.GetString(declaration.Name));
                break;
            }

            if (candidate is null)
                continue;
            if (canonicalInterface is { } expectedInterface)
            {
                if (interfaceHandle == expectedInterface
                    && reader.GetString(declaration.Name) == canonicalMemberName)
                {
                    return candidate;
                }
                continue;
            }
            if (fallback is null)
                fallback = candidate;
            else
                fallbackIsAmbiguous = true;
        }

        return fallbackIsAmbiguous ? null : fallback;
    }

    static (ExternalExplicitInterfaceEventInfo? Event, string? DeclineReason)
        ExternalExplicitInterfaceEvent(
        MetadataReader reader,
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
        TypeDefinition targetType,
        EventDefinition targetEvent,
        MethodDefinitionHandle targetAccessor,
        IReadOnlySet<TypeDefinitionHandle> closureRoots)
    {
        string metadataEventName = reader.GetString(targetEvent.Name);
        if (!TrySplitExplicitInterfaceMetadataName(
                metadataEventName,
                out var interfaceMetadataName,
                out var eventName))
        {
            return (null, null);
        }

        foreach (var implementationHandle in targetType.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody != targetAccessor)
                continue;

            if (implementation.MethodDeclaration.Kind != HandleKind.MemberReference)
                continue;

            var declaration = reader.GetMemberReference(
                (MemberReferenceHandle)implementation.MethodDeclaration);
            if (declaration.GetKind() != MemberReferenceKind.Method
                || declaration.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            if (ExternalInterfaceReference(
                    reader,
                    (TypeReferenceHandle)declaration.Parent) is not { } interfaceReference
                || !string.Equals(
                    interfaceReference.MetadataFullName,
                    interfaceMetadataName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!ExternalInterfaceNameIsRepresentable(interfaceReference.MetadataFullName)
                || !MetadataIdentifierRoundTrips(eventName))
            {
                return Decline("External explicit event identity is not representable in C#.");
            }
            if (ExternalInterfaceSpellingShadowedByClosure(
                    reader,
                    targetType,
                    closureRoots,
                    interfaceReference.MetadataFullName))
            {
                return Decline("External explicit event identity is shadowed by the reconstruction closure.");
            }

            var targetAccessorDefinition = reader.GetMethodDefinition(targetAccessor);
            if (SignatureHasUnrepresentableDetail(reader, targetAccessorDefinition))
                return Decline("External explicit event accessor signature is not representable.");

            MethodSignature<string> targetSignature;
            MethodSignature<string> declarationSignature;
            try
            {
                targetSignature = GuardedSignatureText.MethodText(
                    reader,
                    targetAccessorDefinition,
                    GenericContext.ForMethod(reader, targetType, targetAccessorDefinition));
                declarationSignature = declaration.DecodeMethodSignature(
                    SignatureDecoder.Instance,
                    genericContext: null);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return Decline("External explicit event accessor signature could not be decoded.");
            }

            ReturnToSender.CompilationClosure closure =
                compilationClosure
                ?? ReturnToSender.CreateCompilationClosure(assemblyPath);
            if (ResolveExternalTypeDefinition(
                    closure.TargetAssembly,
                    interfaceReference.AssemblyIdentity,
                    interfaceReference.MetadataFullName,
                    closure.Resolver) is not { } definition)
            {
                return Decline("External explicit event interface was not resolved from the compilation closure.");
            }

            try
            {
                using Stream stream = definition.Assembly.OpenRead();
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                    return Decline("External explicit event interface has no metadata.");
                var externalReader = peReader.GetMetadataReader();
                if (!definition.Address.TryResolve(
                        externalReader,
                        out TypeDefinitionHandle interfaceHandle))
                {
                    return Decline("External explicit event interface identity did not resolve.");
                }

                var interfaceDef = externalReader.GetTypeDefinition(interfaceHandle);
                if ((interfaceDef.Attributes & TypeAttributes.Interface) == 0
                    || interfaceDef.GetGenericParameters().Count != 0
                    || !IsPubliclyAccessible(externalReader, interfaceDef)
                    || AttributeReader.TryGetObsoleteAttribute(
                        externalReader,
                        interfaceDef.GetCustomAttributes(),
                        out _)
                    || AttributeReader.HasAttribute(
                        externalReader,
                        interfaceDef.GetCustomAttributes(),
                        KnownAttributeNames.CompilerFeatureRequiredAttribute)
                    || interfaceDef.GetProperties().Count != 0
                    || interfaceDef.GetInterfaceImplementations().Count != 0)
                {
                    return Decline("External explicit event interface surface is not reconstructable.");
                }

                var matches = new List<EventDefinitionHandle>();
                var targetAccessors = targetEvent.GetAccessors();
                foreach (var eventHandle in interfaceDef.GetEvents())
                {
                    var externalEvent = externalReader.GetEventDefinition(eventHandle);
                    if (externalReader.GetString(externalEvent.Name) != eventName)
                        continue;

                    var accessors = externalEvent.GetAccessors();
                    MethodDefinitionHandle externalAccessor =
                        targetAccessors.Adder == targetAccessor
                            ? accessors.Adder
                            : targetAccessors.Remover == targetAccessor
                                ? accessors.Remover
                                : default;
                    if (externalAccessor.IsNil
                        || externalReader.GetString(
                            externalReader.GetMethodDefinition(externalAccessor).Name)
                            != reader.GetString(declaration.Name))
                    {
                        continue;
                    }

                    var externalAccessorDefinition = externalReader.GetMethodDefinition(externalAccessor);
                    if (externalAccessorDefinition.GetGenericParameters().Count != 0
                        || SignatureHasUnrepresentableDetail(externalReader, externalAccessorDefinition))
                    {
                        return Decline("External explicit event accessor signature is not reconstructable.");
                    }

                    var externalSignature = GuardedSignatureText.MethodText(
                        externalReader,
                        externalAccessorDefinition,
                        GenericContext.ForMethod(
                            externalReader,
                            interfaceDef,
                            externalAccessorDefinition));
                    if (!SameExternalEventAccessorSignature(targetSignature, externalSignature)
                        || !SameExternalEventAccessorSignature(declarationSignature, externalSignature))
                    {
                        return Decline("External explicit event accessor signature does not match its declaration.");
                    }
                    matches.Add(eventHandle);
                }

                if (matches.Count != 1)
                    return Decline("External explicit event declaration is missing or ambiguous.");

                var matchedEvent = externalReader.GetEventDefinition(matches[0]);
                var matchedAccessors = matchedEvent.GetAccessors();
                if (matchedAccessors.Adder.IsNil
                    || matchedAccessors.Remover.IsNil
                    || interfaceDef.GetEvents().Count != 1
                    || interfaceDef.GetMethods().Any(methodHandle =>
                        methodHandle != matchedAccessors.Adder
                        && methodHandle != matchedAccessors.Remover))
                {
                    return Decline("External explicit event interface has unreconstructed required members.");
                }
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                return Decline("External explicit event interface could not be inspected.");
            }

            return (
                new ExternalExplicitInterfaceEventInfo(
                    interfaceReference.DisplayFullName,
                    $"{interfaceReference.DisplayFullName}.{Identifier(eventName)}"),
                null);
        }

        return (null, null);

        (ExternalExplicitInterfaceEventInfo? Event, string? DeclineReason) Decline(string reason)
            => (null, $"{reader.GetFullTypeName(targetType)}::{metadataEventName}: {reason}");
    }

    static bool SameExternalEventAccessorSignature(
        MethodSignature<string> left,
        MethodSignature<string> right)
        => left.Header.IsInstance == right.Header.IsInstance
           && left.ReturnType == right.ReturnType
           && left.ParameterTypes.SequenceEqual(right.ParameterTypes, StringComparer.Ordinal);

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
            requirement => requirement.Type == interfaceIdentity);
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

    internal static CompileBackSourceResult ComposePropertySetter(
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
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
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements,
        CompileBackMemberSurfaceIndex memberSurfaceByDefinitionName,
        IOperatorTypeRelationshipResolver? relationshipResolver = null)
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
                    ? SetterIsInitOnly(reader, targetSetter)
                        ? CompileBackStubBodyKind.AutoPropertyGetInit
                        : CompileBackStubBodyKind.AutoPropertyGetSet
                    : property.GetAccessors().Getter.IsNil
                        ? SetterIsInitOnly(reader, targetSetter)
                            ? CompileBackStubBodyKind.TargetInitBody
                            : CompileBackStubBodyKind.TargetBody
                        : SetterIsInitOnly(reader, targetSetter)
                            ? CompileBackStubBodyKind.TargetInitSetterWithGetter
                            : CompileBackStubBodyKind.TargetSetterWithGetter,
                targetIsAutoProperty ? null : targetBody,
                targetIsAutoProperty
                    ? [
                        new CompileBackFact("metadata", "target-property-setter", reader.GetString(setter.Name)),
                        new CompileBackFact("metadata", "auto-property", propertyName)
                    ]
                    : [new CompileBackFact("metadata", "target-property-setter", reader.GetString(setter.Name))],
                IsVirtual: IsVirtualSlotDeclaration(setter),
                IsOverride: !targetTypeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(setter),
                IsSealed: !targetTypeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(setter)
                    && setter.Attributes.HasFlag(MethodAttributes.Final),
                ExplicitInterfaceMemberName: explicitInterfaceMemberName,
                GetterToken: property.GetAccessors().Getter.IsNil
                    ? null
                    : MetadataTokens.GetToken(property.GetAccessors().Getter),
                SetterToken: MetadataTokens.GetToken(targetSetter),
                MetadataName: metadataPropertyName)
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
                IncludeMemberSurface = targetFacts.Any(fact => fact.Id == "closure-member"),
                IncludeOperatorSurface = targetMembers.Any(member => member.IsOperator),
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

        var production = TypeProducer.Produce(
            reader,
            requirements,
            memberSurfaceByDefinitionName,
            diagnostics,
            relationshipResolver);
        AddImplicitInterfaceTargetDiagnostic(
            diagnostics,
            assemblyPath,
            compilationClosure,
            reader,
            targetTypeDef,
            setter,
            production.Requirements);
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
        return ComposeCompilationUnit(plan);
    }

    internal static CompileBackSourceResult ComposeEventAccessor(
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
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
        CompileBackMemberSurfaceIndex memberSurfaceByDefinitionName,
        string? siblingAccessorBody = null,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected,
        IOperatorTypeRelationshipResolver? relationshipResolver = null)
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
        var sameAssemblyExplicitEvent = ExplicitInterfaceEvent(
            reader,
            targetTypeDef,
            targetAccessor);
        ExternalExplicitInterfaceEventInfo? externalExplicitEvent = null;
        string? externalEventDeclineReason = null;
        if (sameAssemblyExplicitEvent is null)
        {
            (externalExplicitEvent, externalEventDeclineReason) = ExternalExplicitInterfaceEvent(
                reader,
                assemblyPath,
                compilationClosure,
                targetTypeDef,
                eventDefinition,
                targetAccessor,
                closureRoots);
        }

        var diagnostics = new List<CompileBackPlanningDiagnostic>();
        if (externalEventDeclineReason is not null)
        {
            diagnostics.Add(new CompileBackPlanningDiagnostic(
                "type identity",
                "external-explicit-interface-event-not-reconstructed",
                externalEventDeclineReason));
        }
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
                IsVirtual: IsVirtualSlotDeclaration(accessor),
                IsOverride: !targetTypeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(accessor),
                IsSealed: !targetTypeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(accessor)
                    && accessor.Attributes.HasFlag(MethodAttributes.Final),
                RequiresUnsafeModifier: ContainsFixedBufferElementAccess(function),
                ExplicitInterfaceMemberName: sameAssemblyExplicitEvent?.QualifiedName
                    ?? externalExplicitEvent?.QualifiedName,
                SiblingTargetBody: siblingAccessorBody,
                AdderToken: accessors.Adder.IsNil
                    ? null
                    : MetadataTokens.GetToken(accessors.Adder),
                RemoverToken: accessors.Remover.IsNil
                    ? null
                    : MetadataTokens.GetToken(accessors.Remover),
                MetadataName: metadataEventName)
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
                // Mirror the property-getter target path (issue #3000/#3008): when the accessor's
                // closure pulls in same-type members (backing field, sibling accessor, the
                // constructor), emit the full member surface so the event re-declares as a single
                // `event { add remove }` with both real bodies and every sibling declaration is
                // represented — instead of a lone accessor method plus an unrepresented sibling and
                // `.ctor` (issue #3007). The surface enumeration folds the sibling accessor's token
                // into this target event requirement and skips the standalone accessor methods, so
                // there is no CS0082 collision.
                //
                // Gated to Full so the Selected A/B path (which never runs the full-body evidence
                // pass) keeps its pre-existing minimal single-accessor shape for explicit-interface
                // event targets, leaving the corpus baseline unchanged.
                //
                IncludeMemberSurface = bodyPolicy == RoundTripBodyPolicy.Full
                    && targetFacts.Any(fact => fact.Id == "closure-member"),
                IncludeOperatorSurface = targetMembers.Any(member => member.IsOperator),
                ExternalInterfaces = externalExplicitEvent is null
                    ? []
                    : [externalExplicitEvent.InterfaceDisplayName],
            }
        };
        AddClosureTypeRequirements(requirements, reader, targetRoot, closureFacts, closureMemberRequirements);
        foreach (var dependency in closureRoots.OrderBy(handle => MetadataTokens.GetToken(handle)))
        {
            if (dependency != targetRoot)
                AddClosureTypeRequirements(requirements, reader, dependency, closureFacts, closureMemberRequirements);
        }
        AddExplicitInterfaceEventDeclaration(requirements, reader, sameAssemblyExplicitEvent);

        var production = TypeProducer.Produce(
            reader,
            requirements,
            memberSurfaceByDefinitionName,
            diagnostics,
            relationshipResolver);
        AddImplicitInterfaceTargetDiagnostic(
            diagnostics,
            assemblyPath,
            compilationClosure,
            reader,
            targetTypeDef,
            accessor,
            production.Requirements);
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
        return ComposeCompilationUnit(plan);
    }

    internal static CompileBackSourceResult ComposeMethod(
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
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
        CompileBackMemberSurfaceIndex memberSurfaceByDefinitionName,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected,
        string? constructorChain = null,
        IOperatorTypeRelationshipResolver? operatorResolver = null,
        CrossAssemblyTypeResolver? operatorTypeResolver = null,
        bool suppressDestructorSyntax = false)
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

        var targetReturnType = isConstructor
            ? null
            : CompileBackTypeSignature.Display(MethodReturnType(reader, targetTypeDef, method));
        var targetParameters = MethodParameters(reader, method, signature);
        var targetTypeParameters = MethodTypeParameters(reader, method);
        CompileBackMemberKind targetMemberKind =
            MethodKind(reader, method, isConstructor, operatorResolver);
        bool targetHasOperatorIdentity = !isConstructor
            && IsMetadataOperator(reader, method);
        bool targetOperatorIsRepresentable =
            targetMemberKind == CompileBackMemberKind.Operator;
        if (targetHasOperatorIdentity && !targetOperatorIsRepresentable)
        {
            diagnostics.Add(new CompileBackPlanningDiagnostic(
                "member surface",
                "operator-not-representable",
                MethodSignatureText(methodName, signature)));
        }
        bool isFinalizer = !isConstructor
            && memberSurfaceByDefinitionName.TryGetValue(
                TypeProducer.DefinitionName(reader, targetType),
                out var targetSurface)
            && targetSurface.Members.Any(member =>
                member.MetadataToken == MetadataTokens.GetToken(targetMethod)
                && member.IsFinalizer);
        // A class method whose metadata name is an explicit-interface spelling
        // (`IType.Member`) must be reconstructed as an explicit-interface implementation,
        // not a plain method with the dotted name sanitized to `IType_Member`. The latter
        // compiles but carries the wrong metadata name, so the fidelity lookup fails as
        // ContextFail/method-not-found (#3112). Same-assembly interfaces are handled by
        // TypeProducer.FindType; the external path engages only after proving that adding
        // the interface base-list entry cannot create CS0540/CS0535 regressions.
        string? sameAssemblyExplicitInterfaceMemberName = isConstructor
            ? null
            : ExplicitInterfaceMemberName(reader, methodName);
        string? externalInterfaceDeclineReason = null;
        var externalExplicitInterfaceMethod =
            !isConstructor && sameAssemblyExplicitInterfaceMemberName is null
                ? ExternalExplicitInterfaceMethod(
                    reader,
                    assemblyPath,
                    compilationClosure,
                    targetTypeDef,
                    targetMethod,
                    methodName,
                    targetTypeParameters.Count,
                    closureRoots,
                    operatorTypeResolver,
                    out externalInterfaceDeclineReason)
                : null;
        if (externalInterfaceDeclineReason is not null)
        {
            diagnostics.Add(new CompileBackPlanningDiagnostic(
                "type identity",
                "external-interface-base-not-reconstructed",
                externalInterfaceDeclineReason));
        }
        string? explicitInterfaceMemberName =
            sameAssemblyExplicitInterfaceMemberName
            ?? externalExplicitInterfaceMethod?.ExplicitInterfaceMemberName;
        var targetMembers = isConstructor && primaryConstructor is not null
            ? primaryConstructor.FieldInitializers.ToList()
            :
        [
            new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(targetIdentity.FullName, targetMethodName, overload, signatureText),
                targetMemberKind,
                method.Attributes.HasFlag(MethodAttributes.Static),
                targetParameters,
                targetReturnType,
                targetTypeParameters,
                CompileBackStubBodyKind.TargetBody,
                targetBody,
                [new CompileBackFact("metadata", isConstructor ? "target-constructor" : "target-method", reader.GetString(method.Name))],
                isConstructor ? null : MemberAttributes(reader, method.GetCustomAttributes()),
                isConstructor ? null : MethodReturnAttributes(reader, method),
                IsAbstract: !isConstructor && IsAbstractMethod(method),
                IsVirtual: !isConstructor && IsVirtualSlotDeclaration(method),
                IsOverride: !isConstructor
                    && !targetTypeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(method),
                IsSealed: !isConstructor
                    && !targetTypeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(method)
                    && method.Attributes.HasFlag(MethodAttributes.Final),
                IsAsync: !isConstructor
                    && (function.RequiresAsyncBodyModifier
                        || function.IsRuntimeAsync == MetadataFactState.Yes),
                IsOperator: targetOperatorIsRepresentable,
                IsFinalizer: isFinalizer,
                ConstructorInitializer: targetConstructorInitializer,
                ExplicitInterfaceMemberName: explicitInterfaceMemberName,
                RequiresUnsafeModifier: ContainsFixedBufferElementAccess(function),
                MetadataToken: MetadataTokens.GetToken(targetMethod),
                SuppressDestructorSyntax: suppressDestructorSyntax)
        ];
        if (externalExplicitInterfaceMethod is { AdditionalInterfaceMembers.Count: > 0 })
        {
            // Naming a multi-member external interface in the base list requires the
            // reconstructed type to implement its full required surface (CS0535). Add direct
            // implementations here; inherited implementations are attached to their declaring
            // base requirement after the closure types have been collected.
            targetMembers.AddRange(
                externalExplicitInterfaceMethod.AdditionalInterfaceMembers
                    .Where(requirement => requirement.DeclaringType == targetType)
                    .Select(requirement => requirement.Member));
        }
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
                IncludeMemberSurface = bodyPolicy == RoundTripBodyPolicy.Full
                    || includeRecordSurface
                    || targetFacts.Any(fact => fact.Id == "closure-member"),
                IncludeOperatorSurface = targetOperatorIsRepresentable
                    || targetMembers.Any(member => member.IsOperator),
                ExternalInterfaces = externalExplicitInterfaceMethod is null
                    ? []
                    : [externalExplicitInterfaceMethod.InterfaceDisplayName],
            }
        };
        AddClosureTypeRequirements(
            requirements,
            reader,
            targetRoot,
            closureFacts,
            closureMemberRequirements,
            includeFullMemberSurface:
                bodyPolicy == RoundTripBodyPolicy.Full);

        foreach (var dependency in closureRoots.OrderBy(handle => MetadataTokens.GetToken(handle)))
        {
            if (dependency == targetRoot)
                continue;

            AddClosureTypeRequirements(
                requirements,
                reader,
                dependency,
                closureFacts,
                closureMemberRequirements,
                includeFullMemberSurface:
                    bodyPolicy == RoundTripBodyPolicy.Full);
        }
        AddInheritedInterfaceMemberRequirements(
            requirements,
            reader,
            targetType,
            externalExplicitInterfaceMethod,
            closureFacts);

        if (explicitInterfaceMemberName is not null)
        {
            bool explicitInterfaceShapeIsViable = externalExplicitInterfaceMethod is not null
                || AddExplicitInterfaceMethodDeclaration(
                    requirements,
                    reader,
                    targetTypeDef,
                    targetMethod,
                    operatorResolver);
            if (!explicitInterfaceShapeIsViable)
            {
                // The interface member declaration could not be supplied (unsupported
                // interface-member signature, or the interface is not a standalone closure
                // requirement — e.g. a nested interface reached only through its enclosing
                // root). Revert the target to the plain sanitized shape rather than emit
                // `IType.Member()` against an interface that cannot declare it, which would
                // turn a method-not-found ContextFail into a CS0539 RecompileFail.
                // requirements[0].RequiredMembers wraps the still-mutable targetMembers list,
                // so clearing the explicit-interface fields here reverts the rendered shape.
                int targetIndex = targetMembers.FindIndex(member =>
                    member.Kind == CompileBackMemberKind.Method
                    && member.StubBody == CompileBackStubBodyKind.TargetBody
                    && member.ExplicitInterfaceMemberName == explicitInterfaceMemberName);
                if (targetIndex >= 0)
                {
                    targetMembers[targetIndex] = targetMembers[targetIndex] with
                    {
                        ExplicitInterfaceMemberName = null,
                        DeclarationSignature = null,
                    };
                }
            }
        }

        var production = TypeProducer.Produce(
            reader,
            requirements,
            memberSurfaceByDefinitionName,
            diagnostics,
            operatorResolver);
        AddImplicitInterfaceTargetDiagnostic(
            diagnostics,
            assemblyPath,
            compilationClosure,
            reader,
            targetTypeDef,
            method,
            production.Requirements);
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
        return ComposeCompilationUnit(plan);
    }

    static void AddInheritedInterfaceMemberRequirements(
        List<CompileBackTypeRequirement> requirements,
        MetadataReader reader,
        TypeDefinitionHandle targetType,
        ExternalExplicitInterfaceMethodInfo? externalInterface,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts)
    {
        if (externalInterface is null)
            return;

        foreach (var inherited in externalInterface.AdditionalInterfaceMembers.Where(
            requirement => requirement.DeclaringType != targetType))
        {
            var typeDef = reader.GetTypeDefinition(inherited.DeclaringType);
            var identity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            int requirementIndex = requirements.FindIndex(
                requirement => requirement.Type == identity);
            if (requirementIndex < 0)
            {
                var facts = closureFacts.TryGetValue(inherited.DeclaringType, out var foundFacts)
                    ? foundFacts
                    : [new CompileBackFact(
                        "metadata",
                        "inherited-interface-implementation",
                        identity.FullName)];
                requirements.Add(new CompileBackTypeRequirement(
                    identity,
                    ShellKind(reader, typeDef, facts),
                    [inherited.Member],
                    PrimaryConstructor: null,
                    facts));
                continue;
            }

            var requirement = requirements[requirementIndex];
            if (requirement.RequiredMembers.Any(member =>
                SameMemberDeclaration(member, inherited.Member)))
            {
                continue;
            }
            requirements[requirementIndex] = requirement with
            {
                RequiredMembers = requirement.RequiredMembers
                    .Append(inherited.Member)
                    .ToArray(),
            };
        }
    }

    static CompileBackSourceResult ComposeCompilationUnit(CompileBackReconstructionPlan plan)
    {
        const string typeNamePlanningLayer = "type name planning";
        var rendered = new CSharpTypePrinter().PrintBatch(
            plan.PrintRequests,
            new CSharpTypePrintOptions
            {
                IncludeCustomAttributes = true,
                EmitPragmaWarningDisable = true,
                AssemblyAttributes = plan.Module.AssemblyAttributes.Select(attribute => attribute.Text).ToArray(),
                ModuleAttributes = plan.Module.ModuleAttributes.Select(attribute => attribute.Text).ToArray(),
                Usings = plan.Module.Usings,
            });
        var enrichedPlan = plan with
        {
            Diagnostics = plan.Diagnostics
                .Where(diagnostic => diagnostic.Layer != typeNamePlanningLayer)
                .Concat(rendered.Diagnostics
                    .Where(diagnostic => diagnostic.Message.Contains(
                        "conflicts with global type '",
                        StringComparison.Ordinal))
                    .Select(diagnostic => new CompileBackPlanningDiagnostic(
                        typeNamePlanningLayer,
                        "unresolvable namespace root",
                        $"{diagnostic.TypeName}: {diagnostic.Message}")))
                .ToArray()
        };
        return new CompileBackSourceResult(enrichedPlan, rendered.SourceArtifact);
    }

    static CSharpMemberPolicy ToMemberPolicy(
        CompileBackMemberRequirement requirement,
        int primaryConstructorParameterCount)
        => CSharpMemberShellProducer.BuildPolicy(
            ToMemberShellSpec(requirement),
            primaryConstructorParameterCount);

    static CSharpMemberShellSpec ToMemberShellSpec(CompileBackMemberRequirement requirement)
        => new(
            Name: requirement.Identity.Method,
            Kind: requirement.IsOperator
                ? CSharpShellMemberKind.Operator
                : requirement.IsFinalizer
                    ? CSharpShellMemberKind.Finalizer
                : requirement.Kind switch
            {
                CompileBackMemberKind.PropertyGet => CSharpShellMemberKind.PropertyGet,
                CompileBackMemberKind.PropertySet => CSharpShellMemberKind.PropertySet,
                CompileBackMemberKind.EventAdd => CSharpShellMemberKind.EventAdd,
                CompileBackMemberKind.EventRemove => CSharpShellMemberKind.EventRemove,
                CompileBackMemberKind.Constructor => CSharpShellMemberKind.Constructor,
                CompileBackMemberKind.Method => CSharpShellMemberKind.Method,
                CompileBackMemberKind.Operator => CSharpShellMemberKind.Operator,
                CompileBackMemberKind.Field => CSharpShellMemberKind.Field,
                _ => throw new NotSupportedException(
                    $"Unsupported member declaration kind '{requirement.Kind}'."),
            },
            IsStatic: requirement.IsStatic,
            Parameters: requirement.Parameters.Select(ToShellParameter).ToArray(),
            ReturnType: requirement.ReturnType?.DisplayName,
            TypeParameters: requirement.TypeParameters
                .Select(parameter => new CSharpShellTypeParameter(
                    parameter.Name,
                    parameter.Constraints,
                    parameter.StructuredConstraints,
                    parameter.TypeKind))
                .ToArray(),
            BodyKind: requirement.StubBody switch
            {
                CompileBackStubBodyKind.None => CSharpShellBodyKind.None,
                CompileBackStubBodyKind.Throw => CSharpShellBodyKind.Throw,
                CompileBackStubBodyKind.ThrowInit => CSharpShellBodyKind.ThrowInit,
                CompileBackStubBodyKind.ThrowGetSet => CSharpShellBodyKind.ThrowGetSet,
                CompileBackStubBodyKind.ThrowGetInit => CSharpShellBodyKind.ThrowGetInit,
                CompileBackStubBodyKind.TargetBody => CSharpShellBodyKind.TargetBody,
                CompileBackStubBodyKind.TargetGetterWithSetter => CSharpShellBodyKind.TargetGetterWithSetter,
                CompileBackStubBodyKind.TargetGetterWithInitSetter => CSharpShellBodyKind.TargetGetterWithInitSetter,
                CompileBackStubBodyKind.TargetSetterWithGetter => CSharpShellBodyKind.TargetSetterWithGetter,
                CompileBackStubBodyKind.TargetInitSetterWithGetter => CSharpShellBodyKind.TargetInitSetterWithGetter,
                CompileBackStubBodyKind.TargetInitBody => CSharpShellBodyKind.TargetInitBody,
                CompileBackStubBodyKind.TargetEventAccessorWithSibling => CSharpShellBodyKind.TargetEventAccessorWithSibling,
                CompileBackStubBodyKind.AutoProperty => CSharpShellBodyKind.AutoProperty,
                CompileBackStubBodyKind.AutoPropertyGetSet => CSharpShellBodyKind.AutoPropertyGetSet,
                CompileBackStubBodyKind.AutoPropertyGetInit => CSharpShellBodyKind.AutoPropertyGetInit,
                CompileBackStubBodyKind.InitOnlyProperty => CSharpShellBodyKind.InitOnlyProperty,
                CompileBackStubBodyKind.FieldInitializer => CSharpShellBodyKind.FieldInitializer,
                _ => throw new NotSupportedException(
                    $"Unsupported RTS member body shape '{requirement.StubBody}'."),
            },
            Body: requirement.TargetBody,
            Attributes: requirement.Attributes,
            ReturnAttributes: requirement.ReturnAttributes,
            IsAbstract: requirement.IsAbstract,
            IsVirtual: requirement.IsVirtual,
            IsOverride: requirement.IsOverride,
            IsSealed: requirement.IsSealed,
            IsAsync: requirement.IsAsync,
            IsExtension: requirement.IsExtension,
            Accessibility: requirement.Accessibility switch
            {
                CompileBackAccessibility.Public => CSharpShellAccessibility.Public,
                CompileBackAccessibility.Protected => CSharpShellAccessibility.Protected,
                _ => throw new NotSupportedException(
                    $"Unsupported compile-back accessibility '{requirement.Accessibility}'."),
            },
            ConstructorInitializer: requirement.ConstructorInitializer,
            ExplicitInterfaceMemberName: requirement.ExplicitInterfaceMemberName,
            DeclarationSignature: requirement.DeclarationSignature,
            RequiresUnsafeModifier: requirement.RequiresUnsafeModifier,
            SiblingBody: requirement.SiblingTargetBody,
            MetadataToken: requirement.MetadataToken,
            GetterToken: requirement.GetterToken,
            SetterToken: requirement.SetterToken,
            AdderToken: requirement.AdderToken,
            RemoverToken: requirement.RemoverToken,
            SuppressDestructorSyntax: requirement.SuppressDestructorSyntax,
            CSharpOperatorDeclaration: requirement.Kind == CompileBackMemberKind.Operator
                ? requirement.IsOperator
                : null,
            OperatorPairingKey: requirement.OperatorPairingKey,
            HasOperatorPairingKey: requirement.HasOperatorPairingKey,
            MetadataName: requirement.MetadataName);

    static CSharpShellParameter ToShellParameter(CompileBackParameter parameter)
        => new(
            parameter.Name,
            parameter.Type.DisplayName,
            parameter.Modifier,
            parameter.Attributes,
            parameter.HasDefault,
            parameter.DefaultValueText);

    static CompileBackParameter ToCompileBackParameter(ApiParameter parameter)
        => new(
            Identifier(parameter.Name),
            CompileBackTypeSignature.Display(parameter.Type),
            parameter.Modifier,
            parameter.Attributes,
            parameter.HasDefault,
            parameter.DefaultValueText);

    static ApiParameter ToApiParameter(CompileBackParameter parameter)
        => CSharpMemberShellProducer.BuildParameter(ToShellParameter(parameter));

    static IReadOnlyList<CompileBackParameter> ToCompileBackParameters(IEnumerable<ApiParameter> parameters)
        => parameters.Select(ToCompileBackParameter).ToArray();

    static IReadOnlyList<CompileBackTypeParameter> ToCompileBackTypeParameters(IEnumerable<TypeParameter> parameters)
        => parameters
            .Select(parameter => new CompileBackTypeParameter(
                parameter.Name,
                parameter.Constraints,
                parameter.Variance,
                parameter.StructuredConstraints,
                parameter.TypeKind))
            .ToArray();

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

    static bool IsMetadataOperator(MetadataReader reader, MethodDefinition method)
        => ILInspector.Metadata.OperatorMetadata.IsMetadataOperator(reader, method);

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

    static bool IsVirtualSlotDeclaration(MethodDefinition method)
        => method.Attributes.HasFlag(MethodAttributes.Virtual)
            && method.Attributes.HasFlag(MethodAttributes.NewSlot)
            && !method.Attributes.HasFlag(MethodAttributes.Abstract)
            && !method.Attributes.HasFlag(MethodAttributes.Final);

    static bool IsOverrideSlotReuse(MethodDefinition method)
        => method.Attributes.HasFlag(MethodAttributes.Virtual)
            && !method.Attributes.HasFlag(MethodAttributes.NewSlot);

    static void AddImplicitInterfaceTargetDiagnostic(
        List<CompileBackPlanningDiagnostic> diagnostics,
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
        MetadataReader reader,
        TypeDefinition targetType,
        MethodDefinition targetMethod,
        IReadOnlyList<CompileBackTypeRequirement> reconstructedRequirements)
    {
        var omission = ImplicitInterfaceTargetOmission(
            assemblyPath,
            compilationClosure,
            reader,
            targetType,
            targetMethod,
            reconstructedRequirements);
        if (omission == MetadataFactState.No)
            return;

        var targetIdentity = CompileBackTypeIdentity.FromDefinition(reader, targetType);
        AddImplicitInterfaceOmissionDiagnostic(
            diagnostics,
            omission,
            $"{targetIdentity.MetadataFullName}::{reader.GetString(targetMethod.Name)}");
    }

    // Whether an interface the reconstruction omits from the target type's base list
    // declares the target member — i.e. whether the target is an implicit interface
    // implementation whose declaring interface the shell no longer names.
    //
    // The answer is tri-state on purpose. `No` is a proven negative: every omitted
    // interface was read and none declares this member. `Unknown` means the evidence
    // was unavailable — an unresolvable external assembly, an undecodable signature, or
    // an interface reference this planner cannot follow (a nested-scope TypeRef, a
    // TypeSpec rooted in another TypeSpec). Collapsing `Unknown` into `No` is what let a
    // dropped `: IProbe` (or `: IOuter.IInner<int>`) report a clean Exact: the member's
    // final/newslot slot only exists because of an interface the shell never mentions,
    // and nothing said so. Callers must decline on `Unknown` with a distinct reason
    // rather than claim fidelity they cannot prove.
    static MetadataFactState ImplicitInterfaceTargetOmission(
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        IReadOnlyList<CompileBackTypeRequirement> reconstructedRequirements)
    {
        if (ShellKind(reader, typeDef) != CompileBackTypeKind.Class)
            return MetadataFactState.No;

        string name = reader.GetString(method.Name);
        if (name.Contains('.', StringComparison.Ordinal)
            || method.Attributes.HasFlag(MethodAttributes.Static)
            || (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
            || !method.Attributes.HasFlag(MethodAttributes.Virtual)
            || !method.Attributes.HasFlag(MethodAttributes.Final)
            || !method.Attributes.HasFlag(MethodAttributes.NewSlot))
        {
            return MetadataFactState.No;
        }

        MethodSignature<string> targetSignature;
        try
        {
            targetSignature = method.DecodeSignature(
                SignatureDecoder.Instance,
                GenericContext.ForMethod(reader, typeDef, method));
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return MetadataFactState.Unknown;
        }

        var omission = MetadataFactState.No;
        foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
        {
            var interfaceHandle = reader.GetInterfaceImplementation(implementationHandle).Interface;
            var declares = OmittedInterfaceDeclaresTarget(
                assemblyPath,
                compilationClosure,
                reader,
                interfaceHandle,
                method,
                targetSignature);
            if (declares == MetadataFactState.No)
                continue;

            // A same-assembly interface the plan actually reconstructs is not omitted, so
            // it neither proves an omission nor leaves one unproven.
            if (SameAssemblyInterfaceTargetIsReconstructed(
                    reader,
                    interfaceHandle,
                    method,
                    reconstructedRequirements))
            {
                continue;
            }

            omission = CombineInterfaceEvidence(omission, declares);
            if (omission == MetadataFactState.Yes)
                return MetadataFactState.Yes;
        }

        return omission;
    }

    static bool SameAssemblyInterfaceTargetIsReconstructed(
        MetadataReader reader,
        EntityHandle interfaceHandle,
        MethodDefinition targetMethod,
        IReadOnlyList<CompileBackTypeRequirement> reconstructedRequirements)
    {
        if (!TryDecodeMethodSignature(
                reader,
                targetMethod,
                out MethodSignature<TypeRef> targetSignature)
            || !TryFindDeclaringInterfaceMethod(
                reader,
                interfaceHandle,
                targetMethod,
                targetSignature,
                new HashSet<TypeDefinitionHandle>(),
                out var declaringType,
                out var declaringMethod))
        {
            return false;
        }

        var interfaceDef = reader.GetTypeDefinition(declaringType);
        var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
        var declaredMember = InterfaceMemberRequirement(
            reader,
            interfaceDef,
            interfaceIdentity,
            declaringMethod);
        return declaredMember is not null
            && reconstructedRequirements.Any(requirement =>
                requirement.Type == interfaceIdentity
                && requirement.RequiredMembers.Any(member =>
                    SameReconstructedInterfaceMember(member, declaredMember)));
    }

    static bool SameReconstructedInterfaceMember(
        CompileBackMemberRequirement reconstructed,
        CompileBackMemberRequirement declared)
    {
        if (TypeProducer.SameMemberShape(reconstructed, declared))
            return true;

        return declared.Kind switch
        {
            CompileBackMemberKind.Method or CompileBackMemberKind.Operator =>
                declared.MetadataToken is int token
                && reconstructed.MetadataToken == token,
            CompileBackMemberKind.PropertyGet =>
                declared.GetterToken is int token
                && reconstructed.GetterToken == token,
            CompileBackMemberKind.PropertySet =>
                declared.SetterToken is int token
                && reconstructed.SetterToken == token,
            CompileBackMemberKind.EventAdd =>
                declared.AdderToken is int token
                && reconstructed.AdderToken == token,
            CompileBackMemberKind.EventRemove =>
                declared.RemoverToken is int token
                && reconstructed.RemoverToken == token,
            _ => false,
        };
    }

    static bool TryFindDeclaringInterfaceMethod(
        MetadataReader reader,
        EntityHandle interfaceHandle,
        MethodDefinition targetMethod,
        MethodSignature<TypeRef> targetSignature,
        HashSet<TypeDefinitionHandle> visited,
        out TypeDefinitionHandle declaringType,
        out MethodDefinitionHandle declaringMethod)
    {
        declaringType = default;
        declaringMethod = default;
        if (!TryResolveSameAssemblyInterface(
                reader,
                interfaceHandle,
                out var interfaceType,
                out var typeArguments))
        {
            return false;
        }

        return Find(
            interfaceType,
            typeArguments,
            targetMethod,
            targetSignature,
            visited,
            out declaringType,
            out declaringMethod);

        bool Find(
            TypeDefinitionHandle interfaceType,
            ImmutableArray<TypeRef> typeArguments,
            MethodDefinition targetMethod,
            MethodSignature<TypeRef> targetSignature,
            HashSet<TypeDefinitionHandle> visited,
            out TypeDefinitionHandle declaringType,
            out MethodDefinitionHandle declaringMethod)
        {
            declaringType = default;
            declaringMethod = default;
            if (!visited.Add(interfaceType))
                return false;

            var interfaceDef = reader.GetTypeDefinition(interfaceType);
            string targetName = reader.GetString(targetMethod.Name);
            int targetGenericArity = targetMethod.GetGenericParameters().Count;
            foreach (var methodHandle in interfaceDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != targetName
                    || method.GetGenericParameters().Count != targetGenericArity
                    || !TryDecodeMethodSignature(
                        reader,
                        method,
                        out MethodSignature<TypeRef> signature))
                {
                    continue;
                }

                if (!signature.ReturnType
                        .Instantiate(typeArguments, [])
                        .Equals(targetSignature.ReturnType)
                    || !signature.ParameterTypes
                        .Select(parameter => parameter.Instantiate(typeArguments, []))
                        .SequenceEqual(targetSignature.ParameterTypes))
                {
                    continue;
                }

                declaringType = interfaceType;
                declaringMethod = methodHandle;
                return true;
            }

            foreach (var implementationHandle in interfaceDef.GetInterfaceImplementations())
            {
                var inherited = reader.GetInterfaceImplementation(implementationHandle).Interface;
                if (!TryDecodeInterfaceType(reader, inherited, out TypeRef inheritedType)
                    || !TryResolveSameAssemblyInterfaceType(
                        inheritedType.Instantiate(typeArguments, []),
                        out var inheritedInterface,
                        out var inheritedArguments))
                {
                    continue;
                }

                if (Find(
                        inheritedInterface,
                        inheritedArguments,
                        targetMethod,
                        targetSignature,
                        visited,
                        out declaringType,
                        out declaringMethod))
                {
                    return true;
                }
            }

            return false;
        }
    }

    static CompileBackMemberRequirement? InterfaceMemberRequirement(
        MetadataReader reader,
        TypeDefinition interfaceDef,
        CompileBackTypeIdentity interfaceIdentity,
        MethodDefinitionHandle methodHandle)
    {
        foreach (var propertyHandle in interfaceDef.GetProperties())
        {
            var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
            if (accessors.Getter == methodHandle || accessors.Setter == methodHandle)
            {
                return TypeProducer.PropertyRequirement(
                    reader,
                    interfaceDef,
                    interfaceIdentity,
                    propertyHandle,
                    reader.GetString(reader.GetMethodDefinition(methodHandle).Name));
            }
        }

        foreach (var eventHandle in interfaceDef.GetEvents())
        {
            var accessors = reader.GetEventDefinition(eventHandle).GetAccessors();
            if (accessors.Adder == methodHandle || accessors.Remover == methodHandle)
            {
                return TypeProducer.EventRequirement(
                    reader,
                    interfaceDef,
                    interfaceIdentity,
                    eventHandle,
                    reader.GetString(reader.GetMethodDefinition(methodHandle).Name));
            }
        }

        return TypeProducer.MethodRequirement(
            reader,
            interfaceDef,
            interfaceIdentity,
            methodHandle);
    }

    static bool TryDecodeMethodSignature(
        MetadataReader reader,
        MethodDefinition method,
        out MethodSignature<TypeRef> signature)
    {
        signature = default;
        try
        {
            signature = method.DecodeSignature(
                TypeRefDecoder.Instance,
                new GenericScope([], []));
            return !signature.ReturnType.ContainsUnsupported
                && signature.ParameterTypes.All(parameter => !parameter.ContainsUnsupported);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    static bool TryResolveSameAssemblyInterface(
        MetadataReader reader,
        EntityHandle interfaceHandle,
        out TypeDefinitionHandle interfaceType,
        out ImmutableArray<TypeRef> typeArguments)
    {
        interfaceType = default;
        typeArguments = [];
        return TryDecodeInterfaceType(reader, interfaceHandle, out TypeRef decoded)
            && TryResolveSameAssemblyInterfaceType(
                decoded,
                out interfaceType,
                out typeArguments);
    }

    static bool TryDecodeInterfaceType(
        MetadataReader reader,
        EntityHandle interfaceHandle,
        out TypeRef type)
    {
        type = TypeRef.Unsupported("not decoded");
        try
        {
            type = interfaceHandle.Kind switch
            {
                HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(
                    reader,
                    (TypeDefinitionHandle)interfaceHandle,
                    rawTypeKind: 0),
                HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(
                    reader,
                    GenericScope.Empty,
                    (TypeSpecificationHandle)interfaceHandle,
                    rawTypeKind: 0),
                _ => TypeRef.Unsupported("not a same-assembly interface"),
            };
            return !type.ContainsUnsupported;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    static bool TryResolveSameAssemblyInterfaceType(
        TypeRef type,
        out TypeDefinitionHandle interfaceType,
        out ImmutableArray<TypeRef> typeArguments)
    {
        typeArguments = type.Kind == TypeRefKind.GenericInstance
            ? type.TypeArguments
            : [];
        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType!
            : type;
        interfaceType = definition.Kind == TypeRefKind.Definition
            ? definition.DefinitionHandle
            : default;
        return !interfaceType.IsNil;
    }

    // A proven `Yes` wins outright; otherwise one unavailable answer makes the whole
    // question unanswerable. Never let a later `No` overwrite an earlier `Unknown`.
    static MetadataFactState CombineInterfaceEvidence(
        MetadataFactState current,
        MetadataFactState next)
        => (current, next) switch
        {
            (MetadataFactState.Yes, _) or (_, MetadataFactState.Yes) => MetadataFactState.Yes,
            (MetadataFactState.Unknown, _) or (_, MetadataFactState.Unknown) => MetadataFactState.Unknown,
            _ => MetadataFactState.No,
        };

    static void AddImplicitInterfaceOmissionDiagnostic(
        List<CompileBackPlanningDiagnostic> diagnostics,
        MetadataFactState omission,
        string memberIdentity)
    {
        string? reason = omission switch
        {
            MetadataFactState.Yes => "implicit-interface-not-reconstructed",
            MetadataFactState.Unknown => "implicit-interface-evidence-unavailable",
            _ => null,
        };
        if (reason is null)
            return;
        diagnostics.Add(new CompileBackPlanningDiagnostic(
            "type identity",
            reason,
            memberIdentity));
    }

    static MetadataFactState OmittedInterfaceDeclaresTarget(
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
        MetadataReader reader,
        EntityHandle interfaceHandle,
        MethodDefinition targetMethod,
        MethodSignature<string> targetSignature)
    {
        EntityHandle root = interfaceHandle;
        bool genericInstantiation = false;
        if (interfaceHandle.Kind == HandleKind.TypeSpecification)
        {
            if (!TryReadNamedTypeSpecificationRoot(
                    reader,
                    (TypeSpecificationHandle)interfaceHandle,
                    out root,
                    out genericInstantiation))
            {
                return MetadataFactState.Unknown;
            }
        }

        if (root.Kind == HandleKind.TypeDefinition)
        {
            var interfaceDef = reader.GetTypeDefinition((TypeDefinitionHandle)root);
            return InterfaceDeclaresTarget(
                assemblyPath,
                compilationClosure,
                reader,
                interfaceDef,
                targetMethod,
                targetSignature,
                exactSignature: !genericInstantiation);
        }
        // A TypeSpec rooted in another TypeSpec, or a TypeRef this planner cannot turn
        // into an assembly-qualified identity (a nested type's scope is its enclosing
        // TypeRef, not an AssemblyRef), leaves the interface's members unread.
        if (root.Kind != HandleKind.TypeReference
            || ExternalInterfaceReference(
                reader,
                (TypeReferenceHandle)root,
                allowGenericMetadataName: true) is not { } interfaceReference)
        {
            return MetadataFactState.Unknown;
        }

        return ExternalInterfaceDeclaresTarget(
            assemblyPath,
            compilationClosure,
            reader,
            interfaceReference,
            targetMethod,
            targetSignature,
            exactSignature: !genericInstantiation);
    }

    static MetadataFactState InterfaceDeclaresTarget(
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
        MetadataReader reader,
        TypeDefinition interfaceDef,
        MethodDefinition targetMethod,
        MethodSignature<string> targetSignature,
        bool exactSignature)
    {
        var evidence = MetadataFactState.No;
        string targetName = reader.GetString(targetMethod.Name);
        foreach (var methodHandle in interfaceDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != targetName
                || method.GetGenericParameters().Count != targetMethod.GetGenericParameters().Count)
            {
                continue;
            }
            MethodSignature<string> signature;
            try
            {
                signature = method.DecodeSignature(
                    SignatureDecoder.Instance,
                    GenericContext.ForMethod(reader, interfaceDef, method));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                // A same-named, same-arity candidate whose signature will not decode is
                // exactly the case that cannot be dismissed as "does not declare it".
                evidence = MetadataFactState.Unknown;
                continue;
            }
            if (signature.ParameterTypes.Length != targetSignature.ParameterTypes.Length)
                continue;
            if (!exactSignature
                || signature.ReturnType == targetSignature.ReturnType
                    && signature.ParameterTypes.SequenceEqual(
                        targetSignature.ParameterTypes,
                        StringComparer.Ordinal))
            {
                return MetadataFactState.Yes;
            }
        }

        foreach (var implementationHandle in interfaceDef.GetInterfaceImplementations())
        {
            var inherited = reader.GetInterfaceImplementation(implementationHandle).Interface;
            bool inheritedExact = exactSignature;
            if (inherited.Kind == HandleKind.TypeSpecification)
            {
                if (!TryReadNamedTypeSpecificationRoot(
                        reader,
                        (TypeSpecificationHandle)inherited,
                        out inherited,
                        out bool genericInstantiation))
                {
                    evidence = MetadataFactState.Unknown;
                    continue;
                }
                inheritedExact &= !genericInstantiation;
            }
            MetadataFactState inheritedEvidence;
            if (inherited.Kind == HandleKind.TypeDefinition)
            {
                inheritedEvidence = InterfaceDeclaresTarget(
                    assemblyPath,
                    compilationClosure,
                    reader,
                    reader.GetTypeDefinition((TypeDefinitionHandle)inherited),
                    targetMethod,
                    targetSignature,
                    inheritedExact);
            }
            else if (inherited.Kind == HandleKind.TypeReference
                && ExternalInterfaceReference(
                    reader,
                    (TypeReferenceHandle)inherited,
                    allowGenericMetadataName: true) is { } inheritedReference)
            {
                inheritedEvidence = ExternalInterfaceDeclaresTarget(
                    assemblyPath,
                    compilationClosure,
                    reader,
                    inheritedReference,
                    targetMethod,
                    targetSignature,
                    inheritedExact);
            }
            else
            {
                inheritedEvidence = MetadataFactState.Unknown;
            }
            evidence = CombineInterfaceEvidence(evidence, inheritedEvidence);
            if (evidence == MetadataFactState.Yes)
                return MetadataFactState.Yes;
        }
        return evidence;
    }

    static MetadataFactState ExternalInterfaceDeclaresTarget(
        string assemblyPath,
        ReturnToSender.CompilationClosure? compilationClosure,
        MetadataReader targetReader,
        ExternalInterfaceReferenceInfo interfaceReference,
        MethodDefinition targetMethod,
        MethodSignature<string> targetSignature,
        bool exactSignature)
    {
        ReturnToSender.CompilationClosure closure =
            compilationClosure
            ?? ReturnToSender.CreateCompilationClosure(assemblyPath);
        if (ResolveExternalTypeDefinition(
                closure.TargetAssembly,
                interfaceReference.AssemblyIdentity,
                interfaceReference.MetadataFullName,
                closure.Resolver) is not { } definition)
        {
            // The interface exists in metadata but its definition is unavailable, so
            // whether it declares the target member is unknown. Reporting "does not
            // declare it" here is what let a dropped external interface round-trip as
            // Exact when its assembly could not be resolved.
            return MetadataFactState.Unknown;
        }

        try
        {
            return ResolvedExternalInterfaceDeclaresTarget(
                definition,
                closure.Resolver,
                targetReader.GetString(targetMethod.Name),
                targetMethod.GetGenericParameters().Count,
                targetSignature,
                exactSignature,
                []);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return MetadataFactState.Unknown;
        }

        static MetadataFactState ResolvedExternalInterfaceDeclaresTarget(
            (
                ResolvedAssemblyReference Assembly,
                MetadataTypeDefinitionAddress Address) definition,
            AssemblyDependencyResolver resolver,
            string targetName,
            int targetGenericArity,
            MethodSignature<string> targetSignature,
            bool exactSignature,
            HashSet<string> visited)
        {
            using Stream stream = definition.Assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return MetadataFactState.Unknown;
            var reader = peReader.GetMetadataReader();
            if (!definition.Address.TryResolve(
                    reader,
                    out TypeDefinitionHandle interfaceHandle))
            {
                return MetadataFactState.Unknown;
            }
            return ExternalInterfaceDefinitionDeclaresTarget(
                reader,
                definition.Assembly,
                interfaceHandle,
                resolver,
                targetName,
                targetGenericArity,
                targetSignature,
                exactSignature,
                visited);
        }

        static MetadataFactState ExternalInterfaceDefinitionDeclaresTarget(
            MetadataReader reader,
            ResolvedAssemblyReference assembly,
            TypeDefinitionHandle interfaceHandle,
            AssemblyDependencyResolver resolver,
            string targetName,
            int targetGenericArity,
            MethodSignature<string> targetSignature,
            bool exactSignature,
            HashSet<string> visited)
        {
            string visitKey =
                $"{assembly.Identity}|{reader.GetGuid(reader.GetModuleDefinition().Mvid):D}|"
                + $"{MetadataTokens.GetToken(interfaceHandle):X8}";
            // A repeat visit contributed its own answer the first time; treating the
            // cycle stop as `No` keeps it from masking that answer.
            if (!visited.Add(visitKey))
                return MetadataFactState.No;
            var evidence = MetadataFactState.No;
            var interfaceDef = reader.GetTypeDefinition(interfaceHandle);
            foreach (var methodHandle in interfaceDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != targetName
                    || method.GetGenericParameters().Count != targetGenericArity)
                {
                    continue;
                }
                MethodSignature<string> signature;
                try
                {
                    signature = method.DecodeSignature(
                        SignatureDecoder.Instance,
                        GenericContext.ForMethod(reader, interfaceDef, method));
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    evidence = MetadataFactState.Unknown;
                    continue;
                }
                if (signature.ParameterTypes.Length != targetSignature.ParameterTypes.Length)
                    continue;
                if (!exactSignature
                    || signature.ReturnType == targetSignature.ReturnType
                        && signature.ParameterTypes.SequenceEqual(
                            targetSignature.ParameterTypes,
                            StringComparer.Ordinal))
                {
                    return MetadataFactState.Yes;
                }
            }

            foreach (var implementationHandle in interfaceDef.GetInterfaceImplementations())
            {
                EntityHandle inherited = reader.GetInterfaceImplementation(implementationHandle).Interface;
                bool inheritedExact = exactSignature;
                if (inherited.Kind == HandleKind.TypeSpecification)
                {
                    if (!TryReadNamedTypeSpecificationRoot(
                            reader,
                            (TypeSpecificationHandle)inherited,
                            out inherited,
                            out bool genericInstantiation))
                    {
                        evidence = MetadataFactState.Unknown;
                        continue;
                    }
                    inheritedExact &= !genericInstantiation;
                }
                MetadataFactState inheritedEvidence;
                if (inherited.Kind == HandleKind.TypeDefinition)
                {
                    inheritedEvidence = ExternalInterfaceDefinitionDeclaresTarget(
                        reader,
                        assembly,
                        (TypeDefinitionHandle)inherited,
                        resolver,
                        targetName,
                        targetGenericArity,
                        targetSignature,
                        inheritedExact,
                        visited);
                }
                else if (inherited.Kind == HandleKind.TypeReference
                    && ExternalInterfaceReference(
                        reader,
                        (TypeReferenceHandle)inherited,
                        allowGenericMetadataName: true) is { } inheritedReference
                    && ResolveExternalTypeDefinition(
                        assembly,
                        inheritedReference.AssemblyIdentity,
                        inheritedReference.MetadataFullName,
                        resolver) is { } inheritedDefinition)
                {
                    inheritedEvidence = ResolvedExternalInterfaceDeclaresTarget(
                        inheritedDefinition,
                        resolver,
                        targetName,
                        targetGenericArity,
                        targetSignature,
                        inheritedExact,
                        visited);
                }
                else
                {
                    inheritedEvidence = MetadataFactState.Unknown;
                }
                evidence = CombineInterfaceEvidence(evidence, inheritedEvidence);
                if (evidence == MetadataFactState.Yes)
                    return MetadataFactState.Yes;
            }
            return evidence;
        }
    }

    static bool TryReadNamedTypeSpecificationRoot(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        out EntityHandle root,
        out bool genericInstantiation)
    {
        root = default;
        genericInstantiation = false;
        try
        {
            var blob = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
            byte code = blob.ReadByte();
            genericInstantiation = code == 0x15;
            if (genericInstantiation)
                code = blob.ReadByte();
            if (code is not (0x11 or 0x12))
                return false;
            int encoded = blob.ReadCompressedInteger();
            if (encoded < 0)
                return false;
            int row = encoded >> 2;
            root = (encoded & 3) switch
            {
                0 => MetadataTokens.TypeDefinitionHandle(row),
                1 => MetadataTokens.TypeReferenceHandle(row),
                2 => MetadataTokens.TypeSpecificationHandle(row),
                _ => default,
            };
            return !root.IsNil;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

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
        var apiParameter = CSharpMemberShellProducer.BuildParameter(ToShellParameter(parameter));
        var declaration = string.IsNullOrWhiteSpace(apiParameter.Modifier)
            ? $"{apiParameter.Type} {apiParameter.Name}"
            : $"{apiParameter.Modifier} {apiParameter.Type} {apiParameter.Name}";
        if (apiParameter.HasDefault && apiParameter.DefaultValueText is { Length: > 0 })
            declaration = $"{declaration} = {apiParameter.DefaultValueText}";
        return apiParameter.Attributes is { Count: > 0 }
            ? $"[{string.Join(", ", apiParameter.Attributes)}] {declaration}"
            : declaration;
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

    // An init-only setter carries a required custom modifier
    // modreq(System.Runtime.CompilerServices.IsExternalInit) on its return type.
    // Such properties (including record positional properties) must render as a
    // get-only auto-property shell — the init assignment is expressed through the
    // constructor, not a public `set` — so they must not be broadened to
    // `{ get; set; }`.
    static bool SetterIsInitOnly(MetadataReader reader, MethodDefinitionHandle setterHandle)
    {
        if (setterHandle.IsNil)
            return false;
        try
        {
            var setter = reader.GetMethodDefinition(setterHandle);
            return setter.DecodeSignature(InitOnlyModifierDetector.Instance, genericContext: null).ReturnType;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    sealed class InitOnlyModifierDetector : ISignatureTypeProvider<bool, object?>
    {
        public static readonly InitOnlyModifierDetector Instance = new();

        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;

        public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            return IsExternalInit(reader.GetString(type.Name), reader.GetString(type.Namespace));
        }

        public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            return IsExternalInit(reader.GetString(type.Name), reader.GetString(type.Namespace));
        }

        public bool GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => false;

        public bool GetSZArrayType(bool elementType) => elementType;

        public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;

        public bool GetByReferenceType(bool elementType) => elementType;

        public bool GetPointerType(bool elementType) => elementType;

        public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments) => genericType;

        public bool GetGenericMethodParameter(object? context, int index) => false;

        public bool GetGenericTypeParameter(object? context, int index) => false;

        public bool GetFunctionPointerType(MethodSignature<bool> signature) => false;

        public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => (isRequired && modifier) || unmodifiedType;

        public bool GetPinnedType(bool elementType) => elementType;

        static bool IsExternalInit(string name, string ns)
            => name == "IsExternalInit" && ns == "System.Runtime.CompilerServices";
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
        if (!TrySplitExplicitInterfaceMetadataName(
                metadataPropertyName,
                out var interfaceMetadataName,
                out var memberMetadataName))
        {
            return null;
        }
        if (TypeProducer.FindType(reader, interfaceMetadataName) is not { } interfaceHandle)
            return null;
        var interfaceDef = reader.GetTypeDefinition(interfaceHandle);
        if (interfaceDef.GetGenericParameters().Count != 0
            || !IsSupportedClosureRoot(reader, interfaceDef))
        {
            return null;
        }

        string interfaceName = Clean(interfaceMetadataName);
        string memberName = CSharpIdentifier.Sanitize(memberMetadataName);
        return $"{interfaceName}.{memberName}";
    }

    static bool TrySplitExplicitInterfaceMetadataName(
        string metadataMemberName,
        out string interfaceMetadataName,
        out string memberMetadataName)
    {
        int separator = metadataMemberName.LastIndexOf('.');
        if (separator <= 0 || separator == metadataMemberName.Length - 1)
        {
            interfaceMetadataName = "";
            memberMetadataName = "";
            return false;
        }

        interfaceMetadataName = metadataMemberName[..separator];
        memberMetadataName = metadataMemberName[(separator + 1)..];
        return true;
    }

    /// <summary>
    /// The name a member carries as its <see cref="CompileBackMethodIdentity"/>
    /// key. This is model identity, not C# spelling: it is matched against other
    /// requirement/production entries, never emitted as an identifier.
    /// Constructors therefore keep their metadata name <c>.ctor</c> — the
    /// declaring type name is what the emitter spells, selected by
    /// <see cref="CompileBackMemberKind.Constructor"/>. Every other name is a
    /// method name that can reach emitted source, so it goes through the #3129
    /// residual-name sanitizer.
    /// Gate: <c>MemberIdentifierNameTests</c>. That gate is fast on purpose — the
    /// end-to-end <c>ReturnToSenderPrototypeTests</c> coverage is
    /// <c>Speed=Slow</c> and so does not run in the PR test job, which is how
    /// #3251 reached <c>main</c> unnoticed.
    /// </summary>
    internal static string MemberIdentifierName(string metadataName, bool isConstructor)
        => isConstructor ? metadataName : CSharpNaming.SourceMethodName(metadataName);

    static CompileBackMemberKind MethodKind(
        MetadataReader reader,
        MethodDefinition method,
        bool isConstructor,
        IOperatorTypeRelationshipResolver? relationshipResolver = null)
        => isConstructor
            ? CompileBackMemberKind.Constructor
            : ILInspector.Metadata.OperatorMetadata
                .ClassifyCSharpOperatorDeclaration(
                    reader,
                    method,
                    relationshipResolver) switch
                {
                    ILInspector.Metadata.OperatorMetadata.DeclarationClassification.Yes
                        => CompileBackMemberKind.Operator,
                    ILInspector.Metadata.OperatorMetadata.DeclarationClassification.No
                        => CompileBackMemberKind.Method,
                    _ => throw new InvalidOperationException(
                        $"Operator identity for metadata method '{reader.GetString(method.Name)}' could not be resolved."),
                };

    static bool IsOperatorMethod(
        MetadataReader reader,
        MethodDefinition method,
        IOperatorTypeRelationshipResolver? relationshipResolver = null)
        => ILInspector.Metadata.OperatorMetadata.ClassifyCSharpOperatorDeclaration(
            reader,
            method,
            relationshipResolver) switch
        {
            ILInspector.Metadata.OperatorMetadata.DeclarationClassification.Yes
                => true,
            ILInspector.Metadata.OperatorMetadata.DeclarationClassification.No
                => false,
            _ => throw new InvalidOperationException(
                $"Operator identity for metadata method '{reader.GetString(method.Name)}' could not be resolved."),
        };

    sealed class TypeProducer
    {
        public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            MethodRef methodRef,
            IOperatorTypeRelationshipResolver? relationshipResolver = null)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var typeIdentity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            if (TryFindPropertyForAccessor(reader, typeDef, methodRef) is { } propertyHandle)
                return PropertyRequirement(reader, typeDef, typeIdentity, propertyHandle, methodRef.Name);
            if (TryFindMethod(reader, typeDef, methodRef) is { } methodHandle)
                return MethodRequirement(
                    reader,
                    typeDef,
                    typeIdentity,
                    methodHandle,
                    relationshipResolver: relationshipResolver);
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
            CompileBackMemberSurfaceIndex surfaceByDefinitionName,
            List<CompileBackPlanningDiagnostic> diagnostics,
            IOperatorTypeRelationshipResolver? relationshipResolver = null)
        {
            var requests = new List<CSharpTypePrintRequest>();
            var producedRequirements = new List<CompileBackTypeRequirement>();
            var typeDefinitions = new TypeDefinitionIndex(reader);
            var requirementsByIdentity = requirements.ToDictionary(
                requirement => requirement.Type,
                requirement => requirement,
                EqualityComparer<CompileBackTypeIdentity>.Default);
            var emittedRoots = new HashSet<TypeDefinitionHandle>();
            foreach (var requirement in requirements)
            {
                if (typeDefinitions.Find(requirement.Type) is not { } handle)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("type identity", "type-not-found", requirement.Type.MetadataFullName));
                    continue;
                }

                var rootHandle = TopLevelRootOf(reader, handle);
                if (!emittedRoots.Add(rootHandle))
                    continue;

                var rootDef = reader.GetTypeDefinition(rootHandle);
                var rootIdentity = CompileBackTypeIdentity.FromDefinition(reader, rootDef);
                if (!requirementsByIdentity.TryGetValue(rootIdentity, out var rootRequirement))
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
                    requirementsByIdentity,
                    typeDefinitions,
                    surfaceByDefinitionName,
                    producedRequirements,
                    diagnostics,
                    relationshipResolver);
                requests.Add(TypeShellProducer.BuildPrintRequest(reader, rootSpec));
            }

            return new TypeProduction(requests, producedRequirements);
        }

        public sealed record TypeProduction(
            IReadOnlyList<CSharpTypePrintRequest> Requests,
            IReadOnlyList<CompileBackTypeRequirement> Requirements);

        sealed class TypeDefinitionIndex
        {
            readonly Dictionary<CompileBackTypeIdentity, TypeDefinitionHandle> _handles = [];
            readonly HashSet<CompileBackTypeIdentity> _ambiguous = [];

            public TypeDefinitionIndex(MetadataReader reader)
            {
                foreach (var handle in reader.TypeDefinitions)
                {
                    var identity = CompileBackTypeIdentity.FromDefinition(
                        reader,
                        reader.GetTypeDefinition(handle));
                    if (!_handles.TryAdd(identity, handle))
                        _ambiguous.Add(identity);
                }
            }

            public TypeDefinitionHandle? Find(CompileBackTypeIdentity identity)
            {
                if (_ambiguous.Contains(identity))
                {
                    throw new AmbiguousMatchException(
                        $"Product type identity '{identity.MetadataFullName}' matches multiple TypeDef rows.");
                }

                return _handles.TryGetValue(identity, out var handle)
                    ? handle
                    : null;
            }
        }

        internal static MetadataTypeDefinitionName DefinitionName(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            var segments = new List<string>();
            var seen = new HashSet<TypeDefinitionHandle>();
            string? @namespace = null;
            for (int guard = 0;
                !handle.IsNil && guard < MetadataSafetyPolicy.MaxRelationshipNodes;
                guard++)
            {
                if (!seen.Add(handle))
                    throw new InvalidOperationException("Type declaring chain contains a cycle.");

                var type = reader.GetTypeDefinition(handle);
                segments.Add(reader.GetString(type.Name));
                handle = type.GetDeclaringType();
                if (handle.IsNil)
                    @namespace = reader.GetString(type.Namespace);
            }

            if (@namespace is null)
                throw new InvalidOperationException("Type declaring chain exceeded the metadata safety limit.");

            segments.Reverse();
            return MetadataTypeDefinitionName.Create(@namespace, segments.ToImmutableArray())
                is MetadataTypeDefinitionNameResult.Valid valid
                    ? valid.Name
                    : throw new InvalidOperationException("Type has an invalid structured metadata name.");
        }

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
            if (methodRef.GenericParameterCount != methodRef.TypeArguments.Length)
                return false;
            if (method.GetGenericParameters().Count != methodRef.TypeArguments.Length)
                return false;
            try
            {
                var signature = GuardedDecode.MethodSignature(reader, method, IrImporter.CallerScope(reader, typeDef, method));
                var typeArguments = methodRef.DeclaringType.Kind == TypeRefKind.GenericInstance
                    ? methodRef.DeclaringType.TypeArguments
                    : [];
                var expectedReturnType = methodRef.DefinitionReturnType ?? methodRef.ReturnType;
                var candidateReturnType = methodRef.DefinitionReturnType is null
                    ? signature.ReturnType.Instantiate(typeArguments, [])
                    : signature.ReturnType;
                var expectedParameterTypes = methodRef.DefinitionParameterTypes.IsDefaultOrEmpty
                    ? methodRef.ParameterTypes
                    : methodRef.DefinitionParameterTypes;
                var candidateParameterTypes = methodRef.DefinitionParameterTypes.IsDefaultOrEmpty
                    ? signature.ParameterTypes.Select(parameter => parameter.Instantiate(typeArguments, []))
                    : signature.ParameterTypes;
                return candidateReturnType.Equals(expectedReturnType)
                    && signature.ParameterTypes.Length == expectedParameterTypes.Length
                    && candidateParameterTypes.SequenceEqual(expectedParameterTypes);
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
            bool hasGetter = !accessors.Getter.IsNil;
            bool hasSetter = !accessors.Setter.IsNil;
            if (!hasGetter && !hasSetter)
                return null;

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
            var parameters = ToCompileBackParameters(propertyDeclaration.Signature.Parameters);
            bool isAutoProperty = hasGetter
                && IsAutoProperty(reader, typeDef, property, accessors.Getter, returnType.DisplayName);
            bool isInitSetter = hasSetter && SetterIsInitOnly(reader, accessors.Setter);
            bool isAbstractAccessor = !accessor.IsNil && propertyDeclaration.IsAbstract;
            var noBodyProperty = (typeDef.Attributes & TypeAttributes.Interface) != 0 || isAbstractAccessor;
            var stubBody = PropertyStubBody(
                hasGetter,
                hasSetter,
                isInitSetter,
                isAutoProperty,
                noBodyProperty);
            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(
                    typeIdentity.FullName,
                    Identifier(propertyName),
                    0,
                    PropertySignatureText(propertyName, propertyReturnType, parameters)),
                hasGetter ? CompileBackMemberKind.PropertyGet : CompileBackMemberKind.PropertySet,
                isStatic,
                parameters,
                returnType,
                [],
                stubBody,
                null,
                [new CompileBackFact("metadata", factId, accessorName)],
                propertyDeclaration.Attributes,
                propertyDeclaration.Signature.ReturnAttributes,
                IsAbstract: isAbstractAccessor,
                IsVirtual: !accessor.IsNil && IsVirtualSlotDeclaration(accessorMethod),
                IsOverride: !accessor.IsNil
                    && !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && propertyDeclaration.IsOverride,
                IsSealed: !accessor.IsNil
                    && !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && propertyDeclaration.IsSealed,
                ExplicitInterfaceMemberName: explicitInterfaceMemberName,
                GetterToken: accessors.Getter.IsNil
                    ? null
                    : MetadataTokens.GetToken(accessors.Getter),
                SetterToken: accessors.Setter.IsNil
                    ? null
                    : MetadataTokens.GetToken(accessors.Setter),
                MetadataName: propertyName);
        }

        static CompileBackStubBodyKind PropertyStubBody(
            bool hasGetter,
            bool hasSetter,
            bool isInitSetter,
            bool isAutoProperty,
            bool noBodyProperty)
        {
            if (!hasGetter)
            {
                return noBodyProperty
                    ? isInitSetter
                        ? CompileBackStubBodyKind.InitOnlyProperty
                        : CompileBackStubBodyKind.None
                    : isInitSetter
                        ? CompileBackStubBodyKind.ThrowInit
                        : CompileBackStubBodyKind.Throw;
            }
            if (!hasSetter)
            {
                return noBodyProperty
                    ? CompileBackStubBodyKind.None
                    : isAutoProperty
                        ? CompileBackStubBodyKind.AutoProperty
                        : CompileBackStubBodyKind.Throw;
            }
            if (noBodyProperty || isAutoProperty)
            {
                return isInitSetter
                    ? CompileBackStubBodyKind.AutoPropertyGetInit
                    : CompileBackStubBodyKind.AutoPropertyGetSet;
            }
            return isInitSetter
                ? CompileBackStubBodyKind.ThrowGetInit
                : CompileBackStubBodyKind.ThrowGetSet;
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
                IsVirtual: IsVirtualSlotDeclaration(accessor),
                IsOverride: !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(accessor),
                IsSealed: !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && IsOverrideSlotReuse(accessor)
                    && accessor.Attributes.HasFlag(MethodAttributes.Final),
                AdderToken: accessors.Adder.IsNil
                    ? null
                    : MetadataTokens.GetToken(accessors.Adder),
                RemoverToken: accessors.Remover.IsNil
                    ? null
                    : MetadataTokens.GetToken(accessors.Remover),
                MetadataName: reader.GetString(eventDefinition.Name));
        }

        internal static CompileBackMemberRequirement? MethodRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            MethodDefinitionHandle methodHandle,
            string factId = "typed-closure-method",
            IOperatorTypeRelationshipResolver? relationshipResolver = null)
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
            bool hasOperatorIdentity = !isConstructor
                && IsMetadataOperator(reader, method);
            bool operatorIsRepresentable = hasOperatorIdentity
                && IsOperatorMethod(
                    reader,
                    method,
                    relationshipResolver);

            string identifierName = MemberIdentifierName(name, isConstructor);
            IReadOnlyList<CompileBackFact> sourceFacts =
                hasOperatorIdentity && !operatorIsRepresentable
                    ?
                    [
                        new CompileBackFact("metadata", factId, name),
                        new CompileBackFact(
                            "metadata",
                            "operator-raw-method",
                            $"{MethodSignatureText(name, signature)}; C# operator declaration not representable"),
                    ]
                    : [new CompileBackFact(
                        "metadata",
                        isConstructor ? "typed-closure-constructor" : factId,
                        name)];
            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, identifierName, DeclaringOverloadIndex(reader, typeDef, methodHandle, name), MethodSignatureText(identifierName, signature)),
                MethodKind(
                    reader,
                    method,
                    isConstructor,
                    relationshipResolver),
                method.Attributes.HasFlag(MethodAttributes.Static),
                parameters,
                isConstructor ? null : CompileBackTypeSignature.Display(methodReturnType),
                generatedLocalFunction ? [] : ToCompileBackTypeParameters(methodDeclaration!.Signature.TypeParameters),
                (typeDef.Attributes & TypeAttributes.Interface) != 0 || IsAbstractMethod(method)
                    ? CompileBackStubBodyKind.None
                    : CompileBackStubBodyKind.Throw,
                null,
                sourceFacts,
                isConstructor ? null : methodDeclaration?.Attributes,
                isConstructor ? null : methodDeclaration?.Signature.ReturnAttributes,
                IsAbstract: !isConstructor && IsAbstractMethod(method),
                IsVirtual: !isConstructor && IsVirtualSlotDeclaration(method),
                IsOverride: !isConstructor
                    && !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && methodDeclaration?.IsOverride == true,
                IsSealed: !isConstructor
                    && !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                    && methodDeclaration?.IsSealed == true,
                IsExtension: IsExtensionMethod(reader, typeDef, method),
                IsOperator: operatorIsRepresentable,
                MetadataToken: MetadataTokens.GetToken(methodHandle));
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
            IReadOnlyDictionary<CompileBackTypeIdentity, CompileBackTypeRequirement> requirementsByIdentity,
            TypeDefinitionIndex typeDefinitions,
            CompileBackMemberSurfaceIndex surfaceByDefinitionName,
            List<CompileBackTypeRequirement> producedRequirements,
            List<CompileBackPlanningDiagnostic> diagnostics,
            IOperatorTypeRelationshipResolver? relationshipResolver)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var kind = requirement.RequiredKind;
            var members = kind == CompileBackTypeKind.Delegate
                ? [DelegateInvokeRequirement(reader, typeDef, requirement.Type)]
                : RequiredMemberRequirements(requirement);
            foreach (var fact in members
                .SelectMany(member => member.SourceFacts)
                .Where(fact => fact.Id == "operator-raw-method"))
            {
                diagnostics.Add(new CompileBackPlanningDiagnostic(
                    "member surface",
                    "operator-not-representable",
                    fact.Detail));
            }
            bool includeMemberSurface = requirement.IncludeMemberSurface;
            bool includeOperatorSurface = requirement.IncludeOperatorSurface;
            if ((includeMemberSurface || includeOperatorSurface)
                && kind != CompileBackTypeKind.Delegate)
            {
                if (surfaceByDefinitionName.TryGetValue(
                        DefinitionName(reader, handle),
                        out var surface))
                {
                    AddClosureMemberSurface(
                        reader,
                        typeDef,
                        requirement,
                        surface,
                        members,
                        diagnostics,
                        relationshipResolver,
                        operatorOnly: !includeMemberSurface);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Product member surface did not contain required type '{requirement.Type.MetadataFullName}'.");
                }
            }
            if (kind is CompileBackTypeKind.Class or CompileBackTypeKind.Record or CompileBackTypeKind.Struct)
            {
                AddRequiredInterfaceProperties(
                    reader,
                    typeDef,
                    requirement,
                    requirementsByIdentity,
                    surfaceByDefinitionName,
                    members,
                    relationshipResolver);
            }
            NormalizeUnavailableOverrides(
                reader,
                handle,
                requirementsByIdentity,
                members,
                diagnostics);
            bool isReconstructedBase = kind == CompileBackTypeKind.Class
                && IsReconstructedBaseOfAnotherType(
                    reader,
                    requirement,
                    requirementsByIdentity,
                    typeDefinitions);
            if (isReconstructedBase)
                RepairAbstractBaseMembers(members, requirement.Type, diagnostics);

            // When this class is reconstructed as the base of another shell type, a
            // derived stub constructor emits an implicit `: base()`. If the class has
            // only parameterized constructors (no accessible parameterless one), that
            // implicit call fails to bind (CS7036/CS1729). Synthesize a parameterless
            // constructor so base-class reconstruction never breaks the derived shell;
            // at worst the derived constructor stays at its pre-existing opcode diff.
            if (kind == CompileBackTypeKind.Class
                && members.Any(member => member.Kind == CompileBackMemberKind.Constructor)
                && !members.Any(member => member.Kind == CompileBackMemberKind.Constructor && member.Parameters.Count == 0)
                && isReconstructedBase)
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
                InterfaceDisplayNames: InterfaceSignatures(reader, typeDef, requirementsByIdentity)
                    .Select(signature => signature.DisplayName)
                    .Concat(requirement.ExternalInterfaces)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                MemberPolicies: policies,
                PrimaryConstructorParameters: primaryConstructorParameters,
                NestedTypes: NestedSpecs(
                    reader,
                    typeDef,
                    requirementsByIdentity,
                    typeDefinitions,
                    surfaceByDefinitionName,
                    includeMemberSurface,
                    producedRequirements,
                    diagnostics,
                    relationshipResolver));
        }

        static void AddRequiredInterfaceProperties(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeRequirement requirement,
            IReadOnlyDictionary<CompileBackTypeIdentity, CompileBackTypeRequirement> requirementsByIdentity,
            CompileBackMemberSurfaceIndex surfaceByDefinitionName,
            List<CompileBackMemberRequirement> members,
            IOperatorTypeRelationshipResolver? relationshipResolver)
        {
            foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = reader.GetInterfaceImplementation(implementationHandle);
                if (implementation.Interface.Kind != HandleKind.TypeDefinition)
                    continue;

                var interfaceDef = reader.GetTypeDefinition(
                    (TypeDefinitionHandle)implementation.Interface);
                var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
                if (!requirementsByIdentity.TryGetValue(
                        interfaceIdentity,
                        out var interfaceRequirement))
                {
                    continue;
                }

                var interfaceMembers = RequiredMemberRequirements(interfaceRequirement);
                if (interfaceRequirement.IncludeMemberSurface)
                {
                    // The interface's own BuildSpec call reports surface diagnostics.
                    if (!surfaceByDefinitionName.TryGetValue(
                            DefinitionName(
                                reader,
                                (TypeDefinitionHandle)implementation.Interface),
                            out var surface))
                    {
                        throw new InvalidOperationException(
                            $"Product member surface did not contain required interface '{interfaceIdentity.MetadataFullName}'.");
                    }
                    AddClosureMemberSurface(
                        reader,
                        interfaceDef,
                        interfaceRequirement,
                        surface,
                        interfaceMembers,
                        diagnostics: [],
                        relationshipResolver);
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
            int? interfaceAccessorToken = interfaceMember.Kind == CompileBackMemberKind.PropertyGet
                ? interfaceMember.GetterToken
                : interfaceMember.SetterToken;
            var interfaceProperty = interfaceDef.GetProperties().FirstOrDefault(handle =>
            {
                var accessors = reader.GetPropertyDefinition(handle).GetAccessors();
                var accessor = interfaceMember.Kind == CompileBackMemberKind.PropertyGet
                    ? accessors.Getter
                    : accessors.Setter;
                return interfaceAccessorToken is int token
                    ? !accessor.IsNil && MetadataTokens.GetToken(accessor) == token
                    : PropertyMatchesRequirement(
                        reader,
                        interfaceDef,
                        handle,
                        interfaceMember);
            });
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
                return (candidateName == propertyName
                        || candidateName == $"{interfaceName}.{propertyName}")
                    && PropertyMatchesRequirement(
                        reader,
                        typeDef,
                        handle,
                        interfaceMember);
            });
        }

        static bool PropertyMatchesRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            PropertyDefinitionHandle propertyHandle,
            CompileBackMemberRequirement requirement)
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var accessors = property.GetAccessors();
            var accessor = requirement.Kind == CompileBackMemberKind.PropertyGet
                ? accessors.Getter
                : accessors.Setter;
            if (accessor.IsNil
                || reader.GetMethodDefinition(accessor).Attributes.HasFlag(MethodAttributes.Static)
                    != requirement.IsStatic)
            {
                return false;
            }

            try
            {
                var declaration = MetadataDeclarationQuery.GetProperty(reader, typeDef, property);
                return declaration.Signature.ReturnType is { } returnType
                    && CompileBackTypeSignature.Display(returnType) == requirement.ReturnType
                    && SameParameters(
                        ToCompileBackParameters(declaration.Signature.Parameters),
                        requirement.Parameters);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }

        static PropertyDefinitionHandle PropertyForAccessor(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodDefinitionHandle accessor)
        {
            if (accessor.IsNil)
                return default;
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
            IReadOnlyDictionary<CompileBackTypeIdentity, CompileBackTypeRequirement> requirementsByIdentity,
            TypeDefinitionIndex typeDefinitions,
            CompileBackMemberSurfaceIndex surfaceByDefinitionName,
            bool includeMemberSurface,
            List<CompileBackTypeRequirement> producedRequirements,
            List<CompileBackPlanningDiagnostic> diagnostics,
            IOperatorTypeRelationshipResolver? relationshipResolver)
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
                requirementsByIdentity.TryGetValue(identity, out var requirement);
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
                    requirementsByIdentity,
                    typeDefinitions,
                    surfaceByDefinitionName,
                    producedRequirements,
                    diagnostics,
                    relationshipResolver));
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
                        requirementsByIdentity,
                        typeDefinitions,
                        surfaceByDefinitionName,
                        producedRequirements,
                        diagnostics,
                        relationshipResolver));
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
        // requirementsByIdentity, so each requirement's nested tree is walked.
        static bool IsReconstructedBaseOfAnotherType(
            MetadataReader reader,
            CompileBackTypeRequirement requirement,
            IReadOnlyDictionary<CompileBackTypeIdentity, CompileBackTypeRequirement> requirementsByIdentity,
            TypeDefinitionIndex typeDefinitions)
        {
            foreach (var other in requirementsByIdentity.Values)
            {
                if (typeDefinitions.Find(other.Type) is not { } otherHandle)
                    continue;
                if (TypeOrNestedDerivesFrom(reader, otherHandle, requirement.Type))
                    return true;
            }

            return false;
        }

        static bool TypeOrNestedDerivesFrom(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            CompileBackTypeIdentity baseIdentity)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (CompileBackTypeIdentity.FromDefinition(reader, typeDef) != baseIdentity
                && ReconstructedSameAssemblyBaseIdentity(reader, handle, ShellKind(reader, typeDef)) == baseIdentity)
            {
                return true;
            }

            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                if (TypeOrNestedDerivesFrom(reader, nestedHandle, baseIdentity))
                    return true;
            }

            return false;
        }

        // The metadata full name of the class's reconstructed same-assembly base, or
        // null when the base is not reconstructed (external base, or a kind that keeps
        // its compiler-implied base).
        static CompileBackTypeIdentity? ReconstructedSameAssemblyBaseIdentity(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            CompileBackTypeKind kind)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (typeDef.BaseType.Kind != HandleKind.TypeDefinition)
                return null;
            if (TypeShellProducer.ReconstructedBaseTypeDisplay(reader, typeDef, kind == CompileBackTypeKind.Class) is null)
                return null;
            var baseDef = reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType);
            return CompileBackTypeIdentity.FromDefinition(reader, baseDef);
        }

        static void NormalizeUnavailableOverrides(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            IReadOnlyDictionary<CompileBackTypeIdentity, CompileBackTypeRequirement> requirementsByIdentity,
            List<CompileBackMemberRequirement> members,
            List<CompileBackPlanningDiagnostic> diagnostics)
        {
            for (int index = 0; index < members.Count; index++)
            {
                var member = members[index];
                if (!member.IsOverride
                    || OverrideSlotIsAvailable(
                        reader,
                        handle,
                        requirementsByIdentity,
                        member))
                {
                    continue;
                }

                members[index] = member with
                {
                    IsOverride = false,
                    IsSealed = false,
                    IsVirtual = !member.IsStatic
                        && !member.IsAbstract
                        && !reader.GetTypeDefinition(handle).Attributes.HasFlag(TypeAttributes.Sealed),
                    SourceFacts = member.SourceFacts
                        .Append(new CompileBackFact(
                            "synthetic",
                            "override-slot-unavailable",
                            member.Identity.Signature))
                        .ToArray(),
                };
                diagnostics.Add(new CompileBackPlanningDiagnostic(
                    "member surface",
                    "override-slot-unavailable",
                    $"{member.Identity.Type}::{member.Identity.Signature}"));
                if (member.SourceFacts.Any(fact =>
                        fact.Id.StartsWith("target-", StringComparison.Ordinal)))
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "type identity",
                        "target-override-slot-unavailable",
                        $"{member.Identity.Type}::{member.Identity.Signature}"));
                }
            }
        }

        static bool OverrideSlotIsAvailable(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            IReadOnlyDictionary<CompileBackTypeIdentity, CompileBackTypeRequirement> requirementsByIdentity,
            CompileBackMemberRequirement member)
        {
            TypeDefinitionHandle current = handle;
            while (ReconstructedSameAssemblyBaseIdentity(
                    reader,
                    current,
                    ShellKind(reader, reader.GetTypeDefinition(current))) is { } baseIdentity)
            {
                if (!requirementsByIdentity.TryGetValue(baseIdentity, out var baseRequirement))
                    return true;
                var requiredSlot = DeclaredOverrideSlotState(
                    baseRequirement.RequiredMembers,
                    member);
                if (requiredSlot == OverrideSlotState.Usable)
                {
                    return true;
                }
                if (requiredSlot == OverrideSlotState.Unusable)
                    return false;
                if (baseRequirement.IncludeMemberSurface)
                {
                    var surfaceSlot = MemberSurfaceOverrideSlotState(
                        reader,
                        (TypeDefinitionHandle)reader.GetTypeDefinition(current).BaseType,
                        baseRequirement,
                        member);
                    if (surfaceSlot == OverrideSlotState.Usable)
                        return true;
                    if (surfaceSlot == OverrideSlotState.Unusable)
                        return false;
                }

                var baseType = reader.GetTypeDefinition(current).BaseType;
                if (baseType.Kind != HandleKind.TypeDefinition)
                    return false;
                current = (TypeDefinitionHandle)baseType;
            }

            return IsSystemObjectOverride(member);
        }

        enum OverrideSlotState
        {
            Missing,
            Usable,
            Unusable,
        }

        static OverrideSlotState MemberSurfaceOverrideSlotState(
            MetadataReader reader,
            TypeDefinitionHandle baseHandle,
            CompileBackTypeRequirement baseRequirement,
            CompileBackMemberRequirement member)
        {
            var baseDef = reader.GetTypeDefinition(baseHandle);
            var candidates = new List<CompileBackMemberRequirement>();
            if (member.Kind == CompileBackMemberKind.Method)
            {
                foreach (var methodHandle in baseDef.GetMethods())
                {
                    if (MethodRequirement(
                            reader,
                            baseDef,
                            baseRequirement.Type,
                            methodHandle) is { } candidate)
                    {
                        candidates.Add(candidate);
                    }
                }
            }
            else if (member.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet)
            {
                foreach (var propertyHandle in baseDef.GetProperties())
                {
                    var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
                    var accessor = member.Kind == CompileBackMemberKind.PropertyGet
                        ? accessors.Getter
                        : accessors.Setter;
                    if (!accessor.IsNil
                        && PropertyRequirement(
                            reader,
                            baseDef,
                            baseRequirement.Type,
                            propertyHandle,
                            reader.GetString(reader.GetMethodDefinition(accessor).Name)) is { } candidate)
                    {
                        candidates.Add(candidate);
                    }
                }
            }
            else if (member.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove)
            {
                foreach (var eventHandle in baseDef.GetEvents())
                {
                    var accessors = reader.GetEventDefinition(eventHandle).GetAccessors();
                    var accessor = member.Kind == CompileBackMemberKind.EventAdd
                        ? accessors.Adder
                        : accessors.Remover;
                    if (!accessor.IsNil
                        && EventRequirement(
                            reader,
                            baseDef,
                            baseRequirement.Type,
                            eventHandle,
                            reader.GetString(reader.GetMethodDefinition(accessor).Name)) is { } candidate)
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            return DeclaredOverrideSlotState(candidates, member);
        }

        static OverrideSlotState DeclaredOverrideSlotState(
            IEnumerable<CompileBackMemberRequirement> candidates,
            CompileBackMemberRequirement member)
        {
            var matching = candidates.Where(candidate => SameOverrideSlot(candidate, member)).ToArray();
            if (matching.Length == 0)
                return OverrideSlotState.Missing;
            return matching.Any(candidate =>
                candidate.IsVirtual || candidate.IsAbstract || candidate.IsOverride)
                    ? OverrideSlotState.Usable
                    : OverrideSlotState.Unusable;
        }

        static bool SameOverrideSlot(
            CompileBackMemberRequirement left,
            CompileBackMemberRequirement right)
        {
            bool sameKind = left.Kind == right.Kind
                || left.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet
                    && right.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet
                || left.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove
                    && right.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove;
            return sameKind
                && !left.IsStatic
                && !right.IsStatic
                && left.Identity.Method == right.Identity.Method
                && left.TypeParameters.Count == right.TypeParameters.Count
                && SameOverrideSlotParameters(left, right);
        }

        static bool SameOverrideSlotParameters(
            CompileBackMemberRequirement left,
            CompileBackMemberRequirement right)
        {
            if (left.Parameters.Count != right.Parameters.Count)
                return false;

            var leftPositions = GenericParameterPositions(left.TypeParameters);
            var rightPositions = GenericParameterPositions(right.TypeParameters);
            if (leftPositions is null || rightPositions is null)
                return false;

            return left.Parameters.Zip(right.Parameters).All(pair =>
                string.Equals(pair.First.Modifier, pair.Second.Modifier, StringComparison.Ordinal)
                && string.Equals(
                    NormalizeGenericParameterPositions(
                        pair.First.Type.DisplayName,
                        leftPositions),
                    NormalizeGenericParameterPositions(
                        pair.Second.Type.DisplayName,
                        rightPositions),
                    StringComparison.Ordinal));
        }

        static Dictionary<string, int>? GenericParameterPositions(
            IReadOnlyList<CompileBackTypeParameter> parameters)
        {
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < parameters.Count; index++)
            {
                if (!positions.TryAdd(parameters[index].Name, index))
                    return null;
            }
            return positions;
        }

        static string NormalizeGenericParameterPositions(
            string type,
            IReadOnlyDictionary<string, int> positions)
        {
            if (positions.Count == 0)
                return type;

            var normalized = new StringBuilder(type.Length);
            for (int index = 0; index < type.Length;)
            {
                int start = index;
                if (type[index] == '@')
                    index++;
                if (index < type.Length
                    && (type[index] == '_' || char.IsLetter(type[index])))
                {
                    index++;
                    while (index < type.Length
                        && (type[index] == '_' || char.IsLetterOrDigit(type[index])))
                    {
                        index++;
                    }

                    string identifier = type[start..index];
                    if (positions.TryGetValue(identifier, out int position))
                    {
                        normalized.Append('!').Append(position);
                        continue;
                    }

                    normalized.Append(identifier);
                    continue;
                }

                normalized.Append(type[start]);
                index = start + 1;
            }

            return normalized.ToString();
        }

        static bool IsSystemObjectOverride(CompileBackMemberRequirement member)
        {
            if (member.IsFinalizer)
                return true;
            if (member.Kind != CompileBackMemberKind.Method
                || member.IsStatic
                || member.TypeParameters.Count != 0)
            {
                return false;
            }

            return (member.Identity.Method is "ToString" or "GetHashCode"
                    && member.Parameters.Count == 0)
                || (member.Identity.Method == "Equals"
                    && member.Parameters.Count == 1
                    && member.Parameters[0].Type.DisplayName is "object" or "System.Object");
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

        static void RepairAbstractBaseMembers(
            List<CompileBackMemberRequirement> members,
            CompileBackTypeIdentity typeIdentity,
            List<CompileBackPlanningDiagnostic> diagnostics)
        {
            for (int index = 0; index < members.Count; index++)
            {
                var member = members[index];
                if (!member.IsAbstract
                    || member.IsStatic
                    || member.Kind is not (
                        CompileBackMemberKind.Method
                        or CompileBackMemberKind.PropertyGet
                        or CompileBackMemberKind.PropertySet
                        or CompileBackMemberKind.EventAdd
                        or CompileBackMemberKind.EventRemove))
                {
                    continue;
                }

                members[index] = member with
                {
                    StubBody = ConcreteAbstractMemberBody(member),
                    IsAbstract = false,
                    IsVirtual = !member.IsOverride,
                    IsSealed = false,
                    SourceFacts = member.SourceFacts
                        .Append(new CompileBackFact(
                            "synthetic",
                            "abstract-base-member-body",
                            member.Identity.Signature))
                        .ToArray(),
                };
                diagnostics.Add(new CompileBackPlanningDiagnostic(
                    "member surface",
                    "abstract-base-member-stubbed",
                    $"{typeIdentity.MetadataFullName}::{member.Identity.Method} ({member.Identity.Signature})"));
            }
        }

        static CompileBackStubBodyKind ConcreteAbstractMemberBody(
            CompileBackMemberRequirement member)
        {
            if (member.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove)
                return CompileBackStubBodyKind.Throw;
            if (member.Kind is not (CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet))
                return CompileBackStubBodyKind.Throw;

            bool hasGetter = member.GetterToken is not null;
            bool hasSetter = member.SetterToken is not null;
            bool isInitSetter = member.StubBody
                is CompileBackStubBodyKind.AutoPropertyGetInit
                or CompileBackStubBodyKind.InitOnlyProperty;
            if (hasGetter && hasSetter)
            {
                return isInitSetter
                    ? CompileBackStubBodyKind.ThrowGetInit
                    : CompileBackStubBodyKind.ThrowGetSet;
            }
            return isInitSetter
                ? CompileBackStubBodyKind.ThrowInit
                : CompileBackStubBodyKind.Throw;
        }

        static IReadOnlyList<CompileBackTypeSignature> InterfaceSignatures(
            MetadataReader reader,
            TypeDefinition typeDef,
            IReadOnlyDictionary<CompileBackTypeIdentity, CompileBackTypeRequirement> requirementsByIdentity)
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

                var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
                // Naming a base-list interface that this compile-back unit never
                // declares is worse than omitting it: metadata can carry two
                // same-named interfaces of different arity in one namespace (a
                // non-generic `IPropertyValidator` alongside `IPropertyValidator<T,
                // TProperty>`, as in FluentValidation), and closure discovery may
                // queue only one of them as an actual requirement. Referencing the
                // undeclared one by its bare display name lets Roslyn resolve it to
                // the *other*, wrong-arity type in scope (CS0305). Only name an
                // interface here when it is already a known requirement — i.e. it
                // will actually be declared somewhere in the composed unit — mirroring
                // the same guard `AddRequiredInterfaceProperties` uses.
                if (!requirementsByIdentity.ContainsKey(interfaceIdentity))
                    continue;

                interfaces.Add(CompileBackTypeSignature.Definition(interfaceIdentity));
            }

            return interfaces;
        }

        static void AddClosureMemberSurface(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeRequirement requirement,
            ApiType surface,
            List<CompileBackMemberRequirement> members,
            List<CompileBackPlanningDiagnostic> diagnostics,
            IOperatorTypeRelationshipResolver? relationshipResolver,
            bool allowUnsafeSurface = false,
            bool operatorOnly = false)
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
                || requirement.IncludeMemberSurface
                || requirement.IncludeOperatorSurface;
            var surfaceAccessorTokens = surface.Members
                .SelectMany(member => new[]
                {
                    member.GetterToken,
                    member.SetterToken,
                    member.AdderToken,
                    member.RemoverToken,
                })
                .Where(static token => token.HasValue)
                .Select(static token => token.GetValueOrDefault())
                .ToHashSet();
            var typeContext = GenericContext.ForType(reader, typeDef);
            // Product surface extraction owns member discovery, accessor ownership,
            // and backing-field exclusion. RTS retains only reconstruction policy:
            // representability, unsafe gating, deduplication, and stub/full bodies.
            foreach (var surfaceField in surface.Members.Where(member =>
                !operatorOnly && member.Kind == "field"))
            {
                if (FindField(reader, typeDef, surfaceField.Name) is not { } fieldHandle)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "field-token-unresolved",
                        $"{requirement.Type.MetadataFullName}::{surfaceField.Name}"));
                    continue;
                }
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

            foreach (var surfaceProperty in surface.Members.Where(member =>
                !operatorOnly && member.Kind == "property"))
            {
                var propertyHandle = PropertyForAccessor(
                    reader,
                    typeDef,
                    SurfaceMethodHandle(surfaceProperty.GetterToken ?? surfaceProperty.SetterToken));
                if (propertyHandle.IsNil)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "property-accessor-token-unresolved",
                        $"{requirement.Type.MetadataFullName}::{surfaceProperty.Name}"));
                    continue;
                }
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();

                string propertyName = reader.GetString(property.Name);
                if (propertyName.Contains('<', StringComparison.Ordinal)
                    || propertyName.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }

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

                if (propertyDeclaration.Signature.ReturnType is not { } propertyReturnType
                    || IsUnsupportedSurfaceSignature(propertyReturnType)
                    || propertyDeclaration.Signature.Parameters.Any(parameter =>
                        IsUnsupportedSurfaceSignature(parameter.Type))
                    || (!allowUnsafeSurface && IsPointerSignature(propertyReturnType)))
                    continue;

                var accessor = accessors.Getter.IsNil ? accessors.Setter : accessors.Getter;
                bool hasGetter = !accessors.Getter.IsNil;
                bool hasSetter = !accessors.Setter.IsNil;
                if (!hasGetter && !hasSetter)
                    continue;

                var accessorMethod = accessor.IsNil ? default : reader.GetMethodDefinition(accessor);
                bool isStatic = !accessor.IsNil && accessorMethod.Attributes.HasFlag(MethodAttributes.Static);
                if (requirement.RequiredKind == CompileBackTypeKind.Interface && isStatic)
                    continue;
                var returnType = CompileBackTypeSignature.Display(propertyReturnType);
                var propertyParameters = ToCompileBackParameters(
                    propertyDeclaration.Signature.Parameters);
                var existingPropertyIndexes = members
                    .Select((member, index) => (member, index))
                    .Where(candidate =>
                        candidate.member.Kind is CompileBackMemberKind.PropertyGet
                            or CompileBackMemberKind.PropertySet
                        && candidate.member.Identity.Method == Identifier(propertyName)
                        && candidate.member.IsStatic == isStatic
                        && candidate.member.ReturnType?.DisplayName == returnType.DisplayName
                        && SameParameters(
                            candidate.member.Parameters,
                            propertyParameters))
                    .Select(candidate => candidate.index)
                    .ToArray();
                if (existingPropertyIndexes.Length == 1)
                {
                    int existingPropertyIndex = existingPropertyIndexes[0];
                    var existing = members[existingPropertyIndex];
                    members[existingPropertyIndex] = existing with
                    {
                        GetterToken = existing.GetterToken ?? surfaceProperty.GetterToken,
                        SetterToken = existing.SetterToken ?? surfaceProperty.SetterToken,
                    };
                    continue;
                }
                if (existingPropertyIndexes.Length > 1)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "property-token-match-ambiguous",
                        $"{requirement.Type.MetadataFullName}::{propertyName}"));
                    continue;
                }
                if (members.Any(member =>
                    member.Kind == CompileBackMemberKind.Field
                    && member.Identity.Method == Identifier(propertyName)))
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "property-conflicts-with-required-field",
                        $"{requirement.Type.MetadataFullName}::{propertyName}"));
                    continue;
                }

                bool isAutoProperty = hasGetter
                    && IsAutoProperty(reader, typeDef, property, accessors.Getter, returnType.DisplayName);
                bool isInitSetter = hasSetter && SetterIsInitOnly(reader, accessors.Setter);
                bool isAbstractAccessor = !accessor.IsNil && propertyDeclaration.IsAbstract;
                var noBodyProperty = requirement.RequiredKind == CompileBackTypeKind.Interface || isAbstractAccessor;
                var stubBody = PropertyStubBody(
                    hasGetter,
                    hasSetter,
                    isInitSetter,
                    isAutoProperty,
                    noBodyProperty);
                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(
                        requirement.Type.FullName,
                        Identifier(propertyName),
                        0,
                        PropertySignatureText(
                            propertyName,
                            propertyReturnType,
                            propertyParameters)),
                    hasGetter ? CompileBackMemberKind.PropertyGet : CompileBackMemberKind.PropertySet,
                    IsStatic: isStatic,
                    Parameters: propertyParameters,
                    ReturnType: returnType,
                    TypeParameters: [],
                    StubBody: stubBody,
                    TargetBody: null,
                    [new CompileBackFact("metadata", "closure-property", propertyName)],
                    propertyDeclaration.Attributes,
                    propertyDeclaration.Signature.ReturnAttributes,
                    IsAbstract: isAbstractAccessor,
                    IsVirtual: !accessor.IsNil && IsVirtualSlotDeclaration(accessorMethod),
                    IsOverride: !accessor.IsNil
                        && !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                        && propertyDeclaration.IsOverride,
                    IsSealed: !accessor.IsNil
                        && !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                        && propertyDeclaration.IsSealed,
                    Accessibility: accessor.IsNil
                        ? CompileBackAccessibility.Public
                        : MethodAccessibility(accessorMethod),
                    GetterToken: surfaceProperty.GetterToken,
                    SetterToken: surfaceProperty.SetterToken,
                    MetadataName: propertyName));
            }

            foreach (var surfaceEvent in surface.Members.Where(member =>
                !operatorOnly && member.Kind == "event"))
            {
                var eventHandle = EventForAccessor(
                    reader,
                    typeDef,
                    SurfaceMethodHandle(surfaceEvent.AdderToken ?? surfaceEvent.RemoverToken));
                if (eventHandle.IsNil)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "event-accessor-token-unresolved",
                        $"{requirement.Type.MetadataFullName}::{surfaceEvent.Name}"));
                    continue;
                }
                var eventDefinition = reader.GetEventDefinition(eventHandle);
                var accessors = eventDefinition.GetAccessors();

                string eventName = reader.GetString(eventDefinition.Name);
                if (eventName.Contains('<', StringComparison.Ordinal))
                    continue;
                int existingEventIndex = members.FindIndex(member =>
                    member.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove
                    && member.MetadataName == eventName);
                if (existingEventIndex < 0)
                {
                    existingEventIndex = members.FindIndex(member =>
                        member.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove
                        && member.MetadataName is null
                        && member.Identity.Method == Identifier(eventName));
                }
                int? adderToken = surfaceEvent.AdderToken;
                int? removerToken = surfaceEvent.RemoverToken;
                if (existingEventIndex >= 0)
                {
                    var existing = members[existingEventIndex];
                    members[existingEventIndex] = existing with
                    {
                        AdderToken = existing.AdderToken ?? adderToken,
                        RemoverToken = existing.RemoverToken ?? removerToken,
                    };
                    continue;
                }

                var representative = !accessors.Adder.IsNil ? accessors.Adder : accessors.Remover;
                if (representative.IsNil)
                    continue;
                string accessorName = reader.GetString(reader.GetMethodDefinition(representative).Name);
                var eventRequirement = EventRequirement(
                    reader,
                    typeDef,
                    requirement.Type,
                    eventHandle,
                    accessorName,
                    "closure-event");
                if (eventRequirement is null)
                    continue;
                var explicitEvent = eventName.Contains('.', StringComparison.Ordinal)
                    ? ExplicitInterfaceEvent(reader, typeDef, representative)
                    : null;
                members.Add(eventRequirement with
                {
                    AdderToken = adderToken,
                    RemoverToken = removerToken,
                    ExplicitInterfaceMemberName = explicitEvent?.QualifiedName,
                    MetadataName = eventName,
                });
            }

            int overload = 0;
            foreach (var surfaceMethod in surface.Members.Where(member =>
                member.MetadataToken is not null
                && !surfaceAccessorTokens.Contains(member.MetadataToken.Value)
                && member.Kind != "extension-method"))
            {
                if (operatorOnly && surfaceMethod.CSharpOperatorDeclaration != true)
                {
                    // An operator-only surface keeps proven operators and drops everything
                    // else. An unclassifiable operator is neither, so record that it was
                    // dropped without evidence instead of dropping it silently.
                    if (surfaceMethod.HasCSharpOperatorDeclarationClassification
                        && surfaceMethod.CSharpOperatorDeclaration is null)
                    {
                        diagnostics.Add(new CompileBackPlanningDiagnostic(
                            "member surface",
                            "operator-representability-unknown",
                            $"{requirement.Type.MetadataFullName}::{surfaceMethod.Name}; "
                                + "C# operator classification unavailable; omitted from operator surface"));
                    }
                    continue;
                }
                var methodHandle = SurfaceMethodHandle(surfaceMethod.MetadataToken);
                if (methodHandle.IsNil)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "method-token-invalid",
                        $"{requirement.Type.MetadataFullName}::{surfaceMethod.Name} ({surfaceMethod.MetadataToken:X8})"));
                    continue;
                }
                var method = reader.GetMethodDefinition(methodHandle);
                if (CompileBackTypeIdentity.FromDefinition(
                        reader,
                        reader.GetTypeDefinition(method.GetDeclaringType()))
                    != requirement.Type)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "method-token-owner-mismatch",
                        $"{requirement.Type.MetadataFullName}::{surfaceMethod.Name} ({surfaceMethod.MetadataToken:X8})"));
                    continue;
                }
                string name = reader.GetString(method.Name);
                if (name == ".cctor"
                    || (name.Contains('<', StringComparison.Ordinal)
                        && CSharpNaming.MethodName(name) == name)
                    || (name != ".ctor" && name.Contains('.', StringComparison.Ordinal)))
                {
                    continue;
                }

                bool isConstructor = name == ".ctor";
                string identifierName = MemberIdentifierName(name, isConstructor);
                if (requirement.RequiredKind == CompileBackTypeKind.Interface && method.Attributes.HasFlag(MethodAttributes.Static))
                    continue;
                var generatedLocalFunction = IsGeneratedLocalFunctionName(name);
                int typeParameterCount = method.GetGenericParameters().Count;
                if (generatedLocalFunction && typeParameterCount != 0)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "generic-local-function-skipped",
                        name));
                    continue;
                }
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
                string signatureIdentity = MethodSignatureText(identifierName, signature);
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
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "method-signature-not-representable",
                        signatureIdentity));
                    continue;
                }
                bool methodIsStatic = method.Attributes.HasFlag(MethodAttributes.Static);
                bool methodHasOperatorIdentity = !isConstructor && IsMetadataOperator(reader, method);
                bool methodIsOperator = methodHasOperatorIdentity
                    && surfaceMethod.CSharpOperatorDeclaration == true;
                if (methodHasOperatorIdentity && !methodIsOperator)
                {
                    // A null classification is unavailable evidence, not a proven negative:
                    // the surface classifies through a cross-assembly relationship resolver,
                    // so an operator whose signature names a type that cannot be resolved is
                    // unclassifiable. Both cases still emit the raw method, but reporting the
                    // unproven one as "not representable" claims a fact nothing established.
                    bool classificationUnavailable =
                        surfaceMethod.CSharpOperatorDeclaration is null;
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        classificationUnavailable
                            ? "operator-representability-unknown"
                            : "operator-not-representable",
                        classificationUnavailable
                            ? $"{signatureIdentity}; C# operator classification unavailable; emitted as raw method"
                            : $"{signatureIdentity}; emitted as raw method"));
                }
                var memberKind = isConstructor
                    ? CompileBackMemberKind.Constructor
                    : methodIsOperator
                        ? CompileBackMemberKind.Operator
                        : CompileBackMemberKind.Method;
                string? returnTypeIdentity = isConstructor
                    ? null
                    : CompileBackTypeSignature.Display(methodReturnType).DisplayName;
                var parameterIdentity = parameters
                    .Select(parameter => (parameter.Type.DisplayName, parameter.Modifier))
                    .ToArray();
                var tokenMatchedMethodIndexes = members
                    .Select((member, index) => (member, index))
                    .Where(candidate =>
                        surfaceMethod.MetadataToken is int token
                        && candidate.member.MetadataToken == token)
                    .Select(candidate => candidate.index)
                    .ToArray();
                var existingMethodIndexes = tokenMatchedMethodIndexes.Length != 0
                    ? tokenMatchedMethodIndexes
                    : members
                    .Select((member, index) => (member, index))
                    .Where(candidate =>
                        candidate.member.Kind == memberKind
                        && candidate.member.Identity.Method == identifierName
                        && candidate.member.ExplicitInterfaceMemberName is null
                        && candidate.member.IsStatic == methodIsStatic
                        && candidate.member.IsOperator == methodIsOperator
                        && candidate.member.TypeParameters.Count == typeParameterCount
                        && candidate.member.ReturnType?.DisplayName == returnTypeIdentity
                        && candidate.member.Parameters
                            .Select(parameter => (parameter.Type.DisplayName, parameter.Modifier))
                            .SequenceEqual(parameterIdentity))
                    .Select(candidate => candidate.index)
                    .ToArray();
                if (existingMethodIndexes.Length == 1)
                {
                    int existingMethodIndex = existingMethodIndexes[0];
                    var existing = members[existingMethodIndex];
                    members[existingMethodIndex] = existing with
                    {
                        MetadataToken = existing.MetadataToken ?? surfaceMethod.MetadataToken,
                        OperatorPairingKey = surfaceMethod.OperatorPairingKey,
                        HasOperatorPairingKey = surfaceMethod.HasOperatorPairingKey,
                    };
                    continue;
                }
                if (existingMethodIndexes.Length > 1)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic(
                        "member surface",
                        "method-token-match-ambiguous",
                        signatureIdentity));
                    continue;
                }
                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(requirement.Type.FullName, identifierName, overload++, signatureIdentity),
                    memberKind,
                    IsStatic: methodIsStatic,
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
                    IsVirtual: !isConstructor && IsVirtualSlotDeclaration(method),
                    IsOverride: !isConstructor
                        && !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                        && methodDeclaration?.IsOverride == true,
                    IsSealed: !isConstructor
                        && !typeDef.Attributes.HasFlag(TypeAttributes.Interface)
                        && methodDeclaration?.IsSealed == true,
                    IsExtension: surfaceMethod.IsExtension,
                    IsOperator: methodIsOperator,
                    IsFinalizer: surfaceMethod.IsFinalizer,
                    Accessibility: MethodAccessibility(method),
                    MetadataToken: surfaceMethod.MetadataToken,
                    OperatorPairingKey: surfaceMethod.OperatorPairingKey,
                    HasOperatorPairingKey: surfaceMethod.HasOperatorPairingKey));
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

        static MethodDefinitionHandle SurfaceMethodHandle(int? token)
        {
            if (token is null)
                return default;
            try
            {
                var handle = MetadataTokens.EntityHandle(token.Value);
                return handle.Kind == HandleKind.MethodDefinition
                    ? (MethodDefinitionHandle)handle
                    : default;
            }
            catch (ArgumentException)
            {
                return default;
            }
        }

        static EventDefinitionHandle EventForAccessor(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodDefinitionHandle accessor)
        {
            if (accessor.IsNil)
                return default;
            foreach (var eventHandle in typeDef.GetEvents())
            {
                var accessors = reader.GetEventDefinition(eventHandle).GetAccessors();
                if (accessors.Adder == accessor || accessors.Remover == accessor)
                    return eventHandle;
            }
            return default;
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

        static string PropertySignatureText(
            string name,
            string returnType,
            IReadOnlyList<CompileBackParameter> parameters)
            => $"{returnType} {name}[{string.Join(", ", parameters.Select(ParameterSignatureText))}]";

        static string ParameterSignatureText(CompileBackParameter parameter)
            => parameter.Modifier is { Length: > 0 } modifier
                ? $"{modifier} {parameter.Type.DisplayName}"
                : parameter.Type.DisplayName;

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

        static TypeDefinitionHandle? FindType(
            MetadataReader reader,
            CompileBackTypeIdentity identity)
        {
            TypeDefinitionHandle? match = null;
            foreach (var handle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(handle);
                if (CompileBackTypeIdentity.FromDefinition(reader, typeDef) == identity)
                {
                    if (match is not null)
                    {
                        throw new AmbiguousMatchException(
                            $"Product type identity '{identity.MetadataFullName}' matches multiple TypeDef rows.");
                    }

                    match = handle;
                }
            }

            return match;
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
