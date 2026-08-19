<script setup>
import { computed, nextTick, onMounted, reactive, ref } from 'vue'
import {useSku} from './composables/useSku'
import { normalizeProduct } from './utils/product'
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

import { useProducts } from './composables/useProducts'

const {ensureNextSku,advanceSku,getNextSkuFromCache} = useSku()
const { products, loading, error, totalRecords, load, add, edit, remove } = useProducts()

const API_URL = import.meta.env.VITE_API_URL
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

const search = ref('')

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

const syncUrl = (page = currentPage.value, size = pageSize.value, sortBy = 'Id', sortOrder = '1') => {
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

//update the current page and page size, and sync the url with the new values. Then load the products from the backend with the new url params.
const loadProducts = async (page = currentPage.value, size = pageSize.value, sortBy = 'Id', sortOrder = '1') => {
  currentPage.value = page
  pageSize.value = size
  syncUrl(page, size, sortBy, sortOrder)
   return load({ page, pageSize: size, sortBy, sortOrder })
}

//save the product image in reactive variable, and store a path to that file in the productForm object
//notice that productForm.image saves the temporray browser locations of the photo, for later use in <img :src="resolveImageUrl(productForm.image)">
//the productForm.image with later be replaced with the actual path of the static file in the server
const captureProductImage = (event) => {
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
  //validation of product name
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

       if (dialogMode.value === 'edit') {
      await edit(productForm.id, payload)
    } else {
      await add(payload)
      advanceSku(payload.code)
    }
    showAddDialog.value = false
    resetProductForm(true)
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Could not save the product.'
    nextTick(focusErrorBanner)
  } finally {
    submitting.value = false
  }
}

//called after clicking delete product. showeing the "are you sure you want to delete?" dialog
const confirmDelete = (product) => {
  productToDelete.value = product
  showDeleteDialog.value = true
}

const deleteProduct = async () => {
  if (!productToDelete.value) return
  try {
    await remove(productToDelete.value.id)
  } finally {
    showDeleteDialog.value = false
    productToDelete.value = null
  }
}

//fetch the next page , take the current page stored in the event.first , and the current event rows per page
const onPage = (event) =>  loadProducts(Math.floor(event.first / event.rows) + 1,event.rows)

const onSort = (event) => loadProducts(currentPage.value,event.rows,event.sortField,event.sortOrder)

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
        <input id="product-image" type="file" accept="image/*" @change="captureProductImage" />
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
