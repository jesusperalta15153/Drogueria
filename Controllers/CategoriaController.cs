using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Drogueria.Data;
using Drogueria.Models;

namespace Drogueria.Controllers
{
    [Authorize(Roles = "Administrador,Empleado")]
    public class CategoriaController : Controller
    {
        private readonly AppDbContex _context;

        public CategoriaController(AppDbContex context)
        {
            _context = context;
        }

        // GET: /Categoria
        public async Task<IActionResult> Index()
        {
            var categorias = await _context.Categorias
                .OrderByDescending(c => c.FechaCreacion)
                .ToListAsync();

            return View("~/Views/Home/pages/_Categorias.cshtml", categorias);
        }

        // POST: /Categoria/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Nombre,Descripcion,Estado")] Categoria categoria)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            if (ModelState.IsValid)
            {
                categoria.Id = Guid.NewGuid();
                categoria.FechaCreacion = DateTime.Now;
                _context.Categorias.Add(categoria);
                await _context.SaveChangesAsync();

                var successMsg = "Categoría creada correctamente.";
                if (isAjax)
                {
                    return Json(new {
                        success = true,
                        message = successMsg,
                        categoria = new {
                            id = categoria.Id,
                            nombre = categoria.Nombre,
                            descripcion = categoria.Descripcion,
                            estado = categoria.Estado
                        }
                    });
                }

                TempData["Exito"] = successMsg;
                return RedirectToAction(nameof(Index));
            }

            var errorMsg = "Error al crear la categoría. Verifica los campos.";
            if (isAjax) return Json(new { success = false, message = errorMsg });

            TempData["Error"] = errorMsg;
            var categorias = await _context.Categorias.OrderByDescending(c => c.FechaCreacion).ToListAsync();
            return View("~/Views/Home/pages/_Categorias.cshtml", categorias);
        }

        // POST: /Categoria/Edit/guid
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, [Bind("Id,Nombre,Descripcion,Estado,FechaCreacion")] Categoria categoria)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            if (id != categoria.Id)
            {
                if (isAjax) return Json(new { success = false, message = "Categoría no encontrada." });
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Categorias.Update(categoria);
                    await _context.SaveChangesAsync();
                    var successMsg = "Categoría actualizada correctamente.";

                    if (isAjax)
                    {
                        return Json(new {
                            success = true,
                            message = successMsg,
                            categoria = new {
                                id = categoria.Id,
                                nombre = categoria.Nombre,
                                descripcion = categoria.Descripcion,
                                estado = categoria.Estado
                            }
                        });
                    }

                    TempData["Exito"] = successMsg;
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Categorias.Any(c => c.Id == id))
                    {
                        if (isAjax) return Json(new { success = false, message = "La categoría no existe." });
                        return NotFound();
                    }
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }

            var editErrorMsg = "Error al actualizar la categoría.";
            if (isAjax) return Json(new { success = false, message = editErrorMsg });

            TempData["Error"] = editErrorMsg;
            var categorias = await _context.Categorias.OrderByDescending(c => c.FechaCreacion).ToListAsync();
            return View("~/Views/Home/pages/_Categorias.cshtml", categorias);
        }

        // POST: /Categoria/Delete/guid  — soft delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var categoria = await _context.Categorias.FindAsync(id);
            if (categoria != null)
            {
                categoria.Estado = false;
                _context.Categorias.Update(categoria);
                await _context.SaveChangesAsync();
                var successMsg = $"La categoría '{categoria.Nombre}' fue desactivada correctamente.";
                if (isAjax) return Json(new { success = true, message = successMsg, id = categoria.Id, estado = false });
                TempData["Exito"] = successMsg;
            }
            else
            {
                var errorMsg = "La categoría no fue encontrada.";
                if (isAjax) return Json(new { success = false, message = errorMsg });
                TempData["Error"] = errorMsg;
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Categoria/ToggleEstado/guid  — activa o desactiva según estado actual
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleEstado(Guid id)
        {
            var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json");

            var categoria = await _context.Categorias.FindAsync(id);
            if (categoria == null)
            {
                if (isAjax) return Json(new { success = false, message = "Categoría no encontrada." });
                return NotFound();
            }

            categoria.Estado = !categoria.Estado;
            _context.Categorias.Update(categoria);
            await _context.SaveChangesAsync();

            var accion = categoria.Estado ? "activada" : "desactivada";
            var successMsg = $"La categoría '{categoria.Nombre}' fue {accion} correctamente.";

            if (isAjax)
            {
                return Json(new { success = true, message = successMsg, id = categoria.Id, estado = categoria.Estado });
            }

            TempData["Exito"] = successMsg;
            return RedirectToAction(nameof(Index));
        }
    }
}
