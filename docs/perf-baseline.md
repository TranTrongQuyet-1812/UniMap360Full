# Performance Baseline

*Ghi chú: Đây là mức baseline thu thập ở môi trường Local Development (Dev Server). Thời gian có thể dao động tùy vào tải của CPU.*

## 1. Endpoint Nóng (Phản hồi ước tính / Local)

| Endpoint | Method | Chức năng | Thời gian trung bình (ms) | Ghi chú |
| :--- | :--- | :--- | :--- | :--- |
| `/api/listings/cards` | GET | Lấy Feed (Map/Listing) | 120ms - 250ms | Cần join nhiều bảng + query chuỗi địa lý |
| `/api/auth/login` | POST | Xác thực người dùng | 50ms - 80ms | Bcrypt hash verify |
| `/api/admin/rooms` | GET | Lấy DS phòng Admin | 150ms - 300ms | Có Include Entity, Like search |
| `/` (Trang chủ) | GET | Trả về HTML Index | 20ms - 40ms | Cache tĩnh |
| `/api/map` | GET | Global Map feed | 100ms - 200ms | View `v_GlobalMapFeed` |

## 2. Vấn đề Hiệu năng Cần Theo Dõi (Performance Hotspots)
- **Truy vấn chuỗi LIKE**: Trong `ListingsController.cs` và `AdminModerationController.cs`, sử dụng nhiều `EF.Functions.Like(..., "%keyword%")` gây full table scan.
- **Lấy View `v_GlobalMapFeed`**: Nếu dữ liệu phình to (vd: hàng trăm ngàn Phòng/Việc làm), có thể chậm nếu thiếu Index thích hợp trên `ItemType` hoặc `CreatedAt`.
- **Render ảnh Cloudinary**: Hiện tại API trả URL gốc, không áp dụng biến đổi tối ưu kích thước ảnh (vd: `q_auto,f_auto,w_500`), có thể gây tốn băng thông client.

## 3. Phương pháp Test
- **Tool**: Tích hợp cURL / Chrome DevTools Network Tab.
- **Payload**: Test với Database có `~1000` phòng trọ và `~600` việc làm.
- **Tiêu chí tối ưu (Pass criteria)**: Thời gian truy vấn DB với các API đọc danh sách (`Listings`, `Map`) giảm < 100ms thông qua Index Tuning.
