using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Drogueria.Data;
using Drogueria.Models;

namespace Drogueria.Controllers
{
    [Authorize(Roles = "Administrador,Empleado")]
    public class ProductoController : Controller
    {
        private readonly AppDbContex _context;
        private readonly IWebHostEnvironment _env;

        private const string CarpetaImagenes = "uploads/productos";

        public ProductoController(AppDbContex context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // GET: /Producto
        public async Task<IActionResult> Index()
        {
            var productos = await _context.Productos
                .Include(p => p.Categoria)
                .OrderByDescending(p => p.FechaCreacion)
                .ToListAsync();

            ViewBag.Categorias = new SelectList(
                await _context.Categorias.Where(c => c.Estado).OrderBy(c => c.Nombre).ToListAsync(),
                "Id", "Nombre");

            return View("~/Views/Home/pages/_Productos.cshtml", productos);
        }

        // POST: /Producto/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("Nombre,Descripcion,Miligramos,UnidadMedida,Precio,CategoriaId,Stock,StockMinimo,LimiteMaximoPorPedido,Estado")] Producto producto,
            IFormFile? imagenFile)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            if (ModelState.IsValid)
            {
                producto.Id = Guid.NewGuid();
                producto.FechaCreacion = DateTime.Now;
                producto.Imagen = await GuardarImagenAsync(imagenFile);

                _context.Productos.Add(producto);
                await _context.SaveChangesAsync();

                var successMsg = $"Producto '{producto.Nombre}' creado correctamente.";
                if (isAjax)
                {
                    return Json(new {
                        success = true,
                        message = successMsg,
                        producto = new {
                            id = producto.Id,
                            nombre = producto.Nombre,
                            descripcion = producto.Descripcion,
                            miligramos = producto.Miligramos,
                            unidadMedida = producto.UnidadMedida,
                            precio = producto.Precio,
                            stock = producto.Stock,
                            stockMinimo = producto.StockMinimo,
                            limiteMaximoPorPedido = producto.LimiteMaximoPorPedido,
                            estado = producto.Estado,
                            imagen = producto.Imagen,
                            categoriaId = producto.CategoriaId
                        }
                    });
                }

                TempData["Exito"] = successMsg;
                return RedirectToAction(nameof(Index));
            }

            var primerError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage;
            var errorMsg = !string.IsNullOrEmpty(primerError) ? primerError : "Error al crear el producto. Verifique los datos.";
            if (isAjax) return Json(new { success = false, message = errorMsg });

            TempData["Error"] = errorMsg;
            return await CargarVistaConError();
        }

        // POST: /Producto/Edit/guid
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            Guid id,
            [Bind("Id,Nombre,Descripcion,Miligramos,UnidadMedida,Precio,CategoriaId,Stock,StockMinimo,LimiteMaximoPorPedido,Estado,FechaCreacion,Imagen")] Producto producto,
            IFormFile? imagenFile)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            if (id != producto.Id)
            {
                if (isAjax) return Json(new { success = false, message = "Producto no encontrado." });
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Si se subió nueva imagen, reemplazar la anterior
                    if (imagenFile != null && imagenFile.Length > 0)
                    {
                        EliminarImagenAnterior(producto.Imagen);
                        producto.Imagen = await GuardarImagenAsync(imagenFile);
                    }

                    _context.Productos.Update(producto);
                    await _context.SaveChangesAsync();
                    var successMsg = $"Producto '{producto.Nombre}' actualizado correctamente.";

                    if (isAjax)
                    {
                        return Json(new {
                            success = true,
                            message = successMsg,
                            producto = new {
                                id = producto.Id,
                                nombre = producto.Nombre,
                                descripcion = producto.Descripcion,
                                miligramos = producto.Miligramos,
                                unidadMedida = producto.UnidadMedida,
                                precio = producto.Precio,
                                stock = producto.Stock,
                                stockMinimo = producto.StockMinimo,
                                limiteMaximoPorPedido = producto.LimiteMaximoPorPedido,
                                estado = producto.Estado,
                                imagen = producto.Imagen,
                                categoriaId = producto.CategoriaId
                            }
                        });
                    }

                    TempData["Exito"] = successMsg;
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Productos.Any(p => p.Id == id))
                    {
                        if (isAjax) return Json(new { success = false, message = "El producto no existe." });
                        return NotFound();
                    }
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }

            var primerEditError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage;
            var editErrorMsg = !string.IsNullOrEmpty(primerEditError) ? primerEditError : "Error al actualizar el producto.";
            if (isAjax) return Json(new { success = false, message = editErrorMsg });

            TempData["Error"] = editErrorMsg;
            return await CargarVistaConError();
        }

        // POST: /Producto/Delete/guid  — soft delete (desactivar)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var producto = await _context.Productos.FindAsync(id);
            if (producto != null)
            {
                producto.Estado = false;
                _context.Productos.Update(producto);
                await _context.SaveChangesAsync();
                var successMsg = $"Producto '{producto.Nombre}' desactivado correctamente.";
                if (isAjax) return Json(new { success = true, message = successMsg, id = producto.Id, estado = false });
                TempData["Exito"] = successMsg;
            }
            else
            {
                var errorMsg = "El producto no fue encontrado.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Producto/ToggleEstado/guid
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleEstado(Guid id)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null)
            {
                if (isAjax) return Json(new { success = false, message = "Producto no encontrado." });
                return NotFound();
            }

            producto.Estado = !producto.Estado;
            _context.Productos.Update(producto);
            await _context.SaveChangesAsync();

            var accion = producto.Estado ? "activado" : "desactivado";
            var successMsg = $"Producto '{producto.Nombre}' {accion} correctamente.";

            if (isAjax)
            {
                return Json(new { success = true, message = successMsg, id = producto.Id, estado = producto.Estado });
            }

            TempData["Exito"] = successMsg;
            return RedirectToAction(nameof(Index));
        }

        // ── Helpers ──────────────────────────────────────────

        private async Task<string?> GuardarImagenAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0) return null;

            var extensionesPermitidas = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!extensionesPermitidas.Contains(ext)) return null;

            var carpeta = Path.Combine(_env.WebRootPath, CarpetaImagenes);
            Directory.CreateDirectory(carpeta);

            var nombreArchivo = $"{Guid.NewGuid()}{ext}";
            var rutaCompleta = Path.Combine(carpeta, nombreArchivo);

            using var stream = new FileStream(rutaCompleta, FileMode.Create);
            await file.CopyToAsync(stream);

            return $"{CarpetaImagenes}/{nombreArchivo}";
        }

        private void EliminarImagenAnterior(string? rutaRelativa)
        {
            if (string.IsNullOrEmpty(rutaRelativa)) return;
            var rutaCompleta = Path.Combine(_env.WebRootPath, rutaRelativa.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(rutaCompleta))
                System.IO.File.Delete(rutaCompleta);
        }

        private async Task<IActionResult> CargarVistaConError()
        {
            ViewBag.Categorias = new SelectList(
                await _context.Categorias.Where(c => c.Estado).OrderBy(c => c.Nombre).ToListAsync(),
                "Id", "Nombre");

            var productos = await _context.Productos
                .Include(p => p.Categoria)
                .OrderByDescending(p => p.FechaCreacion)
                .ToListAsync();

            return View("~/Views/Home/pages/_Productos.cshtml", productos);
        }
    }
}
