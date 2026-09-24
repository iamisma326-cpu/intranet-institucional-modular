using Microsoft.AspNetCore.Mvc;
using Intranet.Core.Controllers;

namespace Intranet.Modulo09.Controllers;

/// <summary>
/// Entrada del Módulo 09. El botón "09. Tesorería" del menú global
/// aterriza aquí y redirige a la pantalla real del equipo:
///   Alumno    → Mis Trámites TUPA
///   Personal   → Mesa de trámites
/// </summary>
[Route("Modulo09")]
public class Modulo09Controller : ModuloBaseController
{
    [HttpGet("")]
    [HttpGet("Index")]
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Tramites");
    }
}
