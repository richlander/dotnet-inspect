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
    ParticipationMode,
    ContextTwoSendMode,
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
    /\ ParticipationMode \in {"LiveOnly", "AllowRetired"}
    /\ ContextTwoSendMode \in {"Unrestricted", "AfterContextOneCredential"}
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
    postRetirementSendWitness,
    retiredChallengedWitness,
    retiredActiveWitness,
    retiredCacheReadViolation,
    retiredChallengeAuthorizationViolation,
    retiredAcquisitionStartViolation,
    retiredAcquisitionJoinViolation,
    retiredCredentialUseViolation,
    retiredPublicationViolation

vars ==
    <<live, credential, requestState, usedCredential, cacheReadContext,
      authorizedChallenges, rejectedChallenges, joinedAttempt, attemptState,
      attemptContext, publicationWitness, independentFlightWitness,
      retiredPopulated, postRetirementSendWitness, retiredChallengedWitness,
      retiredActiveWitness, retiredCacheReadViolation,
      retiredChallengeAuthorizationViolation,
      retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation,
      retiredCredentialUseViolation, retiredPublicationViolation>>

ActiveAttempts(c) ==
    {a \in Requests :
        /\ attemptContext[a] = c
        /\ attemptState[a] \in ActiveAttemptStates}

ActiveAttempt(c) == CHOOSE a \in ActiveAttempts(c) : TRUE

ChallengedRequests(c) ==
    {r \in Requests :
        /\ RequestContext(r) = c
        /\ requestState[r] = "Challenged"}

ContextMayParticipate(c) ==
    /\ c \in Contexts
    /\ (ParticipationMode = "AllowRetired" \/ live[c])

MayParticipateNow(r) ==
    LET c == RequestContext(r)
    IN
    /\ StaticRequestAuthorization(r, c)
    /\ ContextMayParticipate(c)

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
            /\ credential[ContextTwo] = NoCredential
            /\ (ContextTwoSendMode = "Unrestricted"
                \/ credential[ContextOne] = CredentialFor(ContextOne))
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
    /\ retiredChallengedWitness = FALSE
    /\ retiredActiveWitness = FALSE
    /\ retiredCacheReadViolation = FALSE
    /\ retiredChallengeAuthorizationViolation = FALSE
    /\ retiredAcquisitionStartViolation = FALSE
    /\ retiredAcquisitionJoinViolation = FALSE
    /\ retiredCredentialUseViolation = FALSE
    /\ retiredPublicationViolation = FALSE

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
    /\ retiredCacheReadViolation' =
        (retiredCacheReadViolation
         \/ (/\ selectedContext \in Contexts
             /\ ~live[selectedContext]))
    /\ retiredCredentialUseViolation' =
        (retiredCredentialUseViolation
         \/ (/\ selected # NoCredential
             /\ selectedContext \in Contexts
             /\ ~live[selectedContext]))
    \* Latches on the later send itself, whatever that send selected, so
    \* PostRetirementRequestCannotUsePlugin remains an obligation about the
    \* selection rather than a restatement of it.
    /\ postRetirementSendWitness' =
        (postRetirementSendWitness
         \/ (/\ r = ContextOneLater
             /\ ContextOne \in retiredPopulated
             /\ ~live[ContextOne]))
    /\ UNCHANGED
        <<live, credential, authorizedChallenges, rejectedChallenges,
          joinedAttempt, attemptState, attemptContext, publicationWitness,
          independentFlightWitness, retiredPopulated,
          retiredChallengedWitness, retiredActiveWitness,
          retiredChallengeAuthorizationViolation,
          retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation,
          retiredPublicationViolation>>

ReceiveChallenge(r) ==
    LET c == RequestContext(r)
    IN
    /\ requestState[r] = "SentAnonymous"
    /\ IF MayParticipateNow(r)
       THEN
           /\ requestState' = [requestState EXCEPT ![r] = "Challenged"]
           /\ authorizedChallenges' = authorizedChallenges \cup {r}
           /\ retiredChallengeAuthorizationViolation' =
               (retiredChallengeAuthorizationViolation
                \/ ~live[c])
           /\ UNCHANGED rejectedChallenges
       ELSE
           /\ requestState' = [requestState EXCEPT ![r] = "Done"]
           /\ rejectedChallenges' = rejectedChallenges \cup {r}
           /\ UNCHANGED retiredChallengeAuthorizationViolation
           /\ UNCHANGED authorizedChallenges
    /\ UNCHANGED
        <<live, credential, usedCredential, cacheReadContext, joinedAttempt,
          attemptState, attemptContext, publicationWitness,
          independentFlightWitness, retiredPopulated,
          postRetirementSendWitness, retiredChallengedWitness,
          retiredActiveWitness, retiredCacheReadViolation,
          retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation,
          retiredCredentialUseViolation, retiredPublicationViolation>>

