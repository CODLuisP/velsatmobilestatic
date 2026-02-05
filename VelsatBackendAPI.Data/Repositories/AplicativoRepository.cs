using Dapper;
using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using VelsatMobile.Model;
using VelsatMobile.Model.RastreoCelular;

namespace VelsatMobile.Data.Repositories
{
    public class AplicativoRepository : IAplicativoRepository
    {
        private readonly IDbConnection _defaultConnection; IDbTransaction _defaultTransaction;

        public AplicativoRepository(IDbConnection defaultconnection, IDbTransaction defaulttransaction)
        {
            _defaultConnection = defaultconnection;
            _defaultTransaction = defaulttransaction;
        }

        //SERVICIOS PASAJERO
        public async Task<IEnumerable<ServicioPasajero>> ServiciosPasajeros(string codcliente)
        {
            string fechaActual = DateTime.Now.AddHours(-5).ToString("dd/MM/yyyy HH:mm");

            string fechaFinal = DateTime.Now.AddHours(24).ToString("dd/MM/yyyy HH:mm");

            string sql = @"SELECT l.direccion, l.distrito, l.wy, l.wx, l.referencia, 
                          su.fecha as fechapasajero, su.orden, su.codpedido, su.codcliente,
                          s.codservicio, s.empresa, s.numero, s.codconductor, 
                          s.destino, s.fecha as fechaservicio, s.tipo, 
                          s.totalpax, s.unidad, s.codusuario, s.status
                   FROM lugarcliente l, servicio s, subservicio su
                   WHERE l.codlugar = su.codubicli 
                     AND su.codservicio = s.codservicio 
                     AND su.codcliente = @Codcliente 
                     AND s.estado <> 'C' 
                     AND su.estado <> 'C'
                     AND STR_TO_DATE(s.fecha, '%d/%m/%Y %H:%i') >= STR_TO_DATE(@Fecha, '%d/%m/%Y %H:%i')
                     AND STR_TO_DATE(s.fecha, '%d/%m/%Y %H:%i') <= STR_TO_DATE(@FechaFinal, '%d/%m/%Y %H:%i') ORDER BY fechaservicio";

            return await _defaultConnection.QueryAsync<ServicioPasajero>(sql, new { Codcliente = codcliente, Fecha = fechaActual, FechaFinal = fechaFinal}, transaction: _defaultTransaction);
        }

        public async Task<IEnumerable<DetalleDestino>> GetDetalleDestino(string codcliente)
        {
            string sql = @"SELECT c.apellidos, c.nombres, l.direccion, l.distrito, l.referencia, l.wy, l.wx FROM cliente c, lugarcliente l WHERE c.codlugar = l.codcli AND c.codcliente = @Codcliente AND l.estado = 'A'";

            return await _defaultConnection.QueryAsync<DetalleDestino>(sql, new { Codcliente = codcliente }, transaction: _defaultTransaction);
        }

        public async Task<DetalleConductor> GetDetalleConductor(string codtaxi)
        {
            string sql = @"SELECT apellidos, nombres, imagen, telefono, dni, calificacion FROM taxi WHERE codtaxi = @Codtaxi";

            return await _defaultConnection.QueryFirstOrDefaultAsync<DetalleConductor>(sql, new { Codtaxi = codtaxi }, transaction: _defaultTransaction);
        }

        public async Task<bool> CancelarServicioAsync(ServicioPasajero servicio)
        {
            TimeZoneInfo peruTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
            DateTimeOffset ahoraPeru = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, peruTimeZone);

            long unixNow = ahoraPeru.ToUnixTimeSeconds();
            long unixServicio = ConvertirHoraAUnix(servicio.Fechaservicio ?? "", peruTimeZone);
            long diferenciaMinutos = (unixServicio - unixNow) / 60;

            bool puedeCancelar =
                (servicio.Tipo == "I" && diferenciaMinutos > 120) ||
                (servicio.Tipo == "S" && diferenciaMinutos > 30);

