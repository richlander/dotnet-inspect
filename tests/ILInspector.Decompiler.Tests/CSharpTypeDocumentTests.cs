using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.Json.Nodes;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Tests;

public class CSharpTypeDocumentTests
{
    [Fact]
    public void Create_SnapshotsInputAndPreservesCompleteCollidingAnchors()
    {
        var input = Input();
        CSharpTypeDocument document = Create(input);

        Assert.Equal(4, document.Declarations.Length);
        Assert.Equal(
            document.Declarations[0].Anchor.Fingerprint,
            document.Declarations[1].Anchor.Fingerprint);
        Assert.NotEqual(
            document.Declarations[0].Anchor,
            document.Declarations[1].Anchor);

        input.Artifacts[0] = input.Artifacts[0] with { Id = 99 };
        input.Declarations[0] = input.Declarations[0] with { Id = 99 };

        Assert.Equal(0, document.Artifacts[0].Id);
        Assert.Equal(0, document.Declarations[0].Id);
    }

    [Fact]
    public void Create_RejectsMalformedUtf16AndOutOfBoundsBodyRange()
    {
        var invalidText = Input() with
        {
            Frame = Input().Frame with { Suffix = "\uD800" },
        };
        Assert.Throws<ArgumentException>(() => Create(invalidText));

        var invalidRange = Input();
        CSharpTypeDeclaration constructor = invalidRange.Declarations[2];
        CSharpTypeRenderPart implementation = constructor.Parts[1];
        invalidRange.Declarations[2] = constructor with
        {
            Parts = constructor.Parts.SetItem(
                1,
                implementation with
                {
                    OwnedBodies =
                    [
                        implementation.OwnedBodies[0] with
                        {
                            FullRange = new CSharpSourceRange(
                                implementation.FullText.Length,
                                1),
                        },
                    ],
                }),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => Create(invalidRange));
    }

    [Fact]
    public void Create_RequiresOneBodyRowForEveryMethodArtifact()
    {
        var input = Input();
        input.Bodies.RemoveAt(input.Bodies.Count - 1);
        CSharpTypeDeclaration property = input.Declarations[3];
        input.Declarations[3] = property with
        {
            Parts = property.Parts.SetItem(
                3,
                property.Parts[3] with { OwnedBodies = [] }),
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Create(input));

        Assert.Contains("exactly one physical body row", error.Message);
    }

    [Fact]
    public void Create_RequiresManagedDeclarationBodyOwnership()
    {
        var input = Input();
        CSharpTypeDeclaration constructor = input.Declarations[2];
        input.Declarations[2] = constructor with
        {
            Parts = constructor.Parts.SetItem(
                1,
                constructor.Parts[1] with { OwnedBodies = [] }),
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Create(input));

        Assert.Contains("must be owned exactly once", error.Message);
    }

    [Fact]
    public void Create_RejectsMixedContributionActivationOwners()
    {
        var input = Input();
        MemberAnchor secondConstructor = Anchor(
            ".ctor(int)",
            "void Sample..ctor(int value)");
        input.Artifacts.Insert(
            5,
            Artifact(
                5,
                secondConstructor,
                0x06000004,
                CSharpTypeArtifactKind.Method,
                4));
        input.Artifacts[6] = input.Artifacts[6] with { Id = 6 };
        input.Bodies.Add(
            Body(
                3,
                input.TypeAddress.ModuleVersionId,
                0x06000004,
                5,
                CSharpTypeBodyRole.Method,
                'D'));
        input.Declarations.Add(
            new(
                4,
                4,
                secondConstructor,
                0x06000004,
                CSharpTypeDeclarationKind.Constructor,
                CSharpTypeAccessibility.Public,
                CSharpTypeDeclarationPlacement.Instance,
                CSharpTypeOrigin.NonGenerated,
                [
                    Fixed(0, "public Sample(int value)"),
                    Implementation(
                        1,
                        " { _a = value; }",
                        " { }",
                        CSharpTypeImplementationKind.Body,
                        ownedBodies:
                        [
                            new(3, new(1, 15)),
                        ]),
                ]));
        CSharpTypeDeclaration field = input.Declarations[0];
        CSharpTypeRenderPart initializer = field.Parts[1];
        input.Declarations[0] = field with
        {
            Parts = field.Parts.SetItem(
                1,
                initializer with
                {
                    FullText = " = 1 + value",
                    Contributions =
                    [
                        initializer.Contributions[0],
                        new(
                            3,
                            CSharpTypeBodyContributionRole.FieldInitializer,
                            new(7, 5)),
                    ],
                }),
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Create(input));

        Assert.Contains(
            "different Selected-body activation",
            error.Message);
    }

    [Fact]
    public void Create_RejectsOwnedAndContributedActivationOwners()
    {
        var input = Input();
        CSharpTypeDeclaration constructor = input.Declarations[2];
        CSharpTypeRenderPart implementation = constructor.Parts[1];
        input.Declarations[2] = constructor with
        {
            Parts = constructor.Parts.SetItem(
                1,
                implementation with
                {
                    Contributions =
                    [
                        new(
                            1,
                            CSharpTypeBodyContributionRole.LoweredImplementation,
                            implementation.OwnedBodies[0].FullRange),
                    ],
                }),
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Create(input));

        Assert.Contains(
            "different Selected-body activation",
            error.Message);
    }

