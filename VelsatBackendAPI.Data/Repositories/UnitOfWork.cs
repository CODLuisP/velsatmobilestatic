using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VelsatMobile.Data.Repositories;

namespace VelsatBackendAPI.Data.Repositories
{
    public class UnitOfWork : IUnitOfWork, IDisposable
    {
        private readonly string _defaultConnectionString;

        private MySqlConnection _defaultConnection;
        private MySqlTransaction _defaultTransaction;

        private readonly Lazy<IAplicativoRepository> _aplicativoRepository;
        private readonly Lazy<IUserRepository> _userRepository;


        private bool _disposed = false;
        private bool _committed = false;
        private readonly object _lockObject = new object();

        public UnitOfWork(MySqlConfiguration configuration)
        {
            _defaultConnectionString = configuration.DefaultConnection
                ?? throw new ArgumentNullException(nameof(configuration.DefaultConnection));

            _aplicativoRepository = new Lazy<IAplicativoRepository>(() =>
                new AplicativoRepository(DefaultConnection, _defaultTransaction));

            _userRepository = new Lazy<IUserRepository>(() =>
               new UserRepository(DefaultConnection, _defaultTransaction));
        }

        private MySqlConnection DefaultConnection
        {
            get
            {
                ValidateNotDisposedOrCommitted();

                if (_defaultConnection == null)
                {
                    lock (_lockObject)
                    {
                        if (_defaultConnection == null)
                        {
                            // ✅ CAMBIO: Usar método con retry
                            _defaultConnection = OpenConnectionWithRetry(
                                _defaultConnectionString,
                                "DEFAULT (con transacción)");

                            // Iniciar transacción DESPUÉS de abrir la conexión exitosamente
                            _defaultTransaction = _defaultConnection.BeginTransaction();

                            System.Diagnostics.Debug.WriteLine(
                                $"[UnitOfWork] Transacción DEFAULT iniciada");
                        }
                    }
                }
                return _defaultConnection;
            }
        }

