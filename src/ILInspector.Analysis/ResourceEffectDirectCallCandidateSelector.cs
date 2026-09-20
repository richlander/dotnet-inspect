using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class ResourceEffectDirectCallCandidateSelector
    : IDirectCallDefinitionCandidateSelector
{
    readonly ImmutableArray<AdmittedResourceEffectDeclaration>
        _declarations;
    ImmutableHashSet<(
        AssemblyAcquisitionRegistration Registration,
        ResourceTypeExpression.Named DeclaringType)>
            _nonInterfaceDeclaringTypes = [];
    bool _includePotentialInterfaceImplementations;

    internal ResourceEffectDirectCallCandidateSelector(
        ResourceEffectAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
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
                continue;
            }
            if (_includePotentialInterfaceImplementations
                && !_nonInterfaceDeclaringTypes.Contains((
                    participant.Assembly.Registration,
                    selector.DeclaringType)))
            {
                return true;
            }
        }
        return false;
    }

    internal void IncludePotentialInterfaceImplementations(
        DirectCallDefinitionResolutionOutcome provisional)
    {
        ArgumentNullException.ThrowIfNull(provisional);
        var kinds = new Dictionary<(
            AssemblyAcquisitionRegistration Registration,
            ResourceTypeExpression.Named DeclaringType), bool?>();
        foreach (AdmittedResourceEffectDeclaration declaration
            in _declarations)
        {
            var target =
                (ResourceEffectTargetSelector.Member)
                    declaration.Target;
            if (target.Selector.Kind
                    is ResourceEffectMemberKind.Field
                        or ResourceEffectMemberKind.Constructor
                || target.Selector.IsStatic)
            {
                continue;
            }
            foreach (DirectCallDefinitionResolution.Resolved resolved
                in provisional
                    is DirectCallDefinitionResolutionOutcome.Completed
                        completed
                    ? completed.Results.OfType<
                        DirectCallDefinitionResolution.Resolved>()
                    : [])
            {
                if (ResourceEffectSelectorBinder.Bind(
                        declaration,
                        resolved)
                    is not ResourceEffectSelectorBinding.Resolved)
                {
                    continue;
                }
                var key = (
                    resolved.Participant.Assembly.Registration,
                    target.Selector.DeclaringType);
                bool isInterface =
                    resolved.Definition.IsInterfaceDefinition;
                if (!kinds.TryGetValue(key, out bool? existing))
                {
                    kinds.Add(key, isInterface);
                }
                else if (existing != isInterface)
                {
                    kinds[key] = null;
                }
            }
        }
        _nonInterfaceDeclaringTypes =
            kinds
                .Where(entry => entry.Value == false)
                .Select(entry => entry.Key)
                .ToImmutableHashSet();
        _includePotentialInterfaceImplementations = true;
    }
}
