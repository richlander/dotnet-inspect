//! Synchronous owned and borrowed assembly inspection.
//!
//! Moving an owned session invalidates the source binding:
//!
//! ```compile_fail
//! use resource_ownership_counterfactual::session::AssemblyInspectionSession;
//!
//! let session = AssemblyInspectionSession::open(vec![1, 2, 3]);
//! let moved = session;
//! assert_eq!(session.byte_count(), 3);
//! drop(moved);
//! ```
//!
//! A borrowed session cannot outlive its lender:
//!
//! ```compile_fail
//! use resource_ownership_counterfactual::session::PdbContext;
//!
//! let context = PdbContext::new(vec![1, 2, 3]);
//! let session = context.borrow_session();
//! drop(context);
//! assert_eq!(session.byte_count(), 3);
//! ```
//!
//! A snapshot may return detached data, but not the borrowed bytes:
//!
//! ```compile_fail
//! use resource_ownership_counterfactual::session::AssemblyInspectionSession;
//!
//! let session = AssemblyInspectionSession::open(vec![1, 2, 3]);
//! let escaped = session.snapshot(|view| view.bytes());
//! drop(session);
//! assert_eq!(escaped.len(), 3);
//! ```

#[derive(Debug)]
struct AssemblyImage {
    bytes: Box<[u8]>,
}

impl AssemblyImage {
    fn new(bytes: Vec<u8>) -> Self {
        Self {
            bytes: bytes.into_boxed_slice(),
        }
    }
}

#[derive(Debug)]
enum SessionImage<'lender> {
    Owned(AssemblyImage),
    Borrowed(&'lender AssemblyImage),
}

/// A synchronous owner or lender-bound borrower of one assembly image.
#[derive(Debug)]
pub struct AssemblyInspectionSession<'lender> {
    image: SessionImage<'lender>,
}

impl AssemblyInspectionSession<'static> {
    /// Opens one independently owned image.
    #[must_use]
    pub fn open(bytes: Vec<u8>) -> Self {
        Self {
            image: SessionImage::Owned(AssemblyImage::new(bytes)),
        }
    }
}

impl AssemblyInspectionSession<'_> {
    /// Returns the image byte count without exposing the image.
    #[must_use]
    pub fn byte_count(&self) -> usize {
        self.image().bytes.len()
    }

    /// Runs one synchronous read-only borrow and returns detached data.
    pub fn snapshot<R>(&self, callback: impl for<'borrow> FnOnce(SessionView<'borrow>) -> R) -> R {
        callback(SessionView {
            image: self.image(),
        })
    }

    fn image(&self) -> &AssemblyImage {
        match &self.image {
            SessionImage::Owned(image) => image,
            SessionImage::Borrowed(image) => image,
        }
    }
}

/// An owner of an image that may lend it to an inspection session.
#[derive(Debug)]
pub struct PdbContext {
    image: AssemblyImage,
}

impl PdbContext {
    /// Creates one image owner.
    #[must_use]
    pub fn new(bytes: Vec<u8>) -> Self {
        Self {
            image: AssemblyImage::new(bytes),
        }
    }

    /// Borrows the image for no longer than this context remains live.
    #[must_use]
    pub fn borrow_session(&self) -> AssemblyInspectionSession<'_> {
        AssemblyInspectionSession {
            image: SessionImage::Borrowed(&self.image),
        }
    }
}

/// A read-only assembly view whose lifetime is tied to one callback borrow.
#[derive(Clone, Copy, Debug)]
pub struct SessionView<'borrow> {
    image: &'borrow AssemblyImage,
}

impl<'borrow> SessionView<'borrow> {
    /// Exposes bytes only for the callback borrow lifetime.
    #[must_use]
    pub fn bytes(self) -> &'borrow [u8] {
        &self.image.bytes
    }
}

#[cfg(test)]
mod tests {
    use super::{AssemblyInspectionSession, PdbContext};

    #[test]
    fn owned_session_returns_detached_snapshot() {
        let session = AssemblyInspectionSession::open(vec![1, 2, 3, 4]);

        let detached = session.snapshot(|view| view.bytes().to_vec());
        drop(session);

        assert_eq!(detached, [1, 2, 3, 4]);
    }

    #[test]
    fn borrowed_session_reads_lender_image() {
        let context = PdbContext::new(vec![1, 2, 3]);
        let session = context.borrow_session();

        assert_eq!(session.byte_count(), 3);
        drop(session);
        drop(context);
    }
}
