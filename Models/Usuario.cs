using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Drogueria.Models;

public class Usuario
{
    [Key]
    [StringLength(36)]
    public string Uuid { get; set; } = Guid.NewGuid().ToString();

    [Required(ErrorMessage = "El nombre es obligatorio")]
    [StringLength(100, ErrorMessage = "El nombre no puede superar 100 caracteres")]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "El apellido es obligatorio")]
    [StringLength(100, ErrorMessage = "El apellido no puede superar 100 caracteres")]
    public string Apellido { get; set; } = "";

    [Required(ErrorMessage = "El correo electrónico es obligatorio")]
    [EmailAddress(ErrorMessage = "El correo electrónico no es válido")]
    [RegularExpression(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", ErrorMessage = "El correo electrónico debe ser válido, contener '@', un dominio y una extensión como .com o .co")]
    [DataType(DataType.EmailAddress)]
    [StringLength(150, ErrorMessage = "El correo no puede superar 150 caracteres")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "La contraseña es obligatoria")]
    [DataType(DataType.Password)]
    [StringLength(25, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre 8 y 25 caracteres")]
    public string Password { get; set; } = "";

    public bool Estado { get; set; } = true;

    [Required(ErrorMessage = "El rol es obligatorio")]
    [StringLength(50)]
    public string Rol { get; set; } = "Cliente"; // Administrador, Empleado, Repartidor, Cliente

    [StringLength(20, ErrorMessage = "El teléfono no puede superar 20 caracteres")]
    public string? Telefono { get; set; }

    /// <summary>
    /// Tipo de vehículo usado por el repartidor (ej: Moto, Bicicleta, Carro).
    /// </summary>
    [StringLength(50)]
    public string? TipoVehiculo { get; set; }

    /// <summary>
    /// Placa del vehículo si aplica (ej: Moto o Carro).
    /// </summary>
    [StringLength(20)]
    public string? Placa { get; set; }

    /// <summary>
    /// Indica si el repartidor está en línea/activo para recibir pedidos (true) o descansando/inactivo (false).
    /// </summary>
    public bool EstaEnLinea { get; set; } = true;

    public ICollection<Pedido> Pedidos { get; set; } = new List<Pedido>();
    public ICollection<Pedido> PedidosAsignados { get; set; } = new List<Pedido>();
    public ICollection<DireccionUsuario> Direcciones { get; set; } = new List<DireccionUsuario>();
}