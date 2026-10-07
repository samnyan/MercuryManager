import {createI18n} from 'vue-i18n'
import messages from './locales.json'
const saved=localStorage.getItem('mercury-locale')
export const i18n=createI18n({legacy:false,locale:saved==='en'?'en':'zh',fallbackLocale:'en',messages})
export function setLanguage(locale:'zh'|'en'){i18n.global.locale.value=locale;localStorage.setItem('mercury-locale',locale);document.documentElement.lang=locale}
export function tr(key:string){return i18n.global.t(key)}
export function englishName(name:string){return name.replace(/^b(?=[A-Z])/,'').replace(/([a-z0-9])([A-Z])/g,'$1 $2').replace(/([A-Z])([A-Z][a-z])/g,'$1 $2').replace(/_/g,' ')}
