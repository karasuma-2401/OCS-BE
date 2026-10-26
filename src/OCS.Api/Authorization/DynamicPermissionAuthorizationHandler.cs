// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the authorization handler.
// First rely on validated JWT/session principal, then ask a server-side effective-permission service
// for the current account/session. Succeed only for an active membership grant. A removed permission,
// locked account or revoked session must deny on the next request. Keep business ownership/scope
// checks in Application query/use-case logic; policy answers only the operation-level permission.
