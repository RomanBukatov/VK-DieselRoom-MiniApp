namespace DieselRoom.Api.Models;

public class VkOptions
{
    public const string SectionName = "VkOptions";

    public long AppId { get; set; }
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>Сообщество DIESEL ROOM: https://vk.ru/diesel_room.</summary>
    public long GroupId { get; set; } = 34613849;
    public string CommunityToken { get; set; } = string.Empty;
    public string CallbackConfirmationCode { get; set; } = string.Empty;
    public string CallbackSecretKey { get; set; } = string.Empty;
    public List<string> AdminVkIds { get; set; } = new();
    public List<string> StaffVkIds { get; set; } = new();
    public bool RequireValidation { get; set; } = true;
}
