using System.Globalization;
using System.Security.Claims;
using Carwash.API.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Services;

public sealed class AdministradorRequirement : IAuthorizationRequirement { }

public sealed class AdministradorAuthorizationHandler(AppDbContext database) : AuthorizationHandler<AdministradorRequirement>
{
    public const string PolicyName = "Administrador";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdministradorRequirement requirement)
    {
        if (!int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var userId)) return;
        var profile = await database.Usuarios.AsNoTracking().Where(user => user.IdUser == userId && !user.IsDelete)
            .Select(user => (int?)user.PerfilId).SingleOrDefaultAsync();
        if (profile.HasValue && await PerfilAdministrador.EsAdministradorAsync(database, profile.Value))
            context.Succeed(requirement);
    }
}