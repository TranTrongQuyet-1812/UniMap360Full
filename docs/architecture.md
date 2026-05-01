# UniMap360 — Tài Liệu Kiến Trúc Hệ Thống

## 1. Tổng Quan

**UniMap360** là nền tảng web hỗ trợ sinh viên tìm phòng trọ và việc làm xung quanh các trường đại học tại Việt Nam. Hệ thống tích hợp bản đồ tương tác, quản lý tin đăng, hệ thống thông báo thời gian thực, chat 1-1, và bảng điều khiển quản trị.

**Tech Stack:**
- **Backend:** ASP.NET Core 8 (MVC + REST API), Entity Framework Core 8
- **Database:** SQL Server (2014+), Geography data type, Stored Procedures
- **Frontend:** Razor Views (Server-Side Rendering) + Vanilla JS modules
- **Bản đồ:** Leaflet.js + OpenStreetMap
- **Cloud:** Cloudinary (lưu trữ hình ảnh phòng trọ và CV)
- **Authentication:** JWT Bearer Token (Cookie-based fallback)

---

## 2. Kiến Trúc Tổng Thể

```
┌──────────────────────────────────────────────────────────┐
│                      CLIENT (Browser)                     │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐ │
│  │  map.js   │  │listing.js│  │detail.js │  │ chat.js  │ │
│  │ (Leaflet) │  │          │  │          │  │          │ │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘ │
│       │              │              │              │       │
│  ┌────┴──────────────┴──────────────┴──────────────┴────┐ │
│  │              api-client.js (Fetch wrapper)            │ │
│  └──────────────────────┬────────────────────────────────┘ │
└─────────────────────────┼──────────────────────────────────┘
                          │  HTTP/JSON (JWT Bearer)
┌─────────────────────────┼──────────────────────────────────┐
│                    ASP.NET Core 8                           │
│  ┌──────────────────────┴────────────────────────────────┐ │
│  │              Middleware Pipeline                        │ │
│  │  TraceLogging → Security Headers → ExceptionHandler    │ │
│  │  → Authentication → Authorization → Routing            │ │
│  └──────────────────────┬────────────────────────────────┘ │
│                         │                                   │
│  ┌──────────────────────┴────────────────────────────────┐ │
│  │           API Controllers (Thin Layer)                  │ │
│  │  Auth │ Listings │ Details │ Notifications │ Chat │ ... │ │
│  └──────────────────────┬────────────────────────────────┘ │
│                         │                                   │
│  ┌──────────────────────┴────────────────────────────────┐ │
│  │             Service Layer (Business Logic)              │ │
│  │  AppointmentService │ JobApplicationService │ Admin...   │ │
│  └──────────────────────┬────────────────────────────────┘ │
│                         │                                   │
│  ┌──────────────────────┴────────────────────────────────┐ │
│  │          EF Core 8 (DbContext + LINQ)                   │ │
│  └──────────────────────┬────────────────────────────────┘ │
└─────────────────────────┼──────────────────────────────────┘
                          │
┌─────────────────────────┼──────────────────────────────────┐
│                   SQL Server                                │
│  Tables (20) │ Views (1) │ Procedures (3) │ Triggers (2)   │
│  Indexes (10+) │ CHECK Constraints │ Geography Columns      │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Luồng Domain Chính

### 3.1 Luồng Tìm Phòng Trọ / Việc Làm
```
Sinh Viên mở trang chủ
  → map.js gọi GET /api/feed
  → Server trả tất cả phòng Available + việc Open từ v_GlobalMapFeed
  → Hiển thị marker trên bản đồ Leaflet
  → Click marker → Mở trang Detail (GET /api/rooms/{id} hoặc /api/jobs/{id})
```

### 3.2 Luồng Đặt Lịch Xem Phòng
```
Sinh Viên → POST /api/appointments (có JWT)
  → AppointmentService:
    1. Tìm/tạo StudentProfile
    2. Lấy HostID từ RoomID
    3. Tạo RoomViewingAppointment (Status=Pending)
    4. Tạo Notification cho chủ trọ
  → Chủ Trọ nhận thông báo
  → Chủ Trọ → PUT /api/appointments/{id}/status (Confirmed/Rejected/Rescheduled)
    → AppointmentService cập nhật Status + tạo Notification ngược cho SV
