# 🗺️ UniMap360

![UniMap360 Banner](https://img.shields.io/badge/.NET%208-512BD4?style=for-the-badge&logo=dotnet&logoColor=white) ![PostgreSQL](https://img.shields.io/badge/PostgreSQL-316192?style=for-the-badge&logo=postgresql&logoColor=white) ![Supabase](https://img.shields.io/badge/Supabase-3ECF8E?style=for-the-badge&logo=supabase&logoColor=white) ![Bootstrap](https://img.shields.io/badge/Bootstrap-563D7C?style=for-the-badge&logo=bootstrap&logoColor=white)

**UniMap360** là một ứng dụng nền web (Web Application) toàn diện hỗ trợ sinh viên trong việc tìm kiếm phòng trọ và bạn ở ghép. Hệ thống tích hợp bản đồ số để trực quan hóa vị trí phòng trọ, tối ưu hóa quá trình tìm kiếm với các bộ lọc thông minh, và mang đến trải nghiệm UI/UX hiện đại, mượt mà.

---

## ✨ Tính năng nổi bật

- **🗺️ Bản đồ tương tác:** Xem vị trí phòng trọ trực tiếp trên bản đồ số, kết hợp hiển thị thông tin tóm tắt và đánh dấu thông minh.
- **🔍 Bộ lọc nâng cao (Real-time Filter):** Tìm kiếm phòng trọ và bạn ở ghép theo ngân sách, giới tính, hashtag thói quen sinh hoạt và các tiêu chí khác mà không cần tải lại trang.
- **🔐 Quản lý phân quyền (Authorization & Authentication):** 
  - Hệ thống xác thực bằng JWT bảo mật cao.
  - Phân cấp người dùng: Sinh viên, Chủ trọ, Quản trị viên (Super Admin).
- **☁️ Cloud Storage:** Tích hợp **Cloudinary** để quản lý và tối ưu hóa hình ảnh/video của các bài đăng.
- **📧 Giao tiếp tự động:** Tích hợp hệ thống tự động gửi Email thông báo khi có người muốn ghép trọ hoặc khi có cập nhật từ hệ thống.
- **📱 Responsive Design:** Giao diện tối ưu hoàn toàn cho thiết bị di động (Mobile-first) và máy tính bàn, sử dụng kiến trúc CSS Grid/Flexbox và Bootstrap.

---

## 🛠️ Công nghệ sử dụng

### Back-end
- **Framework:** ASP.NET Core MVC 8.0
- **Database:** Tích hợp đa nền tảng (Hỗ trợ cả PostgreSQL - Supabase và Microsoft SQL Server).
- **ORM:** Entity Framework Core (với PostGIS / NetTopologySuite xử lý dữ liệu bản đồ).
- **Authentication:** JWT Bearer Auth & Cookie-based Auth.
- **Architecture:** Repository Pattern & N-Tier Architecture (Services, Controllers, Models).

### Front-end
- **Cốt lõi:** HTML5, Vanilla JavaScript, CSS3.
- **Thư viện giao diện:** Bootstrap 5.
- **Bản đồ:** Leaflet.js / VietMap API.
- **Icon & UI:** FontAwesome, Google Fonts.

### DevOps & Deployment
- **Hosting:** Railway (App), Supabase (PostgreSQL Database).
- **CI/CD:** GitHub Actions (tích hợp Docker).

---

## 🚀 Hướng dẫn cài đặt (Local Development)

### 1. Yêu cầu hệ thống
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (hoặc PostgreSQL)
- Visual Studio 2022 / JetBrains Rider / VS Code

### 2. Cài đặt và Chạy thử
1. **Clone dự án về máy:**
   ```bash
   git clone https://github.com/TranTrongQuyet-1812/UniMap360.git
   cd UniMap360
   ```

2. **Cấu hình Database & Secret Keys:**
   - Tạo file `secrets.ini` ở thư mục gốc của project (nơi chứa file `.csproj`).
   - Copy nội dung dưới đây và thay đổi Chuỗi kết nối (Connection String) phù hợp với máy của bạn:

   ```ini
   [ConnectionStrings]
   DefaultConnection=Server=.;Database=UniMap360;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
   
   [Database]
   Provider=SqlServer
   ```

3. **Cập nhật Database (Migration):**
   ```bash
   dotnet ef database update
   ```

4. **Chạy ứng dụng:**
   ```bash
   dotnet run
   ```
   Ứng dụng sẽ tự động mở ở địa chỉ `https://localhost:71xx` hoặc `http://localhost:51xx`.

---

## 🛡️ License
Dự án được cấp phép theo tiêu chuẩn [MIT License](LICENSE).

---
*Dự án Đồ Án Sinh Viên được thiết kế và phát triển bởi [TranTrongQuyet-1812](https://github.com/TranTrongQuyet-1812).*
