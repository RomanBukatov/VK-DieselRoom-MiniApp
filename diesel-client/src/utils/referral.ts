// «Приведи друга»: промокод = номер карты гостя, ссылка вида https://vk.ru/appXXXX#ref=100123
import { getVkLaunchParam } from './vkBridge';
import { safeStorage } from './safeStorage';
import { BRAND } from '../config/brand';

export const REFERRAL_STORAGE_KEY = 'diesel_pending_referral_code';

export function sanitizeReferralCode(raw: unknown): string {
  if (raw === null || raw === undefined) return '';
  return String(raw).trim().replace(/[^0-9A-Za-z_-]/g, '').slice(0, 16);
}

export function buildReferralLink(code?: string | null, base: string = BRAND.appUrl): string {
  const clean = sanitizeReferralCode(code);
  return clean ? `${base}#ref=${encodeURIComponent(clean)}` : base;
}

export function getPendingReferralCode(): string {
  return sanitizeReferralCode(safeStorage.getItem(REFERRAL_STORAGE_KEY));
}

export function clearPendingReferralCode(): void {
  safeStorage.removeItem(REFERRAL_STORAGE_KEY);
}

/** Читает ref из hash/query запуска и запоминает его до первой синхронизации профиля. */
export function captureReferralFromLaunch(): string {
  const fromUrl = (() => {
    try {
      return sanitizeReferralCode(getVkLaunchParam('ref'));
    } catch {
      return '';
    }
  })();
  if (fromUrl) {
    safeStorage.setItem(REFERRAL_STORAGE_KEY, fromUrl);
    return fromUrl;
  }
  return getPendingReferralCode();
}

export function referralRuleText(coins: number, rating: number): string {
  return `Приведите друга: после его первого визита по чеку вы получите +${coins} баллов и +${rating} в рейтинг!`;
}
