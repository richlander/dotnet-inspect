using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class ResourceEffectDirectCallCandidateSelector
    : IDirectCallDefinitionCandidateSelector
{
    readonly ImmutableArray<AdmittedResourceEffectDeclaration>
        _declarations;
    readonly IAssemblyBindingPolicy? _bindingPolicy;
    readonly Dictionary<
        CatalogCallGraphParticipant,
        MethodImplBodies> _localMethodImplBodies =
            new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<
        (AssemblyAcquisitionRegistration Registration,
            AssemblyReferenceIdentity Identity),
        MethodImplBodies> _externalMethodImplBodies = [];
    int _remainingMethodImplementations;
    int _remainingNameBytes =
        MetadataSafetyPolicy.MaxStructuralSignatureWorkChars;

    internal ResourceEffectDirectCallCandidateSelector(
        ResourceEffectAdmission admission,
        IAssemblyBindingPolicy? bindingPolicy = null,
        int maxMethodImplementations =
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxMethodImplementations);
        _bindingPolicy = bindingPolicy;
        _remainingMethodImplementations = maxMethodImplementations;
        _declarations =
        [
            .. admission.Models
                .SelectMany(model => model.Declarations)
                .Where(declaration =>
                    declaration.Target
                        is ResourceEffectTargetSelector.Member
                        {
                            Selector.Kind:
                                not ResourceEffectMemberKind.Field,
                        }),
        ];
    }

    public bool Includes(
        CatalogCallGraphParticipant participant,
        DirectCall call)
    {
        foreach (AdmittedResourceEffectDeclaration declaration
            in _declarations)
        {
            var target =
                (ResourceEffectTargetSelector.Member)
                    declaration.Target;
            ResourceEffectMemberSelector selector = target.Selector;
            if (ResourceEffectSelectorBinder.CouldMatch(
                    selector,
                    call.Callee))
            {
                return true;
            }
            if (!ResourceEffectSelectorBinder
                    .CouldMatchInterfaceImplementation(
                        selector,
                        call.Callee))
            {
                if (!ResourceEffectSelectorBinder
                        .CouldMatchInterfaceImplementationBodyShape(
                            selector,
                            call.Callee))
                {
                    continue;
                }
                if (!TryGetLocalMethodDefinitionToken(
                        call,
                        out int methodToken))
                {
                    if (CouldBeExternalMethodImplBody(
                            participant,
                            call))
                    {
                        return true;
                    }
                    continue;
                }
                MethodImplBodies bodies =
                    GetLocalMethodImplBodies(participant);
                if (bodies.IsComplete
                    && !bodies.Tokens.Contains(methodToken)
                    && !bodies.Names.Contains(call.Callee.Name))
                {
                    continue;
                }
            }
            return true;
        }
        return false;
    }

    bool CouldBeExternalMethodImplBody(
        CatalogCallGraphParticipant participant,
        DirectCall call)
    {
        if (_bindingPolicy is null
            || TryGetAssemblyReference(
                call.Callee.DeclaringType,
                out AssemblyReferenceIdentity identity)
                is false)
        {
            return true;
        }
        var key = (
            participant.Assembly.Registration,
            identity);
        if (!_externalMethodImplBodies.TryGetValue(
                key,
                out MethodImplBodies? bodies))
        {
            bodies = SelectExternalMethodImplBodies(
                participant,
                identity);
            _externalMethodImplBodies.Add(key, bodies);
        }
        return !bodies.IsComplete
            || !bodies.CanExcludeExternalNames
            || bodies.Names.Contains(call.Callee.Name);
    }

    MethodImplBodies SelectExternalMethodImplBodies(
        CatalogCallGraphParticipant participant,
        AssemblyReferenceIdentity identity)
    {
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(identity),
            AssemblyBindingOrigin.FromAssembly(
                participant.Assembly),
            TypeResolutionRequestFactory.Scope(identity));
        AssemblyBindingSelectionSnapshot snapshot =
            _bindingPolicy!.Select(request);
        if (!ReferenceEquals(
                snapshot.Version,
                _bindingPolicy.Version)
            || AssemblyBindingSelection.ValidateForMetadataRequest(
                    request,
                    snapshot.Selection)
                is not AssemblyBindingSelection.Selected selected)
        {
            return MethodImplBodies.Incomplete;
        }
        return ReadMethodImplBodies(
            selected.Assembly,
            expectedModule: null);
    }

    MethodImplBodies GetLocalMethodImplBodies(
        CatalogCallGraphParticipant participant)
    {
        if (_localMethodImplBodies.TryGetValue(
                participant,
                out MethodImplBodies? bodies))
        {
            return bodies;
        }
        bodies = ReadMethodImplBodies(
            participant.Assembly,
            participant.CallGraph.ModuleIdentity);
        _localMethodImplBodies.Add(participant, bodies);
        return bodies;
    }

    MethodImplBodies ReadMethodImplBodies(
        ResolvedAssemblyReference assembly,
        LibraryBodyModuleIdentity? expectedModule)
    {
        try
        {
            using Stream stream = assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return MethodImplBodies.Incomplete;
            MetadataReader reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly
                || AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
                    != assembly.Identity
                || expectedModule is not null
                    && (expectedModule.AssemblyIdentity
                            != assembly.Identity
                        || reader.GetGuid(
                                reader.GetModuleDefinition().Mvid)
                            != expectedModule.ModuleVersionId))
            {
                return MethodImplBodies.Incomplete;
            }

            var tokens = ImmutableHashSet.CreateBuilder<int>();
            var names = ImmutableHashSet.CreateBuilder<string>(
                StringComparer.Ordinal);
            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                TypeDefinition type =
                    reader.GetTypeDefinition(typeHandle);
                foreach (MethodImplementationHandle handle
                    in type.GetMethodImplementations())
                {
                    if (--_remainingMethodImplementations < 0)
                        return MethodImplBodies.Incomplete;
                    MethodImplementation implementation =
                        reader.GetMethodImplementation(handle);
                    StringHandle nameHandle;
                    if (implementation.MethodBody.Kind
                        == HandleKind.MethodDefinition)
                    {
                        MethodDefinition body =
                            reader.GetMethodDefinition(
                                (MethodDefinitionHandle)
                                    implementation.MethodBody);
                        nameHandle = body.Name;
                        tokens.Add(
                            MetadataTokens.GetToken(
                                implementation.MethodBody));
                    }
                    else if (implementation.MethodBody.Kind
                        == HandleKind.MemberReference)
                    {
                        nameHandle = reader.GetMemberReference(
                            (MemberReferenceHandle)
                                implementation.MethodBody).Name;
                    }
                    else
                    {
                        return MethodImplBodies.Incomplete;
                    }
                    int nameBytes =
                        reader.GetBlobReader(nameHandle).Length;
                    if (nameBytes
                            > MetadataSafetyPolicy.MaxTypeNameCharacters
                        || (_remainingNameBytes -= nameBytes) < 0)
                    {
                        return MethodImplBodies.Incomplete;
                    }
                    names.Add(reader.GetString(nameHandle));
                }
            }
            return new(
                IsComplete: true,
                CanExcludeExternalNames:
                    reader.ExportedTypes.Count == 0
                    && reader.AssemblyFiles.Count == 0,
                tokens.ToImmutable(),
                names.ToImmutable());
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return MethodImplBodies.Incomplete;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return MethodImplBodies.Incomplete;
        }
    }

    static bool TryGetAssemblyReference(
        TypeRef type,
        out AssemblyReferenceIdentity identity)
    {
        if (type.Kind == TypeRefKind.GenericInstance)
            type = type.ElementType!;
        if (type.Resolution?.Origin
            is TypeReferenceOrigin.AssemblyReference reference)
        {
            identity = reference.Assembly;
            return true;
        }
        identity = null!;
        return false;
    }

    static bool TryGetLocalMethodDefinitionToken(
        DirectCall call,
        out int methodToken)
    {
        methodToken = call.CalleeDefinitionToken;
        try
        {
            return MetadataTokens.EntityHandle(methodToken).Kind
                == HandleKind.MethodDefinition;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    sealed record MethodImplBodies(
        bool IsComplete,
        bool CanExcludeExternalNames,
        ImmutableHashSet<int> Tokens,
        ImmutableHashSet<string> Names)
    {
        internal static MethodImplBodies Incomplete { get; } =
            new(false, false, [], []);
    }
}
