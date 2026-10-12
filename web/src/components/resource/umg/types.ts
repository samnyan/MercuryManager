export type ValueSource = 'serialized' | 'class-default' | 'engine-default' | 'unknown'

export type DiagnosticIssue = {
  code: string
  severity: 'info' | 'warning'
  nodeId?: string
  message: string
}

export type UMGTreeRoot = {
  id: string
  treeId: string
  rootNodeId: string | null
  ownerName: string
  kind: 'generated' | 'archetype' | 'nested' | 'unknown'
}

export type UMGResourceRef = {
  role: string
  assetPath: string
  className: string | null
}

export type UMGNode = {
  id: string
  exportIndex: number
  treeId: string
  name: string
  className: string
  parentId: string | null
  children: string[]
  slot: {
    exportIndex: number
    className: string
    values: Record<string, any>
  } | null
  visual: {
    Visibility?: string
    RenderOpacity?: number
    Text?: string
    RenderTransform?: { Translation?: { X?: number; Y?: number }; Scale?: { X?: number; Y?: number }; Shear?: { X?: number; Y?: number }; Angle?: number }
    RenderTransformPivot?: { X?: number; Y?: number }
    Brush?: { ImageSize?: { X?: number; Y?: number }; ResourceObject?: { assetPath?: string; className?: string } }
    Font?: { Size?: number }
    WidthOverride?: number
    HeightOverride?: number
    bOverride_WidthOverride?: boolean
    bOverride_HeightOverride?: boolean
    [key: string]: any
  }
  sources: Record<string, ValueSource>
  resources: UMGResourceRef[]
  childWidget: {
    assetPath: string | null
    localTreeId: string | null
  } | null
  preview: {
    layout: 'supported' | 'partial' | 'unsupported'
    appearance: 'supported' | 'partial' | 'unsupported'
    issues: string[]
  }
}

export type UMGDocument = {
  schemaVersion: 1
  revision: string
  assetPath: string
  roots: UMGTreeRoot[]
  suggestedRootId: string | null
  nodes: UMGNode[]
  diagnostics: DiagnosticIssue[]
}

export type Rect = {
  x: number
  y: number
  width: number
  height: number
}

export type ComputedLayout = {
  rect: Rect
  visible: boolean
  collapsed: boolean
  issues: string[]
}
