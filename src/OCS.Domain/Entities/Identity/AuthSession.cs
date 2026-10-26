// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the Domain entity.
// Target responsibility: one authenticated login on one client/device; its Id becomes JWT `sid`.
// Include AccountId, client/session policy kind, CreatedAt, LastRefreshedAt, LastActivityAt,
// AbsoluteExpiresAt, IdleExpiresAt, RevokedAt, revocation reason, bounded safe device metadata
// and an optimistic concurrency token. Define a pure-domain predicate for active/expired/revoked.
// Do not store access JWT or plaintext refresh token here. Do not depend on EF, ASP.NET, SMTP,
// HttpContext or a clock singleton. Time is supplied by the Application service.
// A refresh/heartbeat/poll must not silently extend idle expiry; only selected business activity may.
