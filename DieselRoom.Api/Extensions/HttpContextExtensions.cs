using DieselRoom.Api.Filters;
using DieselRoom.Infrastructure.Services;

namespace DieselRoom.Api.Extensions;

public static class HttpContextExtensions
{
    /// <summary>VK ID, проверенный фильтром [VkAuthorize] по подписи параметров запуска.</summary>
    public static string? GetVkUserId(this HttpContext? context) =>
        context != null && context.Items.TryGetValue(VkAuthorizeAttribute.VkUserIdItemKey, out var val) && val is string vkId ? vkId : null;

    /// <summary>Роль и имя сотрудника, определённые фильтром [VkAuthorize].</summary>
    public static StaffIdentity GetStaff(this HttpContext? context) =>
        context != null && context.Items.TryGetValue(VkAuthorizeAttribute.StaffItemKey, out var val) && val is StaffIdentity staff
            ? staff
            : StaffIdentity.Guest;
}
