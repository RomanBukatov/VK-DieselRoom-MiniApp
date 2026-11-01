import { describe, expect, it } from 'bun:test';
import { TIERS, rankBadge, streakText, tierForRank, usageText } from './privileges';
import { displayCode, giftsTabOf, groupOrders, normalizeOrderCode, orderStatusLabel } from './orders';
import { bookingMomentUtc, canGuestCancel, normalizePhone, validateBooking, type BookingForm } from './booking';
import { isSafeUrl, visibleMenu } from './afisha';
import { qrMatrix, qrPath } from './qr';
import type { MenuLink, ShopOrder } from '../types';

// Среда 04.11.2026 18:00 по Перми = 13:00 UTC
const NOW = Date.UTC(2026, 10, 4, 13, 0, 0);

describe('Привилегии «Гонки недели» (ТЗ п. 4.1)', () => {
  it('места соответствуют тирам', () => {
    expect(tierForRank(1)?.tier).toBe('BusinessLunch');
    expect(tierForRank(2)?.tier).toBe('UnlimitedCoffee');
    expect(tierForRank(3)?.tier).toBe('Hookah');
    expect(tierForRank(4)?.tier).toBe('Dessert');
    expect(tierForRank(10)?.tier).toBe('Dessert');
    expect(tierForRank(11)?.tier).toBe('BeerOrCoffee');
    expect(tierForRank(20)?.tier).toBe('BeerOrCoffee');
    expect(tierForRank(21)?.tier).toBe('TeaPot');
    expect(tierForRank(31)?.tier).toBe('Cocktail');
    expect(tierForRank(50)?.tier).toBe('Cocktail');
    expect(tierForRank(51)).toBeNull();
    expect(tierForRank(null)).toBeNull();
  });

  it('тиры покрывают 1–50 без пропусков', () => {
    for (let rank = 1; rank <= 50; rank++) expect(tierForRank(rank)).not.toBeNull();
    expect(TIERS).toHaveLength(7);
  });

  it('значки мест', () => {
    expect([rankBadge(1), rankBadge(2), rankBadge(3), rankBadge(7), rankBadge(15), rankBadge(25), rankBadge(40), rankBadge(60)])
      .toEqual(['🥇', '🥈', '🥉', '🟢', '🟡', '🔵', '⚪', '']);
  });

  it('трекер удержания 1-го места — только у лидера', () => {
    expect(streakText({ rank: 1, firstPlaceStreak: 4, streakTarget: 7 })).toBe('🔥 Удержание 1-го места: 4 / 7 дней подряд');
    expect(streakText({ rank: 2, firstPlaceStreak: 4, streakTarget: 7 })).toBeNull();
  });

  it('подпись лимита', () => {
    expect(usageText({ tier: 'BusinessLunch', used: 2, limit: 3 })).toBe('использовано 2 из 3 на этой неделе');
    expect(usageText({ tier: 'Cocktail', used: 0, limit: 1 })).toBe('использовано 0 из 1 за 2 недели');
    expect(usageText({ tier: 'UnlimitedCoffee', used: 9, limit: null })).toBe('без лимита, пока держите место');
    expect(usageText({ tier: 'None', used: 0, limit: 0 })).toBe('');
  });
});

const order = (patch: Partial<ShopOrder>): ShopOrder => ({
  id: 1,
  code: 'DR-AB3K',
  title: 'Кофе',
  icon: '☕',
  priceCoins: 60,
  status: 'Pending',
  createdAt: '2026-11-04T10:00:00.000Z',
  expiresAt: '2026-11-11T10:00:00.000Z',
  issuedAt: null,
  closedAt: null,
  isPrize: false,
  ...patch,
});

describe('Мои подарки', () => {
  it('раскладывает купоны по вкладкам', () => {
    const grouped = groupOrders(
      [
        order({ id: 1 }),
        order({ id: 2, status: 'Issued' }),
        order({ id: 3, status: 'Cancelled' }),
        order({ id: 4, status: 'Expired' }),
        order({ id: 5, expiresAt: '2026-11-04T12:00:00.000Z' }), // уже истёк, но сервер ещё не закрыл
        order({ id: 6, expiresAt: '2026-11-05T10:00:00.000Z' }),
      ],
      NOW,
    );
    expect(grouped.pending.map((o) => o.id)).toEqual([6, 1]); // ближайший к истечению — сверху
    expect(grouped.received.map((o) => o.id)).toEqual([2]);
    expect(grouped.closed.map((o) => o.id)).toEqual([3, 4, 5]);
  });

  it('статус и возврат баллов', () => {
    expect(orderStatusLabel(order({ status: 'Cancelled' }), NOW)).toBe('Отменён · баллы возвращены');
    expect(orderStatusLabel(order({ status: 'Expired' }), NOW)).toBe('Истёк · баллы возвращены');
    expect(orderStatusLabel(order({ status: 'Expired', priceCoins: 0, isPrize: true }), NOW)).toBe('Истёк');
    expect(orderStatusLabel(order({ expiresAt: '2026-11-01T00:00:00.000Z' }), NOW)).toBe('Истёк · баллы возвращены');
    expect(giftsTabOf(order({ status: 'Issued' }), NOW)).toBe('received');
  });

  it('код купона #DR-XXXX', () => {
    expect(displayCode('DR-AB3K')).toBe('#DR-AB3K');
    expect(displayCode('#DR-AB3K')).toBe('#DR-AB3K');
    expect(normalizeOrderCode('#dr-ab3k')).toBe('DR-AB3K');
    expect(normalizeOrderCode(' ab3k ')).toBe('DR-AB3K');
    expect(normalizeOrderCode('DR AB3K')).toBe('DR-AB3K');
    expect(normalizeOrderCode('DR-AB')).toBeNull();
  });
});