    [Fact]
    public void Create_RejectsArtifactBodyDeclarationRoleMismatch()
    {
        var input = Input();
        input.Bodies[0] = input.Bodies[0] with
        {
            Role = CSharpTypeBodyRole.Getter,
        };
        Assert.Contains(
            "role is inconsistent",
            Assert.Throws<ArgumentException>(() => Create(input)).Message);

        input = Input();
        input.Artifacts[0] = input.Artifacts[0] with
        {
            Representation = input.Artifacts[0].Representation with
            {
                Role = CSharpTypeArtifactRole.Getter,
            },
        };
        Assert.Contains(
            "Artifact 0 role is inconsistent",
            Assert.Throws<ArgumentException>(() => Create(input)).Message);

        input = Input();
        input.Artifacts[3] = input.Artifacts[3] with
        {
            Representation = input.Artifacts[3].Representation with
            {
                Role = CSharpTypeArtifactRole.Declaration,
            },
        };
        Assert.Contains(
            "role is inconsistent",
            Assert.Throws<ArgumentException>(() => Create(input)).Message);
    }

    [Fact]
    public void Create_RejectsRenderPartKindRegionMismatch()
    {
        var input = Input();
        input = input with
        {
            Frame = input.Frame with
            {
                PrefixParts = input.Frame.PrefixParts.SetItem(
                    0,
                    input.Frame.PrefixParts[0] with
                    {
                        Region = CSharpTypeRegionRole.Documentation,
                    }),
            },
        };

        Assert.Contains(
            "must use the Signature region",
            Assert.Throws<ArgumentException>(() => Create(input)).Message);
    }

    [Fact]
    public void Create_RejectsBodyEvidenceWithWhitespaceChanges()
    {
        var input = Input();
        CSharpTypeDeclaration constructor = input.Declarations[2];
        CSharpTypeRenderPart implementation = constructor.Parts[1];
        input.Declarations[2] = constructor with
        {
            Parts = constructor.Parts.SetItem(
                1,
                implementation with
                {
                    SkeletonText = implementation.FullText.Replace(
                        "_a = 1;",
                        "_a  = 1;",
                        StringComparison.Ordinal),
                }),
        };

        Assert.Contains(
            "skeleton retains body evidence modulo whitespace",
            Assert.Throws<ArgumentException>(() => Create(input)).Message);
    }

    [Fact]
    public void Create_RequiresNonEmptyDeclarationSignature()
    {
        var input = Input();
        input.Declarations[0] = input.Declarations[0] with
        {
            Parts = [Fixed(0, " ")],
        };

        Assert.Contains(
            "requires a non-whitespace signature part",
            Assert.Throws<ArgumentException>(() => Create(input)).Message);
    }

    [Fact]
    public void Create_BoundsAggregateSkeletonEvidenceComparisonWork()
    {
        const int referenceCount = 65;
        const int skeletonLength = 64 * 1024;
        var input = Input();
        CSharpTypeDeclaration field = input.Declarations[0];
        CSharpTypeRenderPart initializer = field.Parts[1];
        input.Declarations[0] = field with
        {
            Parts = field.Parts.SetItem(
                1,
                initializer with
                {
                    FullText = new string('x', referenceCount),
                    SkeletonText = new string('y', skeletonLength),
                    Contributions =
                    [
                        .. Enumerable.Range(0, referenceCount).Select(
                            static start => new CSharpTypeBodyContribution(
                                0,
                                CSharpTypeBodyContributionRole.FieldInitializer,
                                new(start, 1))),
                    ],
                }),
        };

        Assert.Contains(
            "exceeds the body-evidence comparison budget",
            Assert.Throws<ArgumentException>(() => Create(input)).Message);
    }

    [Fact]
    public void Create_RejectsAggregateNodeBudgetExhaustion()
    {
        var input = Input();
        IEnumerable<CSharpTypePhysicalArtifact> artifacts = Enumerable.Repeat(
            input.Artifacts[0],
            CSharpTypeDocument.MaxDocumentNodes + 1);

        Assert.Throws<ArgumentException>(() => CSharpTypeDocument.Create(
            input.TypeName,
            input.TypeAddress,
            input.Source,
            input.Frame,
            artifacts,
            input.Bodies,
            input.Declarations,
            input.Documentation,
            CSharpTypeContractRelationshipCapability.Unavailable));
    }

    [Fact]
    public void Create_RejectsAggregateStringsBeforeIssuingUnreplayableDocument()
    {
        var input = Input() with
        {
            Source = Input().Source with
            {
                RenderingPolicy = new string(
                    'x',
                    MetadataSafetyPolicy.MaxStructuralSignatureWorkChars + 1),
            },
        };

        Assert.Throws<ArgumentException>(() => Create(input));
    }

    [Fact]
    public void Create_ChargesDeclarationSeparatorForEveryProjectedDeclaration()
    {
        var input = Input() with
        {
            Frame = Input().Frame with
            {
                DeclarationSeparator = new string(
                    ' ',
                    MetadataSafetyPolicy.MaxStructuralSignatureWorkChars / 2),
            },
        };

        Assert.Throws<ArgumentException>(() => Create(input));
    }

    [Fact]
    public void Create_RequiresBodiesToBelongToTheTypeDefModule()
    {
        var input = Input();
        input.Bodies[0] = input.Bodies[0] with
        {
            Address = input.Bodies[0].Address with
            {
                ModuleVersionId = Guid.NewGuid(),
            },
        };

        Assert.Throws<ArgumentException>(() => Create(input));
    }

