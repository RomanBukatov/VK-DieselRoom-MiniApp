import React, { useCallback, useEffect, useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { Button, Card, Empty, ErrorLine, Field, Pill, Segmented, Sheet, Spinner, inputClass } from '../../components/ui';
import { adminApi, type AfishaEventInput, type BannerInput, type ShopItemInput, type StaffMemberInput } from '../../api/endpoints';
import { apiErrorMessage } from '../../api/client';
import { useApp } from '../../store/useApp';
import { BOOKING_FORMATS } from '../../domain/booking';
import { formatPermDateTime } from '../../utils/format';
import type { AfishaEvent, Banner, LoyaltyRates, MenuLink, ShopItem, StaffAuditEntry, StaffMember } from '../../types';

type Section = 'afisha' | 'shop' | 'rates' | 'staff' | 'audit';

/** Загрузка списка + перезагрузка после сохранения. */
function useAdminList<T>(load: () => Promise<T>) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [version, setVersion] = useState(0);
  useEffect(() => {
    let cancelled = false;
    load().then(
      (fresh) => {
        if (cancelled) return;
        setData(fresh);
        setError(null);
      },
      (e: unknown) => !cancelled && setError(apiErrorMessage(e)),
    );
    return () => {
      cancelled = true;
    };
  }, [load, version]);
  const reload = useCallback(async () => setVersion((v) => v + 1), []);
  return { data, error, reload };
}

function useSaver() {
  const showToast = useApp((s) => s.showToast);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const run = async (work: () => Promise<unknown>, ok = 'Сохранено.') => {
    setBusy(true);
    setError(null);
    try {
      await work();
      showToast(ok);
      return true;
    } catch (e) {
      setError(apiErrorMessage(e));
      return false;
    } finally {
      setBusy(false);
    }
  };
  return { busy, error, run, setError };
}

const Check: React.FC<{ label: string; checked: boolean; onChange: (v: boolean) => void }> = ({ label, checked, onChange }) => (
  <label className="flex items-center gap-2 text-sm">
    <input type="checkbox" className="w-4 h-4 accent-red-600" checked={checked} onChange={(e) => onChange(e.target.checked)} />
    {label}
  </label>
);

export const AdminPanel: React.FC = () => {
  const [section, setSection] = useState<Section>('afisha');
  return (
    <div className="space-y-3">
      <div className="flex gap-1.5 overflow-x-auto no-scrollbar">
        {([
          ['afisha', 'Афиша'],
          ['shop', 'Подарки'],
          ['rates', 'Ставки'],
          ['staff', 'Сотрудники'],
          ['audit', 'Журнал'],
        ] as [Section, string][]).map(([key, label]) => (
          <button
            key={key}
            type="button"
            onClick={() => setSection(key)}
            className={`shrink-0 rounded-full px-3 py-1.5 text-xs font-bold border ${section === key ? 'bg-white text-black border-white' : 'border-dr-border text-dr-muted'}`}
          >
            {label}
          </button>
        ))}
      </div>
      {section === 'afisha' && <AfishaAdmin />}
      {section === 'shop' && <ShopAdmin />}
      {section === 'rates' && <RatesAdmin />}
      {section === 'staff' && <StaffAdmin />}
      {section === 'audit' && <AuditLog />}
    </div>
  );
};

// ---------------- Афиша: баннер, меню, карточки ----------------

const emptyBanner: BannerInput = { title: '', subtitle: '', linkUrl: '', imageUrl: '', isActive: true, sortOrder: 0 };
const emptyEvent: AfishaEventInput = {
  title: '', tagline: '', description: '', productUrl: '', imageUrl: '', allowBooking: true, defaultFormat: 'Hall', isActive: true, sortOrder: 0,
};

