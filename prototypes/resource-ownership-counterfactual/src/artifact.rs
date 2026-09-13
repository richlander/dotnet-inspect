//! Generation-bound and revocable artifact access.
//!
//! A content view keeps both the generation and its non-`Clone` lease
//! borrowed, so the lease cannot be released while the view remains live:
//!
//! ```compile_fail
//! use resource_ownership_counterfactual::artifact::ArtifactGeneration;
//!
//! let generation = ArtifactGeneration::new(1, vec![1, 2, 3]);
//! let authorization = generation.authorize();
//! let lease = generation.issue_query_lease(&authorization).unwrap();
//! let view = generation.open_content(&lease).unwrap();
//! drop(lease);
//! assert_eq!(view.bytes().len(), 3);
//! ```

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};

#[derive(Debug)]
struct GenerationState {
    identity: u64,
    content: Box<[u8]>,
    authorization_revision: AtomicU64,
    retired: AtomicBool,
}

/// One source-neutral immutable artifact generation.
#[derive(Debug)]
pub struct ArtifactGeneration {
    state: Arc<GenerationState>,
}

impl ArtifactGeneration {
    /// Creates one generation and its retained content.
    #[must_use]
    pub fn new(identity: u64, content: Vec<u8>) -> Self {
        Self {
            state: Arc::new(GenerationState {
                identity,
                content: content.into_boxed_slice(),
                authorization_revision: AtomicU64::new(0),
                retired: AtomicBool::new(false),
            }),
        }
    }

    /// Issues revocable permission to request a query lease.
    #[must_use]
    pub fn authorize(&self) -> ArtifactAuthorization {
        ArtifactAuthorization {
            state: Arc::clone(&self.state),
            revision: self.state.authorization_revision.load(Ordering::Acquire),
        }
    }

    /// Issues one non-copyable query lease.
    ///
    /// # Errors
    ///
    /// Returns the owner-issued domain failure when the authorization belongs
    /// to another generation, has been revoked, or the generation is retired.
    pub fn issue_query_lease(
        &self,
        authorization: &ArtifactAuthorization,
    ) -> Result<ArtifactQueryLease, ArtifactAccessError> {
        self.validate_authorization(authorization)?;
        Ok(ArtifactQueryLease {
            state: Arc::clone(&self.state),
            revision: authorization.revision,
        })
    }

    /// Borrows retained content under the supplied lease.
    ///
    /// # Errors
    ///
    /// Returns the owner-issued domain failure when the lease belongs to
    /// another generation, has been revoked, or the generation is retired.
    pub fn open_content<'borrow>(
        &'borrow self,
        lease: &'borrow ArtifactQueryLease,
    ) -> Result<ArtifactContentView<'borrow>, ArtifactAccessError> {
        if !Arc::ptr_eq(&self.state, &lease.state) {
            return Err(ArtifactAccessError::WrongGeneration);
        }
        if self.state.retired.load(Ordering::Acquire) {
            return Err(ArtifactAccessError::Retired);
        }
        if lease.revision != self.state.authorization_revision.load(Ordering::Acquire) {
            return Err(ArtifactAccessError::Revoked);
        }

        Ok(ArtifactContentView {
            content: &self.state.content,
        })
    }

    /// Rejects subsequent issuance and opens under the current revision.
    pub fn revoke_authorizations(&self) {
        self.state
            .authorization_revision
            .fetch_add(1, Ordering::AcqRel);
    }

    /// Rejects subsequent issuance and opens for this generation.
    pub fn retire(&self) {
        self.state.retired.store(true, Ordering::Release);
    }

    fn validate_authorization(
        &self,
        authorization: &ArtifactAuthorization,
    ) -> Result<(), ArtifactAccessError> {
        if !Arc::ptr_eq(&self.state, &authorization.state) {
            return Err(ArtifactAccessError::WrongGeneration);
        }
        if self.state.retired.load(Ordering::Acquire) {
            return Err(ArtifactAccessError::Retired);
        }
        if authorization.revision != self.state.authorization_revision.load(Ordering::Acquire) {
            return Err(ArtifactAccessError::Revoked);
        }
        Ok(())
    }

    /// Returns the owner-issued generation identity.
    #[must_use]
    pub fn identity(&self) -> u64 {
        self.state.identity
    }
}

/// Revocable permission to request an artifact query lease.
#[derive(Clone, Debug)]
pub struct ArtifactAuthorization {
    state: Arc<GenerationState>,
    revision: u64,
}

/// One release obligation for query access.
#[derive(Debug)]
pub struct ArtifactQueryLease {
    state: Arc<GenerationState>,
    revision: u64,
}

/// A read-only view tied to a live generation and lease borrow.
#[derive(Clone, Copy, Debug)]
pub struct ArtifactContentView<'borrow> {
    content: &'borrow [u8],
}

impl<'borrow> ArtifactContentView<'borrow> {
    /// Returns the borrowed content.
    #[must_use]
    pub fn bytes(self) -> &'borrow [u8] {
        self.content
    }
}

/// Domain validation that remains necessary under compiler ownership.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ArtifactAccessError {
    WrongGeneration,
    Revoked,
    Retired,
}

#[cfg(test)]
mod tests {
    use super::{ArtifactAccessError, ArtifactGeneration};

    #[test]
    fn wrong_generation_remains_a_runtime_domain_check() {
        let first = ArtifactGeneration::new(1, vec![1]);
        let second = ArtifactGeneration::new(2, vec![2]);
        let authorization = first.authorize();
        let lease = first.issue_query_lease(&authorization).unwrap();

        assert!(matches!(
            second.open_content(&lease),
            Err(ArtifactAccessError::WrongGeneration)
        ));
    }

    #[test]
    fn revocation_rejects_future_access_but_not_an_admitted_view() {
        let generation = ArtifactGeneration::new(1, vec![1, 2, 3]);
        let authorization = generation.authorize();
        let lease = generation.issue_query_lease(&authorization).unwrap();
        let admitted = generation.open_content(&lease).unwrap();

        generation.revoke_authorizations();

        assert_eq!(admitted.bytes(), [1, 2, 3]);
        assert!(matches!(
            generation.open_content(&lease),
            Err(ArtifactAccessError::Revoked)
        ));
    }

    #[test]
    fn retirement_remains_runtime_state() {
        let generation = ArtifactGeneration::new(1, vec![1, 2, 3]);
        let authorization = generation.authorize();
        let lease = generation.issue_query_lease(&authorization).unwrap();

        generation.retire();

        assert!(matches!(
            generation.open_content(&lease),
            Err(ArtifactAccessError::Retired)
        ));
    }
}
