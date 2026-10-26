// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with a focused DI registration extension.
// Register Auth repositories/services with the same scoped lifetime as AppDbContext; register options
// using validation; and map IEmailSender to Gmail only outside test hosts. Call it from existing
// Infrastructure.DependencyInjection after database registration. Keep service wiring out of Program.
// Tests must override interfaces with fakes without contacting Gmail, Redis or an external database.
