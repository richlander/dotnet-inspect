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
(* Images A-D are managed assemblies. B keeps A's identity but changes the  *)
(* MVID; C keeps A's MVID but changes identity; D keeps both but belongs to  *)
(* another artifact generation and registration. The remaining images      *)
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
    AcceptIdentityMismatch,
    AcceptMvidMismatch,
    AcceptGenerationMismatch,
    AllowRevokedQuery

ASSUME
    /\ AllowStaleAdmission \in BOOLEAN
    /\ LeakContentAuthority \in BOOLEAN
    /\ DropArtifactRegistration \in BOOLEAN
    /\ AcceptIdentityMismatch \in BOOLEAN
    /\ AcceptMvidMismatch \in BOOLEAN
    /\ AcceptGenerationMismatch \in BOOLEAN
    /\ AllowRevokedQuery \in BOOLEAN

NoImage == "NoImage"
NoValue == "NoValue"

ManagedAssemblies == {"A", "B", "C", "D"}
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
    CASE image \in {"A", "B", "D", "EmptyMvid"} -> "Identity1"
      [] image = "C" -> "Identity2"
      [] OTHER -> NoValue

ImageMvid(image) ==
    CASE image \in {"A", "C", "D"} -> "Mvid1"
      [] image = "B" -> "Mvid2"
      [] OTHER -> NoValue

ImageGeneration(image) ==
    IF image = "D" THEN "Generation2" ELSE "Generation1"

ImageRegistration(image) ==
    IF image = "D" THEN "ArtifactRegistration2"
    ELSE "ArtifactRegistration1"

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
QueryStates == {"None", "Validated", "Rejected"}

ExactQueryMatch(image) ==
    /\ image \in ManagedAssemblies
    /\ ImageGeneration(image) = projectionGeneration
    /\ ImageRegistration(image) = projectionRegistration
    /\ ImageIdentity(image) = projectionIdentity
    /\ ImageMvid(image) = projectionMvid

AcceptedQueryMatch(image) ==
    /\ image \in ManagedAssemblies
    /\ (AcceptGenerationMismatch
        \/ ImageGeneration(image) = projectionGeneration)
    /\ (AcceptGenerationMismatch
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
        \in {"ArtifactRegistration1", "ArtifactRegistration2", NoValue}
    /\ projectionIdentity \in {"Identity1", "Identity2", NoValue}
    /\ projectionMvid \in {"Mvid1", "Mvid2", NoValue}
    /\ projectionCarriesAuthority \in BOOLEAN
    /\ published \in BOOLEAN
    /\ queryAuthority \in {"None", "Current", "Revoked"}
    /\ queryState \in QueryStates
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
        queryImage, admissionAuthorityWitness, contentFreeWitness,
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
        projectionCarriesAuthority, published, queryState, queryImage,
        admissionAuthorityWitness, contentFreeWitness,
        exactRegistrationWitness, publicationWitness,
        queryAuthorityWitness, queryMatchWitness,
        matchingRoundTripReached, mismatchRejectionReached
        >>

Project(image) ==
    /\ image \in Images
    /\ image # "D"
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
        admissionAuthority, published, queryAuthority, queryState, queryImage,
        publicationWitness, queryAuthorityWitness, queryMatchWitness,
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
        projectionCarriesAuthority, queryAuthority, queryState, queryImage,
        admissionAuthorityWitness, contentFreeWitness,
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
        queryImage, admissionAuthorityWitness, contentFreeWitness,
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
        queryImage, admissionAuthorityWitness, contentFreeWitness,
        exactRegistrationWitness, publicationWitness,
        queryAuthorityWitness, queryMatchWitness,
        matchingRoundTripReached, mismatchRejectionReached
        >>

ValidateQuery(image) ==
    LET accepted == AcceptedQueryMatch(image)
        exact == ExactQueryMatch(image)
    IN
    /\ image \in Images
    /\ published
    /\ queryState = "None"
    /\ IF AllowRevokedQuery
        THEN queryAuthority \in {"Current", "Revoked"}
        ELSE queryAuthority = "Current"
    /\ queryImage' = image
    /\ queryState' = IF accepted THEN "Validated" ELSE "Rejected"
    /\ queryAuthorityWitness' =
        (queryAuthorityWitness /\ queryAuthority = "Current")
    /\ queryMatchWitness' =
        (queryMatchWitness /\ (~accepted \/ exact))
    /\ matchingRoundTripReached' =
        (matchingRoundTripReached \/ (accepted /\ exact))
    /\ mismatchRejectionReached' =
        (mismatchRejectionReached \/ (~accepted /\ ~exact))
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

MatchingQueryRoundTripIsUnreachable == ~matchingRoundTripReached
MismatchRejectionIsUnreachable == ~mismatchRejectionReached

=============================================================================
