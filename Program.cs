using System.Text;
using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UniMap360.Models;
using UniMap360.Filters;
using UniMap360.Middleware;
using UniMap360.Options;
using UniMap360.Services.Admin;
using UniMap360.Services.Appointments;
using UniMap360.Services.Applications;
using UniMap360.Services.Posts;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using UniMap360.Services.Email;

using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/unimap360-log-.txt", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();
try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("Logs/unimap360-log-.txt", rollingInterval: RollingInterval.Day));

    builder.Configuration.AddIniFile("secrets.ini", optional: true, reloadOnChange: true);
    builder.Configuration.AddEnvironmentVariables();

    // Bật nén dữ liệu để tối ưu tốc độ cho Mobile
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
    });

// Add services to the container.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<ApiValidationFilter>();
})
.ConfigureApiBehaviorOptions(options =>
{
    // Tắt auto 400 mặc định của ASP.NET để dùng ApiValidationFilter
    options.SuppressModelStateInvalidFilter = true;
});

// Cấu hình Rate Limiting để chống Spam
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthRateLimit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 15, // Tăng lên 15 để đỡ bị block nhầm khi F5 nhiều lần
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// JWT authentication for API access control.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key must be configured and at least 32 characters long.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrWhiteSpace(context.Token)
                    && context.Request.Cookies.TryGetValue("unimap360.accessToken", out var cookieToken)
                    && !string.IsNullOrWhiteSpace(cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Cấu hình Swagger API Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "UniMap360 API", 
        Version = "v1",
        Description = "Tài liệu API cho hệ thống UniMap360",
        Contact = new OpenApiContact { Name = "Trần Trọng Quyết", Email = "admin@unimap360.com" }
    });

    // Cấu hình JWT Auth cho Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header sử dụng scheme Bearer. Ví dụ: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // Đọc comment XML từ file C#
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    // Giải quyết xung đột route trùng phương thức và đường dẫn (ví dụ: upload JSON vs upload Form)
    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
});
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("Cloudinary"));
builder.Services.Configure<AdminSecurityOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.AddHttpClient();
builder.Services.AddScoped<ISuperAdminGuardService, SuperAdminGuardService>();
builder.Services.AddScoped<IAdminAuditService, AdminAuditService>();
builder.Services.AddScoped<ICloudinaryAssetPurger, CloudinaryAssetPurger>();
builder.Services.AddScoped<IManagePostsContextService, ManagePostsContextService>();
builder.Services.AddScoped<ILocationResolutionService, LocationResolutionService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IJobApplicationService, JobApplicationService>();
builder.Services.AddProblemDetails();

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnection))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
}

var dbProvider = builder.Configuration["Database:Provider"]?.Trim();
var usePostgres = string.Equals(dbProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase)
    || string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase)
    || string.Equals(dbProvider, "Npgsql", StringComparison.OrdinalIgnoreCase);

Log.Information("Database Provider selected: {Provider} (UsePostgres: {UsePostgres})", dbProvider ?? "Default/SQLServer", usePostgres);

if (usePostgres)
{
    // PostgreSQL (Supabase) + PostGIS
    builder.Services.AddDbContext<UniMap360ProContext>(options =>
        options.UseNpgsql(defaultConnection, x => x.UseNetTopologySuite()));

    builder.Services.AddHealthChecks()
        .AddNpgSql(defaultConnection, name: "Database");
}
else
{
    // SQL Server (current local/default)
    builder.Services.AddDbContext<UniMap360ProContext>(options =>
        options.UseSqlServer(defaultConnection, x => x.UseNetTopologySuite()));

    builder.Services.AddHealthChecks()
        .AddSqlServer(defaultConnection, name: "Database");
}

var app = builder.Build();

app.UseMiddleware<TraceLoggingMiddleware>();

// Kích hoạt nén dữ liệu
app.UseResponseCompression();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<UniMap360ProContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSchemaBootstrapper");
    var adminOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminSecurityOptions>>().Value;
    var ownerId = adminOptions.OwnerAccountId.GetValueOrDefault(1);
    var fallbackPlainPassword = builder.Configuration["Auth:LegacyHashFallbackPlainPassword"] ?? "123456";
    await AdminSchemaBootstrapper.EnsureAsync(context, logger, ownerId, fallbackPlainPassword);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error/500");
    app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}
else 
{
    // Cố tình bật trang lỗi ở Dev để test, có thể bỏ sau
    app.UseExceptionHandler("/Home/Error/500");
    app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");
    
    // Kích hoạt Swagger UI chỉ ở môi trường Development
    app.UseSwagger();
    app.UseSwaggerUI(c => 
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "UniMap360 API v1");
        c.InjectStylesheet("/css/swagger-custom.css"); // Nhúng CSS tùy chỉnh Đỏ Đô/Vàng
        c.DocumentTitle = "UniMap360 API Documentation";
    });
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval' blob: https://accounts.google.com https://unpkg.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://www.googletagmanager.com; frame-src 'self' https://accounts.google.com; worker-src 'self' blob:; style-src 'self' 'unsafe-inline' https://accounts.google.com https://fonts.googleapis.com https://unpkg.com https://cdnjs.cloudflare.com; font-src 'self' data: https://fonts.gstatic.com https://cdnjs.cloudflare.com; img-src 'self' data: blob: https: https://lh3.googleusercontent.com https://accounts.google.com; media-src 'self' https://res.cloudinary.com; connect-src 'self' wss: https: https://accounts.google.com https://www.googletagmanager.com https://www.google-analytics.com;";
    await next();
});

app.UseStaticFiles();

app.UseRouting();

// Bật Rate Limiter (Phải đặt sau UseRouting)
app.UseRateLimiter();

app.UseMiddleware<ApiExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHealthChecks("/health");

app.Run();
}
catch (Exception ex) when (ex.GetType().Name != "HostAbortedException")
{
    Log.Fatal(ex, "Lỗi nghiêm trọng: Server không thể khởi động!");
}
finally
{
    Log.Information("Server đã tắt.");
    Log.CloseAndFlush();
}

public partial class Program { }
