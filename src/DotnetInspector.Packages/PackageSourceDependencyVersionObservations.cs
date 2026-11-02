using System.Runtime.CompilerServices;
using NuGetFetch;

namespace DotnetInspector.Packages;

internal sealed class PackageSourceDependencyVersionObservations :
    PackageSourceDependencyVersionObservations<
        PackageSourceOperationResult<PackageVersionResult>>
{
}

internal class PackageSourceDependencyVersionObservations<TValue>
    where TValue : class
{
    private readonly object _gate = new();
    private readonly Dictionary<ObservationKey, Observation> _observations =
        new(new ObservationKeyComparer());

    internal Observation Join(
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity source,
        string packageId,
        Func<Task<TValue>> acquire,
        out bool reused)
    {
        var key = new ObservationKey(authority, source, packageId);
        lock (_gate)
        {
            if (_observations.TryGetValue(key, out Observation? observation))
            {
                reused = true;
            }
            else
            {
                observation = new(key, acquire);
                _observations.Add(key, observation);
                reused = false;
            }

            observation.Participants++;
            return observation;
        }
    }

    internal void Retain(IReadOnlyList<Observation> observations)
    {
        lock (_gate)
        {
            foreach (Observation observation in observations)
                observation.Retained = true;
        }
    }

    internal void Release(IReadOnlyList<Observation> observations)
    {
        lock (_gate)
        {
            foreach (Observation observation in observations)
            {
                observation.Participants--;
                if (observation.Participants == 0 && !observation.Retained)
                    _observations.Remove(observation.Key);
            }
        }
    }

    internal void Clear()
    {
        lock (_gate)
            _observations.Clear();
    }

    internal readonly record struct ObservationKey(
        ConfiguredPackageAuthority Authority,
        PackageSourceResultIdentity Source,
        string PackageId);

    private sealed class ObservationKeyComparer : IEqualityComparer<ObservationKey>
    {
        public bool Equals(ObservationKey x, ObservationKey y) =>
            ReferenceEquals(x.Authority, y.Authority)
            && ReferenceEquals(x.Source, y.Source)
            && StringComparer.OrdinalIgnoreCase.Equals(x.PackageId, y.PackageId);

        public int GetHashCode(ObservationKey key) =>
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(key.Authority),
                RuntimeHelpers.GetHashCode(key.Source),
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.PackageId));
    }

    internal sealed class Observation(
        ObservationKey key,
        Func<Task<TValue>> acquire)
    {
        internal ObservationKey Key { get; } = key;
        internal Lazy<Task<TValue>> Work { get; } =
            new(acquire, LazyThreadSafetyMode.ExecutionAndPublication);
        internal int Participants { get; set; }
        internal bool Retained { get; set; }
    }
}
