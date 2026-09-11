using System.Collections.Immutable;

using Analysis = ILInspector.Analysis;
using ILInspector.Metadata;
using DotnetInspector.Services;

namespace DotnetInspector.Queries;

/// <summary>
/// One attributed source method that contains exact calls to the other
/// participant.
/// </summary>
public sealed record AssemblyPairCallUseConsumerUseSite(
    AssemblyContextSubject Source,
    Guid SourceModuleVersionId,
    Analysis.MethodIdentity SourceMethod,
    AssemblyContextSubject Target,
    Guid TargetModuleVersionId,
    ImmutableArray<Analysis.TypeRef> TargetTypes,
    ImmutableArray<Analysis.MethodIdentity> TargetMethods,
    ImmutableArray<int> OccurrenceIndexes)
{
    public int CallSiteCount => OccurrenceIndexes.Length;
}

/// <summary>
/// One structured target declaring type selected by exact calls from the other
/// participant.
/// </summary>
public sealed record AssemblyPairCallUseProviderApiType(
    AssemblyContextSubject Source,
    Guid SourceModuleVersionId,
    AssemblyContextSubject Target,
    Guid TargetModuleVersionId,
    Analysis.TypeRef TargetType,
    ImmutableArray<Analysis.MethodIdentity> SourceMethods,
    ImmutableArray<Analysis.MethodIdentity> TargetMethods,
    ImmutableArray<int> OccurrenceIndexes)
{
    public int CallSiteCount => OccurrenceIndexes.Length;
}

