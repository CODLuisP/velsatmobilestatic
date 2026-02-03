using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VelsatMobile.Model.RastreoCelular
{
    public class TramaCelular
    {
        public string DeviceID { get; set; }
        public DateTime Fecha { get; set; }
        public string AccountID { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double SpeedKPH { get; set; }
        public double Heading { get; set; }
        public string? Address { get; set; }

    }
}