            if (!puedeCancelar)
                return false;

            string fechaCancelacion = ahoraPeru.ToString("dd/MM/yyyy HH:mm:ss");

            int filasSubservicio = await _defaultConnection.ExecuteAsync(
                @"UPDATE subservicio 
          SET feccancelpas = @Feccancelpas, estado = 'C' 
          WHERE codpedido = @Codpedido",
                new { Feccancelpas = fechaCancelacion, Codpedido = servicio.Codpedido },
                transaction: _defaultTransaction
            );

            int filasServicio = await _defaultConnection.ExecuteAsync(
                @"UPDATE servicio 
          SET alertcancelpas = '1' 
          WHERE codservicio = @Codservicio",
                new { Codservicio = servicio.Codservicio },
                transaction: _defaultTransaction
            );

            if (filasSubservicio > 0 && filasServicio > 0)
                await DecrementarTotalPax(servicio.Codservicio);

            try
            {
                var correos = await GetCorreosCancelarAsync(servicio.Empresa, servicio.Codusuario);
                var pasajero = await GetNombrePasajero(servicio.Codcliente);

                foreach (var correo in correos)
                {
                    await InsertarCorreoColaAsync(
                        correo.Correo,
                        $"Cancelación de Servicio - {(servicio.Tipo == "I" ? "Ingreso" : "Salida")}",
                        GenerarCuerpoCorreoCancelacion(
                            pasajero.Apellidos,
                            pasajero.Codlan,
                            servicio.Tipo == "I" ? "Ingreso" : "Salida",
                            servicio.Numero ?? "N/A",
                            servicio.Fechaservicio ?? "",
                            correo.Proveedor,
                            servicio.Empresa ?? ""
                        )
                    );
                }
            }
            catch (Exception ex)
            {
                // ⚠️ El servicio NO debe fallar si el correo falla
                Console.WriteLine($"ERROR COLA CORREOS: {ex}");
            }

            return true;
        }

        private async Task InsertarCorreoColaAsync(
    string destinatario,
    string asunto,
    string cuerpoHtml)
        {
            const string sql = @"
        INSERT INTO cola_correos
        (destinatario, asunto, cuerpo, enviado, intentos, fecha_creacion)
        VALUES
        (@Destinatario, @Asunto, @Cuerpo, 0, 0, NOW())";

            await _defaultConnection.ExecuteAsync(
                sql,
                new
                {
                    Destinatario = destinatario,
                    Asunto = asunto,
                    Cuerpo = cuerpoHtml
                },
                transaction: _defaultTransaction
            );
        }

        private async Task<int> DecrementarTotalPax(string codservicio)
        {
            if (!int.TryParse(codservicio, out int codservicioInt))
            {
                throw new ArgumentException("El código de servicio no es válido. Debe ser un número entero.");
            }

            string sql = @"UPDATE servicio SET totalpax = CAST(CAST(totalpax AS UNSIGNED) - 1 AS CHAR) WHERE codservicio = @Codservicio AND CAST(totalpax AS UNSIGNED) > 0";

            int filasAfectadas = await _defaultConnection.ExecuteAsync(sql,
                new { Codservicio = codservicioInt },
                transaction: _defaultTransaction
            );

            return filasAfectadas;
        }

        private async Task<Pasajero> GetNombrePasajero(string codcliente)
        {
            if (!int.TryParse(codcliente, out int codClienteInt))
            {
                throw new ArgumentException("El código de cliente no es válido. Debe ser un número entero.");
            }

            string sql = @"SELECT codlan, apellidos from cliente WHERE codcliente = @Codcliente";

            var pasajero = await _defaultConnection.QueryFirstOrDefaultAsync<Pasajero>(sql,
                new { Codcliente = codClienteInt },
                transaction: _defaultTransaction);

            return pasajero;
        }

