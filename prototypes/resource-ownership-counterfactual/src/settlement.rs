//! Explicit asynchronous quiescence.
//!
//! Starting settlement consumes the root, so it cannot issue more work:
//!
//! ```compile_fail
//! use resource_ownership_counterfactual::settlement::SettlementRoot;
//!
//! let root = SettlementRoot::new("package-source");
//! let settlement = root.settle();
//! let operation = root.issue_operation().unwrap();
//! drop((settlement, operation));
//! ```

use std::future::Future;
use std::pin::Pin;
use std::sync::{Arc, Mutex, MutexGuard};
use std::task::{Context, Poll, Waker};

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum SettlementPhase {
    Active,
    Settling,
    Settled,
    Failed,
    RootAbandoned,
    ObservationAbandoned,
}

#[derive(Debug)]
struct SettlementState {
    phase: SettlementPhase,
    active_operations: u64,
    successful_operations: u64,
    cancelled_operations: u64,
    failed_operations: u64,
    abandoned_operations: u64,
    next_operation_id: u64,
    waker: Option<Waker>,
}

fn lock_state(state: &Mutex<SettlementState>) -> MutexGuard<'_, SettlementState> {
    state.lock().expect("settlement state poisoned")
}

/// One owner-issued root lifetime that admits operation ownership.
#[derive(Debug)]
pub struct SettlementRoot {
    owner: &'static str,
    state: Arc<Mutex<SettlementState>>,
    settlement_started: bool,
}

impl SettlementRoot {
    /// Creates one active settlement root.
    #[must_use]
    pub fn new(owner: &'static str) -> Self {
        Self {
            owner,
            state: Arc::new(Mutex::new(SettlementState {
                phase: SettlementPhase::Active,
                active_operations: 0,
                successful_operations: 0,
                cancelled_operations: 0,
                failed_operations: 0,
                abandoned_operations: 0,
                next_operation_id: 1,
                waker: None,
            })),
            settlement_started: false,
        }
    }

    /// Returns an issuer-visible view of terminal lifecycle state.
    #[must_use]
    pub fn observer(&self) -> SettlementObserver {
        SettlementObserver {
            state: Arc::clone(&self.state),
        }
    }

    /// Transfers one operation release obligation to the caller.
    ///
    /// # Errors
    ///
    /// Returns [`OperationIssueError::Retired`] after settlement has retired
    /// operation admission.
    pub fn issue_operation(&self) -> Result<OperationLease, OperationIssueError> {
        let mut state = lock_state(&self.state);
        if state.phase != SettlementPhase::Active {
            return Err(OperationIssueError::Retired);
        }

        let id = state.next_operation_id;
        state.next_operation_id += 1;
        state.active_operations += 1;
        Ok(OperationLease {
            id,
            state: Some(Arc::clone(&self.state)),
        })
    }

    /// Retires admission and returns completion that waits for quiescence.
    #[must_use = "settlement completion must be observed"]
    pub fn settle(mut self) -> SettlementFuture {
        lock_state(&self.state).phase = SettlementPhase::Settling;
        self.settlement_started = true;
        SettlementFuture {
            owner: self.owner,
            state: Arc::clone(&self.state),
            completed: false,
        }
    }
}

impl Drop for SettlementRoot {
    fn drop(&mut self) {
        if self.settlement_started {
            return;
        }

        let wake = {
            let mut state = lock_state(&self.state);
            state.phase = SettlementPhase::RootAbandoned;
            state.waker.take()
        };
        if let Some(waker) = wake {
            waker.wake();
        }
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum OperationTerminal {
    Succeeded,
    Cancelled,
    Failed,
    Abandoned,
}

/// One operation-owned release obligation.
#[derive(Debug)]
pub struct OperationLease {
    id: u64,
    state: Option<Arc<Mutex<SettlementState>>>,
}

impl OperationLease {
    /// Returns the owner-issued operation identity.
    #[must_use]
    pub fn id(&self) -> u64 {
        self.id
    }

    /// Ends this operation with successful domain completion.
    pub fn succeed(mut self) {
        self.finish(OperationTerminal::Succeeded);
    }

    /// Ends this operation with typed cancellation.
    pub fn cancel(mut self) {
        self.finish(OperationTerminal::Cancelled);
    }

    /// Ends this operation with typed failure.
    pub fn fail(mut self) {
        self.finish(OperationTerminal::Failed);
    }

    fn finish(&mut self, terminal: OperationTerminal) {
        let Some(state) = self.state.take() else {
            return;
        };
        finish_operation(&state, terminal);
    }
}

impl Drop for OperationLease {
    fn drop(&mut self) {
        self.finish(OperationTerminal::Abandoned);
    }
}

fn finish_operation(state: &Mutex<SettlementState>, terminal: OperationTerminal) {
    let wake = {
        let mut state = lock_state(state);
        state.active_operations -= 1;
        match terminal {
            OperationTerminal::Succeeded => state.successful_operations += 1,
            OperationTerminal::Cancelled => state.cancelled_operations += 1,
            OperationTerminal::Failed => state.failed_operations += 1,
            OperationTerminal::Abandoned => state.abandoned_operations += 1,
        }
        if state.phase == SettlementPhase::Settling && state.active_operations == 0 {
            state.waker.take()
        } else {
            None
        }
    };
    if let Some(waker) = wake {
        waker.wake();
    }
}

/// Why a root cannot issue another operation.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum OperationIssueError {
    Retired,
}

/// Issuer-visible root lifecycle state.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum SettlementStatus {
    Active,
    Settling,
    Settled,
    Failed,
    RootAbandoned,
    ObservationAbandoned,
}

