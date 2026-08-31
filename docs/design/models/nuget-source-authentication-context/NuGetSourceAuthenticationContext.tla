---------------- MODULE NuGetSourceAuthenticationContext ----------------
EXTENDS FiniteSets, Naturals, TLC

\* Owned by docs/design/nuget-authentication.md.
\* This model checks only the interaction between source association,
\* resource authorization, plugin acquisition, publication, and replay.

CONSTANTS
    Contexts,
    ContextOne,
    ContextTwo,
    Requests,
    ContextOneFirst,
    ContextOneConcurrent,
    ContextOneLater,
    ContextTwoFirst,
    ContextTwoLater,
    UnassociatedRequest,
    IneligibleRequest,
    OutOfScopeRequest,
    GalleryRequest,
    SharedScope,
    ForeignScope,
    NoContext,
    NoRequest,
    NoCredential,
    CredentialSelectionMode,
    PublicationMode

ASSUME
    /\ Contexts = {ContextOne, ContextTwo}
    /\ ContextOne # ContextTwo
    /\ Requests =
        {ContextOneFirst, ContextOneConcurrent, ContextOneLater,
         ContextTwoFirst, ContextTwoLater, UnassociatedRequest,
         IneligibleRequest, OutOfScopeRequest, GalleryRequest}
    /\ Cardinality(Requests) = 9
    /\ NoContext \notin Contexts
    /\ NoRequest \notin Requests
    /\ NoCredential \notin Contexts
    /\ SharedScope # ForeignScope
    /\ CredentialSelectionMode \in {"ContextBound", "ResourceScoped"}
    /\ PublicationMode \in {"LiveOnly", "PublishStale"}

RequestContext(r) ==
    CASE r \in {ContextOneFirst, ContextOneConcurrent, ContextOneLater,
                IneligibleRequest, OutOfScopeRequest} -> ContextOne
      [] r \in {ContextTwoFirst, ContextTwoLater} -> ContextTwo
      [] OTHER -> NoContext

ResourceScope(c) == SharedScope

TargetScope(r) ==
    IF r = OutOfScopeRequest THEN ForeignScope ELSE SharedScope

PluginEligible(r) == r # IneligibleRequest

IsGallery(r) == r = GalleryRequest

StaticRequestAuthorization(r, c) ==
    /\ c \in Contexts
    /\ RequestContext(r) = c
    /\ PluginEligible(r)
    /\ ~IsGallery(r)
    /\ TargetScope(r) = ResourceScope(c)

ExcludedRequest(r) ==
    \/ RequestContext(r) = NoContext
    \/ ~PluginEligible(r)
    \/ IsGallery(r)
    \/ \A c \in Contexts : TargetScope(r) # ResourceScope(c)

CredentialFor(c) == c

RequestStates ==
    {"NotSent", "SentAnonymous", "Challenged", "Waiting", "Done"}
AttemptStates ==
    {"Unused", "Pending", "SuccessAvailable", "FailureAvailable",
     "Completed"}
ActiveAttemptStates == {"Pending", "SuccessAvailable", "FailureAvailable"}

VARIABLES
    live,
    credential,
    requestState,
    usedCredential,
    cacheReadContext,
    authorizedChallenges,
    rejectedChallenges,
    joinedAttempt,
    attemptState,
    attemptContext,
    publicationWitness,
    independentFlightWitness,
    retiredPopulated,
    postRetirementSendWitness

vars ==
    <<live, credential, requestState, usedCredential, cacheReadContext,
      authorizedChallenges, rejectedChallenges, joinedAttempt, attemptState,
      attemptContext, publicationWitness, independentFlightWitness,
      retiredPopulated, postRetirementSendWitness>>

ActiveAttempts(c) ==
    {a \in Requests :
        /\ attemptContext[a] = c
        /\ attemptState[a] \in ActiveAttemptStates}

ActiveAttempt(c) == CHOOSE a \in ActiveAttempts(c) : TRUE

MayParticipateNow(r) ==
    LET c == RequestContext(r)
    IN
    /\ StaticRequestAuthorization(r, c)
    /\ live[c]

