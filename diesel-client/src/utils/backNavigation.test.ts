import { describe, it, expect, beforeAll, beforeEach, afterAll, mock } from 'bun:test';
import vkBridge from '@vkontakte/vk-bridge';
import { backStackDepth, pushBackEntry, resetBackNavigationForTests } from './backNavigation';

// Мини-модель history браузера: pushState кладёт запись, back() асинхронно снимает её и шлёт popstate (как в WebView ВК)
let depth = 0;
let popListener: (() => void) | null = null;
const pendingPops: Array<() => void> = [];
const fakeWindow = {
  addEventListener: (type: string, fn: () => void) => {
    if (type === 'popstate') popListener = fn;
  },
  history: {
    pushState: () => {
      depth++;
    },
    back: () => {
      if (depth === 0) return;
      depth--;
      pendingPops.push(() => popListener?.());
    },
  },
};

/** Системная кнопка «Назад»: ВК вызывает history.back(). */
function pressSystemBack() {
  fakeWindow.history.back();
}
function flushPops() {
  while (pendingPops.length) pendingPops.shift()!();
}

const originalWindow = globalThis.window;
const originalSend = vkBridge.send;

describe('системная кнопка «Назад»', () => {
  beforeAll(() => {
    globalThis.window = fakeWindow as unknown as Window & typeof globalThis;
    vkBridge.send = mock(async () => ({ result: true })) as unknown as typeof vkBridge.send;
  });
  afterAll(() => {
    globalThis.window = originalWindow;
    vkBridge.send = originalSend;
  });
  beforeEach(() => {
    resetBackNavigationForTests();
    depth = 0;
    pendingPops.length = 0;
  });

  it('закрывает верхнее окно, а не выходит из приложения', () => {
    const closed: string[] = [];
    pushBackEntry(() => closed.push('tab'));
    pushBackEntry(() => closed.push('modal'));
    expect(depth).toBe(2);

    pressSystemBack();
    flushPops();
    expect(closed).toEqual(['modal']);

    pressSystemBack();
    flushPops();
    expect(closed).toEqual(['modal', 'tab']);
    expect(backStackDepth()).toBe(0);
    expect(depth).toBe(0); // дальше «Назад» уже закроет приложение — так и задумано
  });

  it('окно, закрытое крестиком, убирает свою запись и не срабатывает повторно', () => {
    const closed: string[] = [];
    pushBackEntry(() => closed.push('tab'));
    const release = pushBackEntry(() => closed.push('modal'));

    release(); // закрыли кнопкой в интерфейсе
    flushPops();
    expect(closed).toEqual([]);
    expect(depth).toBe(1);
    expect(backStackDepth()).toBe(1);

    pressSystemBack();
    flushPops();
    expect(closed).toEqual(['tab']);
  });

  it('повторный release после «Назад» ничего не ломает', () => {
    const release = pushBackEntry(() => undefined);
    pressSystemBack();
    flushPops();
    release();
    flushPops();
    expect(depth).toBe(0);
    expect(backStackDepth()).toBe(0);
  });

  it('сообщает ВК, есть ли куда возвращаться (свайп назад на iOS)', () => {
    const send = vkBridge.send as unknown as ReturnType<typeof mock>;
    send.mockClear();
    const release = pushBackEntry(() => undefined);
    expect(send).toHaveBeenLastCalledWith('VKWebAppSetSwipeSettings', { history: true });
    release();
    expect(send).toHaveBeenLastCalledWith('VKWebAppSetSwipeSettings', { history: false });
  });
});
