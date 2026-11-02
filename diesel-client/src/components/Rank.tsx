import React from 'react';
import clsx from 'clsx';
import { rankTone } from '../domain/privileges';
import { toneClass } from '../config/tones';
import { AntMascot } from './Brand';

/**
 * Маркер места в «Гонке недели»: цвет — по тиру привилегии (подиум — золото, серебро, бронза),
 * у призовых мест — фирменный муравей.
 */
export const RankBadge: React.FC<{ rank: number; weekly?: boolean; className?: string }> = ({ rank, weekly = true, className }) => {
  const tone = weekly ? rankTone(rank) : null;
  return (
    <span
      className={clsx(
        'relative inline-flex w-10 h-10 shrink-0 flex-col items-center justify-center rounded-xl border font-black leading-none',
        toneClass(tone),
        className,
      )}
    >
      {tone && <AntMascot white className="w-5 opacity-80 mb-0.5" />}
      <span className={clsx(tone ? 'text-[11px]' : 'text-xs')}>{rank}</span>
    </span>
  );
};
