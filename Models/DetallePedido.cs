using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Drogueria.Models;

public class DetallePedido
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid PedidoId { get; set; }

    [ForeignKey(nameof(PedidoId))]
    public Pedido? Pedido { get; set; }

    [Required]
    public Guid ProductoId { get; set; }

    [ForeignKey(nameof(ProductoId))]
    public Producto? Producto { get; set; }

    [Range(1, int.MaxValue)]
    public int Cantidad { get; set; } = 1;

    [Column(TypeName = "decimal(10,2)")]
    public decimal PrecioUnitario { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Subtotal { get; set; }
}
