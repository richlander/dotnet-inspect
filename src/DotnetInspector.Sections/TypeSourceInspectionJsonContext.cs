using System.Text.Json.Serialization;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TypeSourcePdbAcquisitionEvidence))]
public sealed partial class TypeSourceInspectionJsonContext
    : JsonSerializerContext;
