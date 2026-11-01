using DieselRoom.Domain.Enums;
using DieselRoom.Infrastructure.Services;

namespace DieselRoom.Tests;

public class StaffAuthTests
{
    public StaffAuthTests() => StaffAuthService.ResetPinThrottleForTests();

    [Fact]
    public async Task Admin_ByConfiguredVkId_OrMasterKey()
    {
        using var t = new TestDb();
        var auth = t.Create().StaffAuth;

        Assert.Equal(StaffRole.Admin, (await auth.ResolveAsync("1001", null, null, "k1")).Role);
        Assert.Equal(StaffRole.Admin, (await auth.ResolveAsync("id1001", null, null, "k1")).Role);
        Assert.Equal(StaffRole.Admin, (await auth.ResolveAsync("5", "master-key-for-tests-123", null, "k1")).Role);
        Assert.Equal(StaffRole.Guest, (await auth.ResolveAsync("5", "wrong-master-key-0000", null, "k1")).Role);
    }

    [Fact]
    public async Task ShortMasterKey_IsDisabled()
    {
        using var t = new TestDb();
        t.StaffOptions.MasterKey = "short";
        Assert.Equal(StaffRole.Guest, (await t.Create().StaffAuth.ResolveAsync("5", "short", null, "k")).Role);
    }

    [Fact]
    public async Task Staff_ByVkId_ByEnvList_OrByPin_InactiveIgnored()
    {
        using var t = new TestDb();
        t.AddStaff("Бармен Олег", vkId: "2002");
        t.AddStaff("Хостес Ира", pin: "4321");
        t.AddStaff("Уволен", vkId: "2003", active: false);
        t.AddStaff("Арт-директор Фёдор", StaffRole.Admin, vkId: "2004");
        t.StaffOptions.StaffVkIds.Add("2005");
        var auth = t.Create().StaffAuth;

        var oleg = await auth.ResolveAsync("2002", null, null, "a");
        Assert.Equal((StaffRole.Staff, "Бармен Олег"), (oleg.Role, oleg.Name));

        var ira = await auth.ResolveAsync("9999", null, "4321", "b");
        Assert.Equal((StaffRole.Staff, "Хостес Ира"), (ira.Role, ira.Name));
        Assert.NotNull(ira.StaffMemberId);

        Assert.Equal(StaffRole.Guest, (await auth.ResolveAsync("2003", null, null, "c")).Role);
        Assert.Equal(StaffRole.Admin, (await auth.ResolveAsync("2004", null, null, "d")).Role);
        Assert.Equal(StaffRole.Staff, (await auth.ResolveAsync("2005", null, null, "e")).Role);
        Assert.Equal(StaffRole.Guest, (await auth.ResolveAsync("3000", null, null, "f")).Role);
    }

    [Fact]
    public async Task WrongPins_LockOutAfterFiveAttempts_EvenForCorrectPin()
    {
        using var t = new TestDb();
        t.AddStaff("Официант", pin: "1111");
        var auth = t.Create().StaffAuth;

        for (var i = 0; i < StaffAuthService.MaxPinFailures; i++)
        {
            Assert.Equal(StaffRole.Guest, (await auth.ResolveAsync("77", null, "0000", "vk:77")).Role);
        }
        Assert.True(StaffAuthService.IsPinLocked("vk:77"));
        Assert.Equal(StaffRole.Guest, (await auth.ResolveAsync("77", null, "1111", "vk:77")).Role);

        // Другой ключ (другой VK ID) не заблокирован
        Assert.Equal(StaffRole.Staff, (await auth.ResolveAsync("78", null, "1111", "vk:78")).Role);
    }

    [Fact]
    public void PinHash_IsDeterministic_AndNotThePinItself()
    {
        using var t = new TestDb();
        var auth = t.Create().StaffAuth;
        Assert.Equal(auth.HashPin("123456"), auth.HashPin("123456"));
        Assert.NotEqual(auth.HashPin("123456"), auth.HashPin("123457"));
        Assert.DoesNotContain("123456", auth.HashPin("123456"));
        Assert.False(StaffAuthService.IsValidPinFormat("12a4"));
        Assert.False(StaffAuthService.IsValidPinFormat("123"));
        Assert.True(StaffAuthService.IsValidPinFormat("12345678"));
    }
}
