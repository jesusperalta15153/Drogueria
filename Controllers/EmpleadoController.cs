using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Drogueria.Data;
using Drogueria.Models;
using Drogueria.Services;

namespace Drogueria.Controllers
{
    [Authorize(Roles = "Administrador,Empleado")]
    public class EmpleadoController : Controller
    {
        private readonly AppDbContex _context;
        private readonly AsignacionRepartidorService _asignacionService;

        public EmpleadoController(AppDbContex context, AsignacionRepartidorService asignacionService)
        {
            _context = context;
            _asignacionService = asignacionService;
        }

        // ── PRODUCTOS (vista de consulta) ─────────────────────
        // GET: /Empleado/Productos
        public async Task<IActionResult> Productos()
        {
            var productos = await _context.Productos
                .Include(p => p.Categoria)
                .OrderByDescending(p => p.FechaCreacion)
                .ToListAsync();

            ViewData["Title"] = "Productos";
            ViewData["ActiveNav"] = "productos";
            ViewBag.Categorias = await _context.Categorias
                .Where(c => c.Estado)
                .OrderBy(c => c.Nombre)
                .ToListAsync();

            return View("~/Views/Empleado/_Productos.cshtml", productos);
        }

        // ── INVENTARIO ────────────────────────────────────────
        // GET: /Empleado/Inventario
        public async Task<IActionResult> Inventario()
        {
            var productos = await _context.Productos
                .Include(p => p.Categoria)
                .OrderBy(p => p.Nombre)
                .ToListAsync();

            ViewData["Title"] = "Inventario";
            ViewData["ActiveNav"] = "inventario";
            ViewBag.Categorias = await _context.Categorias
                .Where(c => c.Estado)
                .OrderBy(c => c.Nombre)
                .ToListAsync();

            return View("~/Views/Empleado/_Inventario.cshtml", productos);
        }

        // POST: /Empleado/ActualizarStock
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarStock(Guid id, int stock)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null)
            {
                var errorMsg = "Producto no encontrado.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(Inventario));
            }

            if (stock < 0) stock = 0;

            producto.Stock = stock;
            _context.Productos.Update(producto);
            await _context.SaveChangesAsync();

            var successMsg = $"Stock de '{producto.Nombre}' actualizado a {stock} unidades.";
            if (isAjax)
            {
                return Json(new { success = true, message = successMsg, id = producto.Id, stock = producto.Stock });
            }

            TempData["Exito"] = successMsg;
            return RedirectToAction(nameof(Inventario));
        }

        // ── PEDIDOS ───────────────────────────────────────────
        // GET: /Empleado/Pedidos
        public async Task<IActionResult> Pedidos()
        {
            var pedidos = await _context.Pedidos
                .Include(p => p.Usuario)
                .Include(p => p.Repartidor)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.Producto)
                .OrderByDescending(p => p.Fecha)
                .ToListAsync();

            ViewData["Title"] = "Pedidos";
            ViewData["ActiveNav"] = "pedidos";

            ViewBag.Repartidores = await _context.Usuarios
                .Where(u => u.Rol == "Repartidor" && u.Estado)
                .OrderBy(u => u.Nombre)
                .ToListAsync();

            return View("~/Views/Empleado/_Pedidos.cshtml", pedidos);
        }

        // POST: /Empleado/CambiarEstadoPedido
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstadoPedido(Guid id, string estado)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var pedido = await _context.Pedidos.FindAsync(id);
            if (pedido == null)
            {
                var errorMsg = "Pedido no encontrado.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(Pedidos));
            }

            var estadosValidos = new[] { "Pendiente", "Confirmado", "En camino", "Entregado", "Cancelado" };
            if (!estadosValidos.Contains(estado))
            {
                var errorMsg = "Estado no válido.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(Pedidos));
            }

            pedido.Estado = estado;
            _context.Pedidos.Update(pedido);
            await _context.SaveChangesAsync();

            var successMsg = $"Pedido actualizado a '{estado}'.";
            if (isAjax)
            {
                return Json(new { success = true, message = successMsg, id = pedido.Id, estado = pedido.Estado });
            }

            TempData["Exito"] = successMsg;
            return RedirectToAction(nameof(Pedidos));
        }

        // POST: /Empleado/AsignarRepartidor
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AsignarRepartidor(Guid id, string? repartidorId)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var pedido = await _context.Pedidos.FindAsync(id);
            if (pedido == null)
            {
                var errorMsg = "Pedido no encontrado.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(Pedidos));
            }

            string successMsg;
            string? repartidorNombre = null;

            if (string.IsNullOrWhiteSpace(repartidorId))
            {
                pedido.RepartidorId = null;
                successMsg = "Repartidor desasignado del pedido.";
            }
            else
            {
                var repartidor = await _context.Usuarios.FirstOrDefaultAsync(u => u.Uuid == repartidorId && u.Rol == "Repartidor");
                if (repartidor == null)
                {
                    var errorMsg = "El repartidor seleccionado no existe o no es válido.";
                    if (isAjax) return Json(new { success = false, message = errorMsg });
                    TempData["Error"] = errorMsg;
                    return RedirectToAction(nameof(Pedidos));
                }

                pedido.RepartidorId = repartidor.Uuid;
                repartidorNombre = $"{repartidor.Nombre} {repartidor.Apellido}".Trim();
                if (pedido.Estado == "Pendiente")
                {
                    pedido.Estado = "Confirmado";
                }
                successMsg = $"Repartidor '{repartidorNombre}' asignado al pedido.";
            }

            _context.Pedidos.Update(pedido);
            await _context.SaveChangesAsync();

            if (isAjax)
            {
                return Json(new {
                    success = true,
                    message = successMsg,
                    id = pedido.Id,
                    repartidorId = pedido.RepartidorId,
                    repartidorNombre = repartidorNombre,
                    estado = pedido.Estado
                });
            }

            TempData["Exito"] = successMsg;
            return RedirectToAction(nameof(Pedidos));
        }

        // POST: /Empleado/AutoAsignarRepartidor
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutoAsignarRepartidor(Guid id)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var pedido = await _context.Pedidos.FindAsync(id);
            if (pedido == null)
            {
                var errorMsg = "Pedido no encontrado.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(Pedidos));
            }

            var repartidor = await _asignacionService.SeleccionarRepartidorOptimoAsync(pedido.DireccionEntrega);
            if (repartidor == null)
            {
                var errorMsg = "No hay repartidores en línea disponibles en este momento. El pedido permanecerá como 'Pendiente'.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
                return RedirectToAction(nameof(Pedidos));
            }

            pedido.RepartidorId = repartidor.Uuid;
            if (pedido.Estado == "Pendiente")
            {
                pedido.Estado = "Confirmado";
            }

            _context.Pedidos.Update(pedido);
            await _context.SaveChangesAsync();

            var nombreCompleto = $"{repartidor.Nombre} {repartidor.Apellido}".Trim();
            var successMsg = $"Asignado a {nombreCompleto} por cercanía GPS y disponibilidad.";

            if (isAjax)
            {
                return Json(new {
                    success = true,
                    message = successMsg,
                    id = pedido.Id,
                    repartidorId = pedido.RepartidorId,
                    repartidorNombre = nombreCompleto,
                    estado = pedido.Estado
                });
            }

            TempData["Exito"] = successMsg;
            return RedirectToAction(nameof(Pedidos));
        }
    }
}

