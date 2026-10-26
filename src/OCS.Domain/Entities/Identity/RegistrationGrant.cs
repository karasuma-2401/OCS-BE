// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the Domain entity.
// Target responsibility: one-time, narrowly-scoped credential issued after registration OTP proof.
// Include ChallengeId, normalized phone/email, VerifiedAt, TokenHash, ExpiresAt, ConsumedAt,
// RevokedAt and a fixed CompleteRegistration purpose. The raw random secret exists only in the
// short API response after verification; store its hash and compare in constant time as needed.
// It is not a JWT, refresh token, access token or authenticated session. It cannot call `/me`,
// change contacts, reset password, or receive arbitrary privileges. Consume it atomically when
// creating account, password, Consignor membership and first session.
