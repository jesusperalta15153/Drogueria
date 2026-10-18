using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using System.Text.Json;
using Drogueria.Data;
using Drogueria.Models;
using Drogueria.Services;

namespace Drogueria.Controllers
{
    [Authorize(Roles = "Cliente,Administrador")]
    public class TiendaController : Controller
    {
        private readonly AppDbContex _context;
        private readonly AsignacionRepartidorService _asignacionService;
        private readonly IMemoryCache _cache;
        private const string CarritoSessionKey = "Carrito";
        private const string CarritoCookieKey  = "Drogueria_Carrito_Persistente";

        public TiendaController(AppDbContex context, AsignacionRepartidorService asignacionService, IMemoryCache cache)
        {
            _context = context;
            _asignacionService = asignacionService;
            _cache = cache;
        }

        // GET: /Tienda/Catalogo
        public async Task<IActionResult> Catalogo(string? buscar, Guid? categoriaId)
        {
            var query = _context.Productos
                .AsNoTracking()
                .Include(p => p.Categoria)
                .Where(p => p.Estado && p.Stock > 0);

            if (!string.IsNullOrWhiteSpace(buscar))
                query = query.Where(p => p.Nombre.Contains(buscar) || (p.Descripcion != null && p.Descripcion.Contains(buscar)));

            if (categoriaId.HasValue && categoriaId.Value != Guid.Empty)
                query = query.Where(p => p.CategoriaId == categoriaId.Value);

            var productos = await query.OrderBy(p => p.Nombre).ToListAsync();

            const string catCacheKey = "Categorias_Tienda_Catalogo";
            if (!_cache.TryGetValue(catCacheKey, out List<Categoria>? categorias) || categorias == null)
            {
                categorias = await _context.Categorias
                    .AsNoTracking()
                    .Where(c => c.Estado)
                    .OrderBy(c => c.Nombre)
                    .ToListAsync();

                _cache.Set(catCacheKey, categorias, TimeSpan.FromMinutes(5));
            }

            ViewBag.Categorias = categorias;
            ViewBag.BuscarActual = buscar;
            ViewBag.CategoriaActual = categoriaId;
            ViewData["Title"] = "Catálogo";
            ViewData["ActiveNav"] = "catalogo";

            // Cantidad de items en carrito para el badge
            ViewBag.CarritoCount = ObtenerCarrito().Sum(c => c.Cantidad);

            return View("~/Views/Tienda/_Catalogo.cshtml", productos);
        }

        // POST: /Tienda/AgregarAlCarrito
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AgregarAlCarrito(Guid productoId, int cantidad = 1)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json");

            if (cantidad < 1) cantidad = 1;

            var producto = await _context.Productos.FindAsync(productoId);
            if (producto == null || !producto.Estado)
            {
                if (isAjax)
                    return Json(new { success = false, message = "Producto no disponible." });

                TempData["Error"] = "Producto no disponible.";
                return RedirectToAction(nameof(Catalogo));
            }

            var carrito = ObtenerCarrito();
            var itemExistente = carrito.FirstOrDefault(c => c.ProductoId == productoId);

            int cantidadEnCarrito = itemExistente?.Cantidad ?? 0;
            int nuevaCantidadTotal = cantidadEnCarrito + cantidad;

            // Validar que no exceda el stock disponible ni el límite máximo por pedido
            if (producto.LimiteMaximoPorPedido.HasValue && nuevaCantidadTotal > producto.LimiteMaximoPorPedido.Value)
            {
                var mensaje = $"El producto '{producto.Nombre}' tiene un límite máximo de {producto.LimiteMaximoPorPedido.Value} unidades por pedido.";

                if (isAjax)
                    return Json(new { success = false, message = mensaje });

                TempData["Error"] = mensaje;
                return RedirectToAction(nameof(Catalogo));
            }

            if (nuevaCantidadTotal > producto.Stock)
            {
                var mensaje = producto.Stock == 0
                    ? $"'{producto.Nombre}' está agotado."
                    : $"Solo hay {producto.Stock} unidades disponibles de '{producto.Nombre}'. Ya tienes {cantidadEnCarrito} en el carrito.";

                if (isAjax)
                    return Json(new { success = false, message = mensaje });

                TempData["Error"] = mensaje;
                return RedirectToAction(nameof(Catalogo));
            }

