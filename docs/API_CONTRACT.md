# OCS API Contract

Tài liệu này là đặc tả HTTP contract chuẩn giữa Web frontend OCS và backend API (`OCS.Api`). Hệ thống xác thực và quản lý phiên đã được hoàn thiện đầy đủ qua các Checkpoint CP1–CP8 và nghiệm thu tại CP9.

### Trạng thái triển khai thực tế

| Nhóm | Hiện trạng triển khai | Ghi chú & Ràng buộc |
| --- | --- | --- |
| Auth | Hoàn thành đầy đủ: Đăng nhập Email + Mật khẩu, Đăng ký + Email OTP kích hoạt, HttpOnly Cookie (`ocs_session`), Hồ sơ phiên server (`AuthSession`), Bảo vệ CSRF (`X-XSRF-TOKEN`), Strict ClockSkew UTC, Anti-enumeration | Tuyệt đối không dùng login username/SĐT hay SMS OTP; token không lưu tại client storage |
| Products | `GET /api/products` (Công khai Landing Page); `/api/products/{id}`, `/api/products/barcode/*`, `POST /api/products` (Nội bộ Admin/Manager/Staff) | Public response chỉ là projection an toàn; tuyệt đối không lộ dữ liệu người ký gửi |
| Health | `/api/health`, `/api/health/live` | Giữ nguyên |

---

### Các giới hạn nghiệp vụ được ghi nhận và chấp nhận

1. **Số điện thoại & Hồ sơ:** Số điện thoại lưu tại `Account.PhoneNumber` đóng vai trò thông tin liên hệ ký gửi, chưa được xác minh quyền sở hữu qua SMS OTP. Khi xác thực OTP email thành công, chỉ đặt `EmailVerifiedAt` và `ActivatedAt`, tuyệt đối giữ nguyên `PhoneVerifiedAt`.
2. **Không tự cấp quyền từ số điện thoại:** Tài khoản đăng ký mới trùng số điện thoại với hồ sơ cũ nhưng khác email không được tự động gộp hoặc chuyển quyền sở hữu mà chưa có chứng thực.
3. **Phạm vi tính năng Auth:** Bản phát hành hiện tại chưa bao gồm Forgot password (quên mật khẩu), Đổi email/SĐT, Multi-Factor Authentication (MFA / Passkey), Refresh Token xoay vòng (rotating refresh token), hay giao diện quản trị thu hồi đa phiên từ xa.
4. **Thu hồi phiên hiện tại:** Được đảm bảo an toàn tuyệt đối qua `POST /api/auth/logout`: đánh dấu `RevokedAt` trên bản ghi `AuthSession` của database và xóa cookie `ocs_session` (`Path=/api; HttpOnly; SameSite=Lax; Expires=Thu, 01 Jan 1970 00:00:00 GMT`). Token hoặc session đã thu hồi bị từ chối `401 UNAUTHORIZED` ngay lập tức.

---

## 1. Phạm vi, phiên bản và định dạng

### 1.1. Nhóm endpoint và tiền tố định tuyến
- **Operational endpoints (Vận hành):** Giữ tiền tố `/api/health` và `/api/health/live`.
- **Authentication endpoints (Xác thực):** Giữ tiền tố `/api/auth/*` cho đợt này để đảm bảo tính ổn định và tương thích giữa Backend và Frontend.
- **Business endpoints (Nghiệp vụ):** Dùng tiền tố `/api/*` hiện hành; project này không version hóa route bằng `v1`.

### 1.2. Quy ước dữ liệu
- JSON sử dụng quy ước đặt tên **camelCase** cho các trường dữ liệu và mã hóa UTF-8.
- Khóa chính định danh tài nguyên (Resource ID) sử dụng định dạng chuỗi **UUID/GUID** (`Guid.ToString()`).
- Mốc thời gian (Timestamps) tuân theo chuẩn **ISO 8601 UTC** với hậu tố `Z` (ví dụ: `2026-11-03T09:30:00Z`).
- Ngày tháng sử dụng `YYYY-MM-DD`, thời gian sử dụng `HH:mm:ss`.

### 1.3. Giá trị tiền tệ và tỷ lệ
- Giá trị tiền tệ (`listedPrice`, `soldAmount`, v.v.) không dùng số thực nhị phân (float/double) để tránh sai số. Backend dùng `decimal` / PostgreSQL `numeric`.
- Trên API, giá trị tiền tệ được serialize thành chuỗi thập phân với đúng 2 chữ số phần thập phân, ví dụ: `"250000.00"`.
- Tỷ lệ hoa hồng serialize thành chuỗi thập phân tối đa 2 chữ số phần thập phân, ví dụ: `"17.50"`.

---

## 2. Tiêu chuẩn phản hồi và mã lỗi (Baseline)

### 2.1. Mã nguồn thực tế trong backend
- Toàn bộ backend được tổ chức trong project `OCS.Api`.
- Lỗi nghiệp vụ sử dụng `OCS.Api.Common.ApplicationError` với `Code`, `Message`, và `ErrorType`.
- Controllers chuyển đổi `Result<T>` thành `IActionResult` thông qua extension `ResultExtensions.ToActionResult()`.
- Danh sách mã lỗi chuẩn: `OCS.Api.Common.ApiErrorCodes` và các hằng số lỗi theo module (ví dụ `AUTH.*`).
- Định dạng phân trang chuẩn: `OCS.Api.Common.PagedRequest` và `PagedResponse<T>`.

