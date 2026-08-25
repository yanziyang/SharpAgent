import { Dialog } from '@base-ui/react/dialog'
import { X } from 'lucide-react'
import type * as React from 'react'
import { cn } from '@/lib/utils'

type SheetSide = 'top' | 'right' | 'bottom' | 'left'

const Sheet = Dialog.Root
const SheetTrigger = Dialog.Trigger
const SheetClose = Dialog.Close

function SheetContent({
  side = 'right',
  closeLabel = 'Close panel',
  className,
  children,
  ...props
}: React.ComponentProps<typeof Dialog.Popup> & { side?: SheetSide; closeLabel?: string }) {
  return (
    <Dialog.Portal>
      <Dialog.Viewport className="sheet-viewport">
        <Dialog.Backdrop className="sheet-backdrop" />
        <Dialog.Popup data-side={side} className={cn('sheet-content', className)} {...props}>
          {children}
          <Dialog.Close className="sheet-close" aria-label={closeLabel}>
            <X data-icon="inline-start" />
          </Dialog.Close>
        </Dialog.Popup>
      </Dialog.Viewport>
    </Dialog.Portal>
  )
}

function SheetHeader({ className, ...props }: React.ComponentProps<'div'>) {
  return <div className={cn('sheet-header', className)} {...props} />
}

function SheetTitle({ className, ...props }: React.ComponentProps<typeof Dialog.Title>) {
  return <Dialog.Title className={cn('sheet-title', className)} {...props} />
}

function SheetDescription({ className, ...props }: React.ComponentProps<typeof Dialog.Description>) {
  return <Dialog.Description className={cn('sheet-description', className)} {...props} />
}

export {
  Sheet,
  SheetTrigger,
  SheetClose,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
}
