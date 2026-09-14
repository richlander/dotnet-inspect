use std::future::Future;
use std::pin::Pin;
use std::task::{Context, Poll, Waker};

use resource_ownership_counterfactual::artifact::{ArtifactAccessError, ArtifactGeneration};
use resource_ownership_counterfactual::package::{PackageHouseSettlement, PackagePayloadIssuer};
use resource_ownership_counterfactual::session::AssemblyInspectionSession;
use resource_ownership_counterfactual::settlement::{SettlementOutcome, SettlementRoot};

fn poll_once<F: Future>(future: Pin<&mut F>) -> Poll<F::Output> {
    let mut context = Context::from_waker(Waker::noop());
    future.poll(&mut context)
}

fn main() {
    println!("Rust 2024 resource ownership counterfactual");

    let session = AssemblyInspectionSession::open(vec![1, 2, 3, 4]);
    let detached_bytes = session.snapshot(|view| view.bytes().len());
    println!("session: detached snapshot = {detached_bytes} bytes");

    let generation = ArtifactGeneration::new(42, b"artifact".to_vec());
    let authorization = generation.authorize();
    let lease = generation.issue_query_lease(&authorization).unwrap();
    let borrowed_bytes = generation.open_content(&lease).unwrap().bytes().len();
    generation.revoke_authorizations();
    assert!(matches!(
        generation.open_content(&lease),
        Err(ArtifactAccessError::Revoked)
    ));
    println!(
        "artifact: scoped borrow = {borrowed_bytes} bytes; \
         revocation remains runtime state"
    );

    let root = SettlementRoot::new("package-source");
    let operation = root.issue_operation().unwrap();
    let mut settlement = Box::pin(root.settle());
    assert!(poll_once(settlement.as_mut()).is_pending());
    operation.succeed();
    let Poll::Ready(SettlementOutcome::Settled(receipt)) = poll_once(settlement.as_mut()) else {
        panic!("settlement remained pending after operation release");
    };
    println!(
        "settlement: pending with active work; completed operations = {}",
        receipt.successful_operations()
    );

    let issuer = PackagePayloadIssuer::new();
    let (result, payload) = issuer.issue("Example@1.0.0", vec![1, 2, 3]);
    let acquired = PackageHouseSettlement::acquired(result, payload).unwrap();
    let durable_receipt = acquired.receipt().clone();
    drop(acquired);
    assert_eq!(durable_receipt.generation(), 1);
    println!("package: durable receipt survives release of the live payload");
}
