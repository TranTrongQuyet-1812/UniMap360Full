using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using UniMap360.Models;

namespace UniMap360.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly UniMap360ProContext _context;

        public HomeController(ILogger<HomeController> logger, UniMap360ProContext context)
        {
            _logger = logger;
            _context = context;
        }

        public IActionResult Intro()
        {
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "Admin");
            }
            ViewData["IsIntroPage"] = true;
            ViewData["ActivePage"] = "intro";
            return View();
        }

        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "Admin");
            }
            ViewData["IsMapPage"] = true;
            ViewData["ActivePage"] = "map";
            return View();
        }

        public IActionResult Listing()
        {
            ViewData["ActivePage"] = "listing";
            return View();
        }

        public async Task<IActionResult> Detail(string type, int id)
        {
            ViewData["ActivePage"] = "detail";

            // SEO Logic: Lấy thông tin cơ bản để crawler thấy được tiêu đề/ảnh
            if (type == "room")
            {
                var room = await _context.Rooms.FindAsync(id);
                if (room != null)
                {
                    ViewData["Title"] = room.Title;
                    ViewData["MetaDescription"] = room.Description != null && room.Description.Length > 150 
                        ? room.Description.Substring(0, 147) + "..." 
                        : room.Description;
                    
                    var media = _context.Media.FirstOrDefault(m => m.TargetType == "Room" && m.TargetId == id);
                    if (media != null) ViewData["MetaImage"] = media.MediaUrl;
                }
            }
            else if (type == "job")
            {
                var job = await _context.Jobs.FindAsync(id);
                if (job != null)
                {
                    ViewData["Title"] = job.JobTitle;
                    ViewData["MetaDescription"] = $"Công việc: {job.JobTitle} - Lương: {job.SalaryRange}";
                }
            }

            return View();
        }

        public IActionResult Auth()
        {
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "Admin");
            }
            ViewData["ActivePage"] = "auth";
            return View();
        }

        public IActionResult Manage()
        {
            ViewData["ActivePage"] = "manage";
            return View();
        }

        public IActionResult ManageRoommates()
        {
            var isAuthenticated = User.Identity?.IsAuthenticated == true;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value 
                       ?? User.FindFirst("role")?.Value;

            if (!isAuthenticated)
            {
                return RedirectToAction("Auth", "Home");
            }
            if (string.IsNullOrEmpty(role) || !role.Equals("Student", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Home");
            }

            ViewData["ActivePage"] = "manageroommates";
            return View();
        }

        public IActionResult HostAppointments()
        {
            ViewData["ActivePage"] = "hostappointments";
            return View();
        }

        public IActionResult EmployerApplications()
        {
            ViewData["ActivePage"] = "employerapplications";
            return View();
        }

        public IActionResult PostRoom()
        {
            ViewData["ActivePage"] = "postroom";
            return View();
        }

        public IActionResult PostJob()
        {
            ViewData["ActivePage"] = "postjob";
            return View();
        }

        public IActionResult Profile()
        {
            ViewData["ActivePage"] = "profile";
            return View();
        }

        public IActionResult Roommates()
        {
            var isAuthenticated = User.Identity?.IsAuthenticated == true;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value 
                       ?? User.FindFirst("role")?.Value;

            if (!isAuthenticated)
            {
                return RedirectToAction("Auth", "Home");
            }
            if (string.IsNullOrEmpty(role) || !role.Equals("Student", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Home");
            }

            ViewData["ActivePage"] = "roommates";
            return View();
        }

        public IActionResult Privacy()
        {
            ViewData["ActivePage"] = "privacy";
            return View();
        }

        public IActionResult Terms()
        {
            ViewData["ActivePage"] = "terms";
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [Route("Home/Error/{statusCode?}")]
        public IActionResult Error(int? statusCode = null)
        {
            if (statusCode.HasValue)
            {
                if (statusCode == 404)
                {
                    return View("Error404");
                }
                if (statusCode == 500)
                {
                    return View("Error500");
                }
            }
            return View("Error500", new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
