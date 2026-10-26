// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with HTTP integration tests.
// Host API in Testing with an IEmailSender fake and deterministic token/session services. Assert the
// published contract for public Auth routes: request/verify/complete registration; phone login;
// refresh/logout; password reset; 429 OTP limits; 400 validation; no-store headers; and ProblemDetails.
// Include real bearer validation tests: missing/tampered/expired token -> 401, revoked session -> 401,
// active token after permission removal -> 403, and no refresh/grant/OTP/password appears in responses.
