namespace Carwash.API.DTOs
{
    public class LoginResponseDto
    {
        public bool EsValido { get; set; }
        public string Respuesta { get; set; }
        public UsuarioLoginDto Usuario { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? AccessToken { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? TokenType { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? ExpiresAt { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public int? IdleTimeoutSeconds { get; set; }
    }

    public class UsuarioLoginDto
    {
        public int IdUser { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public int PerfilId { get; set; }
        public bool EsAdministrador { get; set; }
    }
}
