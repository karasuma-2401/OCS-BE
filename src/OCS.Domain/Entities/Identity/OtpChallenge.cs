// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the Domain entity.
// Target responsibility: a short-lived OTP challenge bound immutably to purpose and recipient.
// Include nullable AccountId only when the flow already has an account; purpose; normalized email;
// normalized phone/proposed value where applicable; CodeHmac (or equivalent keyed hash); creation,
// expiry, attempts, consumed timestamp, send status and safe provider correlation id.
// Registration binds both proposed phone and email. Password reset binds the existing AccountId
// and its currently verified email. Changing any bound value creates a new challenge.
// Never persist/log the plaintext OTP or expose it through DTO, audit or exception messages.