/// <summary>
/// Deterministic direct-use summaries whose occurrence indexes address the
/// original exact pair result.
/// </summary>
public sealed record AssemblyPairCallUseProjection(
    AssemblyPairCallUseResult Pair,
    ImmutableArray<AssemblyPairCallUseConsumerUseSite> ConsumerUseSites,
    ImmutableArray<AssemblyPairCallUseProviderApiType> ProviderApiTypes)
{
    public bool IsComplete => Pair.IsComplete;

    public static AssemblyPairCallUseProjection Create(
        AssemblyPairCallUseResult pair)
    {
        ArgumentNullException.ThrowIfNull(pair);

        var consumerByKey =
            new Dictionary<ConsumerUseSiteKey, ConsumerUseSiteBuilder>();
        var consumerBuilders = new List<ConsumerUseSiteBuilder>();
        var providerByKey =
            new Dictionary<ProviderApiTypeKey, ProviderApiTypeBuilder>();
        var providerBuilders = new List<ProviderApiTypeBuilder>();

        for (int index = 0; index < pair.Occurrences.Length; index++)
        {
            AssemblyPairCallUseOccurrence occurrence =
                pair.Occurrences[index];
            var consumerKey = new ConsumerUseSiteKey(
                occurrence.Source.Registration,
                occurrence.SourceModuleVersionId,
                occurrence.SourceMethod.MetadataToken,
                occurrence.Target.Registration,
                occurrence.TargetModuleVersionId);
            if (!consumerByKey.TryGetValue(
                    consumerKey,
                    out ConsumerUseSiteBuilder? consumer))
            {
                consumer = new ConsumerUseSiteBuilder(occurrence);
                consumerByKey.Add(consumerKey, consumer);
                consumerBuilders.Add(consumer);
            }
            consumer.Add(index, occurrence);

            var providerKey = new ProviderApiTypeKey(
                occurrence.Source.Registration,
                occurrence.SourceModuleVersionId,
                occurrence.Target.Registration,
                occurrence.TargetModuleVersionId,
                occurrence.TargetMethod.DeclaringType);
            if (!providerByKey.TryGetValue(
                    providerKey,
                    out ProviderApiTypeBuilder? provider))
            {
                provider = new ProviderApiTypeBuilder(occurrence);
                providerByKey.Add(providerKey, provider);
                providerBuilders.Add(provider);
            }
            provider.Add(index, occurrence);
        }

        return new AssemblyPairCallUseProjection(
            pair,
            [.. consumerBuilders.Select(static builder => builder.Build())],
            [.. providerBuilders.Select(static builder => builder.Build())]);
    }

    readonly record struct ConsumerUseSiteKey(
        AssemblyAcquisitionRegistration Source,
        Guid SourceModuleVersionId,
        int SourceMethodToken,
        AssemblyAcquisitionRegistration Target,
        Guid TargetModuleVersionId);

    readonly record struct ProviderApiTypeKey(
        AssemblyAcquisitionRegistration Source,
        Guid SourceModuleVersionId,
        AssemblyAcquisitionRegistration Target,
        Guid TargetModuleVersionId,
        Analysis.TypeRef TargetType);

    sealed class ConsumerUseSiteBuilder
    {
        readonly AssemblyContextSubject _source;
        readonly Guid _sourceModuleVersionId;
        readonly Analysis.MethodIdentity _sourceMethod;
        readonly AssemblyContextSubject _target;
        readonly Guid _targetModuleVersionId;
        readonly List<Analysis.TypeRef> _targetTypes = [];
        readonly HashSet<Analysis.TypeRef> _targetTypeSet = [];
        readonly List<Analysis.MethodIdentity> _targetMethods = [];
        readonly HashSet<Analysis.MethodIdentity> _targetMethodSet = [];
        readonly List<int> _occurrenceIndexes = [];

        internal ConsumerUseSiteBuilder(
            AssemblyPairCallUseOccurrence occurrence)
        {
            _source = occurrence.Source;
            _sourceModuleVersionId =
                occurrence.SourceModuleVersionId;
            _sourceMethod = occurrence.SourceMethod;
            _target = occurrence.Target;
            _targetModuleVersionId =
                occurrence.TargetModuleVersionId;
        }

        internal void Add(
            int index,
            AssemblyPairCallUseOccurrence occurrence)
        {
            Analysis.TypeRef targetType =
                occurrence.TargetMethod.DeclaringType;
            if (_targetTypeSet.Add(targetType))
                _targetTypes.Add(targetType);
            if (_targetMethodSet.Add(occurrence.TargetMethod))
                _targetMethods.Add(occurrence.TargetMethod);
            _occurrenceIndexes.Add(index);
        }

        internal AssemblyPairCallUseConsumerUseSite Build() =>
            new(
                _source,
                _sourceModuleVersionId,
                _sourceMethod,
                _target,
                _targetModuleVersionId,
                [.. _targetTypes],
                [.. _targetMethods],
                [.. _occurrenceIndexes]);
    }

    sealed class ProviderApiTypeBuilder
    {
        readonly AssemblyContextSubject _source;
        readonly Guid _sourceModuleVersionId;
        readonly AssemblyContextSubject _target;
        readonly Guid _targetModuleVersionId;
        readonly Analysis.TypeRef _targetType;
        readonly List<Analysis.MethodIdentity> _sourceMethods = [];
        readonly HashSet<Analysis.MethodIdentity> _sourceMethodSet = [];
        readonly List<Analysis.MethodIdentity> _targetMethods = [];
        readonly HashSet<Analysis.MethodIdentity> _targetMethodSet = [];
        readonly List<int> _occurrenceIndexes = [];

        internal ProviderApiTypeBuilder(
            AssemblyPairCallUseOccurrence occurrence)
        {
            _source = occurrence.Source;
            _sourceModuleVersionId =
                occurrence.SourceModuleVersionId;
            _target = occurrence.Target;
            _targetModuleVersionId =
                occurrence.TargetModuleVersionId;
            _targetType = occurrence.TargetMethod.DeclaringType;
        }

        internal void Add(
            int index,
            AssemblyPairCallUseOccurrence occurrence)
        {
            if (_sourceMethodSet.Add(occurrence.SourceMethod))
                _sourceMethods.Add(occurrence.SourceMethod);
            if (_targetMethodSet.Add(occurrence.TargetMethod))
                _targetMethods.Add(occurrence.TargetMethod);
            _occurrenceIndexes.Add(index);
        }

        internal AssemblyPairCallUseProviderApiType Build() =>
            new(
                _source,
                _sourceModuleVersionId,
                _target,
                _targetModuleVersionId,
                _targetType,
                [.. _sourceMethods],
                [.. _targetMethods],
                [.. _occurrenceIndexes]);
    }
}
