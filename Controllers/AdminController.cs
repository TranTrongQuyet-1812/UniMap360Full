using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace UniMap360.Controllers
{
    [Authorize(Roles = "Admin")]
    [Route("Admin")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class AdminController : Controller
    {
        // GET: /Admin/Login → Redirect về trang Auth chung của hệ thống
        // Admin đăng nhập tại trang chung giống mọi user khác
        [AllowAnonymous]
        [HttpGet("Login")]
        public IActionResult Login()
        {
            return Redirect("/Home/Auth");
        }

        // GET: /Admin/Dashboard
        // GET: /Admin
        [HttpGet("")]
        [HttpGet("Dashboard")]
        public IActionResult Index()
        {
            ViewData["IsAdminArea"] = true;
            ViewData["ActiveAdminPage"] = "dashboard";
            return View();
        }

        // GET: /Admin/Users
        [HttpGet("Users")]
        public IActionResult Users()
        {
            ViewData["IsAdminArea"] = true;
            ViewData["ActiveAdminPage"] = "users";
            return View();
        }

        // GET: /Admin/Moderation/Rooms
        [HttpGet("Moderation/Rooms")]
        public IActionResult ModerationRooms()
        {
            ViewData["IsAdminArea"] = true;
            ViewData["ActiveAdminPage"] = "mod-rooms";
            return View();
        }

        // GET: /Admin/Moderation/Jobs
        [HttpGet("Moderation/Jobs")]
        public IActionResult ModerationJobs()
        {
            ViewData["IsAdminArea"] = true;
            ViewData["ActiveAdminPage"] = "mod-jobs";
            return View();
        }

        // GET: /Admin/AuditLogs
        [HttpGet("AuditLogs")]
        public IActionResult AuditLogs()
        {
            ViewData["IsAdminArea"] = true;
            ViewData["ActiveAdminPage"] = "audit-logs";
            return View();
        }

        // GET: /Admin/Cleanup
        [HttpGet("Cleanup")]
        public IActionResult Cleanup()
        {
            ViewData["IsAdminArea"] = true;
            ViewData["ActiveAdminPage"] = "cleanup";
            return View();
        }

        // GET: /Admin/Moderation/Roommates
        [HttpGet("Moderation/Roommates")]
        public IActionResult ModerationRoommates()
        {
            ViewData["IsAdminArea"] = true;
            ViewData["ActiveAdminPage"] = "mod-roommates";
            return View();
        }

        // GET: /Admin/ApiDocs (Swagger)
        [HttpGet("ApiDocs")]
        public IActionResult ApiDocs()
        {
            // Bảo mật: Chỉ hiển thị trên môi trường Development (Chạy Local)
            var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
            if (!env.IsDevelopment())
            {
                return NotFound();
            }

            ViewData["IsAdminArea"] = true;
            ViewData["ActiveAdminPage"] = "api-docs";
            return View();
        }
    }
}
