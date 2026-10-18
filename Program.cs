using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using System.Globalization;
using Drogueria.Data;
using Drogueria.Hubs;
using Drogueria.Services;

var builder = WebApplication.CreateBuilder(args);

// Configurar puerto dinámico para Render / Entornos en la nube
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://*:{port}");
}

// Configurar encabezados proxy (Render SSL / Cloudflare)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<AsignacionRepartidorService>();
builder.Services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

builder.Services.AddHttpClient("Nominatim", client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org");
    client.DefaultRequestHeaders.Add("User-Agent", "DrogueriaApp/1.0 (contacto@drogueria.com)");
    client.DefaultRequestHeaders.Add("Accept-Language", "es");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("OSRM", client =>
{
    client.BaseAddress = new Uri("https://router.project-osrm.org");
    client.DefaultRequestHeaders.Add("User-Agent", "DrogueriaApp/1.0 (contacto@drogueria.com)");
    client.DefaultRequestHeaders.Add("Accept-Language", "es");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSignalR();


builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Index";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// Sesión para el carrito de compras
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddDbContext<AppDbContex>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 30)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure()
    )
);

// Fijar cultura invariante para que los inputs numéricos con punto decimal
// (como los inputs HTML type="number") se bindeen correctamente.
var supportedCultures = new[] { CultureInfo.InvariantCulture };
builder.Services.Configure<RequestLocalizationOptions>(opts =>
{
    opts.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(CultureInfo.InvariantCulture);
    opts.SupportedCultures = supportedCultures;
    opts.SupportedUICultures = supportedCultures;
});

var app = builder.Build();

app.UseForwardedHeaders();
    
app.UseStatusCodePagesWithReExecute("/Home/ErrorStatus", "?code={0}");

// Seguridad HTTP: HSTS y Cabeceras
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
else
{
    // Habilitar HSTS en desarrollo si se ejecuta sobre HTTPS
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    await next();
});

app.UseRequestLocalization();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.MapHub<UbicacionHub>("/hubs/ubicacion");

// Asegurar columnas de perfil y vehículo en la tabla Usuarios sin generar errores en consola
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContex>();
        db.Database.Migrate();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            conn.Open();
        }

        void AsegurarColumna(string tabla, string columna, string tipoDef)
        {
            using var cmdCheck = conn.CreateCommand();
            cmdCheck.CommandText = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = DATABASE() 
                  AND TABLE_NAME = @tabla 
                  AND COLUMN_NAME = @columna;";

            var pTabla = cmdCheck.CreateParameter();
            pTabla.ParameterName = "@tabla";
            pTabla.Value = tabla;
            cmdCheck.Parameters.Add(pTabla);

            var pCol = cmdCheck.CreateParameter();
            pCol.ParameterName = "@columna";
            pCol.Value = columna;
            cmdCheck.Parameters.Add(pCol);

            var count = Convert.ToInt32(cmdCheck.ExecuteScalar());
            if (count == 0)
            {
                using var cmdAlter = conn.CreateCommand();
                cmdAlter.CommandText = $"ALTER TABLE `{tabla}` ADD COLUMN `{columna}` {tipoDef};";
                cmdAlter.ExecuteNonQuery();
            }
        }

        // Asegurar columnas en Usuarios
        AsegurarColumna("Usuarios", "Telefono", "VARCHAR(20) NULL");
        AsegurarColumna("Usuarios", "TipoVehiculo", "VARCHAR(50) NULL");
        AsegurarColumna("Usuarios", "Placa", "VARCHAR(20) NULL");
        AsegurarColumna("Usuarios", "EstaEnLinea", "TINYINT(1) NOT NULL DEFAULT 1");

        // Asegurar columnas en Pedidos (evita 'Unknown column p.CodigoConfirmacion')
        AsegurarColumna("Pedidos", "CodigoConfirmacion", "VARCHAR(20) NULL");
        AsegurarColumna("Pedidos", "IntentosCodigoEntrega", "INT NOT NULL DEFAULT 0");
        AsegurarColumna("Pedidos", "MetodoPago", "VARCHAR(50) NULL");
        AsegurarColumna("Pedidos", "DireccionEntrega", "VARCHAR(300) NULL");
        AsegurarColumna("Pedidos", "LatitudRepartidor", "DOUBLE NULL");
        AsegurarColumna("Pedidos", "LongitudRepartidor", "DOUBLE NULL");
        AsegurarColumna("Pedidos", "UltimaUbicacionFecha", "DATETIME(6) NULL");

        // Ampliar campo de descripción de productos a 2000 caracteres
        try
        {
            using var cmdDesc = conn.CreateCommand();
            cmdDesc.CommandText = "ALTER TABLE Productos MODIFY COLUMN Descripcion VARCHAR(2000) NULL;";
            cmdDesc.ExecuteNonQuery();
        }
        catch { }

        // Asegurar tabla persistente de Mensajes de Chat
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS MensajesChat (
                    Id CHAR(36) NOT NULL PRIMARY KEY,
                    PedidoId CHAR(36) NOT NULL,
                    Remitente VARCHAR(150) NOT NULL,
                    Mensaje TEXT NOT NULL,
                    Fecha DATETIME NOT NULL,
                    INDEX idx_chat_pedido (PedidoId)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";
            cmd.ExecuteNonQuery();
        }

        // Sembrar cuentas iniciales si no existen (Admin, Recepcionista, Repartidor, Cliente)
        try
        {
            DbInitializer.SeedUsers(db);
        }
        catch { }
    }
    catch { }
}

app.Run();



