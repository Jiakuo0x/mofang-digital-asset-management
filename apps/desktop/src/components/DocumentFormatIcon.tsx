import type { ReactNode } from 'react'

type DocumentFormat = {
  badge: string
  kind: 'markdown' | 'spreadsheet' | 'word' | 'text'
}

const documentFormats: Record<string, DocumentFormat> = {
  '.csv': { badge: 'X', kind: 'spreadsheet' },
  '.doc': { badge: 'W', kind: 'word' },
  '.docx': { badge: 'W', kind: 'word' },
  '.md': { badge: 'MD', kind: 'markdown' },
  '.txt': { badge: 'TXT', kind: 'text' },
  '.xls': { badge: 'X', kind: 'spreadsheet' },
  '.xlsx': { badge: 'X', kind: 'spreadsheet' },
}

export function DocumentFormatIcon({ extension, fallback = null }: { extension: string; fallback?: ReactNode }) {
  const format = documentFormats[extension.toLowerCase()]
  if (!format) return fallback

  return (
    <span className={`document-format-icon document-format-icon--${format.kind}`} aria-hidden="true">
      <svg viewBox="0 0 56 64" fill="none">
        <path className="document-format-icon__page" d="M12 3h23l10 10v46a2 2 0 0 1-2 2H12a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Z" />
        <path className="document-format-icon__fold" d="M35 3v10h10" />
        {format.kind === 'spreadsheet' ? (
          <g className="document-format-icon__content">
            <path d="M19 22h19v20H19zM19 28.7h19M19 35.3h19M25.3 22v20M31.7 22v20" />
          </g>
        ) : (
          <g className="document-format-icon__content">
            <path d="M19 24h19M19 31h19M19 38h13" />
          </g>
        )}
      </svg>
      <span className="document-format-icon__badge">{format.badge}</span>
    </span>
  )
}
