using System.Diagnostics;
using Drogueria.Data;
using Drogueria.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drogueria.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContex _context;

        public HomeController(AppDbContex context)
        {
            _context = context;
        }

        // GET: / o /Home o /Home/Index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Traer hasta 8 productos activos con stock para la vitrina pública
            List<Producto> productos = new();
            try
            {
                if (_context?.Productos != null)
                {
                    productos = await _context.Productos
                        .Include(p => p.Categoria)
                        .Where(p => p.Estado && p.Stock > 0)
                        .OrderByDescending(p => p.FechaCreacion)
                        .Take(8)
                        .ToListAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al consultar productos de vitrina: {ex.Message}");
                productos = new List<Producto>();
            }

            ViewBag.ProductosVitrina = productos;
            return View("~/Views/Home/Pagina_Inicio/Index.cshtml");
        }

        // GET: /Home/Pagina_Inicio (alias)
        [HttpGet]
        public async Task<IActionResult> Pagina_Inicio()
        {
            List<Producto> productos = new();
            try
            {
                if (_context?.Productos != null)
                {
                    productos = await _context.Productos
                        .Include(p => p.Categoria)
                        .Where(p => p.Estado && p.Stock > 0)
                        .OrderByDescending(p => p.FechaCreacion)
                        .Take(8)
                        .ToListAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al consultar productos de vitrina: {ex.Message}");
                productos = new List<Producto>();
            }

            ViewBag.ProductosVitrina = productos;
            return View("~/Views/Home/Pagina_Inicio/Index.cshtml");
        }

        [HttpGet]
        public IActionResult Nosotros()
        {
            return View("~/Views/Home/Pagina_Inicio/_Nosotros.cshtml");
        }

        [HttpGet]
        public IActionResult Trabaja()
        {
            return View("~/Views/Home/Pagina_Inicio/Trabaja.cshtml");
        }

        // Manejador centralizado de errores de estado HTTP (404, 403, 500, etc.)
        [HttpGet]
        [Route("/Home/ErrorStatus")]
        public IActionResult ErrorStatus(int? code)
        {
            if (code == 404)
            {
                TempData["Error"] = "La página a la que intentaste acceder no existe o fue movida.";
            }
            else if (code == 403)
            {
                TempData["Error"] = "Acceso denegado: no cuentas con los permisos requeridos.";
            }
            else
            {
                TempData["Error"] = "Ocurrió un error al procesar tu solicitud.";
            }

            // Si es un cliente autenticado, llevarlo a su catálogo
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Cliente"))
            {
                return RedirectToAction("Catalogo", "Tienda");
            }

            // Si es administrador autenticado, llevarlo a su dashboard
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Administrador"))
            {
                return RedirectToAction("Dashboard", "Usuario");
            }

            // Si es empleado autenticado, llevarlo a su panel
            if (User.Identity?.IsAuthenticated == true && User.IsInRole("Empleado"))
            {
                return RedirectToAction("Productos", "Empleado");
            }

            // Para invitados o público general, a la página de inicio
            return RedirectToAction("Index", "Home");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
