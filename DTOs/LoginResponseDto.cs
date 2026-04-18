namespace Carwash.API.DTOs
{
    public class LoginResponseDto
    {
        public bool EsValido { get; set; }
        public string Respuesta { get; set; }
        public UsuarioLoginDto Usuario { get; set; }
    }

    public class UsuarioLoginDto
    {
        public int IdUser { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public int PerfilId { get; set; }
    }
}
