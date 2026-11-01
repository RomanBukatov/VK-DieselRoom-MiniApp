import React, { useState } from 'react';
import clsx from 'clsx';
import { useApp } from '../../store/useApp';
import { BookingsTool, IssueTool, VisitTool } from './StaffTools';
import { AdminPanel } from '../admin/AdminPanel';

type Tool = 'issue' | 'visit' | 'bookings' | 'admin';

/** Вкладка «Управление»: 3 инструмента сотрудника + раздел администратора. */
export const StaffPanel: React.FC = () => {
  const staff = useApp((s) => s.staff);
  const [tool, setTool] = useState<Tool>('issue');
  if (!staff) return null;

  const tools: { key: Tool; label: string }[] = [
    { key: 'issue', label: '🎁 Выдача' },
    { key: 'visit', label: '🧾 Визит' },
    { key: 'bookings', label: '📅 Брони' },
  ];
  if (staff.role === 'Admin') tools.push({ key: 'admin', label: '⚙️ Админ' });

  return (
    <div className="px-4 pb-28">
      <div className="grid gap-1.5 mb-3" style={{ gridTemplateColumns: `repeat(${tools.length}, minmax(0, 1fr))` }}>
        {tools.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTool(t.key)}
            className={clsx(
              'rounded-xl py-2.5 text-xs font-bold border transition',
              tool === t.key ? 'bg-dr-red border-dr-red shadow-dr-glow' : 'bg-dr-card border-dr-border text-dr-muted',
            )}
          >
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
