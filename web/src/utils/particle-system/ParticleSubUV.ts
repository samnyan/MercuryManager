/** APPROXIMATION: discrete atlas frames, without cross-frame interpolation. */
export function particleSubImageFrame(index: number, columns: number, rows: number): number {
  const total = Math.max(1, Math.floor(columns)) * Math.max(1, Math.floor(rows))
  return Number.isFinite(index) ? Math.min(total - 1, Math.max(0, Math.floor(index))) : 0
}
