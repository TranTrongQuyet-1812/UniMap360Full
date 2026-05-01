# API Baseline Contract (Hiện trạng)

## Đánh giá chung
- **Success Response**: Trả về flat JSON object (VD: `return Ok(new { message = "...", data = ... })`).
- **Error Response**: Rất thiếu đồng nhất. Có chỗ trả về chuỗi text thô (`return BadRequest("Lỗi")`), có chỗ trả JSON (`return NotFound(new { message = "Lỗi" })`).
- **Pagination**: Trả về `page, pageSize, total, totalPages, items, data` (lặp data `items` và `data` trong cùng 1 response).
- **Thiếu Error Envelope chuẩn**: Chưa có cấu trúc `{ success, data, error, traceId }`.

---

## 1. Auth API (`/api/auth`)
### POST `/api/auth/login`
- **Success**:
  ```json
  {
      "accessToken": "eyJhbGci...",
      "accountId": 1,
      "email": "admin@unimap.com",
      "role": "Student"
  }
  ```
- **Error**: `HTTP 400/401` với Body là text thô: `Sai email hoặc mật khẩu.`

## 2. Listings API (`/api/listings/cards`)
### GET `/api/listings/cards`
- **Success**:
  ```json
  {
      "page": 1,
      "pageSize": 20,
      "total": 100,
      "totalPages": 5,
      "items": [ { "id": 1, "type": "room", ... } ],
      "data": [ { "id": 1, "type": "room", ... } ]
  }
  ```
- **Error**: Trả về rỗng (nếu có lỗi hệ thống thì rơi vào exception page).

## 3. Detail API (`/api/admin/rooms/{id}`)
- **Success**: 
  ```json
  {
      "roomId": 1,
      "title": "Phòng trọ...",
      "status": "Available",
      "host": { "accountId": 2, ... },
      "location": { "provinceName": "Hồ Chí Minh", ... }
  }
  ```
- **Error**: `HTTP 404` 
  ```json
  {
      "message": "Không tìm thấy phòng."
  }
  ```

## 4. Moderation API (`/api/admin/rooms/{id}/approve`)
- **Success**:
  ```json
  {
      "message": "Cập nhật trạng thái phòng thành công.",
      "roomId": 1,
      "status": "Available"
  }
  ```
- **Error**: `HTTP 401` Text thô `Token không hợp lệ.` hoặc JSON `{"message": "Không tìm thấy phòng."}`
