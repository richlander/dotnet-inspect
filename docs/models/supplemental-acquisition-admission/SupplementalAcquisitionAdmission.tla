------------------- MODULE SupplementalAcquisitionAdmission -------------------
(***************************************************************************)
(* Focused design model for supplemental acquisition by ArtifactSetSession. *)
(*                                                                         *)
(* The model begins with an exact required-artifact count and retained-byte *)
(* charge. The first supplemental request permanently closes required       *)
(* admission, then an abstract checkpoint either validates those required   *)
(* snapshots or records their existing failure. A successful checkpoint     *)
(* lets one supplemental operation at a time receive all remaining session  *)
(* capacity. Empty batches clean up without contributing artifacts or roles; *)
(* nonempty batches commit only after complete materialization; failure,     *)
(* overrun, cancellation, or disposal cannot shorten a batch into success.  *)
(*                                                                         *)
(* Content reads, stream interruption, and backing-resource release are      *)
(* owned by ArtifactGenerationAccess.tla. Multi-demand single-flight,        *)
(* workspace-wide plan reservation, and dependent-group quiescence are       *)
(* owned by ArtifactSessionAdmission.tla.                                   *)
(***************************************************************************)
EXTENDS Naturals, FiniteSets, TLC

CONSTANTS
  Supplementals,
  SmallSupplemental,
  LargeSupplemental,
  RequiredCount,
  RequiredBytes,
  MaxArtifacts,
  MaxArtifactBytes,
  MaxRetainedBytes,
  SmallResultCount,
  SmallResultBytes,
  SmallResultLargestArtifactBytes,
  LargeResultCount,
  LargeResultBytes,
  LargeResultLargestArtifactBytes,
  EnforceRequiredPhaseGuard,
  EnforceCheckpointGuard,
  EnforceCountGuard,
  EnforceRetainedBytesGuard,
  EnforceArtifactBytesGuard,
  EnforceLateAcceptanceGuard,
  EnforceCleanupBeforeRelease,
  EnforceFailureVisibility,
  EnforceEmptyNoOp

ASSUME Supplementals # {}
ASSUME Supplementals = {SmallSupplemental, LargeSupplemental}
ASSUME SmallSupplemental # LargeSupplemental
ASSUME RequiredCount \in Nat
ASSUME RequiredBytes \in Nat
ASSUME MaxArtifacts \in Nat \ {0}
ASSUME MaxArtifactBytes \in Nat \ {0}
ASSUME MaxRetainedBytes \in Nat \ {0}
ASSUME RequiredCount <= MaxArtifacts
ASSUME RequiredBytes <= MaxRetainedBytes
ASSUME RequiredBytes <= RequiredCount * MaxArtifactBytes
ASSUME SmallResultCount \in Nat \ {0}
ASSUME SmallResultBytes \in Nat \ {0}
ASSUME SmallResultLargestArtifactBytes \in Nat \ {0}
ASSUME SmallResultLargestArtifactBytes <= SmallResultBytes
ASSUME LargeResultCount \in Nat \ {0}
ASSUME LargeResultBytes \in Nat \ {0}
ASSUME LargeResultLargestArtifactBytes \in Nat \ {0}
ASSUME LargeResultLargestArtifactBytes <= LargeResultBytes
ASSUME EnforceRequiredPhaseGuard \in BOOLEAN
ASSUME EnforceCheckpointGuard \in BOOLEAN
ASSUME EnforceCountGuard \in BOOLEAN
ASSUME EnforceRetainedBytesGuard \in BOOLEAN
ASSUME EnforceArtifactBytesGuard \in BOOLEAN
ASSUME EnforceLateAcceptanceGuard \in BOOLEAN
ASSUME EnforceCleanupBeforeRelease \in BOOLEAN
ASSUME EnforceFailureVisibility \in BOOLEAN
ASSUME EnforceEmptyNoOp \in BOOLEAN

NoSupplemental == "none"

ResultCount(s) ==
  IF s = SmallSupplemental THEN SmallResultCount ELSE LargeResultCount

ResultBytes(s) ==
  IF s = SmallSupplemental THEN SmallResultBytes ELSE LargeResultBytes

ResultLargestArtifactBytes(s) ==
  IF s = SmallSupplemental
  THEN SmallResultLargestArtifactBytes
  ELSE LargeResultLargestArtifactBytes

Min2(left, right) == IF left <= right THEN left ELSE right

SessionStates ==
  {"Required", "Supplemental", "Sealing", "Published", "Rejected", "Closed"}
