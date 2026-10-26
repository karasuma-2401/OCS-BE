// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the Application use-case service.
// Orchestrate validation, normalization, repository calls, password verification, OTP/grant logic,
// session issuance/revocation, security-event writing and a single unit-of-work boundary per flow.
// Inject interfaces plus TimeProvider; do not inject DbContext, HttpContext, ControllerBase, SMTP
// client or JwtSecurityTokenHandler. Return stable AUTH.* ApplicationErrors for expected failures.
// Error responses for login/reset must avoid account enumeration. Commit a state transition before
// emitting an email when durable outbox/retry is introduced; do not create duplicate OTPs on retry.
