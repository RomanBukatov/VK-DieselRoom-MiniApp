import { apiClient } from './client';
import type {
  AchievementRule, AdminGuest, Afisha, AfishaEvent, Banner, Booking, BookingFormat, BookingStatus, GuestCard, Leaderboard, LevelRule, LoyaltyRates, MenuLink,
  PointsTransaction, PrivilegeCode, Profile, ShopItem, ShopOrder, StaffAuditEntry, StaffLookup, StaffMember, StaffRole, VisitResult, VisitType,
} from '../types';

const get = async <T>(url: string, params?: Record<string, unknown>) => (await apiClient.get<T>(url, { params })).data;
const post = async <T>(url: string, body?: unknown) => (await apiClient.post<T>(url, body ?? {})).data;
const put = async <T>(url: string, body: unknown) => (await apiClient.put<T>(url, body)).data;
const del = async (url: string) => {
  await apiClient.delete(url);
};

export const guestApi = {
  sync: (body: { firstName?: string; lastName?: string; photoUrl?: string; referralCode?: string }) => post<Profile>('/api/users/me', body),
  me: () => get<Profile>('/api/users/me'),
  consent: () => post<Profile>('/api/users/me/consent', { acceptTerms: true, personalData: true, adult: true }),
  saveProfile: (body: { firstName: string; lastName?: string; phone: string }) => put<Profile>('/api/users/me/profile', body),
  applyReferral: (code: string) => post<Profile>('/api/users/me/referral', { code }),
  verifyTasks: () => post<Profile>('/api/users/me/tasks/verify'),
  privilegeCode: () => get<PrivilegeCode>('/api/users/me/privilege-code'),
  transactions: () => get<PointsTransaction[]>('/api/users/me/transactions'),
  referrals: () => get<{ name: string; rewardPaid: boolean; joinedAt: string }[]>('/api/users/me/referrals'),
  afisha: () => get<Afisha>('/api/afisha'),
  weekly: () => get<Leaderboard>('/api/ratings/weekly'),
  total: () => get<Leaderboard>('/api/ratings/total'),
  shopItems: () => get<ShopItem[]>('/api/shop/items'),
  purchase: (itemId: number) => post<{ order: ShopOrder; coins: number }>(`/api/shop/items/${itemId}/purchase`),
  orders: () => get<ShopOrder[]>('/api/shop/orders'),
  cancelOrder: (orderId: number) => post<{ order: ShopOrder; coins: number }>(`/api/shop/orders/${orderId}/cancel`),
  createBooking: (body: {
    date: string; time: string; guestsCount: number; format: BookingFormat; name: string; phone: string; wishes?: string; afishaEventId?: number | null;
  }) => post<Booking>('/api/bookings', body),
  myBookings: () => get<Booking[]>('/api/bookings/my'),
  cancelBooking: (id: number) => post<Booking>(`/api/bookings/${id}/cancel`),
};

export const staffApi = {
  session: () => get<{ role: StaffRole; name: string }>('/api/staff/session'),
  lookup: (q: string) => get<StaffLookup>('/api/staff/lookup', { q }),
  issueCoupon: (code: string) => post<ShopOrder>('/api/staff/coupons/issue', { code }),
  redeemPrivilege: (userId: number, code: string) => post<GuestCard>('/api/staff/privileges/redeem', { userId, code }),
  accrueVisit: (userId: number, receiptNumber: string, type: VisitType) => post<VisitResult>('/api/staff/visits', { userId, receiptNumber, type }),
  bookings: (status?: BookingStatus) => get<Booking[]>('/api/staff/bookings', status ? { status } : undefined),
  confirmBooking: (id: number, comment?: string) => post<Booking>(`/api/staff/bookings/${id}/confirm`, { comment }),
  rejectBooking: (id: number, comment?: string) => post<Booking>(`/api/staff/bookings/${id}/reject`, { comment }),
  completeBooking: (id: number) => post<Booking>(`/api/staff/bookings/${id}/complete`),
};

export type BannerInput = Omit<Banner, 'id'>;
export type MenuLinkInput = Omit<MenuLink, 'id' | 'key'>;
export type AfishaEventInput = Omit<AfishaEvent, 'id'>;
export type ShopItemInput = Omit<ShopItem, 'id'>;
export interface StaffMemberInput { name: string; role: StaffRole; vkId?: string | null; pin?: string | null; isActive: boolean }

export const adminApi = {
  banners: () => get<Banner[]>('/api/admin/banners'),
  saveBanner: (b: BannerInput, id?: number) => (id ? put<Banner>(`/api/admin/banners/${id}`, b) : post<Banner>('/api/admin/banners', b)),
  deleteBanner: (id: number) => del(`/api/admin/banners/${id}`),
  menu: () => get<MenuLink[]>('/api/admin/menu'),
  saveMenu: (id: number, m: MenuLinkInput) => put<MenuLink>(`/api/admin/menu/${id}`, m),
  events: () => get<AfishaEvent[]>('/api/admin/events'),
  saveEvent: (e: AfishaEventInput, id?: number) => (id ? put<AfishaEvent>(`/api/admin/events/${id}`, e) : post<AfishaEvent>('/api/admin/events', e)),
  deleteEvent: (id: number) => del(`/api/admin/events/${id}`),
  shopItems: () => get<ShopItem[]>('/api/admin/shop/items'),
  saveShopItem: (i: ShopItemInput, id?: number) => (id ? put<ShopItem>(`/api/admin/shop/items/${id}`, i) : post<ShopItem>('/api/admin/shop/items', i)),
  rates: () => get<LoyaltyRates>('/api/admin/rates'),
  saveRates: (r: LoyaltyRates) => put<LoyaltyRates>('/api/admin/rates', r),
  staff: () => get<StaffMember[]>('/api/admin/staff'),
  saveStaff: (s: StaffMemberInput, id?: number) => (id ? put<StaffMember>(`/api/admin/staff/${id}`, s) : post<StaffMember>('/api/admin/staff', s)),
  deleteStaff: (id: number) => del(`/api/admin/staff/${id}`),
  guests: (q?: string, skip = 0) => get<AdminGuest[]>('/api/admin/guests', { q: q || undefined, skip }),
  guestTransactions: (id: number) => get<PointsTransaction[]>(`/api/admin/guests/${id}/transactions`),
  adjustGuest: (id: number, body: { coins: number; rating: number; reason: string }) => post<AdminGuest>(`/api/admin/guests/${id}/adjust`, body),
  achievements: () => get<AchievementRule[]>('/api/admin/achievements'),
  saveAchievements: (rules: AchievementRule[]) => put<AchievementRule[]>('/api/admin/achievements', rules),
  levels: () => get<LevelRule[]>('/api/admin/levels'),
  saveLevels: (levels: LevelRule[]) => put<LevelRule[]>('/api/admin/levels', levels),
  uploadMedia: (dataUrl: string) => post<{ url: string }>('/api/admin/media', { dataUrl }),
  audit: (params: { action?: string; q?: string; beforeId?: number }) => get<StaffAuditEntry[]>('/api/admin/audit', params),
};
