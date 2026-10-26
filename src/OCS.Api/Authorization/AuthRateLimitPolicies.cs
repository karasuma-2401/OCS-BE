// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with named ASP.NET rate-limit policies.
// Apply separate limits for login, OTP request, OTP verify, password reset and refresh; partition by a
// privacy-preserving combination of normalized target/remote signal according to the plan. Return the
// documented 429/Retry-After behaviour. Rate limiting complements persistent OTP attempt/cooldown
// state and must not become a memory-only security source in a multi-instance deployment.
