using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
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
builder.Services.AddMemoryCache();

// Compresión de respuestas HTTP (Gzip y Brotli para acelerar carga de páginas y estáticos)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

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

// Resolver cadena de conexión de forma flexible (compatible con Aiven URI, ADO.NET y Render ENV)
var rawConnectionString = 
    Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? Environment.GetEnvironmentVariable("MYSQL_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "";

static string NormalizarMySql(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return input;
    input = input.Trim().Trim('"', '\'');
    if (input.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var uri = new Uri(input);
            var userInfo = uri.UserInfo.Split(':');
            var user = Uri.UnescapeDataString(userInfo[0]);
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 3306;
            var database = uri.AbsolutePath.TrimStart('/');
            if (database.Contains('?'))
            {
                database = database.Substring(0, database.IndexOf('?'));
            }
            return $"Server={host};Port={port};Database={database};User={user};Password={password};SslMode=Required;TreatTinyAsBoolean=true;";
        }
        catch { }
    }
    if (!input.Contains("SslMode", StringComparison.OrdinalIgnoreCase) && input.Contains("aivencloud.com", StringComparison.OrdinalIgnoreCase))
    {
        input = input.TrimEnd(';') + ";SslMode=Required;";
    }
    // Optimizar connection pooling y keepalive para MySQL en la nube (Aiven / Render)
    if (!input.Contains("Pooling", StringComparison.OrdinalIgnoreCase))
    {
        input = input.TrimEnd(';') + ";Pooling=true;MinPoolSize=5;MaxPoolSize=50;ConnectionReset=false;Keepalive=30;";
    }
    return input;
}

var connectionString = NormalizarMySql(rawConnectionString);

builder.Services.AddDbContext<AppDbContex>(options =>
    options.UseMySql(
        connectionString,
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

// Comprimir respuestas antes de enviar
app.UseResponseCompression();

// Servir archivos estáticos con encabezados de caché del navegador (30 días)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=2592000,must-revalidate";
    }
});

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
            try
            {
                using var cmdCheck = conn.CreateCommand();
                cmdCheck.CommandText = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_SCHEMA = DATABASE() 
                      AND LOWER(TABLE_NAME) = LOWER(@tabla) 
                      AND LOWER(COLUMN_NAME) = LOWER(@columna);";

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
            catch { }
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

        // Asegurar columnas en Productos (evita 'Unknown column p.LimiteMaximoPorPedido')
        AsegurarColumna("Productos", "Miligramos", "INT NOT NULL DEFAULT 0");
        AsegurarColumna("Productos", "UnidadMedida", "VARCHAR(20) NOT NULL DEFAULT 'mg'");
        AsegurarColumna("Productos", "StockMinimo", "INT NOT NULL DEFAULT 5");
        AsegurarColumna("Productos", "LimiteMaximoPorPedido", "INT NULL");

        // Ampliar campo de descripción de productos a 2000 caracteres
        try
        {
            using var cmdDesc = conn.CreateCommand();
            cmdDesc.CommandText = "ALTER TABLE Productos MODIFY COLUMN Descripcion VARCHAR(2000) NULL;";
            cmdDesc.ExecuteNonQuery();
        }
        catch { }

        void AsegurarIndice(string tabla, string indice, string columnasDef)
        {
            try
            {
                using var cmdIdxCheck = conn.CreateCommand();
                cmdIdxCheck.CommandText = @"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.STATISTICS 
                    WHERE TABLE_SCHEMA = DATABASE() 
                      AND LOWER(TABLE_NAME) = LOWER(@tabla) 
                      AND LOWER(INDEX_NAME) = LOWER(@indice);";

                var pTabla = cmdIdxCheck.CreateParameter();
                pTabla.ParameterName = "@tabla";
                pTabla.Value = tabla;
                cmdIdxCheck.Parameters.Add(pTabla);

                var pIdx = cmdIdxCheck.CreateParameter();
                pIdx.ParameterName = "@indice";
                pIdx.Value = indice;
                cmdIdxCheck.Parameters.Add(pIdx);

                var count = Convert.ToInt32(cmdIdxCheck.ExecuteScalar());
                if (count == 0)
                {
                    using var cmdAddIdx = conn.CreateCommand();
                    cmdAddIdx.CommandText = $"ALTER TABLE `{tabla}` ADD INDEX `{indice}` ({columnasDef});";
                    cmdAddIdx.ExecuteNonQuery();
                }
            }
            catch { }
        }

        // Índices de alto rendimiento para acelerar consultas críticas
        AsegurarIndice("Usuarios", "idx_usuarios_email", "`Email`");
        AsegurarIndice("Productos", "idx_productos_estado_stock", "`Estado`, `Stock`");
        AsegurarIndice("Productos", "idx_productos_categoria", "`CategoriaId`");
        AsegurarIndice("Pedidos", "idx_pedidos_usuario_estado", "`UsuarioId`, `Estado`");
        AsegurarIndice("Pedidos", "idx_pedidos_repartidor_estado", "`RepartidorId`, `Estado`");

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



