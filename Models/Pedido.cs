using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Drogueria.Models;

public class Pedido
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(36)]
    public string UsuarioId { get; set; } = "";

    [ForeignKey(nameof(UsuarioId))]
    public Usuario? Usuario { get; set; }

    public DateTime Fecha { get; set; } = DateTime.Now;

    [Column(TypeName = "decimal(10,2)")]
    public decimal Total { get; set; }

    [Required]
    [StringLength(50)]
    public string Estado { get; set; } = "Pendiente";

    [StringLength(36)]
    public string? RepartidorId { get; set; }

    [ForeignKey(nameof(RepartidorId))]
    public Usuario? Repartidor { get; set; }

    [StringLength(300)]
    public string? DireccionEntrega { get; set; }

    [StringLength(50)]
    public string? MetodoPago { get; set; }

    /// <summary>Código de confirmación: referencia Supergiros (fija por usuario) o código de entrega para contraentrega (cambia por pedido).</summary>
    [StringLength(20)]
    public string? CodigoConfirmacion { get; set; }

    /// <summary>Número de intentos fallidos al ingresar el código de entrega.</summary>
    public int IntentosCodigoEntrega { get; set; } = 0;


    public double? LatitudRepartidor { get; set; }
    public double? LongitudRepartidor { get; set; }
    public DateTime? UltimaUbicacionFecha { get; set; }

    public ICollection<DetallePedido> Detalles { get; set; } = new List<DetallePedido>();
}
