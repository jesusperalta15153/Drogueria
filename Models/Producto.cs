using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Drogueria.Models;

public class Producto
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "El nombre es obligatorio")]
    [StringLength(100, ErrorMessage = "El nombre no puede superar 100 caracteres")]
    public string Nombre { get; set; } = "";

    [StringLength(2000, ErrorMessage = "La descripción no puede superar 2000 caracteres")]
    public string? Descripcion { get; set; }

    [Required(ErrorMessage = "La dosis o concentración es obligatoria")]
    [Range(1, 10000, ErrorMessage = "La dosis debe estar entre 1 y 10,000")]
    public int Miligramos { get; set; }

    [Required(ErrorMessage = "La unidad de medida es obligatoria")]
    [StringLength(20, ErrorMessage = "La unidad de medida no puede superar 20 caracteres")]
    public string UnidadMedida { get; set; } = "mg";

    [Required(ErrorMessage = "El precio es obligatorio")]
    [Column(TypeName = "decimal(10,2)")]
    [Range(0.01, 500000.00, ErrorMessage = "El precio debe estar entre $0.01 y $500,000.00")]
    public decimal Precio { get; set; }

    // FK → Categoria
    [Required(ErrorMessage = "La categoría es obligatoria")]
    public Guid CategoriaId { get; set; }

    [ForeignKey(nameof(CategoriaId))]
    public Categoria? Categoria { get; set; }

    [Required(ErrorMessage = "El stock es obligatorio")]
    [Range(0, 50000, ErrorMessage = "El stock debe estar entre 0 y 50,000 unidades")]
    public int Stock { get; set; } = 0;

    [Required(ErrorMessage = "El límite de stock para alerta es obligatorio")]
    [Range(0, 5000, ErrorMessage = "El stock mínimo de alerta debe estar entre 0 y 5,000 unidades")]
    public int StockMinimo { get; set; } = 5;

    [Range(1, 100, ErrorMessage = "El límite por pedido debe estar entre 1 y 100 unidades por cliente")]
    public int? LimiteMaximoPorPedido { get; set; }

    // Ruta relativa de la imagen: "uploads/productos/filename.jpg"
    [StringLength(300)]
    public string? Imagen { get; set; }

    public bool Estado { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.Now;
}
