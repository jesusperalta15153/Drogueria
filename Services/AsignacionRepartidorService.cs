using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Drogueria.Data;
using Drogueria.Models;

namespace Drogueria.Services
{
    public class CandidatoRepartidor
    {
        public Usuario Repartidor { get; set; } = null!;
        public double DistanciaKm { get; set; }
        public int PedidosActivos { get; set; }
        public DateTime? UltimaAsignacion { get; set; }
        public double LatitudActual { get; set; }
        public double LongitudActual { get; set; }
        public bool TieneGpsEnVivo { get; set; }
        public double Puntaje { get; set; }
    }

    public class AsignacionRepartidorService
    {
        private readonly AppDbContex _context;
        private readonly IHttpClientFactory _httpClientFactory;

        // Coordenadas base de la sede de la droguería (Bogotá - FarmaVida)
        public const double SedeLatDefault = 4.60971;
        public const double SedeLngDefault = -74.08175;

        public AsignacionRepartidorService(AppDbContex context, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Calcula la distancia en kilómetros entre dos coordenadas usando la fórmula de Haversine.
        /// </summary>
        public static double CalcularDistanciaKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0; // Radio de la Tierra en kilómetros
            var dLat = (lat2 - lat1) * Math.PI / 180.0;
            var dLon = (lon2 - lon1) * Math.PI / 180.0;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return Math.Round(R * c, 2);
        }

