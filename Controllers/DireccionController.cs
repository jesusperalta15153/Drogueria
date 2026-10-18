using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using Drogueria.Data;
using Drogueria.Models;

namespace Drogueria.Controllers
{
    [Authorize(Roles = "Cliente,Administrador,Empleado,Repartidor")]
    public class DireccionController : Controller
    {
        private readonly AppDbContex _context;
        private readonly IHttpClientFactory _httpClientFactory;

        public DireccionController(AppDbContex context, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        // GET: /Direccion/Geocodificar
        [HttpGet]
        public async Task<IActionResult> Geocodificar(string? direccion, string? ciudad, string? departamento)
        {
            if (string.IsNullOrWhiteSpace(direccion))
                return Json(new { success = false, message = "Ingresa una dirección primero." });

            // Limpiar caracteres que confunden a Nominatim (# en direcciones colombianas)
            var dirLimpia = (direccion ?? "")
                .Replace("#", "")
                .Replace("  ", " ")
                .Trim();
            var query = $"{dirLimpia}, {ciudad}, {departamento}, Colombia".Trim(',', ' ');
            var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&limit=1&addressdetails=1";

            try
            {
                var client = _httpClientFactory.CreateClient("Nominatim");
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("DrogueriaApp/1.0");

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return Json(new { success = false, message = "No se pudo consultar el servicio de mapas." });

                var json = await response.Content.ReadAsStringAsync();
                var resultados = JsonSerializer.Deserialize<JsonElement[]>(json);

                if (resultados == null || resultados.Length == 0)
                {
                    // Fallback solo con ciudad
                    var query2 = $"{dirLimpia}, {ciudad}, Colombia".Trim(',', ' ');
                    var url2 = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query2)}&format=json&limit=1&addressdetails=1";
                    var resp2 = await client.GetAsync(url2);
                    if (resp2.IsSuccessStatusCode)
                    {
                        var json2 = await resp2.Content.ReadAsStringAsync();
                        resultados = JsonSerializer.Deserialize<JsonElement[]>(json2);
                    }
                }

                if (resultados == null || resultados.Length == 0)
                    return Json(new { success = false, message = "La dirección ingresada no corresponde a una ubicación real o no fue encontrada en el mapa de Colombia." });

                var r = resultados[0];
                var lat = double.Parse(r.GetProperty("lat").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var lon = double.Parse(r.GetProperty("lon").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var displayName = r.GetProperty("display_name").GetString() ?? query;

                return Json(new { success = true, lat, lon, displayName });
            }
            catch (Exception)
            {
                return Json(new { success = false, message = "Error al conectar con el servicio de mapas." });
            }
        }

        // GET: /Direccion/CalcularRuta
        [HttpGet]
        public async Task<IActionResult> CalcularRuta(double origenLat, double origenLng, double destinoLat, double destinoLng)
        {
            if (Math.Abs(origenLat) < 0.0001 || Math.Abs(origenLng) < 0.0001 ||
                Math.Abs(destinoLat) < 0.0001 || Math.Abs(destinoLng) < 0.0001)
            {
                return Json(new { success = false, message = "Coordenadas no válidas para calcular la ruta." });
            }

            try
            {
                var oLng = origenLng.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var oLat = origenLat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var dLng = destinoLng.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var dLat = destinoLat.ToString(System.Globalization.CultureInfo.InvariantCulture);

                var url = $"https://router.project-osrm.org/route/v1/driving/{oLng},{oLat};{dLng},{dLat}?overview=full&geometries=geojson";
                var client = _httpClientFactory.CreateClient("OSRM");
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("DrogueriaApp/1.0");
                }

                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("code", out var code) && code.GetString() == "Ok" &&
                        root.TryGetProperty("routes", out var routes) && routes.GetArrayLength() > 0)
                    {
                        var route = routes[0];
                        var duration = route.GetProperty("duration").GetDouble(); // Segundos
                        var distance = route.GetProperty("distance").GetDouble(); // Metros
                        var geometry = route.GetProperty("geometry");

                        var durMin = Math.Max(1, (int)Math.Round(duration / 60.0));
                        var distKm = Math.Round(distance / 1000.0, 1);
                        var horaLlegada = DateTime.Now.AddSeconds(duration).ToString("hh:mm tt", System.Globalization.CultureInfo.InvariantCulture);

                        return Json(new
                        {
                            success = true,
                            duracionSegundos = duration,
                            duracionMinutos = durMin,
                            distanciaMetros = distance,
                            distanciaKm = distKm,
                            horaEstimadaLlegada = horaLlegada,
                            tiempoTexto = durMin < 60 ? $"{durMin} min" : $"{durMin / 60} h {durMin % 60} min",
                            distanciaTexto = $"{distKm} km",
                            geometry = JsonSerializer.Deserialize<object>(geometry.GetRawText())
                        });
                    }
                }
            }
            catch
            {
                // Fallback a cálculo local
            }

