// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with an Application request-auth abstraction.
// Given the authenticated JWT subject/session id and current time, verify account status, session
// revocation, absolute/idle expiry and authorization-version/current membership requirements.
// It returns a minimal current-principal context or a denial reason safe to map to 401/403. This is
// called by API authentication/authorization plumbing for every protected request, not only refresh.