        /// <summary>
        /// Geocodifica una dirección usando el cliente Nominatim registrado.
        /// Retorna (Latitud, Longitud) o null si no se pudo resolver.
        /// </summary>
        public async Task<(double Lat, double Lng)?> GeocodificarDireccionAsync(string? direccion)
        {
            if (string.IsNullOrWhiteSpace(direccion)) return null;

            try
            {
                var dirLimpia = direccion.Replace("#", "").Replace("  ", " ").Trim();
                var query = $"{dirLimpia}, Colombia";
                var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&limit=1";

                var client = _httpClientFactory.CreateClient("Nominatim");
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("DrogueriaApp/1.0");
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await client.GetAsync(url, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    var resultados = JsonSerializer.Deserialize<JsonElement[]>(json);
                    if (resultados != null && resultados.Length > 0)
                    {
                        var primer = resultados[0];
                        if (primer.TryGetProperty("lat", out var latProp) && primer.TryGetProperty("lon", out var lonProp))
                        {
                            if (double.TryParse(latProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                                double.TryParse(lonProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lng))
                            {
                                return (lat, lng);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback silencioso si el servicio de mapas externo no responde
            }

            return null;
        }

        /// <summary>
        /// Evalúa y rankea a todos los repartidores activos ordenados por idoneidad según GPS, proximidad, carga de trabajo y equidad.
        /// </summary>
        public async Task<List<CandidatoRepartidor>> ObtenerCandidatosOrdenadosAsync(string? direccionEntrega = null)
        {
            // 1. Obtener todos los repartidores habilitados y que estén EN LÍNEA para trabajar
            var repartidores = await _context.Usuarios
                .Where(u => u.Rol == "Repartidor" && u.Estado && u.EstaEnLinea)
                .ToListAsync();

            if (!repartidores.Any())
            {
                return new List<CandidatoRepartidor>();
            }

            // 2. Determinar punto objetivo de referencia (destino de entrega o sede de la droguería)
            var coordsDestino = await GeocodificarDireccionAsync(direccionEntrega);
            var targetLat = coordsDestino?.Lat ?? SedeLatDefault;
            var targetLng = coordsDestino?.Lng ?? SedeLngDefault;

            var ahora = DateTime.Now;
            var candidatos = new List<CandidatoRepartidor>();
            var random = new Random();

            foreach (var rep in repartidores)
            {
                // Pedidos activos (Confirmado o En camino)
                var pedidosActivos = await _context.Pedidos
                    .CountAsync(p => p.RepartidorId == rep.Uuid && (p.Estado == "Confirmado" || p.Estado == "En camino"));

                // Último pedido para saber su última ubicación y fecha de asignación
                var ultimoPedido = await _context.Pedidos
                    .Where(p => p.RepartidorId == rep.Uuid)
                    .OrderByDescending(p => p.Fecha)
                    .FirstOrDefaultAsync();

                // Buscar su posición GPS más reciente
                var ultimoGps = await _context.Pedidos
                    .Where(p => p.RepartidorId == rep.Uuid && p.LatitudRepartidor.HasValue && p.LongitudRepartidor.HasValue)
                    .OrderByDescending(p => p.UltimaUbicacionFecha ?? p.Fecha)
                    .Select(p => new {
                        Lat = p.LatitudRepartidor!.Value,
                        Lng = p.LongitudRepartidor!.Value,
                        Fecha = p.UltimaUbicacionFecha
                    })
                    .FirstOrDefaultAsync();

                double repLat = SedeLatDefault;
                double repLng = SedeLngDefault;
                bool tieneGpsEnVivo = false;

                // Si reportó GPS en los últimos 45 minutos, se toma su ubicación real en calle
                if (ultimoGps != null && ultimoGps.Fecha.HasValue && (ahora - ultimoGps.Fecha.Value).TotalMinutes <= 45)
                {
                    repLat = ultimoGps.Lat;
                    repLng = ultimoGps.Lng;
                    tieneGpsEnVivo = true;
                }

                // Distancia al objetivo (en km)
                var distKm = CalcularDistanciaKm(repLat, repLng, targetLat, targetLng);

                // Cálculo de Puntaje Ponderado:
                // - Menor distancia en km = Mejor (base)
                // - Penalización de +2.5 km virtuales por cada pedido activo que ya tenga asignado
                // - Penalización adicional si ya superó 3 pedidos activos (evita saturación excesiva)
                var penalizacionCarga = pedidosActivos * 2.5;
                if (pedidosActivos >= 3)
                {
                    penalizacionCarga += 10.0;
                }

                // Factor de equidad de tiempo (hasta -0.5 km si lleva horas sin pedido)
                double bonoTiempoEspera = 0;
                if (ultimoPedido != null)
                {
                    var horasSinPedido = (ahora - ultimoPedido.Fecha).TotalHours;
                    bonoTiempoEspera = Math.Min(horasSinPedido * 0.1, 1.0); // Hasta 1 km de ventaja por espera
                }
                else
                {
                    bonoTiempoEspera = 1.0; // Nunca ha tenido pedidos, darle prioridad
                }

                // Pequeño jitter aleatorio (0.001 - 0.05) para desempatar en caso de idéntica distancia y carga
                var jitter = random.NextDouble() * 0.05;

                var puntajeFinal = distKm + penalizacionCarga - bonoTiempoEspera + jitter;

                candidatos.Add(new CandidatoRepartidor
                {
                    Repartidor = rep,
                    DistanciaKm = distKm,
                    PedidosActivos = pedidosActivos,
                    UltimaAsignacion = ultimoPedido?.Fecha,
                    LatitudActual = repLat,
                    LongitudActual = repLng,
                    TieneGpsEnVivo = tieneGpsEnVivo,
                    Puntaje = Math.Round(puntajeFinal, 3)
                });
            }

            // Ordenar por menor puntaje (el más idóneo primero)
            return candidatos.OrderBy(c => c.Puntaje).ToList();
        }

        /// <summary>
        /// Selecciona automáticamente al repartidor más idóneo por GPS/Proximidad y menor saturación.
        /// </summary>
        public async Task<Usuario?> SeleccionarRepartidorOptimoAsync(string? direccionEntrega = null)
        {
            var candidatos = await ObtenerCandidatosOrdenadosAsync(direccionEntrega);
            return candidatos.FirstOrDefault()?.Repartidor;
        }
    }
}
