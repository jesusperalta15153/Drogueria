using System.ComponentModel.DataAnnotations;

namespace Drogueria.Models;

public class Categoria
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "El nombre es obligatorio")]
    [StringLength(50, ErrorMessage = "El nombre no puede superar 50 caracteres")]
    public string Nombre { get; set; } = "";

    [StringLength(255, ErrorMessage = "La descripción no puede superar 255 caracteres")]
    public string? Descripcion { get; set; }

    public bool Estado { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.Now;
}
