using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Drogueria.Data;
using Drogueria.Models;

namespace Drogueria.Controllers
{
    public class DashboardMetricsCache
    {
        public int TotalProductos { get; set; }
        public int TotalUsuarios { get; set; }
        public int TotalCategorias { get; set; }
        public int TotalPedidos { get; set; }
        public decimal IngresosTotales { get; set; }
        public int PedidosPendientes { get; set; }
        public int PedidosConfirmados { get; set; }
        public int PedidosEntregados { get; set; }
        public int PedidosCancelados { get; set; }
        public List<Producto> ProductosStockBajo { get; set; } = new();
        public decimal[] VentasPorDia { get; set; } = new decimal[7];
    }

    [Authorize(Roles = "Administrador")]
    public class UsuarioController : Controller
    {
        private readonly AppDbContex _context;
        private readonly IMemoryCache _cache;

        public UsuarioController(AppDbContex context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // Dashboard Administrador (Métricas, accesos rápidos y últimos pedidos)
        public async Task<IActionResult> Dashboard()
        {
            var hoy = DateTime.Now;
            var hace7Dias = hoy.AddDays(-7);

            ViewBag.FechaActual = hoy;

            const string cacheKey = "Admin_Dashboard_Metrics";
            if (!_cache.TryGetValue(cacheKey, out DashboardMetricsCache? metrics) || metrics == null)
            {
                metrics = new DashboardMetricsCache();

                metrics.TotalProductos = await _context.Productos.AsNoTracking().CountAsync();
                metrics.TotalUsuarios = await _context.Usuarios.AsNoTracking().CountAsync();
                metrics.TotalCategorias = await _context.Categorias.AsNoTracking().CountAsync();

                // 1 sola consulta agrupada para todos los estados de pedidos y totales
                var estadosPedidos = await _context.Pedidos
                    .AsNoTracking()
                    .GroupBy(p => p.Estado)
                    .Select(g => new { Estado = g.Key, Cantidad = g.Count(), Total = g.Sum(p => (decimal?)p.Total) ?? 0m })
                    .ToListAsync();

                metrics.TotalPedidos = estadosPedidos.Sum(e => e.Cantidad);
                metrics.IngresosTotales = estadosPedidos.Where(e => e.Estado != "Cancelado").Sum(e => e.Total);
                metrics.PedidosPendientes = estadosPedidos.FirstOrDefault(e => e.Estado == "Pendiente")?.Cantidad ?? 0;
                metrics.PedidosConfirmados = estadosPedidos.FirstOrDefault(e => e.Estado == "Confirmado")?.Cantidad ?? 0;
                metrics.PedidosEntregados = estadosPedidos.FirstOrDefault(e => e.Estado == "Entregado")?.Cantidad ?? 0;
                metrics.PedidosCancelados = estadosPedidos.FirstOrDefault(e => e.Estado == "Cancelado")?.Cantidad ?? 0;

                metrics.ProductosStockBajo = await _context.Productos
                    .AsNoTracking()
                    .Include(p => p.Categoria)
                    .Where(p => p.Stock <= p.StockMinimo && p.Estado)
                    .OrderBy(p => p.Stock)
                    .Take(5)
                    .ToListAsync();

                // Obtener ventas de los últimos 7 días para el gráfico
                var ventasPor7Dias = await _context.Pedidos
                    .AsNoTracking()
                    .Where(p => p.Fecha >= hace7Dias && p.Estado != "Cancelado")
                    .GroupBy(p => p.Fecha.Date)
                    .Select(g => new { Fecha = g.Key, Total = g.Sum(p => p.Total) })
                    .OrderBy(v => v.Fecha)
                    .ToListAsync();

                // Crear array con las ventas de cada día (0 si no hubo ventas ese día)
                var ventasPorDia = new decimal[7];
                for (int i = 0; i < 7; i++)
                {
                    var fecha = hace7Dias.AddDays(i).Date;
                    var venta = ventasPor7Dias.FirstOrDefault(v => v.Fecha == fecha);
                    ventasPorDia[i] = venta?.Total ?? 0m;
                }
                metrics.VentasPorDia = ventasPorDia;

                _cache.Set(cacheKey, metrics, TimeSpan.FromSeconds(60));
            }

            ViewBag.TotalProductos = metrics.TotalProductos;
            ViewBag.TotalUsuarios = metrics.TotalUsuarios;
            ViewBag.TotalCategorias = metrics.TotalCategorias;
            ViewBag.TotalPedidos = metrics.TotalPedidos;
            ViewBag.IngresosTotales = metrics.IngresosTotales;
            ViewBag.PedidosPendientes = metrics.PedidosPendientes;
            ViewBag.PedidosConfirmados = metrics.PedidosConfirmados;
            ViewBag.PedidosEntregados = metrics.PedidosEntregados;
            ViewBag.PedidosCancelados = metrics.PedidosCancelados;
            ViewBag.ProductosStockBajo = metrics.ProductosStockBajo;
            ViewBag.VentasUltimos7Dias = metrics.VentasPorDia;
            ViewBag.HayDatosReales = metrics.VentasPorDia.Any(v => v > 0);

            var ultimosPedidos = await _context.Pedidos
                .AsNoTracking()
                .Include(p => p.Usuario)
                .OrderByDescending(p => p.Fecha)
                .Take(6)
                .ToListAsync();

            return View("~/Views/Home/pages/_Dashboard.cshtml", ultimosPedidos);
        }

        // Listar usuarios
        public async Task<IActionResult> Index()
        {
            var usuarios = await _context.Usuarios
                .AsNoTracking()
                .OrderByDescending(u => u.Uuid)
                .ToListAsync();
            return View("~/Views/Home/pages/_Usuarios.cshtml", usuarios);
        }

        // Crear usuario (POST desde el Modal)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Usuario usuario)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            // Validar que el rol sea uno de los permitidos: Empleado, Administrador, Repartidor, Cliente
            var rolNormalizado = usuario.Rol?.Trim();
            if (string.Equals(rolNormalizado, "Administrador", StringComparison.OrdinalIgnoreCase))
                usuario.Rol = "Administrador";
            else if (string.Equals(rolNormalizado, "Repartidor", StringComparison.OrdinalIgnoreCase))
                usuario.Rol = "Repartidor";
            else if (string.Equals(rolNormalizado, "Cliente", StringComparison.OrdinalIgnoreCase))
                usuario.Rol = "Cliente";
            else
                usuario.Rol = "Empleado";

            // Validar formato de correo y duplicado
            if (!string.IsNullOrWhiteSpace(usuario.Email))
            {
                var emailRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!emailRegex.IsMatch(usuario.Email.Trim()))
                {
                    var errorMsg = "El correo electrónico debe ser válido (incluir '@', un dominio y una extensión como .com o .co).";
                    if (isAjax) return Json(new { success = false, message = errorMsg });
                    TempData["Error"] = errorMsg;
                    return Redirect("/Usuario");
                }

                var emailExiste = await _context.Usuarios
                    .AnyAsync(u => u.Email.ToLower() == usuario.Email.Trim().ToLower());

                if (emailExiste)
                {
                    var errorMsg = $"El correo '{usuario.Email}' ya está registrado con otro usuario.";
                    if (isAjax) return Json(new { success = false, message = errorMsg });
                    TempData["Error"] = errorMsg;
                    return Redirect("/Usuario");
                }
            }