        private long ConvertirHoraAUnix(string fechaHora, TimeZoneInfo timeZone)
        {
            DateTime fechaLocal = DateTime.ParseExact(
                fechaHora,
                "dd/MM/yyyy HH:mm",
                CultureInfo.InvariantCulture
            );

            return new DateTimeOffset(
                fechaLocal,
                timeZone.GetUtcOffset(fechaLocal)
            ).ToUnixTimeSeconds();
        }


        private async Task<IEnumerable<Correocancelacion>> GetCorreosCancelarAsync(string cliente, string proveedor)
        {
            string sql;
            IEnumerable<Correocancelacion> correos;

            if (cliente == "DHL")
            {
                sql = @"SELECT * FROM correos WHERE cliente = 'DHL'";
                correos = await _defaultConnection.QueryAsync<Correocancelacion>(
                    sql,
                    transaction: _defaultTransaction
                );
            }
            else
            {
                sql = @"SELECT * FROM correos WHERE cliente = 'all' AND proveedor = @Proveedor";
                correos = await _defaultConnection.QueryAsync<Correocancelacion>(
                    sql,
                    new { Proveedor = proveedor },
                    transaction: _defaultTransaction
                );
            }

            return correos;
        }

        private string GenerarCuerpoCorreoCancelacion(string nombrePasajero, string codigo, string tipo, string numeroMovil, string fechaServicio, string proveedor, string empresa)
        {
            return $@"
<html>
    <head>
        <style>
            body {{
                font-family: Arial, sans-serif;
                color: #333;
                margin: 0;
                padding: 0;
                word-wrap: break-word;
            }}
            .container {{
                width: 100%;
                max-width: 600px;
                margin: auto;
                background-color: #f4f4f4;
            }}
            .body {{
                background-color: white;
                border-radius: 0 0 8px 8px;
            }}
        </style>
    </head>

    <body>
        <div class='container'>
            <table width='100%' cellpadding='0' cellspacing='0' style='background-color: #fff; padding: 20px; text-align: center;'>
                <tr>
                    <td style='padding-bottom: 10px;'>
                        <img src='' alt='Logo Velsat' style='max-width: 170px; height: auto;' />
                    </td>
                </tr>
                <tr>
                    <td>
                        <h2 style='margin: 0; font-size: 14px; color: #001d3d;'>CENTRAL DE MONITOREO Y GESTIÓN</h2>
                    </td>
                </tr>
            </table>

            <div class='body'>
                <div style='background-color: #f4f4f4; padding: 30px 20px;'>
                    <div style='background-color: #fff; padding: 25px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1);'>
                        <h2 style='font-size: 18px; color: #d00000; margin: 0 0 20px 0; text-align: center;'>
                            ⚠️ CANCELACIÓN DE SERVICIO
                        </h2>
                        
                        <p style='font-size: 13px; color: #333; margin-bottom: 20px; text-align: center;'>
                            Estimado/a, le informamos que se ha cancelado el siguiente servicio:
                        </p>

                        <div style='background-color: #f8f9fa; padding: 20px; border-radius: 6px; margin-bottom: 15px;'>
                            <table style='width: 100%; border-collapse: collapse;'>
                                <tr>
                                    <td style='padding: 8px 0; font-size: 12px; color: #001d3d; font-weight: bold; width: 40%;'>Tipo de Servicio:</td>
                                    <td style='padding: 8px 0; font-size: 12px; color: #333;'>{tipo}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0; font-size: 12px; color: #001d3d; font-weight: bold;'>Código:</td>
                                    <td style='padding: 8px 0; font-size: 12px; color: #333;'>{codigo}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0; font-size: 12px; color: #001d3d; font-weight: bold;'>Pasajero:</td>
                                    <td style='padding: 8px 0; font-size: 12px; color: #333;'>{nombrePasajero}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0; font-size: 12px; color: #001d3d; font-weight: bold;'>Número Móvil:</td>
                                    <td style='padding: 8px 0; font-size: 12px; color: #333;'>{numeroMovil}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0; font-size: 12px; color: #001d3d; font-weight: bold;'>Fecha del Servicio:</td>
                                    <td style='padding: 8px 0; font-size: 12px; color: #333;'>{fechaServicio}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0; font-size: 12px; color: #001d3d; font-weight: bold;'>Proveedor:</td>
                                    <td style='padding: 8px 0; font-size: 12px; color: #333;'>{proveedor}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px 0; font-size: 12px; color: #001d3d; font-weight: bold;'>Empresa:</td>
                                    <td style='padding: 8px 0; font-size: 12px; color: #333;'>{empresa}</td>
                                </tr>
                            </table>
                        </div>
                    </div>
                </div>

                <div style='width: 100%;'>
                    <div style='width: 80%; margin: 0 auto;'>
                        <hr style='border: none; border-top: 0.5px solid black; margin: 20px 0;' />
                        <p style='text-align: center; font-size: 12px;'>
                            Estamos comprometidos con brindarle a usted el mejor servicio. Gracias por su preferencia.
                        </p>
                        <hr style='border: none; border-top: 0.5px solid black; margin: 20px 0;' />
                    </div>
                </div>

                <div style='background-color: #fff; text-align: center; padding: 20px; color: #001d3d; font-size: 11px; font-family: Arial, sans-serif;'>
                    <p style='margin: 5px 0;'><strong>Central de Monitoreo y Gestión</strong></p>
                    <p style='margin: 5px 0;'>989112975 - 952075325</p>
                    <p style='margin: 5px 0;'>cmyg@velsat.com.pe</p>
                    <p style='margin: 5px 0;'>Av. Juan Pablo Fernandini 1439 Int. 603F, Pueblo Libre, Lima.</p>
                    <hr style='border: none; border-top: 1px solid #001d3d; margin: 15px 0;' />
                    <p style='margin: 0;'>© {DateTime.Now.Year} Todos los derechos reservados.</p>
                </div>
            </div>
        </div>
    </body>
</html>";
        }

