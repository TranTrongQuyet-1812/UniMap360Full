# UniMap360 — Thiết Kế Cơ Sở Dữ Liệu

## 1. Tổng Quan

Cơ sở dữ liệu sử dụng **SQL Server** với **20 bảng chính**, hỗ trợ kiểu dữ liệu `geography` cho tọa độ bản đồ. Toàn bộ tên bảng vật lý đã được Việt hóa (ví dụ: `PhongTro`, `ViecLam`, `TaiKhoan`), đồng thời tạo **Synonyms tiếng Anh** để code C# truy vấn thuận tiện (ví dụ: `Rooms` → `PhongTro`).

---

## 2. Sơ Đồ Quan Hệ (ERD Tóm Tắt)

```
┌─────────────┐     ┌──────────────┐     ┌──────────────┐
│  TaiKhoan   │────→│ HoSoSinhVien │     │  HoSoChuTro  │
│  (Accounts) │     │(StudentProf.)|     │(HostProfiles)|
│             │────→│              │     │              │
│  AccountID  │     │  StudentID   │     │   HostID     │
│  Email      │     │  FullName    │     │   FullName   │
│  UserRole   │     │  University  │     │   Phone      │
│  IsLocked   │     │  Major       │     │   IDCard     │
└──────┬──────┘     └──────┬───────┘     └──────┬───────┘
       │                   │                     │
       │    ┌──────────────┼─────────────────────┘
       │    │              │
       │    │    ┌─────────┴────────┐     ┌──────────────┐
       │    │    │   LichXemPhong   │────→│   PhongTro   │
       │    │    │  (Appointments)  │     │   (Rooms)    │
       │    │    │  AppointmentID   │     │   RoomID     │
       │    │    │  Status          │     │   Title      │
       │    │    │  ScheduledAt     │     │   Price      │
       │    │    └──────────────────┘     │   Area       │
       │    │                             │   LocationID │
       │    │    ┌──────────────────┐     └──────┬───────┘
       │    └───→│  HoSoUngTuyen   │             │
       │         │(JobApplications)|     ┌───────┴───────┐
       │         │  ApplicationID  │     │   DiaDiem     │
       │         │  Status         │     │  (Locations)  │
       │         │  CvURL          │     │  LocationID   │
       │         └────────┬────────┘     │  Coordinates  │ ← geography
       │                  │              │  AddressText  │
       │         ┌────────┴────────┐     │  District     │
       │         │    ViecLam      │     │  ProvinceName │
       │         │    (Jobs)       │     └───────────────┘
       │         │  JobID          │
       │         │  JobTitle       │
       │         │  SalaryRange    │
       │         │  EmployerID ────┼──→ HoSoNhaTuyenDung (EmployerProfiles)
       │         └─────────────────┘
       │
       │    ┌──────────────────┐     ┌──────────────────┐
       ├───→│    ThongBao      │     │     DanhGia      │
       │    │ (Notifications)  │     │    (Reviews)     │
       │    │  NotificationID  │     │    ReviewID      │
       │    │  TargetType      │     │    TargetType    │
       │    │  TargetID        │     │    TargetID      │
       │    │  IsRead          │     │    Rating        │
       │    └──────────────────┘     └──────────────────┘
       │
       │    ┌──────────────────┐     ┌──────────────────┐
       ├───→│  CuocTroChuyen   │────→│     TinNhan      │
       │    │ (Conversations)  │     │    (Messages)    │
       │    │  ConversationID  │     │    MessageID     │
       │    │  Kind (direct)   │     │    Content       │
       │    └──────────────────┘     └──────────────────┘
       │
       └───→│  YeuThich (Favorites)  │  TepDaPhuongTien (Media)  │
```

---

## 3. Danh Sách Bảng Chi Tiết

