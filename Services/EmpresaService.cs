using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Carwash.API.Context;
using Carwash.API.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Carwash.API.Services;

public sealed class EmpresaService(AppDbContext db)
{
    public async Task<EmpresaDto?> GetAsync(CancellationToken cancellationToken)
    {
        var entity = await db.Empresas.AsNoTracking().SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        return entity is null ? null : JsonSerializer.Deserialize<EmpresaDto>(entity.DatosJson)! with { Revision = entity.Revision };
    }

    public async Task<EmpresaDto> SaveAsync(EmpresaDto request, CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        var json = JsonSerializer.Serialize(normalized with { Revision = 0 });
        var changed = request.Revision == 0
            ? await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Empresa (Id, DatosJson, Revision) VALUES (1, {json}, 1) ON CONFLICT(Id) DO NOTHING", cancellationToken)
            : await db.Empresas.Where(item => item.Id == 1 && item.Revision == request.Revision)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DatosJson, json)
                    .SetProperty(item => item.Revision, item => item.Revision + 1), cancellationToken);
        if (changed != 1) throw new EmpresaException(409, "La empresa fue modificada. Recargue los datos antes de guardar.");
        return normalized with { Revision = request.Revision + 1 };
    }

    public static EmpresaDto Normalize(EmpresaDto request)
    {
        var result = request with
        {
            NombreComercial = Clean(request.NombreComercial), RazonSocial = Clean(request.RazonSocial),
            TipoPersona = Clean(request.TipoPersona), Nit = Clean(request.Nit), DigitoVerificacion = Clean(request.DigitoVerificacion),
            Direccion = Clean(request.Direccion), Municipio = Clean(request.Municipio), Departamento = Clean(request.Departamento),
            Pais = Clean(request.Pais), Telefono = Clean(request.Telefono), Correo = Clean(request.Correo),
            ResponsabilidadesTributarias = Clean(request.ResponsabilidadesTributarias)
        };
        if (!Validator.TryValidateObject(result, new ValidationContext(result), new List<ValidationResult>(), true)
            || result.DigitoVerificacion != CalculateCheckDigit(result.Nit).ToString(CultureInfo.InvariantCulture))
            throw new EmpresaException(400, "Revise los datos de empresa y el digito de verificacion del NIT.");
        return result with { LogoBase64 = NormalizeLogo(result.LogoBase64) };
    }

    public static int CalculateCheckDigit(string nit)
    {
        int[] weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];
        var sum = 0;
        for (var index = 0; index < nit.Length; index++) sum += (nit[nit.Length - 1 - index] - '0') * weights[index];
        var remainder = sum % 11;
        return remainder > 1 ? 11 - remainder : remainder;
    }

    private static string Clean(string? value)
    {
        if (value?.Any(char.IsControl) == true) throw new EmpresaException(400, "Los datos deben ocupar una sola linea.");
        return value?.Trim() ?? "";
    }

    private static string? NormalizeLogo(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!OperatingSystem.IsWindows()) throw new EmpresaException(503, "La carga de logo requiere el servidor Windows.");
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length > 1_048_576) throw new EmpresaException(400, "El logo debe ocupar hasta 1 MB.");
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            if (image.RawFormat.Guid != ImageFormat.Png.Guid && image.RawFormat.Guid != ImageFormat.Jpeg.Guid
                || image.Width > 4096 || image.Height > 4096)
                throw new EmpresaException(400, "Seleccione un logo PNG/JPEG de hasta 4096 pixeles.");
            var scale = Math.Min(1d, 512d / Math.Max(image.Width, image.Height));
            using var bitmap = new Bitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(image, 0, 0, bitmap.Width, bitmap.Height);
            }
            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Png);
            return Convert.ToBase64String(output.ToArray());
        }
        catch (Exception error) when (error is FormatException or ArgumentException or ExternalException)
        { throw new EmpresaException(400, "El logo no es una imagen PNG/JPEG valida."); }
    }
}

public sealed class EmpresaException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}