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
                Password = BCrypt.Net.BCrypt.HashPassword("Admin1234*", workFactor: 10),
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
                Password = BCrypt.Net.BCrypt.HashPassword("Recepcion1234*", workFactor: 10),
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
                Password = BCrypt.Net.BCrypt.HashPassword("Repartidor1234*", workFactor: 10),
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
                Password = BCrypt.Net.BCrypt.HashPassword("Cliente1234*", workFactor: 10),
                Rol = "Cliente",
                Estado = true,
                Telefono = "3004445566"
            });
        }

        context.SaveChanges();
    }

    public static void SeedCatalog(AppDbContex context)
    {
        if (!context.Categorias.Any())
        {
            var catMedicamentos = new Categoria
            {
                Id = Guid.NewGuid(),
                Nombre = "Medicamentos y Alivio",
                Descripcion = "Analgésicos, antibióticos, antihistamínicos y medicamentos esenciales",
                Estado = true,
                FechaCreacion = DateTime.Now
            };
            var catCuidado = new Categoria
            {
                Id = Guid.NewGuid(),
                Nombre = "Cuidado Personal",
                Descripcion = "Higiene, cuidado de la piel y bienestar corporal",
                Estado = true,
                FechaCreacion = DateTime.Now
            };
            var catVitaminas = new Categoria
            {
                Id = Guid.NewGuid(),
                Nombre = "Vitaminas y Suplementos",
                Descripcion = "Multivitamínicos, suplementos alimenticios y refuerzo inmune",
                Estado = true,
                FechaCreacion = DateTime.Now
            };
            var catPrimerosAuxilios = new Categoria
            {
                Id = Guid.NewGuid(),
                Nombre = "Primeros Auxilios",
                Descripcion = "Gasas, alcohol, vendas y antisépticos de emergencia",
                Estado = true,
                FechaCreacion = DateTime.Now
            };

            context.Categorias.AddRange(catMedicamentos, catCuidado, catVitaminas, catPrimerosAuxilios);
            context.SaveChanges();

            // Productos iniciales para que el catálogo tenga variedad inmediata
            // Solo medicamentos con control o prescripción prudente tienen límite por pedido
            var productos = new List<Producto>
            {
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Acetaminofén 500mg",
                    Descripcion = "Alivia dolores leves a moderados y reduce la fiebre de forma eficaz y segura.",
                    Miligramos = 500,
                    UnidadMedida = "mg",
                    Precio = 3500m,
                    CategoriaId = catMedicamentos.Id,
                    Stock = 100,
                    StockMinimo = 10,
                    LimiteMaximoPorPedido = null, // Sin límite restrictivo
                    Estado = true,
                    FechaCreacion = DateTime.Now
                },
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Ibuprofeno 400mg",
                    Descripcion = "Antiinflamatorio no esteroideo para aliviar el dolor muscular, dental y de cabeza.",
                    Miligramos = 400,
                    UnidadMedida = "mg",
                    Precio = 5500m,
                    CategoriaId = catMedicamentos.Id,
                    Stock = 80,
                    StockMinimo = 10,
                    LimiteMaximoPorPedido = 4, // Límite de 4 unidades por pedido
                    Estado = true,
                    FechaCreacion = DateTime.Now
                },
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Amoxicilina 500mg",
                    Descripcion = "Antibiótico bactericida de amplio espectro para infecciones respiratorias y bacterianas.",
                    Miligramos = 500,
                    UnidadMedida = "mg",
                    Precio = 14500m,
                    CategoriaId = catMedicamentos.Id,
                    Stock = 50,
                    StockMinimo = 5,
                    LimiteMaximoPorPedido = 3, // Límite de 3 unidades por pedido
                    Estado = true,
                    FechaCreacion = DateTime.Now
                },
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Loratadina 10mg",
                    Descripcion = "Antihistamínico de segunda generación para alivio rápido de alergias y rinitis.",
                    Miligramos = 10,
                    UnidadMedida = "mg",
                    Precio = 6200m,
                    CategoriaId = catMedicamentos.Id,
                    Stock = 60,
                    StockMinimo = 8,
                    LimiteMaximoPorPedido = 5, // Límite de 5 unidades por pedido
                    Estado = true,
                    FechaCreacion = DateTime.Now
                },
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Omeprazol 20mg",
                    Descripcion = "Inhibidor de la bomba de protones para el tratamiento de acidez gástrica y reflujo.",
                    Miligramos = 20,
                    UnidadMedida = "mg",
                    Precio = 8000m,
                    CategoriaId = catMedicamentos.Id,
                    Stock = 70,
                    StockMinimo = 10,
                    LimiteMaximoPorPedido = 4, // Límite de 4 unidades por pedido
                    Estado = true,
                    FechaCreacion = DateTime.Now
                },
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Vitamina C 1000mg Efervescente",
                    Descripcion = "Suplemento antioxidante que fortalece el sistema inmunológico con agradable sabor cítrico.",
                    Miligramos = 1000,
                    UnidadMedida = "mg",
                    Precio = 18500m,
                    CategoriaId = catVitaminas.Id,
                    Stock = 45,
                    StockMinimo = 5,
                    LimiteMaximoPorPedido = null, // Libre
                    Estado = true,
                    FechaCreacion = DateTime.Now
                },
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Multivitamínico Completo + Zinc",
                    Descripcion = "Combinación balanceada de vitaminas del complejo B, vitamina D y Zinc para vitalidad diaria.",
                    Miligramos = 500,
                    UnidadMedida = "mg",
                    Precio = 28000m,
                    CategoriaId = catVitaminas.Id,
                    Stock = 35,
                    StockMinimo = 5,
                    LimiteMaximoPorPedido = null, // Libre
                    Estado = true,
                    FechaCreacion = DateTime.Now
                },
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Alcohol Antiséptico 70% 300ml",
                    Descripcion = "Solución antiséptica tópica para desinfección de manos, heridas superficiales y superficies.",
                    Miligramos = 300,
                    UnidadMedida = "ml",
                    Precio = 4800m,
                    CategoriaId = catPrimerosAuxilios.Id,
                    Stock = 90,
                    StockMinimo = 15,
                    LimiteMaximoPorPedido = null, // Libre
                    Estado = true,
                    FechaCreacion = DateTime.Now
                },
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Suero Oral Rehidratante 500ml",
                    Descripcion = "Solución balanceada de electrolitos para rehidratación rápida en estados de desgaste o deshidratación.",
                    Miligramos = 500,
                    UnidadMedida = "ml",
                    Precio = 4200m,
                    CategoriaId = catPrimerosAuxilios.Id,
                    Stock = 75,
                    StockMinimo = 10,
                    LimiteMaximoPorPedido = null, // Libre
                    Estado = true,
                    FechaCreacion = DateTime.Now
                },
                new Producto
                {
                    Id = Guid.NewGuid(),
                    Nombre = "Gel Antibacterial con Aloe 250ml",
                    Descripcion = "Elimina el 99.9% de gérmenes y bacterias sin resecar las manos gracias al aloe vera.",
                    Miligramos = 250,
                    UnidadMedida = "ml",
                    Precio = 6500m,
                    CategoriaId = catCuidado.Id,
                    Stock = 60,
                    StockMinimo = 10,
                    LimiteMaximoPorPedido = null, // Libre
                    Estado = true,
                    FechaCreacion = DateTime.Now
                }
            };

            context.Productos.AddRange(productos);
            context.SaveChanges();
        }
    }
}
