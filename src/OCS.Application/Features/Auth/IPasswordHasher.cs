// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with an Application crypto abstraction.
// Support hash, verify and rehash-needed operations without exposing algorithm configuration to a
// controller. Password policy validation belongs in Application before hashing. Use a strongly typed
// result so invalid password, successful verify and rehash-needed cannot be confused.
// Infrastructure implementation wraps ASP.NET Identity PasswordHasher<Account>; no plaintext data
// may be written to logs, audit events or exceptions.
