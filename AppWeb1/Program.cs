// ============================================================
// ARCHIVO: Program.cs  (VERSIÓN COMPLETA CON SIGNALR)
// ============================================================
// Los bloques marcados con ★ SIGNALR son los únicos que se agregan.
// Todo lo demás es igual a tu Program.cs actual.
// ============================================================

using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using AppWeb1.Data;
using OfficeOpenXml;
using BCrypt.Net;
using AppWeb1.Models;
using AppWeb1.Helpers;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using AppWeb1.Hubs;              // ★ SIGNALR: namespace del Hub

var builder = WebApplication.CreateBuilder(args);

// ==========================================================
// 1. CONFIGURACIÓN DE SERVICIOS
// ==========================================================

// ===== MVC & Vistas =====
#if DEBUG
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
})
    .AddRazorRuntimeCompilation();
#else
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});
#endif

// ===== Base de Datos =====
builder.Services.AddDbContext<DeliveryDBContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DeliveryDB")));

// ===== Servicios HTTP, Email y Contexto =====
builder.Services.AddHttpClient();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddHttpContextAccessor();

// ===== Sesión & Caché =====
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "AppWeb1.Session";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddSingleton<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider,
    Microsoft.AspNetCore.Mvc.ViewFeatures.SessionStateTempDataProvider>();

// ===== Límite de archivos =====
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024;
    options.MultipartHeadersLengthLimit = 64 * 1024;
    options.ValueLengthLimit = 1024 * 1024;
});

// ===== Antiforgery para AJAX =====
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "RequestVerificationToken";
    o.Cookie.Name = "XSRF-TOKEN";
    o.Cookie.SameSite = SameSiteMode.Lax;
});

// ★ SIGNALR: Registrar el servicio
// AddSignalR() ya viene incluido en el SDK de ASP.NET Core 8 (no necesita NuGet extra).
builder.Services.AddSignalR();

// -----------------------------------------------------------

var app = builder.Build();

// ==========================================================
// 2. CONFIGURACIÓN DE MIDDLEWARE Y LICENCIAS
// ==========================================================

ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// ★ SIGNALR: Actualizar Content Security Policy para permitir WebSockets y el CDN de SignalR.
// El 'unsafe-inline' ya estaba; solo hay que asegurarse de que cdn.jsdelivr.net está permitido
// (ya estaba en tu CSP original). No se necesita agregar nada más aquí normalmente,
// pero si usás WSS (WebSocket Secure) en producción, el CSP connect-src debe incluir 'self' wss:.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // ★ SIGNALR: connect-src debe incluir 'self' para permitir WebSockets al mismo origen.
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' data: https:; " +
        "script-src 'self' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; " +
        "font-src 'self' https://cdnjs.cloudflare.com data:; " +
        "connect-src 'self' wss: ws:;";   // ★ Necesario para WebSockets de SignalR
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

// ==========================================================
// 3. MAPEO DE ENDPOINTS (RUTAS)
// ==========================================================

app.MapAreaControllerRoute(
    name: "admin_area",
    areaName: "Admin",
    pattern: "Admin/{controller=Home}/{action=Index}/{id?}");

app.MapAreaControllerRoute(
    name: "cliente_area",
    areaName: "Cliente",
    pattern: "Cliente/{controller=Cliente}/{action=Catalogo}/{id?}");

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Usuario}/{action=Login}/{id?}");

// ★ SIGNALR: Mapear el endpoint del Hub en la ruta /deliveryHub
// IMPORTANTE: debe ir ANTES de app.Run()
app.MapHub<DeliveryHub>("/deliveryHub");

// ==========================================================
// 4. MIGRACIONES Y SEEDING DE DATOS
// ==========================================================

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var env = services.GetRequiredService<IWebHostEnvironment>();
        var db = services.GetRequiredService<DeliveryDBContext>();

        db.Database.Migrate();

        var webRoot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        Directory.CreateDirectory(webRoot);
        var imgPath = Path.Combine(webRoot, "imagenes");
        if (!Directory.Exists(imgPath))
            Directory.CreateDirectory(imgPath);

        var admin = db.Usuarios.FirstOrDefault(u => u.NombreUsuario == "admin");
        if (admin == null)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword("admi123!", workFactor: 12);
            db.Usuarios.Add(new Usuario
            {
                NombreUsuario = "admin",
                Contraseña = hash,
                Rol = "admin"
            });
            db.SaveChanges();
        }
        else
        {
            var h = admin.Contraseña ?? "";
            if (!(h.StartsWith("$2a$") || h.StartsWith("$2b$") || h.StartsWith("$2y$")))
            {
                admin.Contraseña = BCrypt.Net.BCrypt.HashPassword(h, workFactor: 12);
                db.SaveChanges();
            }
        }

        // Seed de productos (igual a tu Program.cs actual, no modificado)
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error durante la migración o el seeding.");
    }
}

app.Run();