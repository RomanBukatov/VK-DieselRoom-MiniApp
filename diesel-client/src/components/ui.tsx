import React, { useEffect } from 'react';
import { X } from 'lucide-react';
import clsx from 'clsx';
import { useBackHandler } from '../utils/backNavigation';
import { AntWatermark } from './Brand';

export const Card: React.FC<React.PropsWithChildren<{ className?: string; watermark?: boolean; glow?: boolean }>> = ({ children, className, watermark, glow }) => (
  <div className={clsx('relative overflow-hidden rounded-2xl bg-dr-card border border-dr-border p-4', glow && 'shadow-dr-glow border-dr-red/40', className)}>
    {watermark && <AntWatermark className="-right-3 -bottom-4 w-24 h-24" />}
    <div className="relative">{children}</div>
  </div>
);

export const SectionTitle: React.FC<React.PropsWithChildren<{ action?: React.ReactNode }>> = ({ children, action }) => (
  <div className="flex items-center justify-between mt-6 mb-2.5 px-1">
    <h2 className="dr-title text-[13px] text-white flex items-center gap-2">
      <span className="inline-block w-1 h-3.5 bg-dr-red rounded-sm" />
      {children}
    </h2>
    {action}
  </div>
);

type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'red' | 'ghost' | 'dark'; size?: 'sm' | 'md' };

export const Button: React.FC<ButtonProps> = ({ variant = 'red', size = 'md', className, children, ...rest }) => (
  <button
    type="button"
    {...rest}
    className={clsx(
      'rounded-xl font-bold transition active:scale-[0.98] disabled:opacity-45 disabled:active:scale-100 inline-flex items-center justify-center gap-1.5',
      size === 'sm' ? 'px-3 py-1.5 text-xs' : 'px-4 py-3 text-sm',
      variant === 'red' && 'dr-red-btn',
      variant === 'dark' && 'bg-dr-surface border border-dr-border text-white',
      variant === 'ghost' && 'text-dr-muted hover:text-white',
      className,
    )}
  >
    {children}
  </button>
);

export function Segmented<T extends string>({ value, options, onChange }: {
  value: T;
  options: { value: T; label: string }[];
  onChange: (v: T) => void;
}) {
  return (
    <div className="flex bg-dr-surface border border-dr-border rounded-xl p-1 gap-1">
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          onClick={() => onChange(o.value)}
          className={clsx(
            'flex-1 py-2 px-2 rounded-lg text-xs font-bold transition',
            value === o.value ? 'bg-dr-red text-white shadow-dr-glow' : 'text-dr-muted',
          )}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

export const Empty: React.FC<React.PropsWithChildren> = ({ children }) => (
  <div className="text-center text-dr-muted text-sm py-8 px-4">{children}</div>
);

export const Spinner: React.FC<{ className?: string }> = ({ className = 'w-6 h-6' }) => (
  <span className={clsx('inline-block rounded-full border-2 border-dr-red/20 border-t-dr-red animate-spin', className)} />
);

export const ErrorLine: React.FC<{ text: string | null | undefined; onRetry?: () => void }> = ({ text, onRetry }) =>
  text ? (
    <div className="rounded-xl border border-dr-red/40 bg-dr-red/10 text-dr-red-light text-xs px-3 py-2 flex items-center justify-between gap-2">
      <span>{text}</span>
      {onRetry && (
        <button type="button" className="underline shrink-0" onClick={onRetry}>
          Повторить
        </button>
      )}
    </div>
  ) : null;

export const Field: React.FC<React.PropsWithChildren<{ label: string; hint?: string }>> = ({ label, hint, children }) => (
  <label className="block">
    <span className="block text-[11px] uppercase tracking-wider text-dr-muted mb-1">{label}</span>
    {children}
    {hint && <span className="block text-[11px] text-dr-muted/70 mt-1">{hint}</span>}
  </label>
);

export const inputClass =
  'w-full bg-dr-surface border border-dr-border rounded-xl px-3 py-2.5 text-white placeholder:text-dr-muted/60 outline-none focus:border-dr-red/70';

/**
 * Нижняя шторка. Системная «Назад» (Android) / свайп (iOS) закрывает её, а не приложение — стек backNavigation.
 */
export const Sheet: React.FC<React.PropsWithChildren<{ open: boolean; title: string; onClose: () => void }>> = ({ open, title, onClose, children }) => {
  useBackHandler(open, onClose);

  useEffect(() => {
    if (!open) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [open]);

  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center" role="dialog" aria-modal="true" aria-label={title}>
      <button type="button" aria-label="Закрыть" className="absolute inset-0 bg-black/70" onClick={onClose} />
      <div className="dr-sheet-in relative w-full max-w-lg max-h-[92vh] overflow-y-auto no-scrollbar bg-dr-surface border-t border-dr-border rounded-t-3xl px-4 pt-4 safe-bottom">
        <div className="flex items-center justify-between mb-3 sticky top-0 bg-dr-surface pb-2 z-10">
          <h3 className="dr-title text-sm">{title}</h3>
          <button type="button" onClick={onClose} className="w-8 h-8 rounded-full bg-dr-card border border-dr-border flex items-center justify-center" aria-label="Закрыть">
            <X className="w-4 h-4" />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
};

export const Pill: React.FC<React.PropsWithChildren<{ tone?: 'red' | 'muted' | 'green' | 'amber' }>> = ({ tone = 'muted', children }) => (
  <span
    className={clsx(
      'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-bold border',
      tone === 'red' && 'bg-dr-red/15 border-dr-red/40 text-dr-red-light',
      tone === 'muted' && 'bg-white/5 border-dr-border text-dr-muted',
      tone === 'green' && 'bg-emerald-500/10 border-emerald-500/30 text-emerald-400',
      tone === 'amber' && 'bg-amber-500/10 border-amber-500/30 text-amber-300',
    )}
  >
    {children}
  </span>
);