CompleteWithoutChallenge(r) ==
    /\ requestState[r] = "SentAnonymous"
    /\ requestState' = [requestState EXCEPT ![r] = "Done"]
    /\ UNCHANGED
        <<live, credential, usedCredential, cacheReadContext,
          authorizedChallenges, rejectedChallenges, joinedAttempt,
          attemptState, attemptContext, publicationWitness,
          independentFlightWitness, retiredPopulated,
          postRetirementSendWitness, retiredChallengedWitness,
          retiredActiveWitness, retiredCacheReadViolation,
          retiredChallengeAuthorizationViolation,
          retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation,
          retiredCredentialUseViolation, retiredPublicationViolation>>

RejectRetiredChallenge(r) ==
    LET c == RequestContext(r)
    IN
    /\ requestState[r] = "Challenged"
    /\ c \in Contexts
    /\ ~live[c]
    /\ requestState' = [requestState EXCEPT ![r] = "Done"]
    /\ rejectedChallenges' = rejectedChallenges \cup {r}
    /\ UNCHANGED
        <<live, credential, usedCredential, cacheReadContext,
          authorizedChallenges, joinedAttempt, attemptState, attemptContext,
          publicationWitness, independentFlightWitness, retiredPopulated,
          postRetirementSendWitness, retiredChallengedWitness,
          retiredActiveWitness, retiredCacheReadViolation,
          retiredChallengeAuthorizationViolation,
          retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation,
          retiredCredentialUseViolation, retiredPublicationViolation>>

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
           /\ retiredAcquisitionStartViolation' =
               (retiredAcquisitionStartViolation \/ ~live[c])
           /\ UNCHANGED retiredAcquisitionJoinViolation
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
           /\ retiredAcquisitionJoinViolation' =
               (retiredAcquisitionJoinViolation \/ ~live[c])
           /\ UNCHANGED
                <<attemptState, attemptContext, independentFlightWitness,
                  retiredAcquisitionStartViolation>>
    /\ UNCHANGED
        <<live, credential, usedCredential, cacheReadContext,
          authorizedChallenges, rejectedChallenges, publicationWitness,
          retiredPopulated, postRetirementSendWitness,
          retiredChallengedWitness, retiredActiveWitness,
          retiredCacheReadViolation, retiredChallengeAuthorizationViolation,
          retiredCredentialUseViolation, retiredPublicationViolation>>

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
    /\ retiredCredentialUseViolation' =
        (retiredCredentialUseViolation \/ ~live[c])
    /\ UNCHANGED
        <<live, credential, cacheReadContext, authorizedChallenges,
          rejectedChallenges, joinedAttempt, attemptState, attemptContext,
          publicationWitness, independentFlightWitness, retiredPopulated,
          postRetirementSendWitness, retiredChallengedWitness,
          retiredActiveWitness, retiredCacheReadViolation,
          retiredChallengeAuthorizationViolation,
          retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation,
          retiredPublicationViolation>>

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
          retiredPopulated, postRetirementSendWitness,
          retiredChallengedWitness, retiredActiveWitness,
          retiredCacheReadViolation, retiredChallengeAuthorizationViolation,
          retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation,
          retiredCredentialUseViolation, retiredPublicationViolation>>

