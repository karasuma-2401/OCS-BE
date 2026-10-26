// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with IAuthRepository implementation.
// Use the scoped AppDbContext and express normalized account/session/challenge/grant queries here.
// Ensure refresh consume+replacement uses a database transaction or conditional update that permits
// exactly one winner under concurrency. Put indexes/unique constraints in EF configuration, not code.
// Do not call SaveChanges here; AuthService controls one IUnitOfWork save per use case. Avoid raw
// account-existence errors that reveal whether a phone/email is registered.
