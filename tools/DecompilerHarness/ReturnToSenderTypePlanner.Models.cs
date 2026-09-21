using System.Globalization;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using DotnetInspector.Services;
using DotnetInspector.RoundTripCompilation;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using PropertyInitializationConstructor = ILInspector.Decompiler.SelectedPropertyAccessorSource.PropertyInitializationConstructor;

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
    internal ReturnToSender.CompilationClosure? CompilationClosure
    { get; set; }
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
        ClosureFacts)
{
    internal PropertyInitializationConstructor? InitializationConstructor { get; init; }
}

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
    int? RemoverToken = null,
    bool IsReadOnly = false,
    CSharpBlockBody? CompanionBody = null,
    string? PropertyInitializer = null)
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
    DecompilationFidelity Fidelity = DecompilationFidelity.Full,
    bool UsesAutomaticGetterBody = false);

internal sealed class CompileBackSourceUnavailableException(string message)
    : InvalidOperationException(message)
{
}

internal sealed record ExplicitInterfaceEventInfo(
    TypeDefinitionHandle InterfaceType,
    EventDefinitionHandle InterfaceEvent,
    string QualifiedName,
    string AccessorName);

internal sealed record ExternalExplicitInterfaceMethodInfo(
    string InterfaceDisplayName,
    string ExplicitInterfaceMemberName,
    IReadOnlyList<CompileBackMemberRequirement> AdditionalInterfaceStubs);

internal sealed record ExternalInterfaceReferenceInfo(
    string MetadataFullName,
    string DisplayFullName,
    AssemblyReferenceIdentity AssemblyIdentity);

internal sealed record ExternalInterfaceRequiredMethod(
    string Name,
    int GenericArity,
    string ReturnType,
    ImmutableArray<string> ParameterTypes);
