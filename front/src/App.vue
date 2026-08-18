<script setup>
import { computed, nextTick, onMounted, reactive, ref } from 'vue'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Checkbox from 'primevue/checkbox'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import mockProducts from './data/products'

const API_URL = 'http://localhost:5000/products'
const API_URLS = [API_URL]
const API_BASE_URL = API_URL.replace(/\/products$/, '')

const resolveImageUrl = (imagePath) => {
  if (!imagePath) {
    return '/images/products/default-product.svg'
  }

  if (/^https?:\/\//i.test(imagePath)) {
    return imagePath
  }

  if (imagePath.startsWith('/')) {
    return `${API_BASE_URL}${imagePath}`
  }

  return imagePath
}

const normalizeProduct = (product = {}) => ({
  ...product,
  inStock: (() => {
    const val = product.inStock ?? product.InStock
    if (typeof val === 'boolean') return val
    if (val === 1 || val === '1' || val === true || val === 'true') return true
    if (val === 0 || val === '0' || val === false || val === 'false') return false
    return true
  })(),
  saleStartDate: product.saleStartDate ?? product.SaleStartDate,
  image: product.image ?? product.Image ?? '/images/products/default-product.svg',
})

const products = ref([])
const search = ref('')
const loading = ref(false)
const error = ref('')
const showAddDialog = ref(false)
const showDeleteDialog = ref(false)
const submitting = ref(false)
const selectedImageFile = ref(null)
const dialogErrorRef = ref(null)

const focusErrorBanner = () => {
  if (dialogErrorRef.value) {
    dialogErrorRef.value.scrollIntoView({ behavior: 'smooth', block: 'start' })
    dialogErrorRef.value.focus()
  }
}

const dialogMode = ref('create')
const productToDelete = ref(null)
const pageSize = ref(5)
const currentPage = ref(1)
const totalRecords = ref(0)
const skuCache = ref(null)
let skuRequest = null



const parseSkuNumber = (value) => {
  const match = String(value ?? '').match(/(\d+)(?!.*\d)/)
  return match ? Number(match[1]) : null
}

const buildSku = (value) => `SKU-${String(value).padStart(4, '0')}`

const getNextSkuFromCache = () => skuCache.value || 'SKU-0001'

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
    //FIXME: need to change to single api url
    const apiBaseUrl = API_URLS[0].replace(/\/products$/, '')
    const response = await fetch(`${apiBaseUrl}/products/next-sku`)

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`)
    }

    const payload = await response.json()
    const nextSku = payload?.sku || payload?.data || 'SKU-0001'
    skuCache.value = nextSku
    return nextSku
  } catch (err) {
    const fallbackSku = getNextSkuFromCache()
    skuCache.value = fallbackSku
    return fallbackSku
  }
}

const ensureNextSku = async () => {
  if (skuCache.value) {
    return skuCache.value
  }

  //Prevent double backend fetching 
  if (!skuRequest) {
    skuRequest = fetchNextSkuFromBackend().finally(() => {
      skuRequest = null
    })
  }

  return skuRequest
}


const emptyProductForm = (prefillSku = '') => ({
  id: null,
  code: prefillSku || '',
  name: '',
  description: '',
  saleStartDate: new Date().toISOString().slice(0, 10),
  inStock: true,
  image: '/images/products/default-product.svg',
})

const productForm = reactive(emptyProductForm())

const resetProductForm = (keepGeneratedSku = false) => {
  const nextSku = keepGeneratedSku ? productForm.code || getNextSkuFromCache() : getNextSkuFromCache()
  error.value = ''
  selectedImageFile.value = null
  Object.assign(productForm, emptyProductForm(nextSku))
}

const openCreateDialog = async () => {
  dialogMode.value = 'create'
  error.value = ''
  const nextSku = await ensureNextSku()
  console.log("Managed to fetch next SKU from backend:", nextSku)
  Object.assign(productForm, emptyProductForm(nextSku))
  showAddDialog.value = true
}

const openEditDialog = (product) => {
  dialogMode.value = 'edit'
  error.value = ''
 
  Object.assign(productForm, {
    id: product.id,
    code: product.code || '',
    name: product.name || '',
    description: product.description || '',
    saleStartDate: product.saleStartDate ? new Date(product.saleStartDate).toISOString().slice(0, 10) : new Date().toISOString().slice(0, 10),
    inStock: product.inStock ?? true,
    image: product.image || '',
  })
  showAddDialog.value = true
}

// read/synce pagination params from/to url

const syncUrlWithPagination = (page = currentPage.value, size = pageSize.value, sortBy = 'Id', sortOrder = '1') => {
  const params = new URLSearchParams(window.location.search)
  params.set('page', String(page))
  params.set('pageSize', String(size))
  params.set('sortBy', sortBy)
  params.set('sortOrder', sortOrder)

  const nextUrl = `${window.location.pathname}?${params.toString()}`
  window.history.replaceState({}, '', nextUrl)
}

const readPaginationFromUrl = () => {
  const params = new URLSearchParams(window.location.search)
  const page = Number(params.get('page') ?? '1')
  const size = Number(params.get('pageSize') ?? '5')


  //FIXME: isFinite check seems to be reduntant 
  currentPage.value = Number.isFinite(page) && page > 0 ? page : 1
  pageSize.value = Number.isFinite(size) && size > 0 ? size : 5
}

const loadProducts = async (page = currentPage.value, size = pageSize.value, sortBy = 'Id', sortOrder = '1') => {
  loading.value = true
  error.value = ''
  currentPage.value = page
  pageSize.value = size
  syncUrlWithPagination(page, size, sortBy, sortOrder)
  console.log('Loading products from API...', { page, pageSize: size })
  try {
   
        const url = `${API_URL}?page=${page}&pageSize=${size}&sortBy=${sortBy}&sortOrder=${sortOrder}`
        console.log('Fetching products from', url)
        const response = await fetch(url)

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`)
        }

        const data = await response.json()
        const normalizedProducts = (data.items ?? data ?? []).map(normalizeProduct)
        products.value = normalizedProducts
        totalRecords.value = data.totalCount ?? normalizedProducts.length
        return
    }
    catch (err) {
    console.error('Failed to load products from API:', err)
    error.value = 'Unable to load products from the API. The database may be unavailable.'
    totalRecords.value = products.value.length || 0
  } finally {
    loading.value = false
  }
}