    [Theory]
    [InlineData(CSharpTypeBodyOutcome.Available, DecompilationFidelity.Failed)]
    [InlineData(CSharpTypeBodyOutcome.Available, null)]
    [InlineData(CSharpTypeBodyOutcome.Failed, DecompilationFidelity.Full)]
    [InlineData(CSharpTypeBodyOutcome.Unavailable, DecompilationFidelity.Partial)]
    [InlineData(CSharpTypeBodyOutcome.NoBody, DecompilationFidelity.Full)]
    public void Create_RejectsInvalidBodyOutcomeAndFidelity(
        CSharpTypeBodyOutcome outcome,
        DecompilationFidelity? fidelity)
    {
        var input = Input();
        input.Bodies[0] = input.Bodies[0] with
        {
            HasManagedBody = outcome != CSharpTypeBodyOutcome.NoBody,
            Outcome = outcome,
            Fidelity = fidelity,
        };

        Assert.Throws<ArgumentException>(() => Create(input));
    }

    [Fact]
    public void Revision_IsStableAndCoversRenderPlansAndBodyEvidence()
    {
        CSharpTypeDocument first = Create(Input());
        CSharpTypeDocument replay = Create(Input());
        Assert.Equal(first.Revision, replay.Revision);

        var changedText = Input();
        CSharpTypeDeclaration field = changedText.Declarations[0];
        CSharpTypeRenderPart initializer = field.Parts[1];
        changedText.Declarations[0] = field with
        {
            Parts = field.Parts.SetItem(
                1,
                initializer with { FullText = " = 3" }),
        };
        Assert.NotEqual(first.Revision, Create(changedText).Revision);

        var changedEvidence = Input();
        changedEvidence.Bodies[0] = changedEvidence.Bodies[0] with
        {
            Fingerprint = new string('B', 64),
        };
        Assert.NotEqual(first.Revision, Create(changedEvidence).Revision);

        var changedDiagnostics = Input();
        changedDiagnostics.Bodies[0] = changedDiagnostics.Bodies[0] with
        {
            Diagnostics = [new("D2000", "Different body evidence.")],
        };
        Assert.NotEqual(first.Revision, Create(changedDiagnostics).Revision);
    }

    [Fact]
    public void Capabilities_DistinguishDocumentationStatesAndRejectRelationships()
    {
        DocumentInput input = Input();
        CSharpTypeDocument absent = Create(input);
        CSharpTypeDocument unavailable = Create(input with
        {
            Documentation = CSharpTypeDocumentationCapability.Unavailable,
        });
        CSharpTypeDocument availableEmpty = Create(input with
        {
            Documentation = CSharpTypeDocumentationCapability.Available,
        });

        Assert.NotEqual(absent.Revision, unavailable.Revision);
        Assert.NotEqual(absent.Revision, availableEmpty.Revision);
        Assert.NotEqual(unavailable.Revision, availableEmpty.Revision);

        Assert.Throws<ArgumentException>(() => CSharpTypeDocument.Create(
            input.TypeName,
            input.TypeAddress,
            input.Source,
            input.Frame,
            input.Artifacts,
            input.Bodies,
            input.Declarations,
            input.Documentation,
            CSharpTypeContractRelationshipCapability.Available));
    }

    [Fact]
    public void AbsentDocumentation_CannotCarryDocumentationRegions()
    {
        DocumentInput input = Input();
        input = input with
        {
            Frame = input.Frame with
            {
                PrefixParts =
                [
                    new(
                        0,
                        CSharpTypeRenderPartKind.Documentation,
                        CSharpTypeRegionRole.Documentation,
                        "/// docs\n",
                        "/// docs\n"),
                    Fixed(1, input.Frame.PrefixParts[0].FullText),
                ],
            },
        };

        Assert.Throws<ArgumentException>(() => Create(input));
        CSharpTypeDocument available = Create(input with
        {
            Documentation = CSharpTypeDocumentationCapability.Available,
        });
        Assert.Equal(
            CSharpTypeDocumentationCapability.Available,
            available.Documentation);
    }

