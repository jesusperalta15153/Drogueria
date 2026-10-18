using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Drogueria.Models;

public class MensajeChat
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PedidoId { get; set; }

    [ForeignKey(nameof(PedidoId))]
    public Pedido? Pedido { get; set; }

    [Required]
    [StringLength(150)]
    public string Remitente { get; set; } = "";

    [Required]
    public string Mensaje { get; set; } = "";

    public DateTime Fecha { get; set; } = DateTime.Now;
}
