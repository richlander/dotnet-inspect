---------------- MODULE NuGetSourceAuthenticationRefresh ----------------
EXTENDS FiniteSets, Naturals

\* Owned by docs/design/nuget-authentication.md.
\* Focused interaction model for one live source context refreshing one
\* rejected cached plugin credential version. The context reference and the
\* target authorization decision are inputs already established by
\* NuGetSourceAuthenticationContext; this module checks only what happens
\* around the rejected version.
\*
\* The model bounds itself to a single refresh episode: the version the server
\* rejects is InitialVersion. A request that first observes a version this
\* episode published is outside the episode and completes without rejecting.
\* Whether that later request is itself rejected later is a further episode
\* and is deliberately not modeled.

CONSTANTS
    Requests,
    RequestOne,
    RequestTwo,
    NoRequest,
    InitialVersion,
    RequestOneVersion,
    RequestTwoVersion,
    MaxVersion,
    RefreshMode,
    AcquisitionMode,
    ConsumptionMode,
    PublicationMode

ASSUME
    /\ Requests = {RequestOne, RequestTwo}
    /\ RequestOne # RequestTwo
    /\ NoRequest \notin Requests
    /\ InitialVersion \in Nat \ {0}
    /\ RequestOneVersion \in Nat
    /\ RequestTwoVersion \in Nat
    /\ InitialVersion < RequestOneVersion
    /\ RequestOneVersion < RequestTwoVersion
    /\ MaxVersion = RequestTwoVersion
    /\ RefreshMode \in {"SingleFlight", "DuplicateAcquisition"}
    /\ AcquisitionMode \in {"CurrentVersionOnly", "StaleMayAcquire"}
    /\ ConsumptionMode \in {"ReadOnly", "ConsumeWritesObservedVersion"}
    /\ PublicationMode \in {"MonotonicPublish", "ClobberPublish"}

RequestStates ==
    {"Ready", "SentCached", "Rejected", "Waiting", "Rechecking", "Done"}
AttemptStates == {"Unused", "Pending", "SuccessAvailable", "Completed"}
ActiveAttemptStates == {"Pending", "SuccessAvailable"}

EpisodeEvents ==
    {"JoinedFlight", "FollowerConsumedNewerVersion", "LateRejection",
     "PostRefreshAccept"}

VARIABLES
    requestState,
    observedVersion,
    joinedAttempt,
    attemptState,
    attemptVersion,
    credentialVersion,
    flightFollowers,
    episodeEvents

vars ==
    <<requestState, observedVersion, joinedAttempt, attemptState,
      attemptVersion, credentialVersion, flightFollowers, episodeEvents>>

RefreshVersion(r) ==
    IF r = RequestOne THEN RequestOneVersion ELSE RequestTwoVersion

ActiveAttempts ==
    {a \in Requests : attemptState[a] \in ActiveAttemptStates}

CompletedAttempts ==
    {a \in Requests : attemptState[a] = "Completed"}

ActiveAttempt == CHOOSE a \in ActiveAttempts : TRUE

\* The specified policy admits provider work only for a request whose observed
\* version is still the cached version. "StaleMayAcquire" is the negative
\* control that removes that gate.
MayAcquire(r) ==
    \/ AcquisitionMode = "StaleMayAcquire"
    \/ observedVersion[r] = credentialVersion

Init ==
    /\ requestState = [r \in Requests |-> "Ready"]
    /\ observedVersion = [r \in Requests |-> 0]
    /\ joinedAttempt = [r \in Requests |-> NoRequest]
    /\ attemptState = [a \in Requests |-> "Unused"]
    /\ attemptVersion = [a \in Requests |-> 0]
    /\ credentialVersion = InitialVersion
    /\ flightFollowers = {}
    /\ episodeEvents = {}

\* A request attaches whatever version the cache currently holds. This is
\* unconstrained by provider progress: either request may send before, during,
\* or after the refresh flight.
SendCachedCredential(r) ==
    /\ requestState[r] = "Ready"
    /\ requestState' = [requestState EXCEPT ![r] = "SentCached"]
    /\ observedVersion' =
        [observedVersion EXCEPT ![r] = credentialVersion]
    /\ UNCHANGED
        <<joinedAttempt, attemptState, attemptVersion, credentialVersion,
          flightFollowers, episodeEvents>>