        public async Task<int> EnviarCalificacion(string valor, string codtaxi)
        {
            // Convertir a int de forma segura
            if (!int.TryParse(codtaxi, out int codTaxiInt))
            {
                throw new ArgumentException("El código de conductor no es válido. Debe ser un número entero.");
            }

            string sql = @"UPDATE taxi SET calificacion = @Calificacion where codtaxi = @Codtaxi";

            int filasAfectadas = await _defaultConnection.ExecuteAsync(sql, new { Calificacion = valor, Codtaxi = codTaxiInt }, transaction: _defaultTransaction);

            return filasAfectadas;
        }

        //SERVICIOS CONDUCTOR
        public async Task<IEnumerable<ServicioConductor>> ServiciosConductor(string codconductor)
        {
            string fechaActual = DateTime.Now.AddHours(-5).ToString("dd/MM/yyyy HH:mm");
            string fechaFinal = DateTime.Now.AddHours(24).ToString("dd/MM/yyyy HH:mm");

            string sqlServicios = @"SELECT s.codservicio, s.empresa, s.numero, s.codconductor, s.destino, s.fecha AS fechaservicio, s.status, s.tipo, s.grupo, s.totalpax, s.unidad, s.codusuario FROM servicio s WHERE s.codconductor = @Codconductor 
            AND s.estado <> 'C' AND STR_TO_DATE(s.fecha, '%d/%m/%Y %H:%i') >= STR_TO_DATE(@Fecha, '%d/%m/%Y %H:%i') AND STR_TO_DATE(s.fecha, '%d/%m/%Y %H:%i') <= STR_TO_DATE(@FechaFinal, '%d/%m/%Y %H:%i') ORDER BY fechaservicio";

            var servicios = (await _defaultConnection.QueryAsync<SCAux1>(sqlServicios, new {Codconductor = codconductor, Fecha = fechaActual, FechaFinal = fechaFinal}, transaction: _defaultTransaction)).ToList();

            if (!servicios.Any())
                return new List<ServicioConductor>();

            //Obtener los codservicios para la segunda consulta
            var codservicios = servicios.Select(s => s.Codservicio).ToList();

            if (codservicios.Count == 0)
                return new List<ServicioConductor>();

            string sqlFechas = @"SELECT codservicio, MIN(fecha) AS fechapasajero FROM subservicio WHERE codservicio IN @Codservicios GROUP BY codservicio";

            var fechas = (await _defaultConnection.QueryAsync<SCAux2>(sqlFechas, new { Codservicios = codservicios }, transaction: _defaultTransaction)).ToList();

            var resultado = from s in servicios
                            join f in fechas on s.Codservicio equals f.Codservicio into sf
                            from f in sf.DefaultIfEmpty()
                            select new ServicioConductor
                            {
                                Codservicio = s.Codservicio,
                                Fechapasajero = f?.Fechapasajero ?? "", // si no tiene, queda vacío
                                Empresa = s.Empresa,
                                Numero = s.Numero,
                                Codconductor = s.Codconductor,
                                Destino = s.Destino,
                                Fechaservicio = s.Fechaservicio,
                                Status = s.Status,
                                Tipo = s.Tipo,
                                Grupo = s.Grupo,
                                Totalpax = s.Totalpax,
                                Unidad = s.Unidad,
                                Codusuario = s.Codusuario
                            };

            return resultado;
        }