const AfishaAdmin: React.FC = () => {
  const banners = useAdminList(adminApi.banners);
  const menu = useAdminList(adminApi.menu);
  const events = useAdminList(adminApi.events);
  const saver = useSaver();
  const [banner, setBanner] = useState<{ id?: number; value: BannerInput } | null>(null);
  const [link, setLink] = useState<MenuLink | null>(null);
  const [event, setEvent] = useState<{ id?: number; value: AfishaEventInput } | null>(null);

  return (
    <div className="space-y-4">
      <div>
        <div className="flex items-center justify-between mb-2">
          <div className="text-xs font-bold uppercase text-dr-muted">Верхний баннер</div>
          <Button size="sm" variant="dark" onClick={() => setBanner({ value: { ...emptyBanner } })}>
            <Plus className="w-3.5 h-3.5" /> Баннер
          </Button>
        </div>
        <ErrorLine text={banners.error} onRetry={banners.reload} />
        {banners.data?.map((b: Banner) => (
          <Card key={b.id} className="!p-3 mb-2">
            <div className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="text-sm font-bold truncate">{b.title}</div>
                <div className="text-[11px] text-dr-muted truncate">{b.linkUrl}</div>
              </div>
              <div className="flex items-center gap-1.5 shrink-0">
                <Pill tone={b.isActive ? 'green' : 'muted'}>{b.isActive ? 'показан' : 'скрыт'}</Pill>
                <Button size="sm" variant="dark" onClick={() => setBanner({ id: b.id, value: { ...b } })}>Изм.</Button>
              </div>
            </div>
          </Card>
        ))}
      </div>

      <div>
        <div className="text-xs font-bold uppercase text-dr-muted mb-2">Ссылки меню</div>
        {menu.data?.map((m) => (
          <Card key={m.id} className="!p-3 mb-2">
            <div className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="text-sm font-bold">{m.emoji} {m.title}</div>
                <div className="text-[11px] text-dr-muted truncate">{m.url}</div>
                {m.hiddenAfter && <div className="text-[10px] text-amber-300">Скрыть после {formatPermDateTime(m.hiddenAfter)}</div>}
              </div>
              <Button size="sm" variant="dark" onClick={() => setLink({ ...m })}>Изм.</Button>
            </div>
          </Card>
        ))}
      </div>

      <div>
        <div className="flex items-center justify-between mb-2">
          <div className="text-xs font-bold uppercase text-dr-muted">Карточки афиши</div>
          <Button size="sm" variant="dark" onClick={() => setEvent({ value: { ...emptyEvent } })}>
            <Plus className="w-3.5 h-3.5" /> Событие
          </Button>
        </div>
        {events.data?.map((e: AfishaEvent) => (
          <Card key={e.id} className="!p-3 mb-2">
            <div className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="text-sm font-bold truncate">{e.title}</div>
                <div className="text-[11px] text-dr-muted truncate">{e.tagline}</div>
              </div>
              <div className="flex items-center gap-1.5 shrink-0">
                {!e.isActive && <Pill>скрыто</Pill>}
                <Button size="sm" variant="dark" onClick={() => setEvent({ id: e.id, value: { ...e } })}>Изм.</Button>
              </div>
            </div>
          </Card>
        ))}
      </div>

      <Sheet open={!!banner} onClose={() => setBanner(null)} title={banner?.id ? 'Баннер' : 'Новый баннер'}>
        {banner && (
          <div className="space-y-3 pb-3">
            <Field label="Заголовок"><input className={inputClass} value={banner.value.title} onChange={(e) => setBanner({ ...banner, value: { ...banner.value, title: e.target.value } })} /></Field>
            <Field label="Подзаголовок"><input className={inputClass} value={banner.value.subtitle ?? ''} onChange={(e) => setBanner({ ...banner, value: { ...banner.value, subtitle: e.target.value } })} /></Field>
            <Field label="Ссылка (https://)"><input className={inputClass} value={banner.value.linkUrl ?? ''} onChange={(e) => setBanner({ ...banner, value: { ...banner.value, linkUrl: e.target.value } })} /></Field>
            <Field label="Картинка (https://, необязательно)"><input className={inputClass} value={banner.value.imageUrl ?? ''} onChange={(e) => setBanner({ ...banner, value: { ...banner.value, imageUrl: e.target.value } })} /></Field>
            <Check label="Показывать" checked={banner.value.isActive} onChange={(v) => setBanner({ ...banner, value: { ...banner.value, isActive: v } })} />
            <ErrorLine text={saver.error} />
            <Button className="w-full" disabled={saver.busy} onClick={async () => (await saver.run(() => adminApi.saveBanner(banner.value, banner.id))) && (setBanner(null), void banners.reload())}>Сохранить</Button>
            {banner.id && (
              <Button variant="ghost" className="w-full" onClick={async () => (await saver.run(() => adminApi.deleteBanner(banner.id!), 'Баннер удалён.')) && (setBanner(null), void banners.reload())}>
                <Trash2 className="w-4 h-4" /> Удалить
              </Button>
            )}
          </div>
        )}
      </Sheet>

      <Sheet open={!!link} onClose={() => setLink(null)} title="Кнопка меню">
        {link && (
          <div className="space-y-3 pb-3">
            <div className="grid grid-cols-[72px_1fr] gap-2">
              <Field label="Эмодзи"><input className={inputClass} value={link.emoji} onChange={(e) => setLink({ ...link, emoji: e.target.value })} /></Field>
              <Field label="Название"><input className={inputClass} value={link.title} onChange={(e) => setLink({ ...link, title: e.target.value })} /></Field>
            </div>
            <Field label="Ссылка (https://)"><input className={inputClass} value={link.url} onChange={(e) => setLink({ ...link, url: e.target.value })} /></Field>
            <Field label="Скрыть после (UTC, ISO), пусто — не скрывать" hint="Банкетное меню: 2026-12-31T18:59:59Z = 31.12 23:59:59 по Перми">
              <input className={inputClass} value={link.hiddenAfter ?? ''} onChange={(e) => setLink({ ...link, hiddenAfter: e.target.value || null })} />
            </Field>
            <Check label="Показывать" checked={link.isActive} onChange={(v) => setLink({ ...link, isActive: v })} />
            <ErrorLine text={saver.error} />
            <Button
              className="w-full"
              disabled={saver.busy}
              onClick={async () =>
                (await saver.run(() => adminApi.saveMenu(link.id, { emoji: link.emoji, title: link.title, url: link.url, hiddenAfter: link.hiddenAfter, isActive: link.isActive, sortOrder: link.sortOrder }))) &&
                (setLink(null), void menu.reload())
              }
            >
              Сохранить
            </Button>
          </div>
        )}
      </Sheet>

      <Sheet open={!!event} onClose={() => setEvent(null)} title={event?.id ? 'Событие афиши' : 'Новое событие'}>
        {event && (
          <div className="space-y-3 pb-3">
            <Field label="Название"><input className={inputClass} value={event.value.title} onChange={(e) => setEvent({ ...event, value: { ...event.value, title: e.target.value } })} /></Field>
            <Field label="Подпись (цена, дни)"><input className={inputClass} value={event.value.tagline ?? ''} onChange={(e) => setEvent({ ...event, value: { ...event.value, tagline: e.target.value } })} /></Field>
            <Field label="Описание"><textarea className={`${inputClass} min-h-[72px]`} value={event.value.description ?? ''} onChange={(e) => setEvent({ ...event, value: { ...event.value, description: e.target.value } })} /></Field>
            <Field label="Ссылка на товар / пост (https://)"><input className={inputClass} value={event.value.productUrl ?? ''} onChange={(e) => setEvent({ ...event, value: { ...event.value, productUrl: e.target.value } })} /></Field>
            <Field label="Картинка (https://)"><input className={inputClass} value={event.value.imageUrl ?? ''} onChange={(e) => setEvent({ ...event, value: { ...event.value, imageUrl: e.target.value } })} /></Field>
            <Field label="Формат брони по умолчанию">
              <select className={inputClass} value={event.value.defaultFormat} onChange={(e) => setEvent({ ...event, value: { ...event.value, defaultFormat: e.target.value as AfishaEventInput['defaultFormat'] } })}>
                {BOOKING_FORMATS.map((f) => <option key={f.value} value={f.value}>{f.label}</option>)}
              </select>
            </Field>
            <Field label="Порядок"><input inputMode="numeric" className={inputClass} value={event.value.sortOrder} onChange={(e) => setEvent({ ...event, value: { ...event.value, sortOrder: Number(e.target.value) || 0 } })} /></Field>
            <Check label="Кнопка «Забронировать»" checked={event.value.allowBooking} onChange={(v) => setEvent({ ...event, value: { ...event.value, allowBooking: v } })} />
            <Check label="Показывать" checked={event.value.isActive} onChange={(v) => setEvent({ ...event, value: { ...event.value, isActive: v } })} />
            <ErrorLine text={saver.error} />
            <Button className="w-full" disabled={saver.busy} onClick={async () => (await saver.run(() => adminApi.saveEvent(event.value, event.id))) && (setEvent(null), void events.reload())}>Сохранить</Button>
            {event.id && (
              <Button variant="ghost" className="w-full" onClick={async () => (await saver.run(() => adminApi.deleteEvent(event.id!), 'Событие удалено.')) && (setEvent(null), void events.reload())}>
                <Trash2 className="w-4 h-4" /> Удалить
              </Button>
            )}
          </div>
        )}
      </Sheet>
    </div>
  );
};