const uploadProductImage = (event) => {
  const file = event?.target?.files?.[0]
  if (!file) {
    selectedImageFile.value = null
    return
  }

  selectedImageFile.value = file
  productForm.image = URL.createObjectURL(file)
  error.value = ''
}

const uploadSelectedImage = async () => {
  if (!selectedImageFile.value) {
    return productForm.image || '/images/products/default-product.svg'
  }

  const formData = new FormData()
  formData.append('file', selectedImageFile.value, selectedImageFile.value.name)

  const apiBaseUrl = API_URLS[0].replace(/\/products$/, '')
  const response = await fetch(`${apiBaseUrl}/products/upload-image`, {
    method: 'POST',
    body: formData,
  })

  if (!response.ok) {
    const text = await response.text()
    let detail = 'Could not upload the selected image.'

    try {
      const parsed = JSON.parse(text)
      detail = parsed?.error || detail
    } catch {
      detail = text || detail
    }

    throw new Error(detail)
  }

  const payload = await response.json()
  selectedImageFile.value = null
  return payload.path || '/images/products/default-product.svg'
}

const saveProduct = async () => {
  const trimmedName = productForm.name.trim()
  if (!trimmedName) {
    error.value = 'Product name is required.'
    nextTick(focusErrorBanner)
    return
  }

  submitting.value = true
  console.log('saving product:', productForm, 'InStock:', productForm.inStock)

  try {
    const uploadedImage = await uploadSelectedImage()
    const payload = {
      code: productForm.code.trim() || getNextSkuFromCache(),
      name: trimmedName,
      description: productForm.description.trim(),
      saleStartDate: productForm.saleStartDate,
      inStock: !!productForm.inStock,
      image: uploadedImage,
    }

    console.log('Saving product payload:', payload)

    if (dialogMode.value === 'edit' && productForm.id) {
      let updatedProduct = null

      for (const apiUrl of API_URLS) {
        try {
          console.log('Updating product', productForm.id, 'at', `${apiUrl}/${productForm.id}`)
          const response = await fetch(`${apiUrl}/${productForm.id}`, {
            method: 'PUT',
            headers: {
              'Content-Type': 'application/json',
            },
            body: JSON.stringify(payload),
          })

          if (!response.ok) {
            throw new Error(`HTTP ${response.status}`)
          }

          updatedProduct = await response.json()
          break
        } catch (err) {
          updatedProduct = null
        }
      }

      if (updatedProduct) {
        const normalizedUpdatedProduct = normalizeProduct(updatedProduct)
        products.value = products.value.map((product) => product.id === productForm.id
          ? { ...product, ...normalizedUpdatedProduct, inStock: normalizedUpdatedProduct.inStock, id: product.id }
          : product)
      } else {
        products.value = products.value.map((product) => product.id === productForm.id ? { ...product, ...payload, inStock: !!payload.inStock, id: product.id } : product)
      }
    } else {
      let savedProduct = null

      for (const apiUrl of API_URLS) {
        try {
          console.log('Creating product at', apiUrl)
          const response = await fetch(apiUrl, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
            },
            body: JSON.stringify(payload),
          })

          if (!response.ok) {
            throw new Error(`HTTP ${response.status}`)
          }

          savedProduct = await response.json()
          break
        } catch (err) {
          savedProduct = null
        }
      }

      if (savedProduct) {
        products.value = [normalizeProduct(savedProduct), ...products.value]
      } else {
        products.value = [{
          id: Date.now(),
          ...payload,
        }, ...products.value]
      }

      advanceSku(payload.code)
    }

    showAddDialog.value = false
    resetProductForm(true)
  } catch (err) {
    console.error('Failed to save product:', err)
    error.value = err instanceof Error ? err.message : 'Could not save the product.'
    nextTick(focusErrorBanner)
  } finally {
    submitting.value = false
  }
}

