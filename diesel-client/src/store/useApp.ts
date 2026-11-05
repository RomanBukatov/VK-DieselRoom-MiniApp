import { create } from 'zustand';
import type { Profile, StaffRole, TabType } from '../types';
import { guestApi, staffApi } from '../api/endpoints';
import { ADMIN_KEY_KEY, STAFF_PIN_KEY, apiErrorMessage } from '../api/client';
import { initVkBridge, requestGroupMessages, type VkUser } from '../utils/vkBridge';
import { readCache, writeCache } from '../utils/cacheEnvelope';
import { safeSessionStorage, safeStorage } from '../utils/safeStorage';
import { captureReferralFromLaunch, clearPendingReferralCode } from '../utils/referral';

export const PROFILE_CACHE_KEY = 'diesel_cached_profile';
const PROFILE_MAX_AGE = 7 * 24 * 60 * 60 * 1000;

export const MESSAGES_ASKED_KEY = 'diesel_messages_asked_at';
export const MESSAGES_ASK_INTERVAL = 7 * 24 * 60 * 60 * 1000;

/** Спрашиваем разрешение на сообщения при старте, но не чаще раза в неделю и только если задание ещё не выполнено. */
export function shouldAskMessages(profile: Pick<Profile, 'consentRequired' | 'tasks'>, lastAskedAt: number, nowMs: number): boolean {
  if (profile.consentRequired) return false;
  if (profile.tasks.some((t) => t.key === 'message_allow' && t.done)) return false;
  return nowMs - lastAskedAt >= MESSAGES_ASK_INTERVAL;
}

export interface StaffSession {
  role: Exclude<StaffRole, 'Guest'>;
  name: string;
}

interface Toast {
  id: number;
  text: string;
  tone: 'ok' | 'error';
}

interface AppState {
  vkUser: VkUser | null;
  profile: Profile | null;
  profileError: string | null;
  booting: boolean;
  activeTab: TabType;
  staff: StaffSession | null;
  toast: Toast | null;

  boot: () => Promise<void>;
  refreshProfile: () => Promise<void>;
  acceptConsent: () => Promise<void>;
  askMessagesPermission: () => Promise<void>;
  setProfile: (p: Profile) => void;
  patchCoins: (coins: number) => void;
  setTab: (tab: TabType) => void;
  loginStaff: (pin: string, masterKey?: string) => Promise<string | null>;
  logoutStaff: () => void;
  showToast: (text: string, tone?: Toast['tone']) => void;
}

export function syncPayload(vkUser: VkUser, referralCode?: string) {
  return vkUser.fromBridge
    ? { firstName: vkUser.first_name || undefined, lastName: vkUser.last_name || undefined, photoUrl: vkUser.photo_200, referralCode }
    : { referralCode };
}

/** Роль персонала из профиля (VK ID в списках) — без ПИН-кода. */
export function staffFromProfile(p: Profile | null): StaffSession | null {
  if (!p || p.role === 'Guest') return null;
  return { role: p.role, name: p.staffName ?? (p.role === 'Admin' ? 'Администратор' : 'Сотрудник') };
}

export const useApp = create<AppState>((set, get) => ({
  vkUser: null,
  profile: readCache<Profile>(PROFILE_CACHE_KEY, PROFILE_MAX_AGE),
  profileError: null,
  booting: true,
  activeTab: 'afisha',
  staff: null,
  toast: null,

  boot: async () => {
    const vkUser = await initVkBridge();
    set({ vkUser });
    try {
      const referralCode = captureReferralFromLaunch() || undefined;
      // Имя отправляем только полученное от VK Bridge: заглушка «Гость» (мост не ответил) не должна затирать настоящее имя
      const profile = await guestApi.sync(syncPayload(vkUser, referralCode));
      if (referralCode) clearPendingReferralCode();
      get().setProfile(profile);
      void get().askMessagesPermission();

      // Планшет персонала: ПИН / мастер-ключ из этой сессии
      if (!get().staff && (safeSessionStorage.getItem(STAFF_PIN_KEY) || safeSessionStorage.getItem(ADMIN_KEY_KEY))) {
        try {
          const s = await staffApi.session();
          if (s.role !== 'Guest') set({ staff: { role: s.role, name: s.name } });
        } catch {
          safeSessionStorage.removeItem(STAFF_PIN_KEY);
          safeSessionStorage.removeItem(ADMIN_KEY_KEY);
        }
      }
    } catch (e) {
      set({ profileError: apiErrorMessage(e, 'Не удалось загрузить профиль.') });
    } finally {
      set({ booting: false });
    }
  },

  refreshProfile: async () => {
    try {
      get().setProfile(await guestApi.me());
    } catch (e) {
      set({ profileError: apiErrorMessage(e) });
    }
  },

  acceptConsent: async () => {
    await guestApi.consent();
    // Имя и фото из ВК передаём только после согласия
    const vkUser = get().vkUser;
    get().setProfile(await guestApi.sync(vkUser ? syncPayload(vkUser) : {}));
    void get().askMessagesPermission();
  },

  askMessagesPermission: async () => {
    const profile = get().profile;
    const now = Date.now();
    if (!profile || !shouldAskMessages(profile, Number(safeStorage.getItem(MESSAGES_ASKED_KEY)) || 0, now)) return;
    safeStorage.setItem(MESSAGES_ASKED_KEY, String(now));
    if (!(await requestGroupMessages())) return;
    try {
      get().setProfile(await guestApi.verifyTasks());
    } catch {
      // Бонус всё равно придёт по событию message_allow
    }
  },

  setProfile: (profile) => {
    writeCache(PROFILE_CACHE_KEY, profile);
    const fromProfile = staffFromProfile(profile);
    set((state) => ({
      profile,
      profileError: null,
      // Роль по VK ID главнее ПИН-сессии: администратор остаётся администратором
      staff: fromProfile && (!state.staff || fromProfile.role === 'Admin') ? fromProfile : state.staff,
    }));
  },

  patchCoins: (coins) => {
    const p = get().profile;
    if (p) get().setProfile({ ...p, coins });
  },

  setTab: (activeTab) => set({ activeTab }),

  loginStaff: async (pin, masterKey) => {
    if (pin) safeSessionStorage.setItem(STAFF_PIN_KEY, pin);
    if (masterKey) safeSessionStorage.setItem(ADMIN_KEY_KEY, masterKey);
    try {
      const s = await staffApi.session();
      if (s.role === 'Guest') throw new Error('guest');
      set({ staff: { role: s.role, name: s.name }, activeTab: 'staff' });
      return null;
    } catch (e) {
      safeSessionStorage.removeItem(STAFF_PIN_KEY);
      safeSessionStorage.removeItem(ADMIN_KEY_KEY);
      return apiErrorMessage(e, 'Неверный ПИН-код.');
    }
  },

  logoutStaff: () => {
    safeSessionStorage.removeItem(STAFF_PIN_KEY);
    safeSessionStorage.removeItem(ADMIN_KEY_KEY);
    set((state) => ({ staff: staffFromProfile(state.profile), activeTab: 'profile' }));
  },

  showToast: (text, tone = 'ok') => {
    const id = Date.now();
    set({ toast: { id, text, tone } });
    setTimeout(() => {
      if (get().toast?.id === id) set({ toast: null });
    }, 3200);
  },
}));