### 2.2. Định dạng lỗi ProblemDetails (RFC 7807)
Mọi phản hồi lỗi (4xx, 5xx) đều có `Content-Type: application/problem+json` và bao gồm `code` ổn định cùng `traceId` để tra cứu log:

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "code": "VALIDATION_FAILED",
  "traceId": "00-7c0a1b2c3d4e5f-01",
  "errors": {
    "email": ["Địa chỉ email không đúng định dạng."],
    "password": ["Mật khẩu phải chứa ít nhất một chữ hoa, một chữ thường và một chữ số."]
  }
}
```

---

## 3. Chính sách xác thực và quản lý phiên (Authentication & Session)

### 3.1. Định danh và tài khoản
- **Đăng nhập bằng Email + Mật khẩu:** Áp dụng thống nhất cho cả tài khoản Khách hàng (Customer) và Nội bộ (Admin, Manager, Staff).
- **Số điện thoại:** Là thông tin hồ sơ phục vụ liên hệ và nghiệp vụ ký gửi, hiện lưu ở `Account.PhoneNumber`; chưa được xác thực quyền sở hữu qua SMS. OTP email chỉ đặt `EmailVerifiedAt`, không đặt `PhoneVerifiedAt`.
- **Chuẩn hóa Email:** Email được chuẩn hóa khi đăng ký, tìm kiếm đăng nhập và kiểm tra trùng lặp: `NormalizedEmail = email.Trim().ToUpperInvariant()`. Email gốc của người dùng được lưu trữ nguyên vẹn để hiển thị.
- **Tính duy nhất (Unique):** Email đăng nhập là duy nhất trên toàn hệ thống (không phân biệt Customer hay Internal).
- **Định dạng email phiên bản đầu:** Địa chỉ ASCII dạng `local@domain` (domain có nhãn phân cách bằng dấu chấm), tối đa 254 ký tự; không có display name, quoted local-part hoặc khoảng trắng bên trong. Domain quốc tế dùng dạng IDNA ASCII. Chỉ trim khoảng trắng ASCII ngoài địa chỉ; không bỏ dấu chấm/`+tag`. Địa chỉ ngoài định dạng hỗ trợ được giữ nguyên trong hồ sơ cũ và chưa đủ điều kiện email login cho đến khi được xử lý. CP1 cung cấp cùng bộ chuẩn hóa cho backfill và ghi mới.
- **Phân quyền và vai trò:**
  - Danh sách mã vai trò chuẩn (`Role.Code`): `Admin`, `Manager`, `Staff`, `Customer`.
  - Token và DTO phản hồi trả về danh sách `roles` dưới dạng chuỗi mã vai trò chuẩn tiếng Anh (`Admin/Manager/Staff/Customer`), không trả tên hiển thị tiếng Việt.
  - Public registration (`POST /api/auth/register`) chỉ tạo tài khoản với vai trò `Customer`. Tài khoản nội bộ không được đăng ký công khai.

### 3.2. Chính sách mật khẩu (Password Policy)
- Độ dài cho mật khẩu mới: **8–70 Unicode scalar values và không quá 72 byte UTF-8**. Giới hạn số ký tự không thay thế giới hạn byte BCrypt; chuỗi Unicode có thể vượt 72 byte dù dưới 70 ký tự. Không trim/normalize/cắt mật khẩu; vượt giới hạn trả validation error. Login tài khoản cũ vẫn verify hash hiện hữu, không áp quy tắc tạo mật khẩu mới ngược lên mật khẩu cũ. Xem [OWASP Password Storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).
- Độ phức tạp: Mật khẩu bắt buộc phải bao gồm ít nhất:
  1. Một chữ cái in hoa (`[A-Z]`).
  2. Một chữ cái in thường (`[a-z]`).
  3. Một chữ số (`[0-9]`) hoặc ký tự đặc biệt.
- Không tự ý cắt (truncate) mật khẩu ngầm. Hỗ trợ toàn bộ ký tự Unicode hợp lệ.

### 3.3. Chính sách OTP qua Email
- **Mục đích:** Kích hoạt tài khoản mới sau đăng ký (`AccountStatus.PendingActivation` -> `AccountStatus.Active`).
- **Độ dài và bảo mật:** Mã OTP gồm 6 chữ số ngẫu nhiên mật mã (`RandomNumberGenerator`). OTP được lưu trữ dưới dạng băm (HMAC/Hash), không lưu plaintext trong cơ sở dữ liệu.
- **Thời hạn hiệu lực (TTL):** **5 phút** (300 giây) kể từ thời điểm phát hành.
- **Thời gian chờ gửi lại (Cooldown):** **60 giây** giữa hai lần yêu cầu gửi lại OTP.
- **Giới hạn thử sai:** Tối đa **5 lần nhập sai** cho mỗi challenge. Sau 5 lần sai, challenge bị hủy/vô hiệu hóa hoàn toàn, người dùng phải tạo challenge mới.
- **Hạn mức gửi (Rate limits):**
  - Tối đa **5 lần gửi OTP / email / giờ**.
  - Tối đa **20 lần gửi OTP / IP / giờ**.
- **Chống Race Condition:** Việc xác thực (verify) và tiêu thụ (consume) OTP được thực hiện trong cùng một database transaction nguyên tử. Hai request gửi đồng thời chỉ có đúng một request có thể kích hoạt thành công.
- **Gửi lại (Resend):** Khi gửi lại OTP thành công, mã OTP cũ bị vô hiệu hóa ngay lập tức.
- **Gửi Email thật:** Tách biệt `IEmailSender` khỏi `AuthService`. Hệ thống sử dụng SMTP cấu hình qua biến môi trường (hỗ trợ Gmail SMTP qua App Password). Trong môi trường Development, có thể cấu hình tài khoản SMTP thử nghiệm hoặc test inbox sink.
- Các hạn mức gửi/giờ là mặc định cấu hình CP0, có thể điều chỉnh khi nghiệm thu. Fake sender chỉ ở Development/Testing; Production không fallback sang fake. Không log OTP, password/hash hoặc secret; không trả OTP qua HTTP.

### 3.4. Giới hạn thử sai mật khẩu và Khóa tạm thời (Temporary Lockout)
- Đăng nhập sai mật khẩu liên tiếp **5 lần** -> Tài khoản bị khóa tạm thời trong **15 phút** (`LockoutEndAt = DateTimeOffset.UtcNow.AddMinutes(15)`).
- Đăng nhập thành công sẽ thiết lập lại (reset) số lần thử sai về 0.
- Kiểm tra đúng mật khẩu trước khi trả trạng thái pending/khóa/deactivated; request bị khóa không kéo dài thời hạn khóa. Hết 15 phút reset cửa sổ đếm sai trước lần thử mới. Khóa tạm chặn login mới, không tự thu hồi phiên đã đăng nhập; khóa quản trị phải chặn cả phiên hiện hữu.
- Khóa tạm thời khác biệt với trạng thái khóa quản trị (`Status = AccountStatus.Locked` hoặc `Deactivated`). Khi hết hạn 15 phút, tài khoản tự động được phép đăng nhập lại nếu mật khẩu đúng, nhưng nếu tài khoản bị khóa do quản trị viên thì vẫn bị từ chối.

### 3.5. Chống dò quét thông tin người dùng (Anti-Enumeration Policy)
- **Khi đăng nhập:** Nếu email không tồn tại hoặc mật khẩu không chính xác, hệ thống luôn trả về cùng mã HTTP `401 Unauthorized`, mã lỗi `AUTH.INVALID_CREDENTIALS` và cùng thông điệp: *"Email hoặc mật khẩu không chính xác."*
- **Khi đăng ký:** Nếu email đã tồn tại trong hệ thống, hệ thống từ chối đăng ký với mã HTTP `409 Conflict`, mã lỗi `AUTH.EMAIL_EXISTS` kèm thông báo chung: *"Không thể hoàn tất đăng ký với địa chỉ email này. Nếu bạn đã có tài khoản, vui lòng đăng nhập hoặc yêu cầu hỗ trợ."* (Không để lộ thông tin chi tiết về loại tài khoản hay trạng thái nội bộ).
- `409 AUTH.EMAIL_EXISTS` vẫn làm lộ việc email đã đăng ký: đây là giới hạn được ghi nhận của contract này, không gọi đó là chống enumeration hoàn toàn. Retry tài khoản pending đi qua login đúng password và credential tiếp tục, không ghi đè password/hồ sơ bằng register lại.

### 3.6. Phiên làm việc (Session Management qua HttpOnly Cookie)
- **Cơ chế lưu trữ:** Access token (JWT) được đặt trực tiếp trong Cookie **HttpOnly**, không trả về raw JWT token trong body phản hồi JSON cho Web client.
- **Thuộc tính Cookie:**
  - Tên cookie: `ocs_session`
  - `HttpOnly`: `true` (ngăn JavaScript đọc cookie; không thay thế phòng chống XSS)
  - `SameSite`: `Lax` (hoặc `Strict` tùy cấu hình triển khai)
  - `Secure`: `true` trên môi trường Production (HTTPS)
  - `Path`: `/api`
  - `Max-Age` / `Expires`: Khớp với thời hạn của Access Token (8 giờ = 28,800 giây).
- **Session phía server:** Server lưu trữ bản ghi `AuthSession` (chứa `SessionId`, `AccountId`, `CreatedAt`, `ExpiresAt`, `RevokedAt`). JWT mang claim `sid` tương ứng. Khi nhận request, server kiểm tra session còn hiệu lực và chưa bị thu hồi.
- TTL tuyệt đối 8 giờ, không sliding/refresh token ở phạm vi này; `expiresIn` tính từ thời gian còn lại, không hardcode ở `/me`. Khi triển khai cookie/session, buộc bearer cũ login lại; không để nhánh bearer bỏ qua kiểm tra session. Thêm `Cache-Control: no-store` cho phản hồi auth/session/CSRF.
- **Đăng xuất (Logout):** Gọi `POST /api/auth/logout`. Server đánh dấu thu hồi session (`RevokedAt = UtcNow`) và xóa cookie bằng header `Set-Cookie: ocs_session=; Max-Age=0; Path=/api`.
- **Lưu trữ phía Client:** Frontend **tuyệt đối không** lưu mật khẩu, OTP, access token hay user JSON trong `localStorage`/`sessionStorage` để làm bằng chứng xác thực. Trạng thái đăng nhập được xác minh khi khởi chạy ứng dụng thông qua endpoint `GET /api/auth/me`.

### 3.7. Chống giả mạo yêu cầu (CSRF Protection)
- Vì phiên làm việc sử dụng Cookie, các HTTP mutation (`POST`, `PUT`, `DELETE`, `PATCH`) yêu cầu Anti-Forgery Token.
- Endpoint `GET /api/auth/csrf` cung cấp token chống CSRF.
- Frontend đọc token và gửi kèm trong header `X-XSRF-TOKEN` cho mọi request thay đổi trạng thái xác thực và dữ liệu nghiệp vụ.
- Dùng ASP.NET Core antiforgery: cookie secret riêng có HttpOnly và request token trả trong JSON; không dùng cùng một chuỗi làm cả cookie secret và header token. Lấy request token mới sau login/logout khi identity đổi. CORS allowlist origin cụ thể + credentials, không wildcard; ưu tiên same-origin `/api`.

---

## 4. Đặc tả chi tiết các Authentication Endpoints

### 4.1. `POST /api/auth/register`
Đăng ký tài khoản Khách hàng mới. Tạo tài khoản ở trạng thái `PendingActivation` và phát hành OTP Challenge gửi qua email.

- **Yêu cầu xác thực:** Không (Anonymous).
- **Yêu cầu CSRF:** Có (`X-XSRF-TOKEN`).

#### Request Body
```json
{
  "fullName": "Nguyễn Văn A",
  "email": "nguyenvana@example.com",
  "phoneNumber": "0912345678",
  "password": "Password123@"
}
```

| Trường | Kiểu | Bắt buộc | Ràng buộc |
| --- | --- | --- | --- |
| `fullName` | `string` | Có | Tối đa 150 ký tự, được trim khoảng trắng. |
| `email` | `string` | Có | Định dạng email hỗ trợ ở mục 3.1, tối đa 254 ký tự. |
| `phoneNumber` | `string` | Có | Định dạng số điện thoại Việt Nam hợp lệ (10 số). |
| `password` | `string` | Có | Theo mục 3.2, bao gồm cả giới hạn 72 byte UTF-8. |

#### Phản hồi thành công (`200 OK`)
```json
{
  "challengeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "purpose": "RegistrationActivation",
  "maskedDestination": "n***a@example.com",
  "expiresAt": "2026-11-03T09:35:00Z",
  "resendAvailableAt": "2026-11-03T09:31:00Z",
  "continuationToken": "opaque-registration-only-credential"
}
```

#### Phản hồi lỗi
- `400 VALIDATION_FAILED`: Dữ liệu không thỏa mãn validation (mật khẩu yếu, email sai định dạng, v.v.).
- `409 AUTH.EMAIL_EXISTS`: Email đã được đăng ký trong hệ thống.
- `429 AUTH.RATE_LIMITED`: Vượt quá số lần gửi OTP cho email/IP trong 1 giờ.
- `503 AUTH.DELIVERY_UNAVAILABLE`: Dịch vụ gửi email gặp sự cố tạm thời; tài khoản vẫn ở trạng thái pending và người dùng có thể gửi lại OTP sau.

---

### 4.2. `POST /api/auth/otp/verify`
Xác thực mã OTP 6 chữ số để kích hoạt tài khoản.

- **Yêu cầu xác thực:** Không (Anonymous).
- **Yêu cầu CSRF:** Có (`X-XSRF-TOKEN`).

#### Request Body
```json
{
  "challengeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "continuationToken": "opaque-registration-only-credential",
  "code": "123456"
}
```

| Trường | Kiểu | Bắt buộc | Ràng buộc |
| --- | --- | --- | --- |
| `challengeId` | `string (UUID)` | Có | Mã định danh challenge được cấp khi register hoặc resend. |
| `continuationToken` | `string` | Có | Credential tiếp tục ngắn hạn, bind với account/email/purpose; không phải access token. |
| `code` | `string` | Có | Đúng 6 chữ số ASCII (`^[0-9]{6}$`). |

#### Phản hồi thành công (`200 OK`)
Kích hoạt tài khoản thành công (`Status = Active`, `EmailVerifiedAt = UtcNow`). **Không tự động cấp Access Token hay tạo phiên đăng nhập.** Client nhận kết quả và chuyển hướng người dùng sang màn hình Đăng nhập.

```json
{
  "message": "Kích hoạt tài khoản thành công. Vui lòng đăng nhập để tiếp tục.",
  "verifiedAt": "2026-11-03T09:32:00Z"
}
```

#### Phản hồi lỗi
- `400 AUTH.OTP_INVALID`: Mã OTP không chính xác. Kèm số lần thử còn lại trong `errors`.
- `400 AUTH.OTP_EXPIRED`: Mã OTP đã hết hạn (quá 5 phút).
- `400 AUTH.OTP_ATTEMPTS_EXCEEDED`: Đã nhập sai quá 5 lần; challenge đã bị hủy, yêu cầu lấy mã mới.
- `404 AUTH.CHALLENGE_NOT_FOUND`: Challenge không tồn tại hoặc đã được sử dụng.

---

### 4.3. `POST /api/auth/otp/resend`
Yêu cầu gửi lại mã OTP cho challenge hiện hành.

- **Yêu cầu xác thực:** Không (Anonymous).
- **Yêu cầu CSRF:** Có (`X-XSRF-TOKEN`).

#### Request Body
```json
{
  "challengeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "continuationToken": "opaque-registration-only-credential"
}
```

#### Phản hồi thành công (`200 OK`)
Vô hiệu hóa mã cũ và cấp mã mới gửi tới email.

```json
{
  "challengeId": "7c0a1b2c-3d4e-5f6a-7b8c-9d0e1f2a3b4c",
  "purpose": "RegistrationActivation",
  "maskedDestination": "n***a@example.com",
  "expiresAt": "2026-11-03T09:37:00Z",
  "resendAvailableAt": "2026-11-03T09:33:00Z",
  "continuationToken": "opaque-registration-only-credential"
}
```

#### Phản hồi lỗi
- `429 AUTH.RESEND_COOLDOWN`: Chưa đủ 60 giây kể từ lần gửi gần nhất. Header trả về kèm `Retry-After: <seconds>`.
- `429 AUTH.RATE_LIMITED`: Vượt quá 5 lần gửi/email/giờ hoặc 20 lần/IP/giờ.
- `404 AUTH.CHALLENGE_NOT_FOUND`: Challenge không tìm thấy hoặc đã hết hạn hoàn toàn.
- `503 AUTH.DELIVERY_UNAVAILABLE`: Lỗi dịch vụ gửi email.

---

### 4.4. `POST /api/auth/login`
Đăng nhập bằng Email và Mật khẩu. Đặt Cookie `ocs_session` (HttpOnly) chứa phiên làm việc.

- **Yêu cầu xác thực:** Không (Anonymous).
- **Yêu cầu CSRF:** Có (`X-XSRF-TOKEN`).

#### Request Body
```json
{
  "email": "nguyenvana@example.com",
  "password": "Password123@"
}
```

#### Phản hồi thành công (`200 OK`)
- **Headers:** `Set-Cookie: ocs_session=<jwt>; Path=/api; HttpOnly; SameSite=Lax; Max-Age=28800; Secure`
- **Body:** Trả về thông tin người dùng và thời gian hết hạn phiên, **không trả raw JWT trong JSON**.

```json
{
  "expiresIn": 28800,
  "expiresAt": "2026-11-03T17:30:00Z",
  "user": {
    "id": "0d32e894-88c9-4083-ae03-bf673e4f0ddc",
    "email": "nguyenvana@example.com",
    "fullName": "Nguyễn Văn A",
    "phoneNumber": "0912345678",
    "roles": ["Customer"]
  }
}
```

#### Phản hồi lỗi
- `401 AUTH.INVALID_CREDENTIALS`: Email không tồn tại hoặc mật khẩu sai.
- `403 AUTH.ACCOUNT_LOCKED`: Tài khoản đang bị khóa tạm thời (do nhập sai 5 lần) hoặc bị khóa bởi quản trị viên. Body kèm thời gian `lockoutEndAt` và `retryAfterSeconds`.
- `403 AUTH.ACCOUNT_PENDING_ACTIVATION`: Tài khoản chưa được kích hoạt qua OTP email (chỉ trả mã này khi mật khẩu đã nhập đúng). Response trả kèm `continuationToken` ngắn hạn (chỉ có quyền yêu cầu gửi/xác thực OTP) để frontend chuyển sang màn hình OTP:
  ```json
  {
    "type": "https://httpstatuses.com/403",
    "status": 403,
    "code": "AUTH.ACCOUNT_PENDING_ACTIVATION",
    "title": "Tài khoản chưa được kích hoạt.",
    "traceId": "...",
    "continuationToken": "ey...",
    "email": "n***a@example.com"
  }
  ```
- `403 AUTH.ACCOUNT_DEACTIVATED`: Tài khoản đã bị vô hiệu hóa vĩnh viễn.

---

### 4.5. `GET /api/auth/me`
Lấy thông tin người dùng hiện tại và kiểm tra thời hạn phiên làm việc từ cookie `ocs_session`.

- **Yêu cầu xác thực:** Bắt buộc (`Authorize` qua cookie `ocs_session`).
- **Yêu cầu CSRF:** Không bắt buộc cho request GET an toàn.

#### Phản hồi thành công (`200 OK`)
```json
{
  "expiresIn": 24500,
  "expiresAt": "2026-11-03T17:30:00Z",
  "user": {
    "id": "0d32e894-88c9-4083-ae03-bf673e4f0ddc",
    "email": "nguyenvana@example.com",
    "fullName": "Nguyễn Văn A",
    "phoneNumber": "0912345678",
    "roles": ["Customer"]
  }
}
```

#### Phản hồi lỗi
- `401 UNAUTHORIZED`: Cookie không tồn tại, JWT sai chữ ký, session đã hết hạn hoặc session đã bị thu hồi (`RevokedAt`).
- `401 UNAUTHORIZED`: Tài khoản không còn hợp lệ/bị quản trị khóa sau khi đăng nhập. `403 FORBIDDEN` dành cho identity còn hợp lệ nhưng thiếu quyền, không làm frontend logout.

---

### 4.6. `POST /api/auth/logout`
Đăng xuất, thu hồi phiên làm việc hiện tại trên server và yêu cầu client xóa cookie.

- **Yêu cầu xác thực:** Bắt buộc (`Authorize`).
- **Yêu cầu CSRF:** Có (`X-XSRF-TOKEN`).

#### Phản hồi thành công (`204 No Content`)
- **Headers:** `Set-Cookie: ocs_session=; Path=/api; HttpOnly; SameSite=Lax; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT`
- **Body:** Rỗng.

---

### 4.7. `GET /api/auth/csrf`
Cấp Anti-Forgery Request Token cho Web client để sử dụng trong header `X-XSRF-TOKEN`.

- **Yêu cầu xác thực:** Không (Anonymous).
- **Phản hồi thành công (`200 OK`):**
  - **Headers:** cookie secret antiforgery riêng của framework, `HttpOnly; Secure` trên Production; request token trả trong JSON, gửi bằng header `X-XSRF-TOKEN`.
  - **Body:**
  ```json
  {
    "token": "CfDJ8..."
  }
  ```

---

## 5. Bảng mã lỗi chuẩn Authentication (`AUTH.*`)

| HTTP Status | Error Code | Ý nghĩa |
| --- | --- | --- |
| `400` | `VALIDATION_FAILED` | Dữ liệu đầu vào không đúng định dạng hoặc vi phạm quy tắc mật khẩu. |
| `400` | `AUTH.OTP_INVALID` | Mã OTP nhập không đúng. |
| `400` | `AUTH.OTP_EXPIRED` | Mã OTP đã quá hạn 5 phút. |
| `400` | `AUTH.OTP_ATTEMPTS_EXCEEDED` | Nhập sai quá 5 lần; challenge đã bị hủy. |
| `401` | `AUTH.INVALID_CREDENTIALS` | Email không tồn tại hoặc mật khẩu không chính xác. |
| `401` | `UNAUTHORIZED` | Phiên làm việc không hợp lệ, thiếu cookie, hoặc token hết hạn. |
| `403` | `FORBIDDEN` | Tài khoản không có quyền truy cập chức năng yêu cầu. |
| `403` | `AUTH.ACCOUNT_PENDING_ACTIVATION` | Tài khoản chưa xác thực OTP email để kích hoạt. |
| `403` | `AUTH.ACCOUNT_LOCKED` | Tài khoản đang bị khóa tạm thời (sai 5 lần) hoặc khóa do quản trị. |
| `403` | `AUTH.ACCOUNT_DEACTIVATED` | Tài khoản đã bị ngừng hoạt động. |
| `403` | `AUTH.CSRF_INVALID` | Thiếu hoặc sai Anti-Forgery token `X-XSRF-TOKEN`. |
| `404` | `AUTH.CHALLENGE_NOT_FOUND` | Challenge OTP không tồn tại hoặc đã bị tiêu thụ. |
| `409` | `AUTH.EMAIL_EXISTS` | Địa chỉ email đã được sử dụng bởi một tài khoản khác trong hệ thống. |
| `429` | `AUTH.RESEND_COOLDOWN` | Yêu cầu gửi lại OTP khi chưa hết thời gian chờ 60 giây. |
| `429` | `AUTH.RATE_LIMITED` | Vượt quá số lần gửi OTP / email / giờ hoặc / IP / giờ. |
| `503` | `AUTH.DELIVERY_UNAVAILABLE` | Dịch vụ gửi email SMTP gặp sự cố tạm thời. |

---

## 6. Ma trận phân quyền và điều hướng mặc định sau đăng nhập

| Vai trò (`Role.Code`) | Đường dẫn điều hướng mặc định trên Frontend | Quyền hạn chính |
| --- | --- | --- |
| `Admin` | `/admin` | Toàn quyền cấu hình, tài khoản và hệ thống. |
| `Manager` | `/admin` | Giám sát doanh thu, duyệt giải ngân, duyệt mua đứt, báo cáo. |
| `Staff` | `/admin/pos` | Tiếp nhận đồ, kiểm định, bán hàng Web POS, trả hàng tồn. |
| `Customer` | `/` | Đặt lịch ký gửi, theo dõi hợp đồng và sản phẩm của mình. |

*Lưu ý:* Khi một tài khoản có nhiều vai trò, Frontend áp dụng thứ tự ưu tiên: `Admin` > `Manager` > `Staff` > `Customer`.

## 7. Chuyển tài khoản cũ và trạng thái bàn giao

- CP1 thêm field nullable và backfill email hợp lệ. Không đặt `EmailVerifiedAt`, không đổi `Status`, Id, profile, role hoặc liên kết nghiệp vụ cho tài khoản cũ chỉ vì đã có email/password.
- Thiếu/sai email vẫn giữ dữ liệu gốc, `NormalizedEmail = null`. Tài khoản đó chưa đủ điều kiện email login; cần hỗ trợ kiểm tra danh tính và bổ sung email/password qua quy trình được quản lý, không nhận quyền từ SĐT do khách tự nhập.
- Email trùng sau chuẩn hóa giữa bất kỳ AccountType nào: dừng migration áp unique, chỉ báo số lượng; người vận hành xử lý mapping có review rồi chạy lại. Không tự gộp/xóa tài khoản hay lấy tài khoản đầu tiên.
- Tài khoản cũ có email/password nhưng chưa có `EmailVerifiedAt` phải hoàn tất email OTP trước khi được cấp cookie ở CP3–CP5, kể cả `Status = Active` cũ. Giữ nguyên khóa quản trị và lịch sử kích hoạt.
- Credential tiếp tục OTP có TTL tối đa 15 phút, ngẫu nhiên khó đoán, lưu hash và bind account/email/purpose; không nhận địa chỉ email thay thế từ client. Resend thay challenge nhưng không reset limiter theo subject/IP. SMTP lỗi trả `503`, account vẫn pending; người dùng tiếp tục bằng login đúng password. Không gửi email trước khi DB ghi được trạng thái challenge bền vững.
- Cấu hình SMTP dự kiến dùng các khóa `Email__Provider`, `Email__SmtpHost`, `Email__SmtpPort`, `Email__Security`, `Email__Username`, `Email__AppPassword`, `Email__FromAddress`, `Email__FromName` trong `.env.example`; secret chỉ qua môi trường/local secrets. CP2 thêm timeout và OTP/limiter options cùng runtime validation.
- Các thay đổi này bổ sung cho project `OCS.Api`: DTO tại `Models/Auth`, service tại `Services`, repository tại `Repositories`, entity/config/migration tại `Entities` và `Context`. Không tạo lại các project Application/Domain không tồn tại.

## Lịch hẹn ký gửi và thông báo

Các endpoint thay đổi dữ liệu yêu cầu cookie phiên hợp lệ và CSRF token như mục 3.7.

| Phương thức | Đường dẫn | Quyền | Mục đích |
| --- | --- | --- | --- |
| GET | `/api/appointments/available-slots?date=YYYY-MM-DD` | Công khai | Xem khung giờ còn chỗ. |
| POST | `/api/appointments` | Đăng nhập | Đặt lịch ký gửi. |
| GET | `/api/appointments/my-history` | Đăng nhập | Xem lịch hẹn của tài khoản hiện tại. |
| GET | `/api/appointments/{id}` | Chủ lịch hẹn hoặc nhân viên | Xem chi tiết lịch hẹn. |
| PUT | `/api/appointments/{id}/cancel` | Chủ lịch hẹn | Hủy lịch theo quy định. |
| GET | `/api/appointments` | Admin, Manager, Staff | Tra cứu danh sách lịch hẹn. |
| PATCH | `/api/appointments/{id}/status` | Admin, Manager, Staff | Cập nhật trạng thái lịch hẹn. |
| POST | `/api/appointments/{id}/start-reception` | Admin, Manager, Staff | Kiểm tra điều kiện bắt đầu tiếp nhận. |
| GET | `/api/notifications` | Đăng nhập | Xem thông báo của tài khoản. |
| PATCH | `/api/notifications/{id}/read` | Đăng nhập | Đánh dấu một thông báo đã đọc. |
| PATCH | `/api/notifications/read-all` | Đăng nhập | Đánh dấu tất cả thông báo đã đọc. |


---

## 8. Hồ sơ tự phục vụ của người ký gửi (Customer Self-Profile)

### 8.1. Nguyên tắc thiết kế và bảo mật
- **Tự phục vụ chính chủ (Self-only):** Không có tham số `id` hoặc `customerId` trong URL hay body. Danh tính tài khoản được lấy trực tiếp và duy nhất từ claim `sub` (`ClaimTypes.NameIdentifier`) của phiên đăng nhập (`ocs_session`).
- **Phân quyền:** Chỉ tài khoản có vai trò `Customer` (`Role.Code = Customer`) mới có quyền truy cập. Yêu cầu không có phiên trả về `401 UNAUTHORIZED`; có phiên nhưng không phải vai trò `Customer` trả về `403 FORBIDDEN`.
- **Bảo vệ CSRF:** Endpoint cập nhật (`PUT /api/customers/me`) bắt buộc kèm header `X-XSRF-TOKEN` cùng cookie chống giả mạo `ocs_antiforgery` (lấy từ `GET /api/auth/csrf`).
- **Bảo vệ dữ liệu nhạy cảm:** Không trả về các trường nội bộ như email, mật khẩu hash, trạng thái tài khoản, vai trò hoặc audit log. Số tài khoản ngân hàng chỉ được trả nguyên vẹn cho chính chủ ở endpoint self-profile này để rà soát chi trả, không đưa vào danh sách tra cứu chung. Mọi thay đổi qua hệ thống đều được tự động lưu log qua `AuditSaveChangesInterceptor` với cơ chế che (mask) số điện thoại, số tài khoản và redact họ tên, tên chủ tài khoản.
- **Ràng buộc giải ngân đang chờ (Payout Lock):** Khi khách hàng có ít nhất một lệnh giải ngân ở trạng thái `Pending`, hệ thống từ chối mọi yêu cầu thay đổi hoặc xóa thông tin tài khoản ngân hàng (`409 CUSTOMER.BANK_ACCOUNT_LOCKED_FOR_PAYOUT`). Khách hàng vẫn được phép cập nhật họ tên và số điện thoại trong trường hợp này. Các trạng thái giải ngân khác (`Transferred`, `Completed`) không kích hoạt khóa.
- **Tính duy nhất số điện thoại:** Kiểm tra duy nhất số điện thoại trong phạm vi `AccountType.Customer` trước khi ghi, đồng thời bắt lỗi unique constraint của database để chống xung đột tương thời (`409 CUSTOMER.PHONE_NUMBER_EXISTS`).

### 8.2. Danh sách endpoint

| Phương thức | Đường dẫn | Quyền | Mục đích |
| --- | --- | --- | --- |
| GET | `/api/customers/me` | Customer (Cookie session) | Xem thông tin hồ sơ của chính mình. |
| PUT | `/api/customers/me` | Customer (Cookie session + `X-XSRF-TOKEN`) | Cập nhật hồ sơ (họ tên, số điện thoại, tài khoản ngân hàng). |

### 8.3. Chi tiết đặc tả

#### 8.3.1. Xem hồ sơ: `GET /api/customers/me`
- **Request Headers:**
  - `Cookie: ocs_session=<jwt_token>`
- **Response 200 OK:**
```json
{
  "id": "a0d6e1a1-1f7b-4fe7-bc74-20b720a254c1",
  "fullName": "Nguyễn Văn A",
  "phoneNumber": "0912345678",
  "bankAccount": {
    "bankCode": "VCB",
    "accountNumber": "000000000001",
    "accountHolderName": "NGUYEN VAN A"
  }
}
```
*Ghi chú:* `bankAccount` trả về `null` nếu khách hàng chưa thiết lập tài khoản ngân hàng nhận tiền.

- **Mã lỗi:**
  - `401 UNAUTHORIZED`: Phiên đăng nhập không hợp lệ hoặc đã hết hạn (`code: "UNAUTHORIZED"`).
  - `403 FORBIDDEN`: Tài khoản không có vai trò `Customer` (`code: "FORBIDDEN"`).
  - `404 NOT_FOUND`: Không tìm thấy hồ sơ người ký gửi (`code: "CUSTOMER.PROFILE_NOT_FOUND"`).

#### 8.3.2. Cập nhật hồ sơ: `PUT /api/customers/me`
- **Thứ tự thực hiện phía client:**
  1. Gửi `GET /api/auth/csrf` để nhận CSRF token và cookie `ocs_antiforgery`.
  2. Gửi `PUT /api/customers/me` với cookie `ocs_session`, cookie `ocs_antiforgery`, và header `X-XSRF-TOKEN: <token>`.
- **Request Body (Cập nhật đầy đủ):**
```json
{
  "fullName": "Nguyễn Văn A",
  "phoneNumber": "0912345678",
  "bankAccount": {
    "bankCode": "VCB",
    "accountNumber": "000000000001",
    "accountHolderName": "NGUYEN VAN A"
  }
}
```
- **Request Body (Xóa cấu hình ngân hàng):**
```json
{
  "fullName": "Nguyễn Văn A",
  "phoneNumber": "0912345678",
  "bankAccount": null
}
```
- **Quy tắc kiểm tra dữ liệu:**
  - `fullName`: Chuỗi bắt buộc sau khi trim, tối đa 150 ký tự.
  - `phoneNumber`: Chuỗi bắt buộc, đúng định dạng số điện thoại Việt Nam 10 chữ số bắt đầu bằng số 0 (`^0[0-9]{9}$`).
  - `bankAccount`:
    - Nếu là `null`: Xóa cả 3 trường thông tin ngân hàng.
    - Nếu khác `null`: Cả 3 trường phải hiện diện sau khi trim: `bankCode` (viết hoa, tối đa 50 ký tự), `accountNumber` (tối đa 50 ký tự), `accountHolderName` (tối đa 200 ký tự).
- **Response 200 OK:** Trả về `CustomerSelfProfileResponse` mới nhất.
- **Mã lỗi:**
  - `400 VALIDATION_FAILED`: Dữ liệu không hợp lệ theo quy tắc (`ValidationProblemDetails`).
  - `401 UNAUTHORIZED`: Thiếu hoặc phiên đăng nhập không hợp lệ (`code: "UNAUTHORIZED"`).
  - `403 AUTH.CSRF_INVALID`: Thiếu hoặc token CSRF không hợp lệ (`code: "AUTH.CSRF_INVALID"`).
  - `403 FORBIDDEN`: Không có vai trò `Customer` (`code: "FORBIDDEN"`).
  - `404 CUSTOMER.PROFILE_NOT_FOUND`: Không tìm thấy hồ sơ khách hàng (`code: "CUSTOMER.PROFILE_NOT_FOUND"`).
  - `409 CUSTOMER.PHONE_NUMBER_EXISTS`: Số điện thoại đã được đăng ký bởi một khách hàng khác (`code: "CUSTOMER.PHONE_NUMBER_EXISTS"`).
  - `409 CUSTOMER.BANK_ACCOUNT_LOCKED_FOR_PAYOUT`: Không thể sửa/xóa thông tin ngân hàng do đang có giải ngân `Pending` (`code: "CUSTOMER.BANK_ACCOUNT_LOCKED_FOR_PAYOUT"`).

---

## 9. Danh mục sản phẩm công khai cho Landing Page (Public Product Catalog)

### 9.1. Lấy danh sách sản phẩm công khai: `GET /api/products`
- **Xác thực:** `[AllowAnonymous]`. Endpoint hoàn toàn mở cho Landing Page công khai, không yêu cầu token, cookie phiên hoặc token CSRF.
- **Query Parameters:**
  - `page` *(int, tùy chọn, mặc định: 1)*: Số trang cần lấy, tối thiểu là 1.
  - `pageSize` *(int, tùy chọn, mặc định: 20, tối đa: 100)*: Số lượng sản phẩm mỗi trang.
  - `q` *(string, tùy chọn, tối đa 100 ký tự)*: Từ khóa tìm kiếm không phân biệt hoa thường theo tên sản phẩm, tên thương hiệu hoặc tên danh mục. Chuỗi rỗng hoặc chỉ có khoảng trắng được bỏ qua.
  - `categoryId` *(UUID/GUID, tùy chọn)*: Lọc chính xác theo danh mục sản phẩm.
  - `brandId` *(UUID/GUID, tùy chọn)*: Lọc chính xác theo thương hiệu sản phẩm.
  - `size` *(string, tùy chọn, tối đa 30 ký tự)*: Lọc theo kích cỡ sản phẩm (không phân biệt hoa thường, tự động loại bỏ khoảng trắng thừa).
  - `minPrice` *(decimal, tùy chọn, tối thiểu 0)*: Lọc theo giá niêm yết tối thiểu.
  - `maxPrice` *(decimal, tùy chọn, tối thiểu 0)*: Lọc theo giá niêm yết tối đa.
  - `minCondition` *(int, tùy chọn, từ 0–100)*: Lọc sản phẩm có độ mới (`conditionPercentage`) lớn hơn hoặc bằng giá trị này.
  - `sort` *(string, tùy chọn, mặc định: "newest")*: Thứ tự sắp xếp kết quả:
    - `newest`: Sản phẩm mới nhất (`CreatedAt DESC, Id DESC`).
    - `priceAsc`: Giá tăng dần (`ListedPrice ASC, Id ASC`).
    - `priceDesc`: Giá giảm dần (`ListedPrice DESC, Id DESC`).
- **Quy tắc hiển thị sản phẩm công khai:**
  - Chỉ hiển thị sản phẩm có `Product.Status == OnSale`.
  - Thứ tự sắp xếp theo tham số `sort` ổn định với tie-break bằng `Id`.
- **Quy tắc bảo mật và riêng tư (Public Privacy Guarantee):**
  - Tuyệt đối **KHÔNG** trả về: `barcode`, `status` nội bộ, `sourceType`, `purchasePrice`, `agreedCommissionRate`, `agreedBrandTier`, `consignmentContractId`, `buyoutId`, `storageLocationId`.
  - Tuyệt đối **KHÔNG** trả về hoặc điều hướng tới bất kỳ thông tin nào của người ký gửi (`customerId`, `customerProfile`, `account`, số điện thoại, email, thông tin tài khoản ngân hàng).
- **Quy tắc ảnh đại diện (Cover Image):**
  - Lấy ảnh có `ImageType == 'Cover'` với `SortOrder` nhỏ nhất, kế tiếp theo `Id`.
  - Nếu sản phẩm chưa có ảnh cover, trường `coverImageUrl` trả về `null`.
- **Response 200 OK:**
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "Áo Blazer Dạ Hàn Quốc",
      "description": "Áo blazer phom suông thanh lịch, độ mới cao",
      "listedPrice": "450000.00",
      "size": "M",
      "conditionPercentage": 95,
      "category": {
        "id": "c1000000-0000-0000-0000-000000000001",
        "name": "Áo khoác"
      },
      "brand": {
        "id": "b1000000-0000-0000-0000-000000000001",
        "name": "Zara"
      },
      "coverImageUrl": "https://cdn.example.com/products/blazer-cover.jpg"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```
