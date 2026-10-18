using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Drogueria.Data;
using Drogueria.Models;

namespace Drogueria.Hubs
{
    [Authorize]
    public class UbicacionHub : Hub
    {
        private readonly IServiceScopeFactory _scopeFactory;

        // Historial en memoria de respaldo rápido durante la sesión activa
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<object>> _historialChatMemoria = new();

        public UbicacionHub(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Valida si el usuario conectado tiene autorización para acceder a los datos o transmisión del pedido:
        /// Debe ser:
        /// 1) Administrador o Empleado
        /// 2) El Cliente dueño del pedido (UsuarioId)
        /// 3) El Repartidor asignado al pedido (RepartidorId)
        /// </summary>
        private async Task<bool> TienePermisoSobrePedidoAsync(string pedidoId)
        {
            if (string.IsNullOrWhiteSpace(pedidoId) || !Guid.TryParse(pedidoId, out var pId))
                return false;

            var user = Context.User;
            if (user == null || user.Identity?.IsAuthenticated != true)
                return false;

            // Administradores y Empleados de farmacia tienen acceso de supervisión y gestión
            if (user.IsInRole("Administrador") || user.IsInRole("Empleado"))
                return true;

            var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentUserId))
                return false;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContex>();

                var pedido = await db.Pedidos
                    .AsNoTracking()
                    .Where(p => p.Id == pId)
                    .Select(p => new { p.UsuarioId, p.RepartidorId })
                    .FirstOrDefaultAsync();

                if (pedido == null)
                    return false;

                // Es el cliente dueño del pedido
                if (string.Equals(pedido.UsuarioId, currentUserId, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Es el repartidor asignado a este pedido
                if (!string.IsNullOrEmpty(pedido.RepartidorId) &&
                    string.Equals(pedido.RepartidorId, currentUserId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error validando permisos en UbicacionHub: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Permite que un cliente, repartidor asignado o admin se una al grupo del pedido para escuchar/transmitir actualizaciones de ubicación.
        /// </summary>
        public async Task JoinOrderGroup(string pedidoId)
        {
            if (await TienePermisoSobrePedidoAsync(pedidoId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, pedidoId);
            }
        }

        /// <summary>
        /// Transmite la ubicación GPS actual del repartidor a los usuarios autorizados del pedido.
        /// </summary>
        public async Task EnviarUbicacion(string pedidoId, double lat, double lng)
        {
            if (await TienePermisoSobrePedidoAsync(pedidoId))
            {
                await Clients.Group(pedidoId).SendAsync("ReceiveLocation", pedidoId, lat, lng);
            }
        }

        /// <summary>
        /// Transmite la ubicación GPS del repartidor junto con tiempo estimado y distancia calculados.
        /// </summary>
        public async Task EnviarUbicacionConRuta(string pedidoId, double lat, double lng, string? tiempoEstimado, string? distancia)
        {
            if (await TienePermisoSobrePedidoAsync(pedidoId))
            {
                await Clients.Group(pedidoId).SendAsync("ReceiveLocationWithRoute", pedidoId, lat, lng, tiempoEstimado, distancia);
                await Clients.Group(pedidoId).SendAsync("ReceiveLocation", pedidoId, lat, lng);
            }
        }

        /// <summary>
        /// Obtiene el historial permanente de mensajes de un pedido desde la base de datos (y memoria si aplica).
        /// </summary>
        public async Task<List<object>> ObtenerHistorialChat(string pedidoId)
        {
            if (!await TienePermisoSobrePedidoAsync(pedidoId))
                return new List<object>();

            var resultado = new List<object>();

            try
            {
                if (Guid.TryParse(pedidoId, out var pId))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContex>();

                    var dbMensajes = await db.MensajesChat
                        .AsNoTracking()
                        .Where(m => m.PedidoId == pId)
                        .OrderBy(m => m.Fecha)
                        .Select(m => new
                        {
                            remitente = m.Remitente,
                            mensaje = m.Mensaje,
                            hora = m.Fecha.ToString("HH:mm")
                        })
                        .ToListAsync();

                    if (dbMensajes.Any())
                    {
                        return dbMensajes.Cast<object>().ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al consultar historial de chat en BD: {ex.Message}");
            }

            // Fallback a memoria si la BD aún no tenía o falló
            if (_historialChatMemoria.TryGetValue(pedidoId, out var memoriaList))
            {
                lock (memoriaList)
                {
                    return new List<object>(memoriaList);
                }
            }

            return resultado;
        }

        /// <summary>
        /// Envía un mensaje de chat entre cliente y repartidor para un pedido específico,
        /// guardándolo de forma PERMANENTE en la base de datos y transmitiéndolo por SignalR.
        /// </summary>
        public async Task EnviarMensajeChat(string pedidoId, string remitente, string mensaje)
        {
            if (string.IsNullOrWhiteSpace(pedidoId) || string.IsNullOrWhiteSpace(mensaje))
                return;

            if (!await TienePermisoSobrePedidoAsync(pedidoId))
                return;

            var ahora = DateTime.Now;
            var horaTexto = ahora.ToString("HH:mm");
            var msgObj = new { remitente, mensaje, hora = horaTexto };

            // 1. Guardar en memoria para acceso ultrarrápido
            var lista = _historialChatMemoria.GetOrAdd(pedidoId, _ => new List<object>());
            lock (lista)
            {
                lista.Add(msgObj);
            }

            // 2. Guardar en Base de Datos de manera PERMANENTE
            try
            {
                if (Guid.TryParse(pedidoId, out var pId))
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContex>();

                    var entidad = new MensajeChat
                    {
                        Id = Guid.NewGuid(),
                        PedidoId = pId,
                        Remitente = remitente,
                        Mensaje = mensaje.Trim(),
                        Fecha = ahora
                    };

                    db.MensajesChat.Add(entidad);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error guardando mensaje en base de datos: {ex.Message}");
            }

            // 3. Notificar en tiempo real a los suscritos autorizados
            await Clients.Group(pedidoId).SendAsync("RecibirMensajeChat", pedidoId, remitente, mensaje, horaTexto);
        }
    }
}
