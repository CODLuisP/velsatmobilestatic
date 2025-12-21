using Dapper;
using System;
using System.Data;
using System.Threading.Tasks;
using VelsatBackendAPI.Model;

namespace VelsatBackendAPI.Data.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly IDbConnection _defaultConnection;
        private readonly IDbTransaction _defaultTransaction;

        public UserRepository(IDbConnection defaultConnection, IDbTransaction defaultTransaction)
        {
            _defaultConnection = defaultConnection;
            _defaultTransaction = defaultTransaction;
        }

        public async Task<Account> GetDetails(string accountID, char tipo)
        {
            string sql = tipo switch
            {
                'n' => @"
                    SELECT description, ruc, contactEmail, contactPhone 
                    FROM usuarios 
                    WHERE accountID = @AccountID",

                'p' => @"
                    SELECT apellidos, dni, telefono, codlan, empresa 
                    FROM cliente 
                    WHERE codlan = @AccountID",

                'c' => @"
                    SELECT apellidos, nombres, dni, telefono, login 
                    FROM taxi 
                    WHERE login = @AccountID",

                _ => throw new ArgumentException("Tipo de usuario no válido", nameof(tipo))
            };

            return await _defaultConnection.QueryFirstOrDefaultAsync<Account>(
                sql,
                new { AccountID = accountID },
                transaction: _defaultTransaction); // ✅ Agregar transaction
        }

        public async Task<bool> UpdateUser(Account account, char tipo)
        {
            string sql = tipo switch
            {
                'n' => @"
                    UPDATE usuarios 
                    SET description = @Description, contactEmail = @ContactEmail, 
                        contactPhone = @ContactPhone 
                    WHERE accountID = @AccountID",

                'p' => @"
                    UPDATE cliente 
                    SET apellidos = @Apellidos, codlan = @Codlan, telefono = @Telefono 
                    WHERE codlan = @AccountID",

                'c' => @"
                    UPDATE taxi 
                    SET apellidos = @Apellidos, login = @Login, telefono = @Telefono, nombres = NULL
                    WHERE login = @AccountID",

                _ => throw new ArgumentException("Tipo de usuario no válido", nameof(tipo))
            };

            var parameters = new
            {
                Description = account.Description,
                ContactEmail = account.ContactEmail,
                ContactPhone = account.ContactPhone,
                Apellidos = account.Apellidos,
                Codlan = account.Codlan,
                Telefono = account.Telefono,
                Login = account.Login,
                AccountID = account.AccountID
            };

            var rowsAffected = await _defaultConnection.ExecuteAsync(
                sql,
                parameters,
                transaction: _defaultTransaction); // ✅ Agregar transaction

            // ❌ NO hacer commit aquí - lo hace el controlador
            return rowsAffected > 0;
        }

        public async Task<bool> UpdatePassword(string username, string password, char tipo)
        {
            string sql = tipo switch
            {
                'n' => @"UPDATE usuarios SET password = @Password WHERE accountID = @AccountID",
                'p' => @"UPDATE cliente SET clave = @Clave WHERE codlan = @AccountID",
                'c' => @"UPDATE taxi SET clave = @Clave WHERE login = @AccountID",
                _ => throw new ArgumentException("Tipo de usuario no válido", nameof(tipo))
            };

            var parameters = new
            {
                AccountID = username,
                Password = password,
                Clave = password
            };

            var rowsAffected = await _defaultConnection.ExecuteAsync(
                sql,
                parameters,
                transaction: _defaultTransaction); // ✅ Agregar transaction

            // ❌ NO hacer commit aquí - lo hace el controlador
            return rowsAffected > 0;
        }

        public async Task<Account> ValidateUser(string login, string clave, char tipo)
        {
            string sql = tipo switch
            {
                'n' => @"
                    SELECT accountID, password, description 
                    FROM usuarios 
                    WHERE accountID = @Login AND password = @Clave",

                'p' => @"
                    SELECT codcliente AS Codigo, codlan AS AccountID, clave AS Password, 
                           apellidos AS Description 
                    FROM cliente 
                    WHERE codlan = @Login AND clave = @Clave AND estadocuenta = 'A'",

                'c' => @"
                    SELECT codtaxi AS Codigo, login AS AccountID, clave AS Password, 
                           CONCAT(apellidos, ' ', nombres) AS Description 
                    FROM taxi 
                    WHERE login = @Login AND clave = @Clave AND estado = 'A'",

                _ => throw new ArgumentException("Tipo de usuario no válido", nameof(tipo))
            };

            return await _defaultConnection.QueryFirstOrDefaultAsync<Account>(
                sql,
                new { Login = login, Clave = clave },
                transaction: _defaultTransaction); // ✅ Agregar transaction
        }
    }
}