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
        MetadataVisibilityClassification visibility =
            MetadataVisibility.Classify(
                reader,
                limits.MaximumTypeDefinitions);
        int visitedTypes =
            visibility.VisitedTypeDefinitions;
        int visitedMethods = 0;
        if (!visibility.IsComplete)
        {
            return Result(
                PublicMethodRootInventoryLimit.TypeDefinitions,
                limits.MaximumTypeDefinitions);
        }

        int methodDefinitionCount =
            reader.GetTableRowCount(TableIndex.MethodDef);
        int methodPointerCount =
            reader.GetTableRowCount(TableIndex.MethodPtr);
        if (methodPointerCount != 0
            && methodPointerCount != methodDefinitionCount)
        {
            throw new BadImageFormatException(
                "The MethodPtr table is not a permutation of "
                    + "the MethodDef table.");
        }

        if (methodDefinitionCount
            > limits.MaximumMethodDefinitions)
        {
            var observedMethods = new HashSet<int>();
            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                MethodDefinitionHandleCollection methods =
                    reader.GetTypeDefinition(typeHandle)
                        .GetMethods();
                if (methods.Count < 0)
                {
                    throw new BadImageFormatException(
                        "The TypeDef MethodList column is not "
                            + "a non-decreasing range.");
                }

                foreach (MethodDefinitionHandle methodHandle
                    in methods)
                {
                    int methodRow = ValidateMethodRow(
                        methodHandle,
                        methodDefinitionCount);
                    if (observedMethods.Contains(methodRow))
                    {
                        throw new BadImageFormatException(
                            "A MethodDef has more than one "
                                + "declaring TypeDef.");
                    }

                    if (visitedMethods
                        == limits.MaximumMethodDefinitions)
                    {
                        return Result(
                            PublicMethodRootInventoryLimit
                                .MethodDefinitions,
                            limits.MaximumMethodDefinitions);
                    }

                    visitedMethods++;
                    observedMethods.Add(methodRow);
                }
            }

            throw new BadImageFormatException(
                "The TypeDef method ranges do not cover the "
                    + "MethodDef table.");
        }

        var declaringTypeRows =
            new int[methodDefinitionCount + 1];
        var publicMethods =
            new bool[methodDefinitionCount + 1];
        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            int typeRow =
                MetadataTokens.GetRowNumber(typeHandle);
            MethodDefinitionHandleCollection methods =
                reader.GetTypeDefinition(typeHandle)
                    .GetMethods();
            if (methods.Count < 0)
            {
                throw new BadImageFormatException(
                    "The TypeDef MethodList column is not a "
                        + "non-decreasing range.");
            }

            foreach (MethodDefinitionHandle methodHandle
                in methods)
            {
                visitedMethods++;
                int methodRow = ValidateMethodRow(
                    methodHandle,
                    methodDefinitionCount);
                if (declaringTypeRows[methodRow] != 0)
                {
                    throw new BadImageFormatException(
                        "A MethodDef has more than one "
                            + "declaring TypeDef.");
                }

                declaringTypeRows[methodRow] = typeRow;
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                publicMethods[methodRow] =
                    (method.Attributes
                        & MethodAttributes.MemberAccessMask)
                    == MethodAttributes.Public;
            }
        }

        if (visitedMethods != methodDefinitionCount)
        {
            throw new BadImageFormatException(
                "The TypeDef method ranges do not cover the "
                    + "MethodDef table.");
        }

        for (int methodRow = 1;
            methodRow <= methodDefinitionCount;
            methodRow++)
        {
            int declaringTypeRow =
                declaringTypeRows[methodRow];
            if (declaringTypeRow == 0)
            {
                throw new BadImageFormatException(
                    "A MethodDef has no declaring TypeDef.");
            }

            if (!visibility.ExternallyVisibleTypes[
                    declaringTypeRow]
                || !publicMethods[methodRow])
            {
                continue;
            }

            if (roots.Count == limits.MaximumRoots)
            {
                return Result(
                    PublicMethodRootInventoryLimit.Roots,
                    limits.MaximumRoots);
            }

            MethodDefinitionHandle methodHandle =
                MetadataTokens.MethodDefinitionHandle(methodRow);
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

        static int ValidateMethodRow(
            MethodDefinitionHandle handle,
            int methodDefinitionCount)
        {
            int row = MetadataTokens.GetRowNumber(handle);
            if (row < 1 || row > methodDefinitionCount)
            {
                throw new BadImageFormatException(
                    "A TypeDef projects an invalid MethodDef.");
            }

            return row;
        }
    }
}