            if (ModelState.IsValid)
            {
                usuario.Uuid = Guid.NewGuid().ToString();
                usuario.Nombre = usuario.Nombre?.Trim() ?? "";
                usuario.Apellido = usuario.Apellido?.Trim() ?? "";
                usuario.Email = usuario.Email?.Trim().ToLower() ?? "";
                usuario.Password = BCrypt.Net.BCrypt.HashPassword(usuario.Password?.Trim() ?? "", workFactor: 10);

                _context.Usuarios.Add(usuario);
                await _context.SaveChangesAsync();

                var successMsg = $"Usuario ({usuario.Rol}) {usuario.Nombre} creado con éxito.";
                if (isAjax)
                {
                    return Json(new {
                        success = true,
                        message = successMsg,
                        usuario = new {
                            uuid = usuario.Uuid,
                            nombre = usuario.Nombre,
                            apellido = usuario.Apellido,
                            email = usuario.Email,
                            rol = usuario.Rol,
                            estado = usuario.Estado
                        }
                    });
                }

                TempData["Success"] = successMsg;
                return Redirect("/Usuario");
            }

            var invalidMsg = "Por favor verifica los campos obligatorios del formulario.";
            if (isAjax) return Json(new { success = false, message = invalidMsg });

            TempData["Error"] = invalidMsg;
            return Redirect("/Usuario");
        }

