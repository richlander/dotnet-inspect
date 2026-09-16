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
        ResourceKindReference buffer = new(BufferKind, [element]);
        var declarations =
            ImmutableArray.CreateBuilder<ResourceEffectTypedDeclaration>();

        declarations.Add(
                new(
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
                    [Provenance("shared", 1)]));
        declarations.Add(
                new(
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
                    [Provenance("rent", 2)]));
        declarations.Add(
                new(
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
                    [Provenance("return", 3)]));
        declarations.Add(
                new(
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
                    [Provenance("return-clear", 4)]));
        AddWrapperDeclarations(
            declarations,
            elementType,
            array,
            int32,
            voidType);

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
            declarations.ToImmutable());
    }

    static void AddWrapperDeclarations(
        ImmutableArray<ResourceEffectTypedDeclaration>.Builder declarations,
        ResourceTypeExpression elementType,
        ResourceTypeExpression array,
        ResourceTypeExpression int32,
        ResourceTypeExpression voidType)
    {
        ResourceTypeExpression.Named span = CoreLibraryType(
            "System",
            "Span",
            [elementType]);
        ResourceTypeExpression.Named readOnlySpan = CoreLibraryType(
            "System",
            "ReadOnlySpan",
            [elementType]);
        ResourceTypeExpression.Named memory = CoreLibraryType(
            "System",
            "Memory",
            [elementType]);
        ResourceTypeExpression.Named readOnlyMemory = CoreLibraryType(
            "System",
            "ReadOnlyMemory",
            [elementType]);
        int ordinal = 10;

        AddArrayConstructor(span, throws: ResourceOperationThrows.Never);
        AddArrayConstructor(
            span,
            [int32, int32],
            ResourceOperationThrows.Possible);
        AddArrayConstructor(
            readOnlySpan,
            throws: ResourceOperationThrows.Never);
        AddArrayConstructor(
            readOnlySpan,
            [int32, int32],
            ResourceOperationThrows.Possible);
        AddArrayConstructor(memory, throws: ResourceOperationThrows.Never);
        AddArrayConstructor(
            memory,
            [int32, int32],
            ResourceOperationThrows.Possible);
        AddArrayConstructor(
            readOnlyMemory,
            throws: ResourceOperationThrows.Never);
        AddArrayConstructor(
            readOnlyMemory,
            [int32, int32],
            ResourceOperationThrows.Possible);

        AddArrayConversion(span);
        AddArrayConversion(readOnlySpan);
        AddArrayConversion(memory);
        AddArrayConversion(readOnlyMemory);
        AddConversion(span, readOnlySpan);
        AddConversion(memory, readOnlyMemory);

        AddSlices(span);
        AddSlices(readOnlySpan);
        AddSlices(memory);
        AddSlices(readOnlyMemory);
        AddView(memory, span);
        AddView(readOnlyMemory, readOnlySpan);

        ResourceEffectGenericVariable methodElement =
            new(ResourceEffectGenericVariableKind.Method, 0);
        ResourceTypeExpression.Variable methodElementType =
            new(methodElement);
        ResourceTypeExpression methodArray =
            new ResourceTypeExpression.SzArray(methodElementType);
        ResourceTypeExpression.Named methodSpan = CoreLibraryType(
            "System",
            "Span",
            [methodElementType]);
        ResourceTypeExpression.Named methodMemory = CoreLibraryType(
            "System",
            "Memory",
            [methodElementType]);
        ResourceTypeExpression.Named extensions = new(
            MemoryAssembly(),
            "System",
            [new ResourceTypeNameSegment("MemoryExtensions", 0)]);
        ResourceTypeExpression.Named index = CoreLibraryType(
            "System",
            "Index");
        ResourceTypeExpression.Named range = CoreLibraryType(
            "System",
            "Range");
        AddArrayViewExtension(
            extensions,
            "AsSpan",
            methodArray,
            methodSpan,
            [],
            ResourceOperationThrows.Never);
        AddArrayViewExtension(
            extensions,
            "AsSpan",
            methodArray,
            methodSpan,
            [int32],
            ResourceOperationThrows.Possible);
        AddArrayViewExtension(
            extensions,
            "AsSpan",
            methodArray,
            methodSpan,
            [index],
            ResourceOperationThrows.Possible);
        AddArrayViewExtension(
            extensions,
            "AsSpan",
            methodArray,
            methodSpan,
            [range],
            ResourceOperationThrows.Possible);
        AddArrayViewExtension(
            extensions,
            "AsSpan",
            methodArray,
            methodSpan,
            [int32, int32],
            ResourceOperationThrows.Possible);
        AddArrayViewExtension(
            extensions,
            "AsMemory",
            methodArray,
            methodMemory,
            [],
            ResourceOperationThrows.Never);
        AddArrayViewExtension(
            extensions,
            "AsMemory",
            methodArray,
            methodMemory,
            [int32],
            ResourceOperationThrows.Possible);
        AddArrayViewExtension(
            extensions,
            "AsMemory",
            methodArray,
            methodMemory,
            [index],
            ResourceOperationThrows.Possible);
        AddArrayViewExtension(
            extensions,
            "AsMemory",
            methodArray,
            methodMemory,
            [range],
            ResourceOperationThrows.Possible);
        AddArrayViewExtension(
            extensions,
            "AsMemory",
            methodArray,
            methodMemory,
            [int32, int32],
            ResourceOperationThrows.Possible);

        void AddArrayConstructor(
            ResourceTypeExpression.Named wrapper,
            ImmutableArray<ResourceTypeExpression> trailing = default,
            ResourceOperationThrows throws =
                ResourceOperationThrows.Possible)
        {
            trailing = trailing.IsDefault ? [] : trailing;
            ResourceEffectTargetSelector target = Member(
                wrapper,
                ".ctor",
                ResourceEffectMemberKind.Constructor,
                isStatic: false,
                parameters:
                [
                    Parameter(array),
                    .. trailing.Select(Parameter),
                ],
                returnType: voidType);
            AddDerivedOperation(
                target,
                new ResourceEffectLocation.Parameter(0),
                new ResourceEffectLocation.Constructed(),
                GuardedArray(),
                throws,
                "constructor");
        }

        void AddArrayConversion(ResourceTypeExpression.Named wrapper)
        {
            ResourceEffectTargetSelector target = Member(
                wrapper,
                "op_Implicit",
                ResourceEffectMemberKind.Method,
                isStatic: true,
                parameters: [Parameter(array)],
                returnType: wrapper);
            AddDerivedOperation(
                target,
                new ResourceEffectLocation.Parameter(0),
                new ResourceEffectLocation.Return(),
                GuardedArray(),
                ResourceOperationThrows.Never,
                "array-conversion");
        }

        void AddConversion(
            ResourceTypeExpression.Named source,
            ResourceTypeExpression.Named targetType)
        {
            ResourceEffectTargetSelector target = Member(
                source,
                "op_Implicit",
                ResourceEffectMemberKind.Method,
                isStatic: true,
                parameters: [Parameter(source)],
                returnType: targetType);
            AddDerivedOperation(
                target,
                new ResourceEffectLocation.Parameter(0),
                new ResourceEffectLocation.Return(),
                Guard: null,
                ResourceOperationThrows.Never,
                "wrapper-conversion");
        }

        void AddSlices(ResourceTypeExpression.Named wrapper)
        {
            AddSlice([int32]);
            AddSlice([int32, int32]);

            void AddSlice(
                ImmutableArray<ResourceTypeExpression> parameters)
            {
                ResourceEffectTargetSelector target = Member(
                    wrapper,
                    "Slice",
                    ResourceEffectMemberKind.Method,
                    isStatic: false,
                    parameters: [.. parameters.Select(Parameter)],
                    returnType: wrapper);
                AddDerivedOperation(
                    target,
                    new ResourceEffectLocation.Receiver(),
                    new ResourceEffectLocation.Return(),
                    Guard: null,
                    ResourceOperationThrows.Possible,
                    "slice");
            }
        }

        void AddView(
            ResourceTypeExpression.Named wrapper,
            ResourceTypeExpression.Named view)
        {
            ResourceEffectTargetSelector target = Member(
                wrapper,
                "get_Span",
                ResourceEffectMemberKind.PropertyGetter,
                isStatic: false,
                parameters: [],
                returnType: view);
            AddDerivedOperation(
                target,
                new ResourceEffectLocation.Receiver(),
                new ResourceEffectLocation.Return(),
                Guard: null,
                ResourceOperationThrows.Possible,
                "span-view");
        }

        void AddArrayViewExtension(
            ResourceTypeExpression.Named extensions,
            string name,
            ResourceTypeExpression source,
            ResourceTypeExpression targetType,
            ImmutableArray<ResourceTypeExpression> trailing,
            ResourceOperationThrows throws)
        {
            ResourceEffectTargetSelector target = Member(
                extensions,
                name,
                ResourceEffectMemberKind.Method,
                isStatic: true,
                parameters:
                [
                    Parameter(source),
                    .. trailing.Select(Parameter),
                ],
                returnType: targetType,
                genericArity: 1);
            AddDerivedOperation(
                target,
                new ResourceEffectLocation.Parameter(0),
                new ResourceEffectLocation.Return(),
                new ResourceEffectGuard.ExactRuntimeType(
                    new ResourceEffectLocation.Parameter(0),
                    new ResourceEffectSignatureLocation.Parameter(0)),
                throws,
                "array-view");
        }

        void AddDerivedOperation(
            ResourceEffectTargetSelector target,
            ResourceEffectLocation source,
            ResourceEffectLocation targetLocation,
            ResourceEffectGuard? Guard,
            ResourceOperationThrows throws,
            string provenance)
        {
            declarations.Add(
                new ResourceEffectTypedDeclaration(
                    target,
                    new ResourceEffect.Derive(
                        source,
                        targetLocation,
                        ResourceDerivationRelation.Alias,
                        Guard),
                    [Provenance(provenance, ordinal++)]));
            declarations.Add(
                new ResourceEffectTypedDeclaration(
                    target,
                    new ResourceEffect.Operation(
                        ResourceOperationBoundary.Transparent,
                        throws,
                        Guard),
                    [Provenance(provenance + "-operation", ordinal++)]));
        }

        ResourceEffectGuard GuardedArray() =>
            new ResourceEffectGuard.ExactRuntimeType(
                new ResourceEffectLocation.Parameter(0),
                new ResourceEffectSignatureLocation.Parameter(0));
    }

    static ResourceEffectTargetSelector Member(
        ResourceTypeExpression.Named declaringType,
        string name,
        ResourceEffectMemberKind kind,
        bool isStatic,
        ImmutableArray<ResourceEffectParameterSelector> parameters,
        ResourceTypeExpression returnType,
        int genericArity = 0) =>
        new ResourceEffectTargetSelector.Member(
            new ResourceEffectMemberSelector(
                declaringType,
                name,
                kind,
                isStatic,
                genericArity,
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
        CoreLibraryType(@namespace, name, []);

    static ResourceTypeExpression.Named CoreLibraryType(
        string @namespace,
        string name,
        ImmutableArray<ResourceTypeExpression> arguments) =>
        new(
            new ResourceAssemblySelector(
                "System.Runtime",
                "b03f5f7f11d50a3a",
                ResourceAssemblyVersionPolicy.Any,
                allowCoreLibraryFacade: true),
            @namespace,
            [new ResourceTypeNameSegment(name, arguments.Length)],
            arguments);

    static ResourceAssemblySelector FrameworkAssembly() =>
        new(
            "System.Buffers",
            "cc7b13ffcd2ddd51",
            ResourceAssemblyVersionPolicy.Any,
            allowCoreLibraryFacade: true);

    static ResourceAssemblySelector MemoryAssembly() =>
        new(
            "System.Memory",
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
