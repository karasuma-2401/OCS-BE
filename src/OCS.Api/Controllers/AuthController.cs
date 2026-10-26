// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with the thin HTTP Auth controller.
// Use route `/api/v1/auth`; make each anonymous operation explicit: customer login; internal login;
// registration request OTP; registration verify OTP; registration complete; reset request/complete;
// refresh; and recovery request. Authenticated operations include me, logout, logout-all, password
// change and email-change flow. Apply [AllowAnonymous] only to public flow steps and [Authorize] to
// session/account operations. Bind API DTOs, call IAuthService and map Result<T> with ToActionResult.
// Set Cache-Control: no-store on all token/grant/password/OTP responses. Never inspect DbContext,
// sign JWT, send email or decide group permissions in this controller.
