using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Data;
using NCS.CBT.Hubs;
using NCS.CBT.Models;
using NCS.CBT.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ─── DATA PROTECTION ─────────────────────────────────────────────────────────
// Persist keys to the mounted volume so antiforgery tokens survive container restarts
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo("/app/data/dp-keys"))
    .SetApplicationName("NcsCbt");

// ─── ANTIFORGERY ─────────────────────────────────────────────────────────────
// Use a fixed cookie name so stale browser cookies never cause 400 errors
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = ".NcsCbt.Af";
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// ─── DATABASE ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ─── IDENTITY ─────────────────────────────────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 4;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.User.RequireUniqueEmail = false;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// ─── SIGNALR ──────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ─── MVC ──────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

// ─── SESSION ─────────────────────────────────────────────────────────────────
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(4);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddMemoryCache();

// ─── AI GRADING ───────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<AIGradingService>();

// ─── EMAIL ────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<FaceVerificationService>();
builder.Services.AddHostedService<NCS.CBT.Services.SessionExpiryService>();

// ─── RATE LIMITING ────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    // Max 60 answer saves per minute per user (1 per second)
    options.AddSlidingWindowLimiter("answers", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 6;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });
    // Max 10 login attempts per minute per IP
    options.AddSlidingWindowLimiter("login", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 6;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });
    options.RejectionStatusCode = 429;
});

var app = builder.Build();

// ─── MIGRATE + SEED ───────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await SeedData.InitializeAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or seeding the database.");
    }
}

// ─── MIDDLEWARE ───────────────────────────────────────────────────────────────

// Must be first — tells ASP.NET Core to trust X-Forwarded-Proto from Nginx (Docker bridge network)
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

// Catch stale antiforgery tokens and redirect to the correct login page
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
    {
        var path = ctx.Request.Path.Value ?? "";
        var dest = path.Contains("StudentLogin", StringComparison.OrdinalIgnoreCase)
            ? "/Account/StudentLogin?expired=1"
            : "/Account/Login?expired=1";
        ctx.Response.Redirect(dest);
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Account/Login");
    app.UseHsts();
}
// Security headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    ctx.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
    ctx.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

// Allow serving extensionless files (needed for face-api.js model shard files)
app.UseStaticFiles(new StaticFileOptions
{
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});
app.UseRateLimiter();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// ─── ROUTES ───────────────────────────────────────────────────────────────────
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapHub<ExamHub>("/examHub");

app.Run();
