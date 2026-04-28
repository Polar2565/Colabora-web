using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Colabora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InfoController : ControllerBase
{
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;

    public InfoController(IConfiguration cfg, IWebHostEnvironment env)
    {
        _cfg = cfg;
        _env = env;
    }

    /// <summary>
    /// Web Service propio de Colabora Web.
    /// Devuelve información básica del sistema en formato JSON.
    /// GET /api/info/system
    /// </summary>
    [HttpGet("system")]
    public IActionResult GetSystemInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "1.0.0";

        var dto = new
        {
            appName = "Colabora Web",
            version,
            environment = _env.EnvironmentName,            // Development, Production, etc.
            serverTime = DateTime.UtcNow.ToString("o"),    // ISO 8601
            machineName = Environment.MachineName
        };

        return Ok(dto);
    }
}

