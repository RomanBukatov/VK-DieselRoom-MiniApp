import type { TierTone } from '../domain/privileges';

/** Цвета маркеров уровней «Гонки недели». */
const TONE: Record<TierTone, string> = {
  gold: 'border-amber-400/70 bg-amber-400/15 text-amber-300',
  silver: 'border-zinc-300/60 bg-zinc-300/10 text-zinc-100',
  bronze: 'border-orange-500/60 bg-orange-700/20 text-orange-300',
  red: 'border-dr-red/50 bg-dr-red/10 text-dr-red-light',
  amber: 'border-yellow-500/40 bg-yellow-500/10 text-yellow-300',
  steel: 'border-sky-400/40 bg-sky-400/10 text-sky-300',
  muted: 'border-white/15 bg-white/5 text-zinc-300',
};

export const toneClass = (tone: TierTone | null) => (tone ? TONE[tone] : 'border-dr-border bg-transparent text-dr-muted');