// ---------------- Каталог подарков ----------------

const emptyItem: ShopItemInput = { title: '', description: '', icon: '🎁', priceCoins: 100, stockCount: 10, isActive: true, sortOrder: 0 };

const ShopAdmin: React.FC = () => {
  const items = useAdminList(adminApi.shopItems);
  const saver = useSaver();
  const [edit, setEdit] = useState<{ id?: number; value: ShopItemInput } | null>(null);

  return (
    <div className="space-y-2">
      <Button size="sm" variant="dark" onClick={() => setEdit({ value: { ...emptyItem } })}>
        <Plus className="w-3.5 h-3.5" /> Подарок
      </Button>
      <ErrorLine text={items.error} onRetry={items.reload} />
      {items.data?.map((i: ShopItem) => (
        <Card key={i.id} className="!p-3">
          <div className="flex items-center justify-between gap-2">
            <div className="min-w-0">
              <div className="text-sm font-bold truncate">{i.icon} {i.title}</div>
              <div className="text-[11px] text-dr-muted">{i.priceCoins} баллов · остаток {i.stockCount}</div>
            </div>
            <div className="flex items-center gap-1.5 shrink-0">
              {!i.isActive && <Pill>скрыт</Pill>}
              {i.stockCount === 0 && <Pill tone="red">нет</Pill>}
              <Button size="sm" variant="dark" onClick={() => setEdit({ id: i.id, value: { ...i } })}>Изм.</Button>
            </div>
          </div>
        </Card>
      ))}

      <Sheet open={!!edit} onClose={() => setEdit(null)} title={edit?.id ? 'Подарок' : 'Новый подарок'}>
        {edit && (
          <div className="space-y-3 pb-3">
            <div className="grid grid-cols-[72px_1fr] gap-2">
              <Field label="Иконка"><input className={inputClass} value={edit.value.icon} onChange={(e) => setEdit({ ...edit, value: { ...edit.value, icon: e.target.value } })} /></Field>
              <Field label="Название"><input className={inputClass} value={edit.value.title} onChange={(e) => setEdit({ ...edit, value: { ...edit.value, title: e.target.value } })} /></Field>
            </div>
            <Field label="Описание"><input className={inputClass} value={edit.value.description ?? ''} onChange={(e) => setEdit({ ...edit, value: { ...edit.value, description: e.target.value } })} /></Field>
            <div className="grid grid-cols-2 gap-2">
              <Field label="Цена, баллов"><input inputMode="numeric" className={inputClass} value={edit.value.priceCoins} onChange={(e) => setEdit({ ...edit, value: { ...edit.value, priceCoins: Number(e.target.value.replace(/\D/g, '')) || 0 } })} /></Field>
              <Field label="Остаток"><input inputMode="numeric" className={inputClass} value={edit.value.stockCount} onChange={(e) => setEdit({ ...edit, value: { ...edit.value, stockCount: Number(e.target.value.replace(/\D/g, '')) || 0 } })} /></Field>
            </div>
            <Check label="Показывать в магазине" checked={edit.value.isActive} onChange={(v) => setEdit({ ...edit, value: { ...edit.value, isActive: v } })} />
            <ErrorLine text={saver.error} />
            <Button className="w-full" disabled={saver.busy} onClick={async () => (await saver.run(() => adminApi.saveShopItem(edit.value, edit.id))) && (setEdit(null), void items.reload())}>Сохранить</Button>
          </div>
        )}
      </Sheet>
    </div>
  );
};

