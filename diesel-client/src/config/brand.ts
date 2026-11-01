// Фирменный стиль DIESEL ROOM: индустриальный чёрно-красно-белый (ТЗ п. 1)
export const BRAND = {
  name: 'DIESEL ROOM',
  city: 'Пермь',
  address: 'ул. Куйбышева, 47',
  phone: '202-11-00',
  phoneHref: 'tel:+73422021100',
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
