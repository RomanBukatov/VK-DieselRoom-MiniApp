// Фирменный стиль DIESEL ROOM: индустриальный чёрно-красно-белый (ТЗ п. 1)
export const BRAND = {
  name: 'DIESEL ROOM',
  city: 'Пермь',
  address: 'ул. Куйбышева, 47',
  phone: '202-11-00',
  phoneHref: 'tel:+73422021100',
  routeUrl: 'https://yandex.ru/maps/?text=%D0%9F%D0%B5%D1%80%D0%BC%D1%8C%2C%20%D1%83%D0%BB.%20%D0%9A%D1%83%D0%B9%D0%B1%D1%8B%D1%88%D0%B5%D0%B2%D0%B0%2C%2047',
  vkGroupUrl: 'https://vk.ru/diesel_room',
  vkGroupId: 34613849,
  // Ссылка на мини-приложение для реферальных приглашений (#ref=номер карты); задаётся при сборке
  appUrl: (import.meta.env?.VITE_VK_APP_URL as string | undefined) || 'https://vk.ru/diesel_room',
  coinsName: 'баллов',
  colors: {
    bg: '#09090B',
    surface: '#121215',
    card: '#18181C',
    border: 'rgba(255,255,255,0.08)',
    red: '#DC2626',
    redLight: '#EF4444',
    glow: 'rgba(220,38,38,0.25)',
    text: '#FFFFFF',
    muted: '#A1A1AA',
  },
} as const;

/** Оригинальные файлы бренда (оптимизированы под мобильный экран, лежат в public/brand). */
export const BRAND_ASSETS = {
  logo: '/brand/logo.webp',
  ant: '/brand/ant.png',
  antWhite: '/brand/ant-white.png',
} as const;
