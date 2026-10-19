using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Drogueria.Data;
using Drogueria.Models;

namespace Drogueria.Controllers
{
    [AllowAnonymous]
    [Route("api/[controller]")]
    [ApiController]
    public class BotController : ControllerBase
    {
        private readonly AppDbContex _context;

        public BotController(AppDbContex context)
        {
            _context = context;
        }

        // GET: /api/bot/diagnostico
        [HttpGet("diagnostico")]
        public async Task<IActionResult> Diagnostico()
        {
            try
            {
                var canConnect = await _context.Database.CanConnectAsync();
                var totalPedidos = canConnect ? await _context.Pedidos.CountAsync() : 0;
                var totalProductos = canConnect ? await _context.Productos.CountAsync() : 0;

                return Ok(new
                {
                    success = true,
                    estado = canConnect ? "OPERATIVO" : "DEGRADADO",
                    dbConectada = canConnect,
                    totalPedidos,
                    totalProductos,
                    servidor = "FarmaVida Web ASP.NET (.NET 10)",
                    fechaUtc = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    estado = "ERROR",
                    dbConectada = false,
                    mensaje = ex.Message,
                    fechaUtc = DateTime.UtcNow
                });
            }
        }

        // GET: /api/bot/pedido/{codigo}
        [HttpGet("pedido/{codigo}")]
        public async Task<IActionResult> ConsultarPedido(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return BadRequest(new { success = false, message = "El código de pedido es requerido." });

            var search = codigo.Trim().TrimStart('#');

            try
            {
                var pedidosQuery = _context.Pedidos
                    .AsNoTracking()
                    .Include(p => p.Detalles)
                        .ThenInclude(d => d.Producto)
                    .Include(p => p.Usuario)
                    .Include(p => p.Repartidor)
                    .AsQueryable();

                // Buscar por GUID exacto o coincidencia de prefijo (primeros 8 caracteres)
                Pedido? pedido = null;
                if (Guid.TryParse(search, out var guidExacto))
                {
                    pedido = await pedidosQuery.FirstOrDefaultAsync(p => p.Id == guidExacto);
                }

                if (pedido == null)
                {
                    var idStr = search.ToLower();
                    pedido = await pedidosQuery
                        .Where(p => p.Id.ToString().ToLower().StartsWith(idStr) || p.Id.ToString().ToLower().Contains(idStr))
                        .OrderByDescending(p => p.Fecha)
                        .FirstOrDefaultAsync();
                }

                if (pedido == null)
                {
                    return Ok(new { success = false, message = $"No se encontró ningún pedido con el código '{search}'." });
                }

                var shortId = pedido.Id.ToString().Substring(0, Math.Min(8, pedido.Id.ToString().Length)).ToUpper();

                return Ok(new
                {
                    success = true,
                    pedido = new
                    {
                        id = pedido.Id.ToString(),
                        idCorto = shortId,
                        nombre = pedido.Usuario?.Nombre ?? "Cliente",
                        apellido = pedido.Usuario?.Apellido ?? "",
                        email = pedido.Usuario?.Email ?? "",
                        fecha = pedido.Fecha,
                        estado = pedido.Estado,
                        direccionEntrega = pedido.DireccionEntrega ?? "Entrega local",
                        total = pedido.Total,
                        metodoPago = pedido.MetodoPago,
                        codigoConfirmacion = pedido.CodigoConfirmacion,
                        repartidor = pedido.Repartidor != null ? $"{pedido.Repartidor.Nombre} {pedido.Repartidor.Apellido}".Trim() : null,
                        telefonoRepartidor = pedido.Repartidor?.Telefono
                    },
                    detalles = pedido.Detalles.Select(d => new
                    {
                        cantidad = d.Cantidad,
                        precioUnitario = d.PrecioUnitario,
                        subtotal = d.Subtotal,
                        nombre = d.Producto?.Nombre ?? "Medicamento"
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // GET: /api/bot/productos?q={termino}
        [HttpGet("productos")]
        public async Task<IActionResult> BuscarProductos([FromQuery] string? q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Ok(new { success = true, productos = new List<object>() });

            try
            {
                var termino = q.Trim().ToLower();
                var prods = await _context.Productos
                    .AsNoTracking()
                    .Where(p => p.Estado && EF.Functions.Like(p.Nombre, $"%{termino}%"))
                    .Take(5)
                    .Select(p => new
                    {
                        nombre = p.Nombre,
                        precio = p.Precio,
                        stock = p.Stock,
                        descripcion = p.Descripcion ?? "",
                        miligramos = p.Miligramos,
                        unidadMedida = p.UnidadMedida
                    })
                    .ToListAsync();

                return Ok(new { success = true, productos = prods });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
