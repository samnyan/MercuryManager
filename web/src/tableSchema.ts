import schemasData from './tableSchemas.json'

export type SchemaFieldType = 'string' | 'number' | 'boolean' | 'array' | 'map' | 'int64' | 'enum'

export interface MessageLinkConfig {
  messageTable: string
}

export interface TableFieldSchema {
  key: string
  nameCn: string
  nameEn: string
  type: SchemaFieldType
  rawType?: string
  isId?: boolean
  readOnly?: boolean
  messageLink?: MessageLinkConfig | null
  tableMinWidth?: number | string
  tableWidthFluid?: boolean
  descriptionCn?: string
  descriptionEn?: string
}

export interface TableSchema {
  id: string
  nameCn: string
  nameEn: string
  group: string
  description?: string
  fields: TableFieldSchema[]
}

const schemasMap = new Map<string, TableSchema>()
for (const item of (schemasData as unknown as TableSchema[])) {
  schemasMap.set(item.id, item)
}

export function getTableSchema(tableId: string): TableSchema | undefined {
  return schemasMap.get(tableId)
}

export function getFieldSchema(tableId: string, fieldKey: string): TableFieldSchema | undefined {
  const schema = schemasMap.get(tableId)
  if (!schema) return undefined
  return schema.fields.find(f => f.key === fieldKey)
}

export function formatFieldHeader(field: TableFieldSchema, locale: string): string {
  if (locale === 'zh') {
    return field.nameCn || field.key
  }
  return field.nameEn || field.key
}

export function formatFieldLabel(field: TableFieldSchema, locale: string): string {
  const title = formatFieldHeader(field, locale)
  return title === field.key ? field.key : `${title} / ${field.key}`
}

export function generateFallbackSchema(tableId: string, sampleRowFields: Array<{ name: string; type: string }>): TableSchema {
  return {
    id: tableId,
    nameCn: tableId,
    nameEn: tableId,
    group: 'Other',
    fields: [
      {
        key: 'RowName',
        nameCn: '行键',
        nameEn: 'Row Key',
        type: 'string',
        isId: true,
        readOnly: true,
        tableMinWidth: 140,
        tableWidthFluid: false
      },
      ...sampleRowFields.map(f => {
        const fluid = /Name|Message|Path|Text|Directory|Description/i.test(f.name)
        const type: SchemaFieldType = f.type === 'BoolPropertyData' ? 'boolean'
          : ['Int64PropertyData', 'UInt64PropertyData'].includes(f.type) ? 'int64'
          : ['IntPropertyData', 'UInt32PropertyData', 'FloatPropertyData', 'BytePropertyData', 'Int16PropertyData', 'Int8PropertyData'].includes(f.type) ? 'number'
          : ['ArrayPropertyData'].includes(f.type) ? 'array'
          : ['MapPropertyData'].includes(f.type) ? 'map'
          : 'string'

        return {
          key: f.name,
          nameCn: f.name,
          nameEn: f.name,
          type,
          rawType: f.type,
          isId: false,
          readOnly: false,
          tableMinWidth: fluid ? 180 : (type === 'boolean' ? 100 : (type === 'int64' ? 150 : 120)),
          tableWidthFluid: fluid
        }
      })
    ]
  }
}
