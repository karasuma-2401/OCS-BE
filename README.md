# OCS Backend

Backend API cho **OCS - Old Clothes Store**, hệ thống quản lý cửa hàng ký gửi quần áo.

## Tài liệu

Các tài liệu dưới đây được chuyển đổi và chuẩn hóa từ Báo cáo SE100 (Final 3), sau đó đối chiếu với mã nguồn hiện có. Những nội dung là định hướng nghiệp vụ trong báo cáo được ghi rõ là yêu cầu; chúng không đồng nghĩa với một API hoặc một bảng dữ liệu đã được hiện thực.

| Tài liệu | Nội dung |
| --- | --- |
| [Requirements.md](docs/Requirements.md) | Yêu cầu chức năng, yêu cầu phi chức năng và phạm vi vận hành. |
| [UseCases.md](docs/UseCases.md) | Sơ đồ và đặc tả chín use case chính. |
| [Architecture.md](docs/Architecture.md) | Kiến trúc hiện tại, layer, dependency và tích hợp runtime. |
| [DataFlows.md](docs/DataFlows.md) | Biểu mẫu, quy định, luồng dữ liệu và thuật toán nghiệp vụ. |
| [DataModel.md](docs/DataModel.md) | Mô hình dữ liệu theo báo cáo và mức độ hiện thực trong EF Core. |
| [DatabaseScript.txt](docs/DatabaseScript.txt) | SQL PostgreSQL được sinh từ các EF Core migration hiện có. |
| [API_CONTRACT.md](docs/API_CONTRACT.md) | Hợp đồng HTTP duy nhất: endpoint đang công bố và quy ước mở rộng API. |

## Tình trạng hiện thực

Mã nguồn hiện có đã có nền tảng cho tài khoản/RBAC, audit log, danh mục, lịch hẹn, hợp đồng ký gửi, thu mua đứt, sản phẩm và lịch sử trạng thái. Các controller nghiệp vụ, POS offline, VietQR, xử lý hết hạn, quyết toán, thông báo và các báo cáo vẫn chưa xuất hiện trong backend; vì vậy chúng chỉ được mô tả như yêu cầu hoặc luồng dự kiến trong tài liệu.

## Công nghệ

- .NET 10 / ASP.NET Core Web API
- Entity Framework Core + Neon PostgreSQL
- Redis cache
- Hangfire với PostgreSQL storage
- Cloudflare R2 object storage qua S3-compatible API
- JWT authentication
- xUnit, Docker và GitHub Actions

## Kiến trúc

Repository dùng Clean Architecture nhẹ. Team vẫn triển khai theo luồng Controller → Application Service → Repository quen thuộc; ranh giới project giúp compiler kiểm soát chiều phụ thuộc:

```text
src/
├── OCS.Domain/           # Entity, value object và business rule thuần
├── OCS.Application/      # Use case/service và abstraction
├── OCS.Infrastructure/   # EF Core, Neon, Redis, Hangfire và Cloudflare R2
└── OCS.Api/              # Controller, authentication và composition root
tests/
└── OCS.Application.Tests/
```

Chiều phụ thuộc: `Api → Application`, `Api → Infrastructure`, `Infrastructure → Application → Domain`. Domain không phụ thuộc framework hoặc hạ tầng. CQRS, MediatR, event bus và domain event không phải yêu cầu bắt buộc của kiến trúc này; chỉ bổ sung khi nghiệp vụ thực tế cần.

Các phân hệ nghiệp vụ sẽ được thêm dần theo tài liệu nhóm: tài khoản/RBAC, lịch hẹn, tiếp nhận và thẩm định, hợp đồng ký gửi, sản phẩm và kho, POS, xử lý hết hạn/rút sớm, đối soát và giải ngân, thông báo và cấu hình hệ thống.

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
