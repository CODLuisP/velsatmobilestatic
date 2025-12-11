using MySql.Data.MySqlClient;
using System;

namespace VelsatBackendAPI.Data
{
    public class MySqlConfiguration
    {
        public MySqlConfiguration(string defaultConnection)
        {
            // ✅ Validar que no sea nula o vacía
            if (string.IsNullOrWhiteSpace(defaultConnection))
                throw new ArgumentNullException(nameof(defaultConnection),
                    "La cadena de conexión por defecto no puede estar vacía");

            // ✅ NORMALIZAR la connection string para evitar duplicados en el pool
            DefaultConnection = NormalizeConnectionString(defaultConnection);
        }

        public string DefaultConnection { get; set; }

        /// <summary>
        /// Normaliza una connection string para garantizar formato consistente.
        /// Esto previene que el MySqlPoolManager considere la misma conexión como diferente.
        /// </summary>
        private static string NormalizeConnectionString(string connectionString)
        {
            try
            {
                // MySqlConnectionStringBuilder automáticamente:
                // - Ordena los parámetros alfabéticamente
                // - Normaliza mayúsculas/minúsculas
                // - Elimina espacios innecesarios
                // - Estandariza el formato
                var builder = new MySqlConnectionStringBuilder(connectionString);
                string normalized = builder.ConnectionString;

                System.Diagnostics.Debug.WriteLine(
                    $"[MySqlConfiguration] Connection string normalizada:\n" +
                    $"  Original: {connectionString.Substring(0, Math.Min(50, connectionString.Length))}...\n" +
                    $"  Normalizada: {normalized.Substring(0, Math.Min(50, normalized.Length))}...");

                return normalized;
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Error al normalizar la cadena de conexión. Verifica que sea válida. Error: {ex.Message}",
                    ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error inesperado al procesar la cadena de conexión: {ex.Message}",
                    ex);
            }
        }
    }
}