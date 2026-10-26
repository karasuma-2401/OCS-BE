// AUTH IMPLEMENTATION SCAFFOLD — replace this comment-only file with IEmailSender implementation.
// Establish SMTP with STARTTLS before authentication, send the fixed OCS OTP template and return only
// a safe provider correlation/result. Use timeout/cancellation handling and classify retryable versus
// permanent failures without exposing SMTP dialogue. Do not disable TLS verification or use port 465
// and STARTTLS together. A successful SMTP response is not proof that the recipient verified OTP.
// Gmail is used only for intentional local smoke testing after an App Password is configured; tests
// inject an in-memory IEmailSender fake and never send an external email.
