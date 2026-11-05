import React, { useState } from 'react';
import { Check } from 'lucide-react';
import { Button, ErrorLine, Field, Pill, Sheet, inputClass } from '../../components/ui';
import { CoinIcon } from '../../components/Brand';
import { guestApi } from '../../api/endpoints';
import { apiErrorMessage } from '../../api/client';
import { useApp } from '../../store/useApp';
import { normalizePhone } from '../../domain/booking';
import { joinVkGroup, requestGroupMessages } from '../../utils/vkBridge';
import type { GuestTask, Profile } from '../../types';

/** Форма профиля: имя и телефон. За первое заполнение сервер начисляет разовый бонус. */
export const ProfileFormSheet: React.FC<{ profile: Profile; onClose: () => void }> = ({ profile, onClose }) => {
  const { setProfile, showToast } = useApp();
  const [firstName, setFirstName] = useState(profile.firstName === 'Гость' ? '' : profile.firstName);
  const [lastName, setLastName] = useState(profile.lastName);
  const [phone, setPhone] = useState(profile.phone ? `+${profile.phone}` : '');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const save = async () => {
    if (firstName.trim().length < 2) return setError('Укажите имя (от 2 символов).');
    if (!normalizePhone(phone)) return setError('Укажите телефон: +7 XXX XXX-XX-XX.');
    setBusy(true);
    setError(null);
    try {
      const before = profile.coins;
      const updated = await guestApi.saveProfile({ firstName: firstName.trim(), lastName: lastName.trim(), phone });
      setProfile(updated);
      showToast(updated.coins > before ? `Профиль заполнен: +${updated.coins - before} баллов!` : 'Профиль сохранён.');
      onClose();
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Sheet open onClose={onClose} title="Профиль гостя">
      <div className="space-y-3 pb-3">
        <Field label="Имя">
          <input className={inputClass} value={firstName} maxLength={100} autoComplete="given-name" onChange={(e) => setFirstName(e.target.value)} />
        </Field>
        <Field label="Фамилия (необязательно)">
          <input className={inputClass} value={lastName} maxLength={100} autoComplete="family-name" onChange={(e) => setLastName(e.target.value)} />
        </Field>
        <Field label="Телефон" hint="Виден только вам и сотрудникам заведения.">
          <input type="tel" inputMode="tel" className={inputClass} value={phone} autoComplete="tel" placeholder="+7 902 000-00-00" onChange={(e) => setPhone(e.target.value)} />
        </Field>
        <ErrorLine text={error} />
        <Button className="w-full" disabled={busy} onClick={save}>{busy ? 'Сохраняем…' : 'Сохранить'}</Button>
      </div>
    </Sheet>
  );
};

const Reward: React.FC<{ task: GuestTask }> = ({ task }) => (
  <div className="flex items-center gap-1 text-xs font-black shrink-0">
    <CoinIcon className="w-3.5 h-3.5" /> +{task.coins}
    {task.rating > 0 && <span className="text-[10px] font-bold text-dr-red-light">+{task.rating} рейт.</span>}
  </div>
);

/** «Как заработать баллы»: разовые задания со статусом и то, за что начисляется постоянно. */
export const EarnPoints: React.FC<{ profile: Profile }> = ({ profile }) => {
  const { setProfile, showToast } = useApp();
  const [formOpen, setFormOpen] = useState(false);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  // Начисляет только сервер: по событию ВК или по ответу API ВК, но не за само нажатие
  const verify = async (task: GuestTask, action: () => Promise<boolean>, notInVk: string) => {
    setBusyKey(task.key);
    try {
      const done = await action();
      const updated = await guestApi.verifyTasks();
      setProfile(updated);
      if (updated.tasks.find((t) => t.key === task.key)?.done) showToast(`Задание выполнено: +${task.coins} баллов!`);
      else showToast(done ? 'Проверим и начислим бонус, как только ВКонтакте подтвердит действие.' : notInVk);
    } catch (e) {
      showToast(apiErrorMessage(e), 'error');
    } finally {
      setBusyKey(null);
    }
  };

  const run = (task: GuestTask) => {
    if (task.action === 'profile') setFormOpen(true);
    if (task.action === 'join') void verify(task, () => joinVkGroup(), 'Откройте приложение во ВКонтакте, чтобы подписаться на группу.');
    if (task.action === 'messages') void verify(task, () => requestGroupMessages(), 'Откройте приложение во ВКонтакте, чтобы разрешить сообщения.');
  };

  const oneTime = profile.tasks.filter((t) => t.oneTime);
  const regular = profile.tasks.filter((t) => !t.oneTime);

  return (
    <>
      <div className="rounded-2xl bg-dr-card border border-dr-border divide-y divide-dr-border">
        {oneTime.map((task) => (
          <div key={task.key} className="flex items-center gap-2 px-3 py-2.5">
            <div className="flex-1 min-w-0">
              <div className="text-sm font-bold leading-tight">{task.title}</div>
              <div className="text-[11px] text-dr-muted">{task.hint}</div>
              <div className="mt-1"><Reward task={task} /></div>
            </div>
            {task.done ? (
              <Pill tone="green"><Check className="w-3 h-3" /> Выполнено</Pill>
            ) : (
              <Button size="sm" disabled={busyKey === task.key} onClick={() => run(task)}>Выполнить</Button>
            )}
          </div>
        ))}
      </div>

      <details className="mt-2 rounded-2xl bg-dr-card border border-dr-border px-3 py-2.5" open>
        <summary className="text-xs font-bold cursor-pointer">Начисляется постоянно</summary>
        <ul className="mt-2 space-y-2">
          {regular.map((task) => (
            <li key={task.key} className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="text-xs font-bold leading-tight">{task.title}</div>
                <div className="text-[11px] text-dr-muted leading-tight">{task.hint}</div>
              </div>
              <Reward task={task} />
            </li>
          ))}
        </ul>
      </details>

      {formOpen && <ProfileFormSheet profile={profile} onClose={() => setFormOpen(false)} />}
    </>
  );
};
