// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with IAuthSessionValidator implementation.
// Load the session/account/current authorization state from PostgreSQL (source of truth) for each
// protected request after cryptographic JWT validation. Reject mismatch of `sub`/`sid`, locked or
// deactivated account, revoke, absolute expiry and idle expiry. Selected business requests update
// activity; refresh/poll/heartbeat do not. Add cache only after a correct invalidation design exists.
