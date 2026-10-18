using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Drogueria.Data;
using Drogueria.Models;

namespace Drogueria.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContex _context;
        private readonly Drogueria.Services.ILoginRateLimiter _rateLimiter;

        public AccountController(AppDbContex context, Drogueria.Services.ILoginRateLimiter rateLimiter)
        {
            _context = context;
            _rateLimiter = rateLimiter;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, string rolEsperado)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json");

            var term = email?.Trim().ToLower() ?? "";
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Rate limiting por IP y usuario
            if (_rateLimiter.IsBlocked(clientIp, term, out var segundosRestantes))
            {
                var minutos = (int)Math.Ceiling(segundosRestantes / 60.0);
                var msgBloqueo = $"Demasiados intentos fallidos. Por seguridad, tu acceso ha sido bloqueado temporalmente. Intenta nuevamente en {minutos} minuto(s).";
                if (isAjax) return Json(new { success = false, message = msgBloqueo });
                TempData["Error"] = msgBloqueo;
                TempData["AbrirModal"] = ObtenerModal(rolEsperado);
                return RedirectToAction("Index", "Home");
            }

            if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(password))
            {
                var msg = "Completa todos los campos obligatorios para ingresar.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                TempData["AbrirModal"] = ObtenerModal(rolEsperado);
                return RedirectToAction("Index", "Home");
            }

            // Validar formato si se ingresó un correo electrónico (contiene '@')
            if (term.Contains("@"))
            {
                var emailRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!emailRegex.IsMatch(term))
                {
                    var msg = "El correo electrónico ingresado no tiene un formato válido (debe incluir '@', un dominio y su extensión como .com o .co).";
                    if (isAjax) return Json(new { success = false, message = msg });
                    TempData["Error"] = msg;
                    TempData["AbrirModal"] = ObtenerModal(rolEsperado);
                    return RedirectToAction("Index", "Home");
                }
            }

            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Email.ToLower() == term ||
                                         u.Nombre.ToLower() == term ||
                                         (u.Nombre + " " + u.Apellido).ToLower() == term);

            bool esValido = false;
            if (usuario != null && !string.IsNullOrEmpty(usuario.Password))
            {
                try { esValido = BCrypt.Net.BCrypt.Verify(password ?? "", usuario.Password); }
                catch { esValido = (usuario.Password == password); }
            }

            if (usuario == null || !esValido)
            {
                _rateLimiter.RecordFailedAttempt(clientIp, term);
                var msg = "Por favor, valide si el usuario o la contraseña son correctos.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                TempData["AbrirModal"] = ObtenerModal(rolEsperado);
                return RedirectToAction("Index", "Home");
            }

            // Si las credenciales fueron correctas, restablecer intentos
            _rateLimiter.ResetAttempts(clientIp, term);

            var rolUsuario = usuario.Rol?.Trim() ?? "";
            var rolEsperadoNorm = rolEsperado?.Trim() ?? "";

            if (string.IsNullOrEmpty(rolEsperadoNorm) ||
                !string.Equals(rolUsuario, rolEsperadoNorm, StringComparison.OrdinalIgnoreCase))
            {
                var msg = $"Acceso denegado: Tu cuenta tiene el rol '{rolUsuario}' y únicamente puedes iniciar sesión desde la sección de {rolUsuario}.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                TempData["AbrirModal"] = ObtenerModal(rolEsperadoNorm);
                return RedirectToAction("Index", "Home");
            }

            if (!usuario.Estado)
            {
                var msg = "Tu cuenta se encuentra desactivada. Contacta al equipo de soporte.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                TempData["AbrirModal"] = ObtenerModal(rolEsperado);
                return RedirectToAction("Index", "Home");
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Uuid),
                new Claim(ClaimTypes.Name, $"{usuario.Nombre} {usuario.Apellido}".Trim()),
                new Claim(ClaimTypes.Email, usuario.Email),
                new Claim(ClaimTypes.Role, usuario.Rol)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });

            TempData["Success"] = $"¡Bienvenido, {usuario.Nombre}!";

            var redirectUrl = usuario.Rol switch
            {
                "Administrador" => Url.Action("Dashboard", "Usuario"),
                "Empleado"      => Url.Action("Productos", "Empleado"),
                "Repartidor"    => Url.Action("MisDomicilios", "Repartidor"),
                _               => Url.Action("Catalogo", "Tienda")
            };

            if (isAjax) return Json(new { success = true, redirectUrl });
            return Redirect(redirectUrl ?? "/Home/Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string nombre, string apellido, string email, string password)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json");

            nombre = nombre?.Trim() ?? "";
            apellido = apellido?.Trim() ?? "";
            email = email?.Trim().ToLower() ?? "";
            password = password ?? "";

            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(apellido) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                var msg = "Completa todos los campos obligatorios para crear tu cuenta.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg; TempData["AbrirModal"] = "_RegistroCliente";
                return RedirectToAction("Index", "Home");
            }

            // Validar formato de correo (debe tener @, punto y extensión .com, .co, etc.)
            var emailRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!emailRegex.IsMatch(email))
            {
                var msg = "El correo electrónico debe tener un formato válido con '@', un dominio y su terminación (ejemplo: usuario@dominio.com o .co).";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg; TempData["AbrirModal"] = "_RegistroCliente";
                return RedirectToAction("Index", "Home");
            }

            if (nombre.Length > 25)
            {
                var msg = "El nombre no puede superar los 25 caracteres.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg; TempData["AbrirModal"] = "_RegistroCliente";
                return RedirectToAction("Index", "Home");
            }

            if (apellido.Length > 25)
            {
                var msg = "El apellido no puede superar los 25 caracteres.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg; TempData["AbrirModal"] = "_RegistroCliente";
                return RedirectToAction("Index", "Home");
            }

            if (email.Length > 60)
            {
                var msg = "El correo electrónico no puede superar los 60 caracteres.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg; TempData["AbrirModal"] = "_RegistroCliente";
                return RedirectToAction("Index", "Home");
            }

            if (password.Length < 8 || password.Length > 25)
            {
                var msg = "La contraseña debe tener entre 8 y 25 caracteres.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg; TempData["AbrirModal"] = "_RegistroCliente";
                return RedirectToAction("Index", "Home");
            }

            if (await _context.Usuarios.AnyAsync(u => u.Email.ToLower() == email))
            {
                var msg = $"El correo '{email}' ya se encuentra registrado.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg; TempData["AbrirModal"] = "_RegistroCliente";
                return RedirectToAction("Index", "Home");
            }

            _context.Usuarios.Add(new Usuario
            {
                Nombre = nombre.Trim(), Apellido = apellido.Trim(), Email = email,
                Password = BCrypt.Net.BCrypt.HashPassword(password), Rol = "Cliente", Estado = true
            });
            await _context.SaveChangesAsync();

            var exitoMsg = "¡Cuenta creada con éxito! Ya puedes iniciar sesión.";
            if (isAjax) return Json(new { success = true, message = exitoMsg, openModal = "_LoginCliente" });
            TempData["Success"] = exitoMsg; TempData["AbrirModal"] = "_LoginCliente";
            return RedirectToAction("Index", "Home");
        }

        [HttpGet, HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            try { HttpContext.Session.Clear(); } catch { }
            TempData["Success"] = "Sesión cerrada correctamente.";
            return RedirectToAction("Index", "Home");
        }

        [HttpGet, HttpPost]
        public IActionResult AccessDenied()
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json");

            var msg = "No tienes permitido ingresar por aquí.";

            if (isAjax)
            {
                return Json(new { success = false, message = msg, accessDenied = true });
            }

            TempData["Error"] = msg;
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Administrador"))
                    return RedirectToAction("Dashboard", "Usuario");
                if (User.IsInRole("Empleado"))
                    return RedirectToAction("Productos", "Empleado");
                if (User.IsInRole("Repartidor"))
                    return RedirectToAction("MisDomicilios", "Repartidor");
                if (User.IsInRole("Cliente"))
                    return RedirectToAction("Catalogo", "Tienda");
            }
            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/Perfil
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Perfil()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Index", "Home");
            }

            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Uuid == userId);
            if (usuario == null)
            {
                TempData["Error"] = "Usuario no encontrado.";
                return RedirectToAction("Index", "Home");
            }

            if (usuario.Rol == "Repartidor")
            {
                ViewBag.TotalEntregados = await _context.Pedidos.CountAsync(p => p.RepartidorId == userId && p.Estado == "Entregado");
                ViewBag.TotalActivos = await _context.Pedidos.CountAsync(p => p.RepartidorId == userId && p.Estado != "Entregado");
            }
            else if (usuario.Rol == "Cliente")
            {
                ViewBag.TotalPedidos = await _context.Pedidos.CountAsync(p => p.UsuarioId == userId);
                ViewBag.Direcciones = await _context.DireccionesUsuario
                    .Where(d => d.UsuarioId == userId)
                    .OrderByDescending(d => d.EsPredeterminada)
                    .ToListAsync();
            }

            ViewData["Title"] = "Mi Perfil";
            ViewData["ActiveNav"] = "perfil";
            return View("~/Views/Account/Perfil.cshtml", usuario);
        }

        // POST: /Account/ActualizarPerfil
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarPerfil(string nombre, string apellido, string email, string? telefono, string? tipoVehiculo, string? placa)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Uuid == userId);
            if (usuario == null)
            {
                var msg = "Usuario no encontrado.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Perfil));
            }

            nombre = nombre?.Trim() ?? "";
            apellido = apellido?.Trim() ?? "";
            email = email?.Trim().ToLower() ?? "";

            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(apellido) || string.IsNullOrWhiteSpace(email))
            {
                var msg = "El nombre, apellido y correo electrónico son obligatorios.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Perfil));
            }

            // Validar correo
            var emailRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!emailRegex.IsMatch(email))
            {
                var msg = "El correo electrónico ingresado no tiene un formato válido.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Perfil));
            }

            // Validar correo duplicado
            if (await _context.Usuarios.AnyAsync(u => u.Email.ToLower() == email && u.Uuid != userId))
            {
                var msg = "El correo electrónico ya está en uso por otra cuenta.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Perfil));
            }

            // Validaciones específicas para Repartidor
            if (usuario.Rol == "Repartidor")
            {
                var vehiculosValidos = new[] { "Moto", "Bicicleta", "Carro" };
                tipoVehiculo = tipoVehiculo?.Trim();

                if (!string.IsNullOrEmpty(tipoVehiculo) && !vehiculosValidos.Contains(tipoVehiculo))
                {
                    var msg = "Selecciona un tipo de vehículo válido (Moto, Bicicleta o Carro).";
                    if (isAjax) return Json(new { success = false, message = msg });
                    TempData["Error"] = msg;
                    return RedirectToAction(nameof(Perfil));
                }

                // Si usa Moto o Carro, la placa es obligatoria
                if (tipoVehiculo == "Moto" || tipoVehiculo == "Carro")
                {
                    var placaLimpia = (placa ?? "").Trim().ToUpper();
                    if (string.IsNullOrEmpty(placaLimpia))
                    {
                        var msg = $"Debes ingresar la placa para el vehículo de tipo '{tipoVehiculo}'.";
                        if (isAjax) return Json(new { success = false, message = msg });
                        TempData["Error"] = msg;
                        return RedirectToAction(nameof(Perfil));
                    }
                    usuario.Placa = placaLimpia;
                }
                else
                {
                    // Para Bicicleta, la placa no se necesita
                    usuario.Placa = null;
                }

                usuario.TipoVehiculo = tipoVehiculo;
            }

            usuario.Nombre = nombre;
            usuario.Apellido = apellido;
            usuario.Email = email;
            usuario.Telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();

            _context.Usuarios.Update(usuario);
            await _context.SaveChangesAsync();

            // Actualizar claims en la cookie de sesión para reflejar cambios en la interfaz
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario.Uuid),
                new Claim(ClaimTypes.Name, $"{usuario.Nombre} {usuario.Apellido}".Trim()),
                new Claim(ClaimTypes.Email, usuario.Email),
                new Claim(ClaimTypes.Role, usuario.Rol)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });

            var exito = "Tu perfil ha sido actualizado exitosamente.";
            if (isAjax) return Json(new { success = true, message = exito });
            TempData["Success"] = exito;
            return RedirectToAction(nameof(Perfil));
        }

        // POST: /Account/CambiarPassword
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarPassword(string actualPassword, string nuevaPassword, string confirmarPassword)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Uuid == userId);
            if (usuario == null)
            {
                var msg = "Usuario no encontrado.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Perfil));
            }

            if (string.IsNullOrWhiteSpace(actualPassword) || string.IsNullOrWhiteSpace(nuevaPassword) || string.IsNullOrWhiteSpace(confirmarPassword))
            {
                var msg = "Todos los campos de contraseña son requeridos.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Perfil));
            }

            // Validar contraseña actual
            bool passValida = false;
            try { passValida = BCrypt.Net.BCrypt.Verify(actualPassword, usuario.Password); }
            catch { passValida = (usuario.Password == actualPassword); }

            if (!passValida)
            {
                var msg = "La contraseña actual ingresada es incorrecta.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Perfil));
            }

            if (nuevaPassword.Length < 6)
            {
                var msg = "La nueva contraseña debe tener al menos 6 caracteres.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Perfil));
            }

            if (nuevaPassword != confirmarPassword)
            {
                var msg = "La nueva contraseña y su confirmación no coinciden.";
                if (isAjax) return Json(new { success = false, message = msg });
                TempData["Error"] = msg;
                return RedirectToAction(nameof(Perfil));
            }

            usuario.Password = BCrypt.Net.BCrypt.HashPassword(nuevaPassword);
            _context.Usuarios.Update(usuario);
            await _context.SaveChangesAsync();

            var exito = "Tu contraseña ha sido cambiada correctamente.";
            if (isAjax) return Json(new { success = true, message = exito });
            TempData["Success"] = exito;
            return RedirectToAction(nameof(Perfil));
        }

        private static string ObtenerModal(string? rolEsperado) => rolEsperado switch
        {
            "Administrador" => "_LoginAdmin",
            "Empleado"      => "_LoginEmpleado",
            "Repartidor"    => "_LoginRepartidor",
            "Cliente"       => "_LoginCliente",
            _               => "_LoginCliente"
        };
    }
}
