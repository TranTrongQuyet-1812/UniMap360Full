# UniMap360 API Contract (Chuẩn hóa Phase 2)

Tài liệu này định nghĩa cấu trúc chuẩn cho tất cả các phản hồi từ API của hệ thống UniMap360. 
Từ Phase 2 trở đi, **mọi endpoint mới và cũ** đều phải tuân thủ nghiêm ngặt Envelope Format dưới đây.

---

## 1. Response Envelope

Tất cả các API Response phải được bọc trong một đối tượng (Envelope) có cấu trúc cố định:

```json
{
    "success": true | false,
    "data": { ... } | [ ... ] | null,
    "error": {
        "code": "STRING_CODE",
        "message": "User-friendly message",
        "details": { ... } | null
    } | null,
    "traceId": "string" | null
}
```

### Các trường (Fields):
- `success` (boolean): Bắt buộc. Xác định logic xử lý thành công hay thất bại.
- `data` (object | array | null): Payload thực tế trả về khi `success: true`. Nếu `success: false`, `data` thường là `null`.
- `error` (object | null): Bắt buộc có khi `success: false`. Chứa thông tin lỗi chi tiết.
- `traceId` (string | null): ID truy vết từ hệ thống (Correlation ID), hữu ích cho việc debug log khi có lỗi xảy ra.

---

## 2. Quy Ước Trả Về Lỗi (Error Handling)

### Format `error` object:
```json
{
    "code": "VALIDATION_ERROR",
    "message": "Dữ liệu đầu vào không hợp lệ.",
    "details": {
        "Email": ["Email không đúng định dạng."],
        "Password": ["Mật khẩu phải dài hơn 8 ký tự."]
    }
}
```

### Danh sách mã lỗi (Error Codes) thông dụng:
- `BAD_REQUEST`: Lỗi chung do client gửi yêu cầu sai.
- `VALIDATION_ERROR`: Dữ liệu đầu vào không qua được quá trình kiểm tra.
- `UNAUTHORIZED`: Chưa xác thực (Chưa có token hoặc token hết hạn).
- `FORBIDDEN`: Đã xác thực nhưng không có quyền thực hiện hành động.
- `NOT_FOUND`: Tài nguyên không tồn tại.
- `INTERNAL_SERVER_ERROR`: Lỗi hệ thống, exception unhandled.

---

## 3. Quy Ước Phân Trang (Pagination)

Dữ liệu phân trang sẽ nằm toàn bộ trong object `data` thay vì rải rác ngoài cấp cao nhất:

```json
{
    "success": true,
    "data": {
        "page": 1,
        "pageSize": 20,
        "total": 150,
        "totalPages": 8,
        "items": [
            { "id": 1, "name": "Item 1" },
            { "id": 2, "name": "Item 2" }
        ]
    },
    "error": null,
    "traceId": null
}
```

- Không lặp lại thuộc tính `data` và `items` ngang hàng như ở Phase 0. Chỉ dùng `items` để chứa mảng kết quả.

---

## 4. Middleware & Xử Lý Tự Động

- `ApiExceptionHandlingMiddleware`: Bắt tất cả Exception chưa được handle và trả về HTTP 500 với cấu trúc `ApiError` code `INTERNAL_SERVER_ERROR`.
- **(Trong tương lai)**: Sẽ có filter để bọc tự động các BadRequest (từ ModelState) thành mã lỗi `VALIDATION_ERROR`.
