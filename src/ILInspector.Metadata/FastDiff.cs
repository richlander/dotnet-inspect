using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>Whether one axis of a Type differs between two Library images.</summary>
public enum FastDiffState
{
    /// <summary>Every fact the axis compares is equal.</summary>
    Unchanged,

    /// <summary>A compared fact differs. The complete diff may present it differently.</summary>
    Changed,

    /// <summary>The axis could not be decided, for example because a row could not be decoded.</summary>
    Indeterminate,
}

/// <summary>
/// The API and Body states of one Type, named by its <c>ApiType.FullName</c>
/// spelling: <c>.</c> between nested names and the metadata backtick arity.
/// </summary>
public sealed record FastDiffTypeState(
    string FullName,
    FastDiffState Api,
    FastDiffState Body);

/// <summary>The work one comparison performed.</summary>
public sealed record FastDiffReceipt(
    int Types,
    int BodiesCompared,
    long IlBytesCompared);

public sealed record FastDiffResult(
    ImmutableArray<FastDiffTypeState> Types,
    FastDiffReceipt Receipt);

/// <summary>
/// Decides, for every Type of one Library image pair, whether its API and its
/// implementation differ, without building either complete diff.
/// </summary>
/// <remarks>
/// <para>
/// The API axis compares the Public API facts of the Type and its members:
/// Type flags, base Type, interfaces, generic
/// parameters and constraints, custom attributes, layout, member signatures
/// and flags, parameter names and defaults, constants, and explicit interface
/// implementations. The Body axis compares the implementation of every
/// member, public or not: IL, exception regions, locals, stack size, and the
/// metadata of non-public members and compiler-generated nested Types.
/// </para>
/// <para>
/// Tokens compare by symbolic name, resolved once per side, never by token
/// number or referenced assembly identity. Compiler-generated nested Types
/// (state machines, closures, local-function holders) belong to their nearest
/// declared Type. Top-level compiler-generated Types are not reported; a
/// body that references them still compares their names.
/// </para>
/// <para>
/// <c>Unchanged</c> is sound for the compared facts. <c>Changed</c> may
/// over-report: a reordered member, a renumbered compiler-generated name, or
/// an IL encoding that canonical comparison treats as equal. A row that cannot
/// be decoded makes the unsettled axes of its Type <c>Indeterminate</c>.
/// </para>
/// </remarks>
public static class FastDiff
{
    public static FastDiffResult Compare(PEReader before, PEReader after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var a = new Side(before);
        var b = new Side(after);
        Dictionary<string, Unit> unitsA = a.Units();
        Dictionary<string, Unit> unitsB = b.Units();

        var states = ImmutableArray.CreateBuilder<FastDiffTypeState>(
            unitsA.Count + unitsB.Count);
        var work = new Work();
        foreach ((string key, Unit unitA) in unitsA)
        {
            if (!unitsB.TryGetValue(key, out Unit? unitB))
            {
                states.Add(OneSided(a, unitA));
                continue;
            }
            states.Add(CompareUnit(a, unitA, b, unitB, work));
        }
        foreach ((string key, Unit unitB) in unitsB)
        {
            if (!unitsA.ContainsKey(key))
                states.Add(OneSided(b, unitB));
        }

        states.Sort((x, y) => string.CompareOrdinal(x.FullName, y.FullName));
        return new FastDiffResult(
            states.ToImmutable(),
            new FastDiffReceipt(states.Count, work.Bodies, work.IlBytes));
    }