            if (itemExistente != null)
            {
                itemExistente.Cantidad = nuevaCantidadTotal;
                itemExistente.StockDisponible = producto.Stock;
                itemExistente.LimiteMaximoPorPedido = producto.LimiteMaximoPorPedido;
            }
            else
            {
                carrito.Add(new CarritoItem
                {
                    ProductoId = producto.Id,
                    Nombre = producto.Nombre,
                    Imagen = producto.Imagen,
                    Precio = producto.Precio,
                    Cantidad = cantidad,
                    StockDisponible = producto.Stock,
                    LimiteMaximoPorPedido = producto.LimiteMaximoPorPedido
                });
            }

            GuardarCarrito(carrito);
            var totalItems = carrito.Sum(c => c.Cantidad);

            if (isAjax)
            {
                return Json(new { 
                    success = true, 
                    message = $"'{producto.Nombre}' añadido al carrito ({nuevaCantidadTotal} ud.).", 
                    totalItems,
                    totalCarrito = carrito.Sum(c => c.Subtotal)
                });
            }

            TempData["Exito"] = $"'{producto.Nombre}' agregado al carrito.";
            return RedirectToAction(nameof(Catalogo));
        }

        // GET: /Tienda/Carrito
        public async Task<IActionResult> Carrito()
        {
            var carrito = ObtenerCarrito();

            // Sincronizar stock actual y limite de cada producto
            if (carrito.Any())
            {
                var productosIds = carrito.Select(c => c.ProductoId).ToList();
                var productos = await _context.Productos
                    .Where(p => productosIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p);

                foreach (var item in carrito)
                {
                    if (productos.TryGetValue(item.ProductoId, out var producto))
                    {
                        item.Precio = producto.Precio;
                        item.Nombre = producto.Nombre;
                        item.Imagen = producto.Imagen;
                        item.StockDisponible = producto.Stock;
                        item.LimiteMaximoPorPedido = producto.LimiteMaximoPorPedido;
                    }
                }
                GuardarCarrito(carrito);
            }

            // Cargar direcciones del usuario para el selector de entrega
            var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.Direcciones = await _context.DireccionesUsuario
                .Where(d => d.UsuarioId == usuarioId)
                .OrderByDescending(d => d.EsPredeterminada)
                .ThenByDescending(d => d.FechaCreacion)
                .ToListAsync();

            ViewData["Title"] = "Mi Carrito";
            ViewData["ActiveNav"] = "carrito";
            return View("~/Views/Tienda/_Carrito.cshtml", carrito);
        }

        // POST: /Tienda/EliminarDelCarrito
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarDelCarrito(Guid productoId)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json");

