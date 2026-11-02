import React, { useEffect, useState } from 'react';
import { useNow } from '../../store/useNow';
import { Timer } from 'lucide-react';
import { Button, Card, Empty, ErrorLine, Pill, Segmented, Sheet } from '../../components/ui';
import { CoinIcon, PrizeVisual } from '../../components/Brand';
import { QrCode } from '../../components/QrCode';
import { guestApi } from '../../api/endpoints';
import { apiErrorMessage } from '../../api/client';
import { useSwr } from '../../store/useSwr';
import { useApp } from '../../store/useApp';
import { GIFT_TABS, displayCode, groupOrders, orderStatusLabel, type GiftsTab } from '../../domain/orders';
import { formatCoins, formatPermDateTime, timeLeft } from '../../utils/format';
import { triggerHaptic } from '../../utils/vkBridge';
import type { ShopItem, ShopOrder } from '../../types';

/** Магазин подарков: цена в баллах, остаток, «Обменять» → купон с QR на 7 дней. */
export const ShopSection: React.FC<{ onPurchased: (order: ShopOrder) => void }> = ({ onPurchased }) => {
  const { profile, patchCoins, showToast } = useApp();
  const items = useSwr('diesel_cached_shop_items', guestApi.shopItems, 60 * 60 * 1000);
  const [confirm, setConfirm] = useState<ShopItem | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const buy = async (item: ShopItem) => {
    setBusy(true);
    setError(null);
    try {
      const result = await guestApi.purchase(item.id);
      patchCoins(result.coins);
      triggerHaptic('heavy');
      showToast(`«${item.title}» ваш. Купон ${displayCode(result.order.code)} — в «Моих подарках».`);
      setConfirm(null);
      onPurchased(result.order);
      void items.reload();
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <ErrorLine text={!items.data ? items.error : null} onRetry={items.reload} />
      {items.data && items.data.length === 0 && <Empty>Подарки скоро появятся.</Empty>}
      <div className="grid grid-cols-2 gap-2">
        {items.data?.map((item) => {
          const enough = (profile?.coins ?? 0) >= item.priceCoins;
          const soldOut = item.stockCount <= 0;
          return (
            <Card key={item.id} className="!p-0 flex flex-col">
              <PrizeVisual icon={item.icon} imageUrl={item.imageUrl} title={item.title} className="rounded-t-2xl" />
              <div className="p-3 pt-2 flex flex-col flex-1">
              <div className="text-sm font-bold leading-tight">{item.title}</div>
              {item.description && <div className="text-[11px] text-dr-muted mt-0.5 line-clamp-2">{item.description}</div>}
              <div className="flex items-center gap-1 mt-2 text-sm font-black">
                <CoinIcon className="w-4 h-4" /> {item.priceCoins.toLocaleString('ru-RU')}
              </div>
              <div className="text-[10px] text-dr-muted">{soldOut ? 'Нет в наличии' : `Осталось: ${item.stockCount}`}</div>
              <Button size="sm" className="mt-2 w-full" disabled={soldOut || !enough} onClick={() => { setError(null); setConfirm(item); }}>
                {soldOut ? 'Закончился' : enough ? 'Обменять' : 'Не хватает баллов'}
              </Button>
              </div>
            </Card>
          );
        })}
      </div>

      <Sheet open={!!confirm} onClose={() => setConfirm(null)} title="Обмен баллов">
        {confirm && (
          <div className="pb-3 space-y-3">
            <div className="text-center">
              <PrizeVisual icon={confirm.icon} imageUrl={confirm.imageUrl} title={confirm.title} className="rounded-2xl" />
              <div className="font-black text-lg mt-3">{confirm.title}</div>
              <div className="text-sm text-dr-muted mt-1">
                Спишется {formatCoins(confirm.priceCoins)}. Купон действует 7 дней — покажите QR-код официанту.
              </div>
              <div className="text-[11px] text-dr-muted mt-1">Передумали? Отмените заказ — баллы вернутся мгновенно.</div>
            </div>
            <ErrorLine text={error} />
            <Button className="w-full" disabled={busy} onClick={() => buy(confirm)}>
              {busy ? 'Обмениваем…' : `Обменять за ${confirm.priceCoins.toLocaleString('ru-RU')}`}
            </Button>
          </div>
        )}
      </Sheet>
    </>
  );
};

/** «Мои подарки»: К получению (QR + таймер), Полученные, Отменённые / Истёкшие. */
export const MyGifts: React.FC<{ version: number }> = ({ version }) => {
  const { patchCoins, showToast, refreshProfile } = useApp();
  const orders = useSwr(`diesel_cached_orders`, guestApi.orders, 60 * 60 * 1000);
  const [tab, setTab] = useState<GiftsTab>('pending');
  const [shown, setShown] = useState<ShopOrder | null>(null);
  const [busy, setBusy] = useState(false);
  const now = useNow();
  const reload = orders.reload;

  useEffect(() => {
    if (version > 0) void reload();
  }, [version, reload]);

  const grouped = groupOrders(orders.data ?? [], now);

  const cancel = async (order: ShopOrder) => {
    setBusy(true);
    try {
      const result = await guestApi.cancelOrder(order.id);
      patchCoins(result.coins);
      showToast(order.priceCoins > 0 ? `Заказ отменён: ${formatCoins(order.priceCoins)} вернулись на баланс.` : 'Заказ отменён.');
      setShown(null);
      void reload();
    } catch (e) {
      showToast(apiErrorMessage(e), 'error');
      void reload();
      void refreshProfile();
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <Segmented<GiftsTab>
        value={tab}
        onChange={setTab}
        options={GIFT_TABS.map((t) => ({ value: t.key, label: `${t.label}${grouped[t.key].length ? ` · ${grouped[t.key].length}` : ''}` }))}
      />
      <div className="mt-2 space-y-2">
        <ErrorLine text={!orders.data ? orders.error : null} onRetry={orders.reload} />
        {grouped[tab].length === 0 && <Empty>{tab === 'pending' ? 'Нет подарков к получению — загляните в магазин!' : 'Пусто.'}</Empty>}
        {grouped[tab].map((order) => (
          <Card key={order.id} className="!p-3">
            <div className="flex items-center gap-3">
              <PrizeVisual icon={order.icon} imageUrl={order.imageUrl} title={order.title} className="!w-16 rounded-xl shrink-0" />
              <div className="flex-1 min-w-0">
                <div className="text-sm font-bold truncate">{order.title}</div>
                <div className="text-[11px] text-dr-muted">
                  {displayCode(order.code)} · {order.isPrize ? 'спецприз' : formatCoins(order.priceCoins)}
                </div>
                {tab === 'pending' ? (
                  <div className="text-[11px] text-amber-300 font-bold flex items-center gap-1 mt-0.5">
                    <Timer className="w-3 h-3" /> Осталось {timeLeft(order.expiresAt, now)}
                  </div>
                ) : (
                  <div className="mt-1">
                    <Pill tone={order.status === 'Issued' ? 'green' : 'muted'}>{orderStatusLabel(order, now)}</Pill>
                    <span className="text-[10px] text-dr-muted ml-1.5">{formatPermDateTime(order.closedAt ?? order.createdAt)}</span>
                  </div>
                )}
              </div>
              {tab === 'pending' && (
                <Button size="sm" onClick={() => setShown(order)}>
                  QR-код
                </Button>
              )}
            </div>
          </Card>
        ))}
      </div>

      <Sheet open={!!shown} onClose={() => setShown(null)} title="Купон на подарок">
        {shown && (
          <div className="flex flex-col items-center gap-3 pb-4">
            <div className="w-full flex items-center gap-3">
              <PrizeVisual icon={shown.icon} imageUrl={shown.imageUrl} title={shown.title} className="!w-16 rounded-xl shrink-0" />
              <div className="text-base font-black leading-snug">{shown.title}</div>
            </div>
            <QrCode value={displayCode(shown.code)} size={220} />
            <div className="text-3xl font-black tracking-[0.15em] text-dr-red-light">{displayCode(shown.code)}</div>
            <Pill tone="amber">
              <Timer className="w-3 h-3" /> Действует ещё {timeLeft(shown.expiresAt, now)} · до {formatPermDateTime(shown.expiresAt)}
            </Pill>
            <div className="text-xs text-dr-muted text-center">Покажите QR-код или продиктуйте код официанту.</div>
            <Button variant="dark" className="w-full" disabled={busy} onClick={() => cancel(shown)}>
              {busy ? 'Отменяем…' : shown.priceCoins > 0 ? `Отменить заказ и вернуть ${formatCoins(shown.priceCoins)}` : 'Отказаться от приза'}
            </Button>
          </div>
        )}
      </Sheet>
    </>
  );
};
