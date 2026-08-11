using System.Runtime.Versioning;
using System.Text.Json;
using Pipeline = ILInspector.Decompiler.Pipeline;

namespace InspectWeb.Engine;

/// <summary>
/// Turns the client's selected style option ids into <see cref="Pipeline.PrinterOptions"/> using
/// the library-owned <see cref="Pipeline.StyleOptionCatalog"/>.
/// </summary>
/// <remarks>
/// The ids are exactly the ones <c>ListStyleOptions</c> handed the client: a descriptor id for a
/// two-state knob, and <c>descriptorId:valueToken</c> for a multi-value axis. Resolving them here
/// is a lookup in the catalog, not a second taxonomy — the host holds no knowledge of which knobs
/// exist, what they mean, or which value is the default. An id the catalog does not know is a
/// visible failure rather than a silently ignored selection.
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserStyleOptions
{
    internal static Pipeline.PrinterOptions Resolve(string? styleOptionsJson)
    {
        Pipeline.PrinterOptions options = Pipeline.StyleOptionCatalog.DefaultOptions;
        if (string.IsNullOrWhiteSpace(styleOptionsJson))
            return options;

        string[] selected = JsonSerializer.Deserialize(
            styleOptionsJson,
            BrowserJsonContext.Default.StringArray) ?? [];
        foreach (string id in selected)
        {
            int separator = id.IndexOf(':', StringComparison.Ordinal);
            string descriptorId = separator < 0 ? id : id[..separator];
            Pipeline.StyleOptionDescriptor descriptor =
                Pipeline.StyleOptionCatalog.Options.FirstOrDefault(
                    candidate => candidate.Id.Equals(descriptorId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"'{id}' is not a style option in the product's style catalog.");

            string token = separator < 0
                ? NonDefaultToken(descriptor, id)
                : id[(separator + 1)..];
            if (!descriptor.Values.Any(
                    value => value.Token.Equals(token, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"'{token}' is not a value of the '{descriptor.Id}' style option.");
            }

            options = descriptor.WithValue(options, token);
        }

        return options;
    }

    static string NonDefaultToken(Pipeline.StyleOptionDescriptor descriptor, string id)
    {
        Pipeline.StyleOptionValue[] choices =
        [
            .. descriptor.Values.Where(value =>
                !value.Token.Equals(descriptor.DefaultValue, StringComparison.Ordinal)),
        ];
        return choices.Length == 1
            ? choices[0].Token
            : throw new InvalidOperationException(
                $"'{id}' names a multi-value style axis, so it must be spelled "
                + $"'{descriptor.Id}:<value>'.");
    }
}
