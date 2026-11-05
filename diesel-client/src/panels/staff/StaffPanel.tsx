import React, { useEffect, useState } from 'react';
import clsx from 'clsx';
import { BookOpenText, CalendarCheck, Gift, Receipt, Settings } from 'lucide-react';
import { useApp } from '../../store/useApp';
import { staffApi } from '../../api/endpoints';
import { BookingsTool, IssueTool, VisitTool } from './StaffTools';
import { AdminPanel } from '../admin/AdminPanel';

type Tool = 'issue' | 'visit' | 'bookings' | 'admin';

const PENDING_REFRESH_MS = 30_000;

/** Сколько заявок ждёт подтверждения: счётчик на вкладке «Брони» обновляется сам. */
function usePendingBookings(): number {
  const [count, setCount] = useState(0);
  useEffect(() => {
    let cancelled = false;
    const load = () =>
      staffApi.bookings('Pending').then(
        (list) => {
          if (!cancelled) setCount(list.length);
        },
        () => {
          // Сеть вернётся — счётчик обновится на следующем проходе
        },
      );
    void load();
    const id = setInterval(load, PENDING_REFRESH_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, []);
  return count;
}

/** Памятка для персонала: что проверить при выдаче и начислении. */
const StaffMemo: React.FC = () => (
  <details className="mb-3 rounded-xl bg-dr-card border border-dr-border px-3 py-2.5">
    <summary className="text-xs font-bold cursor-pointer flex items-center gap-1.5">
      <BookOpenText className="w-4 h-4 text-dr-red-light" /> Памятка для персонала
    </summary>
    <ul className="mt-2 space-y-1.5 text-[11px] text-dr-muted list-disc pl-4">
      <li><b className="text-white">Подарок.</b> Отсканируйте QR или введите код #DR-XXXX с экрана гостя, сверьте подарок и нажмите «Подтвердить выдачу». Код срабатывает один раз.</li>
      <li><b className="text-white">Привилегия.</b> Попросите показать код из профиля (6 цифр или QR): он меняется каждую минуту. Сверьте фото и имя с гостем. Номера карты для выдачи недостаточно.</li>
      <li><b className="text-white">18+.</b> Алкоголь и кальян — только совершеннолетним: при отметке 18+ проверьте паспорт. Нет документа — не выдавайте.</li>
      <li><b className="text-white">Визит.</b> Начисляйте только по закрытому чеку, номер вводите точно: один чек проводится один раз.</li>
      <li><b className="text-white">Брони.</b> Заявка должна быть обработана: нажмите «Подтвердить» или «Отклонить», гость получит сообщение. Новые заявки приходят сообщением от сообщества.</li>
      <li><b className="text-white">Себе нельзя.</b> Начислить или выдать самому себе система не даст, попросите коллегу. Все действия пишутся в журнал.</li>
    </ul>
  </details>
);

/** Вкладка «Управление»: 3 инструмента сотрудника + раздел администратора. */
export const StaffPanel: React.FC = () => {
  const staff = useApp((s) => s.staff);
  const [tool, setTool] = useState<Tool>('issue');
  const pending = usePendingBookings();
  if (!staff) return null;

  const tools: { key: Tool; label: string; icon: React.ReactNode; badge?: number }[] = [
    { key: 'issue', label: 'Выдача', icon: <Gift className="w-4 h-4" /> },
    { key: 'visit', label: 'Визит', icon: <Receipt className="w-4 h-4" /> },
    { key: 'bookings', label: 'Брони', icon: <CalendarCheck className="w-4 h-4" />, badge: pending },
  ];
  if (staff.role === 'Admin') tools.push({ key: 'admin', label: 'Админ', icon: <Settings className="w-4 h-4" /> });

  return (
    <div className="px-4 pb-28">
      <StaffMemo />
      <div className="grid gap-1.5 mb-3" style={{ gridTemplateColumns: `repeat(${tools.length}, minmax(0, 1fr))` }}>
        {tools.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTool(t.key)}
            className={clsx(
              'relative rounded-xl py-2 text-[11px] font-black uppercase tracking-wide border transition flex flex-col items-center gap-1',
              tool === t.key ? 'bg-dr-red border-dr-red shadow-dr-glow' : 'bg-dr-card border-dr-border text-dr-muted',
            )}
          >
            {t.icon}
            {t.label}
            {!!t.badge && (
              <span className="absolute -top-1.5 -right-1 min-w-5 h-5 px-1 rounded-full bg-white text-black text-[11px] font-black flex items-center justify-center" aria-label={`Ожидают: ${t.badge}`}>
                {t.badge}
              </span>
            )}
          </button>
        ))}
      </div>
      {tool === 'issue' && <IssueTool />}
      {tool === 'visit' && <VisitTool />}
      {tool === 'bookings' && <BookingsTool />}
      {tool === 'admin' && staff.role === 'Admin' && <AdminPanel />}
    </div>
  );
};
