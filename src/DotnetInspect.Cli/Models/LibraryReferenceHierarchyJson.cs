using System.Text.Json.Serialization;
using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.Models;

internal sealed record LibraryReferenceHierarchyJson(
    string FileName,
    string? Tfm,
    DependencyHierarchyJsonDocument ReferenceHierarchy);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LibraryReferenceHierarchyJson))]
[JsonSerializable(typeof(LibraryReferenceHierarchyJson[]))]
internal partial class LibraryReferenceHierarchyJsonContext :
    JsonSerializerContext;