        // Alternar Estado Activo / Inactivo
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleEstado(string id)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario != null)
            {
                usuario.Estado = !usuario.Estado;
                await _context.SaveChangesAsync();
                var msg = $"Estado de {usuario.Nombre} actualizado a {(usuario.Estado ? "Activo" : "Inactivo")}.";
                if (isAjax) return Json(new { success = true, message = msg, estado = usuario.Estado, id = usuario.Uuid });
                TempData["Success"] = msg;
            }
            else
            {
                var errorMsg = "Usuario no encontrado.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
            }

            return RedirectToAction(nameof(Index));
        }

        // "Eliminar" usuario -> en realidad solo se desactiva (borrado lógico)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var usuario = await _context.Usuarios.FindAsync(id);
            if (usuario != null)
            {
                usuario.Estado = false;
                await _context.SaveChangesAsync();
                var msg = $"Usuario {usuario.Nombre} desactivado correctamente.";
                if (isAjax) return Json(new { success = true, message = msg, id = usuario.Uuid, estado = false });
                TempData["Success"] = msg;
            }
            else
            {
                var errorMsg = "Usuario no encontrado.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Usuario/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(Usuario usuario)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            // Si el usuario no escribió una contraseña nueva, no la exigimos ni la tocamos
            var nuevaPassword = usuario.Password?.Trim();
            if (string.IsNullOrWhiteSpace(nuevaPassword))
            {
                ModelState.Remove(nameof(Usuario.Password));
            }

            if (!ModelState.IsValid)
            {
                var errorMsg = "Verifica los campos obligatorios.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(Index));
            }

            var existente = await _context.Usuarios.FindAsync(usuario.Uuid);
            if (existente == null)
            {
                var errorMsg = "Usuario no encontrado.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(Index));
            }

            // Validar formato de correo y duplicado (excluyendo al propio usuario que se está editando)
            if (!string.IsNullOrWhiteSpace(usuario.Email))
            {
                var emailRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!emailRegex.IsMatch(usuario.Email.Trim()))
                {
                    var errorMsg = "El correo electrónico debe ser válido (incluir '@', un dominio y una extensión como .com o .co).";
                    if (isAjax) return Json(new { success = false, message = errorMsg });
                    TempData["Error"] = errorMsg;
                    return RedirectToAction(nameof(Index));
                }

                var emailExiste = await _context.Usuarios
                    .AnyAsync(u => u.Uuid != usuario.Uuid && u.Email.ToLower() == usuario.Email.Trim().ToLower());

                if (emailExiste)
                {
                    var errorMsg = $"El correo '{usuario.Email}' ya está registrado con otro usuario.";
                    if (isAjax) return Json(new { success = false, message = errorMsg });
                    TempData["Error"] = errorMsg;
                    return RedirectToAction(nameof(Index));
                }
            }

            // Normalizar y asignar los datos
            existente.Nombre   = usuario.Nombre?.Trim() ?? "";
            existente.Apellido = usuario.Apellido?.Trim() ?? "";
            existente.Email    = usuario.Email?.Trim().ToLower() ?? "";
            existente.Estado   = usuario.Estado;

            // Solo actualizamos la contraseña si el usuario escribió una nueva
            if (!string.IsNullOrWhiteSpace(nuevaPassword))
            {
                existente.Password = BCrypt.Net.BCrypt.HashPassword(nuevaPassword);
            }

            // Normalizar rol (igual que en Create)
            var rol = usuario.Rol?.Trim();
            if (string.Equals(rol, "Administrador", StringComparison.OrdinalIgnoreCase))
                existente.Rol = "Administrador";
            else if (string.Equals(rol, "Repartidor", StringComparison.OrdinalIgnoreCase))
                existente.Rol = "Repartidor";
            else if (string.Equals(rol, "Cliente", StringComparison.OrdinalIgnoreCase))
                existente.Rol = "Cliente";
            else
                existente.Rol = "Empleado";

            // Guardar cambios
            await _context.SaveChangesAsync();
            var successMsg = $"Usuario {existente.Nombre} actualizado con éxito.";

            if (isAjax)
            {
                return Json(new {
                    success = true,
                    message = successMsg,
                    usuario = new {
                        uuid = existente.Uuid,
                        nombre = existente.Nombre,
                        apellido = existente.Apellido,
                        email = existente.Email,
                        rol = existente.Rol,
                        estado = existente.Estado
                    }
                });
            }

            TempData["Success"] = successMsg;
            return RedirectToAction(nameof(Index));
        }

    }
}