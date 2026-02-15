using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VelsatBackendAPI.Data;
using VelsatBackendAPI.Data.Repositories;

namespace VelsatMobile.Data.Repositories
{
    public class ReadOnlyUnitOfWork : IReadOnlyUnitOfWork
    {
        private readonly string _defaultConnectionString;
        private readonly string _secondConnectionString;

        private MySqlConnection _defaultConnection;
        private MySqlConnection _secondConnection;

        private readonly Lazy<IAplicativoRepository> _aplicativoRepository;
        private readonly Lazy<IUserRepository> _userRepository;


        private bool _disposed = false;
        private readonly object _lockObject = new object();

        public ReadOnlyUnitOfWork(VelsatBackendAPI.Data.MySqlConfiguration configuration)
        {
            _defaultConnectionString = configuration.DefaultConnection
                ?? throw new ArgumentNullException(nameof(configuration.DefaultConnection));

            _secondConnectionString = configuration.DefaultConnection
                ?? throw new ArgumentNullException(nameof(configuration.DefaultConnection));

            // ✅ Inicializar servicio SIN transacción (segundo parámetro = null)
            _aplicativoRepository = new Lazy<IAplicativoRepository>(() => new AplicativoRepository(DefaultConnection, null, SecondConnection, null));

            _userRepository = new Lazy<IUserRepository>(() => new UserRepository(DefaultConnection, null));

        }

