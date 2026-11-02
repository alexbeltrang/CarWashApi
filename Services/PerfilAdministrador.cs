using Carwash.API.Context;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Services
{
    public static class PerfilAdministrador
    {
        public static async Task<bool> EsAdministradorAsync(AppDbContext context, int perfilId)
        {
            var coincidencias = await context.Database
                .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Perfiles WHERE PerfilId = {perfilId} AND COALESCE(isDelete, 0) = 0 AND NombrePerfil = 'Administrador' COLLATE NOCASE")
                .SingleAsync();
            return coincidencias > 0;
        }
    }
}
