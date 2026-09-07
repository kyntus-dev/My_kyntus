import type { WeeklyPlanningResponse } from '../services/planning.service';
import type { AbsenceLabelFn } from './planning-week-export.util';
import {
  collectHtelExportAgents,
  formatHtelDateHeader,
  htelCellBreakDisplay,
  htelCellStartDisplay,
  htelExportFileName,
  htelWeekDates,
} from './htel-planning-export.util';

function wrapLines(ctx: CanvasRenderingContext2D, text: string, maxWidth: number): string[] {
  const words = text.split(/\s+/).filter(Boolean);
  if (!words.length) return [''];
  const lines: string[] = [];
  let current = words[0];
  for (let i = 1; i < words.length; i++) {
    const next = `${current} ${words[i]}`;
    if (ctx.measureText(next).width <= maxWidth) current = next;
    else {
      lines.push(current);
      current = words[i];
    }
  }
  lines.push(current);
  return lines.slice(0, 3);
}

function drawPlanningCanvas(planning: WeeklyPlanningResponse): HTMLCanvasElement {
  const agents = collectHtelExportAgents([planning]);
  const dates = htelWeekDates(planning.weekStartDate);
  const nameW = 220;
  const dayW = 92;
  const pairH = 52;
  const halfH = pairH / 2;
  const headerH = 78;
  const tableHeaderH = 36;
  const pad = 24;
  const width = pad * 2 + nameW + dayW * dates.length;
  const height = pad * 2 + headerH + tableHeaderH + pairH * Math.max(agents.length, 1);

  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas indisponible.');

  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, width, height);

  ctx.fillStyle = '#0f172a';
  ctx.font = '700 20px Segoe UI, Arial, sans-serif';
  ctx.fillText(`Planning HTEL — ${planning.subServiceName}`, pad, pad + 22);
  ctx.font = '700 15px Segoe UI, Arial, sans-serif';
  ctx.fillStyle = '#1d4ed8';
  ctx.fillText(planning.weekCode, pad, pad + 46);

  const tableY = pad + headerH;
  const headers = ['Nom', ...dates.map((d) => formatHtelDateHeader(d))];
  const colX = [pad, ...dates.map((_, i) => pad + nameW + i * dayW)];

  ctx.fillStyle = '#d9e2f3';
  ctx.fillRect(pad, tableY, width - pad * 2, tableHeaderH);
  ctx.strokeStyle = '#b4b4b4';
  ctx.strokeRect(pad, tableY, width - pad * 2, tableHeaderH);
  ctx.fillStyle = '#0f172a';
  ctx.font = '700 12px Segoe UI, Arial, sans-serif';
  headers.forEach((h, i) => {
    ctx.fillText(h, colX[i] + 8, tableY + 24);
  });

  agents.forEach((agent, ri) => {
    const y = tableY + tableHeaderH + ri * pairH;
    ctx.fillStyle = ri % 2 === 0 ? '#f8fafc' : '#ffffff';
    ctx.fillRect(pad, y, width - pad * 2, pairH);
    ctx.strokeStyle = '#dbe3ee';
    ctx.strokeRect(pad, y, width - pad * 2, pairH);

    ctx.fillStyle = '#0f172a';
    ctx.font = '700 12px Segoe UI, Arial, sans-serif';
    const nameLines = wrapLines(ctx, agent.name, nameW - 16);
    nameLines.forEach((line, li) => {
      ctx.fillText(line, pad + 8, y + pairH / 2 + 4 + (li - (nameLines.length - 1) / 2) * 14);
    });

    agent.days.forEach((cell, di) => {
      const x = colX[di + 1];
      ctx.strokeStyle = '#dbe3ee';
      ctx.strokeRect(x, y, dayW, pairH);
      ctx.beginPath();
      ctx.moveTo(x, y + halfH);
      ctx.lineTo(x + dayW, y + halfH);
      ctx.stroke();

      const start = htelCellStartDisplay(cell);
      const brk = htelCellBreakDisplay(cell);
      ctx.font = '600 12px Segoe UI, Arial, sans-serif';
      ctx.fillStyle = start === 'CP' ? '#92400e' : start === 'F' ? '#5b21b6' : '#0f172a';
      ctx.textAlign = 'center';
      if (start) ctx.fillText(start, x + dayW / 2, y + 18);
      ctx.fillStyle = '#0f172a';
      if (brk) ctx.fillText(brk, x + dayW / 2, y + halfH + 18);
      ctx.textAlign = 'left';
    });
  });

  return canvas;
}

