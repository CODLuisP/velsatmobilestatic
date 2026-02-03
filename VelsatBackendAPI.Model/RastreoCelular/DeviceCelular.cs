using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VelsatMobile.Model.RastreoCelular
{
    public class DeviceCelular
    {
        public string DeviceID { get; set; }
        public string AccountID { get; set; }
        public double LastValidLatitude { get; set; }
        public double LastValidLongitude { get; set; }
        public double LastValidHeading { get; set; }
        public double LastValidSpeed { get; set; }
        public DateTime LastValidDate { get; set; }
        public string? Direccion { get; set; }
        public char Isservice { get; set; }
    }
}
