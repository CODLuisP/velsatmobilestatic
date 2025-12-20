using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VelsatBackendAPI.Model
{
    public class LoginRequest //Validar las credenciales del inicio de sesión
    {
        [Required(ErrorMessage = "El campo usuario es obligatorio")]
        public string Login { get; set; }

        [Required(ErrorMessage = "El campo contraseña es obligatorio")]
        public string Clave { get; set; }
        public char Tipo { get; set; }

    }
}
