using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Research;

public sealed record AppContextSwitchOccurrence(
    string Switch,
    string Api);

/// <summary>
/// Finds AppContext switch calls by composing Metadata method bodies with
/// decoded Instructions without exposing either layer's readers.
/// </summary>
public static class AppContextSwitchProjectionProducer
{
    public static IReadOnlyList<AppContextSwitchOccurrence> Produce(MethodBodySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<AppContextSwitchOccurrence> switches = [];

        foreach (var method in source.EnumerateMethods())
        {
            if (!method.HasBody
                || !source.TryRead(method.MetadataToken, out var body, out _))
            {
                continue;
            }

            var instructions = MethodInstructions.Decode(body!);
            if (!instructions.IsComplete)
            {
                throw new BadImageFormatException(
                    instructions.Blocks.IncompleteReason ?? "IL body decode failed.");
            }

            string? lastString = null;
            foreach (var instruction in instructions.Instructions)
            {
                if (instruction.OpCode == ILOpCode.Ldstr
                    && instruction.Operand == OperandKind.InlineString)
                {
                    lastString = source.ResolveUserString((int)instruction.OperandValue);
                    continue;
                }

                if (instruction.OpCode is not (ILOpCode.Call or ILOpCode.Callvirt)
                    || instruction.Operand != OperandKind.InlineMethod
                    || lastString is not { Length: > 0 } switchName
                    || source.ResolveMethodIdentity((int)instruction.OperandValue)
                        is not { DeclaringType: "System.AppContext", Name: "TryGetSwitch" or "SetSwitch" })
                {
                    continue;
                }

                switches.Add(new AppContextSwitchOccurrence(
                    switchName,
                    $"{TypeResolver.FormatDisplayName(method.DeclaringType)}.{method.Name}(...)"));
            }
        }

        return switches;
    }

    /// <summary>
    /// Projects raw occurrences into the distinct AppContext switches reported
    /// by library inspection.
    /// </summary>
    public static ImmutableArray<AppContextSwitchOccurrence> ProduceInventory(
        MethodBodySource source)
        => Produce(source)
            .Where(static occurrence => IsReportable(occurrence.Switch))
            .Distinct()
            .OrderBy(static occurrence => occurrence.Switch, StringComparer.Ordinal)
            .ThenBy(static occurrence => occurrence.Api, StringComparer.Ordinal)
            .ToImmutableArray();

    private static bool IsReportable(string switchName)
        => !switchName.StartsWith(
                "System.Resources.UseSystemResourceKeys",
                StringComparison.Ordinal)
            && !switchName.StartsWith("TestSwitch.", StringComparison.Ordinal)
            && !switchName.StartsWith("Switch.", StringComparison.Ordinal);
}
