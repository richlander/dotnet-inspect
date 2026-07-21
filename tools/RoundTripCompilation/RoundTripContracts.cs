using System.Collections.Immutable;
using System.Security.Cryptography;
using ILInspector.CSharp;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.RoundTripCompilation;

public enum RoundTripScope
{
    Cluster,
    All,
}

public enum RoundTripBodyPolicy
{
    Selected,
    Full,
}

public sealed record RoundTripArtifactIdentity(
    string Path,
    string Sha256,
    string Provenance)
{
    public static RoundTripArtifactIdentity FromFile(string path, string provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance);
        string fullPath = System.IO.Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        return new RoundTripArtifactIdentity(
            fullPath,
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            provenance);
    }
}

public sealed record RoundTripModuleIdentity(string Name, Guid ModuleVersionId);

public sealed record RoundTripTarget(
    MetadataMethodAddress Method,
    MemberAnchor Anchor);

public sealed record RoundTripMethodReplacement
{
    RoundTripMethodReplacement(
        MetadataMethodAddress method,
        MemberAnchor anchor,
        CSharpBlockBody body)
    {
        Method = method;
        Anchor = anchor;
        Body = body;
    }

    public MetadataMethodAddress Method { get; }

    public MemberAnchor Anchor { get; }

    public CSharpBlockBody Body { get; }

    public static RoundTripMethodReplacement Create(
        MetadataMethodAddress method,
        MemberAnchor anchor,
        CSharpMemberBody body)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(body);
        if (method.Handle.IsNil)
            throw new ArgumentException("Replacement method must not be nil.", nameof(method));
        if (body is not CSharpBlockBody block)
        {
            throw new ArgumentException(
                $"Round-trip replacements support method block bodies, not {body.GetType().Name}.",
                nameof(body));
        }
        return new RoundTripMethodReplacement(method, anchor, block);
    }
}

public sealed record RoundTripRequest
{
    RoundTripRequest(
        RoundTripArtifactIdentity artifact,
        RoundTripModuleIdentity module,
        ImmutableArray<RoundTripTarget> targets,
        RoundTripScope scope,
        RoundTripBodyPolicy bodyPolicy,
        ImmutableArray<RoundTripMethodReplacement> replacements)
    {
        Artifact = artifact;
        Module = module;
        Targets = targets;
        Scope = scope;
        BodyPolicy = bodyPolicy;
        Replacements = replacements;
    }

    public RoundTripArtifactIdentity Artifact { get; }

    public RoundTripModuleIdentity Module { get; }

    public ImmutableArray<RoundTripTarget> Targets { get; }

    public RoundTripScope Scope { get; }

    public RoundTripBodyPolicy BodyPolicy { get; }

    public ImmutableArray<RoundTripMethodReplacement> Replacements { get; }

    public static RoundTripRequest Create(
        RoundTripArtifactIdentity artifact,
        RoundTripModuleIdentity module,
        IEnumerable<RoundTripTarget> targets,
        RoundTripScope scope,
        RoundTripBodyPolicy bodyPolicy,
        IEnumerable<RoundTripMethodReplacement>? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(targets);
        var targetArray = targets.ToImmutableArray();
        if (targetArray.Length == 0)
            throw new ArgumentException("At least one round-trip target is required.", nameof(targets));
        if (targetArray.Any(target => target.Method.ModuleVersionId != module.ModuleVersionId))
            throw new ArgumentException("Every target must belong to the requested module.", nameof(targets));
        if (targetArray.Select(target => target.Method).Distinct().Count() != targetArray.Length)
            throw new ArgumentException("Round-trip targets must be unique.", nameof(targets));

        var replacementArray = replacements?.ToImmutableArray() ?? [];
        var targetMethods = targetArray.Select(target => target.Method).ToHashSet();
        if (replacementArray.Any(replacement => !targetMethods.Contains(replacement.Method)))
            throw new ArgumentException("Every replacement must address a requested target.", nameof(replacements));
        if (replacementArray.Select(replacement => replacement.Method).Distinct().Count() != replacementArray.Length)
            throw new ArgumentException("A target can have at most one replacement.", nameof(replacements));

        return new RoundTripRequest(
            artifact,
            module,
            targetArray,
            scope,
            bodyPolicy,
            replacementArray);
    }
}
