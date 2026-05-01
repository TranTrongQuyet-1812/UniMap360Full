# Cẩm Nang Vận Hành Database (DB Ops Runbook)

Tài liệu này hướng dẫn cách thao tác với CSDL `UniMap360` dành cho các thành viên trong nhóm và giảng viên khi cần cài đặt, reset, hoặc kiểm tra hệ thống trong quá trình chấm điểm / demo.

## 1. Yêu cầu hệ thống
- SQL Server 2014 trở lên.
- SQL Server Management Studio (SSMS) hoặc Azure Data Studio.

## 2. Restore CSDL (Cài đặt mới)

Khi tải dự án về máy mới, hãy làm theo các bước sau để có CSDL mẫu với đầy đủ dữ liệu:

1. Mở SSMS và kết nối vào SQL Server.
2. Mở file `csdl/UniMap360Full.sql`.
3. Kiểm tra dòng 1-6 để đảm bảo lệnh `CREATE DATABASE [UniMap360_Pro];` có thể chạy an toàn (nếu DB đã tồn tại thì bạn cần xóa DB cũ trước, hoặc script sẽ bỏ qua bước tạo).
4. Nhấn **F5** (Execute) để chạy toàn bộ script. Quá trình này sẽ tạo các bảng (Views/Synonyms), chèn dữ liệu mẫu (Seed Data) và thiết lập index.
5. Cập nhật `appsettings.json` trong thư mục code C# để trỏ đúng `Server=` (vd: `Server=.;` hoặc `Server=.\SQLEXPRESS;`).

## 3. Seed Data (Tạo dữ liệu mẫu bổ sung)
Dữ liệu mẫu cơ bản đã nằm sẵn trong `UniMap360Full.sql`.
Nếu cần tạo thêm các kịch bản test (ví dụ tài khoản admin, dữ liệu vi phạm), hãy chạy các script nằm trong thư mục `sql/`:

- Tài khoản Super Admin mặc định: `admin@gmail.com` / `123456`
- Sinh viên test: `sv1@gmail.com` / `123456`
- Chủ trọ test: `host1@gmail.com` / `123456`

## 4. Reset Hệ Thống (Dọn dẹp sau demo)

Nếu dữ liệu bị rác trong quá trình demo và muốn đưa về trạng thái "sạch":
1. Xóa CSDL: `DROP DATABASE [UniMap360_Pro];`
2. Chạy lại script `csdl/UniMap360Full.sql`.
*(Quá trình này mất khoảng 5-10 giây, cực kỳ an toàn để làm ngay giữa buổi demo nếu có sự cố).*

## 5. Verify (Kiểm tra sức khỏe DB)

Để đảm bảo mọi Constraint, Procedure, và Index hoạt động trơn tru:
1. Mở file `sql/phase3_03_data_quality_checks.sql` trong SSMS.
2. Nhấn **F5** chạy.
3. Kiểm tra Output: Nếu cột `IssueCount` của tất cả các loại lỗi đều là `0` thì CSDL hoàn toàn "sạch sẽ".

## 6. Lệnh SQL thường dùng khi Demo
- **Check User:** `SELECT * FROM Accounts WHERE Email = '...';`
- **Check Token Lock:** `SELECT AccountID, IsLocked, LockedReason FROM Accounts;`
- **Xem nhanh Log Hệ Thống:** `SELECT TOP 50 * FROM SystemLogs ORDER BY ActionTime DESC;`
