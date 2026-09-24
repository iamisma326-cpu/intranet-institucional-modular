using System.Data;
using Dapper;
using Intranet.Core.Contracts;
using Intranet.Modulo09.Models;

namespace Intranet.Modulo09.Services;

/// <summary>
/// Servicio de Trámites TUPA del Módulo 09 (Tesorería & Pagos).
/// Flujo del prototipo WebForms (Equipo 09):
///   Alumno crea trámite con TODOS los requisitos adjuntos (obligatorio)
///   → Mesa de partes (Secretaría) avanza: Recibido → En evaluación →
///     Aprobado (verificando pago) / Observado (motivo) / Rechazado → Entregado
///   → Corrección: el alumno re-adjunta SOLO el requisito observado
///     y el trámite vuelve a En evaluación (no se crea uno nuevo).
/// Dominios reales (CHECK en servidor): Recibido | En evaluación |
///   Aprobado | Observado | Rechazado | Entregado
/// </summary>
public interface ITramiteService
{
    Task<IEnumerable<TramiteListaDto>> ListarPorEstudianteAsync(int estudianteId, string? estado);
    Task<IEnumerable<TramiteMesaDto>> ListarMesaAsync(string? estado);
    Task<TramiteDetalleDto?> ObtenerDetalleAsync(int tramiteId);
    Task<TramiteDetalleDto?> ObtenerPorCodigoAsync(string codigo);
    Task<IEnumerable<TipoTramiteDto>> ListarTiposAsync();
    Task<TipoTramiteDto?> ObtenerTipoAsync(string codigo);
    Task<IEnumerable<RequisitoDto>> ListarRequisitosDeTipoAsync(string tipoCodigo);
    Task<(bool Ok, string Mensaje, string? Codigo)> CrearTramiteAsync(
        int estudianteId, int periodoId, string tipoCodigo, string? datos);
    Task<(bool Ok, string Mensaje)> CorregirRequisitoAsync(
        int tramiteId, int requisitoCatalogoId, string observacion);
    Task<(bool Ok, string Mensaje)> AvanzarEstadoAsync(
        int tramiteId, string nuevoEstado, string? resolucion, int usuarioId);
    Task<int> ContarPendientesAsync();
}

public class TramiteService : ITramiteService
{
    private readonly IModuleDbConnectionFactory _connectionFactory;

