import { computed, ref, watch } from 'vue'
import { api } from '../../project'

interface AuditItem {
  packageName: string
  objectPaths: string[]
  file: string | null
  status: string
}
interface AuditReport {
  assetCount: number
  registeredPackages: number
  availablePackages: number
  matchedPackages: number
  missing: AuditItem[]
  unregistered: AuditItem[]
  caseMismatches: AuditItem[]
}
type AuditKind = 'missing' | 'unregistered' | 'caseMismatches'

export const auditTabs = [
  ['missing', '文件缺失'],
  ['unregistered', '未登记素材'],
  ['caseMismatches', '大小写不匹配'],
] as const
export const auditPageSize = 100

export function useAssetRegistryAudit(projectId: () => string, onError: (error: string) => void) {
  const audit = ref<AuditReport>()
  const auditing = ref(false)
  const auditKind = ref<AuditKind>('missing')
  const auditQuery = ref('')
  const auditPage = ref(0)
  let generation = 0

  const auditRows = computed(() => {
    const query = auditQuery.value.toLowerCase()
    return (audit.value?.[auditKind.value] ?? []).filter(item => item.packageName.toLowerCase().includes(query))
  })
  const auditPageCount = computed(() => Math.max(1, Math.ceil(auditRows.value.length / auditPageSize)))
  const visibleAuditRows = computed(() => auditRows.value.slice(auditPage.value * auditPageSize, (auditPage.value + 1) * auditPageSize))
  watch([auditKind, auditQuery], () => { auditPage.value = 0 })

  function resetAudit() {
    generation++
    audit.value = undefined
    auditing.value = false
    auditPage.value = 0
  }

  async function checkResources() {
    const id = projectId()
    if (!id) return
    const request = ++generation
    auditing.value = true
    try {
      const report = await api<AuditReport>(`/projects/${id}/asset-registry-audit`)
      if (request !== generation || id !== projectId()) return
      audit.value = report
      auditPage.value = 0
    } catch (error) {
      if (request === generation) onError(String(error))
    } finally {
      if (request === generation) auditing.value = false
    }
  }

  return { audit, auditing, auditKind, auditQuery, auditPage, auditRows, auditPageCount, visibleAuditRows, resetAudit, checkResources }
}
