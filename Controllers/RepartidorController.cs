using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Drogueria.Data;
using Drogueria.Models;
using Drogueria.Hubs;

namespace Drogueria.Controllers
{
    [Authorize(Roles = "Administrador,Repartidor")]
    public class RepartidorController : Controller
    {
        private readonly AppDbContex _context;
        private readonly IHubContext<UbicacionHub> _hubContext;

        public RepartidorController(AppDbContex context, IHubContext<UbicacionHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // GET: /Repartidor/MisDomicilios  — Pedidos ACTIVOS (excluye Entregado)
        public async Task<IActionResult> MisDomicilios()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Uuid == userId);

            var pedidos = await _context.Pedidos
                .Include(p => p.Usuario)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.Producto)
                .Where(p => p.RepartidorId == userId && p.Estado != "Entregado")
                .OrderByDescending(p => p.Fecha)
                .ToListAsync();

            ViewData["Title"] = "Mis Domicilios";
            ViewData["ActiveNav"] = "misdomicilios";
            ViewBag.EstaEnLinea = usuario?.EstaEnLinea ?? true;
            ViewBag.RepartidorNombre = usuario != null ? $"{usuario.Nombre} {usuario.Apellido}".Trim() : "Repartidor";

            return View("~/Views/Repartidor/MisDomicilios.cshtml", pedidos);
        }

        // GET: /Repartidor/Historial — Pedidos ENTREGADOS (historial completo)
        public async Task<IActionResult> Historial()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Uuid == userId);

            var pedidos = await _context.Pedidos
                .Include(p => p.Usuario)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.Producto)
                .Where(p => p.RepartidorId == userId && p.Estado == "Entregado")
                .OrderByDescending(p => p.Fecha)
                .ToListAsync();

            ViewData["Title"] = "Historial de Entregas";
            ViewData["ActiveNav"] = "historial";
            ViewBag.EstaEnLinea = usuario?.EstaEnLinea ?? true;
            ViewBag.RepartidorNombre = usuario != null ? $"{usuario.Nombre} {usuario.Apellido}".Trim() : "Repartidor";

            return View("~/Views/Repartidor/Historial.cshtml", pedidos);
        }

        // POST: /Repartidor/ToggleDisponibilidad
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleDisponibilidad([FromForm] bool? enLinea)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers.Accept.ToString().Contains("application/json");
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Uuid == userId);

            if (usuario == null)
            {
                if (isAjax) return Json(new { success = false, message = "Usuario repartidor no encontrado." });
                TempData["Error"] = "Usuario repartidor no encontrado.";
                return RedirectToAction(nameof(MisDomicilios));
            }

            usuario.EstaEnLinea = enLinea.HasValue ? enLinea.Value : !usuario.EstaEnLinea;
            _context.Usuarios.Update(usuario);
            await _context.SaveChangesAsync();

            var mensaje = usuario.EstaEnLinea
                ? "¡Estás En Línea! Ahora estás disponible para recibir nuevos pedidos."
                : "Has pasado a modo Descanso. No recibirás nuevos pedidos mientras estés descansando, pero puedes finalizar los que tienes asignados.";

            if (isAjax)
            {
                return Json(new {
                    success = true,
                    estaEnLinea = usuario.EstaEnLinea,
                    message = mensaje
                });
            }

            TempData["Success"] = mensaje;
            return RedirectToAction(nameof(MisDomicilios));
        }

        // POST: /Repartidor/CambiarEstado
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(Guid id, string estado, string? codigoConfirmacion)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.Id == id && p.RepartidorId == userId);

            if (pedido == null)
            {
                var errorMsg = "Pedido no encontrado o no asignado a tu cuenta.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(MisDomicilios));
            }

            var estadosValidos = new[] { "En camino", "Entregado" };
            if (!estadosValidos.Contains(estado))
            {
                var errorMsg = "Estado no permitido para el repartidor.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(MisDomicilios));
            }

            // Validar que el repartidor no pueda iniciar un pedido si ya tiene otro en camino
            if (estado == "En camino")
            {
                var pedidoEnCaminoActivo = await _context.Pedidos
                    .AnyAsync(p => p.RepartidorId == userId && p.Estado == "En camino" && p.Id != id);

                if (pedidoEnCaminoActivo)
                {
                    var errorMsg = "Ya tienes un pedido en curso ('En camino'). Debes finalizar y entregar ese pedido antes de iniciar uno nuevo.";
                    if (isAjax) return Json(new { success = false, message = errorMsg });
                    TempData["Error"] = errorMsg;
                    return RedirectToAction(nameof(MisDomicilios));
                }
            }

            // Validar código de entrega de 6 dígitos al cambiar a estado Entregado
            if (estado == "Entregado")
            {
                var codigoLimpio = (codigoConfirmacion ?? "").Trim();
                if (string.IsNullOrEmpty(codigoLimpio))
                {
                    var errorMsg = "Debes ingresar el código de confirmación de 6 dígitos entregado por el cliente.";
                    if (isAjax) return Json(new { success = false, message = errorMsg });
                    TempData["Error"] = errorMsg;
                    return RedirectToAction(nameof(MisDomicilios));
                }

                const int MaxIntentosCodigo = 5;
                if (pedido.IntentosCodigoEntrega >= MaxIntentosCodigo)
                {
                    var errorMsg = "Se ha superado el número máximo de intentos permitidos (5). Por seguridad, comunícate con la administración de la droguería para validar la entrega.";
                    if (isAjax) return Json(new { success = false, message = errorMsg });
                    TempData["Error"] = errorMsg;
                    return RedirectToAction(nameof(MisDomicilios));
                }

                if (!string.IsNullOrEmpty(pedido.CodigoConfirmacion) &&
                    !string.Equals(pedido.CodigoConfirmacion.Trim(), codigoLimpio, StringComparison.OrdinalIgnoreCase))
                {
                    pedido.IntentosCodigoEntrega++;
                    _context.Pedidos.Update(pedido);
                    await _context.SaveChangesAsync();

                    var restantes = MaxIntentosCodigo - pedido.IntentosCodigoEntrega;
                    var errorMsg = restantes > 0
                        ? $"Código de confirmación incorrecto. Te quedan {restantes} intento(s)."
                        : "Código incorrecto. Has alcanzado el límite máximo de 5 intentos. Contacta con soporte o administración.";

                    if (isAjax) return Json(new { success = false, message = errorMsg });
                    TempData["Error"] = errorMsg;
                    return RedirectToAction(nameof(MisDomicilios));
                }
            }

            pedido.Estado = estado;
            _context.Pedidos.Update(pedido);
            await _context.SaveChangesAsync();

            // Notificar a los clientes suscritos al pedido sobre el cambio de estado (ej: "En camino")
            await _hubContext.Clients.Group(pedido.Id.ToString()).SendAsync("EstadoCambiado", pedido.Id.ToString(), estado);

            var successMsg = $"El pedido ha sido marcado como '{estado}' exitosamente.";
            if (isAjax)
            {
                return Json(new { success = true, message = successMsg, id = pedido.Id, estado = pedido.Estado });
            }

            TempData["Success"] = successMsg;
            return RedirectToAction(nameof(MisDomicilios));
        }

        // POST: /Repartidor/ActualizarUbicacion
        [HttpPost]
        public async Task<IActionResult> ActualizarUbicacion([FromForm] Guid id, [FromForm] double lat, [FromForm] double lng)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var pedido = await _context.Pedidos.FirstOrDefaultAsync(p => p.Id == id && p.RepartidorId == userId);

            if (pedido == null)
            {
                return Json(new { success = false, message = "Pedido no encontrado." });
            }

            pedido.LatitudRepartidor = lat;
            pedido.LongitudRepartidor = lng;
            pedido.UltimaUbicacionFecha = DateTime.Now;

            _context.Pedidos.Update(pedido);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
    }

    /// <summary>
    /// Reglas de ganancias del repartidor:
    /// - Tarifa de envío: $8.000 fijos por cada entrega.
    /// - Propina voluntaria dejada por el cliente.
    /// - Comisión del 12% sobre la compra total cuando el pedido supera el límite de $45.000.
    /// </summary>
    public static class RepartidorGananciasHelper
    {
        public const decimal LimitePedido = 45000m;
        public const decimal TarifaEnvioBase = 8000m;
        public const decimal PorcentajeComision = 0.12m;

        public class CalculoGanancia
        {
            public decimal SubtotalProductos { get; set; }
            public decimal TarifaEnvio { get; set; } = TarifaEnvioBase;
            public decimal Propina { get; set; }
            public decimal Comision12 { get; set; }
            public bool SuperaLimite { get; set; }
            public decimal GananciaTotal => TarifaEnvio + Propina + Comision12;
        }

        public static CalculoGanancia Calcular(Pedido p)
        {
            var subtotal = (p.Detalles != null && p.Detalles.Count > 0)
                ? p.Detalles.Sum(d => d.Subtotal)
                : p.Total;

            // En la tienda: pedidos < 45.000 cobran 8.000 de envío al cliente, >= 45.000 tienen envío gratis
            var envioCobradoCliente = subtotal >= LimitePedido ? 0m : TarifaEnvioBase;
            var diff = p.Total - subtotal - envioCobradoCliente;
            var propina = diff > 0m ? diff : 0m;

            bool supera = subtotal >= LimitePedido;
            decimal comision12 = supera ? Math.Round(subtotal * PorcentajeComision, 2) : 0m;

            return new CalculoGanancia
            {
                SubtotalProductos = subtotal,
                TarifaEnvio = TarifaEnvioBase,
                Propina = propina,
                Comision12 = comision12,
                SuperaLimite = supera
            };
        }
    }
}