function jpegToPdf(jpeg: Uint8Array, imgW: number, imgH: number): Uint8Array {
  const pageW = 841.89;
  const pageH = 595.28;
  const scale = Math.min((pageW - 24) / imgW, (pageH - 24) / imgH);
  const drawW = imgW * scale;
  const drawH = imgH * scale;
  const x = (pageW - drawW) / 2;
  const y = (pageH - drawH) / 2;

  const objects: string[] = [];
  const encoder = new TextEncoder();

  objects.push('1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n');
  objects.push('2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n');
  objects.push(
    `3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 ${pageW} ${pageH}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >> endobj\n`,
  );

  const content = `q ${drawW.toFixed(2)} 0 0 ${drawH.toFixed(2)} ${x.toFixed(2)} ${y.toFixed(2)} cm /Im0 Do Q`;
  objects.push(`5 0 obj << /Length ${content.length} >> stream\n${content}\nendstream\nendobj\n`);

  const header = encoder.encode('%PDF-1.4\n');
  const offsets = [0];
  let pos = header.length;
  const bodyParts: Uint8Array[] = [header];

  const addObj = (bytes: Uint8Array) => {
    offsets.push(pos);
    bodyParts.push(bytes);
    pos += bytes.length;
  };

  addObj(encoder.encode(objects[0]));
  addObj(encoder.encode(objects[1]));
  addObj(encoder.encode(objects[2]));

  const imgHeader = encoder.encode(
    `4 0 obj << /Type /XObject /Subtype /Image /Width ${imgW} /Height ${imgH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${jpeg.length} >> stream\n`,
  );
  const imgFooter = encoder.encode('\nendstream\nendobj\n');
  offsets.push(pos);
  bodyParts.push(imgHeader, jpeg, imgFooter);
  pos += imgHeader.length + jpeg.length + imgFooter.length;

  addObj(encoder.encode(objects[3]));

  const xrefStart = pos;
  let xref = `xref\n0 ${offsets.length}\n0000000000 65535 f \n`;
  for (let i = 1; i < offsets.length; i++) {
    xref += `${String(offsets[i]).padStart(10, '0')} 00000 n \n`;
  }
  const trailer = `trailer << /Size ${offsets.length} /Root 1 0 R >>\nstartxref\n${xrefStart}\n%%EOF`;
  bodyParts.push(encoder.encode(xref + trailer));

  const total = bodyParts.reduce((n, p) => n + p.length, 0);
  const out = new Uint8Array(total);
  let o = 0;
  for (const p of bodyParts) {
    out.set(p, o);
    o += p.length;
  }
  return out;
}

export async function downloadPlanningWeekPdf(
  planning: WeeklyPlanningResponse,
  _options?: {
    getAbsenceLabel: AbsenceLabelFn;
    shiftColorMap: Record<string, string>;
    statusLabel: string;
  },
): Promise<void> {
  const canvas = drawPlanningCanvas(planning);
  const blob: Blob = await new Promise((resolve, reject) => {
    canvas.toBlob(b => (b ? resolve(b) : reject(new Error('JPEG indisponible.'))), 'image/jpeg', 0.92);
  });
  const jpeg = new Uint8Array(await blob.arrayBuffer());
  const pdf = jpegToPdf(jpeg, canvas.width, canvas.height);
  const pdfBytes = new Uint8Array(pdf.byteLength);
  pdfBytes.set(pdf);
  const file = new Blob([pdfBytes.buffer as ArrayBuffer], { type: 'application/pdf' });
  const url = URL.createObjectURL(file);
  const a = document.createElement('a');
  a.href = url;
  a.download = htelExportFileName([planning], 'pdf');
  a.rel = 'noopener';
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
