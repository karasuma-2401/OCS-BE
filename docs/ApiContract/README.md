# Hợp đồng API

## Phạm vi hiện tại

Backend hiện chưa có controller nghiệp vụ. JWT, CORS, OpenAPI và pipeline xác thực đã được cấu hình, nhưng các endpoint về tài khoản, lịch hẹn, sản phẩm, POS hay giải ngân chưa được map trong source. Vì vậy, không nên công bố request/response mẫu cho các phân hệ đó trước khi chúng được hiện thực.

Các use case và yêu cầu dự kiến nằm tại [Requirements.md](../Requirements.md) và [UseCases.md](../UseCases.md); chúng không phải hợp đồng HTTP đang chạy.

## Endpoint đang có

| Phương thức | Đường dẫn | Mục đích | Xác thực |
| --- | --- | --- | --- |
| `GET` | `/api/health` | Readiness: kiểm tra Neon PostgreSQL, Redis và Cloudflare R2. | Không |
| `GET` | `/api/health/live` | Liveness: chỉ xác nhận tiến trình API còn hoạt động. | Không |

### `GET /api/health`

Endpoint chạy các health check mang tag `ready`: `neon`, `redis`, `r2`. Redis không cấu hình sẽ dùng memory cache nhưng health check trả trạng thái `Degraded`; R2 chưa cấu hình cũng được phản ánh trong kết quả readiness.

Ví dụ cấu trúc phản hồi:

```json
{
  "service": "OCS API",
  "status": "Healthy",
  "timestamp": "2026-10-23T00:00:00+00:00",
  "durationMs": 12.34,
  "checks": {
    "neon": {
      "status": "Healthy",
      "description": null,
      "durationMs": 3.21
    }
  }
}
```

Mã phản hồi dùng quy ước mặc định của ASP.NET Core Health Checks: `200` khi `Healthy` hoặc `Degraded`, `503` khi `Unhealthy`.

### `GET /api/health/live`

Liveness không chạy health check phụ thuộc. Trường `checks` vì vậy rỗng; endpoint chỉ dùng để hạ tầng xác định API process có đang sống hay không.

## Tài liệu tương tác ở môi trường Development

Khi `ASPNETCORE_ENVIRONMENT=Development`, API công bố:

| Đường dẫn | Nội dung |
| --- | --- |
| `/openapi/v1.json` | Tài liệu OpenAPI được sinh từ endpoint đã map. |
| `/scalar/v1` | Giao diện Scalar để xem và thử API. |

Không bật các endpoint này theo pipeline hiện tại ở môi trường không phải Development.

## Quy ước khi bổ sung endpoint nghiệp vụ

Những quy ước sau là hướng dẫn cho API sắp triển khai, không phải endpoint có sẵn:

- Dùng tiền tố phiên bản, ví dụ `/api/v1/appointments`, trước khi phát hành API cho client.
- Mọi endpoint thao tác dữ liệu phải khai báo rõ vai trò/quyền yêu cầu và giới hạn dữ liệu theo người dùng đang đăng nhập.
- Các nghiệp vụ thay đổi trạng thái sản phẩm, thanh toán, đồng bộ POS và giải ngân cần trả lỗi có mã ổn định để Desktop/Mobile xử lý được.
- Sự kiện đồng bộ POS phải có `EventId` idempotent; khi có xung đột phải trả trạng thái để đối soát thay vì tự ghi đè.
- Không đưa chuỗi kết nối, JWT key hoặc khóa Cloudflare R2 vào payload hay OpenAPI mẫu.
