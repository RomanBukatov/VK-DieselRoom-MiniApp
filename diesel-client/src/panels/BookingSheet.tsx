import React, { useState } from 'react';
import { Phone } from 'lucide-react';
import { Button, ErrorLine, Field, Sheet, inputClass } from '../components/ui';
import { BOOKING_FORMATS, validateBooking, type BookingForm } from '../domain/booking';
import { guestApi } from '../api/endpoints';
import { apiErrorMessage } from '../api/client';
import { useApp } from '../store/useApp';
import { useNow } from '../store/useNow';
import { permDateString } from '../utils/format';
import { requestGroupMessages, triggerHaptic } from '../utils/vkBridge';
import { BRAND } from '../config/brand';
import type { AfishaEvent, BookingFormat } from '../types';

interface Props {
  open: boolean;
  event: AfishaEvent | null;
  onClose: () => void;
  onCreated?: () => void;
}

const initialForm = (format: BookingFormat, name: string, phone: string | null): BookingForm => ({
  date: permDateString(Date.now() + 86_400_000),
  time: '20:00',
  guestsCount: 2,
  format,
  name,
  phone: phone ? `+${phone}` : '',
  wishes: '',
});

/** «Забронировать стол»: дата, время, гости, формат, имя, телефон, пожелания (ТЗ п. 5). */
export const BookingSheet: React.FC<Props> = ({ open, event, onClose, onCreated }) => {
  const { profile, showToast } = useApp();
  const [form, setForm] = useState<BookingForm>(() => initialForm(event?.defaultFormat ?? 'Hall', profile?.firstName ?? '', profile?.phone ?? null));
  const [errors, setErrors] = useState<string[]>([]);
  const [sending, setSending] = useState(false);
  const [lastEventId, setLastEventId] = useState<number | null | undefined>(undefined);
  const now = useNow(60_000);

  // Новое открытие для другого мероприятия — формат по умолчанию из карточки
  if (open && lastEventId !== (event?.id ?? null)) {
    setLastEventId(event?.id ?? null);
    setForm((f) => ({ ...f, format: event?.defaultFormat ?? f.format }));
    setErrors([]);
  }

  const update = <K extends keyof BookingForm>(key: K, value: BookingForm[K]) => setForm((f) => ({ ...f, [key]: value }));

  const submit = async () => {
    const problems = validateBooking(form, Date.now());
    setErrors(problems);
    if (problems.length) return;

    setSending(true);
    try {
      // Подтверждение брони придёт сообщением от группы — просим разрешение заранее
      void requestGroupMessages();
      await guestApi.createBooking({
        date: form.date,
        time: form.time,
        guestsCount: form.guestsCount,
        format: form.format,
        name: form.name.trim(),
        phone: form.phone,
        wishes: form.wishes.trim() || undefined,
        afishaEventId: event?.id ?? null,
      });
      triggerHaptic('medium');
      showToast('Заявка отправлена. Подтверждение придёт сообщением от DIESEL ROOM.');
      onCreated?.();
      onClose();
    } catch (e) {
      setErrors([apiErrorMessage(e)]);
    } finally {
      setSending(false);
    }
  };

  return (
    <Sheet open={open} onClose={onClose} title={event ? `Бронь: ${event.title}` : 'Забронировать стол'}>
      <div className="space-y-3 pb-2">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Дата">
            <input type="date" className={inputClass} value={form.date} min={permDateString(now)} onChange={(e) => update('date', e.target.value)} />
          </Field>
          <Field label="Время">
            <input type="time" className={inputClass} value={form.time} step={900} onChange={(e) => update('time', e.target.value)} />
          </Field>
        </div>

        <Field label="Количество гостей">
          <div className="flex items-center gap-2">
            <Button variant="dark" size="sm" className="w-11 h-11 text-lg" onClick={() => update('guestsCount', Math.max(1, form.guestsCount - 1))} aria-label="Меньше гостей">−</Button>
            <input
              inputMode="numeric"
              className={`${inputClass} text-center font-bold`}
              value={form.guestsCount}
              onChange={(e) => update('guestsCount', Number(e.target.value.replace(/\D/g, '')) || 0)}
            />
            <Button variant="dark" size="sm" className="w-11 h-11 text-lg" onClick={() => update('guestsCount', Math.min(200, form.guestsCount + 1))} aria-label="Больше гостей">+</Button>
          </div>
        </Field>

        <Field label="Формат">
          <div className="grid grid-cols-2 gap-2">
            {BOOKING_FORMATS.map((f) => (
              <button
                key={f.value}
                type="button"
                onClick={() => update('format', f.value)}
                className={`rounded-xl border px-3 py-2.5 text-left text-xs font-bold transition ${
                  form.format === f.value ? 'border-dr-red bg-dr-red/15 text-white shadow-dr-glow' : 'border-dr-border bg-dr-card text-dr-muted'
                }`}
              >
                {f.label}
              </button>
            ))}
          </div>
        </Field>

        <Field label="Имя">
          <input className={inputClass} value={form.name} maxLength={60} placeholder="Как к вам обращаться" onChange={(e) => update('name', e.target.value)} />
        </Field>
        <Field label="Телефон">
          <input type="tel" inputMode="tel" className={inputClass} value={form.phone} placeholder="+7 902 000-00-00" onChange={(e) => update('phone', e.target.value)} />
        </Field>
        <Field label="Пожелания">
          <textarea className={`${inputClass} min-h-[72px]`} value={form.wishes} maxLength={500} placeholder="День рождения, стол у сцены…" onChange={(e) => update('wishes', e.target.value)} />
        </Field>

        {errors.map((e) => (
          <ErrorLine key={e} text={e} />
        ))}

        <Button className="w-full" disabled={sending} onClick={submit}>
          {sending ? 'Отправляем…' : 'Отправить заявку'}
        </Button>
        <a href={BRAND.phoneHref} className="flex items-center justify-center gap-1.5 text-xs text-dr-muted py-1">
          <Phone className="w-3.5 h-3.5" /> Или позвоните: {BRAND.phone}
        </a>
      </div>
    </Sheet>
  );
};