- **Mã lỗi:**
  - `400 VALIDATION_FAILED`: Một hoặc nhiều tham số query không hợp lệ (`application/problem+json`). Ví dụ:
    - `page < 1`
    - `pageSize < 1` hoặc `pageSize > 100`
    - Phân trang vượt quá giới hạn integer offset (`(page - 1) * pageSize > int.MaxValue`)
    - `minPrice < 0` hoặc `maxPrice < 0`
    - `minPrice > maxPrice` khi cả hai cùng được truyền
    - `q` dài hơn 100 ký tự hoặc `size` dài hơn 30 ký tự
  - `500 INTERNAL_ERROR`: Lỗi hệ thống nội bộ.

### 9.2. Xem chi tiết sản phẩm công khai: `GET /api/products/{id}/public`
- **Xác thực:** `[AllowAnonymous]`. Endpoint hoàn toàn công khai cho khách xem chi tiết sản phẩm và gallery ảnh.
- **Quy tắc hiển thị & Trạng thái:**
  - Chỉ trả về dữ liệu khi `Product.Status == OnSale`.
  - Nếu sản phẩm không tồn tại HOẶC có tồn tại nhưng `Status != OnSale` ➔ Trả về `404 NOT_FOUND` (tránh rò rỉ sự hiện diện của sản phẩm nội bộ).
