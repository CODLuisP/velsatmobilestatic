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
        private readonly string _secondConnectionString;

        private MySqlConnection _defaultConnection;
        private MySqlTransaction _defaultTransaction;

        private MySqlConnection _secondConnection;
        private MySqlTransaction _secondTransaction;

        private readonly Lazy<IAplicativoRepository> _aplicativoRepository;
        private readonly Lazy<IUserRepository> _userRepository;

        private bool _disposed = false;
        private bool _committed = false;
        private readonly object _lockObject = new object();

        public UnitOfWork(MySqlConfiguration configuration)
        {
            _defaultConnectionString = configuration.DefaultConnection
                ?? throw new ArgumentNullException(nameof(configuration.DefaultConnection));

            _secondConnectionString = configuration.SecondConnection
                ?? throw new ArgumentNullException(nameof(configuration.SecondConnection));

            _aplicativoRepository = new Lazy<IAplicativoRepository>(() =>
                new AplicativoRepository(DefaultConnection, _defaultTransaction, SecondConnection, _secondTransaction));

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
                            _defaultConnection = OpenConnectionWithRetry(
                                _defaultConnectionString,
                                "DEFAULT (con transacción)");

                            _defaultTransaction = _defaultConnection.BeginTransaction();

                            System.Diagnostics.Debug.WriteLine(
                                $"[UnitOfWork] Transacción DEFAULT iniciada");
                        }
                    }
                }
                return _defaultConnection;
            }
        }

        private MySqlConnection SecondConnection
        {
            get
            {
                ValidateNotDisposedOrCommitted();

                if (_secondConnection == null)
                {
                    lock (_lockObject)
                    {
                        if (_secondConnection == null)
                        {
                            _secondConnection = OpenConnectionWithRetry(
                                _secondConnectionString,
                                "SECOND (con transacción)");

                            _secondTransaction = _secondConnection.BeginTransaction();

                            System.Diagnostics.Debug.WriteLine(
                                $"[UnitOfWork] Transacción SECOND iniciada");
                        }
                    }
                }
                return _secondConnection;
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
                        int delayMs = 10 * (int)Math.Pow(2, attempt);
                        System.Threading.Thread.Sleep(delayMs);

                        try
                        {
                            MySqlConnection.ClearPool(new MySqlConnection(connectionString));
                            System.Diagnostics.Debug.WriteLine(
                                $"[UnitOfWork] Pool {connectionName} limpiado");
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[UnitOfWork] ❌ Error abriendo {connectionName}: {ex.Message}");
                    throw;
                }
            }

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
                    // ✅ CORREGIDO: Commit de AMBAS transacciones
                    _defaultTransaction?.Commit();
                    _secondTransaction?.Commit();
                    _committed = true;
                }
                catch
                {
                    // ✅ CORREGIDO: Rollback de AMBAS en caso de error
                    try { _defaultTransaction?.Rollback(); } catch { }
                    try { _secondTransaction?.Rollback(); } catch { }
                    throw;
                }
                finally
                {
                    DisposeTransactionsAndConnections();
                }
            }
        }

        private void DisposeTransactionsAndConnections()
        {
            // ✅ CORREGIDO: Liberar AMBAS transacciones
            if (_defaultTransaction != null)
            {
                _defaultTransaction.Dispose();
                _defaultTransaction = null;
            }

            if (_secondTransaction != null)
            {
                _secondTransaction.Dispose();
                _secondTransaction = null;
            }

            // ✅ CORREGIDO: Cerrar AMBAS conexiones
            if (_defaultConnection != null)
            {
                try { _defaultConnection.Close(); _defaultConnection.Dispose(); } catch { }
                _defaultConnection = null;
            }

            if (_secondConnection != null)
            {
                try { _secondConnection.Close(); _secondConnection.Dispose(); } catch { }
                _secondConnection = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                lock (_lockObject)
                {
                    try
                    {
                        if (!_committed)
                        {
                            // ✅ CORREGIDO: Rollback de AMBAS transacciones en Dispose
                            try
                            {
                                if (_defaultTransaction != null && _defaultTransaction.Connection != null)
                                    _defaultTransaction.Rollback();
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[UnitOfWork] Rollback default error: {ex.Message}");
                            }

                            try
                            {
                                if (_secondTransaction != null && _secondTransaction.Connection != null)
                                    _secondTransaction.Rollback();
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[UnitOfWork] Rollback second error: {ex.Message}");
                            }
                        }

                        DisposeTransactionsAndConnections();
                        DisposeRepositories();
                    }
                    catch (Exception ex)
                    {
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
            TryDisposeRepository(_aplicativoRepository);
        }

        private void TryDisposeRepository<T>(Lazy<T> lazyRepo)
        {
            if (lazyRepo != null && lazyRepo.IsValueCreated && lazyRepo.Value is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch { }
            }
        }
    }
}