        private MySqlConnection DefaultConnection
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ReadOnlyUnitOfWork));

                lock (_lockObject)
                {
                    // ✅ CRÍTICO: Validar estado de la conexión SIEMPRE
                    if (_defaultConnection == null ||
                        _defaultConnection.State == ConnectionState.Closed ||
                        _defaultConnection.State == ConnectionState.Broken)
                    {
                        // Si existe una conexión rota, limpiarla primero
                        if (_defaultConnection != null)
                        {
                            try
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[ReadOnlyUnitOfWork] ⚠️ Conexión DEFAULT en estado {_defaultConnection.State}, recreando...");
                                _defaultConnection.Close();
                                _defaultConnection.Dispose();
                            }
                            catch { }
                            _defaultConnection = null;
                        }

                        _defaultConnection = OpenConnectionWithRetry(
                            _defaultConnectionString,
                            "DEFAULT",
                            maxRetries: 3);
                    }
                    else if (_defaultConnection.State == ConnectionState.Connecting ||
                             _defaultConnection.State == ConnectionState.Executing ||
                             _defaultConnection.State == ConnectionState.Fetching)
                    {
                        // Esperar un poco si está ocupada
                        System.Threading.Thread.Sleep(100);
                    }

                    return _defaultConnection;
                }
            }
        }

        private MySqlConnection SecondConnection
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ReadOnlyUnitOfWork));

                lock (_lockObject)
                {
                    // ✅ CRÍTICO: Validar estado de la conexión SIEMPRE
                    if (_secondConnection == null ||
                        _secondConnection.State == ConnectionState.Closed ||
                        _secondConnection.State == ConnectionState.Broken)
                    {
                        // Si existe una conexión rota, limpiarla primero
                        if (_secondConnection != null)
                        {
                            try
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[ReadOnlyUnitOfWork] ⚠️ Conexión SECOND en estado {_secondConnection.State}, recreando...");
                                _secondConnection.Close();
                                _secondConnection.Dispose();
                            }
                            catch { }
                            _secondConnection = null;
                        }

                        _secondConnection = OpenConnectionWithRetry(
                            _secondConnectionString,
                            "SECOND",
                            maxRetries: 3);
                    }
                    else if (_secondConnection.State == ConnectionState.Connecting ||
                             _secondConnection.State == ConnectionState.Executing ||
                             _secondConnection.State == ConnectionState.Fetching)
                    {
                        // Esperar un poco si está ocupada
                        System.Threading.Thread.Sleep(100);
                    }

                    return _secondConnection;
                }
            }
        }

        public IAplicativoRepository AplicativoRepository
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ReadOnlyUnitOfWork));
                return _aplicativoRepository.Value;
            }
        }

        public IUserRepository UserRepository
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ReadOnlyUnitOfWork));
                return _userRepository.Value;
            }
        }

        private MySqlConnection OpenConnectionWithRetry(
            string connectionString,
            string connectionName,
            int maxRetries = 3)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException($"Connection string para {connectionName} es null o vacío");
            }

            Exception lastException = null;
            int attempt = 0;

            while (attempt < maxRetries)
            {
                MySqlConnection connection = null;

                try
                {
                    attempt++;

                    System.Diagnostics.Debug.WriteLine(
                        $"[ReadOnlyUnitOfWork] 🔄 Intentando conexión {connectionName} (intento {attempt}/{maxRetries})");

                    connection = new MySqlConnection(connectionString);

                    // ✅ CRÍTICO: Validar que la conexión no sea null
                    if (connection == null)
                    {
                        throw new InvalidOperationException($"MySqlConnection para {connectionName} es null");
                    }

                    // Abrir la conexión
                    connection.Open();

                    // ✅ CRÍTICO: Verificar que realmente se abrió
                    if (connection.State != ConnectionState.Open)
                    {
                        throw new InvalidOperationException(
                            $"Conexión {connectionName} en estado {connection.State}, esperaba Open");
                    }

                    // ✅ CRÍTICO: Test de conectividad + configurar charset
                    using (var cmd = new MySqlCommand("SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci; SELECT 1", connection))
                    {
                        cmd.CommandTimeout = 5;
                        var result = cmd.ExecuteScalar();

                        if (result == null || Convert.ToInt32(result) != 1)
                        {
                            throw new InvalidOperationException($"Test de conexión {connectionName} falló");
                        }
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[ReadOnlyUnitOfWork] ✅ Conexión {connectionName} establecida " +
                        $"(ServerThread: {connection.ServerThread})" +
                        (attempt > 1 ? $" después de {attempt} intentos" : ""));

                    return connection;
                }
                catch (MySqlException mysqlEx)
                {
                    lastException = mysqlEx;

                    System.Diagnostics.Debug.WriteLine(
                        $"[ReadOnlyUnitOfWork] ⚠️ MySqlException en {connectionName} " +
                        $"(intento {attempt}/{maxRetries}): [{mysqlEx.Number}] {mysqlEx.Message}");

                    // Limpiar conexión fallida
                    try
                    {
                        connection?.Close();
                        connection?.Dispose();
                    }
                    catch { }

                    // Errores que justifican reintentar
                    bool shouldRetry = mysqlEx.Number == 1042 || // Unable to connect
                                      mysqlEx.Number == 1053 || // Server shutdown in progress
                                      mysqlEx.Number == 1129 || // Host blocked
                                      mysqlEx.Number == 2003 || // Can't connect to MySQL server
                                      mysqlEx.Number == 2006 || // MySQL server has gone away
                                      mysqlEx.Number == 2013;   // Lost connection during query

                    if (!shouldRetry || attempt >= maxRetries)
                    {
                        // No reintentar estos errores o ya alcanzamos max intentos
                        break;
                    }
                }
                catch (ArgumentException argEx) when (argEx.Message.Contains("An item with the same key has already been added"))
                {
                    lastException = argEx;

                    System.Diagnostics.Debug.WriteLine(
                        $"[ReadOnlyUnitOfWork] ⚠️ Pool collision en {connectionName} (intento {attempt}/{maxRetries})");

                    // Limpiar conexión fallida
                    try
                    {
                        connection?.Close();
                        connection?.Dispose();
                    }
                    catch { }
                }
                catch (NullReferenceException nullEx)
                {
                    lastException = nullEx;

                    System.Diagnostics.Debug.WriteLine(
                        $"[ReadOnlyUnitOfWork] ❌ NullReferenceException en {connectionName} " +
                        $"(intento {attempt}/{maxRetries}): {nullEx.Message}");

                    // Limpiar conexión fallida
                    try
                    {
                        connection?.Close();
                        connection?.Dispose();
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    System.Diagnostics.Debug.WriteLine(
                        $"[ReadOnlyUnitOfWork] ❌ Error inesperado en {connectionName}: {ex.GetType().Name} - {ex.Message}");

                    // Limpiar conexión fallida
                    try
                    {
                        connection?.Close();
                        connection?.Dispose();
                    }
                    catch { }
                }

                // Si no es el último intento, hacer backoff y limpiar pool
                if (attempt < maxRetries)
                {
                    // Backoff: 500ms, 1000ms, 1500ms
                    int delayMs = 500 * attempt;

                    System.Diagnostics.Debug.WriteLine(
                        $"[ReadOnlyUnitOfWork] ⏳ Esperando {delayMs}ms antes de reintentar {connectionName}...");

                    System.Threading.Thread.Sleep(delayMs);

                    // ✅ CRÍTICO: Limpiar TODOS los pools antes de reintentar
                    try
                    {
                        MySqlConnection.ClearAllPools();
                        System.Diagnostics.Debug.WriteLine(
                            $"[ReadOnlyUnitOfWork] 🧹 Pools limpiados antes de reintentar {connectionName}");
                    }
                    catch (Exception clearEx)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ReadOnlyUnitOfWork] ⚠️ Error limpiando pools: {clearEx.Message}");
                    }
                }
            }

            // Si llegamos aquí, fallaron todos los intentos
            var errorMsg = $"❌ No se pudo abrir conexión {connectionName} después de {maxRetries} intentos. " +
                          $"Último error: {lastException?.GetType().Name} - {lastException?.Message}";

            System.Diagnostics.Debug.WriteLine($"[ReadOnlyUnitOfWork] {errorMsg}");

            throw new InvalidOperationException(errorMsg, lastException);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_lockObject)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("[ReadOnlyUnitOfWork] 🧹 Disposing...");

                    // Cerrar conexión DEFAULT
                    if (_defaultConnection != null)
                    {
                        try
                        {
                            var connectionId = _defaultConnection.ServerThread;

                            if (_defaultConnection.State == ConnectionState.Open)
                                _defaultConnection.Close();

                            _defaultConnection.Dispose();

                            System.Diagnostics.Debug.WriteLine(
                                $"[ReadOnlyUnitOfWork] ✅ Conexión DEFAULT {connectionId} cerrada");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[ReadOnlyUnitOfWork] ⚠️ Error cerrando DEFAULT: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ReadOnlyUnitOfWork] ⚠️ Error disposing: {ex.Message}");
                }
                finally
                {
                    _defaultConnection = null;
                    _disposed = true;

                    System.Diagnostics.Debug.WriteLine("[ReadOnlyUnitOfWork] ✅ Disposed completamente");
                }
            }
        }
    }
}