        public async Task<IEnumerable<DetalleServicioConductor>> GetDetalleServicioConductor(string codservicio)
        {
            string sql = @"SELECT c.apellidos, c.nombres, c.dni, c.telefono, c.codlan, l.direccion, l.distrito, l.referencia, l.wy, l.wx, su.orden, su.codpedido, su.fecha as fechapasajero, su.codcliente, su.estado from subservicio su, cliente c, lugarcliente l WHERE c.codcliente = su.codcliente AND l.codlugar = su.codubicli AND su.codservicio = @Codservicio AND su.estado <> 'C' order by su.orden";

            var detalles = await _defaultConnection.QueryAsync<DetalleServicioConductor>(sql, new {Codservicio = codservicio}, transaction: _defaultTransaction);

            return detalles;
        }

        public async Task<IEnumerable<Central>> GetCentral()
        {
            string sql = @"SELECT * FROM central where habilitado = '1'";

            return await _defaultConnection.QueryAsync<Central>(sql, transaction: _defaultTransaction);
        }

        public async Task<int> CambiarOrdenBatch(List<CambioOrden> cambios)
        {
            int totalFilas = 0;
            foreach (var cambio in cambios)
            {
                string sql = @"UPDATE subservicio SET orden = @Orden WHERE codpedido = @Codpedido";
                totalFilas += await _defaultConnection.ExecuteAsync(sql, cambio, transaction: _defaultTransaction);
            }
            return totalFilas;
        }

        public async Task<int> EnviarObservacion(string observacion, int codpedido)
        {
            string sql = @"UPDATE subservicio SET observacion = @Observacion WHERE codpedido = @Codpedido";

            int filasAfectadas = await _defaultConnection.ExecuteAsync(sql, new { Observacion = observacion, Codpedido = codpedido }, transaction: _defaultTransaction);

            return filasAfectadas;
        }

        public async Task<int> ActualizarFechaInicioServicio(string codservicio)
        {
            TimeZoneInfo peruTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
            DateTime horaPeruana = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, peruTimeZone);

            string sql = @"UPDATE servicio SET fechaini = @Fechaini, status = '2' WHERE codservicio = @Codservicio";
            int resultado = await _defaultConnection.ExecuteAsync(sql,
                new
                {
                    Fechaini = horaPeruana.ToString("dd/MM/yyyy HH:mm"),
                    Codservicio = codservicio
                });