        private void ValidateNotDisposedOrCommitted()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UnitOfWork),
                    "No se puede usar un UnitOfWork que ya ha sido liberado. Crea una nueva instancia.");
            }

            if (_committed)
            {
                throw new InvalidOperationException(
                    "Este UnitOfWork ya fue confirmado con SaveChanges(). Crea una nueva instancia para realizar más operaciones.");
            }
        }

        /// <summary>
        /// Abre una conexión MySQL con reintentos automáticos en caso de colisión de pool.
        /// </summary>
        private MySqlConnection OpenConnectionWithRetry(
            string connectionString,
            string connectionName,
            int maxRetries = 5)
        {
            Exception lastException = null;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var connection = new MySqlConnection(connectionString);
                    connection.Open();

                    // ✅ CRÍTICO: Configurar charset UTF-8 inmediatamente después de abrir
                    using (var cmd = new MySqlCommand("SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci", connection))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[UnitOfWork] ✅ Conexión {connectionName} " +
                        $"{connection.ServerThread} abierta con transacción" +
                        (attempt > 0 ? $" (intento {attempt + 1})" : ""));

                    return connection;
                }
                catch (ArgumentException ex) when (
                    ex.Message.Contains("An item with the same key has already been added"))
                {
                    lastException = ex;

                    System.Diagnostics.Debug.WriteLine(
                        $"[UnitOfWork] ⚠️ Pool collision detectada en {connectionName} " +
                        $"(intento {attempt + 1}/{maxRetries})");

                    if (attempt < maxRetries - 1)
                    {
                        // Backoff exponencial: 10ms, 20ms, 40ms, 80ms, 160ms
                        int delayMs = 10 * (int)Math.Pow(2, attempt);
                        System.Threading.Thread.Sleep(delayMs);

                        // ✅ CRÍTICO: Intentar limpiar el pool antes de reintentar
                        try
                        {
                            MySqlConnection.ClearPool(new MySqlConnection(connectionString));
                            System.Diagnostics.Debug.WriteLine(
                                $"[UnitOfWork] Pool {connectionName} limpiado");
                        }
                        catch
                        {
                            // Ignorar errores al limpiar
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Otros errores no relacionados con el pool - fallar inmediatamente
                    System.Diagnostics.Debug.WriteLine(
                        $"[UnitOfWork] ❌ Error abriendo {connectionName}: {ex.Message}");
                    throw;
                }
            }

            // Si llegamos aquí, fallaron todos los intentos
            throw new InvalidOperationException(
                $"No se pudo abrir la conexión {connectionName} después de {maxRetries} intentos. " +
                $"Pool de conexiones MySQL posiblemente corrupto.",
                lastException);
        }

        public IAplicativoRepository AplicativoRepository
        {
            get
            {
                ValidateNotDisposedOrCommitted();
                return _aplicativoRepository.Value;

            }
        }

        public IUserRepository UserRepository
        {
            get
            {
                ValidateNotDisposedOrCommitted();
                return _userRepository.Value;
            }
        }


        public void SaveChanges()
        {
            ValidateNotDisposedOrCommitted();

            lock (_lockObject)
            {
                try
                {
                    // Commit de las transacciones
                    _defaultTransaction?.Commit();
                    _committed = true;
                }
                catch
                {
                    // Rollback en caso de error
                    try { _defaultTransaction?.Rollback(); } catch { }
                    throw;
                }
                finally
                {
                    // ✅ CRÍTICO: Liberar TODO inmediatamente después de commit
                    DisposeTransactionsAndConnections();
                }
            }
        }

        private void DisposeTransactionsAndConnections()
        {
            // Liberar transacciones
            if (_defaultTransaction != null)
            {
                _defaultTransaction.Dispose();
                _defaultTransaction = null;
            }

            // ✅ Cerrar Y disponer conexiones inmediatamente
            if (_defaultConnection != null)
            {
                try
                {
                    if (_defaultConnection.State == ConnectionState.Open)
                    {
                        _defaultConnection.Close();
                    }
                    _defaultConnection.Dispose();
                }
                catch { }
                finally
                {
                    _defaultConnection = null;
                }
            }

            // ✅ NUEVO: Sugerir al GC que limpie inmediatamente (opcional, solo si hay problemas graves)
            // GC.Collect(0, GCCollectionMode.Optimized);
        }

        // ✅ Dispose optimizado
        public void Dispose()
        {
            Dispose(true);
            // ✅ REMOVIDO el finalizer, así que esto ya no es necesario
            // GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return; // Ya fue liberado
            }

            if (disposing)
            {
                lock (_lockObject)
                {
                    try
                    {
                        // ✅ MEJORADO: Rollback solo si la transacción está activa
                        if (!_committed)
                        {
                            try
                            {
                                if (_defaultTransaction != null && _defaultTransaction.Connection != null)
                                    _defaultTransaction.Rollback();
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[UnitOfWork] Rollback default error: {ex.Message}");
                            }
                        }

                        // Liberar todo
                        DisposeTransactionsAndConnections();

                        // Disponer repositorios si implementan IDisposable
                        DisposeRepositories();
                    }
                    catch (Exception ex)
                    {
                        // Log el error pero no lanzar excepciones en Dispose
                        System.Diagnostics.Debug.WriteLine($"[UnitOfWork] Error disposing: {ex.Message}");
                    }
                    finally
                    {
                        _disposed = true;
                    }
                }
            }
        }

        private void DisposeRepositories()
        {
            // Solo disponer si fueron inicializados
            TryDisposeRepository(_aplicativoRepository);
        }

        private void TryDisposeRepository<T>(Lazy<T> lazyRepo)
        {
            if (lazyRepo != null && lazyRepo.IsValueCreated && lazyRepo.Value is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch
                {
                    // Ignorar errores al disponer repositorios
                }
            }
        }
    }
}