import qrcode from 'qrcode-generator';

/**
 * QR-код купона рисуется на клиенте (без запросов к сторонним сервисам): матрица модулей → один SVG path.
 * Уровень коррекции M — код читается с экрана телефона даже при бликах.
 */
export function qrMatrix(text: string): boolean[][] {
  const qr = qrcode(0, 'M');
  qr.addData(text);
  qr.make();
  const size = qr.getModuleCount();
  const rows: boolean[][] = [];
  for (let r = 0; r < size; r++) {
    const row: boolean[] = [];
    for (let c = 0; c < size; c++) row.push(qr.isDark(r, c));
    rows.push(row);
  }
  return rows;
}

/** Path для <svg viewBox="0 0 N N">: тёмные модули горизонтальными отрезками (меньше узлов, чем по квадрату). */
export function qrPath(matrix: boolean[][], offset = 0): string {
  const parts: string[] = [];
  matrix.forEach((row, r) => {
    let c = 0;
    while (c < row.length) {
      if (!row[c]) {
        c++;
        continue;
      }
      const start = c;
      while (c < row.length && row[c]) c++;
      parts.push(`M${start + offset} ${r + offset}h${c - start}v1h-${c - start}z`);
    }
  });
  return parts.join('');
}