CheckpointStates == {"NotRun", "InProgress", "Succeeded", "Failed"}
OperationStates ==
  {"Pending", "Requested", "Acquiring", "Materializing", "CleaningEmpty",
   "CleaningFailure", "Accepted", "Empty", "Failed", "CapacityRejected"}
LeaseStates == {"None", "Returned", "Retained", "Disposed", "CleanupFailed"}

RECURSIVE ResultCountSum(_)
ResultCountSum(operations) ==
  IF operations = {}
  THEN 0
  ELSE
    LET s == CHOOSE candidate \in operations : TRUE
    IN ResultCount(s) + ResultCountSum(operations \ {s})

RECURSIVE ResultBytesSum(_)
ResultBytesSum(operations) ==
  IF operations = {}
  THEN 0
  ELSE
    LET s == CHOOSE candidate \in operations : TRUE
    IN ResultBytes(s) + ResultBytesSum(operations \ {s})

VARIABLES
  sessionState,
  checkpointState,
  active,
  operationState,
  leaseState,
  accepted,
  rolesApplied,
  failures,
  cleanupFailures,
  grantCount,
  grantArtifactBytes,
  grantRetainedBytes,
  requiredAddAttempted,
  requiredAcceptedAfterSupplemental,
  adapterFailures,
  checkpointFailureObserved,
  capacityRejectionObserved,
  emptyObserved,
  acceptanceObserved,
  overrunObserved,
  lateOutcomeObserved,
  requiredRejectionObserved,
  checkpointGuardWitness,
  capacityGuardWitness,
  acceptanceGuardWitness,
  publicationGuardWitness,
  cleanupReleaseWitness

vars ==
  << sessionState, checkpointState, active, operationState, leaseState,
     accepted, rolesApplied, failures, cleanupFailures, grantCount,
     grantArtifactBytes, grantRetainedBytes, requiredAddAttempted,
     requiredAcceptedAfterSupplemental, adapterFailures,
     checkpointFailureObserved, capacityRejectionObserved, emptyObserved,
     acceptanceObserved, overrunObserved, lateOutcomeObserved,
     requiredRejectionObserved, checkpointGuardWitness,
     capacityGuardWitness, acceptanceGuardWitness, publicationGuardWitness,
     cleanupReleaseWitness >>

CommittedCount == RequiredCount + ResultCountSum(accepted)
CommittedBytes == RequiredBytes + ResultBytesSum(accepted)
RemainingCount == MaxArtifacts - CommittedCount
RemainingBytes == MaxRetainedBytes - CommittedBytes
RemainingArtifactBytes == Min2(MaxArtifactBytes, RemainingBytes)

WithinGrant(s) ==
  /\ ResultCount(s) <= grantCount
  /\ ResultBytes(s) <= grantRetainedBytes
  /\ ResultLargestArtifactBytes(s) <= grantArtifactBytes

CapacityPermits(s) ==
  /\ (ResultCount(s) <= grantCount \/ ~EnforceCountGuard)
  /\ (ResultBytes(s) <= grantRetainedBytes
        \/ ~EnforceRetainedBytesGuard)
  /\ (ResultLargestArtifactBytes(s) <= grantArtifactBytes
        \/ ~EnforceArtifactBytesGuard)

ActiveStates ==
  {"Acquiring", "Materializing", "CleaningEmpty", "CleaningFailure"}
TerminalOperationStates ==
  {"Accepted", "Empty", "Failed", "CapacityRejected"}
TerminalLeaseStates == {"Disposed", "CleanupFailed"}

TypeOK ==
  /\ sessionState \in SessionStates
  /\ checkpointState \in CheckpointStates
  /\ active \in Supplementals \cup {NoSupplemental}
  /\ operationState \in [Supplementals -> OperationStates]
  /\ leaseState \in [Supplementals -> LeaseStates]
  /\ accepted \subseteq Supplementals
  /\ rolesApplied \subseteq Supplementals
  /\ failures \subseteq Supplementals
  /\ cleanupFailures \subseteq Supplementals
  /\ grantCount \in Nat
  /\ grantArtifactBytes \in Nat
  /\ grantRetainedBytes \in Nat
  /\ requiredAddAttempted \in BOOLEAN
  /\ requiredAcceptedAfterSupplemental \in BOOLEAN
  /\ adapterFailures \subseteq Supplementals
  /\ checkpointFailureObserved \in BOOLEAN
  /\ capacityRejectionObserved \in BOOLEAN
  /\ emptyObserved \in BOOLEAN
  /\ acceptanceObserved \in BOOLEAN
  /\ overrunObserved \in BOOLEAN
  /\ lateOutcomeObserved \in BOOLEAN
  /\ requiredRejectionObserved \in BOOLEAN
  /\ checkpointGuardWitness \in BOOLEAN
  /\ capacityGuardWitness \in BOOLEAN
  /\ acceptanceGuardWitness \in BOOLEAN
  /\ publicationGuardWitness \in BOOLEAN
  /\ cleanupReleaseWitness \in BOOLEAN

