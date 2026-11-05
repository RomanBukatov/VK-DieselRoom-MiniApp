// Фото подарка перед загрузкой: уменьшаем на телефоне, чтобы в базу и к гостям не уезжали многомегабайтные снимки.

const MAX_SIDE = 1000;
const MAX_DATA_URL_LENGTH = 900_000; // ≈ 650 КБ файла: укладывается в лимит прокси на размер запроса
const QUALITIES = [0.85, 0.75, 0.65, 0.5];

/** Размер после уменьшения: длинная сторона не больше maxSide, пропорции сохраняются, маленькие фото не растягиваются. */
export function fitSize(width: number, height: number, maxSide: number = MAX_SIDE): { width: number; height: number } {
  const scale = Math.min(1, maxSide / Math.max(width, height));
  return { width: Math.round(width * scale), height: Math.round(height * scale) };
}

function loadImage(file: File): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file);
    const image = new Image();
    image.onload = () => {
      URL.revokeObjectURL(url);
      resolve(image);
    };
    image.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error('Не удалось прочитать файл: нужно фото JPEG, PNG или WebP.'));
    };
    image.src = url;
  });
}

/** Файл → Data URL в JPEG. Качество снижается, пока результат не уложится в лимит. */
export async function imageFileToDataUrl(file: File): Promise<string> {
  const image = await loadImage(file);
  const { width, height } = fitSize(image.naturalWidth, image.naturalHeight);

  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (!context) throw new Error('Браузер не смог обработать фото.');
  // У JPEG нет прозрачности: прозрачный PNG ложится на тёмный фон карточки, а не на чёрные артефакты
  context.fillStyle = '#18181C';
  context.fillRect(0, 0, width, height);
  context.drawImage(image, 0, 0, width, height);

  for (const quality of QUALITIES) {
    const result = canvas.toDataURL('image/jpeg', quality);
    if (result.length <= MAX_DATA_URL_LENGTH) return result;
  }
  throw new Error('Фото слишком большое: выберите другое.');
}
