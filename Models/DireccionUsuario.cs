using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Drogueria.Models;

public class DireccionUsuario
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(36)]
    public string UsuarioId { get; set; } = "";

    [ForeignKey(nameof(UsuarioId))]
    public Usuario? Usuario { get; set; }

    [Required(ErrorMessage = "El nombre de contacto es obligatorio")]
    [StringLength(50, ErrorMessage = "El nombre de contacto no puede superar 50 caracteres")]
    public string NombreContacto { get; set; } = "";

    [Required(ErrorMessage = "El teléfono es obligatorio")]
    [StringLength(10, ErrorMessage = "El teléfono debe tener máximo 10 dígitos")]
    [RegularExpression(@"^\d{1,10}$", ErrorMessage = "El teléfono debe contener solo dígitos y máximo 10 caracteres")]
    public string Telefono { get; set; } = "";

    [Required(ErrorMessage = "La dirección es obligatoria")]
    [StringLength(150, ErrorMessage = "La dirección no puede superar 150 caracteres")]
    public string Direccion { get; set; } = "";

    [Required(ErrorMessage = "La ciudad es obligatoria")]
    [StringLength(50, ErrorMessage = "La ciudad no puede superar 50 caracteres")]
    public string Ciudad { get; set; } = "";

    [Required(ErrorMessage = "El departamento es obligatorio")]
    [StringLength(50, ErrorMessage = "El departamento no puede superar 50 caracteres")]
    public string Departamento { get; set; } = "";

    public bool EsPredeterminada { get; set; } = false;

    public DateTime FechaCreacion { get; set; } = DateTime.Now;
}
