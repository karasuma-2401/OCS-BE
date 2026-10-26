// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with IAuthorizationGroupRepository.
// Query existing Role/Permission/AccountRole/RolePermission as authorization group data; persist any
// delegation model added by the Auth migration. Retrieve effective permissions from current memberships
// and filter duplicates. Use transactions/concurrency protection where edits replace a permission set.
// Do not decide policy from Role.Code/Name and do not let callers compose unrestricted IQueryable.
