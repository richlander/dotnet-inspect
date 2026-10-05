using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.ControlFlow;
using ILInspector.Metadata;
using Inspector.Text;
using static ILInspector.Decompiler.Pipeline.PointerArithmetic;
using static ILInspector.Decompiler.Pipeline.PlaceIdentity;
using DecisionKey = (string RuleId, string Category, string Subject, string Detail, string OldValue, string NewValue, string DedupDiscriminator);

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Local declaration collection and shape, scope and name reservation, and
/// declaration type text. Carries the definite-assignment consumer (design
/// Instance 3) until it moves to a pass.
/// </summary>
public sealed partial class CSharpPrinter
{
    IEnumerable<string> CollectDeclarations(IrFunction function)
    {
        var locals = new SortedSet<int>();
        foreach (var node in function.DescendantsOutsideNestedFunctions)
        {
            switch (node)
            {
                case LoadLocal l: locals.Add(l.Index); break;
                case StoreLocal s: locals.Add(s.Index); break;
                case LoadLocalAddress a: locals.Add(a.Index); break;
                case NullCoalescingAssignment n: locals.Add(n.LocalIndex); break;
                case ForeachStatement f: locals.Add(f.LocalIndex); break;
                case DeconstructionAssignment d:
                    foreach (var target in d.Targets)
                        if (target.Kind == DeconstructionTargetKind.Local)
                            locals.Add(target.LocalIndex);
                    break;
            }
        }
        int switchIndex = 0;
        foreach (var switchBranch in function.DescendantsOutsideNestedFunctions.OfType<SwitchBranch>())
        {
            string name = ReserveName($"__switchValue{switchIndex++}", new HashSet<string>(CurrentScopeNames(), StringComparer.Ordinal));
            _switchTemps.TryAdd(switchBranch, name);
            yield return $"int {name} = default;";
        }
        var materializedSlotDeclarations =
            new SortedDictionary<(int Slot, int Ordinal), string>();
        foreach (int index in locals)
        {
            // Syntax-owned locals declare at their owner, not up front.
            if (_fixedLocals.Contains(index) || _usingLocals.Contains(index) || _foreachLocals.Contains(index)
                || _isPatternLocals.Contains(index) || _deconstructionLocals.Contains(index)
                || _catchLocals.Contains(index) || _outArgumentLocals.Contains(index))
                continue;
            bool declaredAtStore = _declaringStores.Any(s =>
                s is StoreLocal store && store.Index == index
                || s is InitObject { Address: LoadLocalAddress init } && init.Index == index);
            if (!declaredAtStore)
            {
                // An up-front local is referenced before a defining store, so
                // it relies on IL's zero-initialization of locals (localsinit).
                // Spell that as `= default` — both faithful and what C#'s
                // definite-assignment requires (a bare declaration is CS0165 on
                // any path that reads before assigning). When the local is
                // instead provably assigned on every path before each read, the
                // `= default` is a dead store the IL never had (it leans on
                // localsinit), so drop it and declare bare. A ref local takes
                // neither: `= default` is illegal and a bare declaration is
                // CS8174. IL zero-initializes a managed pointer to a null
                // reference, whose faithful C# spelling is Unsafe.NullRef<T>().
                // Fully qualified so the per-member view compiles without a
                // using; the whole-type hoister shortens it and adds the using.
                string declaration = LocalDeclaration(function, index);
                if (function.TryGetMaterializedStackSlotLocal(
                        index,
                        out int slot))
                {
                    materializedSlotDeclarations.Add(
                        (slot, 0),
                        declaration);
                }
                else
                {
                    yield return declaration;
                }
            }
        }

        foreach (string declaration in materializedSlotDeclarations.Values)
            yield return declaration;
    }

    string LocalDeclaration(IrFunction function, int index)
    {
        var type = function.Locals[index];
        string scoped = _scopedLocals.Contains(index) ? "scoped " : "";
        // A residual-bound local (value-typed-emission.md, "Residual storage
        // binding") is never zero-initialised: a piece read with no reaching
        // store is a binding gap, and `= default` would turn that gap into
        // compiling C# that silently drops the term. The bare declaration
        // keeps it CS0165-visible.
        return type.Kind == TypeRefKind.ByRef
            ? $"{TypeText(type)} {LocalName(index)} = ref System.Runtime.CompilerServices.Unsafe.NullRef<{TypeText(type.ElementType!)}>();"
            : _readBeforeAssign.Contains(index)
                && !function.ResidualSlotBindings.ContainsKey(index)
                ? $"{scoped}{TypeText(type)} {LocalName(index)} = default;"
                : $"{scoped}{TypeText(type)} {LocalName(index)};";
    }

