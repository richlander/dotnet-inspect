using System.Collections.Immutable;

using InertText;

namespace ILInspector.Analysis;

public static class ArrayPoolResourceEffectModel
{
    public static ResourceEffectModelIdentity Identity { get; } =
        new("dotnet.framework.array-pool");

    public static ResourceKindIdentity BufferKind { get; } =
        new("dotnet.array-pool.buffer");

    public static ResourceEffectAdmission Create()
    {
        ResourceEffectModelDefinition definition = Definition();
        return ResourceEffectAdmissionBuilder.Admit([definition])
            is ResourceEffectAdmissionOutcome.Admitted admitted
                ? admitted.Admission
                : throw new InvalidOperationException(
                    "The product-shipped ArrayPool resource-effect model "
                    + "failed its local admission contract.");
    }

    public static ResourceEffectModelDefinition Definition()
    {
        ResourceEffectGenericVariable element =
            new(ResourceEffectGenericVariableKind.Type, 0);
        ResourceTypeExpression.Variable elementType = new(element);
        ResourceTypeExpression.Named arrayPool = new(
            FrameworkAssembly(),
            "System.Buffers",
            [new ResourceTypeNameSegment("ArrayPool", 1)],
            [elementType]);
        ResourceTypeExpression int32 = CoreLibraryType(
            "System",
            "Int32");
        ResourceTypeExpression boolean = CoreLibraryType(
            "System",
            "Boolean");
        ResourceTypeExpression voidType = CoreLibraryType(
            "System",
            "Void");
        ResourceTypeExpression array =
            new ResourceTypeExpression.SzArray(elementType);
        ResourceKindReference buffer =
            new(BufferKind, [element]);

        return new ResourceEffectModelDefinition(
            ResourceEffectLanguageIdentity.Version1,
            Identity,
            [
                new ResourceKindDefinition(
                    BufferKind,
                    arity: 1,
                    [Provenance("resource", 0)]),
            ],
            [],
            [
                new ResourceEffectTypedDeclaration(
                    Member(
                        arrayPool,
                        "get_Shared",
                        ResourceEffectMemberKind.PropertyGetter,
                        isStatic: true,
                        parameters: [],
                        returnType: arrayPool),
                    new ResourceEffect.Authority(
                        buffer,
                        new ResourceEffectLocation.Return(),
                        new ResourceAuthorityKey.Singleton([element])),
                    [Provenance("shared", 1)]),
                new ResourceEffectTypedDeclaration(
                    Member(
                        arrayPool,
                        "Rent",
                        ResourceEffectMemberKind.Method,
                        isStatic: false,
                        parameters: [Parameter(int32)],
                        returnType: array),
                    new ResourceEffect.Acquire(
                        buffer,
                        new ResourceEffectLocation.Return(),
                        new ResourceEffectCompletion.NormalReturn(),
                        new ResourceEffectLocation.Receiver(),
                        Lender: null),
                    [Provenance("rent", 2)]),
                new ResourceEffectTypedDeclaration(
                    Member(
                        arrayPool,
                        "Return",
                        ResourceEffectMemberKind.Method,
                        isStatic: false,
                        parameters: [Parameter(array)],
                        returnType: voidType),
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectCompletion.NormalReturn(),
                        buffer,
                        new ResourceEffectLocation.Receiver(),
                        Observation: null),
                    [Provenance("return", 3)]),
                new ResourceEffectTypedDeclaration(
                    Member(
                        arrayPool,
                        "Return",
                        ResourceEffectMemberKind.Method,
                        isStatic: false,
                        parameters:
                        [
                            Parameter(array),
                            Parameter(boolean),
                        ],
                        returnType: voidType),
                    new ResourceEffect.Release(
                        new ResourceEffectLocation.Parameter(0),
                        new ResourceEffectCompletion.NormalReturn(),
                        buffer,
                        new ResourceEffectLocation.Receiver(),
                        Observation: null),
                    [Provenance("return-clear", 4)]),
            ]);
    }

    static ResourceEffectTargetSelector Member(
        ResourceTypeExpression.Named declaringType,
        string name,
        ResourceEffectMemberKind kind,
        bool isStatic,
        ImmutableArray<ResourceEffectParameterSelector> parameters,
        ResourceTypeExpression returnType) =>
        new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                declaringType,
                name,
                kind,
                isStatic,
                genericArity: 0,
                ResourceEffectCallingConvention.Default,
                hasThis: !isStatic,
                explicitThis: false,
                parameters,
                returnType));

    static ResourceEffectParameterSelector Parameter(
        ResourceTypeExpression type) =>
        new(type, ResourceEffectRefKind.Value);

    static ResourceTypeExpression.Named CoreLibraryType(
        string @namespace,
        string name) =>
        new(
            new ResourceAssemblySelector(
                "System.Runtime",
                "b03f5f7f11d50a3a",
                ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            @namespace,
            [new ResourceTypeNameSegment(name, 0)]);

    static ResourceAssemblySelector FrameworkAssembly() =>
        new(
            "System.Buffers",
            "cc7b13ffcd2ddd51",
            ResourceAssemblyVersionPolicy.Any,
            allowCoreLibraryFacade: true);

    static ResourceDeclarationProvenance Provenance(
        string source,
        int ordinal) =>
        new(
            Identity,
            ResourceDeclarationAuthority.ProductShipped,
            new InertString(
                TextPolicy.Field,
                $"dotnet-inspect.array-pool.{source}"),
            ordinal);
}
