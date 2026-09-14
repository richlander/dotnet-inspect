using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>Decoded, reader-independent observations from one participant.</summary>
public sealed class EcosystemIntegrationObservationContext
{
    internal EcosystemIntegrationObservationContext(
        ImmutableArray<EcosystemIntegrationTypeObservation> types,
        ImmutableArray<EcosystemIntegrationMethodObservation> starterMethods)
    {
        Types = types;
        StarterMethods = starterMethods;
    }

    public ImmutableArray<EcosystemIntegrationTypeObservation> Types { get; }
    public ImmutableArray<EcosystemIntegrationMethodObservation> StarterMethods { get; }
}

/// <summary>An owner-produced observation with its original evidence association.</summary>
public abstract class EcosystemIntegrationObservation
{
    internal EcosystemIntegrationObservation() { }

    public EcosystemIntegrationClassification Classify(
        IntegrationConceptDescriptor concept,
        string kind) => new(this, concept, kind);
}

public sealed class EcosystemIntegrationTypeObservation : EcosystemIntegrationObservation
{
    internal EcosystemIntegrationTypeObservation(
        string metadataName,
        MetadataTypeDefinitionName? definition)
    {
        MetadataName = metadataName;
        Definition = definition;
    }

    public string MetadataName { get; }
    public MetadataTypeDefinitionName? Definition { get; }
}

public sealed class EcosystemIntegrationMethodObservation : EcosystemIntegrationObservation
{
    internal EcosystemIntegrationMethodObservation(
        EcosystemIntegrationTypeObservation declaringType,
        string name,
        MethodSignature<string> signature,
        EcosystemIntegrationApiEvidence? evidence)
    {
        DeclaringType = declaringType;
        Name = name;
        Signature = signature;
        Evidence = evidence;
    }

    public EcosystemIntegrationTypeObservation DeclaringType { get; }
    public string Name { get; }
    public ImmutableArray<string> ParameterTypes => Signature.ParameterTypes;
    public string ReturnType => Signature.ReturnType;
    public EcosystemIntegrationApiEvidence? Evidence { get; }

    internal MethodSignature<string> Signature { get; }
}

/// <summary>Interpretation paired with the observation that supplies its evidence.</summary>
public sealed class EcosystemIntegrationClassification
{
    internal EcosystemIntegrationClassification(
        EcosystemIntegrationObservation observation,
        IntegrationConceptDescriptor concept,
        string kind)
    {
        ArgumentNullException.ThrowIfNull(concept);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!IntegrationConceptCatalog.EcosystemObserved.Concepts.Contains(
                concept,
                ReferenceEqualityComparer.Instance))
        {
            throw new ArgumentException(
                "The concept does not belong to the ecosystem observation policy.",
                nameof(concept));
        }

        Observation = observation;
        Concept = concept;
        Kind = kind;
    }

    public EcosystemIntegrationObservation Observation { get; }
    public IntegrationConceptDescriptor Concept { get; }
    public string Kind { get; }
}