\* Retirement is exogenous: the configured authority can be replaced at any
\* time, so any live context may retire in any state. Nothing about the
\* context's own progress enables or delays it.
RetireContext(c) ==
    /\ live[c]
    /\ live' = [live EXCEPT ![c] = FALSE]
    /\ credential' = [credential EXCEPT ![c] = NoCredential]
    /\ retiredPopulated' =
        IF /\ c = ContextOne
           /\ credential[c] # NoCredential
           /\ ActiveAttempts(c) = {}
        THEN retiredPopulated \cup {c}
        ELSE retiredPopulated
    /\ retiredChallengedWitness' =
        (retiredChallengedWitness \/ ChallengedRequests(c) # {})
    /\ retiredActiveWitness' =
        (retiredActiveWitness \/ ActiveAttempts(c) # {})
    /\ UNCHANGED
        <<requestState, usedCredential, cacheReadContext,
          authorizedChallenges, rejectedChallenges, joinedAttempt,
          attemptState, attemptContext, publicationWitness,
          independentFlightWitness, postRetirementSendWitness,
          retiredCacheReadViolation, retiredChallengeAuthorizationViolation,
          retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation,
          retiredCredentialUseViolation, retiredPublicationViolation>>

CompleteAcquisition(a) ==
    LET c == attemptContext[a]
        success == attemptState[a] = "SuccessAvailable"
        publishAllowed == success /\ ContextMayParticipate(c)
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
    /\ retiredCredentialUseViolation' =
        (retiredCredentialUseViolation
         \/ (/\ retryCredential # NoCredential
             /\ ~live[c]))
    /\ attemptState' = [attemptState EXCEPT ![a] = "Completed"]
    /\ publicationWitness' =
        publicationWitness
        /\ (~publish
            \/ /\ publishAllowed
               /\ c \in Contexts
               /\ credential[c] \in {NoCredential, CredentialFor(c)})
    /\ retiredPublicationViolation' =
        (retiredPublicationViolation
         \/ (publish /\ ~live[c]))
    /\ UNCHANGED
        <<live, cacheReadContext, authorizedChallenges, rejectedChallenges,
          joinedAttempt, attemptContext, independentFlightWitness,
          retiredPopulated, postRetirementSendWitness,
          retiredChallengedWitness, retiredActiveWitness,
          retiredCacheReadViolation, retiredChallengeAuthorizationViolation,
          retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation>>

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
    /\ retiredPublicationViolation' = TRUE
    /\ UNCHANGED
        <<live, usedCredential, cacheReadContext, authorizedChallenges,
          rejectedChallenges, joinedAttempt, attemptContext,
          independentFlightWitness, retiredPopulated,
          postRetirementSendWitness, retiredChallengedWitness,
          retiredActiveWitness, retiredCacheReadViolation,
          retiredChallengeAuthorizationViolation,
          retiredAcquisitionStartViolation, retiredAcquisitionJoinViolation,
          retiredCredentialUseViolation>>

Next ==
    \/ \E r \in Requests : SendRequest(r)
    \/ \E r \in Requests : ReceiveChallenge(r)
    \/ \E r \in Requests : CompleteWithoutChallenge(r)
    \/ \E r \in Requests : RejectRetiredChallenge(r)
    \/ \E r \in Requests : ResolveAuthorizedChallenge(r)
    \/ \E a \in Requests :
        \E outcome \in {"SuccessAvailable", "FailureAvailable"} :
            ProvideOutcome(a, outcome)
    \/ \E c \in Contexts : RetireContext(c)
    \/ \E a \in Requests : CompleteAcquisition(a)
    \/ \E a \in Requests : CompleteAcquisitionWithStalePublication(a)

Fairness ==
    /\ \A r \in Requests : WF_vars(ResolveAuthorizedChallenge(r))
    /\ \A r \in Requests : WF_vars(RejectRetiredChallenge(r))
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
    /\ retiredChallengedWitness \in BOOLEAN
    /\ retiredActiveWitness \in BOOLEAN
    /\ retiredCacheReadViolation \in BOOLEAN
    /\ retiredChallengeAuthorizationViolation \in BOOLEAN
    /\ retiredAcquisitionStartViolation \in BOOLEAN
    /\ retiredAcquisitionJoinViolation \in BOOLEAN
    /\ retiredCredentialUseViolation \in BOOLEAN
    /\ retiredPublicationViolation \in BOOLEAN

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

ChallengedRetirementNotObserved ==
    ~retiredChallengedWitness

ActiveRetirementNotObserved ==
    ~retiredActiveWitness

IndependentFlightsNotObserved ==
    ~independentFlightWitness

SourceIsolationNotObserved ==
    ~(/\ credential[ContextOne] = CredentialFor(ContextOne)
      /\ requestState[ContextTwoFirst] = "SentAnonymous"
      /\ cacheReadContext[ContextTwoFirst] = ContextTwo
      /\ usedCredential[ContextTwoFirst] = NoCredential)

ExcludedAndGalleryNonParticipationNotObserved ==
    LET excluded ==
        {UnassociatedRequest, IneligibleRequest, OutOfScopeRequest,
         GalleryRequest}
    IN
    ~(/\ excluded \subseteq rejectedChallenges
      /\ \A r \in excluded :
            /\ cacheReadContext[r] = NoContext
            /\ usedCredential[r] = NoCredential
            /\ attemptState[r] = "Unused"
            /\ joinedAttempt[r] = NoRequest)

NoRetiredCacheRead ==
    ~retiredCacheReadViolation

NoRetiredChallengeAuthorization ==
    ~retiredChallengeAuthorizationViolation

NoRetiredAcquisitionStart ==
    ~retiredAcquisitionStartViolation

NoRetiredAcquisitionJoin ==
    ~retiredAcquisitionJoinViolation

NoRetiredCredentialUse ==
    ~retiredCredentialUseViolation

NoRetiredPublication ==
    ~retiredPublicationViolation

RetiredParticipationEventsAreContained ==
    /\ NoRetiredCacheRead
    /\ NoRetiredChallengeAuthorization
    /\ NoRetiredAcquisitionStart
    /\ NoRetiredAcquisitionJoin
    /\ NoRetiredCredentialUse
    /\ NoRetiredPublication

AllRetiredParticipationViolationsNotObserved ==
    ~(/\ retiredCacheReadViolation
      /\ retiredChallengeAuthorizationViolation
      /\ retiredAcquisitionStartViolation
      /\ retiredAcquisitionJoinViolation
      /\ retiredCredentialUseViolation
      /\ retiredPublicationViolation)

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
