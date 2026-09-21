------------------ MODULE BrowserLocationIntentArbitration ------------------
EXTENDS Naturals, TLC

\* The Navigation Consumer owns location-intent identity and browser-history
\* publication. Retained-realization cutover is consumed as an irreversible
\* installed-result event; its candidate, receipt, and completion lifecycle are
\* owned by the retained-realization model.

CONSTANTS
    MaxIntents,
    Fault

ASSUME MaxIntents >= 3
ASSUME Fault \in {
    "None",
    "F08LateTraversalCapture",
    "F10F12F13StaleWorkspacePublication",
    "F14FailedTraversalNotRealigned",
    "F15AdmissionBeforeRealignment",
    "F16CompletionMintsIntent",
    "ForeignInstalledAssociationEffect"
}

Intents == 1..MaxIntents
Locations == {"A", "B", "C"}
Sources == {"none", "browser", "nonBrowser"}
Policies == {"none", "browser", "push", "replace"}
Statuses == {"unused", "waiting", "accepted", "settled", "superseded"}
Effects == {"none", "push", "replace", "adopt", "realign"}
TraversalOutcomes == {"exact", "changed", "failed"}
InitialAssociation == [kind |-> "initial", serial |-> 0]
ResultAssociation(i) == [kind |-> "result", serial |-> i]
Associations == {InitialAssociation} \cup {ResultAssociation(i) : i \in Intents}
ForeignAssociation(i) == ResultAssociation(IF i = 1 THEN 2 ELSE 1)

VARIABLES
    nextIntent,
    currentIntent,
    source,
    target,
    resultAssociation,
    policy,
    status,
    commitOwner,
    installedLocation,
    installedIntent,
    installedAssociation,
    selectedLocation,
    selectedAssociation,
    aligned,
    lastEffect,
    lastEffectIntent,
    lastEffectAssociation,
    visibleFailure,
    traversalCaptureWitness,
    currentPublicationWitness,
    publicationAssociationWitness,
    preAdmissionWitness,
    failedRepairAdmissionWitness,
    failedTraversalWitness,
    originatingIntentWitness,
    allowedEffectWitness,
    cutoverYieldWitness,
    postCutoverFailureWitness,
    observedCutoverYield,
    observedFailedTraversalRealignment,
    observedPreAdmissionRealignment,
    observedPreAdmissionRealignmentFailure,
    observedStaleAsyncCompletion,
    observedPostCutoverHistoryFailure

vars ==
    << nextIntent,
       currentIntent,
       source,
       target,
       resultAssociation,
       policy,
       status,
       commitOwner,
       installedLocation,
       installedIntent,
       installedAssociation,
       selectedLocation,
       selectedAssociation,
       aligned,
       lastEffect,
       lastEffectIntent,
       lastEffectAssociation,
       visibleFailure,
       traversalCaptureWitness,
       currentPublicationWitness,
       publicationAssociationWitness,
       preAdmissionWitness,
       failedRepairAdmissionWitness,
       failedTraversalWitness,
       originatingIntentWitness,
       allowedEffectWitness,
       cutoverYieldWitness,
       postCutoverFailureWitness,
       observedCutoverYield,
       observedFailedTraversalRealignment,
       observedPreAdmissionRealignment,
       observedPreAdmissionRealignmentFailure,
       observedStaleAsyncCompletion,
       observedPostCutoverHistoryFailure >>

EffectAllowed(i, effect) ==
    \/ effect = "none"
    \/ effect = "realign"
    \/ /\ source[i] = "browser"
       /\ effect \in {"adopt", "replace"}
    \/ /\ source[i] = "nonBrowser"
       /\ policy[i] = "push"
       /\ effect \in {"push", "replace"}
    \/ /\ source[i] = "nonBrowser"
       /\ policy[i] = "replace"
       /\ effect = "replace"

SupersedeWaitingCurrent ==
    IF currentIntent \in Intents /\ status[currentIntent] = "waiting"
    THEN [status EXCEPT ![currentIntent] = "superseded"]
    ELSE status

