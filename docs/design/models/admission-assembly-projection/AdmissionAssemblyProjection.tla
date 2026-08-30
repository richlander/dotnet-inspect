------------------- MODULE AdmissionAssemblyProjection -------------------
(***************************************************************************)
(* Target admission-scoped artifact-to-assembly projection for             *)
(* docs/design/assembly-inspection-query.md.                                *)
(*                                                                         *)
(* The artifact owner validates admission or query authority and lends one  *)
(* immutable image for a callback. The assembly-query owner classifies the  *)
(* admission image into content-free facts and later validates query bytes  *)
(* against those frozen facts. It never retains a path, opener, stream,     *)
(* content reference, lease, or bytes.                                      *)
(*                                                                         *)
(* Images A-E are managed assemblies. B keeps A's identity but changes the  *)
(* MVID; C keeps A's MVID but changes identity; D keeps both but belongs to  *)
(* a different artifact generation and identity; E keeps both and the       *)
(* generation but reports a different artifact identity. The remaining      *)
(* exercise native, netmodule, malformed, and empty-MVID classification.    *)
(*                                                                         *)
(* Mutation constants independently weaken the authority, output, or        *)
(* validation rules. Witness variables latch the pre-state condition at the *)
(* action that depends on it, so a mutation cannot make its own property    *)
(* vacuously true by changing later state.                                  *)
(***************************************************************************)
EXTENDS FiniteSets, TLC

CONSTANTS
    AllowStaleAdmission,
    LeakContentAuthority,
    DropArtifactRegistration,
    AcceptRegistrationMismatch,
    AcceptIdentityMismatch,
    AcceptMvidMismatch,
    AllowRevokedQuery

ASSUME
    /\ AllowStaleAdmission \in BOOLEAN
    /\ LeakContentAuthority \in BOOLEAN
    /\ DropArtifactRegistration \in BOOLEAN
    /\ AcceptRegistrationMismatch \in BOOLEAN
    /\ AcceptIdentityMismatch \in BOOLEAN
    /\ AcceptMvidMismatch \in BOOLEAN
    /\ AllowRevokedQuery \in BOOLEAN

NoImage == "NoImage"
NoValue == "NoValue"

ManagedAssemblies == {"A", "B", "C", "D", "E"}
Images ==
    ManagedAssemblies
        \union {"Native", "Module", "Malformed", "EmptyMvid"}

ImageKind(image) ==
    CASE image \in ManagedAssemblies -> "Assembly"
      [] image = "Native" -> "Native"
      [] image = "Module" -> "Module"
      [] image = "Malformed" -> "Malformed"
      [] image = "EmptyMvid" -> "EmptyMvid"

ImageIdentity(image) ==
    CASE image \in {"A", "B", "D", "E", "EmptyMvid"} -> "Identity1"
      [] image = "C" -> "Identity2"
      [] OTHER -> NoValue

ImageMvid(image) ==
    CASE image \in {"A", "C", "D", "E"} -> "Mvid1"
      [] image = "B" -> "Mvid2"
      [] OTHER -> NoValue

ImageGeneration(image) ==
    IF image = "D" THEN "Generation2" ELSE "Generation1"

ImageRegistration(image) ==
    CASE image = "D" -> "ArtifactRegistration3"
      [] image = "E" -> "ArtifactRegistration2"
      [] OTHER -> "ArtifactRegistration1"

VARIABLES
    admissionAuthority,
    projectionState,
    projectionReason,
    projectionImage,
    projectionGeneration,
    projectionRegistration,
    projectionIdentity,
    projectionMvid,
    projectionCarriesAuthority,
    published,
    queryAuthority,
    queryState,
    queryReason,
    queryImage,
    admissionAuthorityWitness,
    contentFreeWitness,
    exactRegistrationWitness,
    publicationWitness,
    queryAuthorityWitness,
    queryMatchWitness,
    matchingRoundTripReached,
    mismatchRejectionReached

vars == <<
    admissionAuthority,
    projectionState,
    projectionReason,
    projectionImage,
    projectionGeneration,
    projectionRegistration,
    projectionIdentity,
    projectionMvid,
    projectionCarriesAuthority,
    published,
    queryAuthority,
    queryState,
    queryReason,
    queryImage,
    admissionAuthorityWitness,
    contentFreeWitness,
    exactRegistrationWitness,
    publicationWitness,
    queryAuthorityWitness,
    queryMatchWitness,
    matchingRoundTripReached,
    mismatchRejectionReached
    >>

ProjectionStates == {"None", "Projected", "NotAssembly", "Rejected"}
ProjectionReasons ==
    {"None", "NativeImage", "ManagedModule", "MalformedMetadata",
     "EmptyModuleVersionId"}
QueryStates == {"None", "Validated", "NotAssembly", "Rejected"}
QueryReasons ==
    {"None", "NativeImage", "ManagedModule", "MalformedMetadata",
     "EmptyModuleVersionId", "GenerationMismatch",
     "ArtifactIdentityMismatch", "AssemblyIdentityMismatch",
     "ModuleVersionIdMismatch"}

ExactQueryMatch(image) ==
    /\ image \in ManagedAssemblies
    /\ ImageGeneration(image) = projectionGeneration
    /\ ImageRegistration(image) = projectionRegistration
    /\ ImageIdentity(image) = projectionIdentity
    /\ ImageMvid(image) = projectionMvid

AcceptedQueryMatch(image) ==
    /\ image \in ManagedAssemblies
    /\ ImageGeneration(image) = projectionGeneration
    /\ (AcceptRegistrationMismatch
        \/ ImageRegistration(image) = projectionRegistration)
    /\ (AcceptIdentityMismatch
        \/ ImageIdentity(image) = projectionIdentity)
    /\ (AcceptMvidMismatch
        \/ ImageMvid(image) = projectionMvid)

TypeOK ==
    /\ admissionAuthority \in {"Current", "Revoked", "Ended"}
    /\ projectionState \in ProjectionStates
    /\ projectionReason \in ProjectionReasons
    /\ projectionImage \in Images \union {NoImage}
    /\ projectionGeneration
        \in {"Generation1", "Generation2", NoValue}
    /\ projectionRegistration
        \in {"ArtifactRegistration1", "ArtifactRegistration2",
            "ArtifactRegistration3", NoValue}
    /\ projectionIdentity \in {"Identity1", "Identity2", NoValue}
    /\ projectionMvid \in {"Mvid1", "Mvid2", NoValue}
    /\ projectionCarriesAuthority \in BOOLEAN
    /\ published \in BOOLEAN
    /\ queryAuthority \in {"None", "Current", "Revoked"}
    /\ queryState \in QueryStates
    /\ queryReason \in QueryReasons
    /\ queryImage \in Images \union {NoImage}
    /\ admissionAuthorityWitness \in BOOLEAN
    /\ contentFreeWitness \in BOOLEAN
    /\ exactRegistrationWitness \in BOOLEAN
    /\ publicationWitness \in BOOLEAN
    /\ queryAuthorityWitness \in BOOLEAN
    /\ queryMatchWitness \in BOOLEAN
    /\ matchingRoundTripReached \in BOOLEAN
    /\ mismatchRejectionReached \in BOOLEAN

Init ==
    /\ admissionAuthority = "Current"
    /\ projectionState = "None"
    /\ projectionReason = "None"
    /\ projectionImage = NoImage
    /\ projectionGeneration = NoValue
    /\ projectionRegistration = NoValue
    /\ projectionIdentity = NoValue
    /\ projectionMvid = NoValue
    /\ projectionCarriesAuthority = FALSE
    /\ published = FALSE
    /\ queryAuthority = "None"
    /\ queryState = "None"
    /\ queryReason = "None"
    /\ queryImage = NoImage
    /\ admissionAuthorityWitness = TRUE
    /\ contentFreeWitness = TRUE
    /\ exactRegistrationWitness = TRUE
    /\ publicationWitness = TRUE
    /\ queryAuthorityWitness = TRUE
    /\ queryMatchWitness = TRUE
    /\ matchingRoundTripReached = FALSE
    /\ mismatchRejectionReached = FALSE

RevokeAdmission ==
    /\ admissionAuthority = "Current"
    /\ admissionAuthority' = "Revoked"
    /\ UNCHANGED <<
        projectionState, projectionReason, projectionImage,
        projectionGeneration,
        projectionRegistration, projectionIdentity, projectionMvid,
        projectionCarriesAuthority, published, queryAuthority, queryState,
        queryReason, queryImage, admissionAuthorityWitness, contentFreeWitness,
        exactRegistrationWitness, publicationWitness,
        queryAuthorityWitness, queryMatchWitness,
        matchingRoundTripReached, mismatchRejectionReached
        >>

EndGeneration ==
    /\ admissionAuthority # "Ended"
    /\ admissionAuthority' = "Ended"
    /\ queryAuthority' =
        IF queryAuthority = "Current" THEN "Revoked" ELSE queryAuthority
    /\ UNCHANGED <<
        projectionState, projectionReason, projectionImage,
        projectionGeneration,
        projectionRegistration, projectionIdentity, projectionMvid,
        projectionCarriesAuthority, published, queryState, queryReason,
        queryImage, admissionAuthorityWitness, contentFreeWitness,
        exactRegistrationWitness, publicationWitness,
        queryAuthorityWitness, queryMatchWitness,
        matchingRoundTripReached, mismatchRejectionReached
        >>

Project(image) ==
    /\ image \in Images
    /\ image \notin {"B", "C", "D", "E"}
    /\ projectionState = "None"
    /\ IF AllowStaleAdmission
        THEN admissionAuthority \in {"Current", "Revoked", "Ended"}
        ELSE admissionAuthority = "Current"
    /\ projectionImage' = image
    /\ admissionAuthorityWitness' =
        (admissionAuthorityWitness /\ admissionAuthority = "Current")
    /\ CASE ImageKind(image) = "Assembly" ->
            /\ projectionState' = "Projected"
            /\ projectionReason' = "None"
            /\ projectionGeneration' = ImageGeneration(image)
            /\ projectionRegistration' =
                IF DropArtifactRegistration
                    THEN NoValue
                    ELSE ImageRegistration(image)
            /\ projectionIdentity' = ImageIdentity(image)
            /\ projectionMvid' = ImageMvid(image)
            /\ projectionCarriesAuthority' = LeakContentAuthority
            /\ contentFreeWitness' =
                (contentFreeWitness /\ ~LeakContentAuthority)
            /\ exactRegistrationWitness' =
                (exactRegistrationWitness
                    /\ ~DropArtifactRegistration)
        [] ImageKind(image) \in {"Native", "Module"} ->
            /\ projectionState' = "NotAssembly"
            /\ projectionReason' =
                IF image = "Native"
                    THEN "NativeImage"
                    ELSE "ManagedModule"
            /\ projectionGeneration' = NoValue
            /\ projectionRegistration' = NoValue
            /\ projectionIdentity' = NoValue
            /\ projectionMvid' = NoValue
            /\ projectionCarriesAuthority' = FALSE
            /\ contentFreeWitness' = contentFreeWitness
            /\ exactRegistrationWitness' = exactRegistrationWitness
        [] OTHER ->
            /\ projectionState' = "Rejected"
            /\ projectionReason' =
                IF image = "Malformed"
                    THEN "MalformedMetadata"
                    ELSE "EmptyModuleVersionId"
            /\ projectionGeneration' = NoValue
            /\ projectionRegistration' = NoValue
            /\ projectionIdentity' = NoValue
            /\ projectionMvid' = NoValue
            /\ projectionCarriesAuthority' = FALSE
            /\ contentFreeWitness' = contentFreeWitness
            /\ exactRegistrationWitness' = exactRegistrationWitness
    /\ UNCHANGED <<
        admissionAuthority, published, queryAuthority, queryState, queryReason,
        queryImage, publicationWitness, queryAuthorityWitness,
        queryMatchWitness,
        matchingRoundTripReached, mismatchRejectionReached
        >>

Publish ==
    /\ admissionAuthority = "Current"
    /\ projectionState = "Projected"
    /\ ~published
    /\ published' = TRUE
    /\ admissionAuthority' = "Revoked"
    /\ publicationWitness' =
        (publicationWitness
            /\ projectionRegistration # NoValue
            /\ projectionRegistration
                = ImageRegistration(projectionImage)
            /\ projectionGeneration
                = ImageGeneration(projectionImage)
            /\ projectionIdentity = ImageIdentity(projectionImage)
            /\ projectionMvid = ImageMvid(projectionImage))
    /\ UNCHANGED <<
        projectionState, projectionReason, projectionImage,
        projectionGeneration,
        projectionRegistration, projectionIdentity, projectionMvid,
        projectionCarriesAuthority, queryAuthority, queryState, queryReason,
        queryImage, admissionAuthorityWitness, contentFreeWitness,
        exactRegistrationWitness, queryAuthorityWitness, queryMatchWitness,
        matchingRoundTripReached, mismatchRejectionReached
        >>

AuthorizeQuery ==
    /\ published
    /\ admissionAuthority = "Revoked"
    /\ queryAuthority = "None"
    /\ queryAuthority' = "Current"
    /\ UNCHANGED <<
        admissionAuthority, projectionState, projectionReason,
        projectionImage,
        projectionGeneration, projectionRegistration, projectionIdentity,
        projectionMvid, projectionCarriesAuthority, published, queryState,
        queryReason, queryImage, admissionAuthorityWitness, contentFreeWitness,
        exactRegistrationWitness, publicationWitness,
        queryAuthorityWitness, queryMatchWitness,
        matchingRoundTripReached, mismatchRejectionReached
        >>

RevokeQuery ==
    /\ queryAuthority = "Current"
    /\ queryAuthority' = "Revoked"
    /\ UNCHANGED <<
        admissionAuthority, projectionState, projectionReason,
        projectionImage,
        projectionGeneration, projectionRegistration, projectionIdentity,
        projectionMvid, projectionCarriesAuthority, published, queryState,
        queryReason, queryImage, admissionAuthorityWitness, contentFreeWitness,
        exactRegistrationWitness, publicationWitness,
        queryAuthorityWitness, queryMatchWitness,
        matchingRoundTripReached, mismatchRejectionReached
        >>

ValidateQuery(image) ==
    LET kind == ImageKind(image)
        accepted == AcceptedQueryMatch(image)
        exact == ExactQueryMatch(image)
        outcome ==
            IF kind \in {"Native", "Module"}
                THEN "NotAssembly"
            ELSE IF accepted
                THEN "Validated"
                ELSE "Rejected"
        reason ==
            CASE kind = "Native" -> "NativeImage"
              [] kind = "Module" -> "ManagedModule"
              [] kind = "Malformed" -> "MalformedMetadata"
              [] kind = "EmptyMvid" -> "EmptyModuleVersionId"
              [] ImageGeneration(image) # projectionGeneration ->
                    "GenerationMismatch"
              [] ImageRegistration(image) # projectionRegistration ->
                    "ArtifactIdentityMismatch"
              [] ImageIdentity(image) # projectionIdentity ->
                    "AssemblyIdentityMismatch"
              [] ImageMvid(image) # projectionMvid ->
                    "ModuleVersionIdMismatch"
              [] OTHER -> "None"
    IN
    /\ image \in Images
    /\ published
    /\ queryState = "None"
    /\ IF AllowRevokedQuery
        THEN queryAuthority \in {"Current", "Revoked"}
        ELSE queryAuthority = "Current"
    /\ queryImage' = image
    /\ queryState' = outcome
    /\ queryReason' = reason
    /\ queryAuthorityWitness' =
        (queryAuthorityWitness /\ queryAuthority = "Current")
    /\ queryMatchWitness' =
        (queryMatchWitness /\ (~accepted \/ exact))
    /\ matchingRoundTripReached' =
        (matchingRoundTripReached \/ (accepted /\ exact))
    /\ mismatchRejectionReached' =
        (mismatchRejectionReached
            \/ (outcome \in {"NotAssembly", "Rejected"} /\ ~exact))
    /\ UNCHANGED <<
        admissionAuthority, projectionState, projectionReason,
        projectionImage,
        projectionGeneration, projectionRegistration, projectionIdentity,
        projectionMvid, projectionCarriesAuthority, published,
        queryAuthority, admissionAuthorityWitness, contentFreeWitness,
        exactRegistrationWitness, publicationWitness
        >>

Next ==
    \/ RevokeAdmission
    \/ EndGeneration
    \/ \E image \in Images : Project(image)
    \/ Publish
    \/ AuthorizeQuery
    \/ RevokeQuery
    \/ \E image \in Images : ValidateQuery(image)

Spec == Init /\ [][Next]_vars

ProjectionRequiresCurrentAdmission == admissionAuthorityWitness
ProjectedFactsCarryNoAuthority == contentFreeWitness
ProjectedRegistrationIsExact == exactRegistrationWitness
PublicationCarriesExactFacts == publicationWitness
QueryValidationRequiresCurrentAuthority == queryAuthorityWitness
ValidatedImageMatchesProjection == queryMatchWitness

OnlyManagedAssembliesProject ==
    projectionState = "Projected"
        => projectionImage \in ManagedAssemblies

NonAssembliesHaveNoAssemblyFacts ==
    projectionState \in {"NotAssembly", "Rejected"}
        => /\ projectionGeneration = NoValue
           /\ projectionRegistration = NoValue
           /\ projectionIdentity = NoValue
           /\ projectionMvid = NoValue
           /\ ~projectionCarriesAuthority

NonAssemblyAndRejectionKindsAreTyped ==
    /\ (projectionImage = "Native"
        => /\ projectionState = "NotAssembly"
           /\ projectionReason = "NativeImage")
    /\ (projectionImage = "Module"
        => /\ projectionState = "NotAssembly"
           /\ projectionReason = "ManagedModule")
    /\ (projectionImage = "Malformed"
        => /\ projectionState = "Rejected"
           /\ projectionReason = "MalformedMetadata")
    /\ (projectionImage = "EmptyMvid"
        => /\ projectionState = "Rejected"
           /\ projectionReason = "EmptyModuleVersionId")

PublishedProjectionIsContentFree ==
    published => ~projectionCarriesAuthority

QueryStartsAfterPublication ==
    queryState # "None" => published

QueryOutcomesAreTyped ==
    /\ (queryState = "None" => queryReason = "None")
    /\ (queryState = "Validated" => queryReason = "None")
    /\ (queryState = "NotAssembly"
        => queryReason \in {"NativeImage", "ManagedModule"})
    /\ (queryState = "Rejected"
        => queryReason
            \in {"MalformedMetadata", "EmptyModuleVersionId",
                "GenerationMismatch", "ArtifactIdentityMismatch",
                "AssemblyIdentityMismatch", "ModuleVersionIdMismatch"})

QueryMismatchReasonsAreExact ==
    /\ (queryImage = "B"
        => /\ queryState = "Rejected"
           /\ queryReason = "ModuleVersionIdMismatch")
    /\ (queryImage = "C"
        => /\ queryState = "Rejected"
           /\ queryReason = "AssemblyIdentityMismatch")
    /\ (queryImage = "D"
        => /\ queryState = "Rejected"
           /\ queryReason = "GenerationMismatch")
    /\ (queryImage = "E"
        => /\ queryState = "Rejected"
           /\ queryReason = "ArtifactIdentityMismatch")

MatchingQueryRoundTripIsUnreachable == ~matchingRoundTripReached
MismatchRejectionIsUnreachable == ~mismatchRejectionReached

=============================================================================