            var carrito = ObtenerCarrito();
            carrito.RemoveAll(c => c.ProductoId == productoId);
            GuardarCarrito(carrito);

            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    message = "Producto eliminado del carrito.",
                    totalItems = carrito.Sum(c => c.Cantidad),
                    totalCarrito = carrito.Sum(c => c.Subtotal)
                });
            }

            TempData["Exito"] = "Producto eliminado del carrito.";
            return RedirectToAction(nameof(Carrito));
        }

        // POST: /Tienda/ActualizarCantidad
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarCantidad(Guid productoId, int cantidad)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                         Request.Headers["Accept"].ToString().Contains("application/json");

            if (cantidad < 1) cantidad = 1;

            // Validar stock disponible
            var producto = await _context.Productos.FindAsync(productoId);
            if (producto == null)
            {
                if (isAjax)
                    return Json(new { success = false, message = "Producto no encontrado." });

                TempData["Error"] = "Producto no encontrado.";
                return RedirectToAction(nameof(Carrito));
            }

            if (producto.LimiteMaximoPorPedido.HasValue && cantidad > producto.LimiteMaximoPorPedido.Value)
            {
                var mensaje = $"El producto '{producto.Nombre}' tiene un límite máximo de {producto.LimiteMaximoPorPedido.Value} unidades por pedido.";
                if (isAjax)
                    return Json(new { success = false, message = mensaje, limiteMaximo = producto.LimiteMaximoPorPedido.Value });

                TempData["Error"] = mensaje;
                return RedirectToAction(nameof(Carrito));
            }

            if (cantidad > producto.Stock)
            {
                var mensaje = $"Solo hay {producto.Stock} unidades disponibles de '{producto.Nombre}'.";
                if (isAjax)
                    return Json(new { success = false, message = mensaje, stockDisponible = producto.Stock });

                TempData["Error"] = mensaje;
                return RedirectToAction(nameof(Carrito));
            }

            var carrito = ObtenerCarrito();
            var item = carrito.FirstOrDefault(c => c.ProductoId == productoId);
            if (item != null)
            {
                item.Cantidad = cantidad;
                item.Precio = producto.Precio;
                item.StockDisponible = producto.Stock;
                item.LimiteMaximoPorPedido = producto.LimiteMaximoPorPedido;
                GuardarCarrito(carrito);
            }

            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    totalItems = carrito.Sum(c => c.Cantidad),
                    subtotalItem = item?.Subtotal ?? 0,
                    totalCarrito = carrito.Sum(c => c.Subtotal)
                });
            }

            return RedirectToAction(nameof(Carrito));
        }

        // POST: /Tienda/VaciarCarrito
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult VaciarCarrito()
        {
            GuardarCarrito(new List<CarritoItem>());
            TempData["Exito"] = "Carrito vaciado.";
            return RedirectToAction(nameof(Carrito));
        }

        // PEDIDOS

        // GET: /Tienda/CheckoutGet  — Redireccionado desde RealizarPedido cuando hay errores de validación
        [HttpGet]
        public async Task<IActionResult> CheckoutGet()
        {
            var direccionEntrega      = TempData["DireccionEntrega"]  as string ?? "";
            var productosSeleccionados = TempData["ProductosSeleccionados"] as string ?? "";

            if (string.IsNullOrWhiteSpace(productosSeleccionados))
            {
                return RedirectToAction(nameof(Carrito));
            }

            var ids = productosSeleccionados
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s.Trim(), out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToHashSet();

            var carrito = ObtenerCarrito();
            var seleccionados = carrito.Where(c => ids.Contains(c.ProductoId)).ToList();

            if (!seleccionados.Any())
            {
                return RedirectToAction(nameof(Carrito));
            }

            var prodIds = seleccionados.Select(c => c.ProductoId).ToList();
            var productosDb = await _context.Productos
                .Where(p => prodIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p);

            foreach (var item in seleccionados)
                if (productosDb.TryGetValue(item.ProductoId, out var prod))
                {
                    item.Precio = prod.Precio;
                    item.Nombre = prod.Nombre;
                    item.Imagen = prod.Imagen;
                    item.StockDisponible = prod.Stock;
                    item.LimiteMaximoPorPedido = prod.LimiteMaximoPorPedido;
                }

            // Generar código fijo de Supergiros para el usuario actual
            var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(usuarioId))
            {
                var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(usuarioId));
                ViewBag.SupergirosCode = "SG-" + BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 7).ToUpper();
            }

            var propinaStr = TempData["Propina"] as string;
            if (decimal.TryParse(propinaStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p) && p >= 0m)
            {
                ViewBag.Propina = p;
            }

            ViewBag.DireccionEntrega = direccionEntrega;
            ViewData["Title"] = "Confirmar Pedido";
            return View("~/Views/Tienda/_Checkout.cshtml", seleccionados);
        }

        // GET: /Tienda/Checkout — Permite recargar la página del checkout (F5) sin perder los productos ni la dirección
        [HttpGet]
        public async Task<IActionResult> Checkout()
        {
            var carrito = ObtenerCarrito();
            if (!carrito.Any())
            {
                return RedirectToAction(nameof(Carrito));
            }

            var seleccionadosStr = HttpContext.Session.GetString("Checkout_Seleccionados");
            List<CarritoItem> seleccionados = carrito;

            if (!string.IsNullOrWhiteSpace(seleccionadosStr))
            {
                var ids = seleccionadosStr
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Guid.TryParse(s.Trim(), out var g) ? g : Guid.Empty)
                    .Where(g => g != Guid.Empty)
                    .ToHashSet();

                var filtrados = carrito.Where(c => ids.Contains(c.ProductoId)).ToList();
                if (filtrados.Any()) seleccionados = filtrados;
            }

            // Sincronizar stock actual y precio oficial desde BD
            var prodIds = seleccionados.Select(c => c.ProductoId).ToList();
            var productosDb = await _context.Productos
                .Where(p => prodIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p);

            foreach (var item in seleccionados)
                if (productosDb.TryGetValue(item.ProductoId, out var prod))
                {
                    item.Precio = prod.Precio;
                    item.Nombre = prod.Nombre;
                    item.Imagen = prod.Imagen;
                    item.StockDisponible = prod.Stock;
                    item.LimiteMaximoPorPedido = prod.LimiteMaximoPorPedido;
                }

            var direccionGuardada = HttpContext.Session.GetString("Checkout_Direccion");
            if (string.IsNullOrWhiteSpace(direccionGuardada))
            {
                var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var dir = await _context.DireccionesUsuario
                    .Where(d => d.UsuarioId == usuarioId && d.EsPredeterminada)
                    .FirstOrDefaultAsync();
                if (dir != null)
                {
                    direccionGuardada = $"{dir.NombreContacto} | {dir.Direccion}, {dir.Ciudad}, {dir.Departamento} | Tel: {dir.Telefono}";
                }
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(userId));
                ViewBag.SupergirosCode = "SG-" + BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 7).ToUpper();
            }

            ViewBag.DireccionEntrega = direccionGuardada ?? "";
            ViewData["Title"] = "Confirmar Pedido";
            return View("~/Views/Tienda/_Checkout.cshtml", seleccionados);
        }

        // POST: /Tienda/Checkout
        // Muestra la vista de confirmación con los productos seleccionados, dirección y métodos de pago.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(string? direccionEntrega, string? productosSeleccionados)
        {
            if (string.IsNullOrWhiteSpace(productosSeleccionados))
            {
                TempData["Error"] = "No seleccionaste ningún producto.";
                return RedirectToAction(nameof(Carrito));
            }

            // Guardar en sesión para sobrevivir recargas F5
            HttpContext.Session.SetString("Checkout_Seleccionados", productosSeleccionados);
            HttpContext.Session.SetString("Checkout_Direccion", direccionEntrega ?? "");

            var ids = productosSeleccionados
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s.Trim(), out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToHashSet();

            var carrito = ObtenerCarrito();
            var seleccionados = carrito.Where(c => ids.Contains(c.ProductoId)).ToList();

            if (!seleccionados.Any())
            {
                TempData["Error"] = "Los productos seleccionados no se encontraron en el carrito.";
                return RedirectToAction(nameof(Carrito));
            }

            // Sincronizar stock actual y precio oficial desde BD
            var prodIds = seleccionados.Select(c => c.ProductoId).ToList();
            var productosDb = await _context.Productos
                .Where(p => prodIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p);

            foreach (var item in seleccionados)
                if (productosDb.TryGetValue(item.ProductoId, out var prod))
                {
                    item.Precio = prod.Precio;
                    item.Nombre = prod.Nombre;
                    item.Imagen = prod.Imagen;
                    item.StockDisponible = prod.Stock;
                    item.LimiteMaximoPorPedido = prod.LimiteMaximoPorPedido;
                }

            // Refrescar carrito con precios verificados
            GuardarCarrito(carrito);

            // Generar código fijo de Supergiros para el usuario actual
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(userId));
                ViewBag.SupergirosCode = "SG-" + BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 7).ToUpper();
            }

            ViewBag.DireccionEntrega = direccionEntrega ?? "";
            ViewData["Title"] = "Confirmar Pedido";
            return View("~/Views/Tienda/_Checkout.cshtml", seleccionados);
        }

        // POST: /Tienda/RealizarPedido
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RealizarPedido(
            string? direccionEntrega,
            string? metodoPago,
            List<string>? productosSeleccionados,
            decimal? propina,
            // Campos de tarjeta
            string? tarjeta_numero,
            string? tarjeta_titular,
            string? tarjeta_vencimiento,
            string? tarjeta_cvv,
            string? tarjeta_tipo_documento,
            string? tarjeta_documento,
            string? marcaTarjeta)
        {
            var metodosValidos = new HashSet<string> { "tarjeta-debito", "tarjeta-credito", "supergiros", "contraentrega" };
            if (!metodosValidos.Contains(metodoPago ?? ""))
            {
                TempData["Error"] = "Selecciona un método de pago válido.";
                TempData["DireccionEntrega"] = direccionEntrega;
                TempData["ProductosSeleccionados"] = productosSeleccionados != null
                    ? string.Join(",", productosSeleccionados) : "";
                TempData["Propina"] = (propina.HasValue && propina.Value >= 0m)
                    ? propina.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "1000";
                return RedirectToAction(nameof(CheckoutGet));
            }

            // ── Validación backend de campos de tarjeta ──────────────────────
            if (metodoPago!.StartsWith("tarjeta"))
            {
                var erroresTarjeta = new List<string>();

                // Número de tarjeta
                var numLimpio = (tarjeta_numero ?? "").Replace(" ", "").Trim();
                if (string.IsNullOrEmpty(numLimpio))
                    erroresTarjeta.Add("El número de tarjeta es obligatorio.");
                else if (numLimpio.Length < 13 || numLimpio.Length > 16 || !numLimpio.All(char.IsDigit))
                    erroresTarjeta.Add("El número de tarjeta debe tener entre 13 y 16 dígitos.");
                else
                {
                    var marcaValida = numLimpio.StartsWith("4") ? "visa"
                        : (new System.Text.RegularExpressions.Regex(@"^5[1-5]|^2[2-7]").IsMatch(numLimpio) ? "mastercard" : "");
                    if (string.IsNullOrEmpty(marcaValida))
                        erroresTarjeta.Add("Solo se aceptan tarjetas Visa o Mastercard.");
                }

                // Titular
                if (string.IsNullOrWhiteSpace(tarjeta_titular) || tarjeta_titular.Trim().Length < 3)
                    erroresTarjeta.Add("El nombre del titular es obligatorio (mínimo 3 caracteres).");

                // Vencimiento MM/AA
                var expRegex = new System.Text.RegularExpressions.Regex(@"^(0[1-9]|1[0-2])\/(\d{2})$");
                if (string.IsNullOrWhiteSpace(tarjeta_vencimiento) || !expRegex.IsMatch(tarjeta_vencimiento.Trim()))
                    erroresTarjeta.Add("La fecha de vencimiento debe tener formato MM/AA.");
                else
                {
                    var partes = tarjeta_vencimiento.Trim().Split('/');
                    int mes = int.Parse(partes[0]), anio = int.Parse(partes[1]) + 2000;
                    if (new DateTime(anio, mes, 1) < new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1))
                        erroresTarjeta.Add("La tarjeta está vencida.");
                }

                // CVV
                if (string.IsNullOrWhiteSpace(tarjeta_cvv) || !System.Text.RegularExpressions.Regex.IsMatch(tarjeta_cvv.Trim(), @"^\d{3,4}$"))
                    erroresTarjeta.Add("El código de seguridad (CVV) debe tener 3 o 4 dígitos.");

                // Tipo de documento
                var tiposDocValidos = new HashSet<string> { "CC", "CE", "PA", "NIT", "TI" };
                if (string.IsNullOrWhiteSpace(tarjeta_tipo_documento) || !tiposDocValidos.Contains(tarjeta_tipo_documento.Trim()))
                    erroresTarjeta.Add("Selecciona un tipo de documento válido.");

                // Número de documento
                if (string.IsNullOrWhiteSpace(tarjeta_documento) || tarjeta_documento.Trim().Length < 5)
                    erroresTarjeta.Add("El número de documento es obligatorio (mínimo 5 caracteres).");

                if (erroresTarjeta.Any())
                {
                    TempData["Error"] = string.Join(" | ", erroresTarjeta);
                    TempData["DireccionEntrega"] = direccionEntrega;
                    TempData["ProductosSeleccionados"] = productosSeleccionados != null
                        ? string.Join(",", productosSeleccionados) : "";
                    TempData["Propina"] = (propina.HasValue && propina.Value >= 0m)
                        ? propina.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "1000";
                    return RedirectToAction(nameof(CheckoutGet));
                }
            }

            var carrito = ObtenerCarrito();

            // Filtrar solo los productos seleccionados en el checkout
            List<CarritoItem> itemsPedido;
            if (productosSeleccionados != null && productosSeleccionados.Any())
            {
                var ids = productosSeleccionados
                    .Select(s => Guid.TryParse(s.Trim(), out var g) ? g : Guid.Empty)
                    .Where(g => g != Guid.Empty)
                    .ToHashSet();
                itemsPedido = carrito.Where(c => ids.Contains(c.ProductoId)).ToList();
            }
            else
            {
                itemsPedido = carrito;
            }

            if (!itemsPedido.Any())
            {
                TempData["Error"] = "Tu carrito está vacío o no seleccionaste productos.";
                return RedirectToAction(nameof(Carrito));
            }

            var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(usuarioId))
                return RedirectToAction("Index", "Home");

            // Validar stock antes de confirmar
            var productosIds = itemsPedido.Select(c => c.ProductoId).ToList();
            var productos = await _context.Productos
                .Where(p => productosIds.Contains(p.Id))
                .ToListAsync();

            foreach (var item in itemsPedido)
            {
                var producto = productos.FirstOrDefault(p => p.Id == item.ProductoId);
                if (producto == null || !producto.Estado)
                {
                    TempData["Error"] = $"El producto '{item.Nombre}' ya no está disponible.";
                    return RedirectToAction(nameof(Carrito));
                }

                if (item.Cantidad > producto.Stock)
                {
                    TempData["Error"] = $"Solo hay {producto.Stock} unidades disponibles de '{producto.Nombre}'. Por favor ajusta tu carrito.";
                    return RedirectToAction(nameof(Carrito));
                }

                // Blindaje de seguridad: el precio SIEMPRE se toma de la base de datos oficial
                item.Precio = producto.Precio;
            }

            // Construir etiqueta de método de pago con marca si aplica
            var etiquetaPago = metodoPago?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(marcaTarjeta))
            {
                var marcaLabel = marcaTarjeta.Trim().ToLower() == "visa" ? "Visa"
                               : marcaTarjeta.Trim().ToLower() == "mastercard" ? "Mastercard"
                               : marcaTarjeta.Trim();
                etiquetaPago = $"{etiquetaPago} ({marcaLabel})";
            }

            // ── Código de entrega criptográfico (6 dígitos seguros para el pedido) ──
            var codigoEntrega = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

            // ── Reglas de Envío y Propina ──
            // Recalcular subtotal usando exclusivamente los precios verificados en BD
            var subtotal = itemsPedido.Sum(c => c.Precio * c.Cantidad);
            var costoEnvio = subtotal >= 45000m ? 0m : 8000m;
            var montoPropina = propina.HasValue ? Math.Clamp(Math.Round(propina.Value, 0), 0m, 500000m) : 1000m;
            var totalFinal = subtotal + costoEnvio + montoPropina;

            var pedido = new Pedido
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                Fecha = DateTime.Now,
                Estado = "Pendiente",
                Total = totalFinal,
                DireccionEntrega = direccionEntrega?.Trim(),
                MetodoPago = etiquetaPago,
                CodigoConfirmacion = codigoEntrega
            };

            foreach (var item in itemsPedido)
            {
                // Descontar stock del producto
                var producto = productos.First(p => p.Id == item.ProductoId);
                producto.Stock -= item.Cantidad;

                pedido.Detalles.Add(new DetallePedido
                {
                    Id = Guid.NewGuid(),
                    PedidoId = pedido.Id,
                    ProductoId = item.ProductoId,
                    Cantidad = item.Cantidad,
                    PrecioUnitario = producto.Precio,
                    Subtotal = producto.Precio * item.Cantidad
                });
            }

            // Asignación inteligente por Proximidad/GPS, carga balanceada y equidad
            var repartidorAsignado = await _asignacionService.SeleccionarRepartidorOptimoAsync(pedido.DireccionEntrega);

            if (repartidorAsignado != null)
            {
                pedido.RepartidorId = repartidorAsignado.Uuid;
                pedido.Estado = "Confirmado";
            }

            _context.Pedidos.Add(pedido);
            await _context.SaveChangesAsync();

            // Limpiar carrito
            GuardarCarrito(new List<CarritoItem>());

            var mensajeExito = repartidorAsignado != null
                ? $"Pedido realizado y asignado a {repartidorAsignado.Nombre} {repartidorAsignado.Apellido}."
                : "Pedido realizado. Se asignará un repartidor pronto.";

            TempData["Exito"] = mensajeExito;

            // Pasar código de confirmación de entrega a la vista
            TempData["CodigoConfirmacion"] = codigoEntrega;
            TempData["MetodoPagoInfo"] = metodoPago; // 'tarjeta' | 'supergiros' | 'contraentrega'

            return RedirectToAction(nameof(MisPedidos));
        }

        // GET: /Tienda/MisPedidos
        public async Task<IActionResult> MisPedidos()
        {
            var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var pedidos = await _context.Pedidos
                .AsNoTracking()
                .Include(p => p.Repartidor)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.Producto)
                        .ThenInclude(prod => prod.Categoria)
                .Where(p => p.UsuarioId == usuarioId)
                .OrderByDescending(p => p.Estado == "En camino" || p.Estado == "Confirmado" || p.Estado == "Pendiente")
                .ThenByDescending(p => p.Fecha)
                .ToListAsync();

            ViewData["Title"] = "Mis Pedidos";
            ViewData["ActiveNav"] = "pedidos";
            return View("~/Views/Tienda/_MisPedidos.cshtml", pedidos);
        }

        // GET: /Tienda/DetallePedido/{id}
        public async Task<IActionResult> DetallePedido(Guid id)
        {
            var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var pedido = await _context.Pedidos
                .AsNoTracking()
                .Include(p => p.Repartidor)
                .Include(p => p.Usuario)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.Producto)
                        .ThenInclude(prod => prod.Categoria)
                .FirstOrDefaultAsync(p => p.Id == id && p.UsuarioId == usuarioId);

            if (pedido == null)
            {
                TempData["Error"] = "Pedido no encontrado o no tienes permiso para visualizarlo.";
                return RedirectToAction(nameof(MisPedidos));
            }

            ViewData["Title"] = $"Detalle del Pedido #{pedido.Id.ToString().Substring(0, 8).ToUpper()}";
            ViewData["ActiveNav"] = "pedidos";
            return View("~/Views/Tienda/_DetallePedido.cshtml", pedido);
        }

        private List<CarritoItem> ObtenerCarrito()
        {
            var json = HttpContext.Session.GetString(CarritoSessionKey);
            if (string.IsNullOrEmpty(json))
            {
                // Fallback a Cookie persistente para que el carrito nunca se pierda al recargar la página o reiniciar
                if (Request.Cookies.TryGetValue(CarritoCookieKey, out var cookieJson) && !string.IsNullOrEmpty(cookieJson))
                {
                    try
                    {
                        var desdeCookie = JsonSerializer.Deserialize<List<CarritoItem>>(cookieJson);
                        if (desdeCookie != null && desdeCookie.Any())
                        {
                            HttpContext.Session.SetString(CarritoSessionKey, cookieJson);
                            return desdeCookie;
                        }
                    }
                    catch { }
                }
                return new List<CarritoItem>();
            }
            return JsonSerializer.Deserialize<List<CarritoItem>>(json) ?? new List<CarritoItem>();
        }

        private void GuardarCarrito(List<CarritoItem> carrito)
        {
            var json = JsonSerializer.Serialize(carrito);
            HttpContext.Session.SetString(CarritoSessionKey, json);

            // Persistir en Cookie por 30 días para que no se borre al recargar la página
            if (carrito.Any())
            {
                Response.Cookies.Append(CarritoCookieKey, json, new CookieOptions
                {
                    Expires = DateTimeOffset.Now.AddDays(30),
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax
                });
            }
            else
            {
                Response.Cookies.Delete(CarritoCookieKey);
            }
        }
    }
}


