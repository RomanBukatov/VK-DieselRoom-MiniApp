import React from 'react';
import { Timer, TrendingUp } from 'lucide-react';
import { useNow } from '../store/useNow';
import { formatPoints, raceCountdown } from '../utils/format';
import type { RaceProgress } from '../types';

/** Живой отсчёт до конца «Гонки недели» (воскресенье 23:59 по Перми). */
export const RaceCountdown: React.FC<{ until: string | null | undefined }> = ({ until }) => {
  const now = useNow(1000);
  const left = raceCountdown(until, now);
  return (
    <div className="flex items-center justify-between gap-2 rounded-xl bg-dr-surface border border-dr-border px-3 py-2">
      <span className="text-[11px] text-dr-muted flex items-center gap-1.5">
        <Timer className="w-3.5 h-3.5 text-dr-red-light shrink-0" /> До конца гонки
      </span>
      <span className="text-sm font-black tabular-nums">{left ?? 'Подводим итоги…'}</span>
    </div>
  );
};

/** Плашка «До следующего места X очков» и ближайшая призовая зона. */
export const NextPlaceBanner: React.FC<{ race: RaceProgress | undefined; rank: number | null | undefined }> = ({ race, rank }) => {
  if (!rank) {
    return <div className="text-xs text-dr-muted">Наберите первые очки, чтобы войти в гонку: лайк, комментарий или визит.</div>;
  }
  if (!race?.pointsToNextPlace) {
    return <div className="text-xs font-bold text-amber-300">Вы лидер гонки. Удержите 1-е место до конца недели.</div>;
  }
  return (
    <div className="rounded-xl bg-dr-red/10 border border-dr-red/40 px-3 py-2">
      <div className="text-sm font-black flex items-center gap-1.5">
        <TrendingUp className="w-4 h-4 text-dr-red-light shrink-0" />
        До {race.nextRank}-го места {formatPoints(race.pointsToNextPlace)}
      </div>
      {race.nextPrizeTitle && race.pointsToNextPrize != null && race.nextPrizeRank !== race.nextRank && (
        <div className="text-[11px] text-dr-muted mt-0.5">
          До приза «{race.nextPrizeTitle}» (Топ-{race.nextPrizeRank}): {formatPoints(race.pointsToNextPrize)}
        </div>
      )}
    </div>
  );
};