            // Obtener DeviceID (unidad)
            string sqlObtenerUnidad = @"SELECT unidad FROM servicio WHERE codservicio = @Codservicio";
            string deviceID = await _defaultConnection.QueryFirstOrDefaultAsync<string>(sqlObtenerUnidad,
                new { Codservicio = codservicio });

            // Si unidad no es null, actualizar device
            if (!string.IsNullOrEmpty(deviceID))
            {
                string sqlActualizarDevice = @"UPDATE device SET isservice = '1' WHERE deviceID = @DeviceID";
                await _defaultConnection.ExecuteAsync(sqlActualizarDevice,
                    new { DeviceID = deviceID });
            }

            return resultado;
        }

        public async Task<int> ActualizarTaxiServicio(string codservicio, string codtaxi)
        {
            string sql = @"UPDATE taxi SET servicioactual = @Servicioactual WHERE codtaxi = @Codtaxi";
            return await _defaultConnection.ExecuteAsync(sql,
                new
                {
                    Servicioactual = codservicio,
                    Codtaxi = int.Parse(codtaxi)
                });
        }

        public async Task<int> ActualizarFechaFinServicio(string codservicio)
        {
            TimeZoneInfo peruTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
            DateTime horaPeruana = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, peruTimeZone);

            string sql = @"UPDATE servicio SET fechafin = @Fechafin, status = '3' WHERE codservicio = @Codservicio";
            int resultado = await _defaultConnection.ExecuteAsync(sql,
                new
                {
                    Fechafin = horaPeruana.ToString("dd/MM/yyyy HH:mm"),
                    Codservicio = codservicio
                });

            // Obtener DeviceID (unidad)
            string sqlObtenerUnidad = @"SELECT unidad FROM servicio WHERE codservicio = @Codservicio";
            string deviceID = await _defaultConnection.QueryFirstOrDefaultAsync<string>(sqlObtenerUnidad,
                new { Codservicio = codservicio });

            // Si unidad no es null, actualizar device
            if (!string.IsNullOrEmpty(deviceID))
            {
                string sqlActualizarDevice = @"UPDATE device SET isservice = '0' WHERE deviceID = @DeviceID";
                await _defaultConnection.ExecuteAsync(sqlActualizarDevice,
                    new { DeviceID = deviceID });
            }

            return resultado;
        }

        public async Task<int> ActualizarTaxiFinServicio(string codtaxi)
        {
            string sql = @"UPDATE taxi SET servicioactual = null WHERE codtaxi = @Codtaxi";
            return await _defaultConnection.ExecuteAsync(sql,
                new
                {
                    Codtaxi = int.Parse(codtaxi)
                });
        }

        public async Task<int> SubirPasajero(string codpedido)
        {
            // Obtener hora actual de Perú
            string fechaFormateada = ObtenerFechaHoraPeruFormateada();

            string sql = @"UPDATE subservicio SET fechainicio = @Fechaini, estado = 'A' WHERE codpedido = @Codpedido";

            return await _defaultConnection.ExecuteAsync(sql,
                new
                {
                    Fechaini = fechaFormateada,
                    Codpedido = int.Parse(codpedido)
                }
            );
        }

        public async Task<int> BajarPasajero(string codpedido)
        {
            // Obtener hora actual de Perú
            string fechaFormateada = ObtenerFechaHoraPeruFormateada();

            string sql = @"UPDATE subservicio SET fechafin = @Fechafin, estado = 'N' WHERE codpedido = @Codpedido";

            return await _defaultConnection.ExecuteAsync(sql,
                new
                {
                    Fechafin = fechaFormateada,
                    Codpedido = int.Parse(codpedido)
                }
            );
        }

        private string ObtenerFechaHoraPeruFormateada()
        {
            TimeZoneInfo peruTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
            DateTime fechaActualPeru = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, peruTimeZone);
            return fechaActualPeru.ToString("dd/MM/yyyy HH:mm");
        }

        public async Task<int> PasajerosDisponibles(string codservicio)
        {
            string sql = @"SELECT COUNT(*) FROM subservicio WHERE estado != 'C' AND codservicio = @Codservicio";

            return await _defaultConnection.ExecuteScalarAsync<int>(sql,
                new
                {
                    Codservicio = codservicio
                }
            );
        }

        public async Task<IEnumerable<UbiPasajero>> UbiPasajeros(string codservicio)
        {
            string sql = @"
        SELECT su.codpedido, su.codubicli, su.codcliente, su.estado, su.orden, 
               c.apellidos, c.codlugar, l.direccion, l.distrito, l.wy, l.wx
        FROM subservicio su, cliente c, lugarcliente l 
        WHERE su.codcliente = c.codcliente 
          AND c.codlugar = l.codcli 
          AND su.estado != 'C' 
          AND c.estadocuenta = 'A' 
          AND l.estado = 'A' 
          AND su.codservicio = @Codservicio 
        ORDER BY orden";

            return await _defaultConnection.QueryAsync<UbiPasajero>(sql,
                new
                {
                    Codservicio = codservicio
                }
            );
        }

        //Rastreo con Celular
        //INSERTAR EN EVENTDATA
        public async Task<int> InsertarTrama(List<TramaCelular> trama)
        {
            if (trama == null || !trama.Any())
                return 0;

            string sql = @"INSERT INTO eventdata (deviceID, fecha, accountID, latitude, longitude, speedKPH, heading, address) VALUES (@DeviceID, @Fecha, @AccountID, @Latitude, @Longitude, @SpeedKPH, @Heading, @Address)";

            return await _defaultConnection.ExecuteAsync(sql, trama);
        }

        //ACTUALIZAR EN DEVICE
        public async Task<int> UpdateTramaDevice(DeviceCelular trama)
        {
            string sql = @"UPDATE device SET accountID = @AccountID, lastValidLatitude = @LastValidLatitude, lastValidLongitude = @LastValidLongitude, lastValidHeading = @LastValidHeading, lastValidSpeed = @LastValidSpeed, lastValidDate = @LastValidDate, direccion = @Direccion WHERE deviceID = @DeviceID";

            return await _defaultConnection.ExecuteAsync(sql, trama);
        }

        //PARA CONSUMIR CADA 5 SEG CON POOLLING DE DEVICE
        public async Task<IEnumerable<DeviceCelular>> GetTramaDevice(string accountID)
        {
            string sql = @"SELECT deviceID, accountID, lastValidLatitude, lastValidLongitude, lastValidHeading, lastValidSpeed, lastValidDate, direccion FROM device WHERE accountID = @AccountID AND isservice = '1'";

            return await _defaultConnection.QueryAsync<DeviceCelular>(sql, new { AccountID = accountID });
        }

        //PARA CONSUMIR CADA 5 SEG CON POOLLING DE DEVICE MITSUBISHI
        public async Task<IEnumerable<DeviceCelular>> GetTramaDeviceMitsubishi(string accountID)
        {
            string sql = @"SELECT deviceID, accountID, lastValidLatitude, lastValidLongitude, lastValidHeading, lastValidSpeed, lastValidDate, direccion FROM device WHERE accountID = @AccountID";

            return await _defaultConnection.QueryAsync<DeviceCelular>(sql, new { AccountID = accountID });
        }

        //PARA DETALLE RECORRIDO, DE EVENTDATA
        public async Task<IEnumerable<TramaCelular>> GetTramaEventdata(string accountID, string deviceID, DateTime fechaini, DateTime fechafin)
        {
            string sql = @"SELECT accountID, deviceID, fecha, latitude, longitude, speedKPH, heading, address FROM eventdata WHERE accountID = @AccountID AND deviceID = @DeviceID AND fecha >= @Fechaini AND fecha <= @Fechafin";

            return await _defaultConnection.QueryAsync<TramaCelular>(sql, new { AccountID = accountID, DeviceID = deviceID, Fechaini = fechaini, Fechafin = fechafin });
        }
    }
}