// ---------------- Ставки начисления ----------------

const RATE_FIELDS: { key: keyof LoyaltyRates; label: string }[] = [
  { key: 'likeCoins', label: '❤️ Лайк — баллы' },
  { key: 'likeRating', label: '❤️ Лайк — рейтинг' },
  { key: 'commentCoins', label: '💬 Коммент — баллы' },
  { key: 'commentRating', label: '💬 Коммент — рейтинг' },
  { key: 'repostCoins', label: '↗️ Репост — баллы' },
  { key: 'repostRating', label: '↗️ Репост — рейтинг' },
  { key: 'visitCoins', label: '🧾 Визит — баллы' },
  { key: 'visitRating', label: '🧾 Визит — рейтинг' },
  { key: 'referralCoins', label: '🤝 Друг — баллы' },
  { key: 'referralRating', label: '🤝 Друг — рейтинг' },
  { key: 'maxPaidCommentsPer24h', label: 'Комментов в сутки' },
  { key: 'dailySocialCoinsCap', label: 'Потолок соцбаллов/сутки' },
  { key: 'minRewardPostId', label: 'Мин. ID поста (архив)' },
];

const RatesAdmin: React.FC = () => {
  const rates = useAdminList(adminApi.rates);
  const saver = useSaver();
  const [draft, setDraft] = useState<LoyaltyRates | null>(null);
  const value = draft ?? rates.data;

  if (!value) return <ErrorLine text={rates.error} onRetry={rates.reload} />;
  return (
    <Card>
      <div className="grid grid-cols-2 gap-2">
        {RATE_FIELDS.map((f) => (
          <Field key={f.key} label={f.label}>
            <input inputMode="numeric" className={inputClass} value={value[f.key]} onChange={(e) => setDraft({ ...value, [f.key]: Number(e.target.value.replace(/\D/g, '')) || 0 })} />
          </Field>
        ))}
      </div>
      <ErrorLine text={saver.error} />
      <Button className="w-full mt-3" disabled={saver.busy || !draft} onClick={async () => (await saver.run(() => adminApi.saveRates(value), 'Ставки сохранены.')) && (setDraft(null), void rates.reload())}>
        Сохранить ставки
      </Button>
    </Card>
  );
};

