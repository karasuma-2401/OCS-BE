// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with IEntityTypeConfiguration<RefreshCredential>.
// Enforce unique TokenHash, required SessionId and index session/expiry/consumed state. TokenHash length
// must fit the selected encoded SHA-256 representation. Map rotation/replacement relation explicitly
// without cascade paths that erase reuse-detection history.
