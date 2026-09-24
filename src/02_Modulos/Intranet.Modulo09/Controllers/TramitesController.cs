using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Intranet.Core.Controllers;
using Intranet.Modulo09.Models;
using Intranet.Modulo09.Services;

namespace Intranet.Modulo09.Controllers;

/// <summary>
/// Trámites TUPA del Módulo 09 (Equipo 09).
/// Vistas duales según rol (patrón del prototipo WebForms):
///   Alumno    → "Mis trámites" + nuevo trámite con requisitos dinámicos
///   Secretaría → "Mesa de trámites" (seguimiento y avance del flujo)
/// </summary>
[Route("Modulo09/[controller]")]
public class TramitesController : ModuloBaseController
{
    private readonly ITramiteService _tramiteService;

    public TramitesController(ITramiteService tramiteService)
    {
        _tramiteService = tramiteService;
    }

    // ------------------------------------------------------------------
    // Mis trámites (alumno) / Mesa de trámites (personal)
    // ------------------------------------------------------------------
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(string? estado)
    {
        ViewData["Title"] = "09. Trámites TUPA";
        ViewData["TeamName"] = "Equipo 09";
        ViewData["UsuarioNombre"] = UsuarioActualNombre;
        ViewData["UsuarioRol"] = UsuarioActualRol;

        // el alumno es un estudiante: obtener su id vía persona → estudiantes
        if (EsAlumno && !EsAdmin)
        {
            var estudianteId = await ObtenerEstudianteIdAsync();
            var mios = await _tramiteService.ListarPorEstudianteAsync(estudianteId, estado);
            return View("Mis", new MisTramitesViewModel
            {
                Tramites = mios,
                Tipos = await _tramiteService.ListarTiposAsync(),
                FiltroEstado = estado
            });
        }

        var mesa = await _tramiteService.ListarMesaAsync(estado);
        return View("Mesa", new MesaTramitesViewModel
        {
            Tramites = mesa,
            FiltroEstado = estado,
            Pendientes = await _tramiteService.ContarPendientesAsync()
        });
    }

    // ------------------------------------------------------------------
    // Requisitos dinámicos por tipo (el AutoPostBack del prototipo):
    // devuelve JSON para el select del formulario de nuevo trámite.
    // ------------------------------------------------------------------
    [HttpGet("Requisitos/{tipoCodigo}")]
    public async Task<IActionResult> Requisitos(string tipoCodigo)
    {
        var tipo = await _tramiteService.ObtenerTipoAsync(tipoCodigo);
        if (tipo == null) return NotFound();
        var requisitos = await _tramiteService.ListarRequisitosDeTipoAsync(tipoCodigo);
        return Json(new { tipo, requisitos });
    }

    // ------------------------------------------------------------------
    // Ficha de un trámite
    // ------------------------------------------------------------------
    [HttpGet("Detalle/{id}")]
    public async Task<IActionResult> Detalle(int id)
    {
        var t = await _tramiteService.ObtenerDetalleAsync(id);
        if (t == null) return NotFound();
        return View(t);
    }

    // ------------------------------------------------------------------
    // ALUMNO: crear trámite (los archivos se adjuntan en el form;
    // en esta fase se registran los requisitos como presentados)
    // ------------------------------------------------------------------
    [HttpPost("Crear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(string tipoTramite, string? observaciones)
    {
        if (EsAlumno && !EsAdmin)
        {
            var estudianteId = await ObtenerEstudianteIdAsync();
            var periodoId = await ObtenerPeriodoActivoIdAsync();

            string? datos = null;
            if (!string.IsNullOrWhiteSpace(observaciones))
                datos = JsonSerializer.Serialize(new { observaciones });

            var (ok, mensaje, codigo) = await _tramiteService.CrearTramiteAsync(
                estudianteId, periodoId, tipoTramite, datos);

            if (ok) MostrarAlertaExito(mensaje);
            else MostrarAlertaError(mensaje);
        }
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------
    // ALUMNO: corregir requisito observado (sin crear trámite nuevo)
    // ------------------------------------------------------------------
    [HttpPost("Corregir/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Corregir(int id, int requisitoId, string? nota)
    {
        var (ok, mensaje) = await _tramiteService.CorregirRequisitoAsync(id, requisitoId, nota ?? "Corregido por el estudiante");
        if (ok) MostrarAlertaExito(mensaje);
        else MostrarAlertaError(mensaje);
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------
    // MESA (Secretaría): avanzar el flujo del trámite
    // ------------------------------------------------------------------
    [HttpPost("Avanzar/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Avanzar(int id, string nuevoEstado, string? resolucion)
    {
        if (!EsSecretaria && !EsAdmin)
            return Forbid();

        var (ok, mensaje) = await _tramiteService.AvanzarEstadoAsync(
            id, nuevoEstado, resolucion, UsuarioActualId ?? 0);
        if (ok) MostrarAlertaExito(mensaje);
        else MostrarAlertaError(mensaje);
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------
    // Helpers de contexto
    // ------------------------------------------------------------------
    private async Task<int> ObtenerEstudianteIdAsync()
    {
        // claim PersonaId → core.estudiantes (vía la connection factory del módulo 09)
        var factory = HttpContext.RequestServices.GetRequiredService<Intranet.Core.Contracts.IModuleDbConnectionFactory>();
        using var conn = factory.CreateConnection("09");
        var id = await conn.ExecuteScalarAsync<int?>(
            "SELECT e.id FROM core.estudiantes e WHERE e.persona_id = @PersonaId;",
            new { PersonaId = PersonaActualId ?? 0 });
        return id ?? 0;
    }

    private async Task<int> ObtenerPeriodoActivoIdAsync()
    {
        var factory = HttpContext.RequestServices.GetRequiredService<Intranet.Core.Contracts.IModuleDbConnectionFactory>();
        using var conn = factory.CreateConnection("09");
        return await conn.ExecuteScalarAsync<int>(
            "SELECT id FROM core.periodos_academicos WHERE es_activo ORDER BY id DESC LIMIT 1;");
    }
}

// ---------------------------------------------------------------------
// ViewModels
// ---------------------------------------------------------------------
public class MisTramitesViewModel
{
    public IEnumerable<TramiteListaDto> Tramites { get; set; } = [];
    public IEnumerable<TipoTramiteDto> Tipos { get; set; } = [];
    public string? FiltroEstado { get; set; }
}

public class MesaTramitesViewModel
{
    public IEnumerable<TramiteMesaDto> Tramites { get; set; } = [];
    public string? FiltroEstado { get; set; }
    public int Pendientes { get; set; }
}
