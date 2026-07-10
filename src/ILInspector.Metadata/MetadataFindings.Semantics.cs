using ILInspector.Findings;

namespace ILInspector.Metadata;

/// <summary>
/// Finding producers over typed semantic observations extracted from assembly metadata.
/// </summary>
public static partial class MetadataFindings
{
    public static readonly FindingDescriptor UnionTypeDescriptor =
        new("metadata.union-type", "Union type");

    public static readonly FindingDescriptor EcosystemIntegrationDescriptor =
        new("metadata.ecosystem-integration", "Ecosystem integration");

    public static readonly FindingDescriptor OpenTelemetrySignalDescriptor =
        new("metadata.opentelemetry-signal", "OpenTelemetry signal");

    public static readonly FindingDescriptor SwitchDescriptor =
        new("metadata.switch", "Feature switch");

    public static FindingInspection<UnionTypeInfo> InspectUnionTypes(
        IEnumerable<UnionTypeInfo> unions,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(unions);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectUnionTypesCore(unions, subject, nameof(unions));
    }

    static FindingInspection<UnionTypeInfo> InspectUnionTypesCore(
        IEnumerable<UnionTypeInfo> unions,
        FindingSubject subject,
        string parameterName)
        => InspectInventory(
            unions,
            subject,
            UnionTypeDescriptor,
            static union => union.TypeName,
            static union => JoinSortKey(
                union.TypeName,
                union.Kind,
                union.ImplementsIUnion ? "1" : "0",
                JoinSortKey(union.CaseTypes.Order(StringComparer.Ordinal))),
            parameterName);

    public static FindingComparison<UnionTypeInfo> CompareUnionTypes(
        IEnumerable<UnionTypeInfo> oldUnions,
        IEnumerable<UnionTypeInfo> newUnions,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldUnions);
        ArgumentNullException.ThrowIfNull(newUnions);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectUnionTypesCore(oldUnions, subject, nameof(oldUnions)),
            InspectUnionTypesCore(newUnions, subject, nameof(newUnions)),
            UnionPayloadsEqual);
    }

    public static FindingInspection<EcosystemIntegrationSignalInfo> InspectEcosystemIntegrations(
        IEnumerable<EcosystemIntegrationSignalInfo> integrations,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(integrations);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectEcosystemIntegrationsCore(integrations, subject, nameof(integrations));
    }

    static FindingInspection<EcosystemIntegrationSignalInfo> InspectEcosystemIntegrationsCore(
        IEnumerable<EcosystemIntegrationSignalInfo> integrations,
        FindingSubject subject,
        string parameterName)
        => InspectInventory(
            integrations,
            subject,
            EcosystemIntegrationDescriptor,
            static integration => JoinSortKey(
                integration.Integration,
                integration.Kind,
                integration.Shape,
                integration.Name),
            static integration => JoinSortKey(
                integration.Integration,
                integration.Kind,
                integration.Shape,
                integration.Name),
            parameterName);

    public static FindingComparison<EcosystemIntegrationSignalInfo> CompareEcosystemIntegrations(
        IEnumerable<EcosystemIntegrationSignalInfo> oldIntegrations,
        IEnumerable<EcosystemIntegrationSignalInfo> newIntegrations,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldIntegrations);
        ArgumentNullException.ThrowIfNull(newIntegrations);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectEcosystemIntegrationsCore(oldIntegrations, subject, nameof(oldIntegrations)),
            InspectEcosystemIntegrationsCore(newIntegrations, subject, nameof(newIntegrations)));
    }

    public static FindingInspection<OpenTelemetrySignalInfo> InspectOpenTelemetrySignals(
        IEnumerable<OpenTelemetrySignalInfo> signals,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectOpenTelemetrySignalsCore(signals, subject, nameof(signals));
    }

    static FindingInspection<OpenTelemetrySignalInfo> InspectOpenTelemetrySignalsCore(
        IEnumerable<OpenTelemetrySignalInfo> signals,
        FindingSubject subject,
        string parameterName)
        => InspectInventory(
            signals,
            subject,
            OpenTelemetrySignalDescriptor,
            static signal => JoinSortKey(signal.Kind, signal.Shape, signal.Name),
            static signal => JoinSortKey(signal.Kind, signal.Shape, signal.Name),
            parameterName);

    public static FindingComparison<OpenTelemetrySignalInfo> CompareOpenTelemetrySignals(
        IEnumerable<OpenTelemetrySignalInfo> oldSignals,
        IEnumerable<OpenTelemetrySignalInfo> newSignals,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldSignals);
        ArgumentNullException.ThrowIfNull(newSignals);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectOpenTelemetrySignalsCore(oldSignals, subject, nameof(oldSignals)),
            InspectOpenTelemetrySignalsCore(newSignals, subject, nameof(newSignals)));
    }

    public static FindingInspection<SwitchInfo> InspectSwitches(
        IEnumerable<SwitchInfo> switches,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(switches);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectSwitchesCore(switches, subject, nameof(switches));
    }

    static FindingInspection<SwitchInfo> InspectSwitchesCore(
        IEnumerable<SwitchInfo> switches,
        FindingSubject subject,
        string parameterName)
        => InspectInventory(
            switches,
            subject,
            SwitchDescriptor,
            static featureSwitch => JoinSortKey(
                featureSwitch.Kind,
                featureSwitch.Switch,
                featureSwitch.Api),
            static featureSwitch => JoinSortKey(
                featureSwitch.Kind,
                featureSwitch.Switch,
                featureSwitch.Api),
            parameterName);

    public static FindingComparison<SwitchInfo> CompareSwitches(
        IEnumerable<SwitchInfo> oldSwitches,
        IEnumerable<SwitchInfo> newSwitches,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldSwitches);
        ArgumentNullException.ThrowIfNull(newSwitches);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectSwitchesCore(oldSwitches, subject, nameof(oldSwitches)),
            InspectSwitchesCore(newSwitches, subject, nameof(newSwitches)));
    }

    static bool UnionPayloadsEqual(UnionTypeInfo oldUnion, UnionTypeInfo newUnion)
        => string.Equals(oldUnion.TypeName, newUnion.TypeName, StringComparison.Ordinal)
        && string.Equals(oldUnion.Kind, newUnion.Kind, StringComparison.Ordinal)
        && oldUnion.ImplementsIUnion == newUnion.ImplementsIUnion
        && oldUnion.CaseTypes
            .Order(StringComparer.Ordinal)
            .SequenceEqual(
                newUnion.CaseTypes.Order(StringComparer.Ordinal),
                StringComparer.Ordinal);
}
