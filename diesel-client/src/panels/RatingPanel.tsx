import React, { useState } from 'react';
import clsx from 'clsx';
import { Card, Empty, ErrorLine, Segmented, Spinner } from '../components/ui';
import { AntMascot } from '../components/Brand';
import { guestApi } from '../api/endpoints';
import { useSwr } from '../store/useSwr';
import { useApp } from '../store/useApp';
import { TIERS, rankBadge, streakText, tierRangeLabel } from '../domain/privileges';
import { formatPermDateTime } from '../utils/format';
import type { LeaderboardRow } from '../types';

type Kind = 'weekly' | 'total';

const Avatar: React.FC<{ url: string | null; name: string }> = ({ url, name }) =>
  url ? (
    <img src={url} alt="" className="w-9 h-9 rounded-full object-cover border border-dr-border" loading="lazy" />
  ) : (
    <span className="w-9 h-9 rounded-full bg-dr-surface border border-dr-border flex items-center justify-center text-xs font-bold text-dr-muted">
      {name.slice(0, 1).toUpperCase()}
    </span>
  );

const Row: React.FC<{ row: LeaderboardRow; weekly: boolean }> = ({ row, weekly }) => (
  <div
    className={clsx(
      'flex items-center gap-3 rounded-xl px-3 py-2.5 border',
      row.isMe ? 'bg-dr-red/10 border-dr-red/50 shadow-dr-glow' : 'bg-dr-card border-dr-border',
    )}
  >
    <div className="w-9 text-center">
      <div className={clsx('font-black', row.rank <= 3 ? 'text-lg' : 'text-sm text-dr-muted')}>{weekly && row.rank <= 3 ? rankBadge(row.rank) : `#${row.rank}`}</div>
    </div>
    <Avatar url={row.photoUrl} name={row.name} />
    <div className="flex-1 min-w-0">
      <div className="text-sm font-bold truncate">
        {row.name}
        {row.isMe && <span className="text-dr-red-light"> · вы</span>}
      </div>
      {weekly && row.privilegeLabel && <div className="text-[11px] text-dr-muted truncate">{row.privilegeLabel}</div>}
    </div>
    <div className="text-right">
      <div className="text-sm font-black">{row.rating.toLocaleString('ru-RU')}</div>
      <div className="text-[10px] text-dr-muted">очков</div>
    </div>
  </div>
);

export const RatingPanel: React.FC = () => {
  const [kind, setKind] = useState<Kind>('weekly');
  const weekly = useSwr(kind === 'weekly' ? 'diesel_cached_leaderboard' : null, guestApi.weekly, 60 * 60 * 1000);
  const total = useSwr(kind === 'total' ? 'diesel_cached_leaderboard_total' : null, guestApi.total, 60 * 60 * 1000);
  const board = kind === 'weekly' ? weekly : total;
  const profile = useApp((s) => s.profile);
  const data = board.data;
  const meInList = data?.rows.some((r) => r.isMe);
  const streak = profile ? streakText(profile.privilege) : null;

  return (
    <div className="px-4 pb-28">
      <Segmented<Kind>
        value={kind}
        onChange={setKind}
        options={[
          { value: 'weekly', label: '🔥 Гонка недели' },
          { value: 'total', label: '🏆 За всё время' },
        ]}
      />

      {kind === 'weekly' && (
        <Card className="mt-3" watermark>
          <div className="text-xs text-dr-muted">
            Призы — по месту в недельной гонке и переходят к тому, кто обгонит. Сброс: воскресенье 23:59 по Перми
            {data?.nextResetAt ? ` (${formatPermDateTime(data.nextResetAt)})` : ''}.
          </div>
          {profile?.privilege.label && (
            <div className="mt-2 text-sm font-bold">
              Ваша привилегия: <span className="text-dr-red-light">{profile.privilege.label}</span>
            </div>
          )}
          {streak && <div className="mt-1 text-xs font-bold text-amber-300">{streak} · спецприз 🥃 Бутылка Хаски 0.5</div>}
          <details className="mt-3">
            <summary className="text-xs font-bold text-white cursor-pointer">Призы по местам</summary>
            <ul className="mt-2 space-y-1.5">
              {TIERS.map((t) => (
                <li key={t.tier} className="text-xs flex gap-2">
                  <span className="w-5 text-center">{t.dot}</span>
                  <span className="w-20 shrink-0 text-dr-muted">{tierRangeLabel(t)}</span>
                  <span>
                    {t.emoji} {t.title} <span className="text-dr-muted">({t.conditions})</span>
                  </span>
                </li>
              ))}
            </ul>
          </details>
        </Card>
      )}

      <div className="mt-3 space-y-2">
        {!data && board.loading && (
          <div className="flex justify-center py-12">
            <Spinner className="w-10 h-10" />
          </div>
        )}
        <ErrorLine text={!data ? board.error : null} onRetry={board.reload} />
        {data && data.rows.length === 0 && (
          <Empty>
            <AntMascot className="w-14 h-14 mx-auto mb-2 opacity-60" />
            {kind === 'weekly' ? 'Гонка только началась — ставьте лайки, комментируйте и приходите в гости!' : 'Пока никто не набрал очков.'}
          </Empty>
        )}
        {data?.rows.map((row) => <Row key={row.userId} row={row} weekly={kind === 'weekly'} />)}
        {data?.me && !meInList && (
          <>
            <div className="text-center text-dr-muted text-xs">• • •</div>
            <Row row={data.me} weekly={kind === 'weekly'} />
          </>
        )}
      </div>
    </div>
  );
};
