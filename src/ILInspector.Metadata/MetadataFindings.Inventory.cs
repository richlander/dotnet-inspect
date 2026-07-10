using System.Collections.Immutable;
using ILInspector.Findings;

namespace ILInspector.Metadata;

/// <summary>
/// Finding producers over typed Metadata inventories. These operations consume already-extracted
/// data; acquisition and presentation remain outside the producer.
/// </summary>
public static partial class MetadataFindings
{
    public static readonly FindingDescriptor AssemblyReferenceDescriptor =
        new("metadata.assembly-reference", "Assembly reference");

    public static readonly FindingDescriptor TypeForwarderDescriptor =
        new("metadata.type-forwarder", "Type forwarder");

    public static readonly FindingDescriptor ResourceDescriptor =
        new("metadata.resource", "Manifest resource");

    public static readonly FindingDescriptor AssemblyAttributeDescriptor =
        new("metadata.assembly-attribute", "Assembly or module attribute");

    static readonly FindingMatchOptions InventoryMatchOptions = new()
    {
        MatchMode = FindingMatchMode.IdentitySet,
    };

    public static FindingInspection<AssemblyReference> InspectAssemblyReferences(
        IEnumerable<AssemblyReference> references,
        FindingSubject subject)
        => InspectInventory(
            references,
            subject,
            AssemblyReferenceDescriptor,
            static reference => reference.Name.ToUpperInvariant(),
            static reference => JoinSortKey(
                reference.Name.ToUpperInvariant(),
                reference.Version,
                reference.Culture,
                reference.PublicKeyToken));

    public static FindingComparison<AssemblyReference> CompareAssemblyReferences(
        IEnumerable<AssemblyReference> oldReferences,
        IEnumerable<AssemblyReference> newReferences,
        FindingSubject subject)
        => CompareInventory(
            InspectAssemblyReferences(oldReferences, subject),
            InspectAssemblyReferences(newReferences, subject));

    public static FindingInspection<TypeForwarderInfo> InspectTypeForwarders(
        IEnumerable<TypeForwarderInfo> forwarders,
        FindingSubject subject)
        => InspectInventory(
            forwarders,
            subject,
            TypeForwarderDescriptor,
            static forwarder => forwarder.TypeName,
            static forwarder => JoinSortKey(forwarder.TypeName, forwarder.TargetAssembly));

    public static FindingComparison<TypeForwarderInfo> CompareTypeForwarders(
        IEnumerable<TypeForwarderInfo> oldForwarders,
        IEnumerable<TypeForwarderInfo> newForwarders,
        FindingSubject subject)
        => CompareInventory(
            InspectTypeForwarders(oldForwarders, subject),
            InspectTypeForwarders(newForwarders, subject));

    public static FindingInspection<ManifestResourceInfo> InspectResources(
        IEnumerable<ManifestResourceInfo> resources,
        FindingSubject subject)
        => InspectInventory(
            resources,
            subject,
            ResourceDescriptor,
            static resource => resource.Name,
            static resource => JoinSortKey(
                resource.Name,
                resource.IsPublic,
                resource.IsEmbedded,
                resource.Size));

    public static FindingComparison<ManifestResourceInfo> CompareResources(
        IEnumerable<ManifestResourceInfo> oldResources,
        IEnumerable<ManifestResourceInfo> newResources,
        FindingSubject subject)
        => CompareInventory(
            InspectResources(oldResources, subject),
            InspectResources(newResources, subject));

    public static FindingInspection<AssemblyAttributeInfo> InspectAssemblyAttributes(
        IEnumerable<AssemblyAttributeInfo> attributes,
        FindingSubject subject)
        => InspectInventory(
            attributes,
            subject,
            AssemblyAttributeDescriptor,
            static attribute => JoinSortKey(attribute.Target, attribute.Name),
            static attribute => JoinSortKey(attribute.Target, attribute.Name, attribute.Value));

    public static FindingComparison<AssemblyAttributeInfo> CompareAssemblyAttributes(
        IEnumerable<AssemblyAttributeInfo> oldAttributes,
        IEnumerable<AssemblyAttributeInfo> newAttributes,
        FindingSubject subject)
        => CompareInventory(
            InspectAssemblyAttributes(oldAttributes, subject),
            InspectAssemblyAttributes(newAttributes, subject));

    static FindingInspection<T> InspectInventory<T>(
        IEnumerable<T> observations,
        FindingSubject subject,
        FindingDescriptor descriptor,
        Func<T, string> identity,
        Func<T, string> sortKey)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(sortKey);

        var projected = observations
            .Select(observation => observation is null
                ? throw new ArgumentException("Inventory cannot contain null observations.", nameof(observations))
                : new InventoryObservation<T>(
                    observation,
                    identity(observation),
                    sortKey(observation)))
            .OrderBy(static observation => observation.Identity, StringComparer.Ordinal)
            .ThenBy(static observation => observation.SortKey, StringComparer.Ordinal)
            .ToArray();

        var findings = ImmutableArray.CreateBuilder<Finding<T>>(projected.Length);
        for (int position = 0; position < projected.Length; position++)
        {
            var observation = projected[position];
            findings.Add(new Finding<T>(
                subject,
                descriptor,
                new FindingKey(observation.Identity),
                position,
                observation.Payload));
        }

        return new FindingInspection<T>.Complete(findings.MoveToImmutable());
    }

    static FindingComparison<T> CompareInventory<T>(
        FindingInspection<T> oldInspection,
        FindingInspection<T> newInspection)
        where T : notnull
    {
        var oldFindings = CompleteFindings(oldInspection);
        var newFindings = CompleteFindings(newInspection);
        var match = FindingMatcher.Match(
            oldFindings.Keys(),
            newFindings.Keys(),
            InventoryMatchOptions);
        var pairs = FindingFold.ToPairs(match, oldFindings, newFindings);
        pairs = PromoteChangedPayloads(pairs);

        return new FindingComparison<T>.Complete(
            pairs,
            match,
            oldInspection,
            newInspection);
    }

    static ImmutableArray<PairFinding<T>> PromoteChangedPayloads<T>(
        ImmutableArray<PairFinding<T>> pairs)
        where T : notnull
    {
        var builder = ImmutableArray.CreateBuilder<PairFinding<T>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<T>.Present present
                && !EqualityComparer<T>.Default.Equals(
                    present.Old.Payload,
                    present.New.Payload))
            {
                builder.Add(new PairFinding<T>.Changed(
                    present.Old,
                    present.New,
                    present.Difference));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.MoveToImmutable();
    }

    static ImmutableArray<Finding<T>> CompleteFindings<T>(
        FindingInspection<T> inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Complete complete
            ? complete.Findings
            : throw new InvalidOperationException("An in-memory inventory inspection must complete.");

    static string JoinSortKey(params object?[] parts)
        => string.Join(
            '\u001F',
            parts.Select(static part => part?.ToString() ?? ""));

    sealed record InventoryObservation<T>(
        T Payload,
        string Identity,
        string SortKey)
        where T : notnull;
}
