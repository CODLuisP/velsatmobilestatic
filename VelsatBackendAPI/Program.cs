using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;
using Serilog;
using System.Data;
using System.Diagnostics;
using System.Text;
using VelsatBackendAPI.Data.Repositories;
using VelsatMobile.Data.Repositories;
using MySqlConfiguration = VelsatBackendAPI.Data.MySqlConfiguration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Configuration.AddJsonFile("appsettings.json");

var secretkey = builder.Configuration.GetSection("settings").GetSection("secretkey").Value;
var keyBytes = Encoding.UTF8.GetBytes(secretkey);

builder.Services.AddAuthentication(config =>
{
    config.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    config.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(config =>
{
    config.RequireHttpsMetadata = false;
    config.SaveToken = true;
    config.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ═══════════════════════════════════════════════════════════════
// 🔧 VALIDACIÓN DE CONNECTION STRINGS
// ═══════════════════════════════════════════════════════════════
var defaultConn = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(defaultConn))
{
    throw new InvalidOperationException("❌ DefaultConnection string not found in configuration");
}

Console.WriteLine($"✅ DefaultConnection configurado correctamente");

var mysqlConfiguration = new MySqlConfiguration(defaultConn);
builder.Services.AddSingleton(mysqlConfiguration);

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IReadOnlyUnitOfWork, ReadOnlyUnitOfWork>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", builder =>
    {
        builder
               .AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials()
               .SetIsOriginAllowed(origin => true);
    });
});

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(1);
});

var app = builder.Build();

// ═══════════════════════════════════════════════════════════════
// 🔧 LIMPIEZA DE POOLS AL INICIAR - CON REINTENTOS
// ═══════════════════════════════════════════════════════════════
int startupAttempts = 0;
bool poolsCleared = false;

while (!poolsCleared && startupAttempts < 3)
{
    try
    {
        startupAttempts++;
        MySqlConnection.ClearAllPools();
        poolsCleared = true;

        var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
        startupLogger.LogInformation(
            "✅ [Startup] Pools de MySQL limpiados correctamente (intento {Attempt})",
            startupAttempts
        );

        // Info del proceso para diagnóstico
        var process = Process.GetCurrentProcess();
        startupLogger.LogInformation(
            "📊 [Startup] Process ID: {ProcessId} | Start Time: {StartTime}",
            process.Id,
            process.StartTime
        );
    }
    catch (Exception ex)
    {
        var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
        startupLogger.LogWarning(
            ex,
            "⚠️ [Startup] Error limpiando pools (intento {Attempt}/{MaxAttempts}): {Message}",
            startupAttempts,
            3,
            ex.Message
        );

        if (startupAttempts < 3)
        {
            Thread.Sleep(1000 * startupAttempts); // Backoff: 1s, 2s, 3s
        }
    }
}

if (!poolsCleared)
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogError("❌ [Startup] No se pudieron limpiar los pools después de 3 intentos");
}

// ═══════════════════════════════════════════════════════════════
// 🔧 MANEJO GRACEFUL DE SHUTDOWN (RECICLAJE DE IIS)
// ═══════════════════════════════════════════════════════════════
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

lifetime.ApplicationStopping.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning("⚠️ [Shutdown] Aplicación deteniéndose (reciclaje de IIS detectado)...");

    try
    {
        var process = Process.GetCurrentProcess();
        logger.LogInformation(
            "📊 [Shutdown] Process ID: {ProcessId} | Uptime: {Uptime} minutos",
            process.Id,
            (DateTime.Now - process.StartTime).TotalMinutes
        );

        // Dar tiempo a que terminen requests en vuelo
        logger.LogInformation("⏳ [Shutdown] Esperando 2 segundos para requests activos...");
        Thread.Sleep(2000);

        // Limpiar todos los pools de MySQL
        MySqlConnection.ClearAllPools();
        logger.LogInformation("✅ [Shutdown] Pools de MySQL limpiados correctamente");

        // Forzar recolección de basura para liberar memoria
        logger.LogInformation("🧹 [Shutdown] Ejecutando garbage collection...");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        logger.LogInformation("✅ [Shutdown] Limpieza completada correctamente");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ [Shutdown] Error durante limpieza: {Message}", ex.Message);
    }
});

lifetime.ApplicationStopped.Register(() =>
{
    try
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("🛑 [Shutdown] Aplicación detenida completamente");
    }
    catch
    {
        // Si el logger ya no está disponible, escribir a consola
        Console.WriteLine("🛑 [Shutdown] Aplicación detenida completamente");
    }
});

// ═══════════════════════════════════════════════════════════════
// 🌐 CONFIGURACIÓN DE MIDDLEWARE
// ═══════════════════════════════════════════════════════════════
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseCors("AllowSpecificOrigin");
app.UseSession();

// ═══════════════════════════════════════════════════════════════
// 🗺️ MAPEO DE CONTROLLERS Y HUBS
// ═══════════════════════════════════════════════════════════════
app.MapControllers();

// ═══════════════════════════════════════════════════════════════
// 🚀 INICIAR APLICACIÓN
// ═══════════════════════════════════════════════════════════════
var finalLogger = app.Services.GetRequiredService<ILogger<Program>>();
finalLogger.LogInformation("🚀 [Startup] Aplicación iniciada correctamente");

app.Run();