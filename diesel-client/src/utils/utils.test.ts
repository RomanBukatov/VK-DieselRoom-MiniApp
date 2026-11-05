import { beforeEach, describe, expect, it } from 'bun:test';
import { formatCoins, formatFullDate, formatLocalDate, formatPermDateTime, formatPhone, formatPoints, formatPrivilegeCode, permDateString, permTimeString, plural, raceCountdown, timeLeft } from './format';
import { fitSize } from './imageUpload';
import { buildReferralLink, getPendingReferralCode, referralRuleText, sanitizeReferralCode, REFERRAL_STORAGE_KEY } from './referral';
import { safeStorage } from './safeStorage';
import { apiErrorMessage, classifyApiError } from '../api/client';
import { MESSAGES_ASK_INTERVAL, shouldAskMessages, staffFromProfile, syncPayload } from '../store/useApp';
import type { Profile } from '../types';

describe('Время Перми и форматирование', () => {
  it('местные дата и время — UTC+5 независимо от телефона', () => {
    const t = Date.UTC(2026, 10, 8, 18, 59, 0); // воскресенье 23:59 по Перми — сброс недели
    expect(permDateString(t)).toBe('2026-11-08');
    expect(permTimeString(t)).toBe('23:59');
    expect(formatPermDateTime('2026-11-08T18:59:00.000Z')).toBe('08.11 23:59');
    expect(formatPermDateTime(null)).toBe('—');
  });

  it('дата брони с днём недели', () => {
    expect(formatLocalDate('2026-11-06')).toBe('Пт, 06.11');
  });

  it('таймер купона', () => {
    const now = Date.UTC(2026, 10, 4, 10, 0, 0);
    expect(timeLeft('2026-11-11T10:00:00.000Z', now)).toBe('7 д 0 ч');
    expect(timeLeft('2026-11-04T13:12:00.000Z', now)).toBe('3 ч 12 мин');
    expect(timeLeft('2026-11-04T10:00:30.000Z', now)).toBe('1 мин');
    expect(timeLeft('2026-11-04T09:00:00.000Z', now)).toBe('истёк');
  });

  it('живой отсчёт до конца гонки', () => {
    const now = Date.UTC(2026, 10, 4, 13, 0, 0);
    expect(raceCountdown('2026-11-08T18:59:00.000Z', now)).toBe('4 д 05:59:00');
    expect(raceCountdown('2026-11-04T13:00:09.000Z', now)).toBe('00:00:09');
    expect(raceCountdown('2026-11-04T13:00:00.000Z', now)).toBeNull();
    expect(raceCountdown(null, now)).toBeNull();
  });

  it('код привилегии, очки и полная дата', () => {
    expect(formatPrivilegeCode('482913')).toBe('482 913');
    expect(formatPoints(1)).toBe('1 очко');
    expect(formatPoints(3)).toBe('3 очка');
    expect(formatPoints(12)).toBe('12 очков');
    expect(formatFullDate('2026-12-31')).toBe('31.12.2026');
  });

  it('размер фото при загрузке: уменьшаем, но не растягиваем', () => {
    expect(fitSize(4000, 2000)).toEqual({ width: 1000, height: 500 });
    expect(fitSize(1000, 3000)).toEqual({ width: 333, height: 1000 });
    expect(fitSize(640, 480)).toEqual({ width: 640, height: 480 });
  });

  it('разрешение на сообщения спрашиваем не чаще раза в неделю и после согласия', () => {
    const now = 10 * MESSAGES_ASK_INTERVAL;
    const open = { consentRequired: false, tasks: [{ key: 'message_allow', done: false }] } as Parameters<typeof shouldAskMessages>[0];
    expect(shouldAskMessages(open, 0, now)).toBe(true);
    expect(shouldAskMessages(open, now - 1000, now)).toBe(false);
    expect(shouldAskMessages({ ...open, consentRequired: true }, 0, now)).toBe(false);
    expect(shouldAskMessages({ ...open, tasks: [{ ...open.tasks[0], done: true }] }, 0, now)).toBe(false);
  });

  it('склонения и баллы', () => {
    expect(plural(1, ['балл', 'балла', 'баллов'])).toBe('балл');
    expect(plural(3, ['балл', 'балла', 'баллов'])).toBe('балла');
    expect(plural(11, ['балл', 'балла', 'баллов'])).toBe('баллов');
    expect(plural(21, ['балл', 'балла', 'баллов'])).toBe('балл');
    expect(formatCoins(100)).toMatch(/^100 баллов$/);
  });

  it('телефон', () => {
    expect(formatPhone('79021234567')).toBe('+7 902 123-45-67');
  });
});