\* The server rejects this episode's version. The rejection may arrive after
\* the refresh already published a newer version, which leaves the request
\* holding a stale observation.
RejectCachedCredential(r) ==
    /\ requestState[r] = "SentCached"
    /\ observedVersion[r] = InitialVersion
    /\ requestState' = [requestState EXCEPT ![r] = "Rejected"]
    /\ episodeEvents' =
        IF observedVersion[r] < credentialVersion
        THEN episodeEvents \cup {"LateRejection"}
        ELSE episodeEvents
    /\ UNCHANGED
        <<observedVersion, joinedAttempt, attemptState, attemptVersion,
          credentialVersion, flightFollowers>>

\* A request that first observed a version published by this episode is
\* outside the episode: the server accepts it and no further refresh is owed.
AcceptCachedCredential(r) ==
    /\ requestState[r] = "SentCached"
    /\ observedVersion[r] # InitialVersion
    /\ requestState' = [requestState EXCEPT ![r] = "Done"]
    /\ episodeEvents' = episodeEvents \cup {"PostRefreshAccept"}
    /\ UNCHANGED
        <<observedVersion, joinedAttempt, attemptState, attemptVersion,
          credentialVersion, flightFollowers>>

ResolveSentRequest(r) ==
    \/ RejectCachedCredential(r)
    \/ AcceptCachedCredential(r)

StartOrJoinRefresh(r) ==
    /\ requestState[r] = "Rejected"
    /\ MayAcquire(r)
    /\ IF ActiveAttempts = {}
       THEN
           /\ attemptState[r] = "Unused"
           /\ requestState' = [requestState EXCEPT ![r] = "Waiting"]
           /\ joinedAttempt' = [joinedAttempt EXCEPT ![r] = r]
           /\ attemptState' = [attemptState EXCEPT ![r] = "Pending"]
           /\ attemptVersion' =
               [attemptVersion EXCEPT ![r] = RefreshVersion(r)]
           /\ UNCHANGED <<flightFollowers, episodeEvents>>
       ELSE
           \* r reached the refresh decision while a flight was already
           \* running. It is a follower of that flight in either mode.
           /\ flightFollowers' = flightFollowers \cup {r}
           /\ IF RefreshMode = "SingleFlight"
              THEN
                  LET a == ActiveAttempt
                  IN
                  /\ requestState' = [requestState EXCEPT ![r] = "Waiting"]
                  /\ joinedAttempt' = [joinedAttempt EXCEPT ![r] = a]
                  /\ episodeEvents' =
                      episodeEvents \cup {"JoinedFlight"}
                  /\ UNCHANGED <<attemptState, attemptVersion>>
              ELSE
                  /\ attemptState[r] = "Unused"
                  /\ requestState' = [requestState EXCEPT ![r] = "Waiting"]
                  /\ joinedAttempt' = [joinedAttempt EXCEPT ![r] = r]
                  /\ attemptState' =
                      [attemptState EXCEPT ![r] = "Pending"]
                  /\ attemptVersion' =
                      [attemptVersion EXCEPT ![r] = RefreshVersion(r)]
                  /\ UNCHANGED episodeEvents
    /\ UNCHANGED <<observedVersion, credentialVersion>>

\* A rejected or rechecking request whose observation is older than the cache
\* takes the newer cached version instead of asking the provider again.
ConsumeNewerCredential(r) ==
    /\ requestState[r] \in {"Rejected", "Rechecking"}
    /\ observedVersion[r] < credentialVersion
    /\ requestState' = [requestState EXCEPT ![r] = "Done"]
    /\ observedVersion' =
        [observedVersion EXCEPT ![r] = credentialVersion]
    /\ credentialVersion' =
        IF ConsumptionMode = "ConsumeWritesObservedVersion"
        THEN observedVersion[r]
        ELSE credentialVersion
    /\ episodeEvents' =
        IF r \in flightFollowers
        THEN episodeEvents \cup {"FollowerConsumedNewerVersion"}
        ELSE episodeEvents
    /\ UNCHANGED
        <<joinedAttempt, attemptState, attemptVersion, flightFollowers>>

ResolveRejectedRequest(r) ==
    \/ StartOrJoinRefresh(r)
    \/ ConsumeNewerCredential(r)

ProvideRefreshOutcome(a) ==
    /\ attemptState[a] = "Pending"
    /\ attemptState' = [attemptState EXCEPT ![a] = "SuccessAvailable"]
    /\ UNCHANGED
        <<requestState, observedVersion, joinedAttempt, attemptVersion,
          credentialVersion, flightFollowers, episodeEvents>>