Init ==
  /\ sessionState = "Required"
  /\ checkpointState = "NotRun"
  /\ active = NoSupplemental
  /\ operationState = [s \in Supplementals |-> "Pending"]
  /\ leaseState = [s \in Supplementals |-> "None"]
  /\ accepted = {}
  /\ rolesApplied = {}
  /\ failures = {}
  /\ cleanupFailures = {}
  /\ grantCount = 0
  /\ grantArtifactBytes = 0
  /\ grantRetainedBytes = 0
  /\ requiredAddAttempted = FALSE
  /\ requiredAcceptedAfterSupplemental = FALSE
  /\ adapterFailures = {}
  /\ checkpointFailureObserved = FALSE
  /\ capacityRejectionObserved = FALSE
  /\ emptyObserved = FALSE
  /\ acceptanceObserved = FALSE
  /\ overrunObserved = FALSE
  /\ lateOutcomeObserved = FALSE
  /\ requiredRejectionObserved = FALSE
  /\ checkpointGuardWitness = TRUE
  /\ capacityGuardWitness = TRUE
  /\ acceptanceGuardWitness = TRUE
  /\ publicationGuardWitness = TRUE
  /\ cleanupReleaseWitness = TRUE

BeginSupplementalPhase ==
  /\ sessionState = "Required"
  /\ sessionState' = "Supplemental"
  /\ checkpointState' = "InProgress"
  /\ UNCHANGED
       << active, operationState, leaseState, accepted, rolesApplied,
          failures, cleanupFailures, grantCount, grantArtifactBytes,
          grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

CheckpointSucceeds ==
  /\ sessionState = "Supplemental"
  /\ checkpointState = "InProgress"
  /\ checkpointState' = "Succeeded"
  /\ UNCHANGED
       << sessionState, active, operationState, leaseState, accepted,
          rolesApplied, failures, cleanupFailures, grantCount,
          grantArtifactBytes, grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

CheckpointFails ==
  /\ sessionState = "Supplemental"
  /\ checkpointState = "InProgress"
  /\ checkpointState' = "Failed"
  /\ checkpointFailureObserved' = TRUE
  /\ UNCHANGED
       << sessionState, active, operationState, leaseState, accepted,
          rolesApplied, failures, cleanupFailures, grantCount,
          grantArtifactBytes, grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          capacityRejectionObserved, emptyObserved, acceptanceObserved,
          overrunObserved, lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

AttemptRequiredAdd ==
  /\ sessionState # "Required"
  /\ requiredAddAttempted = FALSE
  /\ requiredAddAttempted' = TRUE
  /\ requiredAcceptedAfterSupplemental' = ~EnforceRequiredPhaseGuard
  /\ requiredRejectionObserved' = EnforceRequiredPhaseGuard
  /\ UNCHANGED
       << sessionState, checkpointState, active, operationState, leaseState,
          accepted, rolesApplied, failures, cleanupFailures, grantCount,
          grantArtifactBytes, grantRetainedBytes, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, checkpointGuardWitness,
          capacityGuardWitness, acceptanceGuardWitness,
          publicationGuardWitness, cleanupReleaseWitness >>

RequestSupplemental(s) ==
  /\ sessionState = "Supplemental"
  /\ (checkpointState = "Succeeded" \/ ~EnforceCheckpointGuard)
  /\ failures = {}
  /\ operationState[s] = "Pending"
  /\ active = NoSupplemental
  /\ \A other \in Supplementals :
       operationState[other] # "Requested"
  /\ operationState' = [operationState EXCEPT ![s] = "Requested"]
  /\ UNCHANGED
       << sessionState, checkpointState, active, leaseState, accepted,
          rolesApplied, failures, cleanupFailures, grantCount,
          grantArtifactBytes, grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

