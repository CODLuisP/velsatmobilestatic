using Microsoft.AspNetCore.Mvc;
using VelsatBackendAPI.Data.Repositories;
using VelsatMobile.Data.Repositories;
using VelsatMobile.Model;
using VelsatMobile.Model.RastreoCelular;

namespace VelsatMobile.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AplicativoController : Controller
    {
        private readonly IReadOnlyUnitOfWork _readOnlyUow;  // ✅ Para GET
        private readonly IUnitOfWork _uow;

        public AplicativoController(IReadOnlyUnitOfWork readOnlyUow, IUnitOfWork uow)
        {
            _readOnlyUow = readOnlyUow;
            _uow = uow;
        }

        [HttpGet("serviciosPasajero/{codcliente}")]
        public async Task<IActionResult> ServiciosPasajeros(string codcliente)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(codcliente))
                {
                    return BadRequest("El código de cliente es requerido");
                }

                var servicios = await _readOnlyUow.AplicativoRepository.ServiciosPasajeros(codcliente);

                if (servicios == null || !servicios.Any())
                {
                    return Ok("No se encontraron servicios para el día de hoy");
                }

                return Ok(servicios);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("detalleDestino/{codcliente}")]
        public async Task<IActionResult> GetDetalleDestino(string codcliente)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(codcliente))
                {
                    return BadRequest("El código de cliente es requerido");
                }

                var destinos = await _readOnlyUow.AplicativoRepository.GetDetalleDestino(codcliente);

                if (destinos == null || !destinos.Any())
                {
                    return Ok("No se encontraron destinos activos para el cliente");
                }

                return Ok(destinos);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("detalleConductor/{codtaxi}")]
        public async Task<IActionResult> GetDetalleConductor(string codtaxi)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(codtaxi))
                {
                    return BadRequest("El código de taxi es requerido");
                }

                var conductor = await _readOnlyUow.AplicativoRepository.GetDetalleConductor(codtaxi);

                if (conductor == null)
                {
                    return Ok("No se encontró información del conductor");
                }

                return Ok(conductor);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        // POST: api/servicios/cancelar
        [HttpPost("cancelarServicio")]
        public async Task<IActionResult> CancelarServicio([FromBody] ServicioPasajero servicio)
        {
            try
            {
                // Validar entrada
                if (servicio == null)
                    return BadRequest("Los datos del servicio son requeridos.");

                if (string.IsNullOrWhiteSpace(servicio.Codpedido) || string.IsNullOrWhiteSpace(servicio.Codservicio))
                    return BadRequest("Codpedido y Codservicio son obligatorios.");

                // Llamada al método en el repositorio
                var resultado = await _uow.AplicativoRepository.CancelarServicioAsync(servicio);

                _uow.SaveChanges();

                if (!resultado)
                    return Ok("El servicio no puede ser cancelado. Verifique las condiciones de tiempo.");

                return Ok(new
                {
                    success = true,
                    message = "Servicio cancelado correctamente y correos enviados."
                });
            }
            catch (Exception ex)
            {
                // Manejo de error interno
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Error interno del servidor: {ex.Message}"
                });
            }
        }

        [HttpPost("enviarCalificacion")]
        public async Task<IActionResult> EnviarCalificacion([FromQuery] string valor, [FromQuery] string codtaxi)
        {
            try
            {
                // Validación de parámetros
                if (string.IsNullOrWhiteSpace(valor) || string.IsNullOrWhiteSpace(codtaxi))
                {
                    return BadRequest("El valor de la calificación y el código del taxi son requeridos.");
                }

                // Llamada al método del repositorio
                int filasAfectadas = await _uow.AplicativoRepository.EnviarCalificacion(valor, codtaxi);

                _uow.SaveChanges();

                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Calificación enviada correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("No se encontró el conductor especificado.");
                }

            }
            catch (ArgumentException ex)
            {
                // Errores por parámetros inválidos
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // Errores inesperados
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("serviciosConductor/{codconductor}")]
        public async Task<IActionResult> ServiciosConductor(string codconductor)
        {
            try
            {
                // Validación del parámetro
                if (string.IsNullOrWhiteSpace(codconductor))
                {
                    return BadRequest("El código de conductor es requerido");
                }

                // Llamar al método del repositorio
                var servicios = await _readOnlyUow.AplicativoRepository.ServiciosConductor(codconductor);

                // Validar si hay resultados
                if (servicios == null || !servicios.Any())
                {
                    return Ok("No se encontraron servicios activos para el día de hoy.");
                }

                // Retornar resultado OK
                return Ok(servicios);
            }
            catch (Exception ex)
            {
                // Manejo de errores
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("detalleServicioConductor/{codservicio}")]
        public async Task<IActionResult> GetDetalleServicioConductor(string codservicio)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(codservicio))
                {
                    return BadRequest("El código de servicio es requerido");
                }

                var detalles = await _readOnlyUow.AplicativoRepository.GetDetalleServicioConductor(codservicio);

                if (detalles == null || !detalles.Any())
                {
                    return NotFound("No se encontraron detalles para el servicio especificado");
                }

                return Ok(detalles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("central")]
        public async Task<IActionResult> GetCentral()
        {
            try
            {
                var centrales = await _readOnlyUow.AplicativoRepository.GetCentral();

                if (centrales == null || !centrales.Any())
                {
                    return Ok("No se encontró personal registrado");
                }

                return Ok(centrales);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpPut("cambiarOrdenBatch")]
        public async Task<IActionResult> CambiarOrdenBatch([FromBody] List<CambioOrden> cambios)
        {
            try
            {
                // Llamada al método del repositorio
                int filasAfectadas = await _uow.AplicativoRepository.CambiarOrdenBatch(cambios);

                _uow.SaveChanges();

                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Orden cambiado correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("No se pudo cambiar el orden.");
                }
            }
            catch (ArgumentException ex)
            {
                // Errores por parámetros inválidos
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // Errores inesperados
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpPost("EnviarObservacion")]
        public async Task<IActionResult> EnviarObservacion([FromBody] string observacion, [FromQuery] int codpedido)
        {
            try
            {

                // Llamada al método del repositorio
                int filasAfectadas = await _uow.AplicativoRepository.EnviarObservacion(observacion, codpedido);

                _uow.SaveChanges();

                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Observación enviada correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("Observación no enviada.");
                }
            }
            catch (ArgumentException ex)
            {
                // Errores por parámetros inválidos
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // Errores inesperados
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpPost("ActualizarFechaInicioServicio")]
        public async Task<IActionResult> ActualizarFechaInicioServicio([FromQuery] string codservicio)
        {
            try
            {
                int filasAfectadas = await _uow.AplicativoRepository.ActualizarFechaInicioServicio(codservicio);
                _uow.SaveChanges();

                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Fecha de inicio actualizada correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("Fecha de inicio no actualizada.");
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpPost("ActualizarTaxiServicio")]
        public async Task<IActionResult> ActualizarTaxiServicio([FromQuery] string codservicio, [FromQuery] string codtaxi)
        {
            try
            {
                int filasAfectadas = await _uow.AplicativoRepository.ActualizarTaxiServicio(codservicio, codtaxi);
                _uow.SaveChanges();

                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Taxi actualizado correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("Taxi no actualizado.");
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpPost("ActualizarFechaFinServicio")]
        public async Task<IActionResult> ActualizarFechaFinServicio([FromQuery] string codservicio)
        {
            try
            {
                int filasAfectadas = await _uow.AplicativoRepository.ActualizarFechaFinServicio(codservicio);
                _uow.SaveChanges();

                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Fecha de fin actualizada correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("Fecha de fin no actualizada.");
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpPost("ActualizarTaxiFinServicio")]
        public async Task<IActionResult> ActualizarTaxiFinServicio([FromQuery] string codtaxi)
        {
            try
            {
                int filasAfectadas = await _uow.AplicativoRepository.ActualizarTaxiFinServicio(codtaxi);
                _uow.SaveChanges();

                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Taxi actualizado correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("Taxi no actualizado.");
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("GetEstadoServicio")]
        public async Task<IActionResult> GetEstadoServicio([FromQuery] string codservicio)
        {
            try
            {
                string estado = await _uow.AplicativoRepository.GetEstadoServicio(codservicio);

                if (estado != null)
                {
                    return Ok(estado);
                }
                else
                {
                    return NotFound("Servicio no encontrado.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpPost("SubirPasajero")]
        public async Task<IActionResult> SubirPasajero([FromQuery] string codpedido)
        {
            try
            {
                int filasAfectadas = await _uow.AplicativoRepository.SubirPasajero(codpedido);
                _uow.SaveChanges();
                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Pasajero subido correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("Pasajero no subido.");
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpPost("BajarPasajero")]
        public async Task<IActionResult> BajarPasajero([FromQuery] string codpedido)
        {
            try
            {
                int filasAfectadas = await _uow.AplicativoRepository.BajarPasajero(codpedido);
                _uow.SaveChanges();
                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Pasajero bajado correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("Pasajero no bajado.");
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("PasajerosDisponibles/{codservicio}")]
        public async Task<IActionResult> GetPasajerosDisponibles(string codservicio)
        {
            try
            {
                var cantidad = await _readOnlyUow.AplicativoRepository.PasajerosDisponibles(codservicio);

                return Ok(new
                {
                    codservicio = codservicio,
                    pasajerosDisponibles = cantidad
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("UbiPasajeros/{codservicio}")]
        public async Task<IActionResult> GetUbiPasajeros(string codservicio)
        {
            try
            {
                var ubicaciones = await _readOnlyUow.AplicativoRepository.UbiPasajeros(codservicio);
                return Ok(new
                {
                    codservicio = codservicio,
                    pasajeros = ubicaciones
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        //Rastreo de Celular
        [HttpPost("InsertarTrama")]
        public async Task<IActionResult> InsertarTrama([FromBody] List<TramaCelular> trama)
        {
            try
            {
                if (trama == null || !trama.Any())
                {
                    return BadRequest("La lista de tramas no puede estar vacía.");
                }

                int filasAfectadas = await _uow.AplicativoRepository.InsertarTrama(trama);
                _uow.SaveChanges();

                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Tramas insertadas correctamente.",
                        filasInsertadas = filasAfectadas
                    });
                }
                else
                {
                    return Ok("No se insertaron tramas.");
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpPut("UpdateTramaDevice")]
        public async Task<IActionResult> UpdateTramaDevice([FromBody] DeviceCelular trama)
        {
            try
            {
                if (trama == null)
                {
                    return BadRequest("Los datos del dispositivo no pueden estar vacíos.");
                }

                if (string.IsNullOrWhiteSpace(trama.DeviceID))
                {
                    return BadRequest("El DeviceID es obligatorio.");
                }

                int filasAfectadas = await _uow.AplicativoRepository.UpdateTramaDevice(trama);
                _uow.SaveChanges();

                if (filasAfectadas > 0)
                {
                    return Ok(new
                    {
                        message = "Dispositivo actualizado correctamente.",
                        filasActualizadas = filasAfectadas
                    });
                }
                else
                {
                    return NotFound($"No se encontró el dispositivo con DeviceID: {trama.DeviceID}");
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }
        
        [HttpGet("GetLastTrama")]
        public async Task<IActionResult> GetLastTramaDevice([FromQuery] string accountID)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(accountID))
                {
                    return BadRequest("El usuario es obligatorio.");
                }

                var device = await _uow.AplicativoRepository.GetTramaDevice(accountID);

                if (device != null)
                {
                    return Ok(device);
                }
                else
                {
                    return NotFound($"No se encontró unidades en el usuario: {accountID}");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("GetTramaMitsubishi")]
        public async Task<IActionResult> GetTramaDeviceMitsubishi([FromQuery] string accountID)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(accountID))
                {
                    return BadRequest("El usuario es obligatorio.");
                }

                var device = await _uow.AplicativoRepository.GetTramaDeviceMitsubishi(accountID);

                if (device != null)
                {
                    return Ok(device);
                }
                else
                {
                    return NotFound($"No se encontró unidades en el usuario: {accountID}");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [HttpGet("RouteDetails")]
        public async Task<IActionResult> GetTramaEventdata([FromQuery] string accountID, [FromQuery] string deviceID, [FromQuery] DateTime fechaini, [FromQuery] DateTime fechafin)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(accountID) || string.IsNullOrWhiteSpace(deviceID))
                {
                    return BadRequest("Faltan parámetros");
                }

                var device = await _uow.AplicativoRepository.GetTramaEventdata(accountID, deviceID, fechaini, fechafin);

                if (device != null && device.Any())
                {
                    return Ok(device);
                }
                else
                {
                    return NotFound($"No se encontró registros en las fechas seleccionadas");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

    }
}