    public TramiteService(IModuleDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private IDbConnection CreateConnection() => _connectionFactory.CreateConnection("09");

    // número correlativo de trámite: T0001, T0002... según el diseño del prototipo
    private const string SqlNuevoCodigo = "SELECT 'T' || lpad((count(*) + 1)::text, 4, '0') FROM tramites;";

    // ------------------------------------------------------------------
    // ALUMNO: sus trámites con días transcurridos y estado de pago
    // ------------------------------------------------------------------
    public async Task<IEnumerable<TramiteListaDto>> ListarPorEstudianteAsync(int estudianteId, string? estado)
    {
        using var db = CreateConnection();
        const string sql = """
            SELECT t.id,
                   t.codigo,
                   t.estado,
                   t.fecha_solicitud AS FechaSolicitud,
                   t.fecha_limite AS FechaLimite,
                   CURRENT_DATE - t.fecha_solicitud AS Dias,
                   tt.codigo AS TipoCodigo,
                   tt.nombre AS TipoNombre,
                   cp.codigo AS PagoCodigo,
                   cp.nombre AS PagoNombre,
                   cp.monto AS PagoMonto,
                   (SELECT count(*) FROM tramite_requisitos tr
                     WHERE tr.tramite_id = t.id AND tr.presentado)  AS RequisitosOk,
                   (SELECT count(*) FROM tramite_requisitos tr
                     WHERE tr.tramite_id = t.id)                      AS RequisitosTotal
            FROM tramites t
            JOIN tipos_tramite tt ON tt.id = t.tipo_tramite_id
            JOIN conceptos_pago cp ON cp.id = tt.concepto_pago_id
            WHERE t.estudiante_id = @EstudianteId
              AND (@Estado IS NULL OR t.estado = @Estado)
            ORDER BY t.fecha_solicitud DESC;
            """;
        return await db.QueryAsync<TramiteListaDto>(sql,
            new { EstudianteId = estudianteId, Estado = string.IsNullOrWhiteSpace(estado) ? null : estado });
    }

    // ------------------------------------------------------------------
    // MESA (Secretaría): todos los trámites con su estado de pago
    // ------------------------------------------------------------------
    public async Task<IEnumerable<TramiteMesaDto>> ListarMesaAsync(string? estado)
    {
        using var db = CreateConnection();
        const string sql = """
            SELECT t.id,
                   t.codigo,
                   t.estado,
                   t.fecha_solicitud AS FechaSolicitud,
                   tt.codigo AS TipoCodigo,
                   tt.nombre AS TipoNombre,
                   p.nombres || ' ' || p.apellidos AS Estudiante,
                   e.codigo_estudiante AS CodigoEstudiante,
                   t.resolucion,
                   (SELECT b.voucher_estado
                      FROM pagos b
                     WHERE b.estudiante_id = t.estudiante_id
                       AND b.concepto_pago_id = tt.concepto_pago_id
                       AND b.periodo_id = t.periodo_id
                     ORDER BY b.fecha_pago DESC LIMIT 1) AS VoucherEstado
            FROM tramites t
            JOIN tipos_tramite tt ON tt.id = t.tipo_tramite_id
            JOIN estudiantes e ON e.id = t.estudiante_id
            JOIN personas p ON p.id = e.persona_id
            WHERE (@Estado IS NULL OR t.estado = @Estado)
            ORDER BY t.fecha_solicitud;
            """;
        return await db.QueryAsync<TramiteMesaDto>(sql,
            new { Estado = string.IsNullOrWhiteSpace(estado) ? null : estado });
    }

    public async Task<TramiteDetalleDto?> ObtenerDetalleAsync(int tramiteId)
    {
        using var db = CreateConnection();
        const string sql = """
            SELECT t.id,
                   t.codigo,
                   t.estado,
                   t.fecha_solicitud AS FechaSolicitud,
                   t.fecha_limite AS FechaLimite,
                   t.resolucion,
                   t.fecha_resolucion AS FechaResolucion,
                   t.datos::text AS DatosJson,
                   tt.codigo AS TipoCodigo,
                   tt.nombre AS TipoNombre,
                   tt.dias_habiles AS DiasHabiles,
                   cp.codigo AS PagoCodigo,
                   cp.nombre AS PagoNombre,
                   cp.monto AS PagoMonto,
                   p.nombres || ' ' || p.apellidos AS Estudiante,
                   e.codigo_estudiante AS CodigoEstudiante
            FROM tramites t
            JOIN tipos_tramite tt ON tt.id = t.tipo_tramite_id
            JOIN conceptos_pago cp ON cp.id = tt.concepto_pago_id
            JOIN estudiantes e ON e.id = t.estudiante_id
            JOIN personas p ON p.id = e.persona_id
            WHERE t.id = @Id;
            """;
        var detalle = await db.QueryFirstOrDefaultAsync<TramiteDetalleDto>(sql, new { Id = tramiteId });
        if (detalle != null)
        {
            detalle.Requisitos = await ListarRequisitosDeTramiteAsync(db, tramiteId);
        }
        return detalle;
    }

    public async Task<TramiteDetalleDto?> ObtenerPorCodigoAsync(string codigo)
    {
        using var db = CreateConnection();
        const string sqlId = "SELECT id FROM tramites WHERE codigo = @Codigo;";
        var id = await db.QueryFirstOrDefaultAsync<int?>(sqlId, new { Codigo = codigo });
        return id is null ? null : await ObtenerDetalleAsync(id.Value);
    }

    // ------------------------------------------------------------------
    // Catálogo: tipos TUPA y requisitos dinámicos por tipo (el AutoPostBack
    // del prototipo: al elegir tipo se cargan sus requisitos y ficha TUPA)
    // ------------------------------------------------------------------
    public async Task<IEnumerable<TipoTramiteDto>> ListarTiposAsync()
    {
        using var db = CreateConnection();
        const string sql = """
            SELECT tt.id, tt.codigo, tt.nombre, tt.dias_habiles AS DiasHabiles,
                   cp.codigo AS PagoCodigo, cp.nombre AS PagoNombre,
                   cp.monto AS PagoMonto
            FROM tipos_tramite tt
            JOIN conceptos_pago cp ON cp.id = tt.concepto_pago_id
            WHERE tt.activo
            ORDER BY tt.codigo;
            """;
        return await db.QueryAsync<TipoTramiteDto>(sql);
    }

    public async Task<TipoTramiteDto?> ObtenerTipoAsync(string codigo)
    {
        using var db = CreateConnection();
        const string sql = """
            SELECT tt.id, tt.codigo, tt.nombre, tt.dias_habiles AS DiasHabiles,
                   cp.codigo AS PagoCodigo, cp.nombre AS PagoNombre,
                   cp.monto AS PagoMonto
            FROM tipos_tramite tt
            JOIN conceptos_pago cp ON cp.id = tt.concepto_pago_id
            WHERE tt.codigo = @Codigo AND tt.activo;
            """;
        return await db.QueryFirstOrDefaultAsync<TipoTramiteDto>(sql, new { Codigo = codigo });
    }

    public async Task<IEnumerable<RequisitoDto>> ListarRequisitosDeTipoAsync(string tipoCodigo)
    {
        using var db = CreateConnection();
        const string sql = """
            SELECT r.id, r.orden, r.requisito
            FROM requisitos_tipos_tramite r
            JOIN tipos_tramite tt ON tt.id = r.tipo_tramite_id
            WHERE tt.codigo = @Codigo
            ORDER BY r.orden;
            """;
        return await db.QueryAsync<RequisitoDto>(sql, new { Codigo = tipoCodigo });
    }

    private static async Task<IEnumerable<RequisitoEstadoDto>> ListarRequisitosDeTramiteAsync(
        IDbConnection db, int tramiteId)
    {
        const string sql = """
            SELECT rc.id, rc.orden, rc.requisito,
                   tr.presentado, tr.observacion
            FROM tramite_requisitos tr
            JOIN requisitos_tipos_tramite rc ON rc.id = tr.requisito_catalogo_id
            WHERE tr.tramite_id = @Id
            ORDER BY rc.orden;
            """;
        return await db.QueryAsync<RequisitoEstadoDto>(sql, new { Id = tramiteId });
    }

    // ------------------------------------------------------------------
    // ALUMNO: crear trámite con TODOS los requisitos marcados como
    // presentados (los archivos se adjuntan en el propio formulario —
    // sin archivos no se registra, regla del prototipo).
    // La fecha límite = fecha_solicitud + dias_habiles del TUPA.
    // ------------------------------------------------------------------
    public async Task<(bool, string, string?)> CrearTramiteAsync(
        int estudianteId, int periodoId, string tipoCodigo, string? datos)
    {
        using var db = CreateConnection();
        db.Open();
        using var tx = db.BeginTransaction();

        var tipo = await db.QueryFirstOrDefaultAsync<TipoTramiteDto>("""
            SELECT tt.id, tt.codigo, tt.nombre, tt.dias_habiles AS DiasHabiles
            FROM tipos_tramite tt WHERE tt.codigo = @Codigo AND tt.activo;
            """, new { Codigo = tipoCodigo }, tx);

        if (tipo == null)
        {
            tx.Rollback();
            return (false, "El tipo de trámite no existe o no está activo.", null);
        }

        var codigo = await db.ExecuteScalarAsync<string>(SqlNuevoCodigo, transaction: tx);

        var nuevoId = await db.ExecuteScalarAsync<int>("""
            INSERT INTO tramites (codigo, tipo_tramite_id, estudiante_id, periodo_id,
                                  fecha_solicitud, fecha_limite, estado, datos)
            VALUES (@Codigo, @TipoId, @EstudianteId, @PeriodoId,
                    CURRENT_DATE, CURRENT_DATE + @DiasHabiles, 'Recibido',
                    COALESCE(@Datos::jsonb, '{}'::jsonb))
            RETURNING id;
            """, new { Codigo = codigo, TipoId = tipo.Id, EstudianteId = estudianteId,
                       PeriodoId = periodoId, DiasHabiles = tipo.DiasHabiles,
                       Datos = string.IsNullOrWhiteSpace(datos) ? null : datos }, tx);

        // plantilla de requisitos: todos presentados (se adjuntaron al crear)
        await db.ExecuteAsync("""
            INSERT INTO tramite_requisitos (tramite_id, requisito_catalogo_id, presentado)
            SELECT @Id, r.id, TRUE
            FROM requisitos_tipos_tramite r
            WHERE r.tipo_tramite_id = @TipoId;
            """, new { Id = nuevoId, TipoId = tipo.Id }, tx);

        tx.Commit();
        return (true, $"Trámite {codigo} registrado. Secretaría lo evaluará en {tipo.DiasHabiles} días hábiles.", codigo);
    }

    // ------------------------------------------------------------------
    // ALUMNO: corrección de trámite Observado/Rechazado — solo se
    // re-adjunta el requisito observado, NO se crea trámite nuevo.
    // ------------------------------------------------------------------
    public async Task<(bool, string)> CorregirRequisitoAsync(
        int tramiteId, int requisitoCatalogoId, string observacion)
    {
        using var db = CreateConnection();
        var filas = await db.ExecuteAsync("""
            UPDATE tramite_requisitos
            SET presentado = TRUE,
                observacion = @Observacion
            WHERE tramite_id = @Id AND requisito_catalogo_id = @ReqId;
            """, new { Id = tramiteId, ReqId = requisitoCatalogoId, Observacion = observacion });
        if (filas == 0) return (false, "El requisito no pertenece a ese trámite.");

        // el trámite observado vuelve a evaluación (corrección del prototipo)
        await db.ExecuteAsync("""
            UPDATE tramites
            SET estado = 'En evaluación',
                resolucion = NULL,
                fecha_resolucion = NULL,
                actualizado_en = CURRENT_TIMESTAMP
            WHERE id = @Id AND estado IN ('Observado','Rechazado');
            """, new { Id = tramiteId });

        return (true, "Requisito corregido. El trámite volvió a evaluación.");
    }

    // ------------------------------------------------------------------
    // MESA (Secretaría): avanzar el flujo con reglas:
    //   Aprobado  — exige voucher Validado en pagos (doble control)
    //   Observado/Rechazado — exige resolución (motivo) para el alumno
    //   Entregado — solo desde Aprobado
    // ------------------------------------------------------------------
    public async Task<(bool, string)> AvanzarEstadoAsync(
        int tramiteId, string nuevoEstado, string? resolucion, int usuarioId)
    {
        using var db = CreateConnection();

        if (nuevoEstado == "Aprobado")
        {
            const string checkPago = """
                SELECT count(*)
                FROM pagos b
                JOIN tramites t ON t.id = @Id
                JOIN tipos_tramite tt ON tt.id = t.tipo_tramite_id
                WHERE b.estudiante_id = t.estudiante_id
                  AND b.concepto_pago_id = tt.concepto_pago_id
                  AND b.periodo_id = t.periodo_id
                  AND b.voucher_estado = 'Validado';
                """;
            var pagado = await db.ExecuteScalarAsync<int>(checkPago, new { Id = tramiteId });
            if (pagado == 0)
                return (false, "Tesorería aún no validó el voucher de pago de este trámite.");
        }

        if ((nuevoEstado == "Observado" || nuevoEstado == "Rechazado")
            && string.IsNullOrWhiteSpace(resolucion))
        {
            return (false, "Debes indicar el motivo de la observación/rechazo.");
        }

        var filas = await db.ExecuteAsync("""
            UPDATE tramites
            SET estado = @Estado,
                resolucion = @Resolucion,
                fecha_resolucion = CASE WHEN @Estado IN ('Aprobado','Rechazado','Entregado')
                                        THEN CURRENT_DATE ELSE NULL END,
                actualizado_en = CURRENT_TIMESTAMP
            WHERE id = @Id
              AND estado <> 'Entregado'
              AND (@Estado = 'En evaluación' OR estado = 'Recibido' OR estado = 'En evaluación' OR estado = 'Observado');
            """, new { Id = tramiteId, Estado = nuevoEstado, Resolucion = resolucion });

        return filas > 0
            ? (true, $"Trámite actualizado a {nuevoEstado}.")
            : (false, "Transición de estado no permitida (¿ya entregado?).");
    }

    public async Task<int> ContarPendientesAsync()
    {
        using var db = CreateConnection();
        return await db.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM tramites WHERE estado IN ('Recibido','En evaluación');");
    }
}