| # | Tên Vật Lý (VN) | Synonym (EN) | Vai trò | Records (ước tính) |
|---|------------------|--------------|---------|---------------------|
| 1 | TaiKhoan | Accounts | Tài khoản đăng nhập (email, password, role) | ~120 |
| 2 | HoSoSinhVien | StudentProfiles | Hồ sơ sinh viên (tên, trường, ngành) | ~50 |
| 3 | HoSoChuTro | HostProfiles | Hồ sơ chủ trọ (tên, SĐT, CCCD) | ~30 |
| 4 | HoSoNhaTuyenDung | EmployerProfiles | Hồ sơ nhà tuyển dụng (công ty, MST) | ~10 |
| 5 | PhongTro | Rooms | Bài đăng phòng trọ | ~3000 |
| 6 | ViecLam | Jobs | Bài đăng việc làm | ~500 |
| 7 | DiaDiem | Locations | Tọa độ GPS + địa chỉ phân cấp | ~3500 |
| 8 | DanhMuc | Categories | Danh mục (Room/Job categories) | 6 |
| 9 | TepDaPhuongTien | Media | Hình ảnh phòng trọ/việc làm (Cloudinary) | ~5000 |
| 10 | LichXemPhong | RoomViewingAppointments | Lịch hẹn xem phòng | ~30 |
| 11 | HoSoUngTuyen | JobApplications | Hồ sơ ứng tuyển (kèm CV) | ~20 |
| 12 | DanhGia | Reviews | Đánh giá phòng/việc (1-5 sao) | ~50 |
| 13 | ThongBao | Notifications | Thông báo (polymorphic TargetType) | ~200 |
| 14 | CuocTroChuyen | Conversations | Cuộc hội thoại | ~10 |
| 15 | ThanhVienCuocTroChuyen | ConversationParticipants | Thành viên tham gia chat | ~20 |
| 16 | TinNhan | Messages | Tin nhắn | ~100 |
| 17 | YeuThich | Favorites | Danh sách yêu thích | ~30 |
| 18 | NhatKyHeThong | SystemLogs | Log hệ thống (trigger ghi) | ~50 |
| 19 | NhatKyKiemToanQuanTri | AdminAuditLogs | Log kiểm toán admin | ~20 |
| 20 | CaiDatUngDung | AppSettings | Cấu hình hệ thống (key-value) | 1 |

---

## 4. Điểm Nổi Bật Thiết Kế

### 4.1 Geography Data Type
Bảng `DiaDiem` sử dụng kiểu `geography` của SQL Server để lưu tọa độ GPS (SRID 4326). Cho phép tính khoảng cách giữa người dùng và các bài đăng bằng hàm `STDistance()` trong Stored Procedure `sp_GetItemsInRadius`.

### 4.2 Polymorphic References (TargetType + TargetID)
Các bảng `Notifications`, `Reviews`, `Media`, `Favorites` sử dụng cặp cột (`TargetType`, `TargetID`) để tham chiếu đa hình:
- `TargetType = 'Room'` → `TargetID` = `RoomID`
- `TargetType = 'Job'` → `TargetID` = `JobID`
- `TargetType = 'Conversation'` → `TargetID` = `ConversationID`

Đã tạo CHECK constraint `CK_Notifications_TargetType` để kiểm soát giá trị hợp lệ.

### 4.3 Synonym Layer (Việt Hóa)
Toàn bộ tên bảng vật lý đã được Việt hóa theo yêu cầu môn học. Đồng thời tạo **Synonyms tiếng Anh** (ví dụ: `Rooms` → `PhongTro`) để code C# và EF Core truy vấn không bị ảnh hưởng.

### 4.4 View `v_GlobalMapFeed`
View tổng hợp phòng trọ (`Available`) + việc làm (`Open`) thành một nguồn dữ liệu duy nhất để hiển thị trên bản đồ. Được sử dụng bởi cả API `/api/feed` và `/api/listings/cards`.

### 4.5 Indexes
- **Spatial Index** `IX_Location_Coordinates` trên `DiaDiem.Coordinates` cho truy vấn bán kính.
- **Composite Indexes** cho các query nóng: `IX_Rooms_Status_Location_Category`, `IX_Jobs_Status_Location_Category`, `IX_HoSoUngTuyen_Student_Status_Application`, v.v.

### 4.6 Triggers (Ghi Log Tự Động)
- `tr_HoSoUngTuyen_LogTrangThai`: Ghi log thay đổi trạng thái ứng tuyển.
- `tr_LichXemPhong_LogTrangThai`: Ghi log thay đổi trạng thái lịch xem phòng.
Cả hai trigger chỉ kích hoạt khi cột `Status` thay đổi, và chỉ chèn 1 dòng vào `NhatKyHeThong` (Minimal Work).

### 4.7 Stored Procedures (Transaction-Safe)
- `sp_XuLyLichXemPhong_Host`: Xử lý phản hồi lịch xem phòng + tạo thông báo (trong 1 transaction).
- `sp_XuLyHoSoUngTuyen_NhaTuyenDung`: Xử lý phản hồi ứng tuyển + tạo thông báo (trong 1 transaction).
- `sp_GetItemsInRadius`: Tìm phòng/việc trong bán kính GPS (read-only).

Cả hai procedure DML đều có: `SET XACT_ABORT ON`, `BEGIN TRY/CATCH`, `ROLLBACK TRAN` khi lỗi.