// ---------------- Сотрудники ----------------

const emptyStaff: StaffMemberInput = { name: '', role: 'Staff', vkId: '', pin: '', isActive: true };

const StaffAdmin: React.FC = () => {
  const list = useAdminList(adminApi.staff);
  const saver = useSaver();
  const [edit, setEdit] = useState<{ id?: number; value: StaffMemberInput; hasPin?: boolean } | null>(null);

  return (
    <div className="space-y-2">
      <Button size="sm" variant="dark" onClick={() => setEdit({ value: { ...emptyStaff } })}>
        <Plus className="w-3.5 h-3.5" /> Сотрудник
      </Button>
      <ErrorLine text={list.error} onRetry={list.reload} />
      {list.data?.length === 0 && <Empty>Сотрудников пока нет. Владельцы входят по VK ID из настроек сервера.</Empty>}
      {list.data?.map((s: StaffMember) => (
        <Card key={s.id} className="!p-3">
          <div className="flex items-center justify-between gap-2">
            <div className="min-w-0">
              <div className="text-sm font-bold truncate">{s.name}</div>
              <div className="text-[11px] text-dr-muted">
                {s.role === 'Admin' ? 'Администратор' : 'Сотрудник'}
                {s.vkId ? ` · VK ${s.vkId}` : ''}
                {s.hasPin ? ' · ПИН' : ''}
              </div>
            </div>
            <div className="flex items-center gap-1.5 shrink-0">
              {!s.isActive && <Pill tone="red">отключён</Pill>}
              <Button size="sm" variant="dark" onClick={() => setEdit({ id: s.id, hasPin: s.hasPin, value: { name: s.name, role: s.role, vkId: s.vkId ?? '', pin: '', isActive: s.isActive } })}>Изм.</Button>
            </div>
          </div>
        </Card>
      ))}

      <Sheet open={!!edit} onClose={() => setEdit(null)} title={edit?.id ? 'Сотрудник' : 'Новый сотрудник'}>
        {edit && (
          <div className="space-y-3 pb-3">
            <Field label="Имя (видно в журнале)"><input className={inputClass} value={edit.value.name} onChange={(e) => setEdit({ ...edit, value: { ...edit.value, name: e.target.value } })} /></Field>
            <Segmented
              value={edit.value.role}
              onChange={(role) => setEdit({ ...edit, value: { ...edit.value, role } })}
              options={[
                { value: 'Staff', label: 'Сотрудник' },
                { value: 'Admin', label: 'Администратор' },
              ]}
            />
            <Field label="VK ID (цифры)" hint="Вход со своего телефона без ПИН-кода.">
              <input inputMode="numeric" className={inputClass} value={edit.value.vkId ?? ''} onChange={(e) => setEdit({ ...edit, value: { ...edit.value, vkId: e.target.value } })} />
            </Field>
            <Field label={edit.hasPin ? 'Новый ПИН-код (пусто — не менять)' : 'ПИН-код (4–8 цифр)'} hint="Для общего планшета у бара.">
              <input inputMode="numeric" className={inputClass} value={edit.value.pin ?? ''} maxLength={8} onChange={(e) => setEdit({ ...edit, value: { ...edit.value, pin: e.target.value.replace(/\D/g, '') } })} />
            </Field>
            <Check label="Активен" checked={edit.value.isActive} onChange={(v) => setEdit({ ...edit, value: { ...edit.value, isActive: v } })} />
            <ErrorLine text={saver.error} />
            <Button
              className="w-full"
              disabled={saver.busy}
              onClick={async () =>
                (await saver.run(() => adminApi.saveStaff({ ...edit.value, vkId: edit.value.vkId || null, pin: edit.value.pin || null }, edit.id))) &&
                (setEdit(null), void list.reload())
              }
            >
              Сохранить
            </Button>
          </div>
        )}
      </Sheet>
    </div>
  );
};