/// A resource-free issuer view of settlement progress and abandonment.
#[derive(Clone, Debug)]
pub struct SettlementObserver {
    state: Arc<Mutex<SettlementState>>,
}

impl SettlementObserver {
    /// Returns the current issuer-visible lifecycle state.
    #[must_use]
    pub fn status(&self) -> SettlementStatus {
        match lock_state(&self.state).phase {
            SettlementPhase::Active => SettlementStatus::Active,
            SettlementPhase::Settling => SettlementStatus::Settling,
            SettlementPhase::Settled => SettlementStatus::Settled,
            SettlementPhase::Failed => SettlementStatus::Failed,
            SettlementPhase::RootAbandoned => SettlementStatus::RootAbandoned,
            SettlementPhase::ObservationAbandoned => SettlementStatus::ObservationAbandoned,
        }
    }
}

/// Durable evidence that root settlement observed successful quiescence.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct SettlementReceipt {
    owner: &'static str,
    successful_operations: u64,
}

impl SettlementReceipt {
    /// Returns the owner that issued the settled root.
    #[must_use]
    pub fn owner(&self) -> &'static str {
        self.owner
    }

    /// Returns the number of successfully completed operations.
    #[must_use]
    pub fn successful_operations(&self) -> u64 {
        self.successful_operations
    }
}

/// Durable typed evidence that quiescence included non-success.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct SettlementFailureReceipt {
    owner: &'static str,
    successful_operations: u64,
    cancelled_operations: u64,
    failed_operations: u64,
    abandoned_operations: u64,
}

impl SettlementFailureReceipt {
    #[must_use]
    pub fn cancelled_operations(&self) -> u64 {
        self.cancelled_operations
    }

    #[must_use]
    pub fn failed_operations(&self) -> u64 {
        self.failed_operations
    }

    #[must_use]
    pub fn abandoned_operations(&self) -> u64 {
        self.abandoned_operations
    }

    #[must_use]
    pub fn successful_operations(&self) -> u64 {
        self.successful_operations
    }

    #[must_use]
    pub fn owner(&self) -> &'static str {
        self.owner
    }
}

/// Terminal result after quiescence has been observed.
#[derive(Clone, Debug, Eq, PartialEq)]
pub enum SettlementOutcome {
    Settled(SettlementReceipt),
    Failed(SettlementFailureReceipt),
}

/// Awaitable root settlement. Rust cannot run this through `Drop`.
#[must_use = "settlement completion must be observed"]
#[derive(Debug)]
pub struct SettlementFuture {
    owner: &'static str,
    state: Arc<Mutex<SettlementState>>,
    completed: bool,
}

impl Future for SettlementFuture {
    type Output = SettlementOutcome;

    fn poll(self: Pin<&mut Self>, context: &mut Context<'_>) -> Poll<Self::Output> {
        let this = self.get_mut();
        assert!(!this.completed, "settlement polled after completion");

        let incoming_waker = context.waker().clone();
        let mut state = lock_state(&this.state);
        if state.active_operations != 0 {
            let displaced_waker = state.waker.replace(incoming_waker);
            drop(state);
            drop(displaced_waker);
            return Poll::Pending;
        }

        this.completed = true;
        if state.cancelled_operations == 0
            && state.failed_operations == 0
            && state.abandoned_operations == 0
        {
            state.phase = SettlementPhase::Settled;
            Poll::Ready(SettlementOutcome::Settled(SettlementReceipt {
                owner: this.owner,
                successful_operations: state.successful_operations,
            }))
        } else {
            state.phase = SettlementPhase::Failed;
            Poll::Ready(SettlementOutcome::Failed(SettlementFailureReceipt {
                owner: this.owner,
                successful_operations: state.successful_operations,
                cancelled_operations: state.cancelled_operations,
                failed_operations: state.failed_operations,
                abandoned_operations: state.abandoned_operations,
            }))
        }
    }
}

impl Drop for SettlementFuture {
    fn drop(&mut self) {
        if self.completed {
            return;
        }
        let abandoned_waker = {
            let mut state = lock_state(&self.state);
            state.phase = SettlementPhase::ObservationAbandoned;
            state.waker.take()
        };
        drop(abandoned_waker);
    }
}

