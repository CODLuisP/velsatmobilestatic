using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VelsatBackendAPI.Model
{
    public class Account
    {
        public string? AccountID { get; set; }
        public string? Password { get; set; }
        
        //Detalles
        //Empresa
        public string? Description { get; set; }
        public string? Ruc {  get; set; }
        public string? ContactEmail { get; set; }
        public string? ContactPhone { get; set; }

        //Cliente
        public string? Codigo { get; set; }
        public string? Apellidos { get; set; }
        public string? Dni { get; set; }
        public string? Telefono { get; set; }
        public string? Codlan { get; set; }
        public string? Empresa { get; set; }

        //Conductor
        public string? Nombres { get; set; }
        public string? Login { get; set; }

    }
}
