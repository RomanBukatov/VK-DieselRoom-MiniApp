import React from 'react';
import { CalendarHeart, Trophy, UserRound, ShieldCheck } from 'lucide-react';
import clsx from 'clsx';
import { useApp } from '../store/useApp';
import { triggerHaptic } from '../utils/vkBridge';
import type { TabType } from '../types';
import { CoinIcon, Logo } from './Brand';

const TITLES: Record<TabType, { title: string; subtitle: string }> = {
  afisha: { title: 'Афиша', subtitle: 'События, меню и бронь столов' },
  rating: { title: 'Рейтинг', subtitle: 'Гонка недели за призы и общий зачёт' },
  profile: { title: 'Профиль', subtitle: 'Баллы, подарки и брони' },
  staff: { title: 'Управление', subtitle: 'Панель персонала' },
};

export const Header: React.FC = () => {
  const { activeTab, profile, staff } = useApp();
  const info = TITLES[activeTab];

  return (
    <header className="px-4 pt-[max(env(safe-area-inset-top),0.75rem)] pb-3">
      <div className="flex items-center justify-between gap-2 vk-controls-pad">
        <Logo className="h-9" />
        {profile && (
          <div className="flex items-center gap-1.5 bg-dr-card border border-dr-border rounded-full pl-1.5 pr-3 py-1 text-xs font-bold">
            <CoinIcon className="w-5 h-5" />
            {profile.coins.toLocaleString('ru-RU')}
          </div>
        )}
      </div>
      <div className="mt-3 flex items-end justify-between">
        <div>
          <h1 className="dr-title text-2xl leading-tight">{info.title}</h1>
          <p className="text-xs text-dr-muted mt-0.5">
            {activeTab === 'staff' && staff ? `${staff.role === 'Admin' ? 'Администратор' : 'Сотрудник'}: ${staff.name}` : info.subtitle}
          </p>
        </div>
      </div>
    </header>
  );
};

export const TabBar: React.FC = () => {
  const { activeTab, setTab, staff } = useApp();

  const tabs: { key: TabType; label: string; icon: React.ReactNode }[] = [
    { key: 'afisha', label: 'Афиша', icon: <CalendarHeart className="w-5 h-5" /> },
    { key: 'rating', label: 'Рейтинг', icon: <Trophy className="w-5 h-5" /> },
    { key: 'profile', label: 'Профиль', icon: <UserRound className="w-5 h-5" /> },
  ];
  if (staff) tabs.push({ key: 'staff', label: 'Управление', icon: <ShieldCheck className="w-5 h-5" /> });

  return (
    <div className="fixed bottom-3 left-0 right-0 z-40 flex justify-center px-4 pointer-events-none pb-[env(safe-area-inset-bottom)]">
      <nav className="pointer-events-auto flex w-full max-w-sm bg-dr-surface/95 backdrop-blur-xl border border-dr-border rounded-2xl p-1 shadow-2xl shadow-black/80">
        {tabs.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => {
              triggerHaptic('light');
              setTab(t.key);
              window.scrollTo({ top: 0 });
            }}
            className={clsx(
              'flex-1 flex flex-col items-center gap-0.5 py-1.5 rounded-xl text-[10px] font-bold transition',
              activeTab === t.key ? 'bg-dr-red text-white shadow-dr-glow' : 'text-dr-muted',
            )}
            aria-current={activeTab === t.key ? 'page' : undefined}
          >
            {t.icon}
            {t.label}
          </button>
        ))}
      </nav>
    </div>
  );
};

export const Toast: React.FC = () => {
  const toast = useApp((s) => s.toast);
  if (!toast) return null;
  return (
    <div className="fixed top-[max(env(safe-area-inset-top),0.75rem)] left-0 right-0 z-[60] flex justify-center px-4 pointer-events-none">
      <div
        role="status"
        className={clsx(
          'dr-sheet-in max-w-sm w-full rounded-xl px-4 py-3 text-sm font-semibold border shadow-2xl',
          toast.tone === 'ok' ? 'bg-dr-card border-dr-border text-white' : 'bg-dr-red/95 border-dr-red text-white',
        )}
      >
        {toast.text}
      </div>
    </div>
  );
};
