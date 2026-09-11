//! Live package payload and durable evidence remain distinct.

use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};

#[derive(Debug)]
struct PackagePayloadCorrespondence;

/// The focused owner that issues corresponding payload and evidence values.
#[derive(Debug)]
pub struct PackagePayloadIssuer {
    next_generation: AtomicU64,
}

impl PackagePayloadIssuer {
    /// Creates an owner whose first issued generation is one.
    #[must_use]
    pub fn new() -> Self {
        Self {
            next_generation: AtomicU64::new(1),
        }
    }

    /// Issues one live payload and its exact detached result evidence.
    #[must_use]
    pub fn issue(
        &self,
        coordinate: impl Into<String>,
        bytes: Vec<u8>,
    ) -> (PackageHouseResult, PackagePayload) {
        let generation = self.next_generation.fetch_add(1, Ordering::Relaxed);
        let correspondence = Arc::new(PackagePayloadCorrespondence);
        let receipt = PackageHouseReceipt {
            coordinate: coordinate.into(),
            generation,
            correspondence: Arc::clone(&correspondence),
        };
        (
            PackageHouseResult { receipt },
            PackagePayload {
                generation,
                correspondence,
                bytes: bytes.into_boxed_slice(),
            },
        )
    }
}

impl Default for PackagePayloadIssuer {
    fn default() -> Self {
        Self::new()
    }
}

/// Durable resource-free evidence for one package acquisition.
#[derive(Clone, Debug)]
pub struct PackageHouseReceipt {
    coordinate: String,
    generation: u64,
    correspondence: Arc<PackagePayloadCorrespondence>,
}

impl PackageHouseReceipt {
    /// Returns the settled coordinate.
    #[must_use]
    pub fn coordinate(&self) -> &str {
        &self.coordinate
    }

    /// Returns the acquired payload generation.
    #[must_use]
    pub fn generation(&self) -> u64 {
        self.generation
    }
}

impl PartialEq for PackageHouseReceipt {
    fn eq(&self, other: &Self) -> bool {
        self.coordinate == other.coordinate
            && self.generation == other.generation
            && Arc::ptr_eq(&self.correspondence, &other.correspondence)
    }
}

impl Eq for PackageHouseReceipt {}

/// One terminal scenario result carrying durable evidence.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct PackageHouseResult {
    receipt: PackageHouseReceipt,
}

impl PackageHouseResult {
    /// Returns durable evidence without exposing a live payload.
    #[must_use]
    pub fn receipt(&self) -> &PackageHouseReceipt {
        &self.receipt
    }
}

/// One independently owned live package payload.
#[derive(Debug)]
pub struct PackagePayload {
    generation: u64,
    correspondence: Arc<PackagePayloadCorrespondence>,
    bytes: Box<[u8]>,
}

impl PackagePayload {
    /// Returns the live payload bytes.
    #[must_use]
    pub fn bytes(&self) -> &[u8] {
        &self.bytes
    }
}

#[derive(Debug)]
enum PackageHouseSettlementKind {
    ResourceFree {
        result: PackageHouseResult,
    },
    Acquired {
        result: PackageHouseResult,
        payload: PackagePayload,
    },
}

/// An opaque House result that exposes live payload ownership in one state.
#[derive(Debug)]
pub struct PackageHouseSettlement {
    kind: PackageHouseSettlementKind,
}

impl PackageHouseSettlement {
    /// Creates a resource-free terminal result.
    #[must_use]
    pub fn resource_free(result: PackageHouseResult) -> Self {
        Self {
            kind: PackageHouseSettlementKind::ResourceFree { result },
        }
    }

    /// Transfers a corresponding live payload into the acquired result.
    ///
    /// # Errors
    ///
    /// Returns a typed mismatch when the live payload does not match the
    /// owner-issued acquisition evidence.
    pub fn acquired(
        result: PackageHouseResult,
        payload: PackagePayload,
    ) -> Result<Self, PackageSettlementError> {
        if result.receipt().generation() != payload.generation {
            return Err(PackageSettlementError::GenerationMismatch);
        }
        if !Arc::ptr_eq(&result.receipt().correspondence, &payload.correspondence) {
            return Err(PackageSettlementError::CorrespondenceMismatch);
        }
        Ok(Self {
            kind: PackageHouseSettlementKind::Acquired { result, payload },
        })
    }

    /// Returns the durable evidence common to both states.
    #[must_use]
    pub fn receipt(&self) -> &PackageHouseReceipt {
        match &self.kind {
            PackageHouseSettlementKind::ResourceFree { result }
            | PackageHouseSettlementKind::Acquired { result, .. } => result.receipt(),
        }
    }

    /// Returns the live payload only for an acquired result.
    #[must_use]
    pub fn payload(&self) -> Option<&PackagePayload> {
        match &self.kind {
            PackageHouseSettlementKind::ResourceFree { .. } => None,
            PackageHouseSettlementKind::Acquired { payload, .. } => Some(payload),
        }
    }
}

/// Domain correspondence failure that compiler ownership cannot eliminate.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum PackageSettlementError {
    GenerationMismatch,
    CorrespondenceMismatch,
}

#[cfg(test)]
mod tests {
    use super::{PackageHouseSettlement, PackagePayloadIssuer, PackageSettlementError};

    #[test]
    fn acquired_payload_must_match_generation() {
        let issuer = PackagePayloadIssuer::new();
        let (first_result, _) = issuer.issue("Example@1.0.0", vec![1, 2, 3]);
        let (_, second_payload) = issuer.issue("Example@1.0.0", vec![4, 5, 6]);

        assert!(matches!(
            PackageHouseSettlement::acquired(first_result, second_payload),
            Err(PackageSettlementError::GenerationMismatch)
        ));
    }

    #[test]
    fn acquired_payload_must_match_owner_issued_correspondence() {
        let first_issuer = PackagePayloadIssuer::new();
        let second_issuer = PackagePayloadIssuer::new();
        let (result, _) = first_issuer.issue("Example@1.0.0", vec![1, 2, 3]);
        let (_, payload) = second_issuer.issue("Example@1.0.0", vec![1, 2, 3]);

        assert!(matches!(
            PackageHouseSettlement::acquired(result, payload),
            Err(PackageSettlementError::CorrespondenceMismatch)
        ));
    }

    #[test]
    fn receipt_survives_live_payload_release() {
        let issuer = PackagePayloadIssuer::new();
        let (result, payload) = issuer.issue("Example@1.0.0", vec![1, 2, 3]);
        let settlement = PackageHouseSettlement::acquired(result, payload).unwrap();

        assert_eq!(settlement.payload().unwrap().bytes(), [1, 2, 3]);
        let receipt = settlement.receipt().clone();
        drop(settlement);

        assert_eq!(receipt.coordinate(), "Example@1.0.0");
        assert_eq!(receipt.generation(), 1);
    }
}