// ---------------- Журнал аудита ----------------

const ACTION_LABELS: Record<string, string> = {
  VisitAccrued: '🧾 Визит по чеку',
  GiftIssued: '🎁 Выдан подарок',
  PrivilegeIssued: '🏆 Выдана привилегия',
  BookingConfirmed: '✅ Бронь подтверждена',
  BookingRejected: '✕ Бронь отклонена',
  BookingCompleted: 'Гость посетил',
  SettingsChanged: '⚙️ Ставки',
  StaffChanged: '👤 Сотрудники',
  CatalogChanged: '🎁 Каталог',
  AfishaChanged: '🎉 Афиша',
};

const AuditLog: React.FC = () => {
  const [action, setAction] = useState('');
  const [q, setQ] = useState('');
  const [rows, setRows] = useState<StaffAuditEntry[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loaded, setLoaded] = useState(false);

  const load = useCallback(async (append: boolean, beforeId?: number) => {
    try {
      const page = await adminApi.audit({ action: action || undefined, q: q.trim() || undefined, beforeId });
      setRows((prev) => (append ? [...prev, ...page] : page));
      setHasMore(page.length >= 100);
      setError(null);
      setLoaded(true);
    } catch (e) {
      setError(apiErrorMessage(e));
    }
  }, [action, q]);

  useEffect(() => {
    const id = setTimeout(() => void load(false), 300);
    return () => clearTimeout(id);
  }, [load]);

  return (
    <div className="space-y-2">
      <div className="grid grid-cols-2 gap-2">
        <select className={inputClass} value={action} onChange={(e) => setAction(e.target.value)}>
          <option value="">Все действия</option>
          {Object.entries(ACTION_LABELS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
        <input className={inputClass} placeholder="Сотрудник, гость, № чека" value={q} onChange={(e) => setQ(e.target.value)} />
      </div>
      <ErrorLine text={error} />
      {!loaded && !error && (
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      )}
      {loaded && rows.length === 0 && !error && <Empty>Записей нет.</Empty>}
      {rows.map((r) => (
        <div key={r.id} className="rounded-xl bg-dr-card border border-dr-border px-3 py-2 text-xs">
          <div className="flex justify-between gap-2">
            <b>{ACTION_LABELS[r.action] ?? r.action}</b>
            <span className="text-dr-muted shrink-0">{formatPermDateTime(r.createdAt)}</span>
          </div>
          <div className="mt-0.5">
            <span className="text-dr-red-light font-bold">{r.staffName}</span>
            {r.guestName && <> → {r.guestName}</>}
            {r.receiptNumber && <> · чек №{r.receiptNumber}</>}
          </div>
          {r.details && <div className="text-dr-muted mt-0.5">{r.details}</div>}
        </div>
      ))}
      {hasMore && (
        <Button variant="dark" className="w-full" onClick={() => load(true, rows[rows.length - 1]?.id)}>
          Показать ещё
        </Button>
      )}
    </div>
  );
};
