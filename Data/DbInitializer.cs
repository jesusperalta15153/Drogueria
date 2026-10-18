using Drogueria.Models;
using Microsoft.EntityFrameworkCore;

namespace Drogueria.Data;

public static class DbInitializer
{
    public static void SeedUsers(AppDbContex context)
    {
        // 1. Administrador
        if (!context.Usuarios.Any(u => u.Email == "admin@drogueria.com"))
        {
            context.Usuarios.Add(new Usuario
            {
                Uuid = Guid.NewGuid().ToString(),
                Nombre = "Administrador",
                Apellido = "Principal",
                Email = "admin@drogueria.com",
                Password = BCrypt.Net.BCrypt.HashPassword("Admin1234*"),
                Rol = "Administrador",
                Estado = true,
                Telefono = "3001112233"
            });
        }

        // 2. Recepcionista / Empleado
        if (!context.Usuarios.Any(u => u.Email == "recepcionista@drogueria.com"))
        {
            context.Usuarios.Add(new Usuario
            {
                Uuid = Guid.NewGuid().ToString(),
                Nombre = "Recepcionista",
                Apellido = "Drogueria",
                Email = "recepcionista@drogueria.com",
                Password = BCrypt.Net.BCrypt.HashPassword("Recepcion1234*"),
                Rol = "Empleado",
                Estado = true,
                Telefono = "3002223344"
            });
        }

        // 3. Repartidor
        if (!context.Usuarios.Any(u => u.Email == "repartidor@drogueria.com"))
        {
            context.Usuarios.Add(new Usuario
            {
                Uuid = Guid.NewGuid().ToString(),
                Nombre = "Repartidor",
                Apellido = "Express",
                Email = "repartidor@drogueria.com",
                Password = BCrypt.Net.BCrypt.HashPassword("Repartidor1234*"),
                Rol = "Repartidor",
                Estado = true,
                Telefono = "3003334455",
                TipoVehiculo = "Moto",
                Placa = "ABC-12D",
                EstaEnLinea = true
            });
        }

        // 4. Cliente
        if (!context.Usuarios.Any(u => u.Email == "cliente@drogueria.com"))
        {
            context.Usuarios.Add(new Usuario
            {
                Uuid = Guid.NewGuid().ToString(),
                Nombre = "Cliente",
                Apellido = "Frecuente",
                Email = "cliente@drogueria.com",
                Password = BCrypt.Net.BCrypt.HashPassword("Cliente1234*"),
                Rol = "Cliente",
                Estado = true,
                Telefono = "3004445566"
            });
        }

        context.SaveChanges();
    }
}
