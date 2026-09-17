using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public sealed record PublicMethodRootInventoryLimits(
    int MaximumTypeDefinitions,
    int MaximumMethodDefinitions,
    int MaximumRoots)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumTypeDefinitions,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumMethodDefinitions,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumRoots,
            1);
    }
}

public enum PublicMethodRootInventoryLimit
{
    TypeDefinitions,
    MethodDefinitions,
    Roots,
}

public sealed record PublicMethodRootInventoryBoundary(
    PublicMethodRootInventoryLimit Limit,
    int Maximum);

public sealed record PublicMethodRootInventoryReceipt(
    int VisitedTypeDefinitions,
    int VisitedMethodDefinitions,
    int RetainedRoots);

/// <summary>
/// Exact public MethodDef roots from one metadata module.
/// </summary>
public sealed record PublicMethodRootInventory(
    Guid ModuleVersionId,
    ImmutableArray<MetadataMethodAddress> Roots,
    PublicMethodRootInventoryReceipt Receipt,
    PublicMethodRootInventoryBoundary? Boundary)
{
    public bool IsComplete => Boundary is null;
}

/// <summary>
/// Reads exact public MethodDef roots without applying API presentation
/// filters.
/// </summary>
public static class PublicMethodRootInventoryReader
{
    public static PublicMethodRootInventory Read(
        MetadataReader reader,
        PublicMethodRootInventoryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        Guid moduleVersionId =
            MetadataModuleIdentity.ReadVersionId(reader);
        var roots =
            ImmutableArray.CreateBuilder<MetadataMethodAddress>();
        int visitedTypes = 0;
        int visitedMethods = 0;
        int typeCapacity = Math.Min(
            reader.TypeDefinitions.Count,
            limits.MaximumTypeDefinitions);
        var externallyVisibleTypes =
            new bool[typeCapacity + 1];

        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            if (visitedTypes == limits.MaximumTypeDefinitions)
            {
                return Result(
                    PublicMethodRootInventoryLimit.TypeDefinitions,
                    limits.MaximumTypeDefinitions);
            }

            visitedTypes++;
            externallyVisibleTypes[
                MetadataTokens.GetRowNumber(typeHandle)] =
                    MetadataVisibility.IsExternallyVisible(
                        reader,
                        typeHandle);
        }

        int methodDefinitionCount =
            reader.GetTableRowCount(TableIndex.MethodDef);
        for (int methodRow = 1;
            methodRow <= methodDefinitionCount;
            methodRow++)
        {
            MethodDefinitionHandle methodHandle =
                MetadataTokens.MethodDefinitionHandle(methodRow);
            if (visitedMethods
                == limits.MaximumMethodDefinitions)
            {
                return Result(
                    PublicMethodRootInventoryLimit
                        .MethodDefinitions,
                    limits.MaximumMethodDefinitions);
            }

            visitedMethods++;
            MethodDefinition method =
                reader.GetMethodDefinition(methodHandle);
            TypeDefinitionHandle declaringType =
                method.GetDeclaringType();
            int declaringTypeRow =
                MetadataTokens.GetRowNumber(declaringType);
            if (declaringType.IsNil
                || declaringTypeRow
                    >= externallyVisibleTypes.Length)
            {
                throw new BadImageFormatException(
                    "A MethodDef has no valid declaring TypeDef.");
            }

            if (!externallyVisibleTypes[declaringTypeRow]
                || (method.Attributes
                        & MethodAttributes.MemberAccessMask)
                    != MethodAttributes.Public)
            {
                continue;
            }

            if (roots.Count == limits.MaximumRoots)
            {
                return Result(
                    PublicMethodRootInventoryLimit.Roots,
                    limits.MaximumRoots);
            }

            roots.Add(
                new MetadataMethodAddress(
                    moduleVersionId,
                    methodHandle));
        }

        return new PublicMethodRootInventory(
            moduleVersionId,
            roots.ToImmutable(),
            new(
                visitedTypes,
                visitedMethods,
                roots.Count),
            Boundary: null);

        PublicMethodRootInventory Result(
            PublicMethodRootInventoryLimit limit,
            int maximum) =>
            new(
                moduleVersionId,
                roots.ToImmutable(),
                new(
                    visitedTypes,
                    visitedMethods,
                    roots.Count),
                new(limit, maximum));
    }
}
