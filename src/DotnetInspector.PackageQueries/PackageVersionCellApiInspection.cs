using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

/// <summary>Explicit bounded API evidence for one metadata Type name in a cell.</summary>
public sealed class PackageVersionCellApiInspectionRequest
{
    public PackageVersionCellApiInspectionRequest(
        string typeFullName,
        ApiSurfaceProjectionLimits limits,
        ApiSurfaceScope scope = ApiSurfaceScope.Public)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeFullName);
        ArgumentNullException.ThrowIfNull(limits);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        TypeFullName = typeFullName;
        Limits = limits;
        Scope = scope;
    }

    public string TypeFullName { get; }
    public ApiSurfaceProjectionLimits Limits { get; }
    public ApiSurfaceScope Scope { get; }
}

/// <summary>Native API censuses associated with the exact projected participant.</summary>
public sealed class PackageVersionCellApiFindingSet
{
    internal PackageVersionCellApiFindingSet(
        AssemblyContextEntry<AssemblyApiSurface>.Available assembly,
        string typeFullName)
    {
        Assembly = assembly;
        var subject = new FindingSubject(
            $"api.type:{typeFullName}",
            typeFullName);
        ApiSurface surface = assembly.Value.Surface;
        Type = MetadataFindings.InspectApiType(surface, subject, typeFullName);
        Members = MetadataFindings.InspectApiMembers(surface, subject, typeFullName);
        Attributes = MetadataFindings.InspectApiAttributes(surface, subject, typeFullName);
    }

    public AssemblyContextEntry<AssemblyApiSurface>.Available Assembly { get; }
    public FindingInspection<ApiTypeHandle> Type { get; }
    public FindingInspection<ApiMemberHandle> Members { get; }
    public FindingInspection<ApiAttributeHandle> Attributes { get; }
}

/// <summary>
/// Detached API projections and native censuses, retaining unavailable participants and bounds.
/// </summary>
public sealed class PackageVersionCellApiInspectionResult
{
    internal PackageVersionCellApiInspectionResult(
        PackageVersionCellApiInspectionRequest request,
        AssemblyContextApiSurfaceResult surfaces,
        CancellationToken cancellationToken)
    {
        TypeFullName = request.TypeFullName;
        Scope = request.Scope;
        Surfaces = surfaces;
        var findings = ImmutableArray.CreateBuilder<PackageVersionCellApiFindingSet>();
        foreach (var entry in surfaces.Assemblies.Assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is AssemblyContextEntry<AssemblyApiSurface>.Available available)
                findings.Add(new(available, request.TypeFullName));
        }
        cancellationToken.ThrowIfCancellationRequested();
        Findings = findings.ToImmutable();
    }

    public string TypeFullName { get; }
    public ApiSurfaceScope Scope { get; }
    public AssemblyContextApiSurfaceResult Surfaces { get; }
    public ImmutableArray<PackageVersionCellApiFindingSet> Findings { get; }
}
