// Контракты API DIESEL ROOM (перечисления приходят строками, даты — ISO 8601 UTC)

export type StaffRole = 'Guest' | 'Staff' | 'Admin';
export type PrivilegeTier =
  | 'None' | 'BusinessLunch' | 'UnlimitedCoffee' | 'Hookah' | 'Dessert' | 'BeerOrCoffee' | 'TeaPot' | 'Cocktail';
export type BookingFormat = 'Hall' | 'Karaoke' | 'Party' | 'Banquet';
export type BookingStatus = 'Pending' | 'Confirmed' | 'Rejected' | 'Completed' | 'Cancelled';
export type ShopOrderStatus = 'Pending' | 'Issued' | 'Cancelled' | 'Expired';
export type VisitType = 'Regular' | 'BusinessLunch' | 'PartyKaraoke';

export type TabType = 'afisha' | 'rating' | 'profile' | 'staff';

export interface Privilege {
  tier: PrivilegeTier;
  icon: string | null;
  title: string | null;
  conditions: string | null;
  label: string | null;
  rank: number | null;
  allowed: boolean;
  used: number;
  limit: number | null;
  reason: string | null;
  firstPlaceStreak: number;
  streakTarget: number;
}

export interface Achievement {
  key: string;
  icon: string;
  title: string;
  description: string;
  unlocked: boolean;
  progress: number;
  target: number;
}

export interface Profile {
  id: number;
  vkId: string;
  cardNumber: string;
  firstName: string;
  lastName: string;
  photoUrl: string | null;
  phone: string | null;
  coins: number;
  totalCoinsEarned: number;
  weeklyRating: number;
  totalRating: number;
  weeklyRank: number | null;
  totalRank: number | null;
  visitsCount: number;
  privilege: Privilege;
  achievements: Achievement[];
  referral: { code: string; invitedCount: number; rewardedCount: number; rewardCoins: number; rewardRating: number };
  role: StaffRole;
  staffName: string | null;
  nextWeeklyResetAt: string;
}

export interface PointsTransaction {
  id: number;
  coins: number;
  rating: number;
  type: string;
  description: string;
  createdAt: string;
}

export interface LeaderboardRow {
  rank: number;
  userId: number;
  name: string;
  photoUrl: string | null;
  rating: number;
  tier: PrivilegeTier;
  privilegeLabel: string | null;
  isMe: boolean;
}

export interface PrivilegeInfo {
  tier: PrivilegeTier;
  topRank: number;
  bottomRank: number;
  icon: string;
  title: string;
  conditions: string;
}

export interface Leaderboard {
  kind: 'weekly' | 'total';
  rows: LeaderboardRow[];
  me: LeaderboardRow | null;
  weekStart: string;
  nextResetAt: string;
  tiers: PrivilegeInfo[];
}

export interface ShopItem {
  id: number;
  title: string;
  description: string | null;
  icon: string;
  imageUrl: string | null;
  priceCoins: number;
  stockCount: number;
  isActive: boolean;
  sortOrder: number;
}

export interface ShopOrder {
  id: number;
  code: string;
  title: string;
  icon: string;
  imageUrl: string | null;
  priceCoins: number;
  status: ShopOrderStatus;
  createdAt: string;
  expiresAt: string;
  issuedAt: string | null;
  closedAt: string | null;
  isPrize: boolean;
}

export interface Banner {
  id: number;
  title: string;
  subtitle: string | null;
  linkUrl: string | null;
  imageUrl: string | null;
  isActive: boolean;
  sortOrder: number;
}

export interface MenuLink {
  id: number;
  key: string;
  icon: string;
  title: string;
  url: string;
  hiddenAfter: string | null;
  isActive: boolean;
  sortOrder: number;
}

export interface AfishaEvent {
  id: number;
  title: string;
  tagline: string | null;
  description: string | null;
  productUrl: string | null;
  imageUrl: string | null;
  allowBooking: boolean;
  defaultFormat: BookingFormat;
  isActive: boolean;
  sortOrder: number;
}

export interface Afisha {
  banners: Banner[];
  menu: MenuLink[];
  events: AfishaEvent[];
}

export interface Booking {
  id: number;
  userId: number;
  date: string;
  time: string;
  guestsCount: number;
  format: BookingFormat;
  name: string;
  phone: string;
  wishes: string | null;
  afishaEventId: number | null;
  status: BookingStatus;
  statusLabel: string;
  staffComment: string | null;
  processedByStaffName: string | null;
  createdAt: string;
}

export interface GuestCard {
  userId: number;
  name: string;
  cardNumber: string;
  phone: string | null;
  photoUrl: string | null;
  coins: number;
  weeklyRating: number;
  visitsCount: number;
  privilege: Privilege;
}

export interface StaffLookup {
  order: { order: ShopOrder; userId: number; guestName: string; cardNumber: string } | null;
  guests: GuestCard[];
}

export interface VisitResult {
  visitId: number;
  receiptNumber: string;
  coinsAwarded: number;
  ratingAwarded: number;
  guest: GuestCard;
  referralRewardPaid: boolean;
  inviterName: string | null;
}

export interface StaffAuditEntry {
  id: number;
  staffName: string;
  action: string;
  guestUserId: number | null;
  guestName: string | null;
  receiptNumber: string | null;
  details: string | null;
  createdAt: string;
}

export interface StaffMember {
  id: number;
  name: string;
  role: StaffRole;
  vkId: string | null;
  hasPin: boolean;
  isActive: boolean;
  createdAt: string;
}

export interface LoyaltyRates {
  likeCoins: number;
  likeRating: number;
  commentCoins: number;
  commentRating: number;
  repostCoins: number;
  repostRating: number;
  visitCoins: number;
  visitRating: number;
  referralCoins: number;
  referralRating: number;
  maxPaidCommentsPer24h: number;
  dailySocialCoinsCap: number;
  minRewardPostId: number;
}