#[cfg(test)]
mod tests {
    use std::future::Future;
    use std::pin::Pin;
    use std::sync::{Arc, Mutex, Weak, mpsc};
    use std::task::{Context, Poll, Wake, Waker};
    use std::thread;
    use std::time::Duration;

    use super::{OperationLease, SettlementOutcome, SettlementRoot, SettlementStatus};

    fn poll_once<F: Future>(future: Pin<&mut F>) -> Poll<F::Output> {
        let mut context = Context::from_waker(Waker::noop());
        future.poll(&mut context)
    }

    struct RetainingWake {
        _retained: Arc<()>,
    }

    // The custom no-op owns the retention probe; Waker::noop cannot model it.
    #[allow(clippy::manual_noop_waker)]
    impl Wake for RetainingWake {
        fn wake(self: Arc<Self>) {}
    }

    struct OperationWake {
        _operation: Mutex<Option<OperationLease>>,
    }

    // The custom no-op owns the operation released by waker replacement.
    #[allow(clippy::manual_noop_waker)]
    impl Wake for OperationWake {
        fn wake(self: Arc<Self>) {}
    }

    #[test]
    fn settlement_waits_for_every_operation() {
        let root = SettlementRoot::new("package-source");
        let first = root.issue_operation().unwrap();
        let second = root.issue_operation().unwrap();
        let observer = root.observer();
        let mut settlement = Box::pin(root.settle());

        assert!(poll_once(settlement.as_mut()).is_pending());
        first.succeed();
        assert!(poll_once(settlement.as_mut()).is_pending());
        second.succeed();

        let Poll::Ready(SettlementOutcome::Settled(receipt)) = poll_once(settlement.as_mut())
        else {
            panic!("settlement did not produce successful evidence");
        };
        assert_eq!(receipt.owner(), "package-source");
        assert_eq!(receipt.successful_operations(), 2);
        assert_eq!(observer.status(), SettlementStatus::Settled);
    }

    #[test]
    fn operation_outcome_is_distinct_from_release_and_quiescence() {
        let root = SettlementRoot::new("package-source");
        let operation = root.issue_operation().unwrap();
        let observer = root.observer();
        let mut settlement = Box::pin(root.settle());

        operation.cancel();

        let Poll::Ready(SettlementOutcome::Failed(receipt)) = poll_once(settlement.as_mut()) else {
            panic!("cancellation did not produce non-success evidence");
        };
        assert_eq!(receipt.cancelled_operations(), 1);
        assert_eq!(receipt.successful_operations(), 0);
        assert_eq!(observer.status(), SettlementStatus::Failed);
    }

    #[test]
    fn dropped_settlement_observation_remains_visible() {
        let root = SettlementRoot::new("package-source");
        let operation = root.issue_operation().unwrap();
        let observer = root.observer();
        let mut settlement = Box::pin(root.settle());

        let retained = Arc::new(());
        let weak: Weak<()> = Arc::downgrade(&retained);
        let waker = Waker::from(Arc::new(RetainingWake {
            _retained: Arc::clone(&retained),
        }));
        drop(retained);

        {
            let mut context = Context::from_waker(&waker);
            assert!(matches!(
                Pin::new(&mut settlement).poll(&mut context),
                Poll::Pending
            ));
        }
        drop(waker);
        assert!(weak.upgrade().is_some());

        drop(settlement);
        operation.succeed();

        assert_eq!(observer.status(), SettlementStatus::ObservationAbandoned);
        assert!(
            weak.upgrade().is_none(),
            "resource-free observation retained the abandoned task waker"
        );
    }

    #[test]
    fn replacing_pending_waker_releases_displaced_task_outside_lock() {
        let (completed_tx, completed_rx) = mpsc::channel();
        let worker = thread::spawn(move || {
            let root = SettlementRoot::new("package-source");
            let operation = root.issue_operation().unwrap();
            let mut settlement = Box::pin(root.settle());
            let operation_waker = Waker::from(Arc::new(OperationWake {
                _operation: Mutex::new(Some(operation)),
            }));

            {
                let mut context = Context::from_waker(&operation_waker);
                assert!(matches!(
                    Pin::new(&mut settlement).poll(&mut context),
                    Poll::Pending
                ));
            }
            drop(operation_waker);

            assert!(poll_once(settlement.as_mut()).is_pending());
            let Poll::Ready(SettlementOutcome::Failed(receipt)) = poll_once(settlement.as_mut())
            else {
                panic!("abandoned operation did not produce non-success");
            };
            completed_tx.send(receipt.abandoned_operations()).unwrap();
        });

        let abandoned = completed_rx
            .recv_timeout(Duration::from_secs(2))
            .expect("waker replacement deadlocked operation release");
        worker.join().unwrap();
        assert_eq!(abandoned, 1);
    }

    #[test]
    fn omitted_settlement_remains_visible() {
        let root = SettlementRoot::new("package-source");
        let observer = root.observer();

        drop(root);

        assert_eq!(observer.status(), SettlementStatus::RootAbandoned);
    }
}
