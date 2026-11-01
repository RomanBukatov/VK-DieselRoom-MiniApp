import React from 'react';

/**
 * Красный Муравей 🐜 — маскот DIESEL ROOM. Векторный: голова, грудь и брюшко, шесть лапок и усики.
 * Используется на иконке валюты, бейджах уровней и водяным знаком на карточках.
 */
export const AntMascot: React.FC<{ className?: string; color?: string; title?: string }> = ({ className, color = '#DC2626', title }) => (
  <svg viewBox="0 0 64 64" className={className} role={title ? 'img' : undefined} aria-hidden={title ? undefined : true} aria-label={title}>
    <g fill="none" stroke={color} strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
      {/* усики */}
      <path d="M27 14 C24 8 20 6 16 6" />
      <path d="M37 14 C40 8 44 6 48 6" />
      {/* лапки */}
      <path d="M27 29 L16 24 L10 28" />
      <path d="M37 29 L48 24 L54 28" />
      <path d="M27 33 L15 34 L9 40" />
      <path d="M37 33 L49 34 L55 40" />
      <path d="M28 37 L19 45 L16 54" />
      <path d="M36 37 L45 45 L48 54" />
    </g>
    <g fill={color}>
      <ellipse cx="32" cy="17" rx="6.5" ry="6" />
      <ellipse cx="32" cy="31" rx="5.5" ry="7" />
      <ellipse cx="32" cy="48" rx="9" ry="11" />
    </g>
    {/* блики — «металлический» индустриальный акцент */}
    <g fill="#ffffff" opacity="0.35">
      <ellipse cx="29.5" cy="15" rx="1.6" ry="1.2" />
      <ellipse cx="28.5" cy="44" rx="2.4" ry="3.2" />
    </g>
  </svg>
);

/** Логотип-надпись DIESEL ROOM с муравьём. */
export const Logo: React.FC<{ compact?: boolean }> = ({ compact }) => (
  <div className="flex items-center gap-1.5 select-none" aria-label="DIESEL ROOM">
    <AntMascot className={compact ? 'w-6 h-6' : 'w-7 h-7'} />
    <span className={`font-black tracking-[0.14em] leading-none ${compact ? 'text-sm' : 'text-base'}`}>
      <span className="text-white">DIESEL</span> <span className="text-dr-red-light">ROOM</span>
    </span>
  </div>
);

/** Иконка валюты «Баллы Diesel Room»: красная монета с муравьём. */
export const CoinIcon: React.FC<{ className?: string }> = ({ className = 'w-4 h-4' }) => (
  <span className={`inline-flex items-center justify-center rounded-full bg-dr-red shadow-dr-glow ${className}`} aria-hidden="true">
    <AntMascot className="w-[70%] h-[70%]" color="#ffffff" />
  </span>
);

/** Водяной знак: полупрозрачный муравей в углу карточки. */
export const AntWatermark: React.FC<{ className?: string }> = ({ className = '' }) => (
  <AntMascot className={`pointer-events-none absolute opacity-[0.07] ${className}`} color="#ffffff" />
);
