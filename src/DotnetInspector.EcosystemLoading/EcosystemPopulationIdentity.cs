using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.EcosystemLoading;

/// <summary>Stable application-owned identity for one population loader.</summary>
public sealed record EcosystemPopulationLoaderId
{
    const string Prefix = "ecosystem-loader.";

    EcosystemPopulationLoaderId(string value) => Value = value;

    public string Value { get; }

    public static EcosystemPopulationLoaderId Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryCreate(value, out EcosystemPopulationLoaderId? id)
            ? id
            : throw new ArgumentException(
                $"'{value}' is not a canonical Ecosystem population loader identity.",
                nameof(value));
    }

    public static bool TryCreate(
        string? value,
        [NotNullWhen(true)] out EcosystemPopulationLoaderId? id)
    {
        id = null;
        if (value is not { Length: > 0 and <= 96 }
            || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> name = value.AsSpan(Prefix.Length);
        if (name.IsEmpty || !char.IsAsciiLetterLower(name[0]))
            return false;

        bool previousWasHyphen = false;
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character))
            {
                previousWasHyphen = false;
                continue;
            }

            if (character != '-'
                || index == name.Length - 1
                || previousWasHyphen)
            {
                return false;
            }

            previousWasHyphen = true;
        }

        id = new EcosystemPopulationLoaderId(value);
        return true;
    }

    public override string ToString() => Value;
}

/// <summary>Opaque identity for one loader operation-policy value.</summary>
public sealed class EcosystemPopulationOperationPolicyIdentity
{
    EcosystemPopulationOperationPolicyIdentity(string name) => Name = name;

    public string Name { get; }

    public static EcosystemPopulationOperationPolicyIdentity Create(
        string name) =>
        new(EcosystemPopulationIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one authorized capability plan.</summary>
public sealed class EcosystemPopulationCapabilityPlanIdentity
{
    EcosystemPopulationCapabilityPlanIdentity(string name) => Name = name;

    public string Name { get; }

    public static EcosystemPopulationCapabilityPlanIdentity Create(
        string name) =>
        new(EcosystemPopulationIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one finite-work policy.</summary>
public sealed class EcosystemPopulationWorkIdentity
{
    EcosystemPopulationWorkIdentity(string name) => Name = name;

    public string Name { get; }

    public static EcosystemPopulationWorkIdentity Create(string name) =>
        new(EcosystemPopulationIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>Opaque identity for one completed-demand witness.</summary>
public sealed class EcosystemPopulationCompletionIdentity
{
    EcosystemPopulationCompletionIdentity(string name) => Name = name;

    public string Name { get; }

    public static EcosystemPopulationCompletionIdentity Create(string name) =>
        new(EcosystemPopulationIdentityName.Validate(name));

    public override string ToString() => Name;
}

/// <summary>
/// Resource-free identities for the typed values supplied to one loader.
/// </summary>
public sealed class EcosystemPopulationLoadInputSnapshot
{
    public EcosystemPopulationLoadInputSnapshot(
        EcosystemPopulationOperationPolicyIdentity operationPolicy,
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan,
        EcosystemPopulationWorkIdentity work)
    {
        ArgumentNullException.ThrowIfNull(operationPolicy);
        ArgumentNullException.ThrowIfNull(capabilityPlan);
        ArgumentNullException.ThrowIfNull(work);
        OperationPolicy = operationPolicy;
        CapabilityPlan = capabilityPlan;
        Work = work;
    }

    public EcosystemPopulationOperationPolicyIdentity OperationPolicy { get; }
    public EcosystemPopulationCapabilityPlanIdentity CapabilityPlan { get; }
    public EcosystemPopulationWorkIdentity Work { get; }
}

/// <summary>
/// Supplies one loader-specific closed composition of typed policy,
/// capabilities, and finite work.
/// </summary>
public interface IEcosystemPopulationLoadInputs
{
    EcosystemPopulationLoadInputSnapshot Snapshot { get; }
}

static class EcosystemPopulationIdentityName
{
    public static string Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
