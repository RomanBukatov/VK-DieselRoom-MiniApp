import React, { useMemo } from 'react';
import { qrMatrix, qrPath } from '../domain/qr';

/** SVG QR-код, сгенерированный на клиенте. Белое поле (quiet zone) 2 модуля — сканеры читают с тёмного фона. */
export const QrCode: React.FC<{ value: string; size?: number; label?: string }> = ({ value, size = 180, label }) => {
  const { path, modules } = useMemo(() => {
    const matrix = qrMatrix(value);
    return { path: qrPath(matrix, 2), modules: matrix.length + 4 };
  }, [value]);

  return (
    <svg
      viewBox={`0 0 ${modules} ${modules}`}
      width={size}
      height={size}
      role="img"
      aria-label={label ?? `QR-код ${value}`}
      shapeRendering="crispEdges"
      className="rounded-xl"
    >
      <rect width={modules} height={modules} fill="#ffffff" />
      <path d={path} fill="#09090b" />
    </svg>
  );
};
