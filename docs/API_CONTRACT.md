# OCS API Contract

This is the only HTTP contract for OCS clients and backend contributors. It defines
the public endpoints that exist today and the conventions required for endpoints added
later. It is not a database schema.

## Scope and versioning

- Business endpoints will use `/api/v1`, for example `/api/v1/products`.
- Operational endpoints remain unversioned: `/api/health` and `/api/health/live`.
- JSON uses camelCase property names and UTF-8 encoding.
- Resource IDs are UUID strings. Timestamps use ISO 8601 UTC, for example
  `2026-10-21T03:15:00Z`. Dates use `YYYY-MM-DD` and times use `HH:mm:ss`.

No business controller is mapped in the current backend. A client must not treat the
examples in this document as implemented `/api/v1` endpoints until the endpoint appears
in OpenAPI/Scalar.

## Published operational endpoints

| Method | Path | Purpose | Authentication |
| --- | --- | --- | --- |
| `GET` | `/api/health` | Readiness check for Neon PostgreSQL, Redis and Cloudflare R2. | None |
| `GET` | `/api/health/live` | Liveness check for the API process only. | None |

`GET /api/health` returns the following shape. The `checks` object contains the
dependencies selected by the readiness predicate; each entry can be `Healthy`,
`Degraded` or `Unhealthy`.

```json
{
  "service": "OCS API",
  "status": "Healthy",
  "timestamp": "2026-10-23T15:00:00+00:00",
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

`/api/health/live` uses the same envelope with an empty `checks` object. ASP.NET Core
returns `200` for `Healthy` or `Degraded`, and `503` for `Unhealthy`.

## Contract baseline in the codebase

- `OCS.Api.Contracts.Common.ApiErrorCodes` is the canonical list of stable error codes.
- `PagedRequest` and `PagedResponse<T>` are the shared pagination types.
- `AddApiConventions()` is registered during API startup and standardizes validation
  errors as RFC 7807 `ProblemDetails` with `code` and `traceId`.
- The contract document and OpenAPI/Scalar must be updated in the same change as every
  newly published endpoint.

## Success responses

Use the resource DTO directly for a single resource. Do not return EF entities or
database table models from a controller.

| Operation | Status | Response |
| --- | --- | --- |
| Read one | `200 OK` | resource DTO |
| Create | `201 Created` | resource DTO and `Location` header |
| Update | `200 OK` | updated resource DTO |
| Delete | `204 No Content` | no body |
| List | `200 OK` | `PagedResponse<T>` |

Example resource response:

```json
{
  "id": "0d32e894-88c9-4083-ae03-bf673e4f0ddc",
  "barcode": "OCS-000001",
  "name": "Denim jacket",
  "listedPrice": "250000.00",
  "status": "PendingProcessing"
}
```

### Monetary values

Monetary values and commission rates must never be represented by binary floating-point
values. Domain and persistence use `decimal` / PostgreSQL `numeric`.

- API monetary values are decimal strings with two fractional digits, for example
  `"250000.00"`.
- API commission rates are decimal strings with up to two fractional digits, for example
  `"17.50"`.
- A client must not calculate totals with JavaScript `number`; the server is the source
  of truth for payable amounts and commissions.

Each future request/response DTO that contains money must parse and serialize these
values explicitly as `decimal` strings.

## Errors

All errors use RFC 7807 `application/problem+json`. Every response includes a stable
`code` for client behavior and a `traceId` for support and log correlation.

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "code": "VALIDATION_FAILED",
  "traceId": "00-7c0a...-01",
  "errors": {
    "pageSize": ["The field PageSize must be between 1 and 100."]
  }
}
```

| HTTP status | Error code | Meaning |
| --- | --- | --- |
| `400` | `VALIDATION_FAILED` | Request model or query validation failed. |
| `400` | `BAD_REQUEST` | Request cannot be processed. |
| `401` | `UNAUTHORIZED` | Missing, invalid, or expired bearer token. |
| `403` | `FORBIDDEN` | Authenticated caller lacks a required permission. |
| `404` | `NOT_FOUND` | Requested resource does not exist or is inaccessible. |
| `409` | `CONFLICT` | Request conflicts with current state, such as duplicate barcode. |
| `500` | `INTERNAL_ERROR` | Unexpected server error; never expose internal details. |

## Validation

- Controllers must use `[ApiController]`; invalid model binding automatically returns
  the `VALIDATION_FAILED` format above.
- Request DTOs use data annotations or feature-specific validators.
- Validate cross-field and business rules in Application handlers, not controllers.
- A handler turns expected business failures into the mapped `400`, `404`, or `409`
  response; unexpected exceptions flow to the global exception handler as `500`.

## Pagination

All collection endpoints accept these query parameters:

```text
GET /api/v1/products?page=1&pageSize=20
```

`page` starts at `1`; `pageSize` is from `1` to `100` and defaults to `20`.

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0,
  "totalPages": 0
}
```

Use `OCS.Api.Contracts.Common.PagedRequest` and `PagedResponse<T>` rather than
reimplementing pagination in each feature.

## Authentication and authorization

- Protected endpoints accept `Authorization: Bearer <JWT>`.
- Controllers/actions declare `[Authorize]`; public endpoints explicitly declare
  `[AllowAnonymous]`.
- Authorization is permission/policy based. Do not rely only on a UI role check.
- Return `401` when no valid identity exists; return `403` when identity exists but
  lacks permission.
- Never put JWTs, R2 credentials, passwords, account numbers, or internal audit data
  in a response DTO.

## OpenAPI and Scalar

In Development, ASP.NET Core publishes the OpenAPI document at `/openapi/v1.json` and
Scalar renders it at `/scalar/v1`. Run the API, then open:

```text
http://localhost:5204/scalar/v1
```

Every new controller action must declare its request/response DTOs, status responses,
authorization requirement, and XML summary so Scalar remains the current API reference.

## New endpoint checklist

1. Define request and response DTOs in `OCS.Api.Contracts/<Feature>/`.
2. Implement the use case in `OCS.Application/Features/<Feature>/`.
3. Add a thin controller action with `[ApiController]`, route, authorization, and
   documented response status codes.
4. Use the common error and pagination conventions in this document.
5. Add unit/integration tests and verify the endpoint in Scalar.