const confirmDelete = (product) => {
  productToDelete.value = product
  showDeleteDialog.value = true
}

const deleteProduct = async () => {
  if (!productToDelete.value) return

  

  try {
    let deleted = false
//FIXME: singular api
    for (const apiUrl of API_URLS) {
      try {
        console.log('Deleting product', productToDelete.value.id, 'at', `${apiUrl}/${productToDelete.value.id}`)
        const response = await fetch(`${apiUrl}/${productToDelete.value.id}`, {
          method: 'DELETE',
        })

        if (response.ok || response.status === 204) {
          deleted = true
          break
        }
      } catch (err) {
        deleted = false
      }
    }

    if (deleted || productToDelete.value.id) {
      products.value = products.value.filter((product) => product.id !== productToDelete.value.id)
    }
  } finally {

    showDeleteDialog.value = false
    productToDelete.value = null
  }
}

const onPage = (event) => {
  const nextPage = Math.floor(event.first / event.rows) + 1
  loadProducts(nextPage, event.rows)
}

onMounted(() => {
  readPaginationFromUrl()
  loadProducts(currentPage.value, pageSize.value)
})

const filteredProducts = computed(() => {
  const list = products.value || []

  if (!search.value) {
    return list
  }

  const term = search.value.toLowerCase()

  return list.filter((product) => {
    return (
      (product.code || '').toLowerCase().includes(term) ||
      (product.name || '').toLowerCase().includes(term) ||
      (product.description || '').toLowerCase().includes(term)
    )
  })
})

const totalProducts = computed(() => products.value.length)


const escapeCsvValue = (value) => {
  const stringValue = String(value ?? '')
  return `"${stringValue.replace(/"/g, '""')}"`
}

