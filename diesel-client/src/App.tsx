import React, { Component, useEffect, type ErrorInfo, type ReactNode } from 'react';
import { Header, TabBar, Toast } from './components/Chrome';
import { AfishaPanel } from './panels/AfishaPanel';
import { RatingPanel } from './panels/RatingPanel';
import { ProfilePanel } from './panels/ProfilePanel';
import { StaffPanel } from './panels/staff/StaffPanel';
import { useApp } from './store/useApp';
import { useBackHandler } from './utils/backNavigation';
import { APP_CACHE_VERSION_KEY, safeStorage } from './utils/safeStorage';
import { AntMascot } from './components/Brand';

const App: React.FC = () => {
  const { activeTab, setTab, boot, staff } = useApp();

  useEffect(() => {
    void boot();
  }, [boot]);

  // Сотрудник вышел из режима персонала — не оставляем его на пустой вкладке
  useEffect(() => {
    if (activeTab === 'staff' && !staff) setTab('profile');
  }, [activeTab, staff, setTab]);

  // «Назад» с любой вкладки возвращает на Афишу, а уже с Афиши — закрывает приложение
  useBackHandler(activeTab !== 'afisha', () => setTab('afisha'));

  return (
    <div className="min-h-screen bg-dr-bg text-white">
      <Toast />
      <Header />
      <main>
        {activeTab === 'afisha' && <AfishaPanel />}
        {activeTab === 'rating' && <RatingPanel />}
        {activeTab === 'profile' && <ProfilePanel />}
        {activeTab === 'staff' && <StaffPanel />}
      </main>
      <TabBar />
    </div>
  );
};

/** Последняя линия: при сбое рендера — экран с перезапуском и сбросом кэша данных. */
export class RootErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state = { error: null as Error | null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('[RootErrorBoundary]', error, info);
  }

  private resetAndReload = () => {
    for (const key of safeStorage.getAllKeys()) {
      if (key.startsWith('diesel_cached_') || key === APP_CACHE_VERSION_KEY) safeStorage.removeItem(key);
    }
    window.location.reload();
  };

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="min-h-screen bg-dr-bg flex flex-col items-center justify-center gap-4 p-6 text-center">
        <AntMascot className="w-28" />
        <div className="dr-title">Что-то пошло не так</div>
        <p className="text-sm text-dr-muted">Попробуйте перезапустить приложение.</p>
        <button type="button" className="dr-red-btn rounded-xl px-5 py-3" onClick={this.resetAndReload}>
          Перезапустить
        </button>
      </div>
    );
  }
}

export default App;