Init ==
    /\ nextIntent = 0
    /\ currentIntent = 0
    /\ source = [i \in Intents |-> "none"]
    /\ target = [i \in Intents |-> "A"]
    /\ resultAssociation = [i \in Intents |-> InitialAssociation]
    /\ policy = [i \in Intents |-> "none"]
    /\ status = [i \in Intents |-> "unused"]
    /\ commitOwner = 0
    /\ installedLocation = "A"
    /\ installedIntent = 0
    /\ installedAssociation = InitialAssociation
    /\ selectedLocation = "A"
    /\ selectedAssociation = InitialAssociation
    /\ aligned = TRUE
    /\ lastEffect = "none"
    /\ lastEffectIntent = 0
    /\ lastEffectAssociation = InitialAssociation
    /\ visibleFailure = FALSE
    /\ traversalCaptureWitness = TRUE
    /\ currentPublicationWitness = TRUE
    /\ publicationAssociationWitness = TRUE
    /\ preAdmissionWitness = TRUE
    /\ failedRepairAdmissionWitness = TRUE
    /\ failedTraversalWitness = TRUE
    /\ originatingIntentWitness = TRUE
    /\ allowedEffectWitness = TRUE
    /\ cutoverYieldWitness = TRUE
    /\ postCutoverFailureWitness = TRUE
    /\ observedCutoverYield = FALSE
    /\ observedFailedTraversalRealignment = FALSE
    /\ observedPreAdmissionRealignment = FALSE
    /\ observedPreAdmissionRealignmentFailure = FALSE
    /\ observedStaleAsyncCompletion = FALSE
    /\ observedPostCutoverHistoryFailure = FALSE

