import React from 'react';
import { iconFor } from '../config/icons';

/** Векторная иконка по ключу с сервера (подарки, плитки меню, привилегии, достижения). */
export const KeyIcon: React.FC<{ name: string | null | undefined; className?: string; strokeWidth?: number }> = ({ name, className = 'w-5 h-5', strokeWidth = 1.75 }) =>
  React.createElement(iconFor(name), { className, strokeWidth, 'aria-hidden': true });
