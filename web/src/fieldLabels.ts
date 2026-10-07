import {i18n,englishName} from './i18n'
export function fieldTitle(name:string){const key=`fields.${name}`;return i18n.global.te(key)?i18n.global.t(key):englishName(name)}
export function fieldLabel(name:string){return `${fieldTitle(name)} / ${name}`}