- **Quy tắc danh sách ảnh (`images`):**
  - Chỉ bao gồm ảnh có `ImageType IN ('Cover', 'Detail')`.
  - Tuyệt đối loại trừ ảnh sự cố (`ImageType != 'Incident'`).
  - Thứ tự ảnh: Sắp xếp tăng dần theo `SortOrder ASC, Id ASC`. Ảnh `Cover` có `SortOrder = 0` đứng đầu mảng.
- **Bảo vệ quyền riêng tư:**
  - Tuyệt đối **KHÔNG** trả về: `barcode`, `purchasePrice`, `agreedCommissionRate`, `consignmentContractId`, `buyoutId`, `storageLocationId`, hay thông tin người ký gửi.
- **Response 200 OK:**
```json
{
  "id": "70000000-0000-0000-0000-000000000001",
  "name": "Áo Blazer Zara Dạ Nâu",
  "description": "Mô tả chi tiết sản phẩm Áo Blazer Zara Dạ Nâu, chất lượng đảm bảo.",
  "listedPrice": "650000.00",
  "size": "M",
  "conditionPercentage": 95,
  "category": {
    "id": "30000000-0000-0000-0000-000000000001",
    "name": "Áo khoác"
  },
  "brand": {
    "id": "40000000-0000-0000-0000-000000000001",
    "name": "Zara"
  },
  "images": [
    {
      "id": "71000000-0000-0000-0000-000000000001",
      "url": "https://images.unsplash.com/photo-1591047139829-d91aecb6caea?w=800&auto=format&fit=crop&q=80",
      "imageType": "Cover",
      "sortOrder": 0
    },
    {
      "id": "72000000-0000-0000-0000-000000000001",
      "url": "https://images.unsplash.com/photo-1591047139829-d91aecb6caea?w=800&auto=format&fit=crop&q=80",
      "imageType": "Detail",
      "sortOrder": 1
    }
  ]
}
```
- **Mã lỗi:**
  - `400 VALIDATION_FAILED`: Định dạng GUID của tham số `id` không hợp lệ (`application/problem+json`).
  - `404 NOT_FOUND`: Không tìm thấy sản phẩm hoặc sản phẩm không còn mở bán công khai:
```json
{
  "type": "https://httpstatuses.com/404",
  "title": "Not Found",
  "status": 404,
  "code": "PRODUCT.NOT_FOUND",
  "detail": "Không tìm thấy sản phẩm hoặc sản phẩm không còn mở bán công khai.",
  "traceId": "00-..."
}
```

### 9.3. Bảo vệ các endpoint nội bộ cùng resource
- Các endpoint quản lý sản phẩm nội bộ:
  - `GET /api/products/{id}`
  - `GET /api/products/barcode/{barcode}`
  - `POST /api/products`
  được cấu hình yêu cầu xác thực vai trò nội bộ: `[Authorize(Roles = "Admin,Manager,Staff")]`.
- Người dùng chưa đăng nhập hoặc anonymous caller khi truy cập các route nội bộ trên sẽ nhận mã lỗi `401 UNAUTHORIZED`. `GET /api/products` là endpoint public duy nhất cho danh sách Landing Page.