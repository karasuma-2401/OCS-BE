# OCS Backend

API cho **Old Clothes Store**, hệ thống quản lý cửa hàng ký gửi quần áo.

## Công nghệ

| | Thành phần | Công nghệ |
| --- | --- | --- |
| ⚙️ | Backend | .NET 10, ASP.NET Core, Entity Framework Core |
| 🗄️ | Dữ liệu | PostgreSQL (Neon), Redis |
| 🔐 | Xác thực | JWT, email OTP |
| ☁️ | Lưu trữ & tác vụ | Cloudflare R2, Hangfire |
| 🐳 | Công cụ | Docker, xUnit |

## Chạy trên máy cá nhân

**Yêu cầu:** Git, Docker Desktop và một PostgreSQL database có thể kết nối từ Docker. Để nhận OTP thật, cần tài khoản Gmail có App Password. Cài .NET SDK 10 nếu muốn phát triển hoặc chạy API ngoài Docker.

1. Clone repository và tạo file cấu hình:

   ```powershell
   git clone https://github.com/karasuma-2401/OCS-BE.git
   cd OCS-BE
   Copy-Item .env.example .env
   ```

2. Cập nhật `.env` theo `.env.example`:

   - `ConnectionStrings__DefaultConnection`: connection string PostgreSQL theo định dạng Npgsql. Nếu PostgreSQL chạy trên máy cá nhân, dùng `host.docker.internal` thay cho `localhost`.
   - `Jwt__Key`: chuỗi bí mật tối thiểu 32 byte.
   - `Email__Username`, `Email__AppPassword`, `Email__FromAddress`: thông tin gửi OTP qua Gmail.
   - Giữ `ASPNETCORE_ENVIRONMENT=Development` và `Redis__ConnectionString=redis:6379` khi chạy bằng Docker Compose. Cấu hình `R2__*` nếu sử dụng lưu trữ ảnh.

   File `.env` chứa thông tin bí mật và không được commit.

3. Mở Docker Desktop, build API và cập nhật schema cho database phát triển:

   ```powershell
   docker compose build api
   docker compose run --rm --no-deps api --apply-migrations
   docker compose up -d
   ```

   Nếu database đã có dữ liệu, hãy sao lưu trước khi cập nhật.

4. Kiểm tra API tại [http://localhost:5204/api/health/live](http://localhost:5204/api/health/live) hoặc xem tài liệu API tại [http://localhost:5204/scalar/v1](http://localhost:5204/scalar/v1). Chạy `docker compose logs -f api` để xem log và `docker compose down` để dừng các container.