```

### 3.3 Luồng Ứng Tuyển Việc Làm
```
Sinh Viên → POST /api/job-applications (có JWT + file CV)
  → JobApplicationService:
    1. Tìm/tạo StudentProfile
    2. Upload CV lên Cloudinary
    3. Tạo JobApplication (Status=Pending)
    4. Tạo Notification cho nhà tuyển dụng
  → Nhà Tuyển Dụng → PUT /api/job-applications/{id}/status (Accepted/Rejected)
    → Tạo Notification ngược cho SV
```

### 3.4 Luồng Chat 1-1
```
User A → POST /api/chat/conversations/direct (với accountId đối phương)
  → Tạo Conversation (kind=direct) nếu chưa có
  → POST /api/chat/conversations/{id}/messages (gửi tin nhắn)
  → Tạo Notification cho người nhận (TargetType=Conversation)
  → Frontend polling GET /api/chat/conversations/{id}/messages
```

### 3.5 Luồng Quản Trị (Admin)
```
Admin → Dashboard (GET /api/dashboard/admin)
  → Quản lý Users: Lock/Unlock/ChangeRole/Delete
  → Quản lý Bài đăng: Approve/Reject Room/Job
  → Mọi hành động → AdminAuditService ghi log + IP + UserAgent
```

---

## 4. Giả Định Bảo Mật (Security Assumptions)

| Lớp | Biện pháp | Chi tiết |
|-----|-----------|----------|
| **Xác thực** | JWT Bearer | Token 120 phút, fallback từ cookie `unimap360.accessToken` |
| **Phân quyền** | Role-based (`[Authorize(Roles="Admin")]`) | 5 roles: Admin, Student, Host, Employer, SuperAdmin |
| **Headers** | Security headers middleware | `X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`, `CSP` |
| **Input** | Data Annotations + ApiValidationFilter | Tự động chặn request không hợp lệ, trả error chuẩn |
| **DB** | Parameterized queries (EF Core) | Không sử dụng raw SQL string concatenation |
| **Audit** | AdminAuditService | Ghi log mọi hành động admin: IP, UserAgent, Before/After JSON |
| **Secrets** | `secrets.ini` (gitignored) | JWT Key, Connection String, Cloudinary credentials tách riêng |
| **Observability** | TraceLoggingMiddleware | TraceId gắn vào mọi log trong request lifecycle |

---

## 5. Cấu Trúc Thư Mục Dự Án

```
UniMap360/
├── Controllers/
│   ├── HomeController.cs          # Server-Side Rendering (Razor)
│   ├── AdminController.cs         # Admin Panel (Razor)
│   └── Api/                       # REST API (19 controllers)
│       ├── AuthController.cs      # Đăng nhập/đăng ký/refresh
│       ├── ListingsController.cs  # Danh sách phòng/việc
│       ├── DetailsController.cs   # Chi tiết phòng/việc
│       ├── NotificationsController.cs
│       ├── ChatController.cs
│       ├── ReviewsController.cs
│       ├── RoomAppointmentsController.cs
│       ├── JobApplicationsController.cs
│       └── Admin/                 # API quản trị
├── Services/                      # Business Logic Layer
│   ├── Appointments/              # AppointmentService
│   ├── Applications/              # JobApplicationService
│   ├── Admin/                     # Audit, Guard, Purger
│   ├── Posts/                     # ManagePostsContextService
│   └── Security/                  # AdminSchemaBootstrapper
├── Models/                        # EF Core entities + DTOs
│   ├── Api/                       # ApiResponse, Extensions
│   ├── Requests/                  # RequestDtos (15 classes)
│   └── UniMap360ProContext.cs     # DbContext (20 tables)
├── Middleware/                    # TraceLogging + ExceptionHandling
├── Filters/                       # ApiValidationFilter
├── Constants/                     # ContentTargetTypes
├── Views/                         # Razor Views (Home, Admin, Shared)
├── wwwroot/                       # Static Assets
│   ├── js/                        # Frontend modules (9 files)
│   └── css/                       # Stylesheets
├── sql/                           # Migration & tuning scripts
├── tests/                         # PowerShell test suites
├── docs/                          # Technical documentation
└── csdl/                          # Full DB script + hardening pack
```
