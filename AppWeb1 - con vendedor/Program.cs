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

    var builder = WebApplication.CreateBuilder(args);

    // ==========================================================
    // 1. CONFIGURACIÓN DE SERVICIOS
    // ==========================================================

    // ===== MVC & Vistas =====
    #if DEBUG
    builder.Services.AddControllersWithViews(options =>
    {
        // APLICA LA VALIDACIÓN DE AntiForgeryToken a TODAS las acciones POST automáticamente (MEJORA SOLICITADA)
        options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    })
        .AddRazorRuntimeCompilation();
    #else
    builder.Services.AddControllersWithViews(options =>
    {
        // APLICA LA VALIDACIÓN DE AntiForgeryToken a TODAS las acciones POST automáticamente (MEJORA SOLICITADA)
        options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    });
    #endif

    // ===== Base de Datos (Entity Framework Core) =====
    builder.Services.AddDbContext<DeliveryDBContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DeliveryDB")));

    // ===== Servicios HTTP, Email y Contexto =====
    builder.Services.AddHttpClient();
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
    builder.Services.AddHttpContextAccessor();

    // ===== Sesión & Caché (Requerido para Session y TempData) =====
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

    // Soporte para TempData usando Session
    builder.Services.AddSingleton<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider,
        Microsoft.AspNetCore.Mvc.ViewFeatures.SessionStateTempDataProvider>();

    // ===== Límite de subida de archivos (Configuración de FormOptions) =====
    builder.Services.Configure<FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 20 * 1024 * 1024;
        options.MultipartHeadersLengthLimit = 64 * 1024;
        options.ValueLengthLimit = 1024 * 1024;
    });

    // Antiforgery para AJAX: usaremos un header
    builder.Services.AddAntiforgery(o =>
    {
        o.HeaderName = "RequestVerificationToken"; // nombre del header que enviaremos
        o.Cookie.Name = "XSRF-TOKEN";              // cookie legible por el cliente
        o.Cookie.SameSite = SameSiteMode.Lax;
    });

    // -----------------------------------------------------------

    var app = builder.Build();

    // ==========================================================
    // 2. CONFIGURACIÓN DE MIDDLEWARE Y LICENCIAS
    // ==========================================================

    // ===== EPPlus: Licencia (Global) =====
    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

    // ===== Middleware de Entorno =====
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    // --- Headers de Seguridad Adicionales--
    // IMPORTANTE: Este middleware debe ir antes de app.UseStaticFiles()
    // para que los headers se apliquen también a los archivos estáticos.
    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        ctx.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; img-src 'self' data: https:; script-src 'self' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com 'unsafe-inline'; style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; font-src 'self' https://cdnjs.cloudflare.com data:";
        await next();
    });

    // ===== Archivos Estáticos =====
    app.UseStaticFiles();

    // (Opcional) Si querés exponer una carpeta externa, ej. /uploads
    // var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
    // Directory.CreateDirectory(uploadsPath);
    // app.UseStaticFiles(new StaticFileOptions
    // {
    //      FileProvider = new PhysicalFileProvider(uploadsPath),
    //      RequestPath = "/uploads"
    // });

    app.UseRouting();

    // -----------------------------------------------------------
    // Verificación de Orden del Middleware
    // Sesión debe ir después de UseRouting() y antes de UseAuthorization()
    app.UseSession();
    // Si luego usás [Authorize]:
    // app.UseAuthentication();
    app.UseAuthorization();

    // ==========================================================
    // 3. MAPEO DE ENDPOINTS (RUTAS)
    // ==========================================================

    // Rutas para las Áreas explícitas
    app.MapAreaControllerRoute(
        name: "admin_area",
        areaName: "Admin",
        pattern: "Admin/{controller=Home}/{action=Index}/{id?}");

    app.MapAreaControllerRoute(
        name: "cliente_area",
        areaName: "Cliente",
        pattern: "Cliente/{controller=Cliente}/{action=Catalogo}/{id?}");

    // Fallback genérico para cualquier Área
    app.MapControllerRoute(
        name: "areas",
        pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

    // Ruta Default (Login/Home fuera de áreas)
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Usuario}/{action=Login}/{id?}");

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

            // 4.1. Asegura que la BD y el schema estén al día (Ejecuta Migraciones)
            db.Database.Migrate();

            // 4.2. Asegura la carpeta de imágenes (para evitar fallos al guardar archivos)
            var webRoot = env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            Directory.CreateDirectory(webRoot);
            var imgPath = Path.Combine(webRoot, "imagenes");
            if (!Directory.Exists(imgPath))
                Directory.CreateDirectory(imgPath);

            // 4.3. Seed: Crea el usuario Admin inicial si no existe
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

            // 4.4. Seed: Productos demo
            if (!db.Productos.Any())
            {
                db.Productos.AddRange(
                    new Producto { NombreProducto = "Hamburguesa Clásica", Descripcion = "Carne, lechuga, tomate", Precio = 4200m },
                    new Producto { NombreProducto = "Pizza Muzza", Descripcion = "Muzzarella y orégano", Precio = 6500m },
                    new Producto { NombreProducto = "Empanadas (docena)", Descripcion = "Carne suave", Precio = 5600m }
                );
                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error al inicializar la aplicación (migraciones/seed): " + ex.Message);
            if (app.Environment.IsDevelopment())
                Console.WriteLine(ex);
        }
    }

    app.Run();

    // Clase auxiliar para el envío de correos, ubicada al final del archivo
    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpClient _smtp;
        private readonly string _from;

        public SmtpEmailSender(IConfiguration cfg)
        {
            _from = cfg["Email:From"] ?? throw new ArgumentException("Email:From no está configurado");

            var smtpHost = cfg["Email:SmtpHost"] ?? throw new ArgumentException("Email:SmtpHost no está configurado");
            var smtpPortStr = cfg["Email:SmtpPort"] ?? throw new ArgumentException("Email:SmtpPort no está configurado");
            var emailUser = cfg["Email:User"] ?? throw new ArgumentException("Email:User no está configurado");
            var emailPass = cfg["Email:Pass"] ?? throw new ArgumentException("Email:Pass no está configurado");

            if (!int.TryParse(smtpPortStr, out int smtpPort))
                throw new ArgumentException("Email:SmtpPort debe ser un número válido");

            _smtp = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(emailUser, emailPass),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
        }

        public Task SendAsync(string to, string subject, string htmlBody)
        {
            var msg = new MailMessage(_from, to, subject, htmlBody)
            {
                IsBodyHtml = true
            };
            return _smtp.SendMailAsync(msg);
        }
    }

    // Interfaz para el envío de correos, necesaria para el servicio
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string htmlBody);
    }