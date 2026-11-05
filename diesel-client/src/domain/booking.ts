import type { AfishaEvent, Booking, BookingFormat, BookingStatus } from '../types';
import { PERM_OFFSET_MS, permDateString } from '../utils/format';

export const BOOKING_FORMATS: { value: BookingFormat; label: string }[] = [
  { value: 'Hall', label: 'Стол в зале' },
  { value: 'Karaoke', label: 'Караоке' },
  { value: 'Party', label: 'Вечеринка / Мероприятие' },
  { value: 'Banquet', label: 'Банкет' },
];

export const BOOKING_STATUS_LABELS: Record<BookingStatus, string> = {
  Pending: 'Ожидает подтверждения',
  Confirmed: 'Подтверждено',
  Rejected: 'Отклонено',
  Completed: 'Посетил',
  Cancelled: 'Отменено гостем',
};

export const formatLabel = (f: BookingFormat) => BOOKING_FORMATS.find((x) => x.value === f)?.label ?? f;

export const MAX_GUESTS = 200;
export const MAX_DAYS_AHEAD = 90;
export const MIN_LEAD_MINUTES = 30;

export interface BookingForm {
  date: string; // yyyy-MM-dd (Пермь)
  time: string; // HH:mm (Пермь)
  guestsCount: number;
  format: BookingFormat;
  name: string;
  phone: string;
  wishes: string;
}

/** Телефон → 7XXXXXXXXXX или null (как на сервере). */
export function normalizePhone(input: string): string | null {
  let digits = input.replace(/\D/g, '');
  if (digits.length === 10) digits = `7${digits}`;
  if (digits.length === 11 && digits[0] === '8') digits = `7${digits.slice(1)}`;
  return digits.length === 11 && digits[0] === '7' ? digits : null;
}

/** Момент брони (UTC, мс) из местных даты и времени Перми. */
export function bookingMomentUtc(date: string, time: string): number {
  const [y, m, d] = date.split('-').map(Number);
  const [hh, mm] = time.split(':').map(Number);
  return Date.UTC(y, (m || 1) - 1, d || 1, hh || 0, mm || 0) - PERM_OFFSET_MS;
}

/** Та же проверка, что на сервере (BookingService.Validate) — ошибки видны до отправки. */
export function validateBooking(form: BookingForm, nowMs: number): string[] {
  const errors: string[] = [];
  const today = permDateString(nowMs);
  const maxDate = permDateString(nowMs + MAX_DAYS_AHEAD * 86_400_000);

  if (!/^\d{4}-\d{2}-\d{2}$/.test(form.date)) errors.push('Укажите дату брони.');
  else if (form.date < today) errors.push('Дата брони уже прошла.');
  else if (form.date > maxDate) errors.push(`Бронь — не более чем на ${MAX_DAYS_AHEAD} дней вперёд.`);
  else if (!/^\d{2}:\d{2}$/.test(form.time)) errors.push('Укажите время.');
  else if (bookingMomentUtc(form.date, form.time) < nowMs + MIN_LEAD_MINUTES * 60_000) {
    errors.push('Это время уже прошло или наступит меньше чем через 30 минут.');
  }

  if (!Number.isInteger(form.guestsCount) || form.guestsCount < 1 || form.guestsCount > MAX_GUESTS) {
    errors.push(`Гостей — от 1 до ${MAX_GUESTS}.`);
  }
  const name = form.name.trim();
  if (name.length < 2 || name.length > 60) errors.push('Укажите имя (от 2 до 60 символов).');
  if (!normalizePhone(form.phone)) errors.push('Укажите телефон: +7 XXX XXX-XX-XX.');
  if (form.wishes.length > 500) errors.push('Пожелания — не длиннее 500 символов.');
  return errors;
}

/** Стартовые значения формы: дата и время события (если оно ещё впереди), иначе завтра в 20:00. */
export function initialBookingForm(event: AfishaEvent | null, name: string, phone: string | null, nowMs: number): BookingForm {
  const today = permDateString(nowMs);
  const eventAhead = event?.eventDate && event.eventDate >= today;
  return {
    date: eventAhead ? event.eventDate! : permDateString(nowMs + 86_400_000),
    time: eventAhead && event.eventTime ? event.eventTime.slice(0, 5) : '20:00',
    guestsCount: 2,
    format: event?.defaultFormat ?? 'Hall',
    name,
    phone: phone ? `+${phone}` : '',
    wishes: '',
  };
}

export type BookingsTab = 'active' | 'archive';

/** «Активные»: ждут подтверждения или подтверждены, дата ещё не прошла. Остальное — «Архив». */
export function bookingsTabOf(booking: Pick<Booking, 'status' | 'date'>, nowMs: number): BookingsTab {
  return canGuestCancel(booking.status) && booking.date >= permDateString(nowMs) ? 'active' : 'archive';
}

export const canGuestCancel = (status: BookingStatus) => status === 'Pending' || status === 'Confirmed';
