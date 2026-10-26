// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the Application interface.
// Define use cases to list/create/update/deactivate authorization groups; assign catalog permissions;
// add/remove account membership; and manage delegation. Every mutation receives actor context from
// ICurrentAccount/validated principal, checks delegation and writes an audit/security event.
// "Role" remains the physical persisted model for migration compatibility; public/use-case language
// says authorization group. No operation decides access by a hard-coded group name.