            // Fallback con fórmula Haversine + velocidad promedio urbana de 25 km/h + curvatura de vías (1.35)
            var dKm = CalcularDistanciaHaversine(origenLat, origenLng, destinoLat, destinoLng) * 1.35;
            var fallbackDistKm = Math.Max(0.1, Math.Round(dKm, 1));
            var fallbackMin = Math.Max(2, (int)Math.Round((fallbackDistKm / 25.0) * 60));
            var fallbackHora = DateTime.Now.AddMinutes(fallbackMin).ToString("hh:mm tt", System.Globalization.CultureInfo.InvariantCulture);

            return Json(new
            {
                success = true,
                isFallback = true,
                duracionSegundos = fallbackMin * 60,
                duracionMinutos = fallbackMin,
                distanciaMetros = fallbackDistKm * 1000,
                distanciaKm = fallbackDistKm,
                horaEstimadaLlegada = fallbackHora,
                tiempoTexto = $"~{fallbackMin} min",
                distanciaTexto = $"~{fallbackDistKm} km",
                geometry = new
                {
                    type = "LineString",
                    coordinates = new[]
                    {
                        new[] { origenLng, origenLat },
                        new[] { destinoLng, destinoLat }
                    }
                }
            });
        }

        private static double CalcularDistanciaHaversine(double lat1, double lon1, double lat2, double lon2)
        {
            var r = 6371.0; // Radio terrestre en km
            var dLat = (lat2 - lat1) * (Math.PI / 180.0);
            var dLon = (lon2 - lon1) * (Math.PI / 180.0);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * (Math.PI / 180.0)) * Math.Cos(lat2 * (Math.PI / 180.0)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return r * c;
        }

        // GET: /Direccion/MisDirecciones
        [HttpGet]
        public async Task<IActionResult> MisDirecciones()
        {
            var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(usuarioId))
                return Json(new { success = false, message = "No autenticado." });

            var direcciones = await _context.DireccionesUsuario
                .Where(d => d.UsuarioId == usuarioId)
                .OrderByDescending(d => d.EsPredeterminada)
                .ThenByDescending(d => d.FechaCreacion)
                .Select(d => new
                {
                    id = d.Id,
                    nombreContacto = d.NombreContacto,
                    telefono = d.Telefono,
                    direccion = d.Direccion,
                    ciudad = d.Ciudad,
                    departamento = d.Departamento,
                    esPredeterminada = d.EsPredeterminada,
                    fechaCreacion = d.FechaCreacion
                })
                .ToListAsync();

            return Json(new { success = true, data = direcciones });
        }

        // POST: /Direccion/Guardar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guardar(
            Guid? id,
            string nombreContacto,
            string telefono,
            string direccion,
            string ciudad,
            string departamento,
            bool esPredeterminada = false)
        {
            var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(usuarioId))
                return Json(new { success = false, message = "No autenticado." });

            // Validaciones básicas
            if (string.IsNullOrWhiteSpace(nombreContacto))
                return Json(new { success = false, message = "El nombre de contacto es obligatorio." });
            if (string.IsNullOrWhiteSpace(telefono))
                return Json(new { success = false, message = "El teléfono es obligatorio." });
            if (string.IsNullOrWhiteSpace(departamento))
                return Json(new { success = false, message = "El departamento es obligatorio." });
            if (string.IsNullOrWhiteSpace(ciudad))
                return Json(new { success = false, message = "La ciudad es obligatoria." });
            if (string.IsNullOrWhiteSpace(direccion))
                return Json(new { success = false, message = "La dirección es obligatoria." });

            // ── Validación de ubicación geográfica real mediante Nominatim ──
            try
            {
                var dirLimpia = direccion.Replace("#", "").Trim();
                var queryGeo = $"{dirLimpia}, {ciudad}, {departamento}, Colombia".Trim(',', ' ');
                var client = _httpClientFactory.CreateClient("Nominatim");
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("DrogueriaApp/1.0");

                var respGeo = await client.GetAsync($"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(queryGeo)}&format=json&limit=1");
                if (respGeo.IsSuccessStatusCode)
                {
                    var jsonGeo = await respGeo.Content.ReadAsStringAsync();
                    var resultadosGeo = JsonSerializer.Deserialize<JsonElement[]>(jsonGeo);

                    if (resultadosGeo == null || resultadosGeo.Length == 0)
                    {
                        var queryFallback = $"{dirLimpia}, {ciudad}, Colombia".Trim(',', ' ');
                        var respFallback = await client.GetAsync($"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(queryFallback)}&format=json&limit=1");
                        if (respFallback.IsSuccessStatusCode)
                        {
                            var jsonFallback = await respFallback.Content.ReadAsStringAsync();
                            resultadosGeo = JsonSerializer.Deserialize<JsonElement[]>(jsonFallback);
                        }
                    }

                    if (resultadosGeo == null || resultadosGeo.Length == 0)
                    {
                        return Json(new { success = false, message = "La dirección ingresada no corresponde a una ubicación real o no se encontró en el mapa. Ingresa una nomenclatura válida (Ej: Calle 45 # 12-34)." });
                    }
                }
            }
            catch
            {
                // Si hay problemas de conexión con Nominatim, se permite guardar para no bloquear al usuario por fallas de terceros
            }

            bool esNueva = id == null || id == Guid.Empty;

            if (esNueva)
            {
                // Si es la primera dirección del usuario, ponerla como predeterminada automáticamente
                bool tieneDirecciones = await _context.DireccionesUsuario
                    .AnyAsync(d => d.UsuarioId == usuarioId);

                if (!tieneDirecciones)
                    esPredeterminada = true;

                // Si se marca como predeterminada, desmarcar las demás
                if (esPredeterminada)
                {
                    await _context.DireccionesUsuario
                        .Where(d => d.UsuarioId == usuarioId && d.EsPredeterminada)
                        .ExecuteUpdateAsync(s => s.SetProperty(d => d.EsPredeterminada, false));
                }

                var nueva = new DireccionUsuario
                {
                    Id = Guid.NewGuid(),
                    UsuarioId = usuarioId,
                    NombreContacto = nombreContacto.Trim(),
                    Telefono = telefono.Trim(),
                    Direccion = direccion.Trim(),
                    Ciudad = ciudad.Trim(),
                    Departamento = departamento.Trim(),
                    EsPredeterminada = esPredeterminada,
                    FechaCreacion = DateTime.Now
                };

                _context.DireccionesUsuario.Add(nueva);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Dirección guardada correctamente.",
                    data = new
                    {
                        id = nueva.Id,
                        nombreContacto = nueva.NombreContacto,
                        telefono = nueva.Telefono,
                        direccion = nueva.Direccion,
                        ciudad = nueva.Ciudad,
                        departamento = nueva.Departamento,
                        esPredeterminada = nueva.EsPredeterminada,
                        fechaCreacion = nueva.FechaCreacion
                    }
                });
            }
            else
            {
                // Editar dirección existente
                var existente = await _context.DireccionesUsuario
                    .FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == usuarioId);

                if (existente == null)
                    return Json(new { success = false, message = "Dirección no encontrada." });

                // Si se marca como predeterminada, desmarcar las demás
                if (esPredeterminada && !existente.EsPredeterminada)
                {
                    await _context.DireccionesUsuario
                        .Where(d => d.UsuarioId == usuarioId && d.EsPredeterminada)
                        .ExecuteUpdateAsync(s => s.SetProperty(d => d.EsPredeterminada, false));
                }

                existente.NombreContacto = nombreContacto.Trim();
                existente.Telefono = telefono.Trim();
                existente.Direccion = direccion.Trim();
                existente.Ciudad = ciudad.Trim();
                existente.Departamento = departamento.Trim();
                existente.EsPredeterminada = esPredeterminada;

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Dirección actualizada correctamente.",
                    data = new
                    {
                        id = existente.Id,
                        nombreContacto = existente.NombreContacto,
                        telefono = existente.Telefono,
                        direccion = existente.Direccion,
                        ciudad = existente.Ciudad,
                        departamento = existente.Departamento,
                        esPredeterminada = existente.EsPredeterminada,
                        fechaCreacion = existente.FechaCreacion
                    }
                });
            }
        }

        // POST: /Direccion/Eliminar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(Guid id)
        {
            var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(usuarioId))
                return Json(new { success = false, message = "No autenticado." });

            var direccion = await _context.DireccionesUsuario
                .FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == usuarioId);

            if (direccion == null)
                return Json(new { success = false, message = "Dirección no encontrada." });

            bool eraPredeterminada = direccion.EsPredeterminada;
            _context.DireccionesUsuario.Remove(direccion);
            await _context.SaveChangesAsync();

            // Si era predeterminada, asignar la más reciente como predeterminada
            if (eraPredeterminada)
            {
                var siguiente = await _context.DireccionesUsuario
                    .Where(d => d.UsuarioId == usuarioId)
                    .OrderByDescending(d => d.FechaCreacion)
                    .FirstOrDefaultAsync();

                if (siguiente != null)
                {
                    siguiente.EsPredeterminada = true;
                    await _context.SaveChangesAsync();
                }
            }

            // Retornar la lista actualizada
            var listaDirs = await _context.DireccionesUsuario
                .Where(d => d.UsuarioId == usuarioId)
                .OrderByDescending(d => d.EsPredeterminada)
                .ThenByDescending(d => d.FechaCreacion)
                .Select(d => new
                {
                    id = d.Id,
                    nombreContacto = d.NombreContacto,
                    telefono = d.Telefono,
                    direccion = d.Direccion,
                    ciudad = d.Ciudad,
                    departamento = d.Departamento,
                    esPredeterminada = d.EsPredeterminada
                })
                .ToListAsync();

            return Json(new
            {
                success = true,
                message = "Dirección eliminada correctamente.",
                data = listaDirs
            });
        }

        // POST: /Direccion/Predeterminar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Predeterminar(Guid id)
        {
            var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(usuarioId))
                return Json(new { success = false, message = "No autenticado." });

            var direccion = await _context.DireccionesUsuario
                .FirstOrDefaultAsync(d => d.Id == id && d.UsuarioId == usuarioId);

            if (direccion == null)
                return Json(new { success = false, message = "Dirección no encontrada." });

            // Desmarcar todas las del usuario
            await _context.DireccionesUsuario
                .Where(d => d.UsuarioId == usuarioId && d.EsPredeterminada)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.EsPredeterminada, false));

            // Marcar la seleccionada
            direccion.EsPredeterminada = true;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Dirección predeterminada actualizada." });
        }
    }
}
