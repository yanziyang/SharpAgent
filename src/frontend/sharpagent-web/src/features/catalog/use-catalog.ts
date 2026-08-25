import { useCallback } from 'react'
import {
  fetchModelProfiles,
  fetchPolicyProfiles,
  fetchWorkspaces,
  type ModelProfile,
  type PolicyProfile,
  type Workspace,
} from '@/shared/api/client'
import { useResource, type ResourceState } from '@/shared/api/use-resource'

export interface CatalogSnapshot {
  workspaces: Workspace[]
  modelProfiles: ModelProfile[]
  policyProfiles: PolicyProfile[]
}

export function useCatalog(): ResourceState<CatalogSnapshot> & { reload: () => void } {
  const loader = useCallback(async (signal: AbortSignal): Promise<CatalogSnapshot> => {
    const [workspaces, modelProfiles, policyProfiles] = await Promise.all([
      fetchWorkspaces(signal),
      fetchModelProfiles(signal),
      fetchPolicyProfiles(signal),
    ])
    return { workspaces, modelProfiles, policyProfiles }
  }, [])

  return useResource('catalog', loader)
}
