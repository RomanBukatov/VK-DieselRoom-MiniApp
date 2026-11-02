import React, { useState } from 'react';
import clsx from 'clsx';
import { CalendarCheck, Gift, Receipt, Settings } from 'lucide-react';
import { useApp } from '../../store/useApp';
import { BookingsTool, IssueTool, VisitTool } from './StaffTools';
import { AdminPanel } from '../admin/AdminPanel';

type Tool = 'issue' | 'visit' | 'bookings' | 'admin';

/** Вкладка «Управление»: 3 инструмента сотрудника + раздел администратора. */
export const StaffPanel: React.FC = () => {
  const staff = useApp((s) => s.staff);
  const [tool, setTool] = useState<Tool>('issue');
  if (!staff) return null;

  const tools: { key: Tool; label: string; icon: React.ReactNode }[] = [
    { key: 'issue', label: 'Выдача', icon: <Gift className="w-4 h-4" /> },
    { key: 'visit', label: 'Визит', icon: <Receipt className="w-4 h-4" /> },
    { key: 'bookings', label: 'Брони', icon: <CalendarCheck className="w-4 h-4" /> },
  ];
  if (staff.role === 'Admin') tools.push({ key: 'admin', label: 'Админ', icon: <Settings className="w-4 h-4" /> });

  return (
    <div className="px-4 pb-28">
      <div className="grid gap-1.5 mb-3" style={{ gridTemplateColumns: `repeat(${tools.length}, minmax(0, 1fr))` }}>
        {tools.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTool(t.key)}
            className={clsx(
              'rounded-xl py-2 text-[11px] font-black uppercase tracking-wide border transition flex flex-col items-center gap-1',
              tool === t.key ? 'bg-dr-red border-dr-red shadow-dr-glow' : 'bg-dr-card border-dr-border text-dr-muted',
            )}
          >
            {t.icon}
            {t.label}
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
