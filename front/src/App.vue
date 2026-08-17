<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import Button from 'primevue/button'
import Card from 'primevue/card'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import mockProducts from './data/products'

const API_URLS = [
  'http://localhost:5000/products',
]

const products = ref([])
const search = ref('')
const loading = ref(false)
const error = ref('')
const showAddDialog = ref(false)
const showDeleteDialog = ref(false)
const submitting = ref(false)
const deleting = ref(false)
const dialogMode = ref('create')
const productToDelete = ref(null)
const pageSize = ref(5)
const currentPage = ref(1)
const totalRecords = ref(0)
const skuCache = ref(null)
let skuRequest = null

//SKU Functions
{
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
}


const emptyProductForm = (prefillSku = '') => ({
  id: null,
  code: prefillSku || '',
  name: '',
  description: '',
  saleStartDate: new Date().toISOString().slice(0, 10),
  image: '',
})

const productForm = reactive(emptyProductForm())

const resetProductForm = (keepGeneratedSku = false) => {
  const nextSku = keepGeneratedSku ? productForm.code || getNextSkuFromCache() : getNextSkuFromCache()
  Object.assign(productForm, emptyProductForm(nextSku))
}

const openCreateDialog = async () => {
  dialogMode.value = 'create'
  const nextSku = await ensureNextSku()
  Object.assign(productForm, emptyProductForm(nextSku))
  showAddDialog.value = true
}

const openEditDialog = (product) => {
  dialogMode.value = 'edit'
  Object.assign(productForm, {
    id: product.id,
    code: product.code || '',
    name: product.name || '',
    description: product.description || '',
    saleStartDate: product.saleStartDate ? new Date(product.saleStartDate).toISOString().slice(0, 10) : new Date().toISOString().slice(0, 10),
    image: product.image || '',
  })
  showAddDialog.value = true
}

// read/synce pagination params from/to url

const syncUrlWithPagination = (page = currentPage.value, size = pageSize.value) => {
  const params = new URLSearchParams(window.location.search)
  params.set('page', String(page))
  params.set('pageSize', String(size))

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

const loadProducts = async (page = currentPage.value, size = pageSize.value) => {
  loading.value = true
  error.value = ''
  currentPage.value = page
  pageSize.value = size
  syncUrlWithPagination(page, size)
  console.log('Loading products from API...', { page, pageSize: size })

  try {
    let lastError = null

    //FIXME: why several urls ? 
    for (const apiUrl of API_URLS) {
      try {
        const url = `${apiUrl}?page=${page}&pageSize=${size}`
        console.log('Fetching products from', url)
        const response = await fetch(url)

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`)
        }

        const data = await response.json()
        products.value = data.items ?? data
        totalRecords.value = data.totalCount ?? products.value.length
        return
      } catch (err) {
        lastError = err
      }
    }

    throw lastError ?? new Error('Unable to fetch products from API')
  } catch (err) {
    const startIndex = (page - 1) * size
    products.value = mockProducts.slice(startIndex, startIndex + size)
    totalRecords.value = mockProducts.length
    error.value = 'API unavailable. Showing mock product data.'
  } finally {
    loading.value = false
  }
}

const saveProduct = async () => {
  submitting.value = true

  //FIXME: configure other default image 
  const payload = {
    code: productForm.code.trim() || getNextSkuFromCache(),
    name: productForm.name.trim(),
    description: productForm.description.trim(),
    saleStartDate: productForm.saleStartDate,
    image: productForm.image.trim() || 'https://images.unsplash.com/photo-1521572267360-ee0c2909d518?auto=format&fit=crop&w=800&q=80',
  }

  console.log('Saving product payload:', payload)

  try {
    if (dialogMode.value === 'edit' && productForm.id) {
      let updatedProduct = null

      //FIXME: singular api
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
        products.value = products.value.map((product) => product.id === productForm.id ? { ...product, ...updatedProduct } : product)
      } else {
        products.value = products.value.map((product) => product.id === productForm.id ? { ...product, ...payload, id: product.id } : product)
      }
    } else {
      let savedProduct = null

      //FIXME: singular api
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
        products.value = [savedProduct, ...products.value]
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

  deleting.value = true

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
    deleting.value = false
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
  console.log("sort clicked")
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

      <p v-if="error" class="api-warning">{{ error }}</p>

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
      >
      
        <Column field="code" header="Product Code" sortable />

        <Column field="name" header="Product Name" sortable>
          <template #body="{ data }">
            <div class="product-cell">
              <img :src="data.image" :alt="data.name" />
              <div class="product-meta">
                <span class="name">{{ data.name }}</span>
              </div>
            </div>
          </template>
        </Column>

        <Column field="description" header="Description" sortable />

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

      <div class="field-group">
        <label for="product-image">Product Image URL</label>
        <InputText id="product-image" v-model="productForm.image" placeholder="https://example.com/image.jpg" />
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
      <Button label="Delete" icon="pi pi-trash" severity="danger" :loading="deleting" @click="deleteProduct" />
    </template>
  </Dialog>
</template>
