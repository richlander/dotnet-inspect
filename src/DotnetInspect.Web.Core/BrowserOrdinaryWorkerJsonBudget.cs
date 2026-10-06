using System.Text.Json;

namespace DotnetInspect.Web;

internal static class BrowserOrdinaryWorkerJsonBudget
{
    internal const int MaxOrdinaryWorkerJsonCharacters = 16_777_216;
    internal const int MaxOrdinaryWorkerCollectionEntries = 524_288;
    internal const int OrdinaryWorkerResultTupleOverhead = 2;

    internal static long CollectionEntries(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => 1 + value.EnumerateObject()
            .Sum(property => 1 + CollectionEntries(property.Value)),
        JsonValueKind.Array => 1 + value.EnumerateArray()
            .Sum(item => 1 + CollectionEntries(item)),
        _ => 0,
    };

    internal static long JsonStringifyCharacters(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                long count = 2;
                bool first = true;
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!first)
                        count++;
                    first = false;
                    count += JsonStringifyStringCharacters(property.Name);
                    count++;
                    count += JsonStringifyCharacters(property.Value);
                }
                return count;
            }
            case JsonValueKind.Array:
            {
                long count = 2;
                bool first = true;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (!first)
                        count++;
                    first = false;
                    count += JsonStringifyCharacters(item);
                }
                return count;
            }
            case JsonValueKind.String:
                return JsonStringifyStringCharacters(
                    element.GetString()
                        ?? throw new InvalidOperationException(
                            "A JSON string had no value."));
            case JsonValueKind.Number:
                return element.GetRawText().Length;
            case JsonValueKind.True:
                return 4;
            case JsonValueKind.False:
                return 5;
            case JsonValueKind.Null:
                return 4;
            default:
                throw new InvalidOperationException(
                    $"Unsupported JSON value kind {element.ValueKind}.");
        }
    }

    static long JsonStringifyStringCharacters(string value)
    {
        long count = 2;
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current is '"' or '\\' or '\b' or '\f' or '\n' or '\r' or '\t')
            {
                count += 2;
            }
            else if (current <= '\u001f')
            {
                count += 6;
            }
            else if (char.IsHighSurrogate(current))
            {
                if (index + 1 < value.Length
                    && char.IsLowSurrogate(value[index + 1]))
                {
                    count += 2;
                    index++;
                }
                else
                {
                    count += 6;
                }
            }
            else if (char.IsLowSurrogate(current))
            {
                count += 6;
            }
            else
            {
                count++;
            }
        }
        return count;
    }
}