ScopeCredentialContext(r) ==
    IF /\ credential[ContextOne] # NoCredential
       /\ TargetScope(r) = ResourceScope(ContextOne)
    THEN ContextOne
    ELSE
        IF /\ credential[ContextTwo] # NoCredential
           /\ TargetScope(r) = ResourceScope(ContextTwo)
        THEN ContextTwo
        ELSE RequestContext(r)

SelectedCacheContext(r) ==
    IF ~MayParticipateNow(r)
    THEN NoContext
    ELSE
        IF CredentialSelectionMode = "ContextBound"
        THEN RequestContext(r)
        ELSE ScopeCredentialContext(r)

SelectedCredential(r) ==
    LET c == SelectedCacheContext(r)
    IN IF c \in Contexts THEN credential[c] ELSE NoCredential

SendPrerequisite(r) ==
    CASE r \in {ContextOneFirst, ContextOneConcurrent} ->
            credential[ContextOne] = NoCredential
      [] r = ContextTwoFirst ->
            credential[ContextTwo] = NoCredential
      [] r = ContextOneLater ->
            credential[ContextOne] # NoCredential
            \/ ContextOne \in retiredPopulated
      [] r = ContextTwoLater -> credential[ContextTwo] # NoCredential
      [] OTHER -> credential[ContextOne] # NoCredential

Init ==
    /\ live = [c \in Contexts |-> TRUE]
    /\ credential = [c \in Contexts |-> NoCredential]
    /\ requestState = [r \in Requests |-> "NotSent"]
    /\ usedCredential = [r \in Requests |-> NoCredential]
    /\ cacheReadContext = [r \in Requests |-> NoContext]
    /\ authorizedChallenges = {}
    /\ rejectedChallenges = {}
    /\ joinedAttempt = [r \in Requests |-> NoRequest]
    /\ attemptState = [a \in Requests |-> "Unused"]
    /\ attemptContext = [a \in Requests |-> NoContext]
    /\ publicationWitness = TRUE
    /\ independentFlightWitness = FALSE
    /\ retiredPopulated = {}
    /\ postRetirementSendWitness = FALSE

SendRequest(r) ==
    LET selected == SelectedCredential(r)
        selectedContext == SelectedCacheContext(r)
    IN
    /\ requestState[r] = "NotSent"
    /\ SendPrerequisite(r)
    /\ requestState' =
        [requestState EXCEPT
            ![r] = IF selected = NoCredential
                    THEN "SentAnonymous"
                    ELSE "Done"]
    /\ usedCredential' = [usedCredential EXCEPT ![r] = selected]
    /\ cacheReadContext' =
        [cacheReadContext EXCEPT ![r] = selectedContext]
    /\ postRetirementSendWitness' =
        (postRetirementSendWitness
         \/ (/\ r = ContextOneLater
             /\ ContextOne \in retiredPopulated
             /\ ~live[ContextOne]
             /\ selectedContext = NoContext
             /\ selected = NoCredential))
    /\ UNCHANGED
        <<live, credential, authorizedChallenges, rejectedChallenges,
          joinedAttempt, attemptState, attemptContext, publicationWitness,
          independentFlightWitness, retiredPopulated>>

ReceiveChallenge(r) ==
    /\ requestState[r] = "SentAnonymous"
    /\ IF MayParticipateNow(r)
       THEN
           /\ requestState' = [requestState EXCEPT ![r] = "Challenged"]
           /\ authorizedChallenges' = authorizedChallenges \cup {r}
           /\ UNCHANGED rejectedChallenges
       ELSE
           /\ requestState' = [requestState EXCEPT ![r] = "Done"]
           /\ rejectedChallenges' = rejectedChallenges \cup {r}
           /\ UNCHANGED authorizedChallenges
    /\ UNCHANGED
        <<live, credential, usedCredential, cacheReadContext, joinedAttempt,
          attemptState, attemptContext, publicationWitness,
          independentFlightWitness, retiredPopulated,
          postRetirementSendWitness>>

CompleteWithoutChallenge(r) ==
    /\ requestState[r] = "SentAnonymous"
    /\ requestState' = [requestState EXCEPT ![r] = "Done"]
    /\ UNCHANGED
        <<live, credential, usedCredential, cacheReadContext,
          authorizedChallenges, rejectedChallenges, joinedAttempt,
          attemptState, attemptContext, publicationWitness,
          independentFlightWitness, retiredPopulated,
          postRetirementSendWitness>>