const exportToExcel = () => {
  const rows = filteredProducts.value.length ? filteredProducts.value : products.value

  if (!rows.length) {
    return
  }

  const headers = ['Product Code', 'Product Name', 'Description', 'Sale Start Date', 'Image URL']
  const csvRows = [headers.map(escapeCsvValue).join(',')]

  rows.forEach((product) => {
    csvRows.push([
      product.code ?? '',
      product.name ?? '',
      product.description ?? '',
      product.saleStartDate ? formatDate(product.saleStartDate) : '',
      product.image ?? '',
    ].map(escapeCsvValue).join(','))
  })

  const csvContent = csvRows.join('\n')
  const blob = new Blob([csvContent], { type: 'application/vnd.ms-excel;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = 'products-export.xls'
  link.click()
  URL.revokeObjectURL(url)
}

const formatDate = (dateString) =>
  new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(new Date(dateString))

const onSort = (event) =>{
  loadProducts(currentPage.value,event.rows,event.sortField,event.sortOrder)
}
</script>

<template>
  <div class="page-shell">
    <header class="page-header">
      <div>
        <p class="eyebrow">Catalog</p>
        <h1>Product Management</h1>
      </div>
      <Button label="Add Product" icon="pi pi-plus" @click="openCreateDialog" />
    </header>

    <section class="table-panel">
      <div class="toolbar">
        <div class="search-box">
          <i class="pi pi-search" />
          <InputText v-model="search" placeholder="Search products...(you can search by code, name, and description)" />
        </div>
        <Button label="Export" icon="pi pi-download" severity="secondary" outlined @click="exportToExcel" />
      </div>

      <DataTable
        :value="filteredProducts"
        tableStyle="min-width: 100%"
        stripedRows
        paginator
        :rows="pageSize"
        :first="(currentPage - 1) * pageSize"
        :totalRecords="totalRecords"
        :lazy="true"
        :loading="loading"
        @page="onPage"
        @sort="onSort"
        :rows-per-page-options="[5,10,20,50]"
      >
      
        <Column field="code" header="Product Code" sortable />

        <Column field="name" header="Product Name" sortable>
          <template #body="{ data }">
            <div class="product-cell">
              <img :src="resolveImageUrl(data.image)" :alt="data.name" />
              <div class="product-meta">
                <span class="name">{{ data.name }}</span>
              </div>
            </div>
          </template>
        </Column>

        <Column field="description" header="Description" sortable />

        <Column field="inStock" header="In Stock" sortable>
          <template #body="{ data }">
            <Tag :severity="data.inStock ? 'success' : 'danger'">
              {{ data.inStock ? 'Yes' : 'No' }}
            </Tag>
          </template>
        </Column>

        <Column field="saleStartDate" header="Sale Start Date" sortable>
          <template #body="{ data }">
            <Tag severity="info">{{ formatDate(data.saleStartDate) }}</Tag>
          </template>
        </Column>

        <Column header="Actions" style="width: 150px">
          <template #body="{ data }">
            <div class="action-buttons">
              <Button
                icon="pi pi-pencil"
                rounded
                text
                severity="info"
                aria-label="Edit product"
                @click="openEditDialog(data)"
              />
              <Button
                icon="pi pi-trash"
                rounded
                text
                severity="danger"
                aria-label="Delete product"
                @click="confirmDelete(data)"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </section>
  </div>

  <Dialog
    v-model:visible="showAddDialog"
    modal
    :header="dialogMode === 'edit' ? 'Edit Product' : 'Add Product'"
    :style="{ width: '32rem' }"
    :breakpoints="{ '960px': '90vw' }"
  >
    <div class="product-form">
      <p v-if="error" ref="dialogErrorRef" tabindex="-1" class="api-warning dialog-inline-error" role="alert">{{ error }}</p>

      <div class="field-group">
        <label for="product-code">SKU</label>
        <InputText id="product-code" v-model="productForm.code" :disabled="true" readonly placeholder="SKU-ABC123" />
      </div>

      <div class="field-group">
        <label for="product-name">Product Name</label>
        <InputText id="product-name" v-model="productForm.name" placeholder="Nova Headset" />
      </div>

      <div class="field-group">
        <label for="product-desc">Description</label>
        <Textarea id="product-desc" v-model="productForm.description" rows="4" autoResize placeholder="Describe this product..." />
      </div>

      <div class="field-group">
        <label for="product-date">Sale Start Date</label>
        <InputText id="product-date" v-model="productForm.saleStartDate" type="date" />
      </div>

      <div class="field-group checkbox-field">
        <div class="checkbox-wrapper">
          <Checkbox id="product-instock" v-model="productForm.inStock" :binary="true" />
          <label for="product-instock">In Stock</label>
        </div>
      </div>

      <div class="field-group">
        <label for="product-image">Product Image</label>
        <input id="product-image" type="file" accept="image/*" @change="uploadProductImage" />
        <div v-if="productForm.image" class="uploaded-image-preview">
          <img :src="resolveImageUrl(productForm.image)" :alt="productForm.name || 'Product preview'" />
        </div>
      </div>
    </div>

    <template #footer>
      <Button
        label="Cancel"
        severity="secondary"
        text
        @click="showAddDialog = false; resetProductForm(false)"
      />
      <Button
        :label="dialogMode === 'edit' ? 'Update Product' : 'Save Product'"
        icon="pi pi-check"
        :loading="submitting"
        @click="saveProduct"
      />
    </template>
  </Dialog>

  <Dialog
    v-model:visible="showDeleteDialog"
    modal
    header="Delete Product"
    :style="{ width: '26rem' }"
    :breakpoints="{ '960px': '90vw' }"
  >
    <p class="delete-confirmation">
      Are you sure you want to delete <strong>{{ productToDelete?.name }}</strong>?
    </p>

    <template #footer>
      <Button label="Cancel" severity="secondary" text @click="showDeleteDialog = false" />
      <Button label="Delete" icon="pi pi-trash" severity="danger" @click="deleteProduct" />
    </template>
  </Dialog>
</template>
