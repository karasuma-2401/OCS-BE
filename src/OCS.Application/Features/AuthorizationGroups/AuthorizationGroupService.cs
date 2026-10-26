// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the Application use-case service.
// Validate immutable catalog permission codes, protected system group rules, no-financial-permission
// invariant for the seeded Admin and anti-self-escalation. Load all affected state atomically, apply
// change, save once and invalidate/revoke impacted sessions/authorization state before returning.
// Do not make a user-created group trusted simply because it has a familiar display name.