StartSupplemental(s) ==
  /\ sessionState = "Supplemental"
  /\ (checkpointState = "Succeeded" \/ ~EnforceCheckpointGuard)
  /\ operationState[s] = "Requested"
  /\ active = NoSupplemental
  /\ RemainingCount > 0
  /\ RemainingBytes > 0
  /\ active' = s
  /\ operationState' = [operationState EXCEPT ![s] = "Acquiring"]
  /\ grantCount' = RemainingCount
  /\ grantArtifactBytes' = RemainingArtifactBytes
  /\ grantRetainedBytes' = RemainingBytes
  /\ checkpointGuardWitness' =
       (checkpointGuardWitness /\ (checkpointState = "Succeeded"))
  /\ capacityGuardWitness' =
       (capacityGuardWitness
          /\ RemainingCount > 0
          /\ RemainingBytes > 0
          /\ RemainingArtifactBytes > 0)
  /\ UNCHANGED
       << sessionState, checkpointState, leaseState, accepted, rolesApplied,
          failures, cleanupFailures, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

RejectForCapacity(s) ==
  /\ sessionState = "Supplemental"
  /\ checkpointState = "Succeeded"
  /\ operationState[s] = "Requested"
  /\ active = NoSupplemental
  /\ RemainingCount = 0 \/ RemainingBytes = 0
  /\ operationState' =
       [operationState EXCEPT ![s] = "CapacityRejected"]
  /\ failures' = failures \cup {s}
  /\ capacityRejectionObserved' = TRUE
  /\ UNCHANGED
       << sessionState, checkpointState, active, leaseState, accepted,
          rolesApplied, cleanupFailures, grantCount, grantArtifactBytes,
          grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, emptyObserved, acceptanceObserved,
          overrunObserved, lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

