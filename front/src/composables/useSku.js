
import { ref } from 'vue'

/*
All the functions regarding sku creation , fetching from server / cache , 
and getting the next sku to present in create prodcut dialog are implemented here. The sku is cached in a reactive variable to avoid unnecessary backend calls.
 The next sku is fetched from the backend only if it is not already cached. The next sku is also advanced in the cache when a new product is created.
*/
export function useSku() {

  const skuCache = ref(null)
  let skuRequest = null
  const API_URL = 'http://localhost:5000/products'

  //using regex to extract the numeric part of the sku, and return it as a number
  const parseSkuNumber = (value) => {
    const match = String(value ?? '').match(/(\d+)(?!.*\d)/)
    return match ? Number(match[1]) : null
  }

  const buildSku = (value) => `SKU-${String(value).padStart(4, '0')}`


  //using of reactive variable to cache the next sku value
  const getNextSkuFromCache = () => skuCache.value || 'SKU-0001'

  //using of reactive variable to cache the next sku value
  const advanceSku = (value) => {
    const numericPart = parseSkuNumber(value)
    if (numericPart === null) {
      skuCache.value = 'SKU-0001'
      return skuCache.value
    }
    skuCache.value = buildSku(numericPart + 1)
    return skuCache.value
  }

  const fetchNextSkuFromBackend = async () => {
    try {
      const apiBaseUrl = API_URL.replace(/\/products$/, '')
      const response = await fetch(`${apiBaseUrl}/products/next-sku`)

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`)
      }

      const payload = await response.json()
      console.log('Fetched next SKU from backend:', payload)
      const nextSku = payload?.sku || payload?.data || 'SKU-0001'
      skuCache.value = nextSku
      return nextSku
    } catch (err) {
      //if we fail to fetch the next sku from backend, we will fallback to the cached value or default to 'SKU-0001'
      const fallbackSku = getNextSkuFromCache()
      skuCache.value = fallbackSku
      return fallbackSku
    }
  }

  //check if we already have the sku cached, if not fetch it from backend and cache it
  const ensureNextSku = async () => {
    if (skuCache.value) {
      return skuCache.value
    }

    //use skuRequest variable to prevent double backend fetching
    if (!skuRequest) {
      skuRequest = fetchNextSkuFromBackend().finally(() => {
        skuRequest = null
      })
    }

    return skuRequest
  }

  return { getNextSkuFromCache, advanceSku, ensureNextSku, skuCache }
}
