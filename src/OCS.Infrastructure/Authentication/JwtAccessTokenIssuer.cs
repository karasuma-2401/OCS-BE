// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with IAccessTokenIssuer implementation.
// Validate issuer, audience and >=32-byte signing key at startup for Production. Use the configured
// signing algorithm consistently with JwtBearer validation. Issue a 15-minute token with UTC times,
// non-forgeable `sub` and `sid`, issuer/audience and only claims required to identify the principal.
// Do not issue refresh tokens as JWTs, add password/group permission lists to claims, or trust client
// supplied account/session identifiers. Do not log the serialized token.
