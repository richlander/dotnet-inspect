using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ObjectInitializerPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, null, RuntimeResolver);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    // Raises a method on any top-level fixture type in this assembly (not just
    // CfgSampleClass), for fixtures declared as their own types at end of file.
    static IrFunction RaisedFrom(string typeFullName, string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, null, RuntimeResolver);
        var function = IrImporter.Import(source, typeFullName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static readonly IAssemblyReferenceResolver RuntimeResolver = TestAssemblyReferenceResolvers.TrustedPlatformAssemblies();

    [Fact]
    public void ObjectInitializer_RaisesPropertyMembers()
    {
        var function = Raised(nameof(CfgSampleClass.MakePoint));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Y"], initializer.Members);
        Assert.Equal(["set_X", "set_Y"], initializer.Entries.Select(entry => entry.ConsumedMethod?.Name));
        Assert.All(initializer.Entries, entry => Assert.Null(entry.ConsumedField));
        // The creation is retained as a child so fidelity/unsafe scans still see it.
        Assert.Single(function.Descendants.OfType<NewObject>());
        // The lowered dup chain is gone.
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
    }

    [Fact]
    public void ObjectInitializer_RaisesFieldMembers()
    {
        var initializer = Assert.Single(Raised(nameof(CfgSampleClass.MakePointWithField)).Descendants.OfType<ObjectInitializerExpression>());

        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Z"], initializer.Members);
        Assert.Equal("set_X", initializer.Entries[0].ConsumedMethod?.Name);
        Assert.Null(initializer.Entries[0].ConsumedField);
        Assert.Null(initializer.Entries[1].ConsumedMethod);
        Assert.Equal("Z", initializer.Entries[1].ConsumedField?.Name);
    }

    [Fact]
    public void CollectionInitializer_RaisesAddCalls()
    {
        var function = Raised(nameof(CfgSampleClass.MakeList));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.True(initializer.IsCollection);
        Assert.Equal(3, initializer.Entries.Count);
        Assert.All(initializer.Entries, entry => Assert.Equal("Add", entry.ConsumedMethod?.Name));
        Assert.All(initializer.Entries, entry => Assert.Null(entry.ConsumedField));
        // No Add call survives as a standalone statement.
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");
    }

    [Fact]
    public void PlainConstruction_WithoutInitializer_StaysNewObject()
    {
        var function = Raised(nameof(CfgSampleClass.MakeEmpty));

        Assert.DoesNotContain(function.Descendants.OfType<ObjectInitializerExpression>(), _ => true);
        Assert.Single(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void Initializer_InArgumentPosition_IsRaised()
    {
        var output = Print(nameof(CfgSampleClass.MakeAndRead));

        Assert.Contains("new InitTarget { X = a }", output);
        Assert.DoesNotContain(".X = a;", output);
    }

    [Fact]
    public void InitializerArgumentBeforeShortCircuit_StaysLowered()
    {
        var function = Raised(nameof(CfgSampleClass.ObjectInitializerArgumentBeforeShortCircuit));

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".X = a;", output);
        Assert.DoesNotContain("new InitTarget {", output);
    }

    [Fact]
    public void NamedLocalObjectInitializer_RaisesIntoLocalDeclaration()
    {
        var function = Raised(nameof(CfgSampleClass.NamedPointInitializer));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Y"], initializer.Members);
        Assert.Empty(function.Descendants.OfType<StoreProperty>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("InitTarget target = new InitTarget { X = a, Y = b };", output);
        Assert.Contains("return target;", output);
    }

    [Fact]
    public void NamedLocalCollectionInitializer_RaisesIntoLocalDeclaration()
    {
        var function = Raised(nameof(CfgSampleClass.NamedListInitializer));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.True(initializer.IsCollection);
        Assert.Equal(3, initializer.Entries.Count);
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("List<int> values = new List<int> { a, b, 42 };", output);
        Assert.Contains("return values;", output);
    }

    [Fact]
    public void DisplayClassLocalSetup_IsNotRaisedAsObjectInitializer()
    {
        var function = Raised(nameof(CfgSampleClass.ClosureWithLinq));

        Assert.DoesNotContain(function.Descendants.OfType<ObjectInitializerExpression>(),
            initializer => GeneratedCodeIdentity.IsDisplayClassName(initializer.Creation.Constructor.DeclaringType));
    }

    [Fact]
    public void InitializerWithExtraOutsideUse_IsNotFoldedIntoSingleExpression()
    {
        // The expression-position slice requires exactly one outside use of the
        // threaded receiver. A kept-alive local has two uses (KeepAlive + return),
        // so folding it into a single object-initializer expression would erase a
        // real use site.
        var function = Raised(nameof(CfgSampleClass.NamedPointInitializerKeptAlive));

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".X = a;", output);
        Assert.Contains("GC.KeepAlive", output);
    }

    [Fact]
    public void InitializerWithDuplicateNamedMember_IsNotFoldedIntoInvalidCSharp()
    {
        var function = FunctionWithDuplicateMemberStores();

        new ObjectInitializerPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Equal(2, function.Descendants.OfType<StoreProperty>().Count());
        function.CheckInvariant();
    }

    [Fact]
    public void ReceiverSlotClobberedBeforeEscape_IsNotFolded()
    {
        // The threaded receiver slot is re-stored with a *different* object between
        // the member-store run and its single downstream load:
        //
        //   S_3 = new T(); S_3.X = 1; S_3 = new T(); return S_3;
        //
        // The escape (`return S_3`) yields the SECOND object, unmutated. Folding the
        // run into `new T { X = 1 }` at that load would drop the re-store and return
        // the wrong object. Dup slots are unique per dup so real lowerings never reuse
        // a receiver slot, but carry slots (and hand-written IL) can — the alias-slot
        // gate must reject a clobbered slot, mirroring the named-local single-store guard.
        var function = FunctionWithClobberedReceiverSlot();

        new ObjectInitializerPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        // Both NewObject creations and the member store survive untouched.
        Assert.Equal(2, function.Descendants.OfType<NewObject>().Count());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();
    }

    [Fact]
    public void PrintRaised_RendersInitializers()
    {
        Assert.Contains("return new InitTarget { X = a, Y = b };", Print(nameof(CfgSampleClass.MakePoint)));
        Assert.Contains("return new InitTarget { X = a, Z = b };", Print(nameof(CfgSampleClass.MakePointWithField)));
        Assert.Contains("return new List<int> { a, b, 42 };", Print(nameof(CfgSampleClass.MakeList)));
    }

    [Fact]
    public void IndexerInitializer_RaisesToObjectInitializerWithIndexerMembers()
    {
        var function = Raised(nameof(CfgSampleClass.MakeDictionaryByIndexer));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        // An indexer member is an object-form entry ([k] = v uses `=`), not a collection Add.
        Assert.False(initializer.IsCollection);
        // Each entry carries its key(s) plus the value, and no member name.
        Assert.All(initializer.Entries, entry => Assert.Null(entry.Member));
        Assert.All(initializer.Entries, entry => Assert.Equal(2, entry.Arguments.Count));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
    }

    [Fact]
    public void DictionaryAddInitializer_RaisesToCollectionInitializerWithMultiArgElements()
    {
        var function = Raised(nameof(CfgSampleClass.MakeDictionaryByAdd));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.True(initializer.IsCollection);
        // Each dictionary Add element keeps both arguments (key, value).
        Assert.All(initializer.Entries, entry => Assert.Equal(2, entry.Arguments.Count));
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");
    }

    [Fact]
    public void CollectionInitializerRequiresEnumerableReceiver()
    {
        var function = Raised(nameof(CfgSampleClass.MakeNonEnumerableAddLookalike));

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".Add(a);", output);
        Assert.DoesNotContain("new NonEnumerableAddTarget {", output);
    }

    [Fact]
    public void NestedObjectInitializer_RaisesMemberStoresIntoAnInitializerBlock()
    {
        var function = Raised(nameof(CfgSampleClass.MakeNestedObject));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        // The single top-level entry is the nested member; its value is the block.
        var entry = Assert.Single(initializer.Entries);
        Assert.Equal("Inner", entry.Member);
        Assert.Equal("get_Inner", entry.ConsumedMethod?.Name);
        var block = Assert.IsType<InitializerBlock>(Assert.Single(entry.Arguments));
        Assert.False(block.IsCollection);
        Assert.Equal(new string?[] { "X", "Y" }, block.Members);
        Assert.Equal(["set_X", "set_Y"], block.Entries.Select(inner => inner.ConsumedMethod?.Name));
        // The nested member reads are folded away — no residual stores/loads of Inner.
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
    }

    [Fact]
    public void NestedCollectionInitializer_RaisesAddsIntoACollectionBlock()
    {
        var function = Raised(nameof(CfgSampleClass.MakeNestedCollection));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);  // top level assigns Items via `=`
        var entry = Assert.Single(initializer.Entries);
        Assert.Equal("Items", entry.Member);
        Assert.Equal("get_Items", entry.ConsumedMethod?.Name);
        var block = Assert.IsType<InitializerBlock>(Assert.Single(entry.Arguments));
        Assert.True(block.IsCollection);
        Assert.Equal(2, block.Entries.Count);
        Assert.All(block.Entries, inner => Assert.Equal("Add", inner.ConsumedMethod?.Name));
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");
    }

    [Fact]
    public void PrintRaised_RendersNestedInitializers()
    {
        Assert.Contains(
            "return new InitContainer { Inner = { X = a, Y = b } };",
            Print(nameof(CfgSampleClass.MakeNestedObject)));
        Assert.Contains(
            "return new InitContainer { Items = { a, b } };",
            Print(nameof(CfgSampleClass.MakeNestedCollection)));
    }

    [Fact]
    public void NestedReassignment_KeepsTheNewAndDoesNotCollapseToMutation()
    {
        // `Inner = new InitTarget { ... }` reconstructs Inner (a flat member store
        // whose value is its own sub-initializer); it must keep `new` and never
        // render as the in-place nested-mutation form `Inner = { ... }`. The outer
        // initializer only folds because seeds are raised inner-first.
        var output = Print(nameof(CfgSampleClass.MakeNestedReassign));

        Assert.Contains("return new InitContainer { Inner = new InitTarget { X = a, Y = b } };", output);
        Assert.DoesNotContain("Inner = { ", output);
    }

    [Fact]
    public void FlatAndNestedMembers_RaiseTogether()
    {
        var output = Print(nameof(CfgSampleClass.MakeFlatAndNested));

        Assert.Contains("return new InitContainer { Tag = c, Inner = { X = a, Y = b } };", output);
    }

    [Fact]
    public void TwoNestedMembers_RaiseAsSeparateBlocks()
    {
        var function = Raised(nameof(CfgSampleClass.MakeTwoNestedMembers));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(new string?[] { "Inner", "Items" }, initializer.Members);
        var blocks = initializer.Entries
            .Select(entry => Assert.IsType<InitializerBlock>(Assert.Single(entry.Arguments)))
            .ToList();
        Assert.False(blocks[0].IsCollection);  // Inner = { X = a }
        Assert.True(blocks[1].IsCollection);   // Items = { b }
        Assert.Contains("return new InitContainer { Inner = { X = a }, Items = { b } };", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void NamedLocalNestedMutation_StaysLowered()
    {
        // A nested mutation through a real local (used twice: KeepAlive + return) is
        // not the expression-position dup form, so it must not fold to an initializer.
        var function = Raised(nameof(CfgSampleClass.MakeNamedLocalNestedMutation));

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".Inner.X = a;", output);
        Assert.Contains("GC.KeepAlive", output);
    }

    [Fact]
    public void GenericPropertySetter_IsNotFoldedIntoInvalidInitializer()
    {
        // s0 = new Owner(); s0.set_Value<string>(fallback); return s0;
        // A generic setter has no `Value = ...` object-initializer spelling, so the
        // pass must leave the StoreProperty lowered rather than emit an unspellable
        // `new Owner { Value = fallback }` (#1416).
        var function = FunctionWithSetter(generic: true);

        new ObjectInitializerPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        Assert.Single(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
    }

    [Fact]
    public void NonGenericVoidSetter_StillFoldsIntoInitializer()
    {
        // The positive canary for the generic-setter guard: an ordinary void setter
        // with one value parameter still raises to an object initializer.
        var function = FunctionWithSetter(generic: false);

        new ObjectInitializerPass().Run(function, PassContext.None);

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Equal(["Value"], initializer.Members);
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();
    }

    [Fact]
    public void KeywordPropertyName_FoldsIntoEscapedInitializer()
    {
        var function = FunctionWithSetter(generic: false, propertyName: "set_else");

        new ObjectInitializerPass().Run(function, PassContext.None);

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Equal(["else"], initializer.Members);
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("new Owner { @else = \"fallback\" }", output);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void UnspellablePropertyName_IsNotFoldedIntoInvalidInitializer()
    {
        // set_bad-name has a usable accessor shape but no `bad-name = v` C# spelling.
        var function = FunctionWithSetter(generic: false, propertyName: "set_bad-name");

        new ObjectInitializerPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();
    }

    [Fact]
    public void SpellableFieldName_StillFoldsIntoInitializer()
    {
        var function = FunctionWithFieldStore("Z");

        new ObjectInitializerPass().Run(function, PassContext.None);

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Equal(["Z"], initializer.Members);
        Assert.Empty(function.Descendants.OfType<StoreField>());
        function.CheckInvariant();
    }

    [Fact]
    public void NestedUnspellablePropertyRoot_IsNotFoldedIntoInvalidInitializer()
    {
        // s0 = new Owner(); s0.get_bad-name().X = 1; return s0;
        // The nested root property name has no `bad-name = { ... }` initializer spelling.
        var type = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var voidType = TypeRef.CoreLib("System", "Void");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", voidType, [], HasThis: true);
        var getBadName = new MethodRef(type, "get_bad-name", type, [], HasThis: true) { IsSpecialName = true };
        var setX = new MethodRef(type, "set_X", voidType, [intType], HasThis: true) { IsSpecialName = true };

        const int slot = 0;
        var block = new Block();
        block.Add(new StoreStackSlot(slot, new NewObject(ctor, [])));
        block.Add(new StoreProperty(setX, new LoadProperty(getBadName, new LoadStackSlot(slot, type), []), [], new Constant(1, intType)));
        block.Add(new Return(new LoadStackSlot(slot, type)));

        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "MakeOwner", type, new MethodSignature(type, [], HasThis: false, GenericParameterCount: 0), [], body);

        new ObjectInitializerPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();
    }

    [Fact]
    public void NestedKeywordPropertyRoot_RaisesAndEscapes()
    {
        var output = Print(nameof(CfgSampleClass.MakeNestedKeywordProperty));

        Assert.Contains("return new InitContainer { @else = { X = value } };", output);
    }

    // #3272: the initializer sits in a trailing constructor-argument position, so a
    // preceding argument is evaluated first and spilled to a local by the stackifier,
    // interleaving between `new()` and the first member store. The pass now skips that
    // independent statement and folds via the use site, restoring the source spelling
    // (which recompiles to the original dup-chain IL — byte-neutral).
    [Fact]
    public void TrailingInitializerWithLeadingArgument_FoldsViaUseSite()
    {
        var function = Raised(nameof(CfgSampleClass.MakeConsumerWithTrailingInitializer));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Y"], initializer.Members);

        // The whole dup chain and its version copies are gone: no residual slot store,
        // property store, or spilled local survives.
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
        Assert.Empty(function.Descendants.OfType<StoreLocal>());

        Assert.Contains(
            "return new InitConsumer(Identity(tag), new InitTarget { X = a, Y = b });",
            CSharpPrinter.Print(function).Output);
    }

    // #3272 breadth: two preceding arguments produce two interleaved statements before
    // the first member store; both are skipped and the initializer still folds (the
    // spilled arg locals are left for later inlining — orthogonal to this pass).
    [Fact]
    public void TrailingInitializerWithTwoLeadingArguments_FoldsViaUseSite()
    {
        var function = Raised(nameof(CfgSampleClass.MakeConsumerWithTwoLeadingArgs));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Equal(["X", "Y"], initializer.Members);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());

        Assert.Contains(
            "new InitTarget { X = a, Y = b }",
            CSharpPrinter.Print(function).Output);
    }

    // Close negative for the skip guard: an interleaved statement that READS the
    // threaded reference before the first member store is not independent, so it must
    // break the run (never be skipped) and leave the object lowered.
    [Fact]
    public void ForeignReadBeforeMembers_StaysLowered()
    {
        var function = FunctionWithForeignReadBeforeFirstMember();
        IrPasses.Run(function);
        function.CheckInvariant();

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
    }

    // #3272 provenance guard, real reachable case: `var t = new(); SideEffect(); t.X =
    // a; return t;`. Roslyn erases `t` into the SAME stack-slot dup form as the
    // trailing-argument fixtures, so the interleaved SideEffect() call is slot-
    // independent and would be skipped on shape alone. But the `newobj` runs BEFORE
    // SideEffect() in the original IL (offset 0 vs 5); folding via the use site would
    // move the construction after the call, an observable reorder. The offset guard
    // declines the skip, so the object must stay lowered.
    [Fact]
    public void ReorderingVoidCallBetween_StaysLowered()
    {
        var function = Raised(nameof(CfgSampleClass.MakeTargetWithVoidCallBetween));

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
    }

    // #3272 provenance robustness (GPT adversarial finding): the leading constructor
    // argument is a STATIC PROPERTY read. PropertySugarPass rewrites the getter Call
    // into a zero-child LoadProperty; it now inherits the Call's SourceOffset, so the
    // skip guard can still prove the spill ran before the `newobj` and folds. Without
    // the inherited offset the value subtree carries no offset and the guard would
    // over-conservatively decline.
    [Fact]
    public void TrailingInitializerWithStaticPropertyArgument_FoldsViaUseSite()
    {
        var function = Raised(nameof(CfgSampleClass.MakeConsumerWithStaticPropertyArg));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Equal(["X"], initializer.Members);

        Assert.Contains(
            "new InitConsumer(CfgSampleClass.StaticTag, new InitTarget { X = a })",
            CSharpPrinter.Print(function).Output);
    }

    // #3336 stage 1: a single-use member-value `default` spill. Roslyn spills
    // `default(InitFlag)` (a struct) to a local via `initobj` and reads it back as
    // the trailing member value AFTER the `newobj`. #3272's skip guard tolerates
    // only spills computed BEFORE the newobj, so this member — and thus the whole
    // initializer — stayed lowered. The pass now consumes the single-use spill and
    // inlines `default(InitFlag)` at the member, folding the entire chain.
    [Fact]
    public void DefaultStructMemberSpill_FoldsWholeInitializer()
    {
        var function = Raised(nameof(CfgSampleClass.MakeTargetWithDefaultStructMember));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Y", "Flag"], initializer.Members);
        // The inlined member value is default(InitFlag), and the spilling initobj is gone.
        Assert.Single(initializer.Entries[^1].Arguments.OfType<DefaultValue>());
        Assert.Empty(function.Descendants.OfType<InitObject>());

        // The whole dup chain, its version copies, and the spill local are gone.
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());

        Assert.Contains(
            "new InitTargetWithFlag { X = a, Y = b, Flag = default(InitFlag) }",
            CSharpPrinter.Print(function).Output);
    }

    // #3336 stage 2: a branchy member value (`f ? PickTag(..) : null`) that
    // Roslyn spilled to a reused temp beneath the dup chain. The early pass
    // declines it (the value is a cross-block ternary diamond at stage 18); the
    // post-structuring late spill pass folds it once the diamond has collapsed
    // to a single Conditional in one straight-line block.
    [Fact]
    public void ReusedTempSpill_BranchyMemberValues_FoldsWholeInitializer()
    {
        var function = Raised(nameof(CfgSampleClass.MakeBranchyReusedTempSpill));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(["A", "B"], initializer.Members);
        // Both member values are the raised ternaries, not spilled version copies.
        Assert.Equal(2, initializer.Entries.Count(entry => entry.Arguments.OfType<Conditional>().Any()));

        // The whole dup chain, its version copies, and the reused spill temps are gone.
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());

        Assert.Contains(
            "new Branchy { A = f ? PickTag(x) : null, B = f ? PickTag(-x) : null }",
            CSharpPrinter.Print(function).Output);
    }

    // #3336 stage 3: the outer initializer's Inner member is itself a branchy
    // reused-temp spill initializer. The late pass folds inner-first (the inner
    // initializer becomes the outer member's value), then the enclosing chain.
    [Fact]
    public void ReusedTempSpill_NestedInitializer_FoldsBothLevels()
    {
        var function = Raised(nameof(CfgSampleClass.MakeNestedReusedTempSpill));

        var initializers = function.Descendants.OfType<ObjectInitializerExpression>().ToList();
        Assert.Equal(2, initializers.Count);

        var outer = Assert.Single(initializers, init => init.Members.SequenceEqual(["Inner", "Tag"]));
        var inner = Assert.Single(initializers, init => init.Members.SequenceEqual(["A", "B"]));
        // The inner initializer lands as the outer Inner member's value.
        Assert.Contains(outer.Entries, entry => entry.Arguments.Contains(inner));

        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());

        Assert.Contains(
            "new InitOuter { Inner = new Branchy { A = f ? PickTag(x) : null, B = f ? PickTag(-x) : null }, Tag = x }",
            CSharpPrinter.Print(function).Output);
    }

    // #3459: an object initializer used as a CALL ARGUMENT whose enclosing call has a
    // pure receiver spill (`_rest`, a field off `this`) and a `default` struct argument
    // spilled around the member store — the Azure.Data.Tables `TableClient.Create`
    // shape. The fold moves the construction to the call-argument position AND inlines
    // both reorder-safe spills back into their operands, restoring the canonical
    // stack-only spelling that recompiles byte-for-byte to the original IL.
    [Fact]
    public void CallArgumentInitializer_FoldsAndInlinesReorderSafeSpills()
    {
        var function = RaisedFrom("ILInspector.Decompiler.Tests.CallArgClient", "CreateViaField");

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(["Name"], initializer.Members);

        // Both reorder-safe spills are inlined: the receiver `_rest` (no residual slot
        // store) and the `default` struct argument (the spilling initobj is gone).
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<InitObject>());

        Assert.Contains(
            "return _rest.Create(new CallArgTarget { Name = Label }, default(Nullable<CallArgFlag>), _options);",
            CSharpPrinter.Print(function).Output);
    }

    // #3459 breadth: a VOLATILE field receiver. The receiver read runs before the
    // `newobj`, so it is an offset-guarded skip rather than a reorder-safe one — the
    // object-initializer pass leaves it in place (only reorder-safe spills are inlined
    // by this pass; a later copy-propagation pass may still hoist it). Either way the
    // initializer folds, and the result stays byte-neutral (verified on the standalone
    // witness). This pins that a volatile receiver never blocks the call-argument fold.
    [Fact]
    public void CallArgumentInitializer_WithVolatileReceiver_StillFolds()
    {
        var function = RaisedFrom("ILInspector.Decompiler.Tests.CallArgClient", "CreateViaVolatileField");

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Equal(["Name"], initializer.Members);

        Assert.Contains(
            "Create(new CallArgTarget { Name = Label }, default(Nullable<CallArgFlag>), _options)",
            CSharpPrinter.Print(function).Output);
    }

    // #3459 close negative for the inline single-use guard: a reorder-safe spill whose
    // slot is loaded MORE THAN ONCE must NOT be inlined — replacing one load and
    // detaching the store would leave the other load dangling on a removed slot. The
    // guard leaves the spill store in place; the initializer still folds via the use
    // site. (Roslyn never spills a pure value it reads twice — it re-loads it — so this
    // shape is reached only through hand-written IL or an unusual lowering, exercised
    // here with a synthetic function.)
    [Fact]
    public void ReorderSafeSpillWithMultipleUses_IsNotInlined()
    {
        var function = FunctionWithReorderSafeSpillUsedTwice();

        new ObjectInitializerPass().Run(function, PassContext.None);

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Equal(["X"], initializer.Members);

        // The twice-used reorder-safe spill store survives (not inlined), and both of
        // its loads remain intact.
        Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 257);
        Assert.Equal(2, function.Descendants.OfType<LoadStackSlot>().Count(load => load.Slot == 257));
        function.CheckInvariant();
    }

    // #3459 blocking negative (reorder soundness): a receiver field read that sits
    // AFTER a member value which may write that field must NOT be hoisted ahead of the
    // member value. Inlining `this.Rest` into the folded receiver position (evaluated
    // before the initializer argument) would observe the pre-`MutateRest()` receiver.
    // The spill store must survive, and the dangerous reordered spelling must not
    // appear.
    [Fact]
    public void FieldReceiverSpillAfterMutatingEntry_IsNotHoistedAcrossSideEffect()
    {
        var function = FunctionWithFieldReceiverSpillAfterMutatingEntry();

        new ObjectInitializerPass().Run(function, PassContext.None);

        // The receiver read is left as a spill store (not inlined ahead of the mutation).
        Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 257);
        Assert.DoesNotContain("this.Rest.Consume(new", CSharpPrinter.Print(function).Output);
        function.CheckInvariant();
    }

    // #3459 blocking negative (throw soundness): in a STATIC method argument 0 is an
    // ordinary, possibly null parameter, so reading a field off it can throw. It must
    // not be admitted as a non-throwing reorder-safe spill and inlined as the folded
    // receiver, which would change when the NullReferenceException is observed.
    [Fact]
    public void StaticArg0FieldReceiverSpill_IsNotInlined()
    {
        var function = FunctionWithStaticArg0FieldReceiverSpill();

        new ObjectInitializerPass().Run(function, PassContext.None);

        // The receiver read remains a spill store (not treated as a `this` field read).
        Assert.Single(function.Descendants.OfType<StoreStackSlot>(), store => store.Slot == 257);
        function.CheckInvariant();
    }

    static IrFunction FunctionWithSetter(bool generic, string propertyName = "set_Value")
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var voidType = TypeRef.CoreLib("System", "Void");
        var stringType = TypeRef.CoreLib("System", "String");
        var ctor = new MethodRef(type, ".ctor", voidType, [], HasThis: true);
        var setter = new MethodRef(type, propertyName, voidType, [stringType], HasThis: true)
        {
            IsSpecialName = true,
            TypeArguments = generic ? [stringType] : [],
        };

        const int slot = 0;
        var block = new Block();
        block.Add(new StoreStackSlot(slot, new NewObject(ctor, [])));
        block.Add(new StoreProperty(setter, new LoadStackSlot(slot, type), [], new Constant("fallback", stringType)));
        block.Add(new Return(new LoadStackSlot(slot, type)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "MakeOwner",
            type,
            new MethodSignature(type, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction FunctionWithFieldStore(string fieldName)
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var voidType = TypeRef.CoreLib("System", "Void");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", voidType, [], HasThis: true);
        var field = new FieldRef(type, fieldName, intType);

        const int slot = 0;
        var block = new Block();
        block.Add(new StoreStackSlot(slot, new NewObject(ctor, [])));
        block.Add(new StoreField(field, new LoadStackSlot(slot, type), new Constant(1, intType)));
        block.Add(new Return(new LoadStackSlot(slot, type)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "MakeOwner",
            type,
            new MethodSignature(type, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction FunctionWithDuplicateMemberStores()
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "InitTarget");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true);
        var setter = new MethodRef(type, "set_X", TypeRef.CoreLib("System", "Void"), [intType], HasThis: true)
        {
            IsSpecialName = true,
        };

        var block = new Block();
        block.Add(new StoreStackSlot(256, new NewObject(ctor, [])));
        block.Add(new StoreStackSlot(257, new LoadStackSlot(256, type)));
        block.Add(new StoreProperty(setter, new LoadStackSlot(257, type), [], new Constant(1, intType)));
        block.Add(new StoreStackSlot(258, new LoadStackSlot(256, type)));
        block.Add(new StoreProperty(setter, new LoadStackSlot(258, type), [], new Constant(2, intType)));
        block.Add(new Return(new LoadStackSlot(256, type)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "DuplicateMember",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(type, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction FunctionWithClobberedReceiverSlot()
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "InitTarget");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true);
        var setter = new MethodRef(type, "set_X", TypeRef.CoreLib("System", "Void"), [intType], HasThis: true)
        {
            IsSpecialName = true,
        };

        // A reusable carry slot (< DupSlotBase) holds the receiver, gets one member
        // store, then is re-stored with a second, unrelated object before the escape.
        const int slot = 3;
        var block = new Block();
        block.Add(new StoreStackSlot(slot, new NewObject(ctor, [])));
        block.Add(new StoreProperty(setter, new LoadStackSlot(slot, type), [], new Constant(1, intType)));
        block.Add(new StoreStackSlot(slot, new NewObject(ctor, [])));
        block.Add(new Return(new LoadStackSlot(slot, type)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "ClobberedReceiver",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(type, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    // A `new()` followed — before any member store — by a statement that reads the
    // freshly-constructed reference (Observe(t)) and only then a member store. The read
    // is not independent of the reference, so the pass must not skip it; the object
    // stays lowered.
    static IrFunction FunctionWithForeignReadBeforeFirstMember()
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "InitTarget");
        var voidType = TypeRef.CoreLib("System", "Void");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", voidType, [], HasThis: true);
        var setter = new MethodRef(type, "set_X", voidType, [intType], HasThis: true)
        {
            IsSpecialName = true,
        };
        var observe = new MethodRef(
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            "Observe",
            voidType,
            [type],
            HasThis: false);

        const int slot = 256;
        var block = new Block();
        block.Add(new StoreStackSlot(slot, new NewObject(ctor, [])));
        block.Add(new ExpressionStatement(new Call(observe, isVirtual: false, [new LoadStackSlot(slot, type)])));
        block.Add(new StoreProperty(setter, new LoadStackSlot(slot, type), [], new Constant(1, intType)));
        block.Add(new Return(new LoadStackSlot(slot, type)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "ForeignReadBeforeMembers",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(type, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    // Builds `S_256 = new(); S_257 = n; S_256.X = 1; return Consume(S_256, S_257, S_257);`
    // where S_257 (a reorder-safe LoadArgument spill) is loaded TWICE in the escape call.
    // The pass folds the initializer into the call-argument position but must leave the
    // twice-used spill store in place (inlining it would drop the second load).
    static IrFunction FunctionWithReorderSafeSpillUsedTwice()
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "InitTarget");
        var voidType = TypeRef.CoreLib("System", "Void");
        var intType = TypeRef.CoreLib("System", "Int32");
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var ctor = new MethodRef(type, ".ctor", voidType, [], HasThis: true);
        var setter = new MethodRef(type, "set_X", voidType, [intType], HasThis: true)
        {
            IsSpecialName = true,
        };
        var consume = new MethodRef(owner, "Consume", intType, [type, intType, intType], HasThis: false);

        const int seed = 256;
        const int spill = 257;
        var block = new Block();
        block.Add(new StoreStackSlot(seed, new NewObject(ctor, [])));
        block.Add(new StoreStackSlot(spill, new LoadArgument(0, "n", intType)));
        block.Add(new StoreProperty(setter, new LoadStackSlot(seed, type), [], new Constant(1, intType)));
        block.Add(new Return(new Call(consume, isVirtual: false,
        [
            new LoadStackSlot(seed, type),
            new LoadStackSlot(spill, intType),
            new LoadStackSlot(spill, intType),
        ])));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "ReorderSafeSpillUsedTwice",
            owner,
            new MethodSignature(intType, [new Parameter("n", intType)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    // Builds `S_256 = new(); S_256.X = MutateRest(); S_257 = this.Rest;
    // return S_257.Consume(S_256);` in an INSTANCE method. The receiver field read
    // `this.Rest` is a reorder-safe candidate (non-volatile field off `this`), but it
    // sits AFTER a member value `MutateRest()` that has a side effect and may write
    // `this.Rest`. Inlining it into the folded call's receiver position would move the
    // read ahead of `MutateRest()`, observing the pre-mutation receiver. The pass must
    // NOT admit a mutable-memory read after an entry, so `this.Rest` is never inlined
    // as the folded receiver.
    static IrFunction FunctionWithFieldReceiverSpillAfterMutatingEntry()
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "InitTarget");
        var rest = TypeRef.Definition("Synthetic", "Samples", "Rest");
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var voidType = TypeRef.CoreLib("System", "Void");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", voidType, [], HasThis: true);
        var setter = new MethodRef(type, "set_X", voidType, [intType], HasThis: true)
        {
            IsSpecialName = true,
        };
        var mutateRest = new MethodRef(owner, "MutateRest", intType, [], HasThis: false);
        var consume = new MethodRef(rest, "Consume", intType, [type], HasThis: true);
        var restField = new FieldRef(owner, "Rest", rest);

        const int seed = 256;
        const int receiver = 257;
        var block = new Block();
        block.Add(new StoreStackSlot(seed, new NewObject(ctor, [])));
        block.Add(new StoreProperty(setter, new LoadStackSlot(seed, type), [], new Call(mutateRest, isVirtual: false, [])));
        block.Add(new StoreStackSlot(receiver, new LoadField(restField, new LoadArgument(0, "this", owner))));
        block.Add(new Return(new Call(consume, isVirtual: false,
        [
            new LoadStackSlot(receiver, rest),
            new LoadStackSlot(seed, type),
        ])));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "FieldReceiverSpillAfterMutatingEntry",
            owner,
            new MethodSignature(intType, [], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    // Builds `S_256 = new(); S_257 = holder.Rest; S_256.X = 1;
    // return S_257.Consume(S_256);` in a STATIC method, where argument 0 (`holder`) is
    // an ordinary, possibly null parameter rather than `this`. Reading `holder.Rest`
    // can throw NullReferenceException, so it is NOT reorder-safe: moving it (and the
    // construction) would change when the throw is observed. The pass must decline to
    // inline it as the folded receiver.
    static IrFunction FunctionWithStaticArg0FieldReceiverSpill()
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "InitTarget");
        var rest = TypeRef.Definition("Synthetic", "Samples", "Rest");
        var owner = TypeRef.Definition("Synthetic", "Samples", "Owner");
        var voidType = TypeRef.CoreLib("System", "Void");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", voidType, [], HasThis: true);
        var setter = new MethodRef(type, "set_X", voidType, [intType], HasThis: true)
        {
            IsSpecialName = true,
        };
        var consume = new MethodRef(rest, "Consume", intType, [type], HasThis: true);
        var restField = new FieldRef(owner, "Rest", rest);

        const int seed = 256;
        const int receiver = 257;
        var block = new Block();
        block.Add(new StoreStackSlot(seed, new NewObject(ctor, [])));
        block.Add(new StoreStackSlot(receiver, new LoadField(restField, new LoadArgument(0, "holder", owner))));
        block.Add(new StoreProperty(setter, new LoadStackSlot(seed, type), [], new Constant(1, intType)));
        block.Add(new Return(new Call(consume, isVirtual: false,
        [
            new LoadStackSlot(receiver, rest),
            new LoadStackSlot(seed, type),
        ])));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "StaticArg0FieldReceiverSpill",
            owner,
            new MethodSignature(intType, [new Parameter("holder", owner)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
