namespace DieselRoom.Domain.Enums;

/// <summary>Статус брони стола.</summary>
public enum BookingStatus
{
    Pending = 0,
    Confirmed = 1,
    Rejected = 2,
    Completed = 3,
    Cancelled = 4,
}

/// <summary>Формат брони.</summary>
public enum BookingFormat
{
    Hall = 0,
    Karaoke = 1,
    Party = 2,
    Banquet = 3,
}

/// <summary>Статус заказа подарка из магазина.</summary>
public enum ShopOrderStatus
{
    Pending = 0,
    Issued = 1,
    Cancelled = 2,
    Expired = 3,
}

/// <summary>Тип визита по закрытому чеку.</summary>
public enum VisitType
{
    Regular = 0,
    BusinessLunch = 1,
    PartyKaraoke = 2,
}

/// <summary>Роль персонала. Guest — обычный гость без доступа к панели.</summary>
public enum StaffRole
{
    Guest = 0,
    Staff = 1,
    Admin = 2,
}

/// <summary>Переходящая привилегия по месту в «Гонке недели» (ТЗ п. 4.1).</summary>
public enum PrivilegeTier
{
    None = 0,
    BusinessLunch = 1,   // 1 место
    UnlimitedCoffee = 2, // 2 место
    Hookah = 3,          // 3 место
    Dessert = 4,         // 4–10
    BeerOrCoffee = 5,    // 11–20
    TeaPot = 6,          // 21–30
    Cocktail = 7,        // 31–50
}
