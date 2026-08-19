// services/productsApi.js  — pure, no Vue, easily testable
const API_URL = 'http://localhost:5000/products'
export const API_BASE_URL = API_URL.replace(/\/products$/, '')

export async function fetchProducts({ page, pageSize, sortBy = 'Id', sortOrder = '1' }) {
  const url = `${API_URL}?page=${page}&pageSize=${pageSize}&sortBy=${sortBy}&sortOrder=${sortOrder}`
  const res = await fetch(url)
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return res.json()
}

export async function createProduct(payload) {
  const res = await fetch(API_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return res.json()
}

export async function updateProduct(id, payload) {
  console.log("about to send update request", id,payload)
  const res = await fetch(`${API_URL}/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
    console.log(res)
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return res.json()
}

export async function deleteProductById(id) {
  const res = await fetch(`${API_URL}/${id}`, { method: 'DELETE' })
  console.log(res)
  //if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return true
}