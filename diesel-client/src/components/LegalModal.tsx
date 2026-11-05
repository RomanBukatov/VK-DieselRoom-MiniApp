import React, { useState } from 'react';
import { Button, ErrorLine, Segmented, Sheet } from './ui';
import { Logo } from './Brand';
import { apiErrorMessage } from '../api/client';
import { useApp } from '../store/useApp';
import { OPERATOR_REQUISITES, PERSONAL_DATA_CONSENT, PROGRAM_RULES, type LegalSection } from '../config/legal';
import { BRAND } from '../config/brand';
import { formatPermDateTime } from '../utils/format';

type DocTab = 'rules' | 'consent';

const LegalText: React.FC<{ sections: LegalSection[] }> = ({ sections }) => (
  <div className="space-y-3 text-xs leading-relaxed text-dr-muted">
    {sections.map((s) => (
      <section key={s.title}>
        <h4 className="font-bold text-white mb-1">{s.title}</h4>
        {s.paragraphs.map((p) => (
          <p key={p} className="mb-1.5">{p}</p>
        ))}
      </section>
    ))}
    {OPERATOR_REQUISITES && <p>{OPERATOR_REQUISITES}</p>}
  </div>
);

const Documents: React.FC<{ className?: string }> = ({ className }) => {
  const [tab, setTab] = useState<DocTab>('rules');
  return (
    <div className={className}>
      <Segmented<DocTab>
        value={tab}
        onChange={setTab}
        options={[
          { value: 'rules', label: 'Условия программы' },
          { value: 'consent', label: 'Персональные данные' },
        ]}
      />
      <div className="mt-3">
        <LegalText sections={tab === 'rules' ? PROGRAM_RULES : PERSONAL_DATA_CONSENT} />
      </div>
    </div>
  );
};

const Agreement: React.FC<{ checked: boolean; onChange: (v: boolean) => void; children: React.ReactNode }> = ({ checked, onChange, children }) => (
  <label className="flex items-start gap-2.5 text-xs leading-snug cursor-pointer">
    <input type="checkbox" className="w-5 h-5 mt-0.5 shrink-0 accent-red-600" checked={checked} onChange={(e) => onChange(e.target.checked)} />
    <span>{children}</span>
  </label>
);

/**
 * Экран согласия при первом запуске и при новой редакции условий. Закрыть его нельзя: без согласия сервер не хранит
 * имя и фото гостя, не принимает брони и обмен подарков. Дату согласия сохраняет сервер.
 */
export const LegalModal: React.FC = () => {
  const acceptConsent = useApp((s) => s.acceptConsent);
  const [terms, setTerms] = useState(false);
  const [personalData, setPersonalData] = useState(false);
  const [adult, setAdult] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await acceptConsent();
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[70] bg-dr-bg flex justify-center" role="dialog" aria-modal="true" aria-label="Условия программы лояльности">
      <div className="w-full max-w-xl flex flex-col h-full">
        <div className="px-4 pt-[max(env(safe-area-inset-top),0.75rem)] pb-3 vk-controls-pad">
          <Logo className="h-9" />
          <h1 className="dr-title text-lg mt-3 leading-tight">Программа лояльности</h1>
          <p className="text-xs text-dr-muted mt-0.5">Бар-ресторан {BRAND.name}, г. {BRAND.city}, {BRAND.address}</p>
        </div>

        <Documents className="flex-1 overflow-y-auto no-scrollbar px-4 pb-3" />

        <div className="border-t border-dr-border bg-dr-surface px-4 pt-3 safe-bottom space-y-2.5">
          <Agreement checked={terms} onChange={setTerms}>Принимаю условия программы лояльности</Agreement>
          <Agreement checked={personalData} onChange={setPersonalData}>Даю согласие на обработку персональных данных (<span className="whitespace-nowrap">152-ФЗ</span>)</Agreement>
          <Agreement checked={adult} onChange={setAdult}>Подтверждаю, что мне исполнилось 18 лет</Agreement>
          <ErrorLine text={error} />
          <Button className="w-full" disabled={busy || !terms || !personalData || !adult} onClick={submit}>
            {busy ? 'Сохраняем…' : 'Принять и продолжить'}
          </Button>
          <p className="text-[10px] text-dr-muted text-center pb-1">Если вы не согласны, просто закройте приложение.</p>
        </div>
      </div>
    </div>
  );
};

/** Те же документы для чтения из профиля. */
export const LegalDocsSheet: React.FC<{ open: boolean; onClose: () => void; acceptedAt: string | null }> = ({ open, onClose, acceptedAt }) => (
  <Sheet open={open} onClose={onClose} title="Условия и персональные данные">
    <Documents className="pb-3" />
    {acceptedAt && <p className="text-[11px] text-dr-muted pb-4">Вы приняли условия {formatPermDateTime(acceptedAt)}.</p>}
  </Sheet>
);
