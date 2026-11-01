using DieselRoom.Api.Services;
using DieselRoom.Domain.Enums;
using DieselRoom.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DieselRoom.Api.Filters;

/// <summary>
/// Проверяет подпись VK и определяет роль: Guest / Staff (официант, бармен, хостес) / Admin (Стас, Фёдор).
/// <see cref="MinRole"/> — минимальная роль для доступа к эндпоинту.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class VkAuthorizeAttribute : Attribute, IAsyncActionFilter
{
    public const string VkUserIdItemKey = "VkUserId";
    public const string StaffItemKey = "StaffIdentity";

    public StaffRole MinRole { get; set; } = StaffRole.Guest;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var validator = http.RequestServices.GetRequiredService<IVkAuthValidator>();
        var result = validator.Validate(http);

        if (!result.IsValid || string.IsNullOrWhiteSpace(result.VkUserId))
        {
            context.Result = new UnauthorizedObjectResult(new { message = result.ErrorMessage ?? "Ошибка авторизации через VK." });
            return;
        }

        var staffAuth = http.RequestServices.GetRequiredService<StaffAuthService>();
        var pin = http.Request.Headers["X-Staff-Pin"].FirstOrDefault();
        var masterKey = http.Request.Headers["X-Admin-Key"].FirstOrDefault();
        var throttleKey = $"vk:{StaffAuthService.NormalizeVkId(result.VkUserId)}";

        var identity = await staffAuth.ResolveAsync(result.VkUserId, masterKey, pin, throttleKey, http.RequestAborted);

        http.Items[VkUserIdItemKey] = StaffAuthService.NormalizeVkId(result.VkUserId);
        http.Items[StaffItemKey] = identity;

        if (identity.Role < MinRole)
        {
            var logger = http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("DieselRoom.Security");
            logger?.LogWarning("[SECURITY] Action=StaffAccessDenied VkId={VkId} Path={Path} Required={Required} PinProvided={Pin}",
                result.VkUserId, http.Request.Path.Value, MinRole, !string.IsNullOrEmpty(pin));

            var locked = !string.IsNullOrEmpty(pin) && StaffAuthService.IsPinLocked(throttleKey);
            context.Result = new ObjectResult(new
            {
                message = locked
                    ? "Слишком много неверных ПИН-кодов. Попробуйте через 15 минут."
                    : MinRole == StaffRole.Admin
                        ? "Доступ запрещён: требуются права администратора."
                        : "Доступ запрещён: требуется вход сотрудника.",
            })
            {
                StatusCode = locked ? StatusCodes.Status429TooManyRequests : StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