    /// <summary>
    /// Returns the first compared fact that differs on each axis of one Type,
    /// or null for an axis whose facts are equal. A body difference inside IL
    /// is named by its method.
    /// </summary>
    public static (string? Api, string? Body) Explain(
        PEReader before,
        PEReader after,
        string fullName)
    {
        var a = new Side(before);
        var b = new Side(after);
        Unit? unitA = a.Units().Values.FirstOrDefault(unit => unit.FullName == fullName);
        Unit? unitB = b.Units().Values.FirstOrDefault(unit => unit.FullName == fullName);
        if (unitA is null || unitB is null)
            return ("one-sided", "one-sided");

        string? api = FirstDifference(a.ApiCensus(unitA), b.ApiCensus(unitB));
        bool ownerVisible = a.IsVisible(unitA.Owner) || b.IsVisible(unitB.Owner);
        string? body = FirstDifference(
            a.BodyCensus(unitA, ownerVisible, out var methodsA),
            b.BodyCensus(unitB, ownerVisible, out var methodsB));
        if (body is null)
        {
            var work = new Work();
            foreach ((string key, MethodDefinitionHandle methodA) in methodsA)
            {
                if (methodsB.TryGetValue(key, out MethodDefinitionHandle methodB)
                    && !BodyEqual(a, methodA, b, methodB, work))
                {
                    body = "IL " + key;
                    break;
                }
            }
        }
        return (api, body);
    }

    static string? FirstDifference(List<string> before, List<string> after)
    {
        var onlyBefore = before.Except(after, StringComparer.Ordinal).FirstOrDefault();
        var onlyAfter = after.Except(before, StringComparer.Ordinal).FirstOrDefault();
        return onlyBefore is null && onlyAfter is null
            ? null
            : $"- {onlyBefore}\n+ {onlyAfter}";
    }

    static FastDiffTypeState OneSided(Side side, Unit unit)
    {
        FastDiffState api;
        try
        {
            api = side.IsVisible(unit.Owner) ? FastDiffState.Changed : FastDiffState.Unchanged;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            api = FastDiffState.Indeterminate;
        }
        return new FastDiffTypeState(unit.FullName, api, FastDiffState.Changed);
    }

    static FastDiffTypeState CompareUnit(Side a, Unit unitA, Side b, Unit unitB, Work work)
    {
        FastDiffState api = FastDiffState.Indeterminate;
        FastDiffState body = FastDiffState.Indeterminate;
        try
        {
            api = a.ApiCensus(unitA).SequenceEqual(b.ApiCensus(unitB), StringComparer.Ordinal)
                ? FastDiffState.Unchanged
                : FastDiffState.Changed;
            body = BodiesEqual(a, unitA, b, unitB, work)
                ? FastDiffState.Unchanged
                : FastDiffState.Changed;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
        }
        return new FastDiffTypeState(unitA.FullName, api, body);
    }

    static bool BodiesEqual(Side a, Unit unitA, Side b, Unit unitB, Work work)
    {
        // A Type visible on either side partitions its facts as visible on
        // both, so a visibility change is an API fact only.
        bool ownerVisible = a.IsVisible(unitA.Owner) || b.IsVisible(unitB.Owner);
        if (!a.BodyCensus(unitA, ownerVisible, out Dictionary<string, MethodDefinitionHandle> methodsA)
                .SequenceEqual(
                    b.BodyCensus(unitB, ownerVisible, out Dictionary<string, MethodDefinitionHandle> methodsB),
                    StringComparer.Ordinal))
        {
            return false;
        }

        // Equal censuses declare the same non-public members. A public method
        // on one side only is an API fact; its body is not compared.
        foreach ((string key, MethodDefinitionHandle methodA) in methodsA)
        {
            if (!methodsB.TryGetValue(key, out MethodDefinitionHandle methodB))
                continue;
            work.Bodies++;
            if (!BodyEqual(a, methodA, b, methodB, work))
                return false;
        }
        return true;
    }