    [Fact]
    public void Json_RoundTripsAndRejectsDuplicateOrStalePayloads()
    {
        CSharpTypeDocument document = Create(Input());
        string json = CSharpTypeDocumentJson.Serialize(document, indented: false);

        CSharpTypeDocument replay = CSharpTypeDocumentJson.Deserialize(json);
        Assert.Equal(document.Revision, replay.Revision);
        Assert.Equal(
            Project(document, new()).Text,
            Project(replay, new()).Text);

        string duplicate = json.Replace(
            "{\"schema_version\":1,",
            "{\"schema_version\":1,\"schema_version\":1,",
            StringComparison.Ordinal);
        Assert.Throws<JsonException>(
            () => CSharpTypeDocumentJson.Deserialize(duplicate));

        JsonObject stale = Assert.IsType<JsonObject>(JsonNode.Parse(json));
        stale["source"]!["rendering_policy"] = "changed-policy";
        Assert.Throws<JsonException>(
            () => CSharpTypeDocumentJson.Deserialize(stale.ToJsonString()));

        JsonObject missing = Assert.IsType<JsonObject>(JsonNode.Parse(json));
        Assert.True(missing["source"]!.AsObject().Remove("kind"));
        Assert.Throws<JsonException>(
            () => CSharpTypeDocumentJson.Deserialize(missing.ToJsonString()));

        JsonObject contradictory = Assert.IsType<JsonObject>(JsonNode.Parse(json));
        contradictory["artifacts"]!.AsArray()[0]!["role"] =
            (int)CSharpTypeArtifactRole.Getter;
        Assert.Throws<JsonException>(
            () => CSharpTypeDocumentJson.Deserialize(
                contradictory.ToJsonString()));

        JsonObject mismatchedRegion =
            Assert.IsType<JsonObject>(JsonNode.Parse(json));
        mismatchedRegion["frame"]!["prefix_parts"]!.AsArray()[0]!["region"] =
            (int)CSharpTypeRegionRole.Documentation;
        Assert.Throws<JsonException>(
            () => CSharpTypeDocumentJson.Deserialize(
                mismatchedRegion.ToJsonString()));

        JsonObject missingSkeletonDifference =
            Assert.IsType<JsonObject>(JsonNode.Parse(json));
        JsonNode constructorPart =
            missingSkeletonDifference["declarations"]!.AsArray()[2]!["parts"]!
                .AsArray()[1]!;
        constructorPart["skeleton_text"] = constructorPart["full_text"]!
            .GetValue<string>()
            .Replace(
                "_a = 1;",
                "_a  = 1;",
                StringComparison.Ordinal);
        Assert.Throws<JsonException>(
            () => CSharpTypeDocumentJson.Deserialize(
                missingSkeletonDifference.ToJsonString()));

        JsonObject emptyDeclaration =
            Assert.IsType<JsonObject>(JsonNode.Parse(json));
        JsonNode whitespaceSignature = emptyDeclaration["declarations"]!
            .AsArray()[0]!["parts"]!.AsArray()[0]!.DeepClone();
        whitespaceSignature["full_text"] = " ";
        whitespaceSignature["skeleton_text"] = " ";
        emptyDeclaration["declarations"]!.AsArray()[0]!["parts"] =
            new JsonArray(whitespaceSignature);
        Assert.Throws<JsonException>(
            () => CSharpTypeDocumentJson.Deserialize(
                emptyDeclaration.ToJsonString()));

        JsonObject amplifiedComparison =
            Assert.IsType<JsonObject>(JsonNode.Parse(json));
        JsonNode initializer =
            amplifiedComparison["declarations"]!.AsArray()[0]!["parts"]!
                .AsArray()[1]!;
        initializer["full_text"] = new string('x', 65);
        initializer["skeleton_text"] = new string('y', 64 * 1024);
        initializer["contributions"] = new JsonArray(
            Enumerable.Range(0, 65)
                .Select(static start => (JsonNode)new JsonObject
                {
                    ["body_id"] = 0,
                    ["role"] =
                        (int)CSharpTypeBodyContributionRole.FieldInitializer,
                    ["full_range"] = new JsonObject
                    {
                        ["start"] = start,
                        ["length"] = 1,
                    },
                })
                .ToArray());
        Assert.Throws<JsonException>(
            () => CSharpTypeDocumentJson.Deserialize(
                amplifiedComparison.ToJsonString()));

        Assert.Throws<JsonException>(() => CSharpTypeDocumentJson.Deserialize(
            new string(' ', CSharpTypeDocumentJson.MaxSerializedCharacters + 1)));
    }

