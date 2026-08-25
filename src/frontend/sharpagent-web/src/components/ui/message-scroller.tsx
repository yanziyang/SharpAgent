import { useEffect, useRef, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

export function Message({ role, children, className }: {
  role: 'user' | 'assistant'
  children: ReactNode
  className?: string
}) {
  return <article className={cn('chat-message', `chat-message-${role}`, className)} data-message-role={role}>{children}</article>
}

export function Bubble({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('chat-bubble', className)}>{children}</div>
}

/** Chat scroll surface that keeps the newest streamed content in view. */
export function MessageScroller({
  children,
  className,
  'aria-label': ariaLabel,
}: {
  children: ReactNode
  className?: string
  'aria-label'?: string
}) {
  const viewportRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const viewport = viewportRef.current
    if (viewport) {
      viewport.scrollTop = viewport.scrollHeight
    }
  }, [children])

  return <div ref={viewportRef} className={cn('message-scroller', className)} aria-label={ariaLabel}>{children}</div>
}