    static bool BodyEqual(
        Side a,
        MethodDefinitionHandle handleA,
        Side b,
        MethodDefinitionHandle handleB,
        Work work)
    {
        MethodDefinition methodA = a.Md.GetMethodDefinition(handleA);
        MethodDefinition methodB = b.Md.GetMethodDefinition(handleB);
        if ((methodA.RelativeVirtualAddress == 0) != (methodB.RelativeVirtualAddress == 0))
            return false;
        if (methodA.RelativeVirtualAddress == 0)
            return true;

        MethodBodyBlock bodyA = a.Pe.GetMethodBody(methodA.RelativeVirtualAddress);
        MethodBodyBlock bodyB = b.Pe.GetMethodBody(methodB.RelativeVirtualAddress);
        if (bodyA.MaxStack != bodyB.MaxStack
            || bodyA.LocalVariablesInitialized != bodyB.LocalVariablesInitialized
            || bodyA.Size != bodyB.Size
            || bodyA.LocalSignature.IsNil != bodyB.LocalSignature.IsNil
            || (!bodyA.LocalSignature.IsNil
                && a.Key(bodyA.LocalSignature) != b.Key(bodyB.LocalSignature)))
        {
            return false;
        }

        ImmutableArray<ExceptionRegion> regionsA = bodyA.ExceptionRegions;
        ImmutableArray<ExceptionRegion> regionsB = bodyB.ExceptionRegions;
        if (regionsA.Length != regionsB.Length)
            return false;
        for (int i = 0; i < regionsA.Length; i++)
        {
            ExceptionRegion x = regionsA[i];
            ExceptionRegion y = regionsB[i];
            if (x.Kind != y.Kind
                || x.TryOffset != y.TryOffset
                || x.TryLength != y.TryLength
                || x.HandlerOffset != y.HandlerOffset
                || x.HandlerLength != y.HandlerLength
                || x.FilterOffset != y.FilterOffset
                || x.CatchType.IsNil != y.CatchType.IsNil
                || (!x.CatchType.IsNil && a.Key(x.CatchType) != b.Key(y.CatchType)))
            {
                return false;
            }
        }

        BlobReader readerA = bodyA.GetILReader();
        BlobReader readerB = bodyB.GetILReader();
        work.IlBytes += readerA.Length;
        while (readerA.RemainingBytes > 0)
        {
            int opCode = readerA.ReadByte();
            if (opCode != readerB.ReadByte())
                return false;
            if (opCode == 0xFE)
            {
                opCode = 0xFE00 | readerA.ReadByte();
                if (opCode != (0xFE00 | readerB.ReadByte()))
                    return false;
            }

            switch (OperandOf((ILOpCode)opCode))
            {
                case Operand.None:
                    break;
                case Operand.Int8:
                    if (readerA.ReadByte() != readerB.ReadByte())
                        return false;
                    break;
                case Operand.Int16:
                    if (readerA.ReadInt16() != readerB.ReadInt16())
                        return false;
                    break;
                case Operand.Int32:
                    if (readerA.ReadInt32() != readerB.ReadInt32())
                        return false;
                    break;
                case Operand.Int64:
                    if (readerA.ReadInt64() != readerB.ReadInt64())
                        return false;
                    break;
                case Operand.Token:
                    if (a.Key(MetadataTokens.EntityHandle(readerA.ReadInt32()))
                        != b.Key(MetadataTokens.EntityHandle(readerB.ReadInt32())))
                    {
                        return false;
                    }
                    break;
                case Operand.String:
                    if (a.Md.GetUserString(MetadataTokens.UserStringHandle(readerA.ReadInt32() & 0xFFFFFF))
                        != b.Md.GetUserString(MetadataTokens.UserStringHandle(readerB.ReadInt32() & 0xFFFFFF)))
                    {
                        return false;
                    }
                    break;
                case Operand.Switch:
                    int count = readerA.ReadInt32();
                    if (count != readerB.ReadInt32())
                        return false;
                    for (int i = 0; i < count; i++)
                    {
                        if (readerA.ReadInt32() != readerB.ReadInt32())
                            return false;
                    }
                    break;
            }
        }
        return true;
    }

    enum Operand
    {
        None,
        Int8,
        Int16,
        Int32,
        Int64,
        Token,
        String,
        Switch,
    }

    // The `no.` prefix (0xFE 0x19) takes a one-byte operand and has no ILOpCode member.
    const ILOpCode NoPrefix = (ILOpCode)0xFE19;