BeginNonBrowser(destination, requestedPolicy, repairSucceeds) ==
    /\ destination \in Locations
    /\ requestedPolicy \in {"push", "replace"}
    /\ repairSucceeds \in BOOLEAN
    /\ commitOwner = 0
    /\ nextIntent < MaxIntents
    /\ LET i == nextIntent + 1
           needsRepair == ~aligned
           repair ==
               /\ needsRepair
               /\ Fault # "F15AdmissionBeforeRealignment"
               /\ repairSucceeds
           rejected ==
               /\ needsRepair
               /\ Fault # "F15AdmissionBeforeRealignment"
               /\ ~repairSucceeds
       IN
       /\ nextIntent' = IF rejected THEN nextIntent ELSE i
       /\ currentIntent' = IF rejected THEN currentIntent ELSE i
       /\ source' =
            IF rejected
            THEN source
            ELSE [source EXCEPT ![i] = "nonBrowser"]
       /\ target' =
            IF rejected
            THEN target
            ELSE [target EXCEPT ![i] = destination]
       /\ resultAssociation' =
            IF rejected
            THEN resultAssociation
            ELSE [resultAssociation EXCEPT ![i] = ResultAssociation(i)]
       /\ policy' =
            IF rejected
            THEN policy
            ELSE [policy EXCEPT ![i] = requestedPolicy]
       /\ status' =
            IF rejected
            THEN status
            ELSE [SupersedeWaitingCurrent EXCEPT ![i] = "waiting"]
       /\ selectedLocation' =
            IF repair THEN installedLocation ELSE selectedLocation
       /\ selectedAssociation' =
            IF repair THEN installedAssociation ELSE selectedAssociation
       /\ aligned' = IF repair THEN TRUE ELSE aligned
       /\ lastEffect' = IF repair THEN "realign" ELSE "none"
       /\ lastEffectIntent' = IF repair THEN currentIntent ELSE 0
       /\ lastEffectAssociation' =
            IF repair THEN installedAssociation ELSE InitialAssociation
       /\ visibleFailure' = (visibleFailure \/ rejected)
       /\ currentPublicationWitness' =
            (/\ currentPublicationWitness
             /\ (~repair \/ currentIntent \in Intents))
       /\ publicationAssociationWitness' =
            (/\ publicationAssociationWitness
             /\ (~repair
                 \/ lastEffectAssociation' = installedAssociation))
       /\ preAdmissionWitness' =
            (/\ preAdmissionWitness
             /\ (rejected
                 \/ aligned
                 \/ /\ repair
                    /\ selectedLocation' = installedLocation
                    /\ selectedAssociation' = installedAssociation
                    /\ aligned'))
       /\ failedRepairAdmissionWitness' =
            (/\ failedRepairAdmissionWitness
             /\ (~rejected
                 \/ /\ nextIntent' = nextIntent
                    /\ currentIntent' = currentIntent
                    /\ source' = source
                    /\ target' = target
                    /\ resultAssociation' = resultAssociation
                    /\ policy' = policy
                    /\ status' = status
                    /\ visibleFailure'))
       /\ allowedEffectWitness' =
            (/\ allowedEffectWitness
             /\ (~repair \/ EffectAllowed(currentIntent, "realign")))
       /\ observedPreAdmissionRealignment' =
            (observedPreAdmissionRealignment \/ repair)
       /\ observedPreAdmissionRealignmentFailure' =
            (observedPreAdmissionRealignmentFailure \/ rejected)
    /\ UNCHANGED << commitOwner,
                    installedLocation,
                    installedIntent,
                    installedAssociation,
                    traversalCaptureWitness,
                    failedTraversalWitness,
                    originatingIntentWitness,
                    cutoverYieldWitness,
                    postCutoverFailureWitness,
                    observedCutoverYield,
                    observedFailedTraversalRealignment,
                    observedStaleAsyncCompletion,
                    observedPostCutoverHistoryFailure >>

CaptureTraversal(destination) ==
    /\ destination \in Locations
    /\ nextIntent < MaxIntents
    /\ LET i == nextIntent + 1
           capturedTarget ==
               IF Fault = "F08LateTraversalCapture"
               THEN installedLocation
               ELSE destination
       IN
       /\ nextIntent' = i
       /\ currentIntent' = i
       /\ source' = [source EXCEPT ![i] = "browser"]
       /\ target' = [target EXCEPT ![i] = capturedTarget]
       /\ resultAssociation' =
            [resultAssociation EXCEPT ![i] = ResultAssociation(i)]
       /\ policy' = [policy EXCEPT ![i] = "browser"]
       /\ status' = [SupersedeWaitingCurrent EXCEPT ![i] = "waiting"]
       /\ selectedLocation' = destination
       /\ selectedAssociation' = ResultAssociation(i)
       /\ aligned' = FALSE
       /\ lastEffect' = "none"
       /\ lastEffectIntent' = 0
       /\ lastEffectAssociation' = InitialAssociation
       /\ traversalCaptureWitness' =
            (/\ traversalCaptureWitness
             /\ capturedTarget = destination)
    /\ UNCHANGED << commitOwner,
                    installedLocation,
                    installedIntent,
                    installedAssociation,
                    visibleFailure,
                    currentPublicationWitness,
                    publicationAssociationWitness,
                    preAdmissionWitness,
                    failedRepairAdmissionWitness,
                    failedTraversalWitness,
                    originatingIntentWitness,
                    allowedEffectWitness,
                    cutoverYieldWitness,
                    postCutoverFailureWitness,
                    observedCutoverYield,
                    observedFailedTraversalRealignment,
                    observedPreAdmissionRealignment,
                    observedPreAdmissionRealignmentFailure,
                    observedStaleAsyncCompletion,
                    observedPostCutoverHistoryFailure >>

AcceptIrreversible(i) ==
    /\ i \in Intents
    /\ i = currentIntent
    /\ status[i] = "waiting"
    /\ commitOwner = 0
    /\ status' = [status EXCEPT ![i] = "accepted"]
    /\ commitOwner' = i
    /\ UNCHANGED << nextIntent,
                    currentIntent,
                    source,
                    target,
                    resultAssociation,
                    policy,
                    installedLocation,
                    installedIntent,
                    installedAssociation,
                    selectedLocation,
                    selectedAssociation,
                    aligned,
                    lastEffect,
                    lastEffectIntent,
                    lastEffectAssociation,
                    visibleFailure,
                    traversalCaptureWitness,
                    currentPublicationWitness,
                    publicationAssociationWitness,
                    preAdmissionWitness,
                    failedRepairAdmissionWitness,
                    failedTraversalWitness,
                    originatingIntentWitness,
                    allowedEffectWitness,
                    cutoverYieldWitness,
                    postCutoverFailureWitness,
                    observedCutoverYield,
                    observedFailedTraversalRealignment,
                    observedPreAdmissionRealignment,
                    observedPreAdmissionRealignmentFailure,
                    observedStaleAsyncCompletion,
                    observedPostCutoverHistoryFailure >>

CompleteIrreversible(i, writeSucceeds) ==
    /\ i \in Intents
    /\ writeSucceeds \in BOOLEAN
    /\ commitOwner = i
    /\ status[i] = "accepted"
    /\ LET ownsLocation == i = currentIntent
           mayPublish ==
               ownsLocation
               \/ Fault = "F10F12F13StaleWorkspacePublication"
           effect ==
               IF source[i] = "browser"
               THEN "adopt"
               ELSE IF policy[i] = "push" THEN "push" ELSE "replace"
           publishes == mayPublish /\ writeSucceeds
           publishedAssociation ==
               IF Fault = "ForeignInstalledAssociationEffect"
               THEN ForeignAssociation(i)
               ELSE resultAssociation[i]
           historyFailure == ownsLocation /\ ~writeSucceeds
       IN
       /\ installedLocation' = target[i]
       /\ installedIntent' = i
       /\ installedAssociation' = resultAssociation[i]
       /\ selectedLocation' =
            IF publishes THEN target[i] ELSE selectedLocation
       /\ selectedAssociation' =
            IF publishes THEN publishedAssociation ELSE selectedAssociation
       /\ aligned' =
            IF publishes
            THEN TRUE
            ELSE IF ownsLocation /\ ~writeSucceeds THEN FALSE ELSE aligned
       /\ lastEffect' = IF publishes THEN effect ELSE "none"
       /\ lastEffectIntent' = IF publishes THEN i ELSE 0
       /\ lastEffectAssociation' =
            IF publishes THEN publishedAssociation ELSE InitialAssociation
       /\ visibleFailure' = (visibleFailure \/ historyFailure)
       /\ status' = [status EXCEPT ![i] = "settled"]
       /\ commitOwner' = 0
       /\ currentPublicationWitness' =
            (/\ currentPublicationWitness
             /\ (~publishes \/ ownsLocation))
       /\ publicationAssociationWitness' =
            (/\ publicationAssociationWitness
             /\ (~publishes
                 \/ publishedAssociation = installedAssociation'))
       /\ allowedEffectWitness' =
            (/\ allowedEffectWitness
             /\ (~publishes \/ EffectAllowed(i, effect)))
       /\ cutoverYieldWitness' =
            (/\ cutoverYieldWitness
             /\ (ownsLocation \/ selectedLocation' = selectedLocation))
       /\ postCutoverFailureWitness' =
            (/\ postCutoverFailureWitness
             /\ (~historyFailure
                 \/ /\ installedLocation' = target[i]
                    /\ installedIntent' = i
                    /\ installedAssociation' = resultAssociation[i]
                    /\ selectedLocation' = selectedLocation
                    /\ selectedAssociation' = selectedAssociation
                    /\ ~aligned'
                    /\ visibleFailure'))
       /\ observedCutoverYield' =
            (observedCutoverYield
             \/ /\ ~ownsLocation
                /\ selectedLocation' = selectedLocation)
       /\ observedPostCutoverHistoryFailure' =
            (observedPostCutoverHistoryFailure
             \/ historyFailure)
    /\ UNCHANGED << nextIntent,
                    currentIntent,
                    source,
                    target,
                    resultAssociation,
                    policy,
                    traversalCaptureWitness,
                    preAdmissionWitness,
                    failedRepairAdmissionWitness,
                    failedTraversalWitness,
                    originatingIntentWitness,
                    observedFailedTraversalRealignment,
                    observedPreAdmissionRealignment,
                    observedPreAdmissionRealignmentFailure,
                    observedStaleAsyncCompletion >>

CompleteTraversal(i, outcome) ==
    /\ i \in Intents
    /\ outcome \in TraversalOutcomes
    /\ source[i] = "browser"
    /\ status[i] = "waiting"
    /\ commitOwner = 0
    /\ LET ownsLocation == i = currentIntent
           successful == outcome # "failed"
           repair ==
               /\ ownsLocation
               /\ outcome = "failed"
               /\ Fault # "F14FailedTraversalNotRealigned"
           effect ==
               CASE outcome = "exact" -> "adopt"
                 [] outcome = "changed" -> "replace"
                 [] OTHER -> "realign"
           performsEffect ==
               /\ ownsLocation
               /\ (successful \/ repair)
           publishedAssociation ==
               IF successful
               THEN
                   IF Fault = "ForeignInstalledAssociationEffect"
                   THEN ForeignAssociation(i)
                   ELSE resultAssociation[i]
               ELSE installedAssociation
       IN
       /\ installedLocation' =
            IF ownsLocation /\ successful
            THEN target[i]
            ELSE installedLocation
       /\ installedIntent' =
            IF ownsLocation /\ successful THEN i ELSE installedIntent
       /\ installedAssociation' =
            IF ownsLocation /\ successful
            THEN resultAssociation[i]
            ELSE installedAssociation
       /\ selectedLocation' =
            IF ownsLocation /\ successful
            THEN target[i]
            ELSE IF repair THEN installedLocation ELSE selectedLocation
       /\ selectedAssociation' =
            IF ownsLocation /\ successful
            THEN publishedAssociation
            ELSE IF repair THEN installedAssociation ELSE selectedAssociation
       /\ aligned' =
            IF ownsLocation /\ successful
            THEN TRUE
            ELSE IF repair THEN TRUE ELSE aligned
       /\ lastEffect' = IF performsEffect THEN effect ELSE "none"
       /\ lastEffectIntent' = IF performsEffect THEN i ELSE 0
       /\ lastEffectAssociation' =
            IF performsEffect
            THEN publishedAssociation
            ELSE InitialAssociation
       /\ status' =
            [status EXCEPT
                ![i] = IF ownsLocation THEN "settled" ELSE "superseded"]
       /\ currentPublicationWitness' =
            (/\ currentPublicationWitness
             /\ (~performsEffect \/ ownsLocation))
       /\ publicationAssociationWitness' =
            (/\ publicationAssociationWitness
             /\ (~performsEffect
                 \/ publishedAssociation = installedAssociation'))
       /\ failedTraversalWitness' =
            (/\ failedTraversalWitness
             /\ (outcome # "failed"
                 \/ ~ownsLocation
                 \/ /\ repair
                    /\ selectedLocation' = installedLocation
                    /\ selectedAssociation' = installedAssociation
                    /\ aligned'))
       /\ allowedEffectWitness' =
            (/\ allowedEffectWitness
             /\ (~performsEffect \/ EffectAllowed(i, effect)))
       /\ observedFailedTraversalRealignment' =
            (observedFailedTraversalRealignment \/ repair)
    /\ UNCHANGED << nextIntent,
                    currentIntent,
                    source,
                    target,
                    resultAssociation,
                    policy,
                    commitOwner,
                    visibleFailure,
                    traversalCaptureWitness,
                    preAdmissionWitness,
                    failedRepairAdmissionWitness,
                    originatingIntentWitness,
                    cutoverYieldWitness,
                    postCutoverFailureWitness,
                    observedCutoverYield,
                    observedPreAdmissionRealignment,
                    observedPreAdmissionRealignmentFailure,
                    observedStaleAsyncCompletion,
                    observedPostCutoverHistoryFailure >>

CompleteAsync(i, succeeded, writeSucceeds) ==
    /\ i \in Intents
    /\ succeeded \in BOOLEAN
    /\ writeSucceeds \in BOOLEAN
    /\ source[i] = "nonBrowser"
    /\ status[i] \in {"waiting", "superseded"}
    /\ commitOwner = 0
    /\ LET wasCurrent == i = currentIntent
           mayClaim ==
               wasCurrent \/ Fault = "F16CompletionMintsIntent"
           publishes == mayClaim /\ succeeded /\ writeSucceeds
           effect == IF policy[i] = "push" THEN "push" ELSE "replace"
           publishedAssociation ==
               IF Fault = "ForeignInstalledAssociationEffect"
               THEN ForeignAssociation(i)
               ELSE resultAssociation[i]
           historyFailure == mayClaim /\ succeeded /\ ~writeSucceeds
       IN
       /\ currentIntent' =
            IF mayClaim THEN i ELSE currentIntent
       /\ installedLocation' =
            IF mayClaim /\ succeeded THEN target[i] ELSE installedLocation
       /\ installedIntent' =
            IF mayClaim /\ succeeded THEN i ELSE installedIntent
       /\ installedAssociation' =
            IF mayClaim /\ succeeded
            THEN resultAssociation[i]
            ELSE installedAssociation
       /\ selectedLocation' =
            IF publishes THEN target[i] ELSE selectedLocation
       /\ selectedAssociation' =
            IF publishes THEN publishedAssociation ELSE selectedAssociation
       /\ aligned' =
            IF publishes
            THEN TRUE
            ELSE IF mayClaim /\ succeeded /\ ~writeSucceeds
                 THEN FALSE
                 ELSE aligned
       /\ lastEffect' = IF publishes THEN effect ELSE "none"
       /\ lastEffectIntent' = IF publishes THEN i ELSE 0
       /\ lastEffectAssociation' =
            IF publishes THEN publishedAssociation ELSE InitialAssociation
       /\ visibleFailure' = (visibleFailure \/ historyFailure)
       /\ status' = [status EXCEPT ![i] = "settled"]
       /\ currentPublicationWitness' =
            (/\ currentPublicationWitness
             /\ (~publishes \/ wasCurrent))
       /\ publicationAssociationWitness' =
            (/\ publicationAssociationWitness
             /\ (~publishes
                 \/ publishedAssociation = installedAssociation'))
       /\ originatingIntentWitness' =
            (/\ originatingIntentWitness
             /\ (wasCurrent \/ currentIntent' = currentIntent))
       /\ allowedEffectWitness' =
            (/\ allowedEffectWitness
             /\ (~publishes \/ EffectAllowed(i, effect)))
       /\ observedStaleAsyncCompletion' =
            (observedStaleAsyncCompletion \/ ~wasCurrent)
    /\ UNCHANGED << nextIntent,
                    source,
                    target,
                    resultAssociation,
                    policy,
                    commitOwner,
                    traversalCaptureWitness,
                    preAdmissionWitness,
                    failedRepairAdmissionWitness,
                    failedTraversalWitness,
                    cutoverYieldWitness,
                    postCutoverFailureWitness,
                    observedCutoverYield,
                    observedFailedTraversalRealignment,
                    observedPreAdmissionRealignment,
                    observedPreAdmissionRealignmentFailure,
                    observedPostCutoverHistoryFailure >>

Next ==
    \/ \E destination \in Locations,
          requestedPolicy \in {"push", "replace"},
          repairSucceeds \in BOOLEAN:
          BeginNonBrowser(destination, requestedPolicy, repairSucceeds)
    \/ \E destination \in Locations:
          CaptureTraversal(destination)
    \/ \E i \in Intents:
          AcceptIrreversible(i)
    \/ \E i \in Intents,
          writeSucceeds \in BOOLEAN:
          CompleteIrreversible(i, writeSucceeds)
    \/ \E i \in Intents,
          outcome \in TraversalOutcomes:
          CompleteTraversal(i, outcome)
    \/ \E i \in Intents,
          succeeded \in BOOLEAN,
          writeSucceeds \in BOOLEAN:
          CompleteAsync(i, succeeded, writeSucceeds)

Spec ==
    Init /\ [][Next]_vars

TypeOK ==
    /\ nextIntent \in 0..MaxIntents
    /\ currentIntent \in 0..MaxIntents
    /\ source \in [Intents -> Sources]
    /\ target \in [Intents -> Locations]
    /\ resultAssociation \in [Intents -> Associations]
    /\ policy \in [Intents -> Policies]
    /\ status \in [Intents -> Statuses]
    /\ commitOwner \in 0..MaxIntents
    /\ installedLocation \in Locations
    /\ installedIntent \in 0..MaxIntents
    /\ installedAssociation \in Associations
    /\ selectedLocation \in Locations
    /\ selectedAssociation \in Associations
    /\ aligned \in BOOLEAN
    /\ lastEffect \in Effects
    /\ lastEffectIntent \in 0..MaxIntents
    /\ lastEffectAssociation \in Associations
    /\ visibleFailure \in BOOLEAN
    /\ traversalCaptureWitness \in BOOLEAN
    /\ currentPublicationWitness \in BOOLEAN
    /\ publicationAssociationWitness \in BOOLEAN
    /\ preAdmissionWitness \in BOOLEAN
    /\ failedRepairAdmissionWitness \in BOOLEAN
    /\ failedTraversalWitness \in BOOLEAN
    /\ originatingIntentWitness \in BOOLEAN
    /\ allowedEffectWitness \in BOOLEAN
    /\ cutoverYieldWitness \in BOOLEAN
    /\ postCutoverFailureWitness \in BOOLEAN
    /\ observedCutoverYield \in BOOLEAN
    /\ observedFailedTraversalRealignment \in BOOLEAN
    /\ observedPreAdmissionRealignment \in BOOLEAN
    /\ observedPreAdmissionRealignmentFailure \in BOOLEAN
    /\ observedStaleAsyncCompletion \in BOOLEAN
    /\ observedPostCutoverHistoryFailure \in BOOLEAN

AlignedLocationMatchesInstalled ==
    aligned =>
        /\ selectedLocation = installedLocation
        /\ selectedAssociation = installedAssociation

CurrentBrowserEntryIsCaptured ==
    IF currentIntent \in Intents
       /\ source[currentIntent] = "browser"
       /\ status[currentIntent] \in {"waiting", "accepted"}
    THEN /\ ~aligned
         /\ target[currentIntent] = selectedLocation
         /\ resultAssociation[currentIntent] = selectedAssociation
    ELSE TRUE

OnlyCurrentIntentPublishes ==
    currentPublicationWitness

EveryEffectUsesInstalledAssociation ==
    publicationAssociationWitness

NonBrowserAdmissionRepairsLocation ==
    preAdmissionWitness

FailedPreAdmissionRepairRejectsIntent ==
    failedRepairAdmissionWitness

FailedTraversalRepairsLocation ==
    failedTraversalWitness

AsyncCompletionKeepsOriginatingIntent ==
    originatingIntentWitness

EveryEffectIsAllowed ==
    allowedEffectWitness

IrreversibleCutoverYieldsLocation ==
    cutoverYieldWitness

PostCutoverFailurePreservesInstalledResult ==
    postCutoverFailureWitness

Safety ==
    /\ TypeOK
    /\ AlignedLocationMatchesInstalled
    /\ CurrentBrowserEntryIsCaptured
    /\ traversalCaptureWitness
    /\ OnlyCurrentIntentPublishes
    /\ EveryEffectUsesInstalledAssociation
    /\ NonBrowserAdmissionRepairsLocation
    /\ FailedPreAdmissionRepairRejectsIntent
    /\ FailedTraversalRepairsLocation
    /\ AsyncCompletionKeepsOriginatingIntent
    /\ EveryEffectIsAllowed
    /\ IrreversibleCutoverYieldsLocation
    /\ PostCutoverFailurePreservesInstalledResult

NeverCutoverYieldObserved ==
    ~observedCutoverYield

NeverFailedTraversalRealignmentObserved ==
    ~observedFailedTraversalRealignment

NeverPreAdmissionRealignmentObserved ==
    ~observedPreAdmissionRealignment

NeverPreAdmissionRealignmentFailureObserved ==
    ~observedPreAdmissionRealignmentFailure

NeverStaleAsyncCompletionObserved ==
    ~observedStaleAsyncCompletion

NeverPostCutoverHistoryFailureObserved ==
    ~observedPostCutoverHistoryFailure

=============================================================================
