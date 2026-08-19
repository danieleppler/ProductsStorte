export const normalizeProduct = (product = {}) => ({
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