// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with an Application token abstraction.
// It receives an already-authorized account/session context and produces a short-lived signed access
// token with subject, session id and minimal stable claims. Expiry is server policy (15 minutes),
// not a client argument. Do not embed the full permission list or refresh token in the JWT.
// Infrastructure implementation reads validated issuer/audience/signing configuration only.