AdapterReturnsEmpty(s) ==
  /\ active = s
  /\ operationState[s] = "Acquiring"
  /\ operationState' =
       [operationState EXCEPT
          ![s] =
            IF sessionState = "Supplemental"
            THEN "CleaningEmpty"
            ELSE "CleaningFailure"]
  /\ leaseState' = [leaseState EXCEPT ![s] = "Returned"]
  /\ lateOutcomeObserved' =
       (lateOutcomeObserved \/ (sessionState # "Supplemental"))
  /\ UNCHANGED
       << sessionState, checkpointState, active, accepted, rolesApplied,
          failures, cleanupFailures, grantCount, grantArtifactBytes,
          grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          requiredRejectionObserved, checkpointGuardWitness,
          capacityGuardWitness, acceptanceGuardWitness,
          publicationGuardWitness, cleanupReleaseWitness >>

AdapterReturnsFailure(s) ==
  /\ active = s
  /\ operationState[s] = "Acquiring"
  /\ operationState' =
       [operationState EXCEPT
          ![s] =
            IF EnforceFailureVisibility THEN "Failed" ELSE "Empty"]
  /\ active' = NoSupplemental
  /\ failures' =
       IF EnforceFailureVisibility THEN failures \cup {s} ELSE failures
  /\ adapterFailures' = adapterFailures \cup {s}
  /\ grantCount' = 0
  /\ grantArtifactBytes' = 0
  /\ grantRetainedBytes' = 0
  /\ lateOutcomeObserved' =
       (lateOutcomeObserved \/ (sessionState # "Supplemental"))
  /\ UNCHANGED
       << sessionState, checkpointState, leaseState, accepted, rolesApplied,
          cleanupFailures, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, checkpointFailureObserved,
          capacityRejectionObserved, emptyObserved, acceptanceObserved,
          overrunObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

AdapterReturnsBatch(s) ==
  /\ active = s
  /\ operationState[s] = "Acquiring"
  /\ operationState' =
       [operationState EXCEPT
          ![s] =
            IF /\ sessionState = "Supplemental"
               /\ CapacityPermits(s)
            THEN "Materializing"
            ELSE "CleaningFailure"]
  /\ leaseState' = [leaseState EXCEPT ![s] = "Returned"]
  /\ overrunObserved' = (overrunObserved \/ ~WithinGrant(s))
  /\ lateOutcomeObserved' =
       (lateOutcomeObserved \/ (sessionState # "Supplemental"))
  /\ UNCHANGED
       << sessionState, checkpointState, active, accepted, rolesApplied,
          failures, cleanupFailures, grantCount, grantArtifactBytes,
          grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

MaterializationSucceeds(s) ==
  /\ active = s
  /\ operationState[s] = "Materializing"
  /\ (sessionState = "Supplemental" \/ ~EnforceLateAcceptanceGuard)
  /\ operationState' = [operationState EXCEPT ![s] = "Accepted"]
  /\ leaseState' = [leaseState EXCEPT ![s] = "Retained"]
  /\ accepted' = accepted \cup {s}
  /\ rolesApplied' = rolesApplied \cup {s}
  /\ active' = NoSupplemental
  /\ grantCount' = 0
  /\ grantArtifactBytes' = 0
  /\ grantRetainedBytes' = 0
  /\ acceptanceObserved' = TRUE
  /\ acceptanceGuardWitness' =
       (acceptanceGuardWitness
          /\ sessionState = "Supplemental"
          /\ checkpointState = "Succeeded"
          /\ WithinGrant(s))
  /\ UNCHANGED
       << sessionState, checkpointState, failures, cleanupFailures,
          requiredAddAttempted, requiredAcceptedAfterSupplemental,
          adapterFailures, checkpointFailureObserved,
          capacityRejectionObserved, emptyObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          publicationGuardWitness, cleanupReleaseWitness >>

MaterializationFails(s) ==
  /\ active = s
  /\ operationState[s] = "Materializing"
  /\ operationState' =
       [operationState EXCEPT ![s] = "CleaningFailure"]
  /\ UNCHANGED
       << sessionState, checkpointState, active, leaseState, accepted,
          rolesApplied, failures, cleanupFailures, grantCount,
          grantArtifactBytes, grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

CleanupSucceeds(s) ==
  /\ active = s
  /\ operationState[s] \in {"CleaningEmpty", "CleaningFailure"}
  /\ leaseState[s] = "Returned"
  /\ operationState' =
       [operationState EXCEPT
          ![s] =
            IF operationState[s] = "CleaningEmpty"
            THEN "Empty"
            ELSE "Failed"]
  /\ leaseState' = [leaseState EXCEPT ![s] = "Disposed"]
  /\ failures' =
       IF operationState[s] = "CleaningFailure"
       THEN failures \cup {s}
       ELSE failures
  /\ emptyObserved' =
       (emptyObserved \/ (operationState[s] = "CleaningEmpty"))
  /\ active' = NoSupplemental
  /\ grantCount' = 0
  /\ grantArtifactBytes' = 0
  /\ grantRetainedBytes' = 0
  /\ cleanupReleaseWitness' =
       (cleanupReleaseWitness
          /\ leaseState[s] = "Returned"
          /\ active = s
          /\ grantCount > 0
          /\ grantArtifactBytes > 0
          /\ grantRetainedBytes > 0)
  /\ UNCHANGED
       << sessionState, checkpointState, accepted, rolesApplied,
          cleanupFailures, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          acceptanceObserved, overrunObserved, lateOutcomeObserved,
          requiredRejectionObserved, checkpointGuardWitness,
          capacityGuardWitness, acceptanceGuardWitness,
          publicationGuardWitness >>

CleanupFails(s) ==
  /\ active = s
  /\ operationState[s] \in {"CleaningEmpty", "CleaningFailure"}
  /\ leaseState[s] = "Returned"
  /\ operationState' =
       [operationState EXCEPT
          ![s] =
            IF operationState[s] = "CleaningEmpty"
            THEN "Empty"
            ELSE "Failed"]
  /\ leaseState' = [leaseState EXCEPT ![s] = "CleanupFailed"]
  /\ failures' =
       IF operationState[s] = "CleaningFailure"
       THEN failures \cup {s}
       ELSE failures
  /\ cleanupFailures' = cleanupFailures \cup {s}
  /\ emptyObserved' =
       (emptyObserved \/ (operationState[s] = "CleaningEmpty"))
  /\ active' = NoSupplemental
  /\ grantCount' = 0
  /\ grantArtifactBytes' = 0
  /\ grantRetainedBytes' = 0
  /\ cleanupReleaseWitness' =
       (cleanupReleaseWitness
          /\ leaseState[s] = "Returned"
          /\ active = s
          /\ grantCount > 0
          /\ grantArtifactBytes > 0
          /\ grantRetainedBytes > 0)
  /\ UNCHANGED
       << sessionState, checkpointState, accepted, rolesApplied,
          requiredAddAttempted, requiredAcceptedAfterSupplemental,
          adapterFailures, checkpointFailureObserved,
          capacityRejectionObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness >>

ReleaseBeforeCleanup(s) ==
  /\ ~EnforceCleanupBeforeRelease
  /\ active = s
  /\ operationState[s] \in {"CleaningEmpty", "CleaningFailure"}
  /\ leaseState[s] = "Returned"
  /\ operationState' =
       [operationState EXCEPT
          ![s] =
            IF operationState[s] = "CleaningEmpty"
            THEN "Empty"
            ELSE "Failed"]
  /\ failures' =
       IF operationState[s] = "CleaningFailure"
       THEN failures \cup {s}
       ELSE failures
  /\ active' = NoSupplemental
  /\ grantCount' = 0
  /\ grantArtifactBytes' = 0
  /\ grantRetainedBytes' = 0
  /\ cleanupReleaseWitness' = FALSE
  /\ UNCHANGED
       << sessionState, checkpointState, leaseState, accepted, rolesApplied,
          cleanupFailures, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness >>

CommitEmptyAsArtifact(s) ==
  /\ ~EnforceEmptyNoOp
  /\ active = s
  /\ operationState[s] = "CleaningEmpty"
  /\ leaseState[s] = "Returned"
  /\ operationState' = [operationState EXCEPT ![s] = "Empty"]
  /\ leaseState' = [leaseState EXCEPT ![s] = "Disposed"]
  /\ accepted' = accepted \cup {s}
  /\ rolesApplied' = rolesApplied \cup {s}
  /\ emptyObserved' = TRUE
  /\ active' = NoSupplemental
  /\ grantCount' = 0
  /\ grantArtifactBytes' = 0
  /\ grantRetainedBytes' = 0
  /\ cleanupReleaseWitness' =
       (cleanupReleaseWitness
          /\ leaseState[s] = "Returned"
          /\ active = s
          /\ grantCount > 0
          /\ grantArtifactBytes > 0
          /\ grantRetainedBytes > 0)
  /\ UNCHANGED
       << sessionState, checkpointState, failures, cleanupFailures,
          requiredAddAttempted, requiredAcceptedAfterSupplemental,
          adapterFailures, checkpointFailureObserved,
          capacityRejectionObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness >>

BeginSeal ==
  /\ sessionState = "Supplemental"
  /\ checkpointState \in {"Succeeded", "Failed"}
  /\ active = NoSupplemental
  /\ \A s \in Supplementals : operationState[s] # "Requested"
  /\ sessionState' = "Sealing"
  /\ UNCHANGED
       << checkpointState, active, operationState, leaseState, accepted,
          rolesApplied, failures, cleanupFailures, grantCount,
          grantArtifactBytes, grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

Publish ==
  /\ sessionState = "Sealing"
  /\ checkpointState = "Succeeded"
  /\ failures = {}
  /\ CommittedCount > 0
  /\ sessionState' = "Published"
  /\ publicationGuardWitness' =
       (publicationGuardWitness
          /\ checkpointState = "Succeeded"
          /\ failures = {}
          /\ active = NoSupplemental
          /\ CommittedCount > 0
          /\ \A s \in Supplementals : leaseState[s] # "Returned")
  /\ UNCHANGED
       << checkpointState, active, operationState, leaseState, accepted,
          rolesApplied, failures, cleanupFailures, grantCount,
          grantArtifactBytes, grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, cleanupReleaseWitness >>

RejectSeal ==
  /\ sessionState = "Sealing"
  /\ checkpointState = "Failed" \/ failures # {} \/ CommittedCount = 0
  /\ sessionState' = "Rejected"
  /\ UNCHANGED
       << checkpointState, active, operationState, leaseState, accepted,
          rolesApplied, failures, cleanupFailures, grantCount,
          grantArtifactBytes, grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

CloseSession ==
  /\ sessionState \in
       {"Required", "Supplemental", "Sealing", "Published"}
  /\ \A s \in Supplementals : operationState[s] # "Requested"
  /\ sessionState' = "Closed"
  /\ operationState' =
       IF active = NoSupplemental
       THEN operationState
       ELSE
         [operationState EXCEPT
            ![active] =
              IF /\ operationState[active] = "Materializing"
                 /\ EnforceLateAcceptanceGuard
              THEN "CleaningFailure"
              ELSE IF operationState[active] = "CleaningEmpty"
              THEN "CleaningFailure"
              ELSE @]
  /\ UNCHANGED
       << checkpointState, active, leaseState, accepted, rolesApplied,
          failures, cleanupFailures, grantCount, grantArtifactBytes,
          grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

DisposeRetainedSucceeds(s) ==
  /\ sessionState \in {"Rejected", "Closed"}
  /\ leaseState[s] = "Retained"
  /\ leaseState' = [leaseState EXCEPT ![s] = "Disposed"]
  /\ UNCHANGED
       << sessionState, checkpointState, active, operationState, accepted,
          rolesApplied, failures, cleanupFailures, grantCount,
          grantArtifactBytes, grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

DisposeRetainedFails(s) ==
  /\ sessionState \in {"Rejected", "Closed"}
  /\ leaseState[s] = "Retained"
  /\ leaseState' = [leaseState EXCEPT ![s] = "CleanupFailed"]
  /\ cleanupFailures' = cleanupFailures \cup {s}
  /\ UNCHANGED
       << sessionState, checkpointState, active, operationState, accepted,
          rolesApplied, failures, grantCount, grantArtifactBytes,
          grantRetainedBytes, requiredAddAttempted,
          requiredAcceptedAfterSupplemental, adapterFailures,
          checkpointFailureObserved, capacityRejectionObserved,
          emptyObserved, acceptanceObserved, overrunObserved,
          lateOutcomeObserved, requiredRejectionObserved,
          checkpointGuardWitness, capacityGuardWitness,
          acceptanceGuardWitness, publicationGuardWitness,
          cleanupReleaseWitness >>

AdapterCompletes(s) ==
  AdapterReturnsEmpty(s) \/ AdapterReturnsFailure(s) \/ AdapterReturnsBatch(s)

ResolveRequest(s) == StartSupplemental(s) \/ RejectForCapacity(s)

MaterializationSettles(s) ==
  MaterializationSucceeds(s) \/ MaterializationFails(s)

CleanupSettles(s) ==
  CleanupSucceeds(s)
    \/ CleanupFails(s)
    \/ ReleaseBeforeCleanup(s)
    \/ CommitEmptyAsArtifact(s)

RetainedLeaseSettles(s) ==
  DisposeRetainedSucceeds(s) \/ DisposeRetainedFails(s)

SealSettles == Publish \/ RejectSeal

Next ==
  \/ BeginSupplementalPhase
  \/ CheckpointSucceeds
  \/ CheckpointFails
  \/ AttemptRequiredAdd
  \/ \E s \in Supplementals : RequestSupplemental(s)
  \/ \E s \in Supplementals : ResolveRequest(s)
  \/ \E s \in Supplementals : AdapterCompletes(s)
  \/ \E s \in Supplementals : MaterializationSettles(s)
  \/ \E s \in Supplementals : CleanupSettles(s)
  \/ BeginSeal
  \/ SealSettles
  \/ CloseSession
  \/ \E s \in Supplementals : RetainedLeaseSettles(s)

Spec ==
  /\ Init
  /\ [][Next]_vars
  /\ WF_vars(CheckpointSucceeds \/ CheckpointFails)
  /\ \A s \in Supplementals : WF_vars(ResolveRequest(s))
  /\ \A s \in Supplementals : WF_vars(AdapterCompletes(s))
  /\ \A s \in Supplementals : WF_vars(MaterializationSettles(s))
  /\ \A s \in Supplementals : WF_vars(CleanupSettles(s))
  /\ WF_vars(SealSettles)
  /\ \A s \in Supplementals : WF_vars(RetainedLeaseSettles(s))

(***************************************************************************)
(* Safety properties.                                                      *)
(***************************************************************************)
PhaseCoherence ==
  /\ (sessionState = "Required") => checkpointState = "NotRun"
  /\ (checkpointState = "InProgress")
       => sessionState \in {"Supplemental", "Closed"}
  /\ (sessionState \in {"Sealing", "Published", "Rejected"})
       => checkpointState \in {"Succeeded", "Failed"}

OneActiveGrant ==
  /\ (active = NoSupplemental)
       <=> /\ grantCount = 0
           /\ grantArtifactBytes = 0
           /\ grantRetainedBytes = 0
  /\ active # NoSupplemental
       => /\ operationState[active] \in ActiveStates
          /\ grantCount > 0
          /\ grantArtifactBytes > 0
          /\ grantRetainedBytes > 0

CapacityBounded ==
  /\ CommittedCount <= MaxArtifacts
  /\ CommittedBytes <= MaxRetainedBytes
  /\ active # NoSupplemental
       => /\ CommittedCount + grantCount <= MaxArtifacts
          /\ CommittedBytes + grantRetainedBytes <= MaxRetainedBytes
          /\ grantArtifactBytes <= MaxArtifactBytes
          /\ grantArtifactBytes <= grantRetainedBytes

AcceptedArtifactsFitPerArtifactLimit ==
  \A s \in accepted :
    ResultLargestArtifactBytes(s) <= MaxArtifactBytes

ReturnedLeaseRetainsGrant ==
  \A s \in Supplementals :
    leaseState[s] = "Returned"
      => /\ active = s
         /\ grantCount > 0
         /\ grantArtifactBytes > 0
         /\ grantRetainedBytes > 0

BatchCommitIsAtomic ==
  /\ accepted = {s \in Supplementals : operationState[s] = "Accepted"}
  /\ rolesApplied = accepted

EmptyBatchIsNoOp ==
  \A s \in Supplementals :
    operationState[s] = "Empty"
      => /\ s \notin accepted
         /\ s \notin rolesApplied
         /\ leaseState[s] \in TerminalLeaseStates

FailureIsVisible ==
  /\ adapterFailures \subseteq failures
  /\ \A s \in Supplementals :
       operationState[s] \in {"Failed", "CapacityRejected"} => s \in failures

LeaseOwnershipCoherent ==
  \A s \in Supplementals :
    /\ operationState[s] = "Pending" => leaseState[s] = "None"
    /\ operationState[s] = "Requested" => leaseState[s] = "None"
    /\ operationState[s] = "Acquiring" => leaseState[s] = "None"
    /\ operationState[s] \in
         {"Materializing", "CleaningEmpty", "CleaningFailure"}
         => leaseState[s] = "Returned"
    /\ operationState[s] = "Accepted"
         => leaseState[s] \in {"Retained", "Disposed", "CleanupFailed"}
    /\ operationState[s] = "Empty"
         => leaseState[s] \in TerminalLeaseStates
    /\ operationState[s] = "CapacityRejected"
         => leaseState[s] = "None"

RequiredPhaseStaysClosed == ~requiredAcceptedAfterSupplemental
CheckpointGuardWitnessHolds == checkpointGuardWitness
CapacityGuardWitnessHolds == capacityGuardWitness
AcceptanceGuardWitnessHolds == acceptanceGuardWitness
PublicationGuardWitnessHolds == publicationGuardWitness
CleanupReleaseWitnessHolds == cleanupReleaseWitness

PublishedStateIsCoherent ==
  sessionState = "Published"
    => /\ checkpointState = "Succeeded"
       /\ failures = {}
       /\ active = NoSupplemental
       /\ CommittedCount > 0

(***************************************************************************)
(* Liveness properties.                                                    *)
(***************************************************************************)
EveryStartedOperationEventuallySettles ==
  \A s \in Supplementals :
    operationState[s] \in ActiveStates
      ~> operationState[s] \in TerminalOperationStates

EveryRequestedCallEventuallyResolves ==
  \A s \in Supplementals :
    operationState[s] = "Requested"
      ~> operationState[s] \in
           {"Acquiring", "CapacityRejected"}

EveryReturnedLeaseEventuallyTransfersOrCleans ==
  \A s \in Supplementals :
    leaseState[s] = "Returned"
      ~> leaseState[s] \in
           {"Retained", "Disposed", "CleanupFailed"}

RejectedSessionEventuallyCleansRetainedLeases ==
  \A s \in Supplementals :
    (sessionState = "Rejected" /\ leaseState[s] = "Retained")
      ~> leaseState[s] \in TerminalLeaseStates

ClosedSessionEventuallyCleansRetainedLeases ==
  \A s \in Supplementals :
    (sessionState = "Closed" /\ leaseState[s] = "Retained")
      ~> leaseState[s] \in TerminalLeaseStates

(***************************************************************************)
(* Reachability sentinels. Each is intentionally false after its named     *)
(* scenario executes.                                                      *)
(***************************************************************************)
CheckpointFailureNotReached == ~checkpointFailureObserved
CapacityRejectionNotReached == ~capacityRejectionObserved
CountCapacityRejectionNotReached ==
  ~(capacityRejectionObserved /\ RemainingCount = 0)
ByteCapacityRejectionNotReached ==
  ~(capacityRejectionObserved /\ RemainingBytes = 0)
EmptyBatchNotReached == ~emptyObserved
AcceptanceNotReached == ~acceptanceObserved
OverrunNotReached == ~overrunObserved
LateOutcomeNotReached == ~lateOutcomeObserved
LateDiagnosticNotReached ==
  ~(lateOutcomeObserved /\ adapterFailures # {})
RequiredRejectionNotReached == ~requiredRejectionObserved
EmptyOnlyRejectionNotReached ==
  ~( /\ sessionState = "Rejected"
     /\ RequiredCount = 0
     /\ accepted = {}
     /\ checkpointState = "Succeeded"
     /\ failures = {}
     /\ emptyObserved )
SupplementalOnlyPublicationNotReached ==
  ~(sessionState = "Published" /\ RequiredCount = 0 /\ accepted # {})
=============================================================================
