import vkBridge from '@vkontakte/vk-bridge';
import { safeStorage, safeSessionStorage } from './safeStorage';
import { BRAND } from '../config/brand';

export interface VkUser {
  id: number;
  first_name: string;
  last_name: string;
  photo_200?: string;
  /** Имя и аватар получены от VK Bridge (а не подставлены, потому что мост не ответил вовремя). */
  fromBridge?: boolean;
}

const GUEST: VkUser = { id: 0, first_name: 'Гость', last_name: '' };

async function sendWithTimeout<T>(promise: Promise<T>, timeoutMs = 1500, errorMsg = 'VK Bridge timeout'): Promise<T> {
  return Promise.race([
    promise,
    new Promise<T>((_, reject) => setTimeout(() => reject(new Error(errorMsg)), timeoutMs)),
  ]);
}

type LooseSend = (method: string, props?: Record<string, unknown>) => Promise<unknown>;
const looseSend = (): LooseSend => vkBridge.send as unknown as LooseSend;

export function getVkLaunchParam(paramName: string): string | null {
  if (typeof window === 'undefined') return null;
  try {
    const searchParams = new URLSearchParams(window.location.search);
    if (searchParams.has(paramName)) return searchParams.get(paramName);

    // В мобильных клиентах или при редиректах параметры могут находиться в hash
    if (window.location.hash) {
      const hashStr = window.location.hash.startsWith('#') ? window.location.hash.slice(1) : window.location.hash;
      const queryPart = hashStr.includes('?') ? hashStr.split('?')[1] : hashStr;
      const hashParams = new URLSearchParams(queryPart);
      if (hashParams.has(paramName)) return hashParams.get(paramName);
    }

    const cachedSearch = safeSessionStorage.getItem('vk_launch_search');
    if (cachedSearch) {
      const cachedParams = new URLSearchParams(cachedSearch.startsWith('?') ? cachedSearch.slice(1) : cachedSearch);
      if (cachedParams.has(paramName)) return cachedParams.get(paramName);
    }
  } catch {
    // ignore
  }
  return null;
}

export function isVkEnvironment(): boolean {
  if (typeof window === 'undefined') return false;
  const isIframe = window.parent !== window;
  return Boolean(
    getVkLaunchParam('vk_user_id') ||
    getVkLaunchParam('vk_app_id') ||
    (isIframe && getVkLaunchParam('vk_platform')),
  );
}

/**
 * Мобильный клиент ВК (Android/iOS): поверх правого верхнего угла нарисованы кнопки «⋯ ✕».
 * Приложение ВК на iPhone запускает мини-апп с vk_platform=mobile_web (как m.vk.com), поэтому его узнаём
 * по User-Agent «com.vk.vkclient/...». В обычном мобильном браузере (m.vk.com) кнопок поверх нет.
 */
export function isVkMobileClient(): boolean {
  const platform = getVkLaunchParam('vk_platform') ?? '';
  if (/^mobile_(android|iphone|ipad)/.test(platform)) return true;
  const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent || '' : '';
  return platform === 'mobile_web' && /com\.vk\.vkclient\//i.test(userAgent);
}

/** Помечает <html> классом vk-mobile, чтобы шапка (index.css: .vk-controls-pad) не заезжала под кнопки ВК. */
export function markVkMobileClient(): void {
  if (typeof document === 'undefined') return;
  document.documentElement.classList.toggle('vk-mobile', isVkMobileClient());
}

export async function initVkBridge(): Promise<VkUser> {
  if (!isVkEnvironment()) {
    const existing = Number(safeStorage.getItem('vk_test_user_id')) || 0;
    return { ...GUEST, id: existing };
  }

  markVkMobileClient();
  const urlVkUserId = getVkLaunchParam('vk_user_id');

  try {
    vkBridge.send('VKWebAppInit').catch(() => {});
    const platform = getVkLaunchParam('vk_platform');
    const isIframe = window.parent !== window;
    if (platform === 'desktop_web' || isIframe) {
      looseSend()('VKWebAppResizeWindow', { width: 900, height: 850 }).catch(() => {});
    } else {
      vkBridge.send('VKWebAppSetViewSettings', {
        status_bar_style: 'light',
        action_bar_color: BRAND.colors.bg,
        navigation_bar_color: BRAND.colors.bg,
      }).catch(() => {});
    }

    // Не держим чёрный экран: если мост не ответил за 600 мс — открываемся с ID из параметров запуска
    const info = await sendWithTimeout(vkBridge.send('VKWebAppGetUserInfo'), 600, 'Таймаут получения данных пользователя VK');
    return { id: info.id, first_name: info.first_name, last_name: info.last_name, photo_200: info.photo_200, fromBridge: true };
  } catch {
    return { ...GUEST, id: Number(urlVkUserId) || 0, first_name: 'Гость' };
  }
}

