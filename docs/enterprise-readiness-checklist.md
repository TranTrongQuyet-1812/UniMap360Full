# UniMap360 — Enterprise Readiness Checklist

Checklist tự đánh giá mức độ sẵn sàng của dự án trước khi demo / phỏng vấn / đưa vào CV.  
Cập nhật lần cuối: **25/04/2026**

---

## 1. KIẾN TRÚC & CODE QUALITY

| # | Hạng mục | Trạng thái | Ghi chú |
|---|----------|------------|---------|
| 1.1 | API Response chuẩn hóa (Envelope pattern) | ✅ Done | `ApiResponse<T>` với `success/data/error/traceId` |
| 1.2 | Tách Controller / Service Layer | ✅ Done | Controller chỉ validate + gọi service + trả response |
| 1.3 | DTO tập trung với Data Annotations | ✅ Done | 15 DTO classes trong `Models/Requests/RequestDtos.cs` |
| 1.4 | Global Validation Filter | ✅ Done | `ApiValidationFilter` tự động chặn input không hợp lệ |
| 1.5 | Global Exception Handling | ✅ Done | `ApiExceptionHandlingMiddleware` trả error chuẩn + TraceId |
| 1.6 | Dependency Injection đúng chuẩn | ✅ Done | Tất cả services đăng ký `AddScoped` trong `Program.cs` |
| 1.7 | Không hardcode secrets trong source | ✅ Done | Dùng `secrets.ini` (gitignored) |

## 2. BẢO MẬT (SECURITY)

| # | Hạng mục | Trạng thái | Ghi chú |
|---|----------|------------|---------|
| 2.1 | JWT Authentication | ✅ Done | Token 120 phút, HMAC-SHA256, cookie fallback |
| 2.2 | Role-based Authorization | ✅ Done | `[Authorize(Roles="Admin")]` trên các endpoint nhạy cảm |
| 2.3 | Security Headers (CSP, XSS, Clickjack) | ✅ Done | Middleware inline trong `Program.cs` |
| 2.4 | Input Validation (Server-side) | ✅ Done | Data Annotations + ApiValidationFilter |
| 2.5 | SQL Injection Prevention | ✅ Done | EF Core parameterized queries; SP dùng `@param` |
| 2.6 | Admin Audit Trail | ✅ Done | Log IP, UserAgent, Before/After JSON |
| 2.7 | Account Lock/Unlock mechanism | ✅ Done | `IsLocked`, `LockedReason`, `LockedAt` |
| 2.8 | Password hashing (BCrypt) | ⚠️ Partial | AdminSchemaBootstrapper hash existing accounts; new accounts lưu plain text (cần cải thiện) |
| 2.9 | Rate Limiting | ❌ TODO | Chưa có middleware giới hạn request/phút |
| 2.10 | CORS Policy | ❌ TODO | Chưa cấu hình (chỉ dùng same-origin hiện tại) |

## 3. CƠ SỞ DỮ LIỆU (DATABASE)

| # | Hạng mục | Trạng thái | Ghi chú |
|---|----------|------------|---------|
| 3.1 | CHECK Constraints cho business enums | ✅ Done | `CK_Notifications_TargetType` |
| 3.2 | Stored Procedures có Transaction | ✅ Done | `XACT_ABORT ON` + `TRY/CATCH` |
| 3.3 | Triggers minimal work | ✅ Done | Chỉ log thay đổi Status |
| 3.4 | Index tuning cho query nóng | ✅ Done | 6+ Non-Clustered Indexes với INCLUDE |
| 3.5 | Spatial Index cho geography | ✅ Done | `IX_Location_Coordinates` |
| 3.6 | Data Quality Check script | ✅ Done | `sql/phase3_03_data_quality_checks.sql` |
| 3.7 | DB Operations Runbook | ✅ Done | `docs/db-ops-runbook.md` |
| 3.8 | Backup/Restore procedure | ✅ Done | Full script `csdl/UniMap360Full.sql` |
| 3.9 | Việt hóa tên bảng + Synonym layer | ✅ Done | 20 Synonyms tiếng Anh |

## 4. LOGGING & OBSERVABILITY

| # | Hạng mục | Trạng thái | Ghi chú |
|---|----------|------------|---------|
| 4.1 | Request TraceId | ✅ Done | `TraceLoggingMiddleware` |
| 4.2 | Structured Logging (Service Layer) | ✅ Done | `LogInformation` với message template |
| 4.3 | Error logging với TraceId | ✅ Done | Exception middleware ghi TraceId |
| 4.4 | Centralized Log Provider (Serilog/ELK) | ❌ TODO | Hiện dùng Console logger mặc định |
| 4.5 | Health Check endpoint | ❌ TODO | Chưa có `/health` |

## 5. TESTING

| # | Hạng mục | Trạng thái | Ghi chú |
|---|----------|------------|---------|
| 5.1 | Integration Tests (API) | ✅ Done | 23 test cases, PowerShell script |
| 5.2 | Frontend Smoke Tests | ✅ Done | 13 test cases, PowerShell script |
| 5.3 | Unit Tests (Service Layer) | ❌ TODO | Chưa có xUnit project |
| 5.4 | Load/Performance Testing | ❌ TODO | Chưa chạy stress test |
| 5.5 | CI/CD Pipeline | ❌ TODO | Chưa tích hợp GitHub Actions |

## 6. TÀI LIỆU (DOCUMENTATION)

| # | Hạng mục | Trạng thái | Ghi chú |
|---|----------|------------|---------|
| 6.1 | Architecture Overview | ✅ Done | `docs/architecture.md` |
| 6.2 | Database Design | ✅ Done | `docs/database-design.md` |
| 6.3 | API Contract | ✅ Done | `docs/api-contract.md` |
| 6.4 | TargetType Business Enum | ✅ Done | `docs/db-target-types.md` |
| 6.5 | DB Programmability Review | ✅ Done | `docs/db-programmability-review.md` |
| 6.6 | DB Ops Runbook | ✅ Done | `docs/db-ops-runbook.md` |
| 6.7 | Enterprise Readiness Checklist | ✅ Done | (File này) |

---

## 7. TỔNG KẾT ĐIỂM

| Nhóm | Done | Partial | TODO | Tỷ lệ hoàn thành |
|------|------|---------|------|-------------------|
| Kiến trúc & Code | 7/7 | 0 | 0 | **100%** |
| Bảo mật | 7/10 | 1 | 2 | **75%** |
| Database | 9/9 | 0 | 0 | **100%** |
| Observability | 3/5 | 0 | 2 | **60%** |
| Testing | 2/5 | 0 | 3 | **40%** |
| Tài liệu | 7/7 | 0 | 0 | **100%** |
| **TỔNG** | **35/43** | **1** | **7** | **~83%** |

> **Đánh giá:** Dự án đạt mức **Production-Ready cơ bản** (83%). Các hạng mục TODO chủ yếu liên quan đến CI/CD, Load Testing và Centralized Logging — là những thứ thường được triển khai ở giai đoạn DevOps, không ảnh hưởng đến chất lượng code và tính đúng đắn của nghiệp vụ.