    [Fact]
    public void BodiesAndSkeleton_UseOwnerIssuedAlternativesAndFreshRanges()
    {
        CSharpTypeDocument document = Create(Input());

        CSharpTypeDocumentProjection bodies = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));
        CSharpTypeDocumentProjection skeleton = Project(
            document,
            new(CSharpTypeBodyMode.Skeleton));

        Assert.Equal(
            """
            public class Sample
            {
                private int _a = 1;
                private int _b = 2;
                public Sample() { _a = 1; _b = 2; }
                public int X { get { return _a; } set { _a = value; } }
            }
            """,
            bodies.Text);
        Assert.Equal(
            """
            public class Sample
            {
                private int _a;
                private int _b;
                public Sample() { }
                public int X { get; set; }
            }
            """,
            skeleton.Text);

        foreach (CSharpTypeProjectedDeclaration declaration in bodies.Declarations)
        {
            Assert.Equal(
                declaration.Anchor.StableSelector switch
                {
                    "_a" => "private int _a = 1;",
                    "_b" => "private int _b = 2;",
                    ".ctor()" => "public Sample() { _a = 1; _b = 2; }",
                    "X" => "public int X { get { return _a; } set { _a = value; } }",
                    _ => throw new InvalidOperationException(),
                },
                Slice(bodies.Text, declaration.Range));
        }

        Assert.Equal(
            "{ _a = 1; _b = 2; }",
            Slice(
                bodies.Text,
                Assert.Single(bodies.Declarations[2].Bodies).Range));
        Assert.Equal(
            ["{ return _a; }", "{ _a = value; }"],
            bodies.Declarations[3].Bodies
                .Select(body => Slice(bodies.Text, body.Range)));
        Assert.Empty(skeleton.Declarations.SelectMany(static row => row.Bodies));
    }

    [Fact]
    public void Projection_PreservesDeclarationIdentityAndClassifications()
    {
        CSharpTypeDocument document = Create(Input());
        CSharpTypeProjectionRequest[] requests =
        [
            new(CSharpTypeBodyMode.Bodies),
            new(CSharpTypeBodyMode.Skeleton),
            new(
                CSharpTypeBodyMode.SelectedBody,
                document.Declarations[2].Anchor),
            new(accessibilities: [CSharpTypeAccessibility.Public]),
        ];

        foreach (CSharpTypeProjectionRequest request in requests)
        {
            CSharpTypeDocumentProjection projection = Project(document, request);
            foreach (CSharpTypeProjectedDeclaration row in projection.Declarations)
            {
                CSharpTypeDeclaration declaration =
                    document.Declarations[row.DeclarationId];
                Assert.Equal(declaration.Anchor, row.Anchor);
                Assert.Equal(declaration.DeclarationToken, row.DeclarationToken);
                Assert.Equal(declaration.Kind, row.Kind);
                Assert.Equal(declaration.Accessibility, row.Accessibility);
                Assert.Equal(declaration.Placement, row.Placement);
                Assert.Equal(declaration.Origin, row.Origin);
            }
        }
    }

    [Fact]
    public void FailedBodies_AlwaysUseSkeletonAndEmitDiagnostics()
    {
        var input = Input();
        input.Bodies[0] = input.Bodies[0] with
        {
            Outcome = CSharpTypeBodyOutcome.Failed,
            Fidelity = DecompilationFidelity.Failed,
            Diagnostics = [new("D1000", "Body production failed.")],
        };
        CSharpTypeDocument document = Create(input);

        CSharpTypeDocumentProjection bodies = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));
        CSharpTypeDocumentProjection skeleton = Project(
            document,
            new(CSharpTypeBodyMode.Skeleton));
        CSharpTypeDocumentProjection filtered = Project(
            document,
            new(
                CSharpTypeBodyMode.Bodies,
                placement: CSharpTypePlacementFilter.Static));

        Assert.Contains("private int _a;", bodies.Text);
        Assert.Contains("private int _b;", bodies.Text);
        Assert.Contains("public Sample() { }", bodies.Text);
        Assert.Single(bodies.Diagnostics);
        Assert.Single(skeleton.Diagnostics);
        Assert.Single(filtered.Diagnostics);
        Assert.Equal(0, bodies.Diagnostics[0].BodyId);
        Assert.Equal(0, skeleton.Diagnostics[0].BodyId);
        Assert.Equal(0, filtered.Diagnostics[0].BodyId);
        Assert.Equal(2, bodies.Diagnostics[0].DeclarationId);
        Assert.Equal(2, skeleton.Diagnostics[0].DeclarationId);
        Assert.Equal(2, filtered.Diagnostics[0].DeclarationId);
    }

    [Fact]
    public void Create_AllowsEmptyManagedBodyEvidence()
    {
        var input = Input();
        CSharpTypeDeclaration constructor = input.Declarations[2];
        CSharpTypeRenderPart implementation = constructor.Parts[1];
        input.Declarations[2] = constructor with
        {
            Parts = constructor.Parts.SetItem(
                1,
                implementation with
                {
                    FullText = " { }",
                    SkeletonText = " { }",
                    OwnedBodies =
                    [
                        implementation.OwnedBodies[0] with
                        {
                            FullRange = new(2, 0),
                        },
                    ],
                }),
        };

        CSharpTypeDocument document = Create(input);
        CSharpTypeDocument replay = CSharpTypeDocumentJson.Deserialize(
            CSharpTypeDocumentJson.Serialize(document, indented: false));
        CSharpTypeDocumentProjection bodies = Project(
            replay,
            new(CSharpTypeBodyMode.Bodies));

        Assert.Equal(
            0,
            Assert.Single(bodies.Declarations[2].Bodies).Range.Length);
    }

    [Fact]
    public void BodylessMethods_DoNotEmitUnavailableDiagnostics()
    {
        var input = Input();
        input.Bodies[0] = input.Bodies[0] with
        {
            HasManagedBody = false,
            Outcome = CSharpTypeBodyOutcome.NoBody,
            Fidelity = null,
        };
        CSharpTypeDocumentProjection projection = Project(
            Create(input),
            new(CSharpTypeBodyMode.Bodies));

        Assert.DoesNotContain(
            projection.Diagnostics,
            static diagnostic => diagnostic.BodyId == 0);
    }

    [Fact]
    public void FailedSetter_PreservesAvailableGetterBody()
    {
        var input = Input();
        input.Bodies[2] = input.Bodies[2] with
        {
            Outcome = CSharpTypeBodyOutcome.Failed,
            Fidelity = DecompilationFidelity.Failed,
            Diagnostics = [new("D2000", "Setter production failed.")],
        };
        CSharpTypeDocument document = Create(input);

        CSharpTypeDocumentProjection projection = Project(
            document,
            new(CSharpTypeBodyMode.Bodies));

        Assert.Contains(
            "public int X { get { return _a; } set; }",
            projection.Text);
        Assert.Equal(
            "{ return _a; }",
            Slice(
                projection.Text,
                Assert.Single(projection.Declarations[3].Bodies).Range));
        Assert.Equal(2, Assert.Single(projection.Diagnostics).BodyId);
    }

    [Fact]
    public void SelectedBody_RejectsFailedBodyWithoutObservableDifference()
    {
        var input = Input();
        input.Bodies[0] = input.Bodies[0] with
        {
            Outcome = CSharpTypeBodyOutcome.Failed,
            Fidelity = DecompilationFidelity.Failed,
            Diagnostics = [new("D1000", "Body production failed.")],
        };
        CSharpTypeDocument document = Create(input);

        var rejected = Assert.IsType<CSharpTypeProjectionOutcome.Rejected>(
            CSharpTypeDocumentProjector.Project(
                document,
                new(
                    CSharpTypeBodyMode.SelectedBody,
                    document.Declarations[2].Anchor)));

        Assert.Equal(
            CSharpTypeProjectionFailureKind.SelectedMemberHasNoImplementationDifference,
            rejected.Kind);
    }

    [Fact]
    public void SelectedBody_ProjectsTypeFrameContributionRanges()
    {
        var input = Input();
        const string contribution = "// from ctor\n";
        input = input with
        {
            Frame = input.Frame with
            {
                PrefixParts =
                [
                    input.Frame.PrefixParts[0],
                    Implementation(
                        1,
                        contribution,
                        "",
                        CSharpTypeImplementationKind.Initializer,
                        contributions:
                        [
                            new(
                                0,
                                CSharpTypeBodyContributionRole.LoweredImplementation,
                                new(3, 9)),
                        ]),
                ],
            },
        };
        CSharpTypeDocument document = Create(input);

        CSharpTypeDocumentProjection projection = Project(
            document,
            new(
                CSharpTypeBodyMode.SelectedBody,
                document.Declarations[2].Anchor));

        CSharpTypeProjectedContribution projected =
            Assert.Single(projection.FrameContributions);
        Assert.Equal(0, projected.BodyId);
        Assert.Equal(
            "from ctor",
            Slice(projection.Text, projected.Range));
    }

    [Fact]
    public void SelectedConstructor_ExpandsContributionClosureOnly()
    {
        CSharpTypeDocument document = Create(Input());

        CSharpTypeDocumentProjection projection = Project(
            document,
            new(
                CSharpTypeBodyMode.SelectedBody,
                document.Declarations[2].Anchor));

        Assert.Equal(
            """
            public class Sample
            {
                private int _a = 1;
                private int _b = 2;
                public Sample() { _a = 1; _b = 2; }
                public int X { get; set; }
            }
            """,
            projection.Text);
        Assert.Equal(
            ["= 1", "= 2"],
            projection.Declarations
                .SelectMany(static declaration => declaration.Contributions)
                .Select(contribution => Slice(projection.Text, contribution.Range)));
        Assert.Single(projection.Declarations[2].Bodies);
        Assert.Empty(projection.Declarations[3].Bodies);
    }

    [Fact]
    public void SelectedField_ExpandsItsLocalInitializerWithoutConstructor()
    {
        CSharpTypeDocument document = Create(Input());

        CSharpTypeDocumentProjection projection = Project(
            document,
            new(
                CSharpTypeBodyMode.SelectedBody,
                document.Declarations[0].Anchor));

        Assert.Contains("private int _a = 1;", projection.Text);
        Assert.Contains("private int _b;", projection.Text);
        Assert.Contains("public Sample() { }", projection.Text);
        Assert.DoesNotContain("_b = 2", projection.Text, StringComparison.Ordinal);
        Assert.Empty(projection.Declarations.SelectMany(static row => row.Bodies));
    }

    [Fact]
    public void SelectedBody_ReportsContributionsHiddenByStructuralFilters()
    {
        CSharpTypeDocument document = Create(Input());

        CSharpTypeDocumentProjection projection = Project(
            document,
            new(
                CSharpTypeBodyMode.SelectedBody,
                document.Declarations[2].Anchor,
                accessibilities: [CSharpTypeAccessibility.Public]));

        Assert.DoesNotContain("private int", projection.Text, StringComparison.Ordinal);
        Assert.Equal(
            2,
            projection.Diagnostics.Count(diagnostic =>
                diagnostic.Kind
                    == CSharpTypeProjectionDiagnosticKind.HiddenSelectedBodyContribution));
        Assert.Equal([0, 1], projection.Diagnostics
            .Where(diagnostic =>
                diagnostic.Kind
                    == CSharpTypeProjectionDiagnosticKind.HiddenSelectedBodyContribution)
            .Select(static diagnostic => diagnostic.DeclarationId));
    }

    [Fact]
    public void HiddenContributions_ReportEachBodyAndRole()
    {
        var input = Input();
        CSharpTypeDeclaration field = input.Declarations[0];
        CSharpTypeRenderPart initializer = field.Parts[1];
        input.Declarations[0] = field with
        {
            Parts = field.Parts.SetItem(
                1,
                initializer with
                {
                    Contributions =
                    [
                        new(
                            1,
                            CSharpTypeBodyContributionRole.LoweredImplementation,
                            new(1, 3)),
                        new(
                            2,
                            CSharpTypeBodyContributionRole.PropertyInitializer,
                            new(1, 3)),
                    ],
                }),
        };
        CSharpTypeDocument document = Create(input);

        CSharpTypeDocumentProjection projection = Project(
            document,
            new(
                CSharpTypeBodyMode.SelectedBody,
                document.Declarations[3].Anchor,
                accessibilities: [CSharpTypeAccessibility.Public]));

        CSharpTypeProjectionDiagnostic[] hidden =
            [.. projection.Diagnostics.Where(diagnostic =>
                diagnostic.Kind
                    == CSharpTypeProjectionDiagnosticKind.HiddenSelectedBodyContribution)];
        Assert.Equal([1, 2], hidden.Select(static diagnostic => diagnostic.BodyId));
        Assert.Equal(
            [
                CSharpTypeBodyContributionRole.LoweredImplementation,
                CSharpTypeBodyContributionRole.PropertyInitializer,
            ],
            hidden.Select(static diagnostic => diagnostic.ContributionRole));
    }

    [Fact]
    public void SelectedBody_RejectsHiddenAndForeignMembers()
    {
        CSharpTypeDocument document = Create(Input());

        var hidden = Assert.IsType<CSharpTypeProjectionOutcome.Rejected>(
            CSharpTypeDocumentProjector.Project(
                document,
                new(
                    CSharpTypeBodyMode.SelectedBody,
                    document.Declarations[0].Anchor,
                    accessibilities: [CSharpTypeAccessibility.Public])));
        Assert.Equal(
            CSharpTypeProjectionFailureKind.SelectedMemberHidden,
            hidden.Kind);

        var foreign = Assert.IsType<CSharpTypeProjectionOutcome.Rejected>(
            CSharpTypeDocumentProjector.Project(
                document,
                new(
                    CSharpTypeBodyMode.SelectedBody,
                    Anchor("Foreign", "void Foreign()"))));
        Assert.Equal(
            CSharpTypeProjectionFailureKind.SelectedMemberNotFound,
            foreign.Kind);
    }

    [Fact]
    public void NarrowPlacementFilters_DoNotGuessUnclassifiedDeclarations()
    {
        var input = Input();
        input.Declarations[3] = input.Declarations[3] with
        {
            Placement = CSharpTypeDeclarationPlacement.Unclassified,
        };
        CSharpTypeDocument document = Create(input);

        CSharpTypeDocumentProjection projection = Project(
            document,
            new(placement: CSharpTypePlacementFilter.Static));

        Assert.Empty(projection.Declarations);
        Assert.Equal("public class Sample\n{\n}", projection.Text);
    }

    [Fact]
    public void Outcomes_RequireExactIncompleteBodyEvidence()
    {
        var input = Input();
        input.Bodies[0] = input.Bodies[0] with
        {
            Outcome = CSharpTypeBodyOutcome.Failed,
            Fidelity = DecompilationFidelity.Failed,
            Diagnostics = [new("D1000", "Body production failed.")],
        };
        CSharpTypeDocument document = Create(input);

        Assert.Throws<ArgumentException>(
            () => new CSharpTypeDocumentOutcome.Available(document));
        var incomplete = new CSharpTypeDocumentOutcome.Incomplete(document, [0]);
        Assert.Equal([0], incomplete.FailedBodyIds);
        Assert.Throws<ArgumentException>(
            () => new CSharpTypeDocumentOutcome.Incomplete(document, [1]));
    }

    static CSharpTypeDocumentProjection Project(
        CSharpTypeDocument document,
        CSharpTypeProjectionRequest request)
        => Assert.IsType<CSharpTypeProjectionOutcome.Projected>(
            CSharpTypeDocumentProjector.Project(document, request)).Projection;

    static string Slice(string text, CSharpSourceRange range)
        => text.Substring(range.Start, range.Length);

    static CSharpTypeDocument Create(DocumentInput input)
        => CSharpTypeDocument.Create(
            input.TypeName,
            input.TypeAddress,
            input.Source,
            input.Frame,
            input.Artifacts,
            input.Bodies,
            input.Declarations,
            input.Documentation);

    static DocumentInput Input()
    {
        Guid mvid = Guid.Parse("D1D973E2-91F0-45F2-8B5D-4F85944B8114");
        MemberAnchor fieldA = Anchor("_a", "int Sample._a", "aaaaaaaaaa");
        MemberAnchor fieldB = Anchor("_b", "int Sample._b", "aaaaaaaaaa");
        MemberAnchor constructor = Anchor(".ctor()", "void Sample..ctor()");
        MemberAnchor getter = Anchor(
            "get_X()",
            "int Sample.get_X()");
        MemberAnchor setter = Anchor(
            "set_X(int)",
            "void Sample.set_X(int value)");
        MemberAnchor property = Anchor("X", "int Sample.X");

        const string constructorBody = " { _a = 1; _b = 2; }";
        const string getterBody = " get { return _a; }";
        const string setterBody = " set { _a = value; }";
        int getterStart = getterBody.IndexOf("{ return", StringComparison.Ordinal);
        int setterStart = setterBody.IndexOf("{ _a = value", StringComparison.Ordinal);

        return new(
            TypeName("Samples", "Sample"),
            MetadataTypeDefinitionAddress.FromToken(
                mvid,
                0x02000001),
            new(
                CSharpTypeSourceKind.Decompiled,
                "Sample.Assembly",
                PdbSupplied: false,
                DecompilerSymbolSource.None,
                "structured-type-document-v1"),
            new(
                [
                    Fixed(0, "public class Sample\n{"),
                ],
                "\n    ",
                "\n}"),
            [
                Artifact(0, fieldA, 0x04000001, CSharpTypeArtifactKind.Field, 0),
                Artifact(1, fieldB, 0x04000002, CSharpTypeArtifactKind.Field, 1),
                Artifact(2, constructor, 0x06000001, CSharpTypeArtifactKind.Method, 2),
                Artifact(
                    3,
                    getter,
                    0x06000002,
                    CSharpTypeArtifactKind.Method,
                    3,
                    CSharpTypeArtifactRole.Getter),
                Artifact(
                    4,
                    setter,
                    0x06000003,
                    CSharpTypeArtifactKind.Method,
                    3,
                    CSharpTypeArtifactRole.Setter),
                Artifact(5, property, 0x17000001, CSharpTypeArtifactKind.Property, 3),
            ],
            [
                Body(0, mvid, 0x06000001, 2, CSharpTypeBodyRole.Method, 'A'),
                Body(1, mvid, 0x06000002, 3, CSharpTypeBodyRole.Getter, 'B'),
                Body(2, mvid, 0x06000003, 4, CSharpTypeBodyRole.Setter, 'C'),
            ],
            [
                new(
                    0,
                    0,
                    fieldA,
                    0x04000001,
                    CSharpTypeDeclarationKind.Field,
                    CSharpTypeAccessibility.Private,
                    CSharpTypeDeclarationPlacement.Instance,
                    CSharpTypeOrigin.NonGenerated,
                    [
                        Fixed(0, "private int _a"),
                        Implementation(
                            1,
                            " = 1",
                            "",
                            CSharpTypeImplementationKind.Initializer,
                            contributions:
                            [
                                new(
                                    0,
                                    CSharpTypeBodyContributionRole.FieldInitializer,
                                    new(1, 3)),
                            ]),
                        Fixed(2, ";"),
                    ]),
                new(
                    1,
                    1,
                    fieldB,
                    0x04000002,
                    CSharpTypeDeclarationKind.Field,
                    CSharpTypeAccessibility.Private,
                    CSharpTypeDeclarationPlacement.Instance,
                    CSharpTypeOrigin.NonGenerated,
                    [
                        Fixed(0, "private int _b"),
                        Implementation(
                            1,
                            " = 2",
                            "",
                            CSharpTypeImplementationKind.Initializer,
                            contributions:
                            [
                                new(
                                    0,
                                    CSharpTypeBodyContributionRole.FieldInitializer,
                                    new(1, 3)),
                            ]),
                        Fixed(2, ";"),
                    ]),
                new(
                    2,
                    2,
                    constructor,
                    0x06000001,
                    CSharpTypeDeclarationKind.Constructor,
                    CSharpTypeAccessibility.Public,
                    CSharpTypeDeclarationPlacement.Instance,
                    CSharpTypeOrigin.NonGenerated,
                    [
                        Fixed(0, "public Sample()"),
                        Implementation(
                            1,
                            constructorBody,
                            " { }",
                            CSharpTypeImplementationKind.Body,
                            ownedBodies:
                            [
                                new(
                                    0,
                                    new(1, constructorBody.Length - 1)),
                            ]),
                    ]),
                new(
                    3,
                    3,
                    property,
                    0x17000001,
                    CSharpTypeDeclarationKind.Property,
                    CSharpTypeAccessibility.Public,
                    CSharpTypeDeclarationPlacement.Instance,
                    CSharpTypeOrigin.NonGenerated,
                    [
                        Fixed(0, "public int X"),
                        Fixed(1, " {"),
                        Implementation(
                            2,
                            getterBody,
                            " get;",
                            CSharpTypeImplementationKind.Body,
                            ownedBodies:
                            [
                                new(
                                    1,
                                    new(
                                        getterStart,
                                        "{ return _a; }".Length)),
                            ]),
                        Implementation(
                            3,
                            setterBody,
                            " set;",
                            CSharpTypeImplementationKind.Body,
                            ownedBodies:
                            [
                                new(
                                    2,
                                    new(
                                        setterStart,
                                        "{ _a = value; }".Length)),
                            ]),
                        Fixed(4, " }"),
                    ]),
            ],
            CSharpTypeDocumentationCapability.Absent);
    }

    static CSharpTypePhysicalArtifact Artifact(
        int id,
        MemberAnchor anchor,
        int token,
        CSharpTypeArtifactKind kind,
        int declarationId,
        CSharpTypeArtifactRole role = CSharpTypeArtifactRole.Declaration)
        => new(
            id,
            anchor,
            token,
            kind,
            CSharpTypeOrigin.NonGenerated,
            new(
                CSharpTypeArtifactRepresentationKind.Declaration,
                role,
                declarationId));

    static CSharpTypePhysicalBody Body(
        int id,
        Guid mvid,
        int token,
        int artifactId,
        CSharpTypeBodyRole role,
        char fingerprint)
        => new(
            id,
            new(
                mvid,
                MetadataTokens.MethodDefinitionHandle(token & 0x00FFFFFF)),
            artifactId,
            role,
            HasManagedBody: true,
            CSharpTypeBodyOutcome.Available,
            DecompilationFidelity.Full,
            new string(fingerprint, 64),
            []);

    static CSharpTypeRenderPart Fixed(int id, string text)
        => new(
            id,
            CSharpTypeRenderPartKind.Fixed,
            CSharpTypeRegionRole.Signature,
            text,
            text);

    static CSharpTypeRenderPart Implementation(
        int id,
        string full,
        string skeleton,
        CSharpTypeImplementationKind kind,
        ImmutableArray<CSharpTypeOwnedBodyReference> ownedBodies = default,
        ImmutableArray<CSharpTypeBodyContribution> contributions = default)
        => new(
            id,
            CSharpTypeRenderPartKind.Implementation,
            CSharpTypeRegionRole.Implementation,
            full,
            skeleton,
            kind,
            ownedBodies,
            contributions);

    static MemberAnchor Anchor(
        string selector,
        string signature,
        string? fingerprint = null)
        => new(
            selector,
            signature,
            fingerprint ?? MemberAnchor.ComputeFingerprint(signature),
            "Samples.Sample",
            selector);

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        params string[] segments)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments])).Name;

    sealed record DocumentInput(
        MetadataTypeDefinitionName TypeName,
        MetadataTypeDefinitionAddress TypeAddress,
        CSharpTypeDocumentSource Source,
        CSharpTypeFrame Frame,
        List<CSharpTypePhysicalArtifact> Artifacts,
        List<CSharpTypePhysicalBody> Bodies,
        List<CSharpTypeDeclaration> Declarations,
        CSharpTypeDocumentationCapability Documentation);
}
