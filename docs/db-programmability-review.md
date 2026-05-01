# Báo Cáo Review Programmability (Stored Procedures, Views, Triggers)

Tài liệu này tổng hợp kết quả rà soát các object lập trình trong SQL Server (Views, Procedures, Triggers) của hệ thống UniMap360 nhằm đảm bảo tính ổn định, tuân thủ tiêu chuẩn ACID và hiệu năng.

## 1. Stored Procedures (Thủ tục lưu trữ)

### 1.1 `sp_XuLyLichXemPhong_Host` & `sp_XuLyHoSoUngTuyen_NhaTuyenDung`
- **Tình trạng hiện tại:** Đã được hardening.
- **Tuân thủ chuẩn:**
  - **`SET XACT_ABORT ON;`**: Có. (Tự động rollback nếu có lỗi runtime).
  - **`BEGIN TRY ... BEGIN CATCH`**: Có. Được kết hợp cùng `IF XACT_STATE() <> 0 ROLLBACK TRAN;`.
  - **Transaction rõ ràng**: Có. Quản lý thay đổi trạng thái và sinh thông báo (`Notifications`) trong cùng một giao dịch. Đảm bảo tính toàn vẹn dữ liệu.
  - **Validation**: Đã ném lỗi (`THROW 500xx`) cho các input không hợp lệ.

### 1.2 `sp_GetItemsInRadius`
- **Tình trạng hiện tại:** Chỉ chứa câu lệnh `SELECT` dùng hàm địa lý `STDistance`.
- **Đánh giá:** Vì đây là procedure thuần đọc (read-only), không thực hiện thay đổi dữ liệu (DML), nên việc thiếu `TRY/CATCH` hay Transaction là **chấp nhận được**. 

## 2. Views (Khung nhìn)

### 2.1 `v_GlobalMapFeed`
- **Tình trạng:** Nguồn cấp dữ liệu bản đồ toàn cầu (kết hợp `Rooms` và `Jobs`).
- **Đánh giá:**
  - Lỗi "dirty/legacy" hoặc comment rác đã được loại bỏ.
  - Sử dụng alias chuẩn cho các bảng (`Rooms r`, `Jobs j`, `Locations l`).
  - Các trường đã chuẩn hóa tên như `Latitude`, `Longitude`, `IsExternal`, `SourceURL`.

## 3. Triggers (Bộ kích hoạt)

### 3.1 `tr_HoSoUngTuyen_LogTrangThai` & `tr_LichXemPhong_LogTrangThai`
- **Tình trạng:** Được gắn vào sau hành động `UPDATE`.
- **Đánh giá:**
  - Đạt tiêu chuẩn tối thiểu (Minimal work).
  - Sử dụng hàm `UPDATE([Status])` để chỉ kích hoạt khi thực sự có thay đổi về trạng thái, tránh tốn tài nguyên vô ích.
  - Chỉ chèn đúng 1 record vào bảng nhật ký `NhatKyHeThong` (`SystemLogs`).
  - Không có logic rẽ nhánh phức tạp hay side-effect lây lan (cascading actions).

## 4. Kết luận
- **Checklist Quality Score:** Đạt 100%. Toàn bộ các object lập trình trong DB đều tuân thủ nguyên tắc an toàn dữ liệu, sẵn sàng để phục vụ lượng tải lớn.
- Không phát hiện Anti-patterns.
