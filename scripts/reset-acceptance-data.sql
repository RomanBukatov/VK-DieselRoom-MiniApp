-- Очистка базы после приёмки: гости и всё, что они успели сделать, исчезают; настройки заведения остаются.
-- Запускать только через scripts/reset-acceptance-data.sh: он делает резервную копию и останавливает API.
--
-- CLEAR: Users, PointsTransactions, VkActivityLogs, Bookings, ShopOrders, Visits, PrivilegeRedemptions, WeeklyRaceResults, DailyLeaderSnapshots, StaffAuditLogs
-- KEEP:  AppSettings, PromoBanners, MenuLinks, AfishaEvents, ShopItems, MediaFiles, StaffMembers
\set ON_ERROR_STOP on

BEGIN;

-- Запоминаем масштаб очистки для записи в журнале
CREATE TEMP TABLE reset_summary ON COMMIT DROP AS
SELECT (SELECT count(*) FROM "Users") AS guests,
       (SELECT count(*) FROM "PointsTransactions") AS operations,
       (SELECT count(*) FROM "ShopOrders") AS orders,
       (SELECT count(*) FROM "Visits") AS visits,
       (SELECT count(*) FROM "Bookings") AS bookings;

-- Возвращаем на склад то, что забрали тестовые купоны. Статусы: 0 — к получению, 1 — выдан.
-- Отменённые и истёкшие уже вернули товар сами. Спецпризы (без ShopItemId) со склада не списывались.
UPDATE "ShopItems" AS item
SET "StockCount" = item."StockCount" + taken.quantity
FROM (
    SELECT "ShopItemId" AS id, count(*)::int AS quantity
    FROM "ShopOrders"
    WHERE "Status" IN (0, 1) AND "ShopItemId" IS NOT NULL
    GROUP BY "ShopItemId"
) AS taken
WHERE item."Id" = taken.id;

-- Счётчики с нуля: первый настоящий гость получит карту № 100001
TRUNCATE TABLE
    "Users", "PointsTransactions", "VkActivityLogs", "Bookings", "ShopOrders", "Visits",
    "PrivilegeRedemptions", "WeeklyRaceResults", "DailyLeaderSnapshots", "StaffAuditLogs"
RESTART IDENTITY;

INSERT INTO "StaffAuditLogs" ("StaffName", "Action", "Details", "CreatedAt")
SELECT 'Служебный скрипт', 'DataReset',
       format('Очистка после приёмки: гостей %s, операций %s, купонов %s, визитов %s, броней %s. Остатки подарков восстановлены.',
              guests, operations, orders, visits, bookings),
       now() AT TIME ZONE 'UTC'
FROM reset_summary;

COMMIT;
