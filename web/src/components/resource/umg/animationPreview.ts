import type { UMGAnimation, UMGDocument, UMGNode } from './types'

export function reachableNodes(nodes: UMGNode[], root: string | null | undefined): Set<string> {
  const byId = new Map(nodes.map(n => [n.id, n]))
  const seen = new Set<string>()
  function visit(id: string) { if (seen.has(id)) return; seen.add(id); byId.get(id)?.children.forEach(visit) }
  if (root) visit(root)
  return seen
}

// Bind by owning object identity, including instance-prefixed dependency trees.
export function resolveAnimation(a: UMGAnimation, doc: UMGDocument): UMGAnimation {
  const owner = doc.nodes.find(n => n.id === a.ownerId)
  const tree = owner?.childWidget?.localTreeId ?? a.ownerTreeId
  return { ...a, bindings: a.bindings.map(b => {
    const matches = doc.nodes.filter(n => n.treeId === tree && n.name === b.widgetName)
    const nodeId = b.isRoot && owner ? owner.id : matches.length === 1 ? matches[0].id : null
    const elsewhere = doc.nodes.some(n => n.name === b.widgetName)
    const resolutionIssue = nodeId ? undefined : matches.length > 1
      ? 'Ambiguous within owner tree'
      : elsewhere ? 'Name exists only outside owner tree; not rebound' : 'Target absent from cooked widget document'
    return { ...b, nodeId, resolutionIssue }
  }) }
}

export function entranceSelection(animations: UMGAnimation[], reachable: Set<string>): string[] {
  const owners = new Map<string, UMGAnimation[]>()
  for (const a of animations) {
    if (!/^in(?:_|$)/i.test(a.name) || !(a.ownerId && reachable.has(a.ownerId)) && !a.bindings.some(b => b.nodeId && reachable.has(b.nodeId))) continue
    const key = a.ownerId ?? a.id
    owners.set(key, [...(owners.get(key) ?? []), a])
  }
  return [...owners.values()].map(items => items.sort((a,b) => a.name.localeCompare(b.name, undefined, { numeric: true }))[0].id)
}

// All clips start together; shorter clips hold their final keyed values.
export function combineAnimations(animations: UMGAnimation[], name: string): UMGAnimation | null {
  if (!animations.length) return null
  const channel = (c: UMGAnimation['tracks'][number]['channels'][number], start: number) => ({ ...c, keys: c.keys.map(k => ({ ...k, frame: k.frame - start })) })
  return {
    id: '__combined', name, start: 0, end: Math.max(...animations.map(a => a.end - a.start)), tickRate: 60, displayRate: 60,
    bindings: animations.flatMap(a => a.bindings.map(b => ({ ...b, guid: `${a.id}:${b.guid}`, label: `${a.ownerPath ?? a.ownerId ?? '?'} / ${b.widgetName}` }))),
    tracks: animations.flatMap(a => a.tracks.map(t => ({ ...t, targetGuid: `${a.id}:${t.targetGuid}`, name: `${a.id}:${t.name}`,
      channels: t.channels.map(c => channel(c, a.start)), materialParameters: t.materialParameters?.map(p => ({ ...p, channel: channel(p.channel, a.start) })) })))
  }
}

export function isolationHidden(nodes: UMGNode[], targets: string[], root: string | null | undefined): Set<string> {
  const reachable = reachableNodes(nodes, root)
  const byId = new Map(nodes.map(n => [n.id,n]))
  const keep = new Set<string>()
  for (const id of targets.filter(id => reachable.has(id))) {
    for (const child of reachableNodes(nodes,id)) keep.add(child)
    let n = byId.get(id); const seen = new Set<string>()
    while (n && !seen.has(n.id)) { seen.add(n.id); keep.add(n.id); n = n.parentId ? byId.get(n.parentId) : undefined }
  }
  return keep.size ? new Set([...reachable].filter(id => !keep.has(id))) : new Set()
}
