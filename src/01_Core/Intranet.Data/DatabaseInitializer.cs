using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Intranet.Core.Entities;
using Intranet.Core.Contracts;

namespace Intranet.Data;

public static class DatabaseInitializer
{
    public static async Task InicializarAsync(ApplicationDbContext context, IConfiguration? configuration = null)
    {
        try
        {
            await context.Database.EnsureCreatedAsync();

            // 1. Usuarios institucionales representativos del IESTP Argentina
            var usuariosSemilla = new List<Usuario>
            {
                new() { Dni = "00000001", CodigoInstitucional = "ADMIN-2026", Nombres = "Administrador", Apellidos = "General de TI", Email = "admin.ti@ieargentina.edu.pe", Rol = "Admin", Estado = true },
                new() { Dni = "10000001", CodigoInstitucional = "DIR-2026", Nombres = "Manuel", Apellidos = "Alvarado Carranza", Email = "direccion@ieargentina.edu.pe", Rol = "Director", Estado = true },
                new() { Dni = "20000001", CodigoInstitucional = "COORD-DSI", Nombres = "Carlos", Apellidos = "Mendoza Rivas", Email = "coord.sistemas@ieargentina.edu.pe", Rol = "Coordinador", Estado = true },
                new() { Dni = "30000001", CodigoInstitucional = "SEC-ACAD", Nombres = "Rosa", Apellidos = "Morales Salazar", Email = "secretaria.academica@ieargentina.edu.pe", Rol = "Secretaria", Estado = true },
                new() { Dni = "40000001", CodigoInstitucional = "TES-2026", Nombres = "Elena", Apellidos = "Ramos Palacios", Email = "tesoreria@ieargentina.edu.pe", Rol = "Tesoreria", Estado = true },
                new() { Dni = "12345678", CodigoInstitucional = "DOC-DSI-01", Nombres = "Roberto", Apellidos = "Sánchez Benítez", Email = "rsanchez@ieargentina.edu.pe", Rol = "Docente", Estado = true },
                new() { Dni = "87654321", CodigoInstitucional = "EST-DSI-001", Nombres = "Felipe", Apellidos = "Ostos", Email = "felipe.ostos@ieargentina.edu.pe", Rol = "Alumno", Estado = true },
                new() { Dni = "77654321", CodigoInstitucional = "EST-DSI-002", Nombres = "Ana", Apellidos = "García Flores", Email = "ana.garcia@ieargentina.edu.pe", Rol = "Alumno", Estado = true },
                new() { Dni = "66554433", CodigoInstitucional = "EST-CONT-001", Nombres = "Luis", Apellidos = "Torres Quispe", Email = "luis.torres@ieargentina.edu.pe", Rol = "Alumno", Estado = true }
            };

            foreach (var u in usuariosSemilla)
            {
                if (!await context.Usuarios.AnyAsync(x => x.Dni == u.Dni))
                {
                    await context.Usuarios.AddAsync(u);
                }
            }

            await context.SaveChangesAsync();

            // 2. Auto-Runner de scripts SQL por módulo (Sql/schema.sql)
            if (configuration != null)
            {
                await EjecutarEsquemasModularesAsync(configuration);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DatabaseInitializer] Aviso: {ex.Message}");
        }
    }

    private static async Task EjecutarEsquemasModularesAsync(IConfiguration configuration)
    {
        var defaultConn = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(defaultConn)) return;

        var baseDir = AppContext.BaseDirectory;
        var currentDir = Directory.GetCurrentDirectory();

        for (int i = 1; i <= 9; i++)
        {
            var num = i.ToString("D2");
            var dbName = $"db_modulo{num}";
            
            // Buscar si existe un archivo schema.sql en las rutas del módulo
            // Orden: (1) copia junto al binario (dotnet run / publish / Docker),
            //        (2-3) rutas relativas al directorio de trabajo actual,
            //        (4-5) rutas relativas al binario como fallback histórico.
            var possiblePaths = new[]
            {
                Path.Combine(baseDir, "Sql", $"modulo{num}_schema.sql"),
                Path.Combine(currentDir, "src", "02_Modulos", $"Intranet.Modulo{num}", "Sql", "schema.sql"),
                Path.Combine(currentDir, "02_Modulos", $"Intranet.Modulo{num}", "Sql", "schema.sql"),
                Path.Combine(baseDir, "..", "..", "..", "..", "02_Modulos", $"Intranet.Modulo{num}", "Sql", "schema.sql"),
                Path.Combine(baseDir, "src", "02_Modulos", $"Intranet.Modulo{num}", "Sql", "schema.sql")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var sql = await File.ReadAllTextAsync(path);
                        if (!string.IsNullOrWhiteSpace(sql))
                        {
                            var builder = new MySqlConnectionStringBuilder(defaultConn) { Database = dbName };
                            using var conn = new MySqlConnection(builder.ConnectionString);
                            await conn.OpenAsync();
                            using var cmd = new MySqlCommand(sql, conn);
                            await cmd.ExecuteNonQueryAsync();
                            Console.WriteLine($"[DatabaseInitializer] ✓ Script SQL ejecutado para {dbName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DatabaseInitializer] Aviso al ejecutar SQL de {dbName}: {ex.Message}");
                    }
                    break;
                }
            }
        }
    }
}
