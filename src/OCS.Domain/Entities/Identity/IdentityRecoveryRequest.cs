// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the Domain entity.
// Target responsibility: controlled claim/recovery for a legacy profile without trusted email,
// or a phone/email change that cannot be self-served. Keep target Account/Profile, requested
// contacts, requester/account context when present, status, manager decision, timestamps and
// audit-safe reason/evidence reference. Do not copy sensitive evidence into free-form logs.
// The request must preserve the existing AccountId and business history; approval does not create
// a duplicate account. Manager authority comes from dynamic permission + delegation, not name.
