// composables/useProducts.js
import { ref } from 'vue'
import { fetchProducts, createProduct, updateProduct, deleteProductById  } from '../services/productsApi'
import { normalizeProduct } from '../utils/product'

export function useProducts() {
  const products = ref([])
  const loading = ref(false)
  const error = ref('')
  const totalRecords = ref(0)



  //fetching products from backend, with the current url params.updating loading reactive variable to manage loading spinner. updating error reactive variable to manage error banner
  async function load({ page, pageSize, sortBy = 'Id', sortOrder = '1' }) {
    loading.value = true
    error.value = ''
    try {
      const data = await fetchProducts({ page, pageSize, sortBy, sortOrder })
      const items = (data.items ?? data ?? []).map(normalizeProduct)
      products.value = items
      totalRecords.value = data.totalCount ?? items.length
    } catch (err) {
      error.value = 'Unable to load products from the API. The database may be unavailable.'
      totalRecords.value = products.value.length || 0
    } finally {
      loading.value = false
    }
  }

  async function add(payload) {
    const saved = normalizeProduct(await createProduct(payload))
    products.value = [saved, ...products.value]
    return saved
  }

  async function edit(id, payload) {
    console.log('useProducts reached:',id, payload)
    const updated = normalizeProduct(await updateProduct(id, payload))
    products.value = products.value.map(p => p.id === id ? { ...p, ...updated, id } : p)
  }

  //remove product by id
  async function remove(id) {
    console.log("deleteing id {0}", id)
    await deleteProductById(id)
    console.log( products.value.filter(p => p.id !== id))
    products.value = products.value.filter(p => p.id !== id)
  }

  return { products, loading, error, totalRecords, load, add, edit, remove }
} 