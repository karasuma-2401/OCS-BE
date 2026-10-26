// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with an Application outbound-email abstraction.
// Offer a narrow operation for the approved OTP message/template, recipient and correlation id.
// Do not expose a generic arbitrary-from/arbitrary-html mail API to controllers. Implementations
// must report a safe delivery outcome and never return SMTP credentials or raw provider transcripts.
// Tests substitute this interface with an in-memory fake and assert recipient/purpose without Gmail.