const form = (patch: Partial<BookingForm> = {}): BookingForm => ({
  date: '2026-11-06',
  time: '20:00',
  guestsCount: 4,
  format: 'Karaoke',
  name: 'Мария',
  phone: '+7 (902) 123-45-67',
  wishes: '',
  ...patch,
});

describe('Бронирование', () => {
  it('корректная заявка проходит', () => {
    expect(validateBooking(form(), NOW)).toEqual([]);
  });

  it('прошедшие даты, слишком близкое время и дальше 90 дней — ошибки', () => {
    expect(validateBooking(form({ date: '2026-11-03' }), NOW)).toContain('Дата брони уже прошла.');
    expect(validateBooking(form({ date: '2026-11-04', time: '18:10' }), NOW).length).toBe(1);
    expect(validateBooking(form({ date: '2026-11-04', time: '19:00' }), NOW)).toEqual([]);
    expect(validateBooking(form({ date: '2027-03-01' }), NOW).length).toBe(1);
  });

  it('гости, имя и телефон', () => {
    const errors = validateBooking(form({ guestsCount: 0, name: 'Я', phone: '123' }), NOW);
    expect(errors).toHaveLength(3);
  });

  it('время брони — по Перми (UTC+5)', () => {
    expect(new Date(bookingMomentUtc('2026-11-06', '20:00')).toISOString()).toBe('2026-11-06T15:00:00.000Z');
  });

  it('телефон нормализуется как на сервере', () => {
    expect(normalizePhone('+7 (902) 123-45-67')).toBe('79021234567');
    expect(normalizePhone('89021234567')).toBe('79021234567');
    expect(normalizePhone('9021234567')).toBe('79021234567');
    expect(normalizePhone('12345')).toBeNull();
  });

  it('гость отменяет только активные брони', () => {
    expect(canGuestCancel('Pending')).toBe(true);
    expect(canGuestCancel('Confirmed')).toBe(true);
    expect(canGuestCancel('Completed')).toBe(false);
    expect(canGuestCancel('Rejected')).toBe(false);
  });
});

describe('Меню и ссылки', () => {
  const link = (patch: Partial<MenuLink>): MenuLink => ({
    id: 1, key: 'k', emoji: '🍽', title: 'Меню', url: 'https://vk.ru/a', hiddenAfter: null, isActive: true, sortOrder: 0, ...patch,
  });

  it('банкетное меню скрывается после 31.12.2026 23:59:59 по Перми', () => {
    const menu = [
      link({ id: 1, key: 'kitchen', sortOrder: 0 }),
      link({ id: 2, key: 'bar', sortOrder: 1 }),
      link({ id: 3, key: 'banquet', sortOrder: 2, hiddenAfter: '2026-12-31T18:59:59.000Z' }),
      link({ id: 4, key: 'off', isActive: false }),
    ];
    expect(visibleMenu(menu, Date.UTC(2026, 11, 31, 18, 59, 59)).map((m) => m.key)).toEqual(['kitchen', 'bar', 'banquet']);
    expect(visibleMenu(menu, Date.UTC(2026, 11, 31, 19, 0, 0)).map((m) => m.key)).toEqual(['kitchen', 'bar']);
  });

  it('открываем только https-ссылки', () => {
    expect(isSafeUrl('https://vk.ru/album-34613849_258959228')).toBe(true);
    expect(isSafeUrl('javascript:alert(1)')).toBe(false);
    expect(isSafeUrl('http://example.com')).toBe(false);
    expect(isSafeUrl(null)).toBe(false);
  });
});

describe('QR-код купона (генерируется на клиенте)', () => {
  it('матрица квадратная, с тремя поисковыми узорами', () => {
    const m = qrMatrix('#DR-AB3K');
    expect(m.length).toBeGreaterThanOrEqual(21);
    expect(m.every((row) => row.length === m.length)).toBe(true);
    // Угловые поисковые квадраты 7×7: верхняя строка тёмная
    const n = m.length;
    for (let i = 0; i < 7; i++) {
      expect(m[0][i]).toBe(true);
      expect(m[0][n - 1 - i]).toBe(true);
      expect(m[n - 1][i]).toBe(true);
    }
  });

  it('разные коды → разные матрицы, одинаковые → одинаковые', () => {
    expect(qrPath(qrMatrix('#DR-AB3K'))).toBe(qrPath(qrMatrix('#DR-AB3K')));
    expect(qrPath(qrMatrix('#DR-AB3K'))).not.toBe(qrPath(qrMatrix('#DR-ZZ9Q')));
  });

  it('path склеивает соседние модули в отрезки и учитывает отступ', () => {
    expect(qrPath([[true, true, false, true]], 2)).toBe('M2 2h2v1h-2zM5 2h1v1h-1z');
  });
});
