import { ChevronDown, GitBranch, Plus, Send, Sparkles, Square } from 'lucide-react'
import type { FormEventHandler, ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import type { ModelProfile, PolicyProfile, SessionMode, Workspace } from '@/shared/api/client'

export interface ChatComposerProps {
  value: string
  onChange: (value: string) => void
  onSubmit: FormEventHandler<HTMLFormElement>
  ariaLabel: string
  placeholder: string
  mode: SessionMode
  onModeChange?: (mode: SessionMode) => void
  modelProfileId?: string
  modelProfiles?: readonly ModelProfile[]
  modelLabel?: string
  onModelChange?: (modelProfileId: string) => void
  workspaceId?: string
  workspaces?: readonly Workspace[]
  workspaceLabel?: string
  onWorkspaceChange?: (workspaceId: string) => void
  policyProfileId?: string
  policyProfiles?: readonly PolicyProfile[]
  policyLabel?: string
  onPolicyChange?: (policyProfileId: string) => void
  submitting?: boolean
  disabled?: boolean
  active?: boolean
  onCancel?: () => void
  canSubmit?: boolean
  submitLabel?: string
  archived?: boolean
}

function SelectControl({
  label,
  value,
  onChange,
  disabled,
  children,
}: {
  label: string
  value: string
  onChange?: (value: string) => void
  disabled: boolean
  children: ReactNode
}) {
  return (
    <label className="opencode-composer-select">
      <span className="sr-only">{label}</span>
      <select aria-label={label} value={value} onChange={(event) => onChange?.(event.target.value)} disabled={disabled || !onChange}>
        {children}
      </select>
      <ChevronDown aria-hidden />
    </label>
  )
}

export function ChatComposer({
  value,
  onChange,
  onSubmit,
  ariaLabel,
  placeholder,
  mode,
  onModeChange,
  modelProfileId = '',
  modelProfiles = [],
  modelLabel = 'Model profile',
  onModelChange,
  workspaceId = '',
  workspaces = [],
  workspaceLabel,
  onWorkspaceChange,
  policyProfileId = '',
  policyProfiles = [],
  policyLabel,
  onPolicyChange,
  submitting = false,
  disabled = false,
  active = false,
  onCancel,
  canSubmit = true,
  submitLabel = 'Send',
  archived = false,
}: ChatComposerProps) {
  const controlsDisabled = disabled || submitting || archived
  const selectedWorkspaceLabel = workspaceLabel ?? workspaces.find((workspace) => workspace.id === workspaceId)?.name
  const selectedPolicyLabel = policyLabel ?? policyProfiles.find((policy) => policy.id === policyProfileId)?.name
  const hasModelOptions = modelProfiles.length > 0
  const hasWorkspaceOptions = workspaces.length > 0
  const hasPolicyOptions = policyProfiles.length > 0

  return (
    <form className="chat-composer" onSubmit={onSubmit} aria-busy={submitting}>
      <div className="opencode-composer-surface">
        <label htmlFor={`${ariaLabel.toLowerCase().replaceAll(' ', '-')}-composer-input`} className="sr-only">{ariaLabel}</label>
        <textarea
          id={`${ariaLabel.toLowerCase().replaceAll(' ', '-')}-composer-input`}
          aria-label={ariaLabel}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
          rows={3}
          disabled={controlsDisabled || active}
        />

        <section className="opencode-composer-toolbar" aria-label="Session controls">
          <div className="opencode-composer-controls">
            <Button type="button" variant="ghost" size="icon-sm" aria-label="Add context" disabled>
              <Plus data-icon="inline-start" />
            </Button>
            <SelectControl label="Run mode" value={mode} onChange={onModeChange ? (value) => onModeChange(value as SessionMode) : undefined} disabled={controlsDisabled}>
              <option value="plan">Plan</option>
              <option value="execute">Build</option>
            </SelectControl>
            {hasModelOptions ? (
              <SelectControl label="Model profile" value={modelProfileId} onChange={onModelChange} disabled={controlsDisabled}>
                <option value="" disabled>Select model</option>
                {modelProfiles.map((profile) => <option key={profile.id} value={profile.id}>{profile.displayName}</option>)}
              </SelectControl>
            ) : (
              <span className="opencode-composer-readonly" aria-label="Model profile">{modelLabel}<ChevronDown aria-hidden /></span>
            )}
            <Button type="button" variant="ghost" size="sm" className="opencode-max-control" aria-label="Maximum response length" disabled>
              Max<ChevronDown data-icon="inline-end" />
            </Button>
            {hasWorkspaceOptions ? (
              <SelectControl label="Workspace" value={workspaceId} onChange={onWorkspaceChange} disabled={controlsDisabled}>
                <option value="" disabled>Select workspace</option>
                {workspaces.map((workspace) => <option key={workspace.id} value={workspace.id}>{workspace.name}</option>)}
              </SelectControl>
            ) : null}
            {hasPolicyOptions ? (
              <SelectControl label="Policy and limits" value={policyProfileId} onChange={onPolicyChange} disabled={controlsDisabled}>
                <option value="" disabled>Select policy</option>
                {policyProfiles.map((policy) => <option key={policy.id} value={policy.id}>{policy.name}</option>)}
              </SelectControl>
            ) : null}
          </div>

          {active ? (
            <Button type="button" variant="destructive" size="icon" aria-label="Stop response" onClick={onCancel} disabled={submitting || !onCancel}>
              <Square data-icon="inline-start" />
            </Button>
          ) : (
            <Button type="submit" variant="secondary" size="icon" aria-label={submitting ? 'Sending…' : submitLabel} disabled={controlsDisabled || !canSubmit}>
              <Send data-icon="inline-start" />
            </Button>
          )}
        </section>
      </div>

      <div className="opencode-composer-context" aria-label="Conversation context">
        <span className="opencode-context-project"><Sparkles aria-hidden />SharpAgent<ChevronDown aria-hidden /></span>
        {selectedWorkspaceLabel ? <><span className="opencode-context-divider">/</span><span className="opencode-context-branch"><GitBranch aria-hidden />{selectedWorkspaceLabel}</span></> : null}
        {selectedPolicyLabel ? <span className="opencode-context-policy">{selectedPolicyLabel}</span> : null}
        {archived ? <span className="opencode-context-policy">Archived</span> : null}
      </div>
    </form>
  )
}