    static Operand OperandOf(ILOpCode opCode) => opCode switch
    {
        ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
            or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
            or ILOpCode.Ldc_i4_s or ILOpCode.Unaligned or NoPrefix
            or ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s
            or ILOpCode.Beq_s or ILOpCode.Bge_s or ILOpCode.Bgt_s
            or ILOpCode.Ble_s or ILOpCode.Blt_s or ILOpCode.Bne_un_s
            or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s or ILOpCode.Ble_un_s
            or ILOpCode.Blt_un_s or ILOpCode.Leave_s => Operand.Int8,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
            or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => Operand.Int16,
        ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4
            or ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue
            or ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt
            or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_un
            or ILOpCode.Bge_un or ILOpCode.Bgt_un or ILOpCode.Ble_un
            or ILOpCode.Blt_un or ILOpCode.Leave => Operand.Int32,
        ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => Operand.Int64,
        ILOpCode.Ldstr => Operand.String,
        ILOpCode.Switch => Operand.Switch,
        ILOpCode.Jmp or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt
            or ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Newobj
            or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Unbox
            or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
            or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
            or ILOpCode.Stobj or ILOpCode.Box or ILOpCode.Newarr
            or ILOpCode.Ldelema or ILOpCode.Ldelem or ILOpCode.Stelem
            or ILOpCode.Unbox_any or ILOpCode.Refanyval or ILOpCode.Mkrefany
            or ILOpCode.Ldtoken or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
            or ILOpCode.Initobj or ILOpCode.Constrained or ILOpCode.Sizeof => Operand.Token,
        _ => Operand.None,
    };

    static bool IsMalformed(Exception ex)
        => ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException
            or ArgumentException
            or IndexOutOfRangeException;

    sealed class Work
    {
        public int Bodies;
        public long IlBytes;
    }

    sealed record Unit(string FullName, TypeDefinitionHandle Owner, List<TypeDefinitionHandle> Generated);

    sealed class Side
    {
        readonly Dictionary<int, string> _keys = [];
        readonly Dictionary<int, string> _displayNames = [];
        readonly SignatureKeys _signatures;
        readonly HashSet<MethodDefinitionHandle> _explicitImplementations = [];

        public Side(PEReader pe)
        {
            Pe = pe;
            Md = MetadataFormatAdmission.GetMetadataReader(pe);
            _signatures = new SignatureKeys(this);
            foreach (TypeDefinitionHandle type in Md.TypeDefinitions)
            {
                foreach (MethodImplementationHandle handle in
                    Md.GetTypeDefinition(type).GetMethodImplementations())
                {
                    if (Md.GetMethodImplementation(handle).MethodBody is
                        { Kind: HandleKind.MethodDefinition } body)
                    {
                        _explicitImplementations.Add((MethodDefinitionHandle)body);
                    }
                }
            }
        }

        public PEReader Pe { get; }

        public MetadataReader Md { get; }

        public Dictionary<string, Unit> Units()
        {
            var units = new Dictionary<string, Unit>(StringComparer.Ordinal);
            var generated = new List<(TypeDefinitionHandle Type, TypeDefinitionHandle Owner)>();
            foreach (TypeDefinitionHandle handle in Md.TypeDefinitions)
            {
                if (MetadataTokens.GetRowNumber(handle) == 1)
                    continue;

                // A Type with a compiler-generated Type in its declaring chain
                // belongs to the nearest declared Type above that generated
                // Type; under a top-level generated Type it belongs to none.
                TypeDefinitionHandle owner = handle;
                TypeDefinitionHandle generatedAncestor = default;
                int depth = 0;
                for (TypeDefinitionHandle current = handle; !current.IsNil;
                    current = Md.GetTypeDefinition(current).GetDeclaringType())
                {
                    if (++depth > Md.TypeDefinitions.Count)
                        throw new BadImageFormatException("Nested Type chain does not terminate.");
                    if (IsGeneratedName(Md.GetTypeDefinition(current)))
                        generatedAncestor = current;
                }
                if (generatedAncestor.IsNil)
                {
                    units[TypeKey(handle)] = new Unit(DisplayName(handle), handle, []);
                    continue;
                }
                owner = Md.GetTypeDefinition(generatedAncestor).GetDeclaringType();
                while (!owner.IsNil && IsGeneratedName(Md.GetTypeDefinition(owner)))
                    owner = Md.GetTypeDefinition(owner).GetDeclaringType();
                if (!owner.IsNil)
                    generated.Add((handle, owner));
            }
            foreach ((TypeDefinitionHandle type, TypeDefinitionHandle owner) in generated)
            {
                if (units.TryGetValue(TypeKey(owner), out Unit? unit))
                    unit.Generated.Add(type);
            }
            return units;
        }

