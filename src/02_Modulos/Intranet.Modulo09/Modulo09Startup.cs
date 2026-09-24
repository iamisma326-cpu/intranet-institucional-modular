using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Intranet.Core.Contracts;
using Intranet.Modulo09.Services;

namespace Intranet.Modulo09;

/// <summary>
/// Registrador de Servicios del Módulo 09.
/// Agrega aquí tus servicios o repositorios propios. El sistema los cargará automáticamente.
/// </summary>
public class Modulo09Startup : IModuloStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Trámites TUPA (Equipo 09)
        services.AddScoped<ITramiteService, TramiteService>();
    }
}
