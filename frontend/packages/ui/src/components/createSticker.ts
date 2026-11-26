export const stickerAccents = {
  mint: {name: 'Мята', color: '#8ce4cd'},
  amber: {name: 'Янтарь', color: '#ffc981'},
  lilac: {name: 'Сирень', color: '#cfb3ff'},
} as const;

export type StickerAccent = keyof typeof stickerAccents;

/** Draws a bounded PNG using only the selected template and local system fonts. */
export async function createSticker(caption: string, emoji: string, accent: StickerAccent): Promise<File> {
  const title = caption.trim();
  if (!title || Array.from(title).length > 18) throw Error('Введите подпись от 1 до 18 символов.');
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 384;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw Error('Браузер не может нарисовать стикер.');

  const color = stickerAccents[accent].color;
  ctx.fillStyle = '#202232';
  ctx.strokeStyle = color;
  ctx.lineWidth = 10;
  ctx.beginPath();
  ctx.roundRect(30, 20, 324, 344, 68);
  ctx.fill();
  ctx.stroke();

  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.font = '132px "Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", sans-serif';
  ctx.fillText(emoji, 192, 151, 280);

  ctx.fillStyle = color;
  ctx.fillRect(82, 247, 220, 4);
  ctx.font = '700 48px system-ui, sans-serif';
  while (ctx.measureText(title).width > 275) {
    const size = Number(ctx.font.match(/(\d+)px/)?.[1] ?? 48);
    if (size <= 21) break;
    ctx.font = `700 ${size - 1}px system-ui, sans-serif`;
  }
  ctx.fillText(title, 192, 299, 275);

  const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/png'));
  if (!blob || blob.size > 524288) throw Error('Не удалось создать PNG до 512 КиБ.');
  return new File([blob], title + '.png', {type: 'image/png'});
}