        bool IsGeneratedName(TypeDefinition type)
            => Md.StringComparer.StartsWith(type.Name, "<");

        /// <remarks>
        /// Visibility is the Type's own row, not its declaring chain: the
        /// Public API surface admits a public nested Type of a non-public Type,
        /// so the API axis must compare it.
        /// </remarks>
        public bool IsVisible(TypeDefinitionHandle handle)
            => (Md.GetTypeDefinition(handle).Attributes & TypeAttributes.VisibilityMask)
                    is TypeAttributes.Public
                    or TypeAttributes.NestedPublic
                    or TypeAttributes.NestedFamily
                    or TypeAttributes.NestedFamORAssem;

        bool IsVisible(MethodDefinitionHandle handle, MethodDefinition method)
            => IsVisibleAccess((int)(method.Attributes & MethodAttributes.MemberAccessMask))
                || _explicitImplementations.Contains(handle);

        bool IsVisible(FieldDefinition field)
            => IsVisibleAccess((int)(field.Attributes & FieldAttributes.FieldAccessMask));

        // Public, Family, and FamORAssem share their encoding across method and field access.
        static bool IsVisibleAccess(int access)
            => access is (int)MethodAttributes.Public
                or (int)MethodAttributes.Family
                or (int)MethodAttributes.FamORAssem;

        /// <summary>
        /// Facts of visible Types and members that the API diff does not
        /// present carry this prefix and belong to the Body axis. Custom
        /// attributes stay API facts: compiler-emitted ones such as nullable
        /// annotations shape the rendered signatures the API diff compares.
        /// </summary>
        const string NonApiFact = "~";

        public List<string> ApiCensus(Unit unit)
        {
            var census = new List<string>();
            bool visible = IsVisible(unit.Owner);
            census.Add(visible ? "visible" : "hidden");
            if (!visible)
                return census;
            census.AddRange(VisibleFacts(unit).Where(fact => !fact.StartsWith(NonApiFact, StringComparison.Ordinal)));
            census.Sort(StringComparer.Ordinal);
            return census;
        }

        List<string> VisibleFacts(Unit unit)
        {
            var facts = new List<string>();
            TypeFacts(facts, unit.Owner);
            MemberFacts(facts, unit.Owner, api: true, out _);
            return facts;
        }

        public List<string> BodyCensus(
            Unit unit,
            bool visible,
            out Dictionary<string, MethodDefinitionHandle> methods)
        {
            var census = new List<string>();
            methods = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
            if (!visible)
                TypeFacts(census, unit.Owner);
            else
                census.AddRange(VisibleFacts(unit).Where(fact => fact.StartsWith(NonApiFact, StringComparison.Ordinal)));
            MemberFacts(census, unit.Owner, api: !visible ? null : false, out var ownerMethods);
            foreach ((string key, MethodDefinitionHandle method) in ownerMethods)
                methods[key] = method;
            foreach (TypeDefinitionHandle generated in unit.Generated)
            {
                TypeFacts(census, generated);
                MemberFacts(census, generated, api: null, out var generatedMethods);
                foreach ((string key, MethodDefinitionHandle method) in generatedMethods)
                    methods[key] = method;
            }
            census.Sort(StringComparer.Ordinal);
            return census;
        }