StartOrJoinChallenge(r) ==
    LET c == RequestContext(r)
        active == ActiveAttempts(c)
    IN
    /\ requestState[r] = "Challenged"
    /\ r \in authorizedChallenges
    /\ MayParticipateNow(r)
    /\ credential[c] = NoCredential
    /\ IF active = {}
       THEN
           /\ attemptState[r] = "Unused"
           /\ requestState' = [requestState EXCEPT ![r] = "Waiting"]
           /\ joinedAttempt' = [joinedAttempt EXCEPT ![r] = r]
           /\ attemptState' = [attemptState EXCEPT ![r] = "Pending"]
           /\ attemptContext' = [attemptContext EXCEPT ![r] = c]
           /\ independentFlightWitness' =
               (independentFlightWitness
                \/ (\E other \in Contexts \ {c} :
                        /\ ResourceScope(other) = ResourceScope(c)
                        /\ ActiveAttempts(other) # {}))
       ELSE
           LET a == ActiveAttempt(c)
           IN
           /\ requestState' = [requestState EXCEPT ![r] = "Waiting"]
           /\ joinedAttempt' = [joinedAttempt EXCEPT ![r] = a]
           /\ UNCHANGED
                <<attemptState, attemptContext, independentFlightWitness>>
    /\ UNCHANGED
        <<live, credential, usedCredential, cacheReadContext,
          authorizedChallenges, rejectedChallenges, publicationWitness,
          retiredPopulated, postRetirementSendWitness>>

ConsumePublishedCredential(r) ==
    LET c == RequestContext(r)
    IN
    /\ requestState[r] = "Challenged"
    /\ r \in authorizedChallenges
    /\ MayParticipateNow(r)
    /\ credential[c] = CredentialFor(c)
    /\ requestState' = [requestState EXCEPT ![r] = "Done"]
    /\ usedCredential' =
        [usedCredential EXCEPT ![r] = CredentialFor(c)]
    /\ UNCHANGED
        <<live, credential, cacheReadContext, authorizedChallenges,
          rejectedChallenges, joinedAttempt, attemptState, attemptContext,
          publicationWitness, independentFlightWitness, retiredPopulated,
          postRetirementSendWitness>>

ResolveAuthorizedChallenge(r) ==
    \/ StartOrJoinChallenge(r)
    \/ ConsumePublishedCredential(r)

ProvideOutcome(a, outcome) ==
    /\ attemptState[a] = "Pending"
    /\ outcome \in {"SuccessAvailable", "FailureAvailable"}
    /\ attemptState' = [attemptState EXCEPT ![a] = outcome]
    /\ UNCHANGED
        <<live, credential, requestState, usedCredential, cacheReadContext,
          authorizedChallenges, rejectedChallenges, joinedAttempt,
          attemptContext, publicationWitness, independentFlightWitness,
          retiredPopulated, postRetirementSendWitness>>

RetireContext(c) ==
    /\ live[c]
    /\ (ActiveAttempts(c) # {}
        \/ /\ c = ContextOne
           /\ credential[c] # NoCredential)
    /\ live' = [live EXCEPT ![c] = FALSE]
    /\ credential' = [credential EXCEPT ![c] = NoCredential]
    /\ retiredPopulated' =
        IF /\ c = ContextOne
           /\ credential[c] # NoCredential
           /\ ActiveAttempts(c) = {}
        THEN retiredPopulated \cup {c}
        ELSE retiredPopulated
    /\ UNCHANGED
        <<requestState, usedCredential, cacheReadContext,
          authorizedChallenges, rejectedChallenges, joinedAttempt,
          attemptState, attemptContext, publicationWitness,
          independentFlightWitness, postRetirementSendWitness>>

CompleteAcquisition(a) ==
    LET c == attemptContext[a]
        success == attemptState[a] = "SuccessAvailable"
        publishAllowed == success /\ live[c]
        publish == publishAllowed
        retryCredential ==
            IF publishAllowed THEN CredentialFor(c) ELSE NoCredential
    IN
    /\ attemptState[a] \in {"SuccessAvailable", "FailureAvailable"}
    /\ credential' =
        IF publish
        THEN [credential EXCEPT ![c] = CredentialFor(c)]
        ELSE credential
    /\ requestState' =
        [r \in Requests |->
            IF requestState[r] = "Waiting" /\ joinedAttempt[r] = a
            THEN "Done"
            ELSE requestState[r]]
    /\ usedCredential' =
        [r \in Requests |->
            IF requestState[r] = "Waiting" /\ joinedAttempt[r] = a
            THEN retryCredential
            ELSE usedCredential[r]]
    /\ attemptState' = [attemptState EXCEPT ![a] = "Completed"]
    /\ publicationWitness' =
        publicationWitness
        /\ (~publish
            \/ /\ publishAllowed
               /\ c \in Contexts
               /\ credential[c] \in {NoCredential, CredentialFor(c)})
    /\ UNCHANGED
        <<live, cacheReadContext, authorizedChallenges, rejectedChallenges,
          joinedAttempt, attemptContext, independentFlightWitness,
          retiredPopulated, postRetirementSendWitness>>

CompleteAcquisitionWithStalePublication(a) ==
    LET c == attemptContext[a]
    IN
    /\ PublicationMode = "PublishStale"
    /\ attemptState[a] = "SuccessAvailable"
    /\ c \in Contexts
    /\ ~live[c]
    /\ credential' = [credential EXCEPT ![c] = CredentialFor(c)]
    /\ requestState' =
        [r \in Requests |->
            IF requestState[r] = "Waiting" /\ joinedAttempt[r] = a
            THEN "Done"
            ELSE requestState[r]]
    /\ attemptState' = [attemptState EXCEPT ![a] = "Completed"]
    /\ publicationWitness' = FALSE
    /\ UNCHANGED
        <<live, usedCredential, cacheReadContext, authorizedChallenges,
          rejectedChallenges, joinedAttempt, attemptContext,
          independentFlightWitness, retiredPopulated,
          postRetirementSendWitness>>

Next ==
    \/ \E r \in Requests : SendRequest(r)
    \/ \E r \in Requests : ReceiveChallenge(r)
    \/ \E r \in Requests : CompleteWithoutChallenge(r)
    \/ \E r \in Requests : ResolveAuthorizedChallenge(r)
    \/ \E a \in Requests :
        \E outcome \in {"SuccessAvailable", "FailureAvailable"} :
            ProvideOutcome(a, outcome)
    \/ \E c \in Contexts : RetireContext(c)
    \/ \E a \in Requests : CompleteAcquisition(a)
    \/ \E a \in Requests : CompleteAcquisitionWithStalePublication(a)

Fairness ==
    /\ \A r \in Requests : WF_vars(ResolveAuthorizedChallenge(r))
    /\ \A a \in Requests : WF_vars(CompleteAcquisition(a))

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ Fairness

TypeOK ==
    /\ live \in [Contexts -> BOOLEAN]
    /\ credential \in [Contexts -> Contexts \cup {NoCredential}]
    /\ requestState \in [Requests -> RequestStates]
    /\ usedCredential \in [Requests -> Contexts \cup {NoCredential}]
    /\ cacheReadContext \in [Requests -> Contexts \cup {NoContext}]
    /\ authorizedChallenges \subseteq Requests
    /\ rejectedChallenges \subseteq Requests
    /\ joinedAttempt \in [Requests -> Requests \cup {NoRequest}]
    /\ attemptState \in [Requests -> AttemptStates]
    /\ attemptContext \in [Requests -> Contexts \cup {NoContext}]
    /\ publicationWitness \in BOOLEAN
    /\ independentFlightWitness \in BOOLEAN
    /\ retiredPopulated \subseteq Contexts
    /\ postRetirementSendWitness \in BOOLEAN

DistinctContextsShareResourceScope ==
    /\ ContextOne # ContextTwo
    /\ ResourceScope(ContextOne) = ResourceScope(ContextTwo)

ContextCredentialsAreIsolated ==
    \A c \in Contexts :
        credential[c] # NoCredential =>
            /\ live[c]
            /\ credential[c] = CredentialFor(c)

RetiredContextsHaveNoCredential ==
    \A c \in Contexts : ~live[c] => credential[c] = NoCredential

PopulatedRetirementIsSound ==
    \A c \in retiredPopulated :
        /\ ~live[c]
        /\ credential[c] = NoCredential

PostRetirementRequestCannotUsePlugin ==
    postRetirementSendWitness =>
        /\ ContextOne \in retiredPopulated
        /\ requestState[ContextOneLater] # "NotSent"
        /\ usedCredential[ContextOneLater] = NoCredential
        /\ cacheReadContext[ContextOneLater] = NoContext
        /\ ContextOneLater \notin authorizedChallenges
        /\ attemptState[ContextOneLater] = "Unused"
        /\ joinedAttempt[ContextOneLater] = NoRequest

PopulatedRetirementAndLaterSendNotObserved ==
    ~postRetirementSendWitness

CredentialUseIsAuthorized ==
    \A r \in Requests :
        usedCredential[r] # NoCredential =>
            LET c == RequestContext(r)
            IN
            /\ StaticRequestAuthorization(r, c)
            /\ usedCredential[r] = CredentialFor(c)

CacheReadsStayContextBound ==
    \A r \in Requests :
        cacheReadContext[r] # NoContext =>
            LET c == RequestContext(r)
            IN
            /\ StaticRequestAuthorization(r, c)
            /\ cacheReadContext[r] = c

AcquisitionStartsAreAuthorized ==
    \A a \in Requests :
        attemptState[a] # "Unused" =>
            LET c == attemptContext[a]
            IN
            /\ a \in authorizedChallenges
            /\ StaticRequestAuthorization(a, c)

WaitersStayInTheirContext ==
    \A r \in Requests :
        requestState[r] = "Waiting" =>
            LET a == joinedAttempt[r]
                c == RequestContext(r)
            IN
            /\ a \in Requests
            /\ attemptState[a] \in ActiveAttemptStates
            /\ attemptContext[a] = c
            /\ StaticRequestAuthorization(r, c)

PublicationIsAuthorized ==
    publicationWitness

AtMostOneAcquisitionPerContext ==
    \A c \in Contexts : Cardinality(ActiveAttempts(c)) <= 1

CrossContextAcquisitionDoesNotBlock ==
    \A r \in Requests :
        LET c == RequestContext(r)
        IN
        /\ requestState[r] = "Challenged"
        /\ c \in Contexts
        /\ ActiveAttempts(c) = {}
        /\ credential[c] = NoCredential
        /\ MayParticipateNow(r)
        => ENABLED ResolveAuthorizedChallenge(r)

ContextTwoCannotConsumeContextOneCredential ==
    \A r \in Requests :
        RequestContext(r) = ContextTwo =>
            /\ usedCredential[r] # CredentialFor(ContextOne)
            /\ cacheReadContext[r] # ContextOne

ExcludedRequestsDoNotParticipate ==
    \A r \in Requests :
        ExcludedRequest(r) =>
            /\ usedCredential[r] = NoCredential
            /\ cacheReadContext[r] = NoContext
            /\ r \notin authorizedChallenges
            /\ attemptState[r] = "Unused"
            /\ joinedAttempt[r] = NoRequest

GalleryDoesNotParticipate ==
    /\ usedCredential[GalleryRequest] = NoCredential
    /\ cacheReadContext[GalleryRequest] = NoContext
    /\ GalleryRequest \notin authorizedChallenges
    /\ attemptState[GalleryRequest] = "Unused"
    /\ joinedAttempt[GalleryRequest] = NoRequest

AvailableAcquisitionsEventuallyComplete ==
    \A a \in Requests :
        (attemptState[a] \in {"SuccessAvailable", "FailureAvailable"})
            ~> (attemptState[a] = "Completed")

AdmittedAuthorizedChallengesEventuallySettle ==
    \A r \in Requests :
        (/\ requestState[r] = "Waiting"
         /\ joinedAttempt[r] \in Requests
         /\ attemptState[joinedAttempt[r]]
                \in {"SuccessAvailable", "FailureAvailable"})
            ~> (requestState[r] = "Done")

=============================================================================
