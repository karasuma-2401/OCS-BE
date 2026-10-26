// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the Domain entity.
// Target responsibility: one link in a rotating refresh-token family for exactly one AuthSession.
// Persist only TokenHash (SHA-256 of a cryptographically random 256-bit-or-more secret), never
// the bearer secret. Include Id, SessionId, CreatedAt, ExpiresAt, ConsumedAt, RevokedAt and
// ReplacedById/navigation so a later replay can be detected after a successful rotation.
// Keep tombstones/history long enough for reuse detection. Do not physically delete a consumed
// token immediately. Application/Infrastructure owns transaction and conditional-update logic.