    string FixedLocalName(Fixed fixedStatement)
        => LocalName(fixedStatement.LocalIndex);

    bool IsReferenceLike(TypeRef type)
        => CoercionRendering.IsReferenceLike(type, _function.TypeShapes);

    IReadOnlySet<string> CurrentScopeNames()
    {
        var names = CurrentReservedNames(includeLocals: true);
        names.UnionWith(_reservedScopeNames);
        AddDescendantBinderNames(names);
        foreach (var name in _switchTemps.Values)
            names.Add(name);
        // Synthetic locals (e.g. __stackalloc) are in scope for this body and for
        // any nested lambda/local-function printer built from these names.
        foreach (var name in _syntheticLocalNames)
            names.Add(name);
        return names;
    }

    HashSet<string> CurrentReservedNames(bool includeLocals = false)
    {
        var names = ExactLocalNameAllocation.ReservedNames(
            _function,
            _function.Signature.Parameters,
            _function.Signature.GenericParameterNames,
            _capturedScopeNames);
        if (includeLocals)
        {
            for (int i = 0; i < _function.Locals.Length; i++)
            {
                if (RetainedLocalSlots().Contains(i))
                    names.Add(LocalName(i));
            }
        }
        return names;
    }

    void AddDescendantBinderNames(HashSet<string> names)
    {
        foreach (var nested in _function.Descendants.OfType<Lambda>())
            foreach (var parameter in nested.Parameters)
                names.Add(parameter.DisplayName);
        foreach (var nested in _function.Descendants.OfType<LocalFunctionStatement>())
        {
            names.Add(nested.Name);
            foreach (var parameter in nested.Parameters)
                names.Add(parameter.DisplayName);
        }
    }

    bool HasBranchTargetAfterStatement(IrNode statement)
    {
        if (statement.Parent is not Block block || statement.ChildIndex < 0)
            return false;
        return block.Children.Skip(statement.ChildIndex + 1)
            .SelectMany(node => node.DescendantsAndSelfOutsideNestedFunctions)
            .Any(n => n.OwnsSourceLabel
                && n.SourceOffset >= 0
                && _labelTargets.Contains(n.SourceOffset));
    }

    static bool ReferencesLocalIncludingSharedNestedScopes(IrNode node, int index)
    {
        if (node is Lambda { NeedsIsolatedLocalScope: true })
            return false;
        if (node is LocalFunctionStatement { NeedsIsolatedLocalScope: true })
            return false;
        if (IsLocalReference(node, index))
            return true;
        foreach (var child in node.Children)
        {
            if (child is Lambda { NeedsIsolatedLocalScope: true })
                continue;
            if (child is LocalFunctionStatement { NeedsIsolatedLocalScope: true })
                continue;
            if (ReferencesLocalIncludingSharedNestedScopes(child, index))
                return true;
        }
        return false;
    }

    static bool IsLocalReference(IrNode node, int index)
        => node is StoreLocal store && store.Index == index
            || node is LoadLocal load && load.Index == index
            || node is LoadLocalAddress address && address.Index == index;

    static bool IsDescendantOrSelf(IrNode node, IrNode ancestor)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    string FreshSyntheticLocalName(string baseName)
    {
        var used = CurrentScopeNames();
        string chosen = baseName;
        if (used.Contains(baseName))
        {
            for (int i = 0; ; i++)
            {
                string candidate = $"{baseName}{i}";
                if (!used.Contains(candidate))
                {
                    chosen = candidate;
                    break;
                }
            }
        }
        // Record every generated synthetic local so a self-static call whose name
        // collides with it (e.g. a suffixed `__stackalloc0`) stays qualified.
        _syntheticLocalNames.Add(chosen);
        return chosen;
    }

    HashSet<string>? _localScopeNames;

    IReadOnlySet<int> RetainedLocalSlots()
        => _localDeclarationPlan?.RetainedLocalSlots
            ?? throw new InvalidOperationException(
                "Local bindings are unavailable before declaration planning.");

    /// <summary>
    /// The display name for local slot <paramref name="index"/>: the PDB source
    /// name issued by the finalized declaration and binding plan. Every
    /// reference to a slot — declaration, load, address, shadow test — spells
    /// the same planned identifier.
    /// </summary>
    string LocalName(int index)
    {
        if (_localDeclarationPlan is not { } plan)
        {
            throw new InvalidOperationException(
                "Local bindings are unavailable before declaration planning.");
        }
        if ((uint)index >= (uint)plan.Bindings.Length)
        {
            throw new InvalidOperationException(
                $"Local {index} is outside the planned binding population.");
        }
        var binding = plan.Bindings[index];
        if (binding.Provenance == LocalBindingNameProvenance.Eliminated)
        {
            throw new InvalidOperationException(
                $"Eliminated local {index} has no presentation binding.");
        }
        return binding.Identifier;
    }

