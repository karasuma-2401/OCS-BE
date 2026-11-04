# OCS Backend

Backend API cho **OCS - Old Clothes Store**, hệ thống quản lý cửa hàng ký gửi quần áo.

## Tài liệu

Các tài liệu dưới đây được chuyển đổi và chuẩn hóa từ Báo cáo SE100 (Final 3), sau đó đối chiếu với mã nguồn hiện có. Những nội dung là định hướng nghiệp vụ trong báo cáo được ghi rõ là yêu cầu; chúng không đồng nghĩa với một API hoặc một bảng dữ liệu đã được hiện thực.

| Tài liệu | Nội dung |
| --- | --- |
| [Requirements.md](docs/Requirements.md) | Yêu cầu chức năng, yêu cầu phi chức năng và phạm vi vận hành. |
| [UseCases.md](docs/UseCases.md) | Sơ đồ và đặc tả chín use case chính. |
| [Architecture.md](docs/Architecture.md) | Kiến trúc hiện tại, cấu trúc thư mục, luồng dữ liệu và tích hợp runtime. |
| [DataFlows.md](docs/DataFlows.md) | Biểu mẫu, quy định, luồng dữ liệu và thuật toán nghiệp vụ. |
| [DataModel.md](docs/DataModel.md) | Mô hình dữ liệu theo báo cáo và mức độ hiện thực trong EF Core. |
| [DatabaseScript.txt](docs/DatabaseScript.txt) | SQL PostgreSQL được sinh từ các EF Core migration hiện có. |
| [API_CONTRACT.md](docs/API_CONTRACT.md) | Hợp đồng HTTP duy nhất: endpoint đang công bố và quy ước mở rộng API. |
| [AUTH_DATA_MIGRATION.md](docs/AUTH_DATA_MIGRATION.md) | CP1: preflight email cũ, migration OTP/session, test PostgreSQL và xử lý xung đột. |

## Tình trạng hiện thực

Mã nguồn hiện có đã có nền tảng cho tài khoản/RBAC, audit log, danh mục, lịch hẹn, hợp đồng ký gửi, thu mua đứt, sản phẩm và lịch sử trạng thái. Hệ thống vận hành 100% online qua Web API và React Frontend; các cơ chế offline SQLite đã được loại bỏ hoàn toàn.

## Công nghệ

Đăng nhập Email + Mật khẩu, đăng ký với OTP email, cookie HttpOnly (`ocs_session`), phiên server (`AuthSession`) và bảo vệ CSRF (`X-XSRF-TOKEN`). Đăng ký trả lỗi khi email đã tồn tại, vì vậy endpoint này vẫn có thể tiết lộ email đã đăng ký.

Local: đặt cấu hình theo `.env.example` trong `.env` bị Git bỏ qua; khi chạy môi trường `Development`, API tự nạp file và ưu tiên biến môi trường đã có. Không đưa App Password vào source, log hoặc Git. Cần cấu hình đúng `Email__Username`, `Email__AppPassword` và `Email__FromAddress` để gửi Gmail thật.

Production: cấu hình các GitHub Secrets `SMTP_USERNAME`, `SMTP_APP_PASSWORD`, `SMTP_FROM_ADDRESS`, cùng các secrets triển khai hiện có; `SMTP_HOST` là GitHub variable tùy chọn, mặc định `smtp.gmail.com`. `.env` trên máy phát triển không tự được đưa lên GitHub/VPS. Triển khai kiểm tra migration còn thiếu trước khi khởi động phiên bản mới, nhưng không tự áp migration: thực hiện preflight và migration theo [runbook](docs/AUTH_DATA_MIGRATION.md). Redis là bắt buộc để giới hạn tần suất thống nhất giữa các instance. Khi có reverse proxy, đặt GitHub variable `TRUSTED_PROXY_IP` bằng IP proxy tin cậy mà API nhìn thấy (hoặc cấu hình `ReverseProxy__KnownProxies__0`, `__1`, ...); không tin `X-Forwarded-For` từ nguồn bất kỳ. Thiếu cấu hình này, API giới hạn theo IP proxy nên nhiều người dùng có thể dùng chung hạn mức.

Mật khẩu vượt quá 72 byte UTF-8 không được chấp nhận vì giới hạn BCrypt. Tài khoản cũ từng dùng mật khẩu dài hơn cần được hỗ trợ đặt lại qua quy trình quản trị được xác minh; chưa có chức năng tự khôi phục mật khẩu.

- .NET 10 / ASP.NET Core Web API
- Entity Framework Core + Neon PostgreSQL
- Redis cache
- Hangfire với PostgreSQL storage
- Cloudflare R2 object storage qua S3-compatible API
- JWT authentication
- xUnit, Docker và GitHub Actions

## Kiến trúc

Backend được tổ chức theo kiến trúc monolithic N-tier tinh gọn (tương tự mô hình `eBoard-backend`), quy về một project duy nhất `OCS.Api` để tối ưu hóa phát triển, triển khai và bảo trì:

```text
src/
└── OCS.Api/
    ├── Controllers/             # HTTP endpoints, routing, authorization
    ├── Services/                # Business logic implementation (AuthService, ProductService, ...)
    ├── Repositories/            # UnitOfWork & Repository pattern (resolved via DI container)
    ├── Interfaces/              # Abstraction cho Services & Repositories
    ├── Entities/                # Database entities, domain models & BaseEntity
    ├── Context/                 # EF Core AppDbContext, configurations, migrations, seeds
    ├── Models/                  # Request/Response DTOs & ViewModels
    ├── Common/                  # Result<T>, ApplicationError, PagedRequest/Response, ErrorCodes
    ├── Consts/                  # Domain Enums & hằng số hệ thống
    ├── Extensions/              # DI container registration, middleware & API conventions
    └── Storage/                 # Cloudflare R2 storage options & client
tests/
└── OCS.Api.IntegrationTests/    # Contract tests, JWT auth, validation, liveness
```

Luồng xử lý chuẩn: `Controller → Service → Repository / UnitOfWork → AppDbContext`. Toàn bộ phụ thuộc được giải quyết thông qua .NET Dependency Injection container tiêu chuẩn.

## Chạy local

Yêu cầu: .NET SDK 10 và một Neon PostgreSQL project.

```powershell
Copy-Item .env.example .env
```

Cập nhật `.env` bằng connection string từ Neon. Không commit `.env` hoặc bất kỳ secret nào.

Chạy trực tiếp (Redis sẽ tự fallback sang distributed memory cache nếu chưa cấu hình):

```powershell
$env:ConnectionStrings__DefaultConnection = "<NEON_CONNECTION_STRING>"
dotnet run --project src/OCS.Api
```

Hoặc chạy API và Redis bằng Docker:

```powershell
docker compose up --build
```

Readiness endpoint: `GET http://localhost:5204/api/health` kiểm tra Neon, Redis và Cloudflare R2.
Liveness endpoint: `GET http://localhost:5204/api/health/live` chỉ kiểm tra tiến trình API.
Scalar API reference: `http://localhost:5204/scalar/v1` khi chạy ở Development.
Cloudflare R2 là dịch vụ bên ngoài Docker; API local dùng bucket R2 được cấu hình trong `.env`.

## Kiểm tra chất lượng

Để chạy cả test migration/concurrency auth, đặt `OCS_TEST_POSTGRES` tới PostgreSQL test riêng có quyền CREATE DATABASE. Test tạo/xóa database `ocs_auth_test_<guid>`; không dùng connection string vận hành. Nếu thiếu biến, các test PostgreSQL được báo skip. CI cung cấp service PostgreSQL riêng cho bộ test này.

Để kiểm thử rate limit với Redis thật, đặt `OCS_TEST_REDIS` tới Redis test riêng (ví dụ `localhost:6379,abortConnect=true`). Test chỉ tạo và xóa các key có tên ngẫu nhiên của riêng nó; nếu thiếu biến, test Redis sẽ được báo skip. CI chạy các test này bằng Redis service riêng.

```powershell
dotnet format OCS.sln --verify-no-changes
dotnet build OCS.sln --configuration Release
dotnet test OCS.sln --configuration Release
docker build --file src/OCS.Api/Dockerfile --tag ocs-api:local .
```

GitHub Actions gồm:

- `Backend CI`: format, secret scanning, build, test và Docker build verification.
- `Publish API Image`: build multi-architecture image, quét Trivy và publish lên GHCR với tag `dev`, `main`, `latest` và `sha-xxxxxxx`.
- `Deploy API to VPS`: SSH vào VPS và chạy API cùng Redis bằng Docker Compose. Neon và Cloudflare R2 là các dịch vụ bên ngoài VPS.

### GitHub Actions secrets cho CD

Các repository secret bắt buộc:

- `VPS_HOST`, `VPS_SSH_KEY`; tùy chọn `VPS_USERNAME` (mặc định `ubuntu`) và `VPS_PORT` (mặc định `22`).
- `NEON_CONNECTION_STRING`.
- `JWT_KEY`.
- `CORS_ALLOWED_ORIGIN`.
- `REDIS_PASSWORD`.
- `R2_SERVICE_URL`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`.

Các repository variable tùy chọn:

- `JWT_ISSUER` (mặc định `ocs-api`).
- `JWT_AUDIENCE` (mặc định `ocs-clients`).
- `R2_BUCKET_NAME` (mặc định `ocs`).
- `R2_PUBLIC_BASE_URL` nếu bucket được public qua custom domain hoặc `r2.dev`.

R2 API token nên chỉ có quyền Object Read & Write trên đúng bucket của OCS. Không đưa Access Key ID hoặc Secret Access Key xuống frontend; khi cần upload trực tiếp từ client, backend sẽ cấp presigned URL có thời hạn.

## Cấu hình production

Các khóa cấu hình dùng định dạng environment variable của ASP.NET Core, ví dụ:

- `ConnectionStrings__DefaultConnection`: Neon PostgreSQL connection string.
- `Redis__ConnectionString`: Redis connection string.
- `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`: cấu hình JWT.
- `Cors__AllowedOrigins__0`: origin đầu tiên được phép.
- `R2__ServiceUrl`: `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`.
- `R2__AccessKeyId`, `R2__SecretAccessKey`: S3 API credentials của R2.
- `R2__BucketName`, `R2__PublicBaseUrl`: bucket và public/custom domain tùy chọn.
