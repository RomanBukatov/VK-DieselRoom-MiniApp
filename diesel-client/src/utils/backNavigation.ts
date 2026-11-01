import { useEffect, useRef } from 'react';
import vkBridge from '@vkontakte/vk-bridge';

/**
 * Системная кнопка «Назад» (Android) и свайп назад (iOS) в мини-приложении ВК.
 *
 * ВК на «Назад» вызывает history.back(), а если истории нет — закрывает приложение. Поэтому каждое открытое окно
 * (и уход с главной вкладки) кладёт запись в history; «Назад» снимает верхнюю запись и вызывает её обработчик —
 * закрывает окно. Если окно закрыли кнопкой в интерфейсе, его запись убирается тихим history.back().
 */

interface BackEntry {
  onBack: () => void;
}

const stack: BackEntry[] = [];
let ignoredPops = 0;
let installed = false;

function syncVkSwipe(): void {
  // На iOS свайп от края закрывает приложение, пока ВК не сказали, что внутри есть куда возвращаться
  try {
    const send = vkBridge.send as unknown as (method: string, props?: Record<string, unknown>) => Promise<unknown>;
    send('VKWebAppSetSwipeSettings', { history: stack.length > 0 }).catch(() => undefined);
  } catch {
    // вне ВК метода нет
  }
}

function handlePopState(): void {
  if (ignoredPops > 0) {
    ignoredPops--;
    return;
  }
  const top = stack.pop();
  syncVkSwipe();
  top?.onBack();
}

function install(): void {
  if (installed || typeof window === 'undefined') return;
  installed = true;
  window.addEventListener('popstate', handlePopState);
}

/** Кладёт запись «Назад». Возвращает функцию, которая убирает её, если окно закрыли не кнопкой «Назад». */
export function pushBackEntry(onBack: () => void): () => void {
  if (typeof window === 'undefined') return () => undefined;
  install();
  const entry: BackEntry = { onBack };
  stack.push(entry);
  try {
    window.history.pushState({ drBack: stack.length }, '');
  } catch {
    // history недоступна (редкие встроенные браузеры) — «Назад» просто останется системным
  }
  syncVkSwipe();

  return () => {
    const index = stack.indexOf(entry);
    if (index === -1) return; // запись уже снята кнопкой «Назад»
    stack.splice(index, 1);
    syncVkSwipe();
    ignoredPops++;
    try {
      window.history.back();
    } catch {
      ignoredPops--;
    }
  };
}

/** Пока active === true, системная «Назад» вызывает onBack (обычно — закрыть окно) вместо выхода из приложения. */
export function useBackHandler(active: boolean, onBack: () => void): void {
  const handlerRef = useRef(onBack);
  useEffect(() => {
    handlerRef.current = onBack;
  });
  useEffect(() => {
    if (!active) return;
    return pushBackEntry(() => handlerRef.current());
  }, [active]);
}

/** Только для тестов: сбросить состояние модуля. */
export function resetBackNavigationForTests(): void {
  stack.length = 0;
  ignoredPops = 0;
}

export function backStackDepth(): number {
  return stack.length;
}