        void TypeFacts(List<string> census, TypeDefinitionHandle handle)
        {
            TypeDefinition type = Md.GetTypeDefinition(handle);
            string name = TypeKey(handle);
            census.Add(
                $"T {name} {(int)(type.Attributes & ~TypeAttributes.BeforeFieldInit)} "
                    + (type.BaseType.IsNil ? "" : Key(type.BaseType)));
            // beforefieldinit only changes when a static constructor runs.
            if ((type.Attributes & TypeAttributes.BeforeFieldInit) != 0
                && type.GetMethods().Any(method =>
                    Md.StringComparer.Equals(Md.GetMethodDefinition(method).Name, ".cctor")))
            {
                census.Add($"{NonApiFact}beforefieldinit {name}");
            }
            foreach (InterfaceImplementationHandle implementation in type.GetInterfaceImplementations())
            {
                InterfaceImplementation value = Md.GetInterfaceImplementation(implementation);
                census.Add($"I {name} {Key(value.Interface)}");
                Attributes(census, value.GetCustomAttributes(), $"I {name} {Key(value.Interface)}");
            }
            GenericParameters(census, type.GetGenericParameters(), name);
            Attributes(census, type.GetCustomAttributes(), name);
            TypeLayout layout = type.GetLayout();
            if (!layout.IsDefault)
                census.Add($"L {name} {layout.PackingSize} {layout.Size}");
            foreach (MethodImplementationHandle implementation in type.GetMethodImplementations())
            {
                MethodImplementation value = Md.GetMethodImplementation(implementation);
                census.Add($"X {name} {Key(value.MethodBody)} {Key(value.MethodDeclaration)}");
            }
        }

