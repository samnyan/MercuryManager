// UE rich-text fallback, not HTML. Keep the source untouched for inspection/editing.
export function previewText(className: string, text: string): string {
  if (className !== 'RichTextBlock') return text
  return text.replace(/<\/>|<[\w.:-]+(?:\s+[\w.:-]+\s*=\s*(?:"[^"]*"|'[^']*'))*\s*\/?>/g, '')
    .replace(/&(lt|gt|amp|quot);/g, (_, entity: string) => ({lt:'<',gt:'>',amp:'&',quot:'"'}[entity]!))
}
