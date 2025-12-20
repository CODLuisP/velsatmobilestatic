using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using VelsatBackendAPI.Data.Repositories;
using VelsatBackendAPI.Model;
using VelsatMobile.Data.Repositories;

namespace VelsatBackendAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LoginController : ControllerBase
    {
        private readonly IReadOnlyUnitOfWork _readOnlyUow;
        private readonly string secretkey;

        public LoginController(IReadOnlyUnitOfWork readOnlyUow, IConfiguration config)
        {
            _readOnlyUow = readOnlyUow;
            secretkey = config.GetSection("settings").GetSection("secretkey").Value;
        }

        [HttpPost("MobileLogin")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Login) || string.IsNullOrEmpty(request.Clave))
            {
                return BadRequest("Los campos están vacíos");
            }

            // Validar que el tipo sea válido
            if (request.Tipo != 'n' && request.Tipo != 'c' && request.Tipo != 'p')
            {
                return BadRequest("Tipo de usuario no válido");
            }

            var account = await _readOnlyUow.AplicativoRepository.ValidateUser(request.Login, request.Clave, request.Tipo);

            if (account != null)
            {
                var token = GenerateLoginToken(account);

                return StatusCode(StatusCodes.Status200OK, new
                {
                    Token = token,
                    Username = request.Login,
                    Account = new
                    {
                        AccountID = account.AccountID,
                        Description = account.Description,
                        Codigo = account.Codigo,
                    }
                });
            }

            return StatusCode(StatusCodes.Status401Unauthorized);
        }

        // ← ELIMINAR TODO ESTE BLOQUE:
        // - IniciarTemporizadorDatosEnTiempoReal()
        // - EnviarDatosEnTiempoReal()
        // - ObtenerDatosCargaDesdeBD()
        // - DetenerTimer()

        private string GenerateLoginToken(Account account)
        {
            var keyBytes = Encoding.ASCII.GetBytes(secretkey);

            var claims = new ClaimsIdentity();
            claims.AddClaim(new Claim(ClaimTypes.NameIdentifier, account.AccountID));

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = claims,
                Expires = DateTime.UtcNow.AddHours(4),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenConfig = tokenHandler.CreateToken(tokenDescriptor);

            string tokenString = tokenHandler.WriteToken(tokenConfig);

            return tokenString;
        }
    }
}