        /// <param name="api">
        /// True for the public members only, false for the non-public members,
        /// and null for every member.
        /// </param>
        void MemberFacts(
            List<string> census,
            TypeDefinitionHandle handle,
            bool? api,
            out List<(string Key, MethodDefinitionHandle Method)> methods)
        {
            TypeDefinition type = Md.GetTypeDefinition(handle);
            string name = TypeKey(handle);
            methods = [];
            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = Md.GetFieldDefinition(fieldHandle);
                if (api is { } wanted && IsVisible(field) != wanted)
                    continue;
                string key = Key(fieldHandle);
                census.Add($"F {key} {(int)field.Attributes} {Constant(field.GetDefaultValue())} {field.GetOffset()}");
                Attributes(census, field.GetCustomAttributes(), key);
            }
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = Md.GetMethodDefinition(methodHandle);
                string key = Key(methodHandle);
                methods.Add((key, methodHandle));
                if (api is { } wanted && IsVisible(methodHandle, method) != wanted)
                    continue;
                census.Add($"M {key} {(int)method.Attributes} {(int)method.ImplAttributes}");
                GenericParameters(census, method.GetGenericParameters(), key);
                Attributes(census, method.GetCustomAttributes(), key);
                foreach (ParameterHandle parameterHandle in method.GetParameters())
                {
                    Parameter parameter = Md.GetParameter(parameterHandle);
                    string parameterKey = $"{key}#{parameter.SequenceNumber}";
                    census.Add(
                        $"A {parameterKey} {Md.GetString(parameter.Name)} {(int)parameter.Attributes} "
                            + Constant(parameter.GetDefaultValue()));
                    Attributes(census, parameter.GetCustomAttributes(), parameterKey);
                }
                MethodImport import = method.GetImport();
                if (!import.Module.IsNil)
                {
                    census.Add(
                        $"N {key} {Md.GetString(import.Name)} "
                            + $"{Md.GetString(Md.GetModuleReference(import.Module).Name)} {(int)import.Attributes}");
                }
            }
            foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
            {
                PropertyDefinition property = Md.GetPropertyDefinition(propertyHandle);
                PropertyAccessors accessors = property.GetAccessors();
                if (api is { } wanted
                    && AccessorVisible(accessors.Getter, accessors.Setter, accessors.Others) != wanted)
                {
                    continue;
                }
                string key =
                    $"{name}::{Md.GetString(property.Name)}:{PropertySignature(property)}";
                census.Add(
                    $"P {key} {(int)property.Attributes} {Constant(property.GetDefaultValue())} "
                        + $"{AccessorKey(accessors.Getter)} {AccessorKey(accessors.Setter)}");
                Attributes(census, property.GetCustomAttributes(), key);
            }
            foreach (EventDefinitionHandle eventHandle in type.GetEvents())
            {
                EventDefinition value = Md.GetEventDefinition(eventHandle);
                EventAccessors accessors = value.GetAccessors();
                if (api is { } wanted
                    && AccessorVisible(accessors.Adder, accessors.Remover, accessors.Others) != wanted)
                {
                    continue;
                }
                string key = $"{name}::{Md.GetString(value.Name)}:{Key(value.Type)}";
                census.Add(
                    $"E {key} {(int)value.Attributes} {AccessorKey(accessors.Adder)} "
                        + $"{AccessorKey(accessors.Remover)} {AccessorKey(accessors.Raiser)}");
                Attributes(census, value.GetCustomAttributes(), key);
            }
        }

        bool AccessorVisible(
            MethodDefinitionHandle first,
            MethodDefinitionHandle second,
            ImmutableArray<MethodDefinitionHandle> others)
        {
            foreach (MethodDefinitionHandle accessor in (MethodDefinitionHandle[])[first, second, .. others])
            {
                if (!accessor.IsNil && IsVisible(accessor, Md.GetMethodDefinition(accessor)))
                    return true;
            }
            return false;
        }

        string AccessorKey(MethodDefinitionHandle accessor)
            => accessor.IsNil ? "-" : Key(accessor);

        void GenericParameters(List<string> census, GenericParameterHandleCollection parameters, string owner)
        {
            foreach (GenericParameterHandle handle in parameters)
            {
                GenericParameter parameter = Md.GetGenericParameter(handle);
                string key = $"{owner}<{parameter.Index}";
                census.Add($"G {key} {Md.GetString(parameter.Name)} {(int)parameter.Attributes}");
                foreach (GenericParameterConstraintHandle constraintHandle in parameter.GetConstraints())
                {
                    GenericParameterConstraint constraint = Md.GetGenericParameterConstraint(constraintHandle);
                    census.Add($"C {key} {Key(constraint.Type)}");
                    Attributes(census, constraint.GetCustomAttributes(), $"C {key} {Key(constraint.Type)}");
                }
                Attributes(census, parameter.GetCustomAttributes(), key);
            }
        }

        void Attributes(List<string> census, CustomAttributeHandleCollection attributes, string owner)
        {
            foreach (CustomAttributeHandle handle in attributes)
            {
                CustomAttribute attribute = Md.GetCustomAttribute(handle);
                census.Add(
                    $"@ {owner} {Key(attribute.Constructor)} "
                        + Convert.ToHexString(Md.GetBlobBytes(attribute.Value)));
            }
        }

        string Constant(ConstantHandle handle)
        {
            if (handle.IsNil)
                return "-";
            Constant constant = Md.GetConstant(handle);
            return $"{(int)constant.TypeCode}:{Convert.ToHexString(Md.GetBlobBytes(constant.Value))}";
        }

        string PropertySignature(PropertyDefinition property)
        {
            MethodSignature<string> signature = property.DecodeSignature(_signatures, null);
            return $"{signature.Header.IsInstance}({string.Join(",", signature.ParameterTypes)}){signature.ReturnType}";
        }

        /// <summary>The symbolic name of a Type, with <c>/</c> between nested names.</summary>
        public string TypeKey(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_keys.TryGetValue(token, out string? key))
                return key;
            TypeDefinition type = Md.GetTypeDefinition(handle);
            TypeDefinitionHandle declaring = type.GetDeclaringType();
            key = declaring.IsNil
                ? (type.Namespace.IsNil ? "" : Md.GetString(type.Namespace) + ".") + Md.GetString(type.Name)
                : TypeKey(declaring) + "/" + Md.GetString(type.Name);
            _keys[token] = key;
            return key;
        }

        string DisplayName(TypeDefinitionHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_displayNames.TryGetValue(token, out string? name))
                return name;
            TypeDefinition type = Md.GetTypeDefinition(handle);
            TypeDefinitionHandle declaring = type.GetDeclaringType();
            name = declaring.IsNil
                ? (type.Namespace.IsNil ? "" : Md.GetString(type.Namespace) + ".") + Md.GetString(type.Name)
                : DisplayName(declaring) + "." + Md.GetString(type.Name);
            _displayNames[token] = name;
            return name;
        }

        /// <summary>The symbolic name of a metadata entity, resolved once per side.</summary>
        public string Key(EntityHandle handle)
        {
            int token = MetadataTokens.GetToken(handle);
            if (_keys.TryGetValue(token, out string? key))
                return key;
            key = handle.Kind switch
            {
                HandleKind.TypeDefinition => TypeKey((TypeDefinitionHandle)handle),
                HandleKind.TypeReference => TypeReferenceKey((TypeReferenceHandle)handle),
                HandleKind.TypeSpecification =>
                    Md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(_signatures, null),
                HandleKind.MethodDefinition => MethodKey((MethodDefinitionHandle)handle),
                HandleKind.FieldDefinition => FieldKey((FieldDefinitionHandle)handle),
                HandleKind.MemberReference => MemberReferenceKey((MemberReferenceHandle)handle),
                HandleKind.MethodSpecification => MethodSpecificationKey((MethodSpecificationHandle)handle),
                HandleKind.StandaloneSignature => StandaloneKey((StandaloneSignatureHandle)handle),
                _ => throw new BadImageFormatException(
                    $"Unsupported metadata token 0x{token:X8} in a compared fact."),
            };
            _keys[token] = key;
            return key;
        }

        string TypeReferenceKey(TypeReferenceHandle handle)
        {
            TypeReference type = Md.GetTypeReference(handle);
            string name = (type.Namespace.IsNil ? "" : Md.GetString(type.Namespace) + ".")
                + Md.GetString(type.Name);
            return type.ResolutionScope.Kind == HandleKind.TypeReference
                ? Key(type.ResolutionScope) + "/" + name
                : name;
        }

        string MethodKey(MethodDefinitionHandle handle)
        {
            MethodDefinition method = Md.GetMethodDefinition(handle);
            MethodSignature<string> signature = method.DecodeSignature(_signatures, null);
            return $"{TypeKey(method.GetDeclaringType())}::{Md.GetString(method.Name)}"
                + $"`{signature.GenericParameterCount}({string.Join(",", signature.ParameterTypes)}){signature.ReturnType}";
        }

        string FieldKey(FieldDefinitionHandle handle)
        {
            FieldDefinition field = Md.GetFieldDefinition(handle);
            return $"{TypeKey(field.GetDeclaringType())}::{Md.GetString(field.Name)}:"
                + field.DecodeSignature(_signatures, null);
        }

        string MemberReferenceKey(MemberReferenceHandle handle)
        {
            MemberReference member = Md.GetMemberReference(handle);
            string parent = Key(member.Parent);
            if (member.GetKind() == MemberReferenceKind.Field)
                return $"{parent}::{Md.GetString(member.Name)}:{member.DecodeFieldSignature(_signatures, null)}";
            MethodSignature<string> signature = member.DecodeMethodSignature(_signatures, null);
            return $"{parent}::{Md.GetString(member.Name)}`{signature.GenericParameterCount}"
                + $"({string.Join(",", signature.ParameterTypes)}){signature.ReturnType}";
        }

        string MethodSpecificationKey(MethodSpecificationHandle handle)
        {
            MethodSpecification specification = Md.GetMethodSpecification(handle);
            return Key(specification.Method)
                + "<" + string.Join(",", specification.DecodeSignature(_signatures, null)) + ">";
        }

        string StandaloneKey(StandaloneSignatureHandle handle)
        {
            StandaloneSignature signature = Md.GetStandaloneSignature(handle);
            if (signature.GetKind() == StandaloneSignatureKind.LocalVariables)
                return "L(" + string.Join(",", signature.DecodeLocalSignature(_signatures, null)) + ")";
            MethodSignature<string> method = signature.DecodeMethodSignature(_signatures, null);
            return $"S{(int)method.Header.CallingConvention}({string.Join(",", method.ParameterTypes)}){method.ReturnType}";
        }
    }

    sealed class SignatureKeys(Side side) : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape)
            => $"{elementType}[{shape.Rank}:{string.Join(",", shape.Sizes)}:{string.Join(",", shape.LowerBounds)}]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature)
            => $"fnptr{(int)signature.Header.CallingConvention}({string.Join(",", signature.ParameterTypes)}){signature.ReturnType}";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
            => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";
        public string GetPinnedType(string elementType) => elementType + " pinned";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => side.TypeKey(handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => side.Key(handle);
        public string GetTypeFromSpecification(
            MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => side.Key(handle);
    }
}