    string LocalFactLabel(int index)
    {
        if (_localDeclarationPlan is { } plan
            && (uint)index < (uint)plan.Bindings.Length
            && plan.Bindings[index].Provenance
                == LocalBindingNameProvenance.Eliminated)
        {
            return $"V_{index} (eliminated)";
        }
        return LocalName(index);
    }

    static string ReserveName(string baseName, HashSet<string> taken)
    {
        if (taken.Add(baseName))
            return baseName;
        for (int i = 1; ; i++)
        {
            string candidate = $"{baseName}_{i}";
            if (taken.Add(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// True when an instance-method parameter or local would shadow a field of
    /// this name, so a bare reference binds to the local rather than the field.
    /// Locals print with their resolved display name; parameters carry their
    /// metadata names.
    /// </summary>
    bool IsShadowedByLocal(string fieldName)
    {
        if (_localScopeNames is null)
        {
            _localScopeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in _function.Signature.Parameters)
                _localScopeNames.Add(parameter.DisplayName);
            for (int i = 0; i < _function.Locals.Length; i++)
            {
                if (RetainedLocalSlots().Contains(i))
                    _localScopeNames.Add(LocalName(i));
            }
        }
        return _localScopeNames.Contains(fieldName);
    }

    HashSet<string>? _staticScopeShadowNames;
    readonly HashSet<string> _syntheticLocalNames = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the escaped source spelling of a static-call name is captured by
    /// a parameter or local in scope — the enclosing method's parameters and
    /// locals, names inherited from an enclosing printer, and nested lambda /
    /// local-function parameters — so an unqualified call would bind to that local
    /// rather than the static method. Names are compared in their escaped C#
    /// spelling (a keyword carries the leading <c>@</c>), matching the rendered
    /// call name.
    ///
    /// This is a deliberate over-approximation of lexical scope: it treats every
    /// parameter/local anywhere in the method (including sibling lambdas) as a
    /// shadow, so a rare sibling-lambda name collision keeps a call qualified that
    /// could in principle be bare. That only costs fidelity (the qualified form is
    /// always valid), whereas missing a real shadow would rebind the call and
    /// produce wrong or uncompilable C#; the guard errs toward qualifying.
    /// </summary>
    bool IsStaticCallNameShadowed(string escapedName)
    {
        // Synthetic locals are generated during rendering; check the live set so a
        // name emitted before this call (regardless of the cache below) is seen.
        // These names are never keywords, so their spelling equals escapedName.
        if (_syntheticLocalNames.Contains(escapedName))
            return true;
        if (_staticScopeShadowNames is null)
        {
            _staticScopeShadowNames = new HashSet<string>(StringComparer.Ordinal);
            // CurrentScopeNames aggregates every binder in scope for this printer:
            // the enclosing method's parameters and locals, names inherited from an
            // enclosing printer (when this renders a lambda/local-function body with
            // its own scope), nested lambda/local-function parameters, and the
            // printer's own synthetic locals (stack slots S_n, switch temps). It
            // mixes raw and escaped names, so the normalizer has to be idempotent
            // on an already-escaped spelling: ContainedIdentifier maps both `int`
            // and `@int` to `@int`, which is what the rendered call name carries.
            // SafeIdentifier would NOT work here despite being the spelling
            // SourceMethodName uses -- it is not idempotent, and rewrites `@int` to
            // `__int`, which matches nothing.
            //
            // That leaves one residual mismatch, pre-existing and deliberately not
            // widened here: an unspellable binder stays `<>c` in this set while
            // SourceMethodName sanitizes the call name to `___c`, so a shadow by an
            // unspellable name is missed. The IsUnspeakableName guard in
            // RecordBareCallName covers that for the recording path; the remaining
            // hole is tracked separately rather than fixed inside a security change
            // whose corpus evidence is byte-neutrality.
            foreach (var name in CurrentScopeNames())
                _staticScopeShadowNames.Add(CSharpNaming.ContainedIdentifier(name));
        }
        return _staticScopeShadowNames.Contains(escapedName);
    }

    string DeclarationTypeText(TypeRef type, IrExpression initializer)
        // An anonymous type has no spellable name, so `var` is mandatory here — not a
        // taste call. Otherwise the declaration keeps its explicit type unless its
        // style bucket is enabled and the initializer proves exact inference.
        => (initializer is AnonymousObject anonymous && type.Equals(anonymous.Type))
            || SpellVar(type, initializer)
            ? "var"
            : TypeText(type);

    /// <summary>
    /// Renders a declaration initializer. A <c>var</c> declaration cannot also use
    /// target-typed <c>new()</c> (CS8754), so the same decision that spells
    /// <c>var</c> suppresses that shortener and retains explicit <c>new T(...)</c>.
    /// </summary>
    string DeclarationInitializerText(TypeRef type, IrExpression initializer)
        => InitializerText(initializer, type, SpellVar(type, initializer) ? null : type);

    /// <summary>
    /// Whether the enabled editorconfig-style bucket spells this declaration with
    /// <c>var</c>. Built-in, apparent non-built-in, and elsewhere form a disjoint
    /// partition in that order. Every bucket shares the exact-inference gate: the
    /// initializer's rendered C# natural type must be exactly the declared type.
    /// </summary>
    bool SpellVar(TypeRef type, IrExpression initializer)
    {
        if (!VarInfersDeclaredType(type, initializer))
            return false;
        if (IsBuiltInType(type))
            return _options.PreferVarForBuiltInTypes;
        return TypeIsApparent(type, initializer)
            ? _options.PreferVarWhenTypeApparent
            : _options.PreferVarElsewhere;
    }

    /// <summary>
    /// Proves that replacing the explicit declaration type with <c>var</c> preserves
    /// the local's type. <see cref="IrExpression.ResultType"/> is not sufficient by
    /// itself: constants can be retagged by their sink, coercions can render as bare
    /// implicit conversions, and several raised forms are target-typed. This is an
    /// allow list of renderings whose natural type the printer owns; unknown forms
    /// decline rather than failing open as new IR nodes are added.
    /// </summary>
    static bool VarInfersDeclaredType(TypeRef type, IrExpression initializer)
    {
        // `dynamic` erases to System.Object at every nesting depth. Calls and member
        // reads do not yet retain the full DynamicAttribute transform, so matching
        // erased TypeRefs are not proof that `var` preserves the authored static type.
        if (ContainsSystemObjectType(type) && !ErasedObjectTypeIsProvenBySyntax(type, initializer))
        {
            return false;
        }

        TypeRef? inferred = initializer switch
        {
            Constant constant => ConstantNaturalType(constant),
            NewObject or ObjectInitializerExpression or WithExpression
                or NewArray or ArrayLiteral or CastClass or UnboxAny
                or TypeOf or SizeOf or DefaultValue
                or InterpolatedStringExpression or DelegateCreation
                or Call or CallIndirect or LocalFunctionInvocation
                or LoadArgument { IsDynamic: false } or LoadLocal
                or LoadField { Field.IsDynamic: false } or LoadProperty
                or Binary or Comparison or LogicalBinary or LogicalNot or TupleBinaryExpression
                or ArrayLength or RangeExpression or IndexFromEnd or SliceExpression
                or AwaitExpression or IncrementDecrement or IsPattern
                => EffectiveType(initializer),
            _ => null,
        };
        return inferred?.Equals(type) == true;
    }

    static bool ContainsSystemObjectType(TypeRef type)
    {
        if (IsSystemObjectType(type))
            return true;
        if (type.ElementType is { } element && ContainsSystemObjectType(element))
            return true;
        return type.TypeArguments.Any(ContainsSystemObjectType);
    }

    static bool ErasedObjectTypeIsProvenBySyntax(TypeRef type, IrExpression initializer)
        => initializer is NewObject or ObjectInitializerExpression
            or NewArray or ArrayLiteral or CastClass or UnboxAny
            or DefaultValue or DelegateCreation or LoadLocal
            || IsSystemObjectType(type) && initializer is LoadArgument { IsDynamic: false };

    /// <summary>The type C# infers for the literal text emitted by <see cref="ConstantText"/>.</summary>
    static TypeRef? ConstantNaturalType(Constant constant)
        => constant.Value switch
        {
            string => TypeRef.CoreLib("System", "String"),
            bool => TypeRef.CoreLib("System", "Boolean"),
            char => TypeRef.CoreLib("System", "Char"),
            int => TypeRef.CoreLib("System", "Int32"),
            long value when value is >= int.MinValue and <= int.MaxValue => TypeRef.CoreLib("System", "Int32"),
            long value when value is >= 0 and <= uint.MaxValue => TypeRef.CoreLib("System", "UInt32"),
            long => TypeRef.CoreLib("System", "Int64"),
            float => TypeRef.CoreLib("System", "Single"),
            double => TypeRef.CoreLib("System", "Double"),
            _ => null,
        };
}
