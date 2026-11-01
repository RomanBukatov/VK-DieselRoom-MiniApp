// Выполняется ПЕРВЫМ (см. main.tsx): модули ES выполняются в порядке импорта, поэтому очистка устаревшего кэша
// должна произойти до того, как сторы прочитают localStorage при своей инициализации.
import { migrateOrClearStaleAppCache } from './utils/safeStorage';
import { captureReferralFromLaunch } from './utils/referral';
import { markVkMobileClient } from './utils/vkBridge';

migrateOrClearStaleAppCache();
// Отступ шапки под кнопки «⋯ ✕» мобильного ВК (mobile_* или mobile_web + com.vk.vkclient на iPhone) — до первого рендера
markVkMobileClient();
// Реферальная ссылка (#ref=100123): запоминаем код друга до первой синхронизации профиля
captureReferralFromLaunch();
