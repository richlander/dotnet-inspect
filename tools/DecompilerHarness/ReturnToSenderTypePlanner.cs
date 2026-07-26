using System.Globalization;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Services;
using DotnetInspector.RoundTripCompilation;
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
    IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> ClosureFacts)
{
    internal RoundTripBodyPolicy BodyPolicy { get; init; } = RoundTripBodyPolicy.Selected;
    internal MetadataSource? BodySource { get; init; }
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
    string Source,
    IReadOnlyList<CompileBackFact> SourceFacts,
    IReadOnlyList<CompileBackPlanningDiagnostic> Diagnostics,
    IReadOnlySet<TypeDefinitionHandle> ClosureRoots,
    CompileBackReconstructionPlan Plan,
    IReadOnlyList<FullBodyProduction> FullBodies)
{
    internal static ProductArtifact From(
        ArtifactRequest request,
        CompileBackSourceResult result,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyList<FullBodyProduction>? fullBodies = null)
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
            result.Plan,
            fullBodies ?? []);
}

public sealed record FullBodyProduction(
    MetadataMethodAddress Method,
    string Member,
    MemberBodyProductionStatus Status,
    string? Failure);

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
    int? RemoverToken = null)
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
    bool RequiresUnsafeModifier = false);

internal sealed record ExplicitInterfaceEventInfo(
    TypeDefinitionHandle InterfaceType,
    EventDefinitionHandle InterfaceEvent,
    string QualifiedName,
    string AccessorName);

internal sealed record ExternalExplicitInterfaceMethodInfo(
    string InterfaceDisplayName,
    string ExplicitInterfaceMemberName);

internal sealed record ExternalInterfaceReferenceInfo(
    string MetadataFullName,
    string DisplayFullName,
    AssemblyReferenceIdentity AssemblyIdentity);