export function triggerHaptic(style: 'light' | 'medium' | 'heavy' = 'medium'): void {
  try {
    vkBridge.send('VKWebAppTapticImpactOccurred', { style }).catch(() => {});
  } catch {
    // вне ВК
  }
}

/** Разрешение на сообщения от группы DIESEL ROOM — туда приходят подтверждения броней и уведомления рейтинга. Ждём, пока гость ответит в диалоге ВК. */
export async function requestGroupMessages(groupId: number = BRAND.vkGroupId): Promise<boolean> {
  if (!isVkEnvironment()) return false;
  try {
    await sendWithTimeout(vkBridge.send('VKWebAppAllowMessagesFromGroup', { group_id: groupId }), 60_000);
    return true;
  } catch {
    return false;
  }
}

/** Подписка на группу через диалог ВК. Бонус начисляет сервер по событию ВК, а не этот вызов. */
export async function joinVkGroup(groupId: number = BRAND.vkGroupId): Promise<boolean> {
  if (!isVkEnvironment()) return false;
  try {
    await sendWithTimeout(looseSend()('VKWebAppJoinGroup', { group_id: groupId }), 60_000);
    return true;
  } catch {
    return false;
  }
}

/** Сканер QR в мобильном ВК. null — сканер недоступен (браузер, компьютер) или гость его закрыл: тогда код вводят вручную. */
export async function scanQrCode(): Promise<string | null> {
  if (!isVkEnvironment()) return null;
  try {
    const result = (await looseSend()('VKWebAppOpenCodeReader')) as { code_data?: string } | undefined;
    return result?.code_data?.trim() || null;
  } catch {
    return null;
  }
}

export const openExternalUrl = (url: string): void => {
  if (!url || typeof window === 'undefined') return;

  try {
    looseSend()('VKWebAppOpenURL', { url }).catch(() => {});
  } catch {
    // ignore
  }

  // iOS Safari в iframe (m.vk.ru на iPhone) блокирует window.open — переходим через window.top
  const isIframe = window.self !== window.top;
  const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent || '' : '';
  const isIOS = /iPad|iPhone|iPod/.test(userAgent) ||
    (typeof navigator !== 'undefined' && navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

  if (isIframe && isIOS) {
    try {
      if (window.top) {
        window.top.location.href = url;
        return;
      }
    } catch {
      // cross-origin
    }
    window.location.href = url;
    return;
  }

  let popup: Window | null = null;
  try {
    popup = window.open(url, '_blank', 'noopener,noreferrer');
  } catch {
    // заблокировано
  }
  if (!popup || popup.closed || typeof popup.closed === 'undefined') {
    try {
      if (window.top && window.top !== window) {
        window.top.location.href = url;
        return;
      }
    } catch {
      // cross-origin
    }
    window.location.href = url;
  }
};

/** Копирует текст: VKWebAppCopyText → Clipboard API → textarea. */
export async function copyTextToClipboard(text: string): Promise<boolean> {
  if (!text) return false;
  try {
    const res = (await looseSend()('VKWebAppCopyText', { text })) as { result?: boolean } | undefined;
    if (res && res.result !== false) return true;
  } catch {
    // вне ВК
  }
  try {
    if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // нет прав
  }
  try {
    const area = document.createElement('textarea');
    area.value = text;
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    const ok = document.execCommand('copy');
    document.body.removeChild(area);
    return ok;
  } catch {
    return false;
  }
}

export type ShareResult = 'shared' | 'copied' | 'cancelled' | 'failed';

export async function shareLinkInVk(link: string): Promise<ShareResult> {
  try {
    await looseSend()('VKWebAppShare', { link });
    return 'shared';
  } catch (err) {
    if ((err as { error_data?: { error_code?: number } })?.error_data?.error_code === 4) return 'cancelled';
  }
  return (await copyTextToClipboard(link)) ? 'copied' : 'failed';
}