describe('Рефералка #ref=', () => {
  beforeEach(() => safeStorage.clear());

  it('ссылка и очистка кода', () => {
    expect(buildReferralLink('100123', 'https://vk.ru/app1')).toBe('https://vk.ru/app1#ref=100123');
    expect(buildReferralLink('', 'https://vk.ru/app1')).toBe('https://vk.ru/app1');
    expect(sanitizeReferralCode(' 100<script>123 ')).toBe('100script123');
    expect(sanitizeReferralCode(null)).toBe('');
  });

  it('отложенный код хранится в safeStorage', () => {
    safeStorage.setItem(REFERRAL_STORAGE_KEY, '100777');
    expect(getPendingReferralCode()).toBe('100777');
  });

  it('правило — бонус только после первого визита друга', () => {
    expect(referralRuleText(100, 5)).toContain('после его первого визита по чеку');
    expect(referralRuleText(100, 5)).toContain('+100 баллов и +5 в рейтинг');
  });
});

describe('Ошибки API', () => {
  it('сообщение сервера важнее общего текста', () => {
    expect(apiErrorMessage({ response: { status: 409, data: { message: 'Чек №1 уже проведён' } } })).toBe('Чек №1 уже проведён');
    expect(apiErrorMessage({ response: { status: 429, data: {} } })).toBe('Слишком много запросов — подождите немного.');
    expect(apiErrorMessage({ code: 'ERR_NETWORK' })).toBe('Нет связи с сервером. Проверьте интернет.');
  });

  it('классификация сбоев', () => {
    expect(classifyApiError({ code: 'ERR_CANCELED' })).toBe('aborted');
    expect(classifyApiError({ code: 'ECONNABORTED' })).toBe('timeout');
    expect(classifyApiError({ message: 'x' })).toBe('network');
    expect(classifyApiError({ response: { status: 500 } })).toBe('http');
  });
});

describe('Синхронизация профиля', () => {
  it('заглушка «Гость» без ответа VK Bridge не затирает имя на сервере', () => {
    expect(syncPayload({ id: 5, first_name: 'Гость', last_name: '' }, '100001')).toEqual({ referralCode: '100001' });
  });

  it('имя и аватар от VK Bridge отправляются', () => {
    expect(syncPayload({ id: 5, first_name: 'Анна', last_name: 'К.', photo_200: 'https://vk.ru/a.jpg', fromBridge: true })).toEqual({
      firstName: 'Анна', lastName: 'К.', photoUrl: 'https://vk.ru/a.jpg', referralCode: undefined,
    });
  });
});

describe('Роль персонала из профиля', () => {
  const base = { role: 'Guest', staffName: null } as unknown as Profile;

  it('гость не видит вкладку управления', () => {
    expect(staffFromProfile(base)).toBeNull();
    expect(staffFromProfile(null)).toBeNull();
  });

  it('сотрудник и администратор', () => {
    expect(staffFromProfile({ ...base, role: 'Staff', staffName: 'Бармен Олег' })).toEqual({ role: 'Staff', name: 'Бармен Олег' });
    expect(staffFromProfile({ ...base, role: 'Admin', staffName: null })).toEqual({ role: 'Admin', name: 'Администратор' });
  });
});
