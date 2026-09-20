using System.Globalization;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using DotnetInspector.Services;
using DotnetInspector.RoundTripCompilation;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.DecompilerHarness;

public static partial class CompileBackSourceComposer
{

    sealed class TypeProducer
    {
        public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            MethodRef methodRef)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var typeIdentity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            if (TryFindPropertyForAccessor(reader, typeDef, methodRef) is { } propertyHandle)
            {
                var requirement = PropertyRequirement(
                    reader,
                    typeDef,
                    typeIdentity,
                    propertyHandle,
                    methodRef.Name);
                return requirement is null ? null : requirement with
                {
                    RequiresUnsafeModifier = methodRef.RequiresUnsafe,
                };
            }
            if (TryFindMethod(reader, typeDef, methodRef) is { } methodHandle)
            {
                var requirement = MethodRequirement(
                    reader,
                    typeDef,
                    typeIdentity,
                    methodHandle);
                return requirement is null ? null : requirement with
                {
                    RequiresUnsafeModifier = methodRef.RequiresUnsafe,
                };
            }
            return null;
        }

        public static CompileBackMemberRequirement? TryCreateClosureMemberRequirement(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            FieldRef fieldRef)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var typeIdentity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            if (FindField(reader, typeDef, fieldRef.Name) is not { } fieldHandle)
                return null;
            return FieldRequirement(reader, typeDef, typeIdentity, fieldHandle);
        }

        public static CompileBackMemberRequirement? TryCreateRecordEqualityContractRequirement(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var typeIdentity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                if (reader.GetString(property.Name) != "EqualityContract")
                    continue;
                var accessors = property.GetAccessors();
                if (accessors.Getter.IsNil)
                    continue;
                var getter = reader.GetMethodDefinition(accessors.Getter);
                if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, getter.GetCustomAttributes()))
                    continue;
                return PropertyRequirement(reader, typeDef, typeIdentity, propertyHandle, reader.GetString(getter.Name), factId: "record-equality-contract");
            }

            return null;
        }

        static CompileBackMemberRequirement? FieldRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            FieldDefinitionHandle fieldHandle)
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            string fieldType;
            try
            {
                fieldType = GuardedSignatureText.FieldText(reader, field, GenericContext.ForType(reader, typeDef));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }

            string fieldName = reader.GetString(field.Name);
            if (fieldName.Contains('.', StringComparison.Ordinal))
            {
                return null;
            }

            string? fixedBufferSignature = FixedBufferDeclarationSignature(reader, field, fieldName);
            if (fixedBufferSignature is null && IsUnsupportedSurfaceSignature(fieldType))
                return null;

            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, Identifier(fieldName), 0, $"field {fieldType}"),
                CompileBackMemberKind.Field,
                field.Attributes.HasFlag(FieldAttributes.Static),
                [],
                CompileBackTypeSignature.Display(fieldType),
                [],
                TryFormatConstantField(reader, field, out var constant)
                    ? CompileBackStubBodyKind.TargetBody
                    : CompileBackStubBodyKind.None,
                constant,
                [new CompileBackFact("metadata", "typed-closure-field", fieldName)],
                DeclarationSignature: fixedBufferSignature,
                IsReadOnly: field.Attributes.HasFlag(FieldAttributes.InitOnly));
        }

        public static TypeProduction Produce(
            MetadataReader reader,
            IReadOnlyList<CompileBackTypeRequirement> requirements,
            List<CompileBackPlanningDiagnostic> diagnostics,
            bool usesUpdatedMemorySafetyRules)
        {
            var requests = new List<CSharpTypePrintRequest>();
            var producedRequirements = new List<CompileBackTypeRequirement>();
            var requirementsByMetadataName = requirements.ToDictionary(
                requirement => requirement.Type.MetadataFullName,
                requirement => requirement,
                StringComparer.Ordinal);
            var emittedRoots = new HashSet<TypeDefinitionHandle>();
            foreach (var requirement in requirements)
            {
                if (FindType(reader, requirement.Type.MetadataFullName) is not { } handle)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("type identity", "type-not-found", requirement.Type.MetadataFullName));
                    continue;
                }

                var rootHandle = TopLevelRootOf(reader, handle);
                if (!emittedRoots.Add(rootHandle))
                    continue;

                var rootDef = reader.GetTypeDefinition(rootHandle);
                var rootIdentity = CompileBackTypeIdentity.FromDefinition(reader, rootDef);
                if (!requirementsByMetadataName.TryGetValue(rootIdentity.MetadataFullName, out var rootRequirement))
                {
                    rootRequirement = new CompileBackTypeRequirement(
                        rootIdentity,
                        ShellKind(reader, rootDef),
                        RequiredMembers: [],
                        PrimaryConstructor: null,
                        SourceFacts: [new CompileBackFact("metadata", "declaring-closure-type", rootIdentity.FullName)]);
                }

                var rootSpec = BuildSpec(
                    reader,
                    rootHandle,
                    rootRequirement,
                    requirementsByMetadataName,
                    producedRequirements,
                    diagnostics,
                    usesUpdatedMemorySafetyRules);
                requests.Add(TypeShellProducer.BuildPrintRequest(reader, rootSpec));
            }

            return new TypeProduction(requests, producedRequirements);
        }

        public sealed record TypeProduction(
            IReadOnlyList<CSharpTypePrintRequest> Requests,
            IReadOnlyList<CompileBackTypeRequirement> Requirements);

        static PropertyDefinitionHandle? TryFindPropertyForAccessor(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodRef methodRef)
        {
            if (!methodRef.Name.StartsWith("get_", StringComparison.Ordinal)
                && !methodRef.Name.StartsWith("set_", StringComparison.Ordinal))
                return null;

            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                var accessorHandle = methodRef.Name.StartsWith("get_", StringComparison.Ordinal)
                    ? accessors.Getter
                    : accessors.Setter;
                if (accessorHandle.IsNil)
                    continue;
                var accessor = reader.GetMethodDefinition(accessorHandle);
                if (!MethodMatches(reader, typeDef, accessor, methodRef))
                    continue;
                return propertyHandle;
            }

            return null;
        }

        public static MethodDefinitionHandle? TryFindMethod(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodRef methodRef)
        {
            var matches = new List<MethodDefinitionHandle>();
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!MethodMatches(reader, typeDef, method, methodRef))
                    continue;
                matches.Add(methodHandle);
            }

            return matches.Count == 1 ? matches[0] : null;
        }

        static bool MethodMatches(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodDefinition method,
            MethodRef methodRef)
        {
            if (reader.GetString(method.Name) != methodRef.Name)
                return false;
            if (method.GetGenericParameters().Count != methodRef.TypeArguments.Length)
                return false;
            try
            {
                var signature = GuardedDecode.MethodSignature(reader, method, IrImporter.CallerScope(reader, typeDef, method));
                return signature.ParameterTypes.Length == methodRef.ParameterTypes.Length
                    && signature.ParameterTypes.SequenceEqual(methodRef.ParameterTypes);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }

        internal static CompileBackMemberRequirement? PropertyRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            PropertyDefinitionHandle propertyHandle,
            string accessorName,
            string factId = "typed-closure-property")
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var accessors = property.GetAccessors();
            bool hasGetter = !accessors.Getter.IsNil;
            bool hasSetter = !accessors.Setter.IsNil;
            if (!hasGetter && !hasSetter)
                return null;

            string propertyName = reader.GetString(property.Name);
            if (propertyName.Contains('<', StringComparison.Ordinal))
                return null;
            string? explicitInterfaceMemberName = ExplicitInterfaceMemberName(reader, propertyName);

            MetadataPropertyDeclaration propertyDeclaration;
            try
            {
                propertyDeclaration = MetadataDeclarationQuery.GetProperty(reader, typeDef, property);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }

            if (propertyDeclaration.Signature.ReturnType is not { } propertyReturnType
                || IsUnsupportedSurfaceSignature(propertyReturnType))
                return null;

            var accessor = accessorName.StartsWith("get_", StringComparison.Ordinal) ? accessors.Getter : accessors.Setter;
            var accessorMethod = accessor.IsNil ? default : reader.GetMethodDefinition(accessor);
            bool isStatic = !accessor.IsNil && accessorMethod.Attributes.HasFlag(MethodAttributes.Static);
            var returnType = CompileBackTypeSignature.Display(propertyReturnType);
            bool isAutoProperty = hasGetter
                && IsAutoProperty(reader, typeDef, property, accessors.Getter, returnType.DisplayName);
            bool isInitSetter = hasSetter && SetterIsInitOnly(reader, accessors.Setter);
            bool isAbstractAccessor = !accessor.IsNil && propertyDeclaration.IsAbstract;
            var noBodyProperty = (typeDef.Attributes & TypeAttributes.Interface) != 0 || isAbstractAccessor;
            var stubBody = PropertyStubBody(
                hasGetter,
                hasSetter,
                isInitSetter,
                isAutoProperty,
                noBodyProperty);
            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, Identifier(propertyName), 0, $"property {propertyReturnType}"),
                hasGetter ? CompileBackMemberKind.PropertyGet : CompileBackMemberKind.PropertySet,
                isStatic,
                ToCompileBackParameters(propertyDeclaration.Signature.Parameters),
                returnType,
                [],
                stubBody,
                null,
                [new CompileBackFact("metadata", factId, accessorName)],
                propertyDeclaration.Attributes,
                propertyDeclaration.Signature.ReturnAttributes,
                IsAbstract: isAbstractAccessor,
                IsVirtual: !accessor.IsNil && propertyDeclaration.IsVirtual,
                ExplicitInterfaceMemberName: explicitInterfaceMemberName);
        }

        static CompileBackStubBodyKind PropertyStubBody(
            bool hasGetter,
            bool hasSetter,
            bool isInitSetter,
            bool isAutoProperty,
            bool noBodyProperty)
        {
            if (!hasGetter)
            {
                return noBodyProperty
                    ? isInitSetter
                        ? CompileBackStubBodyKind.InitOnlyProperty
                        : CompileBackStubBodyKind.None
                    : isInitSetter
                        ? CompileBackStubBodyKind.ThrowInit
                        : CompileBackStubBodyKind.Throw;
            }
            if (!hasSetter)
            {
                return noBodyProperty
                    ? CompileBackStubBodyKind.None
                    : isAutoProperty
                        ? CompileBackStubBodyKind.AutoProperty
                        : CompileBackStubBodyKind.Throw;
            }
            if (noBodyProperty || isAutoProperty)
            {
                return isInitSetter
                    ? CompileBackStubBodyKind.AutoPropertyGetInit
                    : CompileBackStubBodyKind.AutoPropertyGetSet;
            }
            return isInitSetter
                ? CompileBackStubBodyKind.ThrowGetInit
                : CompileBackStubBodyKind.ThrowGetSet;
        }

        internal static CompileBackMemberRequirement? EventRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            EventDefinitionHandle eventHandle,
            string accessorName,
            string factId = "typed-closure-event")
        {
            var eventDefinition = reader.GetEventDefinition(eventHandle);
            var accessors = eventDefinition.GetAccessors();
            MethodDefinitionHandle accessorHandle;
            if (!accessors.Adder.IsNil
                && reader.GetString(reader.GetMethodDefinition(accessors.Adder).Name) == accessorName)
            {
                accessorHandle = accessors.Adder;
            }
            else if (!accessors.Remover.IsNil
                && reader.GetString(reader.GetMethodDefinition(accessors.Remover).Name) == accessorName)
            {
                accessorHandle = accessors.Remover;
            }
            else
            {
                return null;
            }

            var accessor = reader.GetMethodDefinition(accessorHandle);
            MethodSignature<string> signature;
            IReadOnlyList<CompileBackParameter> parameters;
            try
            {
                signature = GuardedSignatureText.MethodText(
                    reader,
                    accessor,
                    GenericContext.ForMethod(reader, typeDef, accessor));
                parameters = MethodParameters(reader, accessor, signature);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }
            if (parameters.Count != 1)
                return null;

            string eventName = Identifier(reader.GetString(eventDefinition.Name));
            bool isAbstract = IsAbstractMethod(accessor);
            bool hasNoBody = (typeDef.Attributes & TypeAttributes.Interface) != 0 || isAbstract;
            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(
                    typeIdentity.FullName,
                    eventName,
                    0,
                    $"event {parameters[0].Type.DisplayName}"),
                accessorHandle == accessors.Adder
                    ? CompileBackMemberKind.EventAdd
                    : CompileBackMemberKind.EventRemove,
                accessor.Attributes.HasFlag(MethodAttributes.Static),
                [],
                parameters[0].Type,
                [],
                hasNoBody ? CompileBackStubBodyKind.None : CompileBackStubBodyKind.Throw,
                null,
                [new CompileBackFact("metadata", factId, accessorName)],
                MemberAttributes(reader, eventDefinition.GetCustomAttributes()),
                IsAbstract: isAbstract,
                IsVirtual: IsVirtualMethod(accessor));
        }

        internal static CompileBackMemberRequirement? MethodRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity,
            MethodDefinitionHandle methodHandle,
            string factId = "typed-closure-method")
        {
            var method = reader.GetMethodDefinition(methodHandle);
            string name = reader.GetString(method.Name);
            bool isConstructor = name == ".ctor";
            if (name == ".cctor"
                || (name.Contains('<', StringComparison.Ordinal)
                    && CSharpNaming.MethodName(name) == name)
                || (!isConstructor && name.Contains('.', StringComparison.Ordinal)))
                return null;

            if (!isConstructor
                && method.Attributes.HasFlag(MethodAttributes.SpecialName)
                && !name.StartsWith("op_", StringComparison.Ordinal))
            {
                return null;
            }

            MethodSignature<string> signature;
            try
            {
                signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }

            var generatedLocalFunction = IsGeneratedLocalFunctionName(name);
            var methodDeclaration = generatedLocalFunction
                ? null
                : MetadataDeclarationQuery.GetMethod(reader, typeDef, method, signature);
            var parameters = generatedLocalFunction
                ? Parameters(reader, method, signature)
                : ToCompileBackParameters(methodDeclaration!.Signature.Parameters);
            var methodReturnType = generatedLocalFunction
                ? signature.ReturnType
                : methodDeclaration!.Signature.ReturnType;
            if (methodReturnType is null
                || IsUnsupportedSurfaceSignature(methodReturnType)
                || parameters.Any(parameter => IsUnsupportedSurfaceSignature(parameter.Type.DisplayName)))
            {
                return null;
            }

            string identifierName = MemberIdentifierName(name, isConstructor);
            return new CompileBackMemberRequirement(
                new CompileBackMethodIdentity(typeIdentity.FullName, identifierName, DeclaringOverloadIndex(reader, typeDef, methodHandle, name), MethodSignatureText(identifierName, signature)),
                isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method,
                method.Attributes.HasFlag(MethodAttributes.Static),
                parameters,
                isConstructor ? null : CompileBackTypeSignature.Display(methodReturnType),
                generatedLocalFunction ? [] : ToCompileBackTypeParameters(methodDeclaration!.Signature.TypeParameters),
                (typeDef.Attributes & TypeAttributes.Interface) != 0 || IsAbstractMethod(method)
                    ? CompileBackStubBodyKind.None
                    : CompileBackStubBodyKind.Throw,
                null,
                [new CompileBackFact("metadata", isConstructor ? "typed-closure-constructor" : factId, name)],
                isConstructor ? null : methodDeclaration?.Attributes,
                isConstructor ? null : methodDeclaration?.Signature.ReturnAttributes,
                IsAbstract: !isConstructor && IsAbstractMethod(method),
                IsVirtual: !isConstructor && IsVirtualMethod(method),
                IsOverride: false,
                IsSealed: false,
                IsExtension: IsExtensionMethod(reader, typeDef, method));
        }

        static bool IsExtensionMethod(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
            => typeDef.Attributes.HasFlag(TypeAttributes.Abstract)
               && typeDef.Attributes.HasFlag(TypeAttributes.Sealed)
               && method.Attributes.HasFlag(MethodAttributes.Static)
               && AttributeReader.HasExtensionAttribute(reader, typeDef.GetCustomAttributes())
               && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());

        static int DeclaringOverloadIndex(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle target, string name)
        {
            int index = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) != name)
                    continue;
                if (methodHandle == target)
                    return index;
                index++;
            }

            return index;
        }

        static CSharpTypeShellSpec BuildSpec(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            CompileBackTypeRequirement requirement,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName,
            List<CompileBackTypeRequirement> producedRequirements,
            List<CompileBackPlanningDiagnostic> diagnostics,
            bool usesUpdatedMemorySafetyRules)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var kind = requirement.RequiredKind;
            var members = kind == CompileBackTypeKind.Delegate
                ? [DelegateInvokeRequirement(reader, typeDef, requirement.Type)]
                : RequiredMemberRequirements(requirement);
            bool includeMemberSurface = requirement.IncludeMemberSurface;
            if (includeMemberSurface && kind != CompileBackTypeKind.Delegate)
                AddClosureMemberSurface(reader, typeDef, requirement, members, diagnostics);
            if (kind is CompileBackTypeKind.Class or CompileBackTypeKind.Record or CompileBackTypeKind.Struct)
            {
                AddRequiredInterfaceProperties(
                    reader,
                    typeDef,
                    requirement,
                    requirementsByMetadataName,
                    members);
            }
            // When this class is reconstructed as the base of another shell type, a
            // derived stub constructor emits an implicit `: base()`. If the class has
            // only parameterized constructors (no accessible parameterless one), that
            // implicit call fails to bind (CS7036/CS1729). Synthesize a parameterless
            // constructor so base-class reconstruction never breaks the derived shell;
            // at worst the derived constructor stays at its pre-existing opcode diff.
            if (kind == CompileBackTypeKind.Class
                && members.Any(member => member.Kind == CompileBackMemberKind.Constructor)
                && !members.Any(member => member.Kind == CompileBackMemberKind.Constructor && member.Parameters.Count == 0)
                && IsReconstructedBaseOfAnotherType(reader, requirement, requirementsByMetadataName))
            {
                members.Add(SyntheticParameterlessConstructor(requirement.Type));
            }
            var producedRequirement = requirement with { RequiredMembers = members };
            producedRequirements.Add(producedRequirement);

            var primaryConstructorParameters = requirement.PrimaryConstructor?.ParameterList
                .Select(ToApiParameter)
                .ToArray() ?? [];
            var policies = members
                .Select(member => ToMemberPolicy(
                    member,
                    primaryConstructorParameters.Length,
                    usesUpdatedMemorySafetyRules))
                .ToArray();

            return new CSharpTypeShellSpec(
                Handle: handle,
                Namespace: requirement.Type.Namespace,
                MetadataName: requirement.Type.MetadataName,
                Kind: ToShellKind(kind),
                InterfaceDisplayNames: InterfaceSignatures(reader, typeDef, requirementsByMetadataName)
                    .Select(signature => signature.DisplayName)
                    .Concat(requirement.ExternalInterfaces)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                MemberPolicies: policies,
                PrimaryConstructorParameters: primaryConstructorParameters,
                NestedTypes: NestedSpecs(
                    reader,
                    typeDef,
                    requirementsByMetadataName,
                    includeMemberSurface,
                    producedRequirements,
                    diagnostics,
                    usesUpdatedMemorySafetyRules));
        }

        static void AddRequiredInterfaceProperties(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeRequirement requirement,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName,
            List<CompileBackMemberRequirement> members)
        {
            foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = reader.GetInterfaceImplementation(implementationHandle);
                if (implementation.Interface.Kind != HandleKind.TypeDefinition)
                    continue;

                var interfaceDef = reader.GetTypeDefinition(
                    (TypeDefinitionHandle)implementation.Interface);
                var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
                if (!requirementsByMetadataName.TryGetValue(
                        interfaceIdentity.MetadataFullName,
                        out var interfaceRequirement))
                {
                    continue;
                }

                var interfaceMembers = RequiredMemberRequirements(interfaceRequirement);
                if (interfaceRequirement.IncludeMemberSurface)
                {
                    // The interface's own BuildSpec call reports surface diagnostics.
                    AddClosureMemberSurface(
                        reader,
                        interfaceDef,
                        interfaceRequirement,
                        interfaceMembers,
                        diagnostics: []);
                }

                foreach (var interfaceMember in interfaceMembers.Where(
                    member => member.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet))
                {
                    var propertyHandle = ImplementingProperty(
                        reader,
                        typeDef,
                        interfaceDef,
                        interfaceMember);
                    if (propertyHandle.IsNil)
                        continue;
                    var propertyDef = reader.GetPropertyDefinition(propertyHandle);
                    var accessors = propertyDef.GetAccessors();
                    var accessor = interfaceMember.Kind == CompileBackMemberKind.PropertyGet
                        ? accessors.Getter
                        : accessors.Setter;
                    if (accessor.IsNil)
                        continue;

                    var property = PropertyRequirement(
                        reader,
                        typeDef,
                        requirement.Type,
                        propertyHandle,
                        reader.GetString(reader.GetMethodDefinition(accessor).Name),
                        "required-interface-property");
                    if (property is not null
                        && !members.Any(existing => SameMemberShape(existing, property)))
                    {
                        members.Add(property);
                    }
                }
            }
        }

        static PropertyDefinitionHandle ImplementingProperty(
            MetadataReader reader,
            TypeDefinition typeDef,
            TypeDefinition interfaceDef,
            CompileBackMemberRequirement interfaceMember)
        {
            var interfaceProperty = interfaceDef.GetProperties().FirstOrDefault(handle =>
                Identifier(reader.GetString(reader.GetPropertyDefinition(handle).Name))
                    == interfaceMember.Identity.Method);
            if (interfaceProperty.IsNil)
                return default;

            var interfaceAccessors = reader.GetPropertyDefinition(interfaceProperty).GetAccessors();
            var declaration = interfaceMember.Kind == CompileBackMemberKind.PropertyGet
                ? interfaceAccessors.Getter
                : interfaceAccessors.Setter;
            foreach (var implementationHandle in typeDef.GetMethodImplementations())
            {
                var implementation = reader.GetMethodImplementation(implementationHandle);
                if (implementation.MethodDeclaration == declaration
                    && implementation.MethodBody.Kind == HandleKind.MethodDefinition)
                {
                    return PropertyForAccessor(
                        reader,
                        typeDef,
                        (MethodDefinitionHandle)implementation.MethodBody);
                }
            }

            string interfaceName = CompileBackTypeIdentity.FromDefinition(
                reader,
                interfaceDef).MetadataFullName;
            string propertyName = reader.GetString(
                reader.GetPropertyDefinition(interfaceProperty).Name);
            return typeDef.GetProperties().FirstOrDefault(handle =>
            {
                string candidateName = reader.GetString(reader.GetPropertyDefinition(handle).Name);
                return candidateName == propertyName
                    || candidateName == $"{interfaceName}.{propertyName}";
            });
        }

        static PropertyDefinitionHandle PropertyForAccessor(
            MetadataReader reader,
            TypeDefinition typeDef,
            MethodDefinitionHandle accessor)
        {
            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
                if (accessors.Getter == accessor || accessors.Setter == accessor)
                    return propertyHandle;
            }
            return default;
        }

        internal static bool SameMemberShape(
            CompileBackMemberRequirement left,
            CompileBackMemberRequirement right)
        {
            if (SameMemberDeclaration(left, right))
                return true;
            bool bothProperties = left.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet
                && right.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet;
            bool bothEvents = left.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove
                && right.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove;
            if (!bothProperties && !bothEvents)
            {
                return false;
            }

            string leftName = left.ExplicitInterfaceMemberName ?? left.Identity.Method;
            string rightName = right.ExplicitInterfaceMemberName ?? right.Identity.Method;
            return leftName == rightName
                && left.ReturnType == right.ReturnType
                && SameParameters(left.Parameters, right.Parameters);
        }

        static CSharpTypeShellKind ToShellKind(CompileBackTypeKind kind)
            => kind switch
            {
                CompileBackTypeKind.Class => CSharpTypeShellKind.Class,
                CompileBackTypeKind.Record => CSharpTypeShellKind.Record,
                CompileBackTypeKind.Struct => CSharpTypeShellKind.Struct,
                CompileBackTypeKind.Interface => CSharpTypeShellKind.Interface,
                CompileBackTypeKind.Enum => CSharpTypeShellKind.Enum,
                CompileBackTypeKind.Delegate => CSharpTypeShellKind.Delegate,
                _ => throw new NotSupportedException($"Unsupported RTS type kind '{kind}'."),
            };
        static CompileBackMemberRequirement DelegateInvokeRequirement(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeIdentity typeIdentity)
        {
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != "Invoke")
                    continue;

                var signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
                return new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(typeIdentity.FullName, "Invoke", 0, MethodSignatureText("Invoke", signature)),
                    CompileBackMemberKind.Method,
                    IsStatic: false,
                    Parameters: Parameters(reader, method, signature),
                    ReturnType: CompileBackTypeSignature.Display(signature.ReturnType),
                    TypeParameters: [],
                    StubBody: CompileBackStubBodyKind.None,
                    TargetBody: null,
                    [new CompileBackFact("metadata", "generated-dynamic-delegate-invoke", reader.GetString(typeDef.Name))]);
            }

            throw new InvalidOperationException($"Generated dynamic delegate '{typeIdentity.MetadataFullName}' has no Invoke method.");
        }

        static List<CompileBackMemberRequirement> RequiredMemberRequirements(CompileBackTypeRequirement requirement)
            => requirement.RequiredMembers
                .Select(member => member with { Accessibility = CompileBackAccessibility.Public })
                .ToList();

        static IReadOnlyList<CSharpTypeShellSpec> NestedSpecs(
            MetadataReader reader,
            TypeDefinition typeDef,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName,
            bool includeMemberSurface,
            List<CompileBackTypeRequirement> producedRequirements,
            List<CompileBackPlanningDiagnostic> diagnostics,
            bool usesUpdatedMemorySafetyRules)
        {
            var nestedTypes = new List<CSharpTypeShellSpec>();
            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                var nestedDef = reader.GetTypeDefinition(nestedHandle);
                string name = reader.GetString(nestedDef.Name);
                if (IsDelegate(reader, nestedDef) && !IsGeneratedDynamicDelegate(reader, nestedDef))
                {
                    continue;
                }

                var identity = CompileBackTypeIdentity.FromDefinition(reader, nestedDef);
                requirementsByMetadataName.TryGetValue(identity.MetadataFullName, out var requirement);
                var kind = requirement?.RequiredKind ?? ShellKind(reader, nestedDef);
                requirement ??= new CompileBackTypeRequirement(
                    identity,
                    kind,
                    RequiredMembers: [],
                    PrimaryConstructor: null,
                    SourceFacts: [new CompileBackFact("metadata", "nested-closure-type", identity.FullName)]);
                bool includeNestedMemberSurface = includeMemberSurface
                    || requirement.IncludeMemberSurface
                    || IsGeneratedMetadataName(name);
                var nestedRequirement = includeNestedMemberSurface
                    ? requirement with { IncludeMemberSurface = true }
                    : requirement;
                nestedTypes.Add(BuildSpec(
                    reader,
                    nestedHandle,
                    nestedRequirement,
                    requirementsByMetadataName,
                    producedRequirements,
                    diagnostics,
                    usesUpdatedMemorySafetyRules));
            }

            if (HasGeneratedCallSiteCache(reader, typeDef))
            {
                foreach (var delegateHandle in GeneratedDynamicDelegates(reader))
                {
                    var delegateDef = reader.GetTypeDefinition(delegateHandle);
                    var identity = CompileBackTypeIdentity.FromDefinition(reader, delegateDef);
                    if (nestedTypes.Any(spec => spec.MetadataName == identity.MetadataName))
                        continue;

                    nestedTypes.Add(BuildSpec(
                        reader,
                        delegateHandle,
                        new CompileBackTypeRequirement(
                            identity,
                            CompileBackTypeKind.Delegate,
                            RequiredMembers: [],
                            PrimaryConstructor: null,
                            SourceFacts: [new CompileBackFact("metadata", "generated-dynamic-delegate", identity.FullName)]),
                        requirementsByMetadataName,
                        producedRequirements,
                        diagnostics,
                        usesUpdatedMemorySafetyRules));
                }
            }

            return nestedTypes;
        }

        static bool HasGeneratedCallSiteCache(MetadataReader reader, TypeDefinition typeDef)
        {
            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                var nestedDef = reader.GetTypeDefinition(nestedHandle);
                if (reader.GetString(nestedDef.Name).StartsWith("<>o__", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static IEnumerable<TypeDefinitionHandle> GeneratedDynamicDelegates(MetadataReader reader)
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                if (IsGeneratedDynamicDelegate(reader, reader.GetTypeDefinition(handle)))
                    yield return handle;
            }
        }

        // True when some other reconstructed shell type — top-level or nested —
        // derives from this class via a reconstructed (same-assembly) base
        // declaration, so its implicit `: base()` depends on this class exposing an
        // accessible parameterless constructor. Nested types are emitted by
        // NestedTypes() from their enclosing requirement and are not present in
        // requirementsByMetadataName, so each requirement's nested tree is walked.
        static bool IsReconstructedBaseOfAnotherType(
            MetadataReader reader,
            CompileBackTypeRequirement requirement,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName)
        {
            string metadataFullName = requirement.Type.MetadataFullName;
            foreach (var other in requirementsByMetadataName.Values)
            {
                if (FindType(reader, other.Type.MetadataFullName) is not { } otherHandle)
                    continue;
                if (TypeOrNestedDerivesFrom(reader, otherHandle, metadataFullName))
                    return true;
            }

            return false;
        }

        static bool TypeOrNestedDerivesFrom(MetadataReader reader, TypeDefinitionHandle handle, string baseMetadataFullName)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (CompileBackTypeIdentity.FromDefinition(reader, typeDef).MetadataFullName != baseMetadataFullName
                && ReconstructedSameAssemblyBaseName(reader, handle, ShellKind(reader, typeDef)) == baseMetadataFullName)
            {
                return true;
            }

            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                if (TypeOrNestedDerivesFrom(reader, nestedHandle, baseMetadataFullName))
                    return true;
            }

            return false;
        }

        // The metadata full name of the class's reconstructed same-assembly base, or
        // null when the base is not reconstructed (external base, or a kind that keeps
        // its compiler-implied base).
        static string? ReconstructedSameAssemblyBaseName(MetadataReader reader, TypeDefinitionHandle handle, CompileBackTypeKind kind)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (typeDef.BaseType.Kind != HandleKind.TypeDefinition)
                return null;
            if (TypeShellProducer.ReconstructedBaseTypeDisplay(reader, typeDef, kind == CompileBackTypeKind.Class) is null)
                return null;
            var baseDef = reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType);
            return CompileBackTypeIdentity.FromDefinition(reader, baseDef).MetadataFullName;
        }

        static CompileBackMemberRequirement SyntheticParameterlessConstructor(CompileBackTypeIdentity typeIdentity)
            => new(
                new CompileBackMethodIdentity(typeIdentity.FullName, ".ctor", 0, "synthetic-base-parameterless-constructor()"),
                CompileBackMemberKind.Constructor,
                IsStatic: false,
                Parameters: [],
                ReturnType: null,
                TypeParameters: [],
                CompileBackStubBodyKind.Throw,
                TargetBody: null,
                [new CompileBackFact("synthetic", "base-parameterless-constructor", typeIdentity.MetadataFullName)]);

        static IReadOnlyList<CompileBackTypeSignature> InterfaceSignatures(
            MetadataReader reader,
            TypeDefinition typeDef,
            IReadOnlyDictionary<string, CompileBackTypeRequirement> requirementsByMetadataName)
        {
            if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
                return [];

            var interfaces = new List<CompileBackTypeSignature>();
            foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = reader.GetInterfaceImplementation(implementationHandle);
                if (implementation.Interface.Kind != HandleKind.TypeDefinition)
                    continue;

                var interfaceDef = reader.GetTypeDefinition((TypeDefinitionHandle)implementation.Interface);
                if (interfaceDef.GetGenericParameters().Count != 0 || !IsSupportedClosureRoot(reader, interfaceDef))
                    continue;

                var interfaceIdentity = CompileBackTypeIdentity.FromDefinition(reader, interfaceDef);
                // Naming a base-list interface that this compile-back unit never
                // declares is worse than omitting it: metadata can carry two
                // same-named interfaces of different arity in one namespace (a
                // non-generic `IPropertyValidator` alongside `IPropertyValidator<T,
                // TProperty>`, as in FluentValidation), and closure discovery may
                // queue only one of them as an actual requirement. Referencing the
                // undeclared one by its bare display name lets Roslyn resolve it to
                // the *other*, wrong-arity type in scope (CS0305). Only name an
                // interface here when it is already a known requirement — i.e. it
                // will actually be declared somewhere in the composed unit — mirroring
                // the same guard `AddRequiredInterfaceProperties` uses.
                if (!requirementsByMetadataName.ContainsKey(interfaceIdentity.MetadataFullName))
                    continue;

                interfaces.Add(CompileBackTypeSignature.Definition(interfaceIdentity));
            }

            return interfaces;
        }

        static void AddClosureMemberSurface(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeRequirement requirement,
            List<CompileBackMemberRequirement> members,
            List<CompileBackPlanningDiagnostic> diagnostics,
            bool allowUnsafeSurface = false)
        {
            if (requirement.RequiredKind == CompileBackTypeKind.Enum)
            {
                // An enum reconstructed as a closure supporting type must carry its
                // named members: the target body can reference any of them by name, and
                // a member-less `enum { }` shell fails to bind those references (CS0117).
                // Emit each literal member with its constant value so references resolve
                // and keep their numeric identity. The special `value__` storage field
                // (not a literal) and any name-mangled fields are not enum members.
                foreach (var enumFieldHandle in typeDef.GetFields())
                {
                    var enumField = reader.GetFieldDefinition(enumFieldHandle);
                    if (!enumField.Attributes.HasFlag(FieldAttributes.Literal))
                        continue;
                    string enumMemberName = reader.GetString(enumField.Name);
                    if (enumMemberName.Contains('.', StringComparison.Ordinal))
                        continue;
                    if (!TryFormatConstantField(reader, enumField, out var enumConstant))
                        continue;
                    if (members.Any(member => member.Kind == CompileBackMemberKind.Field
                            && member.Identity.Method == Identifier(enumMemberName)))
                        continue;
                    members.Add(new CompileBackMemberRequirement(
                        new CompileBackMethodIdentity(requirement.Type.FullName, Identifier(enumMemberName), 0, "enum-member"),
                        CompileBackMemberKind.Field,
                        IsStatic: true,
                        Parameters: [],
                        ReturnType: CompileBackTypeSignature.Display(requirement.Type.FullName),
                        TypeParameters: [],
                        StubBody: CompileBackStubBodyKind.TargetBody,
                        TargetBody: enumConstant,
                        [new CompileBackFact("metadata", "enum-member", enumMemberName)]));
                }
                return;
            }

            allowUnsafeSurface = allowUnsafeSurface
                || requirement.RequiredMembers.Count != 0
                || requirement.IncludeMemberSurface;
            var accessorMethods = new HashSet<MethodDefinitionHandle>();
            var typeContext = GenericContext.ForType(reader, typeDef);
            // The product owns the field-inclusion decision (ApiSurfaceExtractor.SurfaceFieldHandles):
            // it drops synthesized auto-property backing fields (`<Name>k__BackingField`, which the
            // compiler re-synthesizes for each reconstructed auto-property, issue #3036), the enum
            // `value__` slot, and a field-like event's compiler-generated backing field (issue
            // #3083), while surfacing the closure/state-machine captures reconstruction needs
            // (includeCompilerGenerated). RTS keeps only the reconstruction-side gates below
            // (unspeakable names, signature decode, fixed buffers, pointer surface, dedup).
            foreach (var fieldHandle in ApiSurfaceExtractor.SurfaceFieldHandles(
                reader, typeDef, includeAll: true, includeCompilerGenerated: true))
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                string fieldName = reader.GetString(field.Name);
                if (fieldName.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }
                if (members.Any(member => member.Kind == CompileBackMemberKind.Field && member.Identity.Method == Identifier(fieldName)))
                    continue;

                string fieldType;
                try
                {
                    fieldType = GuardedSignatureText.FieldText(reader, field, typeContext);
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "field-signature-decode-failed", fieldName));
                    continue;
                }
                string? fixedBufferSignature = FixedBufferDeclarationSignature(reader, field, fieldName);
                if ((fixedBufferSignature is null && IsUnsupportedSurfaceSignature(fieldType))
                    || (!allowUnsafeSurface && IsPointerSignature(fieldType)))
                    continue;

                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(requirement.Type.FullName, Identifier(fieldName), 0, $"field {fieldType}"),
                    CompileBackMemberKind.Field,
                    IsStatic: field.Attributes.HasFlag(FieldAttributes.Static),
                    Parameters: [],
                    ReturnType: CompileBackTypeSignature.Display(fieldType),
                    TypeParameters: [],
                    StubBody: TryFormatConstantField(reader, field, out var constant)
                        ? CompileBackStubBodyKind.TargetBody
                        : CompileBackStubBodyKind.None,
                    TargetBody: constant,
                    [new CompileBackFact("metadata", "closure-field", fieldName)],
                    DeclarationSignature: fixedBufferSignature,
                    IsReadOnly: field.Attributes.HasFlag(FieldAttributes.InitOnly)));
            }

            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (!accessors.Getter.IsNil)
                    accessorMethods.Add(accessors.Getter);
                if (!accessors.Setter.IsNil)
                    accessorMethods.Add(accessors.Setter);

                string propertyName = reader.GetString(property.Name);
                if (propertyName.Contains('<', StringComparison.Ordinal)
                    || propertyName.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }
                int existingPropertyIndex = members.FindIndex(member =>
                    (member.Kind is CompileBackMemberKind.PropertyGet or CompileBackMemberKind.PropertySet or CompileBackMemberKind.Field)
                    && member.Identity.Method == Identifier(propertyName));
                if (existingPropertyIndex >= 0)
                {
                    var existing = members[existingPropertyIndex];
                    members[existingPropertyIndex] = existing with
                    {
                        GetterToken = existing.GetterToken ?? (accessors.Getter.IsNil ? null : MetadataTokens.GetToken(accessors.Getter)),
                        SetterToken = existing.SetterToken ?? (accessors.Setter.IsNil ? null : MetadataTokens.GetToken(accessors.Setter)),
                    };
                    continue;
                }

                MetadataPropertyDeclaration propertyDeclaration;
                try
                {
                    propertyDeclaration = MetadataDeclarationQuery.GetProperty(reader, typeDef, property);
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "property-signature-decode-failed", propertyName));
                    continue;
                }

                if (propertyDeclaration.Signature.Parameters.Count != 0)
                    continue;
                if (propertyDeclaration.Signature.ReturnType is not { } propertyReturnType
                    || IsUnsupportedSurfaceSignature(propertyReturnType)
                    || (!allowUnsafeSurface && IsPointerSignature(propertyReturnType)))
                    continue;

                var accessor = accessors.Getter.IsNil ? accessors.Setter : accessors.Getter;
                bool hasGetter = !accessors.Getter.IsNil;
                bool hasSetter = !accessors.Setter.IsNil;
                if (!hasGetter && !hasSetter)
                    continue;

                var accessorMethod = accessor.IsNil ? default : reader.GetMethodDefinition(accessor);
                bool isStatic = !accessor.IsNil && accessorMethod.Attributes.HasFlag(MethodAttributes.Static);
                if (requirement.RequiredKind == CompileBackTypeKind.Interface && isStatic)
                    continue;
                var returnType = CompileBackTypeSignature.Display(propertyReturnType);
                bool isAutoProperty = hasGetter
                    && IsAutoProperty(reader, typeDef, property, accessors.Getter, returnType.DisplayName);
                bool isInitSetter = hasSetter && SetterIsInitOnly(reader, accessors.Setter);
                bool isAbstractAccessor = !accessor.IsNil && propertyDeclaration.IsAbstract;
                var noBodyProperty = requirement.RequiredKind == CompileBackTypeKind.Interface || isAbstractAccessor;
                var stubBody = PropertyStubBody(
                    hasGetter,
                    hasSetter,
                    isInitSetter,
                    isAutoProperty,
                    noBodyProperty);
                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(requirement.Type.FullName, Identifier(propertyName), 0, $"property {propertyReturnType}"),
                    hasGetter ? CompileBackMemberKind.PropertyGet : CompileBackMemberKind.PropertySet,
                    IsStatic: isStatic,
                    Parameters: [],
                    ReturnType: returnType,
                    TypeParameters: [],
                    StubBody: stubBody,
                    TargetBody: null,
                    [new CompileBackFact("metadata", "closure-property", propertyName)],
                    propertyDeclaration.Attributes,
                    propertyDeclaration.Signature.ReturnAttributes,
                    IsAbstract: isAbstractAccessor,
                    IsVirtual: !accessor.IsNil && propertyDeclaration.IsVirtual,
                    Accessibility: accessor.IsNil
                        ? CompileBackAccessibility.Public
                        : MethodAccessibility(accessorMethod),
                    GetterToken: accessors.Getter.IsNil ? null : MetadataTokens.GetToken(accessors.Getter),
                    SetterToken: accessors.Setter.IsNil ? null : MetadataTokens.GetToken(accessors.Setter)));
            }

            foreach (var eventHandle in typeDef.GetEvents())
            {
                var eventDefinition = reader.GetEventDefinition(eventHandle);
                var accessors = eventDefinition.GetAccessors();
                if (!accessors.Adder.IsNil)
                    accessorMethods.Add(accessors.Adder);
                if (!accessors.Remover.IsNil)
                    accessorMethods.Add(accessors.Remover);

                string eventName = reader.GetString(eventDefinition.Name);
                if (eventName.Contains('<', StringComparison.Ordinal))
                    continue;
                int existingEventIndex = members.FindIndex(member =>
                    member.Kind is CompileBackMemberKind.EventAdd or CompileBackMemberKind.EventRemove
                    && member.Identity.Method == Identifier(eventName));
                int? adderToken = accessors.Adder.IsNil ? null : MetadataTokens.GetToken(accessors.Adder);
                int? removerToken = accessors.Remover.IsNil ? null : MetadataTokens.GetToken(accessors.Remover);
                if (existingEventIndex >= 0)
                {
                    var existing = members[existingEventIndex];
                    members[existingEventIndex] = existing with
                    {
                        AdderToken = existing.AdderToken ?? adderToken,
                        RemoverToken = existing.RemoverToken ?? removerToken,
                    };
                    continue;
                }

                var representative = !accessors.Adder.IsNil ? accessors.Adder : accessors.Remover;
                if (representative.IsNil)
                    continue;
                string accessorName = reader.GetString(reader.GetMethodDefinition(representative).Name);
                var eventRequirement = EventRequirement(
                    reader,
                    typeDef,
                    requirement.Type,
                    eventHandle,
                    accessorName,
                    "closure-event");
                if (eventRequirement is null)
                    continue;
                var explicitEvent = eventName.Contains('.', StringComparison.Ordinal)
                    ? ExplicitInterfaceEvent(reader, typeDef, representative)
                    : null;
                members.Add(eventRequirement with
                {
                    AdderToken = adderToken,
                    RemoverToken = removerToken,
                    ExplicitInterfaceMemberName = explicitEvent?.QualifiedName,
                });
            }

            int overload = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                string name = reader.GetString(method.Name);
                if (accessorMethods.Contains(methodHandle)
                    || name == ".cctor"
                    || (name.Contains('<', StringComparison.Ordinal)
                        && CSharpNaming.MethodName(name) == name)
                    || (name != ".ctor" && name.Contains('.', StringComparison.Ordinal)))
                {
                    continue;
                }

                bool isConstructor = name == ".ctor";
                string identifierName = MemberIdentifierName(name, isConstructor);
                int existingMethodIndex = members.FindIndex(member =>
                    member.Kind == (isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method)
                    && member.Identity.Method == identifierName);
                if (existingMethodIndex >= 0)
                {
                    var existing = members[existingMethodIndex];
                    members[existingMethodIndex] = existing with
                    {
                        MetadataToken = existing.MetadataToken ?? MetadataTokens.GetToken(methodHandle),
                    };
                    continue;
                }
                if (requirement.RequiredKind == CompileBackTypeKind.Interface && method.Attributes.HasFlag(MethodAttributes.Static))
                    continue;
                if (method.GetGenericParameters().Count != 0)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "generic-method-skipped", name));
                    continue;
                }
                if (!isConstructor && method.Attributes.HasFlag(MethodAttributes.SpecialName))
                    continue;

                MethodSignature<string> signature;
                try
                {
                    signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "method-signature-decode-failed", name));
                    continue;
                }

                var generatedLocalFunction = IsGeneratedLocalFunctionName(name);
                var methodDeclaration = generatedLocalFunction
                    ? null
                    : MetadataDeclarationQuery.GetMethod(reader, typeDef, method, signature);
                var parameters = generatedLocalFunction
                    ? Parameters(reader, method, signature)
                    : ToCompileBackParameters(methodDeclaration!.Signature.Parameters);
                var methodReturnType = generatedLocalFunction
                    ? signature.ReturnType
                    : methodDeclaration!.Signature.ReturnType;
                if (methodReturnType is null
                    || IsUnsupportedSurfaceSignature(methodReturnType)
                    || parameters.Any(parameter => IsUnsupportedSurfaceSignature(parameter.Type.DisplayName))
                    || (!allowUnsafeSurface
                        && (IsPointerSignature(methodReturnType)
                            || parameters.Any(parameter => IsPointerSignature(parameter.Type.DisplayName)))))
                {
                    continue;
                }
                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(requirement.Type.FullName, identifierName, overload++, MethodSignatureText(identifierName, signature)),
                    isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method,
                    IsStatic: method.Attributes.HasFlag(MethodAttributes.Static),
                    Parameters: parameters,
                    ReturnType: isConstructor ? null : CompileBackTypeSignature.Display(methodReturnType),
                    TypeParameters: generatedLocalFunction ? [] : ToCompileBackTypeParameters(methodDeclaration!.Signature.TypeParameters),
                    StubBody: requirement.RequiredKind == CompileBackTypeKind.Interface || IsAbstractMethod(method)
                        ? CompileBackStubBodyKind.None
                        : CompileBackStubBodyKind.Throw,
                    TargetBody: null,
                    [new CompileBackFact("metadata", isConstructor ? "closure-constructor" : "closure-method", name)],
                    isConstructor ? null : methodDeclaration?.Attributes,
                    isConstructor ? null : methodDeclaration?.Signature.ReturnAttributes,
                    IsAbstract: !isConstructor && IsAbstractMethod(method),
                    IsVirtual: !isConstructor && IsVirtualMethod(method),
                    IsOverride: false,
                    IsSealed: false,
                    Accessibility: MethodAccessibility(method),
                    MetadataToken: MetadataTokens.GetToken(methodHandle)));
            }

            if (requirement.RequiredKind == CompileBackTypeKind.Class
                && !TypeShellProducer.IsStaticType(typeDef)
                && requirement.PrimaryConstructor is null
                && !members.Any(member => member.Kind == CompileBackMemberKind.Constructor && member.Parameters.Count == 0)
                && !HasParameterlessInstanceConstructor(reader, typeDef))
            {
                members.Add(new CompileBackMemberRequirement(
                    new CompileBackMethodIdentity(requirement.Type.FullName, ".ctor", overload, "void .ctor()"),
                    CompileBackMemberKind.Constructor,
                    IsStatic: false,
                    ReturnType: null,
                    Parameters: [],
                    TypeParameters: [],
                    StubBody: CompileBackStubBodyKind.Throw,
                    TargetBody: null,
                    SourceFacts: [new CompileBackFact("metadata", "synthetic-parameterless-ctor", "same-assembly closure root")]));
            }
        }

        static bool IsUnsupportedSurfaceSignature(string signature)
            // Normalize the raw signature text to its C# display form first (the
            // harness's own naming concern: strips modreq/modopt, maps `!`-typed and
            // generated `<>` segments), then defer the representability heuristic to
            // the product skeleton so every consumer judges surfaces the same way.
            => TypeShellProducer.IsUnsupportedSurfaceSignature(CompileBackTypeSignature.Display(signature).DisplayName);

        static bool IsGeneratedMetadataName(string name)
            => name.Contains('<', StringComparison.Ordinal) || name.Contains('>', StringComparison.Ordinal);

        static bool IsGeneratedLocalFunctionName(string name)
            => name.Contains('<', StringComparison.Ordinal) && CSharpNaming.MethodName(name) != name;

        static bool IsPointerSignature(string signature)
            => signature.Contains('*', StringComparison.Ordinal);

        static bool TryFormatConstantField(MetadataReader reader, FieldDefinition field, out string? constant)
        {
            constant = null;
            if (!field.Attributes.HasFlag(FieldAttributes.Literal))
                return false;

            var constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil)
                return false;

            var value = reader.GetConstant(constantHandle);
            var blob = reader.GetBlobReader(value.Value);
            constant = value.TypeCode switch
            {
                ConstantTypeCode.Boolean => blob.ReadBoolean() ? "true" : "false",
                ConstantTypeCode.Char => $"'{EscapeCharLiteral(blob.ReadChar())}'",
                ConstantTypeCode.SByte => blob.ReadSByte().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Byte => blob.ReadByte().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Int16 => blob.ReadInt16().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Int32 => blob.ReadInt32().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.UInt32 => blob.ReadUInt32().ToString(CultureInfo.InvariantCulture),
                ConstantTypeCode.Int64 => blob.ReadInt64().ToString(CultureInfo.InvariantCulture) + "L",
                ConstantTypeCode.UInt64 => blob.ReadUInt64().ToString(CultureInfo.InvariantCulture) + "UL",
                ConstantTypeCode.Single => FormatSingleConstant(blob.ReadSingle()),
                ConstantTypeCode.Double => FormatDoubleConstant(blob.ReadDouble()),
                ConstantTypeCode.String => StringLiteral(blob.ReadUTF16(blob.Length)),
                ConstantTypeCode.NullReference => "null",
                _ => null,
            };

            return constant is not null;
        }

        static string FormatSingleConstant(float value)
        {
            if (float.IsNaN(value))
                return "float.NaN";
            if (float.IsPositiveInfinity(value))
                return "float.PositiveInfinity";
            if (float.IsNegativeInfinity(value))
                return "float.NegativeInfinity";
            return value.ToString("R", CultureInfo.InvariantCulture) + "f";
        }

        static string FormatDoubleConstant(double value)
        {
            if (double.IsNaN(value))
                return "double.NaN";
            if (double.IsPositiveInfinity(value))
                return "double.PositiveInfinity";
            if (double.IsNegativeInfinity(value))
                return "double.NegativeInfinity";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static string StringLiteral(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char ch in value)
                sb.Append(EscapeCharLiteral(ch));
            sb.Append('"');
            return sb.ToString();
        }

        static string EscapeCharLiteral(char ch)
            => ch switch
            {
                '\'' => "\\'",
                '"' => "\\\"",
                '\\' => "\\\\",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when char.IsControl(ch) => $"\\u{(int)ch:x4}",
                _ => ch.ToString(),
            };

        static IReadOnlyList<CompileBackParameter> Parameters(
            MetadataReader reader,
            MethodDefinition method,
            MethodSignature<string> signature)
        {
            var names = new Dictionary<int, string>();
            foreach (var parameterHandle in method.GetParameters())
            {
                var parameter = reader.GetParameter(parameterHandle);
                if (parameter.SequenceNumber > 0)
                    names[parameter.SequenceNumber - 1] = Identifier(reader.GetString(parameter.Name));
            }

            var parameters = new List<CompileBackParameter>();
            for (int i = 0; i < signature.ParameterTypes.Length; i++)
            {
                string name = names.TryGetValue(i, out var metadataName) && metadataName.Length > 0
                    ? metadataName
                    : $"arg{i}";
                parameters.Add(new CompileBackParameter(name, CompileBackTypeSignature.Display(signature.ParameterTypes[i])));
            }

            return parameters;
        }

        static string MethodSignatureText(string name, MethodSignature<string> signature)
            => $"{signature.ReturnType} {name}({string.Join(", ", signature.ParameterTypes)})";

        internal static TypeDefinitionHandle? FindType(MetadataReader reader, string metadataFullName)
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(handle);
                if (reader.GetFullTypeName(typeDef) == metadataFullName)
                    return handle;
            }

            return null;
        }

        static bool HasParameterlessInstanceConstructor(MetadataReader reader, TypeDefinition typeDef)
        {
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != ".ctor" || method.Attributes.HasFlag(MethodAttributes.Static))
                    continue;

                try
                {
                    var signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
                    if (signature.ParameterTypes.Length == 0)
                        return true;
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