CompleteRefresh(a) ==
    LET candidate == attemptVersion[a]
        nextVersion ==
            CASE PublicationMode = "ClobberPublish" -> candidate
              [] candidate > credentialVersion -> candidate
              [] OTHER -> credentialVersion
    IN
    /\ attemptState[a] = "SuccessAvailable"
    /\ credentialVersion' = nextVersion
    /\ requestState' =
        [r \in Requests |->
            IF requestState[r] = "Waiting" /\ joinedAttempt[r] = a
            THEN "Rechecking"
            ELSE requestState[r]]
    /\ attemptState' = [attemptState EXCEPT ![a] = "Completed"]
    /\ UNCHANGED
        <<observedVersion, joinedAttempt, attemptVersion, flightFollowers,
          episodeEvents>>

Next ==
    \/ \E r \in Requests : SendCachedCredential(r)
    \/ \E r \in Requests : ResolveSentRequest(r)
    \/ \E r \in Requests : ResolveRejectedRequest(r)
    \/ \E a \in Requests : ProvideRefreshOutcome(a)
    \/ \E a \in Requests : CompleteRefresh(a)

Fairness ==
    /\ \A r \in Requests : WF_vars(ResolveRejectedRequest(r))
    /\ \A r \in Requests : WF_vars(ConsumeNewerCredential(r))
    /\ \A a \in Requests : WF_vars(CompleteRefresh(a))

Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ Fairness

TypeOK ==
    /\ requestState \in [Requests -> RequestStates]
    /\ observedVersion \in [Requests -> 0..MaxVersion]
    /\ joinedAttempt \in [Requests -> Requests \cup {NoRequest}]
    /\ attemptState \in [Requests -> AttemptStates]
    /\ attemptVersion \in [Requests -> 0..MaxVersion]
    /\ credentialVersion \in 1..MaxVersion
    /\ flightFollowers \subseteq Requests
    /\ episodeEvents \subseteq EpisodeEvents

AtMostOneProviderAcquisition ==
    Cardinality(ActiveAttempts) <= 1

\* One refresh episode consults the provider once. This holds because a
\* follower joins the running flight and because a request whose observation
\* the episode already superseded consumes the newer version instead of
\* starting its own flight.
AtMostOneProviderCompletion ==
    Cardinality(CompletedAttempts) <= 1

WaitingRequestsShareActiveAcquisition ==
    \A r \in Requests :
        requestState[r] = "Waiting" =>
            /\ joinedAttempt[r] \in ActiveAttempts
            /\ observedVersion[r] <= credentialVersion

\* A follower is served by the flight it found running; it never runs provider
\* work of its own.
FollowersDoNotRunTheirOwnProviderWork ==
    \A r \in flightFollowers :
        /\ attemptState[r] = "Unused"
        /\ joinedAttempt[r] \in Requests \ {r}

DoneRequestsConsumedCurrentVersion ==
    \A r \in Requests :
        requestState[r] = "Done" =>
            observedVersion[r] = credentialVersion

\* Action properties. Each states an obligation about the transition itself,
\* so it is independent of how any single action happens to be guarded.

\* No request may enter provider work carrying an observation the cache has
\* already superseded, whether it would start or join that work.
StaleObservedRequestCannotAcquire ==
    [][\A r \in Requests :
          (/\ requestState[r] = "Rejected"
           /\ requestState'[r] = "Waiting")
              => observedVersion[r] = credentialVersion]_vars

\* Taking the newer cached version is a read: it must not clear the cache or
\* write the consuming request's older observation back over it.
StaleObservedConsumptionIsReadOnly ==
    [][\A r \in Requests :
          (/\ requestState[r] \in {"Rejected", "Rechecking"}
           /\ requestState'[r] = "Done"
           /\ observedVersion[r] < credentialVersion)
              => credentialVersion' = credentialVersion]_vars

CredentialVersionNeverRegresses ==
    [][credentialVersion' >= credentialVersion]_vars

AvailableRefreshEventuallyCompletes ==
    \A a \in Requests :
        (attemptState[a] = "SuccessAvailable")
            ~> (attemptState[a] = "Completed")

StaleObservedRequestsEventuallyConsume ==
    \A r \in Requests :
        (/\ requestState[r] \in {"Rejected", "Rechecking"}
         /\ observedVersion[r] < credentialVersion)
            ~> (requestState[r] = "Done")

\* Reachability witnesses. Each must be violated by its own configuration.

JoinAndFollowerConsumptionNotObserved ==
    ~({"JoinedFlight", "FollowerConsumedNewerVersion"} \subseteq episodeEvents)

LateRejectionNotObserved ==
    "LateRejection" \notin episodeEvents

PostRefreshAcceptNotObserved ==
    "PostRefreshAccept" \notin episodeEvents

=============================================================================
