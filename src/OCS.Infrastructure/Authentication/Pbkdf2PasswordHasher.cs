// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with IPasswordHasher implementation.
// Wrap PasswordHasher<Account> using PBKDF2-HMAC-SHA512 with iteration count no lower than 210,000.
// Keep the configured work factor explicit and cover old-hash rehash behaviour with tests. The class
// must accept/return only Application abstraction types; it never validates HTTP DTOs or logs input.
// Do not use reversible encryption, SHA-256/MD5 directly, a static salt, or an application-wide salt.
