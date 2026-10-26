// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the Domain entity.
// Target responsibility: data-driven guardrails for dynamic authorization groups. Model which
// actor group/permission may manage which target group and which actions (membership, permission
// assignment, group edit, recovery approval). Include stable IDs and the minimum state needed to
// disable/revoke a delegation safely. Design uniqueness to forbid duplicate active grants.
// Validate delegation before every group mutation. It must prevent self-escalation, grants to
// protected system groups, and any path that gives the seeded Admin financial permissions.