internal sealed record ExternalInterfaceRequiredMethod(
    string Name,
    int GenericArity,
    string ReturnType,
    ImmutableArray<string> ParameterTypes);

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
            produced.Projection.ConstructorChain,
            produced.Body.RequiresAsyncModifier,
            produced.Body.RequiresUnsafeModifier);
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
                closure.MemberRequirements,
                request.BodyPolicy),
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
                eventAccessor.SiblingAccessorBody?.Source,
                request.BodyPolicy),
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
        return new CompileBackSourceResult(plan, ComposeCompilationUnit(plan));

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
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackMemberRequirement>> closureMemberRequirements,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected)
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
                // #3000 class). An init-only setter renders a get/init auto-property under Full so
                // the init accessor is represented; the getter-only A/B path (Selected) keeps the
                // minimal get-only shell (records rely on this).
                targetIsAutoProperty
                    ? accessors.Setter.IsNil
                        ? CompileBackStubBodyKind.AutoProperty
                        : SetterIsInitOnly(reader, accessors.Setter)
                            ? bodyPolicy == RoundTripBodyPolicy.Full
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
        MethodDefinitionHandle targetMethod)
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
            if (OperatorNames.FormatDisplayName(declarationName) != declarationName)
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
                "explicit-interface-target-method");
            if (member is null)
                return false;

            int requirementIndex = requirements.FindIndex(
                requirement => requirement.Type.MetadataFullName == interfaceIdentity.MetadataFullName);
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
        TypeDefinition targetType,
        MethodDefinitionHandle targetMethod,
        string metadataMethodName,
        int targetMethodGenericArity,
        IReadOnlySet<TypeDefinitionHandle> closureRoots)
    {
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
            if (OperatorNames.FormatDisplayName(declarationName) != declarationName)
                return null;

            if (ExternalInterfaceReference(reader, (TypeReferenceHandle)declaration.Parent) is not { } interfaceReference)
                return null;
            if (!string.Equals(interfaceReference.MetadataFullName, interfaceMetadataName, StringComparison.Ordinal))
                continue;

            if (!TryReadExternalInterfaceSurface(
                    assemblyPath,
                    interfaceReference,
                    out var requiredMethods)
                || requiredMethods.Count != 1)
            {
                return null;
            }

            var requiredMethod = requiredMethods[0];
            if (!string.Equals(requiredMethod.Name, declarationName, StringComparison.Ordinal)
                || requiredMethod.GenericArity != targetMethodGenericArity)
            {
                return null;
            }

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
            if (!string.Equals(targetSignature.ReturnType, requiredMethod.ReturnType, StringComparison.Ordinal)
                || !targetSignature.ParameterTypes.SequenceEqual(requiredMethod.ParameterTypes, StringComparer.Ordinal))
            {
                return null;
            }

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
                    interfaceReference.DisplayFullName))
            {
                return null;
            }

            string explicitInterfaceMemberName =
                $"{interfaceReference.DisplayFullName}.{Identifier(declarationName)}";
            return new ExternalExplicitInterfaceMethodInfo(
                interfaceReference.DisplayFullName,
                explicitInterfaceMemberName);
        }

        return null;
    }

    // True when a type declared in the recompile closure would intercept the leading
    // identifier of <paramref name="interfaceDisplayFullName"/> as spelled from inside the
    // target type's namespace. The external-interface spelling appears in two positions —
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
    static bool ExternalInterfaceSpellingShadowedByClosure(
        MetadataReader reader,
        TypeDefinition targetType,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        string interfaceDisplayFullName)
    {
        string leadingSegment = interfaceDisplayFullName.Split('.', 2)[0];
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
        ExternalInterfaceReferenceInfo interfaceReference,
        out IReadOnlyList<ExternalInterfaceRequiredMethod> requiredMethods)
    {
        // Read the interface surface from the SAME filename-deduplicated dependency closure
        // the recompile references (ReturnToSender.CompilationReferences: resolver.ResolveAll()
        // with ExcludeTargetAssembly, deduplicated by simple assembly name). Reading from that
        // exact closure — rather than an identity/platform resolution that can select a
        // different file than the recompile ends up referencing — guarantees the members
        // validated here are precisely the members C# will require against the reconstructed
        // `: DisplayName`, and lets us prove the interface is defined by exactly one assembly
        // in the closure (otherwise the unqualified base-list name is ambiguous, CS0433).
        // Memoize per (target assembly, interface identity, interface full name): the same
        // interface recurs across many targets and rescanning the closure per target is an
        // unbounded slowdown. Negative results (unresolvable, ambiguous, or unrepresentable)
        // are cached so they are not retried.
        string cacheKey = $"{assemblyPath}|{interfaceReference.AssemblyIdentity}|{interfaceReference.MetadataFullName}";
        var cached = _externalInterfaceSurfaces.GetOrAdd(cacheKey, _ =>
        {
            var resolver = _externalInterfaceResolvers.GetOrAdd(assemblyPath, static path =>
                new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(path)
                {
                    ExcludeTargetAssembly = true,
                }));

            // Locate the single closure assembly that defines the interface as a
            // TypeDefinition. Type forwarders are ExportedType rows (FindType returns null),
            // so a BCL interface defined once in CoreLib and forwarded elsewhere resolves to
            // exactly one definition. Zero, or more than one, definition declines.
            string? definitionPath = null;
            foreach (var dependency in resolver.ResolveAll())
            {
                if (!ManagedReferenceFilter.IsManagedAssembly(dependency.Path))
                    continue;
                try
                {
                    using var probeStream = File.OpenRead(dependency.Path);
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

                if (definitionPath is not null)
                    return null;
                definitionPath = dependency.Path;
            }

            if (definitionPath is null)
                return null;

            try
            {
                using var stream = File.OpenRead(definitionPath);
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                    return null;
                var reader = peReader.GetMetadataReader();
                if (TypeProducer.FindType(reader, interfaceReference.MetadataFullName) is not { } interfaceHandle)
                    return null;

                var collected = new List<ExternalInterfaceRequiredMethod>();
                return TryCollectRequiredInterfaceMethods(
                        reader,
                        interfaceHandle,
                        resolver,
                        Path.GetFullPath(definitionPath),
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

    static readonly ConcurrentDictionary<string, AssemblyDependencyResolver> _externalInterfaceResolvers =
        new(StringComparer.OrdinalIgnoreCase);

    static readonly ConcurrentDictionary<string, IReadOnlyList<ExternalInterfaceRequiredMethod>?> _externalInterfaceSurfaces =
        new(StringComparer.Ordinal);

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
        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;
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
        ResolvedAssemblyReference assembly,
        string metadataFullName,
        AssemblyDependencyResolver resolver,
        HashSet<string> visited,
        List<ExternalInterfaceRequiredMethod> methods)
    {
        try
        {
            var location = TypeForwardResolver.LocateType(
                    assembly,
                    metadataFullName,
                    resolver,
                    scope: AssemblyResolutionScope.Any)
                ?? TypeForwardResolver.LocateType(
                    assembly,
                    metadataFullName,
                    resolver,
                    scope: AssemblyResolutionScope.Platform);
            if (location is null)
                return false;

            using var stream = location.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return false;

            var externalReader = peReader.GetMetadataReader();
            if (TypeProducer.FindType(externalReader, location.FullTypeName) is not { } interfaceHandle)
                return false;

            string assemblyKey = location.AssemblyPath is { Length: > 0 } path
                ? Path.GetFullPath(path)
                : location.AssemblyKey;
            return TryCollectRequiredInterfaceMethods(
                externalReader,
                interfaceHandle,
                resolver,
                assemblyKey,
                visited,
                methods);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    static bool TryCollectRequiredInterfaceMethods(
        MetadataReader reader,
        TypeDefinitionHandle interfaceHandle,
        AssemblyDependencyResolver resolver,
        string assemblyKey,
        HashSet<string> visited,
        List<ExternalInterfaceRequiredMethod> methods)
    {
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
                || OperatorNames.FormatDisplayName(methodName) != methodName)
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
                requiredSignature.ParameterTypes));
        }

        foreach (var implementationHandle in interfaceDef.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(implementationHandle);
            if (implementation.Interface.Kind == HandleKind.TypeDefinition)
            {
                if (!TryCollectRequiredInterfaceMethods(
                        reader,
                        (TypeDefinitionHandle)implementation.Interface,
                        resolver,
                        assemblyKey,
                        visited,
                        methods))
                {
                    return false;
                }
                continue;
            }

            if (implementation.Interface.Kind == HandleKind.TypeReference)
            {
                if (ExternalInterfaceReference(reader, (TypeReferenceHandle)implementation.Interface) is not { } baseReference)
                    return false;
                if (ResolveExternalAssembly(resolver, baseReference.AssemblyIdentity) is not { } baseAssembly)
                    return false;
                if (!TryCollectExternalInterfaceMethods(
                        baseAssembly,
                        baseReference.MetadataFullName,
                        resolver,
                        visited,
                        methods))
                {
                    return false;
                }
                continue;
            }

            return false;
        }

        return true;
    }

    static ResolvedAssemblyReference? ResolveExternalAssembly(
        AssemblyDependencyResolver resolver,
        AssemblyReferenceIdentity identity)
        => resolver.Resolve(identity, AssemblyResolutionScope.Any)
           ?? resolver.Resolve(identity, AssemblyResolutionScope.Platform);

    static ExternalInterfaceReferenceInfo? ExternalInterfaceReference(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        var typeRef = reader.GetTypeReference(handle);
        if (typeRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return null;

        string metadataFullName = reader.GetFullTypeName(typeRef);
        if (metadataFullName.Contains('`', StringComparison.Ordinal))
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
        string? siblingAccessorBody = null,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected)
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
                // Explicit-interface event targets are excluded: the surface folds events by the
                // sanitized full metadata name (`IBaseEvents.Changed`) while this target requirement
                // carries the stripped identity (`Changed`), so the fold misses and a second
                // explicit-interface event is appended (CS8646/CS0102). They retain the pre-#3007
                // single-accessor shape (an honest floor, not a double-declaration false success);
                // coherent explicit-interface reconstruction is out of #3007's plain-event scope.
                IncludeMemberSurface = bodyPolicy == RoundTripBodyPolicy.Full
                    && explicitEvent is null
                    && targetFacts.Any(fact => fact.Id == "closure-member")
            }
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

        var targetReturnType = isConstructor
            ? null
            : CompileBackTypeSignature.Display(MethodReturnType(reader, targetTypeDef, method));
        var targetParameters = MethodParameters(reader, method, signature);
        var targetTypeParameters = MethodTypeParameters(reader, method);
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
        var externalExplicitInterfaceMethod =
            !isConstructor && sameAssemblyExplicitInterfaceMemberName is null
                ? ExternalExplicitInterfaceMethod(
                    reader,
                    assemblyPath,
                    targetTypeDef,
                    targetMethod,
                    methodName,
                    targetTypeParameters.Count,
                    closureRoots)
                : null;
        string? explicitInterfaceMemberName =
            sameAssemblyExplicitInterfaceMemberName
            ?? externalExplicitInterfaceMethod?.ExplicitInterfaceMemberName;
        string? explicitInterfaceDeclarationSignature = explicitInterfaceMemberName is null
            ? null
            : ExplicitInterfaceMethodDeclarationSignature(
                explicitInterfaceMemberName,
                targetReturnType!,
                targetTypeParameters,
                targetParameters);

        var targetMembers = isConstructor && primaryConstructor is not null
            ? primaryConstructor.FieldInitializers.ToList()
            :
        [
            new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(targetIdentity.FullName, targetMethodName, overload, signatureText),
                isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method,
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
                IsVirtual: !isConstructor && IsVirtualMethod(method),
                IsOverride: false,
                IsSealed: false,
                IsAsync: !isConstructor
                    && (function.RequiresAsyncBodyModifier
                        || function.IsRuntimeAsync == MetadataFactState.Yes),
                ConstructorInitializer: targetConstructorInitializer,
                ExplicitInterfaceMemberName: explicitInterfaceMemberName,
                DeclarationSignature: explicitInterfaceDeclarationSignature,
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
                    || targetFacts.Any(fact => fact.Id == "closure-member"),
                ExternalInterfaces = externalExplicitInterfaceMethod is null
                    ? []
                    : [externalExplicitInterfaceMethod.InterfaceDisplayName],
            }
        };
        AddClosureTypeRequirements(requirements, reader, targetRoot, closureFacts, closureMemberRequirements);

        foreach (var dependency in closureRoots.OrderBy(handle => MetadataTokens.GetToken(handle)))
        {
            if (dependency == targetRoot)
                continue;

            AddClosureTypeRequirements(requirements, reader, dependency, closureFacts, closureMemberRequirements);
        }

        if (explicitInterfaceMemberName is not null)
        {
            bool explicitInterfaceShapeIsViable = externalExplicitInterfaceMethod is not null
                || AddExplicitInterfaceMethodDeclaration(requirements, reader, targetTypeDef, targetMethod);
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
        bool isExplicitInterfaceMethod = member.ExplicitInterfaceMemberName is not null
            && member.Kind is CompileBackMemberKind.Method;
        var apiMember = new ApiMember
        {
            Name = member.ExplicitInterfaceMemberName ?? member.Identity.Method,
            Kind = isExplicitInterfaceProperty || isExplicitInterfaceEvent || isExplicitInterfaceMethod
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
            MetadataToken = member.MetadataToken,
            GetterToken = member.GetterToken,
            SetterToken = member.SetterToken,
            AdderToken = member.AdderToken,
            RemoverToken = member.RemoverToken,
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
        // AutoPropertyGetInit renders a get/init auto-property. The compiler-synthesized init
        // accessor faithfully reproduces the original init setter body, so the sibling/target
        // setter stays represented (not dropped) while remaining honest about its init-only shape.
        bool isAutoGetInit = member.StubBody is CompileBackStubBodyKind.AutoPropertyGetInit;
        // Explicit-body init accessors must be spelled `init`, not `set`; otherwise the round-trip
        // silently downgrades an init-only property to a public setter (dropping the required
        // modreq(IsExternalInit)) while still reporting the body Complete.
        bool setterIsInit = member.StubBody is CompileBackStubBodyKind.TargetGetterWithInitSetter
            or CompileBackStubBodyKind.TargetInitSetterWithGetter
            or CompileBackStubBodyKind.TargetInitBody
            or CompileBackStubBodyKind.ThrowGetInit;
        bool hasGetter = isAutoGetInit
            || member.Kind == CompileBackMemberKind.PropertyGet
            || member.StubBody is CompileBackStubBodyKind.AutoPropertyGetSet
                or CompileBackStubBodyKind.ThrowGetSet
                or CompileBackStubBodyKind.ThrowGetInit
                or CompileBackStubBodyKind.TargetSetterWithGetter
                or CompileBackStubBodyKind.TargetInitSetterWithGetter;
        bool hasSetter = !isAutoGetInit
            && (member.Kind == CompileBackMemberKind.PropertySet
                || member.StubBody is CompileBackStubBodyKind.AutoPropertyGetSet
                    or CompileBackStubBodyKind.ThrowGetSet
                    or CompileBackStubBodyKind.ThrowGetInit
                    or CompileBackStubBodyKind.TargetGetterWithSetter
                    or CompileBackStubBodyKind.TargetGetterWithInitSetter);
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
            accessors.Add(new ApiAccessor { Kind = setterIsInit ? "init" : "set" });
        if (isAutoGetInit)
            accessors.Add(new ApiAccessor { Kind = "init" });
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
            CompileBackStubBodyKind.AutoPropertyGetInit
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
            CompileBackStubBodyKind.ThrowGetInit
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
            CompileBackStubBodyKind.TargetInitBody
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
            CompileBackStubBodyKind.TargetGetterWithInitSetter
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
            CompileBackStubBodyKind.TargetInitSetterWithGetter
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
            bool isInitSetter = hasSetter && SetterIsInitOnly(reader, accessors.Setter);
            bool isAbstractAccessor = !accessor.IsNil && propertyDeclaration.IsAbstract;
            var noBodyProperty = (typeDef.Attributes & TypeAttributes.Interface) != 0 || isAbstractAccessor;
            var stubBody = noBodyProperty
                ? hasSetter
                    ? isInitSetter
                        ? CompileBackStubBodyKind.AutoPropertyGetInit
                        : CompileBackStubBodyKind.AutoPropertyGetSet
                    : CompileBackStubBodyKind.None
                : hasSetter && isAutoProperty
                    ? isInitSetter
                        ? CompileBackStubBodyKind.AutoPropertyGetInit
                        : CompileBackStubBodyKind.AutoPropertyGetSet
                    : isAutoProperty
                        ? CompileBackStubBodyKind.AutoProperty
                        : hasSetter
                            ? isInitSetter
                                ? CompileBackStubBodyKind.ThrowGetInit
                                : CompileBackStubBodyKind.ThrowGetSet
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

        internal static CompileBackMemberRequirement? MethodRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            MethodDefinitionHandle methodHandle,
            string factId = "typed-closure-method")
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
                [new CompileBackFact("metadata", isConstructor ? "typed-closure-constructor" : factId, name)],
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
                InterfaceDisplayNames: InterfaceSignatures(reader, typeDef, requirementsByMetadataName)
                    .Select(signature => signature.DisplayName)
                    .Concat(requirement.ExternalInterfaces)
                    .Distinct(StringComparer.Ordinal)
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

        static IReadOnlyList<CompileBackTypeSignature> InterfaceSignatures(
            MetadataReader reader,
            TypeDefinition typeDef,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName)
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
                if (!requirementsByMetadataName.ContainsKey(interfaceIdentity.MetadataFullName))
                    continue;

                interfaces.Add(CompileBackTypeSignature.Definition(interfaceIdentity));
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
            // The product owns the field-inclusion decision (ApiSurfaceExtractor.SurfaceFieldHandles):
            // it drops synthesized auto-property backing fields (`<Name>k__BackingField`, which the
            // compiler re-synthesizes for each reconstructed auto-property, issue #3036), the enum
            // `value__` slot, and a field-like event's compiler-generated backing field (issue
            // #3083), while surfacing the closure/state-machine captures reconstruction needs
            // (includeCompilerGenerated). RTS keeps only the reconstruction-side gates below
            // (unspeakable names, signature decode, fixed buffers, pointer surface, dedup).
            foreach (var fieldHandle in ApiSurfaceExtractor.SurfaceFieldHandles(
                reader, typeDef, includeAll: true, includeCompilerGenerated: true))
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
                int existingPropertyIndex = members.FindIndex(member =>
                    (member.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet or CompileBackMemberKind.Field)
                    && member.Identity.Method == Identifier(propertyName));
                if (existingPropertyIndex >= 0)
                {
                    var existing = members[existingPropertyIndex];
                    members[existingPropertyIndex] = existing with
                    {
                        GetterToken = existing.GetterToken ?? (accessors.Getter.IsNil ? null : MetadataTokens.GetToken(accessors.Getter)),
                        SetterToken = existing.SetterToken ?? (accessors.Setter.IsNil ? null : MetadataTokens.GetToken(accessors.Setter)),
                    };
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
                bool isInitSetter = hasSetter && SetterIsInitOnly(reader, accessors.Setter);
                bool isAbstractAccessor = !accessor.IsNil && propertyDeclaration.IsAbstract;
                var noBodyProperty = requirement.RequiredKind == CompileBackTypeKind.Interface || isAbstractAccessor;
                var stubBody = noBodyProperty
                    ? hasSetter
                        ? isInitSetter
                            ? CompileBackStubBodyKind.AutoPropertyGetInit
                            : CompileBackStubBodyKind.AutoPropertyGetSet
                        : CompileBackStubBodyKind.None
                    : hasSetter && isAutoProperty
                        ? isInitSetter
                            ? CompileBackStubBodyKind.AutoPropertyGetInit
                            : CompileBackStubBodyKind.AutoPropertyGetSet
                        : isAutoProperty
                            ? CompileBackStubBodyKind.AutoProperty
                            : hasSetter
                                ? isInitSetter
                                    ? CompileBackStubBodyKind.ThrowGetInit
                                    : CompileBackStubBodyKind.ThrowGetSet
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
                        : MethodAccessibility(accessorMethod),
                    GetterToken: accessors.Getter.IsNil ? null : MetadataTokens.GetToken(accessors.Getter),
                    SetterToken: accessors.Setter.IsNil ? null : MetadataTokens.GetToken(accessors.Setter)));
            }

            foreach (var eventHandle in typeDef.GetEvents())
            {
                var eventDefinition = reader.GetEventDefinition(eventHandle);
                var accessors = eventDefinition.GetAccessors();
                if (!accessors.Adder.IsNil)
                    accessorMethods.Add(accessors.Adder);
                if (!accessors.Remover.IsNil)
                    accessorMethods.Add(accessors.Remover);

                string eventName = reader.GetString(eventDefinition.Name);
                if (eventName.Contains('<', StringComparison.Ordinal))
                    continue;
                int existingEventIndex = members.FindIndex(member =>
                    member.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove
                    && member.Identity.Method == Identifier(eventName));
                int? adderToken = accessors.Adder.IsNil ? null : MetadataTokens.GetToken(accessors.Adder);
                int? removerToken = accessors.Remover.IsNil ? null : MetadataTokens.GetToken(accessors.Remover);
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
                });
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
                    || (name != ".ctor" && name.Contains('.', StringComparison.Ordinal)))
                {
                    continue;
                }

                bool isConstructor = name == ".ctor";
                string identifierName = CSharpNaming.SourceMethodName(name);
                int existingMethodIndex = members.FindIndex(member =>
                    member.Kind == (isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method)
                    && member.Identity.Method == identifierName);
                if (existingMethodIndex >= 0)
                {
                    var existing = members[existingMethodIndex];
                    members[existingMethodIndex] = existing with
                    {
                        MetadataToken = existing.MetadataToken ?? MetadataTokens.GetToken(methodHandle),
                    };
                    continue;
                }
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
                    Accessibility: MethodAccessibility(method),
                    MetadataToken: MetadataTokens.GetToken(methodHandle)));
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
