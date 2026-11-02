import {
  Award, Beef, Beer, BottleWine, Cake, CloudFog, Coffee, CookingPot, CupSoda, DoorOpen, Flame, Gift, GlassWater,
  Hamburger, Martini, Pizza, Salad, Sandwich, Soup, Trophy, Users, Utensils, Wine, type LucideIcon,
} from 'lucide-react';

/**
 * Ключи иконок приходят с сервера (подарки, плитки меню, привилегии, достижения) — вместо эмодзи.
 * Неизвестный ключ (например, символ из старой базы) показывается как подарок.
 */
export const ICONS: Record<string, LucideIcon> = {
  badge: Award,
  coffee: Coffee,
  beer: Beer,
  wine: Wine,
  salad: Salad,
  shots: GlassWater,
  pasta: CookingPot,
  cocktail: CupSoda,
  pizza: Pizza,
  set: Sandwich,
  beef: Beef,
  burger: Hamburger,
  hookah: CloudFog,
  bottle: BottleWine,
  lunch: Soup,
  dessert: Cake,
  tea: CupSoda,
  utensils: Utensils,
  martini: Martini,
  door: DoorOpen,
  flame: Flame,
  users: Users,
  trophy: Trophy,
  gift: Gift,
};

/** Ключи для выбора в админке (без дублей-синонимов). */
export const PRIZE_ICON_KEYS = [
  'gift', 'badge', 'coffee', 'beer', 'wine', 'salad', 'shots', 'pasta', 'cocktail', 'pizza', 'set', 'beef', 'burger', 'hookah', 'bottle', 'dessert',
] as const;
export const MENU_ICON_KEYS = ['utensils', 'martini', 'wine', 'beer', 'coffee', 'pizza'] as const;

export function iconFor(key: string | null | undefined): LucideIcon {
  return (key && ICONS[key]) || Gift;
}
