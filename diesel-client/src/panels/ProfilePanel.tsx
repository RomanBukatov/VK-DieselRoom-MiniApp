import React, { useEffect, useState } from 'react';
import { Bell, KeyRound } from 'lucide-react';
import { Button, ErrorLine, SectionTitle, Spinner } from '../components/ui';
import { useApp } from '../store/useApp';
import { WalletCard } from './profile/Wallet';
import { MyGifts, ShopSection } from './profile/Gifts';
import { AchievementsGrid, History, MyBookings, ReferralCard, StaffLoginSheet } from './profile/Extras';
import { requestGroupMessages } from '../utils/vkBridge';

export const ProfilePanel: React.FC = () => {
  const { profile, profileError, refreshProfile, booting, staff, logoutStaff, showToast } = useApp();
  const [giftsVersion, setGiftsVersion] = useState(0);
  const [staffLogin, setStaffLogin] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);

  useEffect(() => {
    void refreshProfile();
  }, [refreshProfile]);

  if (!profile) {
    return (
      <div className="px-4 pb-28">
        {booting ? (
          <div className="flex justify-center py-16">
            <Spinner className="w-10 h-10" />
          </div>
        ) : (
          <ErrorLine text={profileError ?? 'Профиль недоступен.'} onRetry={refreshProfile} />
        )}
      </div>
    );
  }

  return (
    <div className="px-4 pb-28">
      <WalletCard profile={profile} />

      <SectionTitle>Магазин подарков</SectionTitle>
      <ShopSection
        onPurchased={() => {
          setGiftsVersion((v) => v + 1);
          void refreshProfile();
        }}
      />

      <SectionTitle>Мои подарки</SectionTitle>
      <MyGifts version={giftsVersion} />

      <SectionTitle>Мои брони</SectionTitle>
      <MyBookings version={giftsVersion} />

      <SectionTitle>Рефералка</SectionTitle>
      <ReferralCard profile={profile} />

      <SectionTitle>Достижения</SectionTitle>
      <AchievementsGrid profile={profile} />

      <SectionTitle action={<Button size="sm" variant="ghost" onClick={() => setHistoryOpen((v) => !v)}>{historyOpen ? 'Скрыть' : 'Показать'}</Button>}>
        История баллов
      </SectionTitle>
      {historyOpen && <History />}

      <div className="mt-6 space-y-2">
        <Button
          variant="dark"
          className="w-full"
          onClick={async () => showToast((await requestGroupMessages()) ? '🔔 Уведомления от DIESEL ROOM включены.' : 'Откройте приложение во ВКонтакте, чтобы включить уведомления.')}
        >
          <Bell className="w-4 h-4" /> Уведомления о бронях и рейтинге
        </Button>
        {staff ? (
          <Button variant="ghost" className="w-full" onClick={logoutStaff}>
            Выйти из режима персонала ({staff.name})
          </Button>
        ) : (
          <Button variant="ghost" className="w-full" onClick={() => setStaffLogin(true)}>
            <KeyRound className="w-4 h-4" /> Вход для персонала
          </Button>
        )}
      </div>

      <StaffLoginSheet open={staffLogin} onClose={() => setStaffLogin(false)} />
    </div>
  );
};
