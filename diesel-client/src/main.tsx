import './bootstrap';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import vkBridge from '@vkontakte/vk-bridge';
import './index.css';
import App, { RootErrorBoundary } from './App.tsx';

// Стандартная инициализация VK: первый сигнал VKWebAppInit уже отправлен встроенным скриптом в <head> (index.html),
// повторный вызов из бандла безопасен
vkBridge.send('VKWebAppInit').catch(() => undefined);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <RootErrorBoundary>
      <App />
    </RootErrorBoundary>
  </StrictMode>,
);
