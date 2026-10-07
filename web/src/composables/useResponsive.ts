import { ref, onMounted, onUnmounted } from 'vue'

const isMobile = ref(false)
const windowWidth = ref(typeof window !== 'undefined' ? window.innerWidth : 1200)

let listenerAdded = false

function updateSize() {
  if (typeof window === 'undefined') return
  windowWidth.value = window.innerWidth
  isMobile.value = window.innerWidth < 768
}

export function useResponsive() {
  if (typeof window !== 'undefined' && !listenerAdded) {
    listenerAdded = true
    updateSize()
    window.addEventListener('resize', updateSize, { passive: true })
    window.addEventListener('orientationchange', updateSize, { passive: true })
  }

  return {
    isMobile,
    windowWidth
  }
}
