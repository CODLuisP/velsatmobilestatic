using DocumentFormat.OpenXml.Drawing.Charts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VelsatBackendAPI.Model;
using VelsatMobile.Model;

namespace VelsatMobile.Data.Repositories
{
    public interface IAplicativoRepository
    {
        Task<IEnumerable<ServicioPasajero>> ServiciosPasajeros(string codcliente);

        Task<IEnumerable<DetalleDestino>> GetDetalleDestino(string codcliente);

        Task<DetalleConductor> GetDetalleConductor(string codtaxi);

        Task<bool> CancelarServicioAsync(ServicioPasajero servicio);

        Task<int> EnviarCalificacion(string valor, string codtaxi);

        Task<IEnumerable<ServicioConductor>> ServiciosConductor(string codconductor);

        Task<IEnumerable<DetalleServicioConductor>> GetDetalleServicioConductor(string codservicio);

        Task<IEnumerable<Central>> GetCentral();

        Task<int> CambiarOrdenBatch(List<CambioOrden> cambios);

        Task<int> EnviarObservacion(string observacion, int codpedido);

        Task<int> ActualizarFechaInicioServicio(string codservicio);
        Task<int> ActualizarTaxiServicio(string codservicio, string codtaxi);

        Task<int> ActualizarFechaFinServicio(string codservicio);
        Task<int> ActualizarTaxiFinServicio(string codtaxi);

        Task<int> SubirPasajero(string codpedido);

        Task<int> BajarPasajero(string codpedido);

        Task<int> PasajerosDisponibles(string codservicio);

        Task<IEnumerable<UbiPasajero>> UbiPasajeros(string codservicio);
    }
}
