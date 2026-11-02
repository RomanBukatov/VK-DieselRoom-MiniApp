import React from 'react';
import clsx from 'clsx';
import { KeyIcon } from './Icon';
import { BRAND_ASSETS } from '../config/brand';

/** Красный Муравей — фирменный маскот DIESEL ROOM. */
export const AntMascot: React.FC<{ className?: string; white?: boolean; title?: string }> = ({ className, white, title }) => (
  <img
    src={white ? BRAND_ASSETS.antWhite : BRAND_ASSETS.ant}
    alt={title ?? ''}
    aria-hidden={title ? undefined : true}
    className={clsx('object-contain select-none', className)}
    draggable={false}
  />
);

/** Логотип DIESEL ROOM. */
export const Logo: React.FC<{ className?: string }> = ({ className = 'h-9' }) => (
  <img src={BRAND_ASSETS.logo} alt="DIESEL ROOM" className={clsx('w-auto object-contain select-none', className)} draggable={false} />
);

/** Иконка валюты «Баллы Diesel Room»: красная монета с белым муравьём. */
export const CoinIcon: React.FC<{ className?: string }> = ({ className = 'w-4 h-4' }) => (
  <span className={clsx('inline-flex items-center justify-center rounded-full bg-dr-red ring-1 ring-white/15 shrink-0', className)} aria-hidden="true">
    <AntMascot white className="w-[78%]" />
  </span>
);

/** Водяной знак: полупрозрачный муравей на фоне карточки. */
export const AntWatermark: React.FC<{ className?: string }> = ({ className = '' }) => (
  <AntMascot white className={clsx('pointer-events-none absolute opacity-[0.06]', className)} />
);

/**
 * Превью подарка: фото блюда из меню, а если фото нет — брендированная заглушка
 * (графитовый фон, штриховка, водяной знак-муравей и векторная иконка).
 */
export const PrizeVisual: React.FC<{ icon: string; imageUrl?: string | null; title: string; className?: string }> = ({ icon, imageUrl, title, className }) =>
  imageUrl ? (
    <img src={imageUrl} alt={title} loading="lazy" className={clsx('w-full aspect-[4/3] object-cover', className)} />
  ) : (
    <div className={clsx('relative w-full aspect-[4/3] overflow-hidden bg-gradient-to-br from-[#1f1f24] via-dr-card to-dr-bg dr-hatch flex items-center justify-center', className)}>
      <AntMascot white className="absolute -right-4 -bottom-2 w-28 opacity-[0.07]" />
      <span className="relative w-14 h-14 rounded-2xl border border-dr-red/50 bg-dr-red/10 flex items-center justify-center text-dr-red-light">
        <KeyIcon name={icon} className="w-7 h-7" />
      </span>
    </div>
  );
