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

            // ✅ CORREGIDO: Estaba usando DefaultConnection en lugar de SecondConnection
            _secondConnectionString = configuration.SecondConnection
                ?? throw new ArgumentNullException(nameof(configuration.SecondConnection));

            _aplicativoRepository = new Lazy<IAplicativoRepository>(() =>
                new AplicativoRepository(DefaultConnection, null, SecondConnection, null));

            _userRepository = new Lazy<IUserRepository>(() =>
                new UserRepository(DefaultConnection, null));
        }

        private MySqlConnection DefaultConnection
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ReadOnlyUnitOfWork));

                lock (_lockObject)
                {
                    if (_defaultConnection == null ||
                        _defaultConnection.State == ConnectionState.Closed ||
                        _defaultConnection.State == ConnectionState.Broken)
                    {
                        if (_defaultConnection != null)
                        {
                            try { _defaultConnection.Close(); _defaultConnection.Dispose(); } catch { }
                            _defaultConnection = null;
                        }

                        _defaultConnection = OpenConnectionWithRetry(_defaultConnectionString, "DEFAULT", maxRetries: 3);
                    }
                    else if (_defaultConnection.State == ConnectionState.Connecting ||
                             _defaultConnection.State == ConnectionState.Executing ||
                             _defaultConnection.State == ConnectionState.Fetching)
                    {
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
                    if (_secondConnection == null ||
                        _secondConnection.State == ConnectionState.Closed ||
                        _secondConnection.State == ConnectionState.Broken)
                    {
                        if (_secondConnection != null)
                        {
                            try { _secondConnection.Close(); _secondConnection.Dispose(); } catch { }
                            _secondConnection = null;
                        }

                        _secondConnection = OpenConnectionWithRetry(_secondConnectionString, "SECOND", maxRetries: 3);
                    }
                    else if (_secondConnection.State == ConnectionState.Connecting ||
                             _secondConnection.State == ConnectionState.Executing ||
                             _secondConnection.State == ConnectionState.Fetching)
                    {
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
                throw new ArgumentException($"Connection string para {connectionName} es null o vacío");

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
                    connection.Open();

                    if (connection.State != ConnectionState.Open)
                        throw new InvalidOperationException(
                            $"Conexión {connectionName} en estado {connection.State}, esperaba Open");

                    using (var cmd = new MySqlCommand("SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci; SELECT 1", connection))
                    {
                        cmd.CommandTimeout = 5;
                        var result = cmd.ExecuteScalar();

                        if (result == null || Convert.ToInt32(result) != 1)
                            throw new InvalidOperationException($"Test de conexión {connectionName} falló");
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
                    try { connection?.Close(); connection?.Dispose(); } catch { }

                    bool shouldRetry = mysqlEx.Number == 1042 ||
                                      mysqlEx.Number == 1053 ||
                                      mysqlEx.Number == 1129 ||
                                      mysqlEx.Number == 2003 ||
                                      mysqlEx.Number == 2006 ||
                                      mysqlEx.Number == 2013;

                    if (!shouldRetry || attempt >= maxRetries) break;
                }
                catch (ArgumentException argEx) when (argEx.Message.Contains("An item with the same key has already been added"))
                {
                    lastException = argEx;
                    try { connection?.Close(); connection?.Dispose(); } catch { }
                }
                catch (NullReferenceException nullEx)
                {
                    lastException = nullEx;
                    try { connection?.Close(); connection?.Dispose(); } catch { }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    try { connection?.Close(); connection?.Dispose(); } catch { }
                }

                if (attempt < maxRetries)
                {
                    int delayMs = 500 * attempt;
                    System.Threading.Thread.Sleep(delayMs);

                    try
                    {
                        MySqlConnection.ClearAllPools();
                    }
                    catch { }
                }
            }

            throw new InvalidOperationException(
                $"❌ No se pudo abrir conexión {connectionName} después de {maxRetries} intentos. " +
                $"Último error: {lastException?.GetType().Name} - {lastException?.Message}",
                lastException);
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

                    // ✅ CORREGIDO: Cerrar AMBAS conexiones
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

                    // ✅ CORREGIDO: Segunda conexión también se cierra correctamente
                    if (_secondConnection != null)
                    {
                        try
                        {
                            var connectionId = _secondConnection.ServerThread;
                            if (_secondConnection.State == ConnectionState.Open)
                                _secondConnection.Close();
                            _secondConnection.Dispose();
                            System.Diagnostics.Debug.WriteLine(
                                $"[ReadOnlyUnitOfWork] ✅ Conexión SECOND {connectionId} cerrada");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[ReadOnlyUnitOfWork] ⚠️ Error cerrando SECOND: {ex.Message}");
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
                    _secondConnection = null; // ✅ CORREGIDO: También limpiar la segunda
                    _disposed = true;
                    System.Diagnostics.Debug.WriteLine("[ReadOnlyUnitOfWork] ✅ Disposed completamente");
                }
            }
        }
    }
}