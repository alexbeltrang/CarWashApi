namespace Carwash.API.Modelos
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("Usuarios")]
    public class Usuario
    {
        [Key]
        [Column("idUser")]
        public int IdUser { get; set; }
        public string UserName { get; set; }
        [Column("password")]
        public string Password { get; set; }
        public string Nombres { get; set; }
        public string Apellidos { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        [Column("isDelete")]
        public bool IsDelete { get; set; }
        public int PerfilId { get; set; }
    }
}
