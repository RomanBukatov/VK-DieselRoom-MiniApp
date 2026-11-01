import axios, { type InternalAxiosRequestConfig } from 'axios';
import { safeStorage, safeSessionStorage } from '../utils/safeStorage';

const API_BASE_URL = import.meta.env.VITE_API_URL ?? '';

// Таймаут ОДНОЙ попытки GET. На iOS (клиент VK) параллельные запросы периодически «зависают» внутри телефона;
// зависший запрос надёжно лечится быстрым повтором, поэтому долго ждать нет смысла.
export const GET_ATTEMPT_TIMEOUT_MS = 6_000;
export const MUTATION_TIMEOUT_MS = 15_000;
export const GET_RETRY_DELAYS_MS = [300, 800];

/** Сессия персонала на общем планшете: ПИН-код / мастер-ключ живут только до закрытия приложения. */
export const STAFF_PIN_KEY = 'diesel_staff_pin';
export const ADMIN_KEY_KEY = 'diesel_admin_key';

export type ApiErrorKind = 'aborted' | 'timeout' | 'network' | 'http';

type TrackedConfig = InternalAxiosRequestConfig & { _retryCount?: number };

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
  timeout: GET_ATTEMPT_TIMEOUT_MS,
});

export function classifyApiError(error: unknown): ApiErrorKind {
  const e = error as { code?: string; message?: string; response?: unknown } | null | undefined;
  if (axios.isCancel(error) || e?.code === 'ERR_CANCELED') return 'aborted';
  if (e?.code === 'ECONNABORTED' || e?.code === 'ETIMEDOUT' || /timeout/i.test(e?.message || '')) return 'timeout';
  if (!e?.response) return 'network';
  return 'http';
}

/** Текст ошибки для гостя: сообщение сервера или понятная подсказка по типу сбоя. */
export function apiErrorMessage(error: unknown, fallback = 'Что-то пошло не так. Попробуйте ещё раз.'): string {
  const e = error as { response?: { status?: number; data?: { message?: string; title?: string } } };
  const message = e?.response?.data?.message || e?.response?.data?.title;
  if (message) return message;
  if (e?.response?.status === 429) return 'Слишком много запросов — подождите немного.';
  const kind = classifyApiError(error);
  if (kind === 'network' || kind === 'timeout') return 'Нет связи с сервером. Проверьте интернет.';
  return fallback;
}

// Параметры запуска VK (кэшируются в сессии на случай SPA-навигации внутри iframe VK)
export const getVkLaunchParams = (): string => {
  if (typeof window === 'undefined') return '';
  const search = window.location.search || '';
  if (search.length > 1 && (search.includes('vk_user_id') || search.includes('sign='))) {
    safeSessionStorage.setItem('vk_launch_search', search);
    return search;
  }
  const hash = window.location.hash || '';
  if (hash.includes('vk_user_id') || hash.includes('sign=')) {
    const queryPart = hash.includes('?') ? hash.split('?')[1] : hash.replace(/^#/, '');
    const formatted = queryPart.startsWith('?') ? queryPart : `?${queryPart}`;
    safeSessionStorage.setItem('vk_launch_search', formatted);
    return formatted;
  }
  return safeSessionStorage.getItem('vk_launch_search') ?? search;
};

const newRequestId = (): string => {
  try {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  } catch {
    // ignore
  }
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
};

apiClient.interceptors.request.use((rawConfig) => {
  const config = rawConfig as TrackedConfig;
  const method = config.method?.toUpperCase();
  if ((method === 'POST' || method === 'PUT' || method === 'DELETE') && (!config.timeout || config.timeout < MUTATION_TIMEOUT_MS)) {
    config.timeout = MUTATION_TIMEOUT_MS;
  }

  config.headers['X-Client-Request-Id'] = newRequestId();

  const launch = getVkLaunchParams();
  if (launch && launch.length > 1 && (launch.includes('vk_user_id') || launch.includes('sign='))) {
    try {
      config.headers['X-VK-Sign'] = encodeURI(launch);
    } catch {
      config.headers['X-VK-Sign'] = launch;
    }
  }

  // Тестовый VK ID — ТОЛЬКО в режиме разработки (сервер принимает его лишь в Development без проверки подписи)
  if (import.meta.env.DEV) {
    config.headers['X-Test-Vk-Id'] = safeStorage.getItem('vk_test_user_id') || '0';
  }

  const pin = safeSessionStorage.getItem(STAFF_PIN_KEY);
  if (pin) config.headers['X-Staff-Pin'] = pin;
  const adminKey = safeSessionStorage.getItem(ADMIN_KEY_KEY);
  if (adminKey) config.headers['X-Admin-Key'] = adminKey;

  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    const config = error.config as TrackedConfig | undefined;
    const kind = classifyApiError(error);
    if (kind === 'aborted') return Promise.reject(error);

    // До 2 быстрых повторов безопасных GET при сетевом сбое, таймауте или 5xx
    const isGet = config?.method?.toUpperCase() === 'GET';
    const retryable = kind === 'network' || kind === 'timeout' || (error.response && error.response.status >= 500);
    const retryCount = config?._retryCount ?? 0;
    if (config && isGet && retryable && retryCount < GET_RETRY_DELAYS_MS.length) {
      config._retryCount = retryCount + 1;
      return new Promise((resolve, reject) => {
        setTimeout(() => apiClient(config).then(resolve, reject), GET_RETRY_DELAYS_MS[retryCount]);
      });
    }
    return Promise.reject(error);
  },
);
