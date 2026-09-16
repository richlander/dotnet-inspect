using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.Options;

internal sealed record PackageChangesOptions
{
    internal required string Ecosystem { get; init; }
    internal DateTimeOffset? FromExclusive { get; init; }
    internal DateTimeOffset? ThroughInclusive { get; init; }
    internal bool SecurityOnly { get; init; }
    internal int MaximumRows { get; init; }
    internal OutputFormat Format { get; init; }
    internal bool CompactJson { get; init; }
    internal bool Verbose { get; init; }
}
