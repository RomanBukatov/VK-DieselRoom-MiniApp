import React, { useState } from 'react';
import { ChevronRight, ExternalLink, MapPin, Phone, Play } from 'lucide-react';
import { Button, Card, Empty, ErrorLine, SectionTitle, Spinner } from '../components/ui';
import { AntWatermark } from '../components/Brand';
import { KeyIcon } from '../components/Icon';
import { BookingSheet } from './BookingSheet';
import { guestApi } from '../api/endpoints';
import { useSwr } from '../store/useSwr';
import { useNow } from '../store/useNow';
import { isSafeUrl, visibleMenu } from '../domain/afisha';
import { openExternalUrl, triggerHaptic } from '../utils/vkBridge';
import { BRAND } from '../config/brand';
import type { AfishaEvent } from '../types';

export const AfishaPanel: React.FC = () => {
  const { data, error, loading, reload } = useSwr('diesel_cached_afisha', guestApi.afisha, 6 * 60 * 60 * 1000);
  const [bookingFor, setBookingFor] = useState<AfishaEvent | null>(null);
  const [bookingOpen, setBookingOpen] = useState(false);
  const now = useNow(60_000);

  const openBooking = (event: AfishaEvent | null) => {
    triggerHaptic('light');
    setBookingFor(event);
    setBookingOpen(true);
  };

  const banner = data?.banners[0];
  const menu = data ? visibleMenu(data.menu, now) : [];

  return (
    <div className="px-4 pb-28">
      {!data && loading && (
        <div className="flex justify-center py-16">
          <Spinner className="w-10 h-10" />
        </div>
      )}
      <ErrorLine text={!data ? error : null} onRetry={reload} />

      {banner && (
        <button
          type="button"
          onClick={() => isSafeUrl(banner.linkUrl) && openExternalUrl(banner.linkUrl)}
          className="relative w-full overflow-hidden rounded-2xl border border-dr-red/40 shadow-dr-glow text-left dr-hatch bg-gradient-to-br from-[#2a0b0b] via-dr-card to-dr-bg p-4 min-h-[120px]"
          style={banner.imageUrl ? { backgroundImage: `linear-gradient(90deg, rgba(9,9,11,.92), rgba(9,9,11,.35)), url(${banner.imageUrl})`, backgroundSize: 'cover' } : undefined}
        >
          <AntWatermark className="right-2 -bottom-3 w-28 h-28" />
          <span className="inline-flex items-center gap-1 text-[10px] font-black uppercase tracking-widest text-dr-red-light">
            <Play className="w-3 h-3 fill-current" /> Розыгрыш
          </span>
          <div className="dr-title text-lg mt-1 max-w-[80%] leading-snug">{banner.title}</div>
          {banner.subtitle && <div className="text-xs text-dr-muted mt-1 max-w-[80%]">{banner.subtitle}</div>}
          {banner.linkUrl && (
            <span className="mt-3 inline-flex items-center gap-1 text-xs font-bold text-white">
              Смотреть <ChevronRight className="w-4 h-4" />
            </span>
          )}
        </button>
      )}

      {menu.length > 0 && (
        <>
          <SectionTitle>Меню</SectionTitle>
          <div className={`grid gap-2 ${menu.length >= 3 ? 'grid-cols-3' : 'grid-cols-2'}`}>
            {menu.map((m) => (
              <button
                key={m.id}
                type="button"
                onClick={() => isSafeUrl(m.url) && openExternalUrl(m.url)}
                className="rounded-2xl bg-dr-card border border-dr-border px-2 py-3 flex flex-col items-center gap-1.5 active:scale-[0.97] transition"
              >
                <span className="w-10 h-10 rounded-xl bg-dr-red/10 border border-dr-red/40 flex items-center justify-center text-dr-red-light">
                  <KeyIcon name={m.icon} className="w-5 h-5" />
                </span>
                <span className="text-[11px] font-black uppercase tracking-wide text-center leading-tight">{m.title}</span>
              </button>
            ))}
          </div>
        </>
      )}

      {data && (
        <>
          <SectionTitle action={<Button size="sm" variant="dark" onClick={() => openBooking(null)}>Забронировать стол</Button>}>Афиша</SectionTitle>
          {data.events.length === 0 && <Empty>Скоро здесь появятся события.</Empty>}
          <div className="space-y-3">
            {data.events.map((ev) => (
              <Card key={ev.id} watermark>
                {ev.imageUrl && <img src={ev.imageUrl} alt="" className="w-full h-36 object-cover rounded-xl mb-3" loading="lazy" />}
                {ev.tagline && <div className="text-[11px] font-bold text-dr-red-light uppercase tracking-wide">{ev.tagline}</div>}
                <div className="dr-title text-[15px] mt-1 leading-snug">{ev.title}</div>
                {ev.description && <p className="text-xs text-dr-muted mt-1.5 leading-relaxed">{ev.description}</p>}
                <div className="flex flex-wrap gap-2 mt-3">
                  {ev.allowBooking && (
                    <Button size="sm" onClick={() => openBooking(ev)}>
                      Забронировать
                    </Button>
                  )}
                  {isSafeUrl(ev.productUrl) && (
                    <Button size="sm" variant="dark" onClick={() => openExternalUrl(ev.productUrl!)}>
                      Подробнее <ExternalLink className="w-3.5 h-3.5" />
                    </Button>
                  )}
                </div>
              </Card>
            ))}
          </div>

          <Card className="mt-4">
            <div className="flex items-center gap-2 text-sm">
              <MapPin className="w-4 h-4 text-dr-red-light shrink-0" /> {BRAND.city}, {BRAND.address}
            </div>
            <a href={BRAND.phoneHref} className="flex items-center gap-2 text-sm mt-2">
              <Phone className="w-4 h-4 text-dr-red-light shrink-0" /> {BRAND.phone}
            </a>
          </Card>
        </>
      )}

      <BookingSheet open={bookingOpen} event={bookingFor} onClose={() => setBookingOpen(false)} />
    </div>
  );
};
