using DieselRoom.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DieselRoom.Api.Controllers;

/// <summary>Разбор и проверка загружаемых фото: тип определяем по содержимому файла, а не по словам клиента.</summary>
public static class MediaFiles
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public static (string ContentType, byte[] Bytes)? ParseDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return null;
        var comma = dataUrl.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        if (comma < 0) return null;

        var base64 = dataUrl[(comma + ";base64,".Length)..];
        if (base64.Length > (MaxBytes / 3 + 1) * 4) return null;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        var contentType = Detect(bytes);
        return contentType == null || bytes.Length > MaxBytes ? null : (contentType, bytes);
    }

    private static string? Detect(byte[] b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }
}

/// <summary>Фото подарков. Без авторизации: тег img не передаёт подпись ВК, а персональных данных здесь нет.</summary>
[ApiController]
[Route("api/media")]
public class MediaController : ControllerBase
{
    private readonly AppDbContext _db;

    public MediaController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var file = await _db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (file == null) return NotFound();

        // Файл под этим адресом не меняется: новое фото получает новый адрес
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Bytes, file.ContentType);
    }
}
