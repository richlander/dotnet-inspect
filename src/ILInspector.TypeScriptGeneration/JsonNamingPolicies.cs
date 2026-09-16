using System.Text.Json;

namespace ILInspector.TypeScriptGeneration;

internal static class JsonNamingPolicies
{
    public static string SnakeCaseLower(string name) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

    public static string SnakeCaseUpper(string name) =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(name);

    public static string KebabCaseLower(string name) =>
        JsonNamingPolicy.KebabCaseLower.ConvertName(name);

    public static string KebabCaseUpper(string name) =>
        JsonNamingPolicy.KebabCaseUpper.ConvertName(name);
}
