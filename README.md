# Products Store

A full-stack product management application with a Vue 3 frontend and an ASP.NET Core (.NET 8) minimal-API backend. The backend persists data to a file-based SQL Server LocalDB using **stored procedures with JSON parameters** (no ORM), and the frontend provides a searchable, paginated catalog with full create/read/update/delete support and image uploads.

## Tech Stack

**Frontend**
- Vue 3 (`<script setup>` Composition API)
- PrimeVue component library (DataTable, Dialog, Button, InputText, Textarea, Checkbox, Tag)
- Vite (dev server + build tooling)

**Backend**
- ASP.NET Core (.NET 8) Minimal APIs
- Microsoft.Data.SqlClient (raw ADO.NET, no Entity Framework)
- SQL Server LocalDB with an internal `.mdf` data file
- Stored procedures using `OPENJSON` (input) and `FOR JSON PATH` (output)
- Swagger / Swashbuckle for API documentation

## Architecture

The frontend is organized by concern rather than piling all logic into the root component:

- `App.vue` — the products view; orchestrates the composables and holds view-local UI state (dialogs, search, form).
- `composables/useProducts.js` — reactive product state (list, loading, error, total) plus CRUD orchestration.
- `composables/useSku.js` — SKU generation and caching (next-SKU lookup, advance, cache read).
- `services/productsApi.js` — a plain service layer that owns all HTTP calls (no Vue reactivity).
- `utils/product.js` — pure helpers such as `normalizeProduct` (reconciles casing/shape differences from the API).

The backend follows a repository pattern: minimal-API endpoints in `Program.cs` delegate to `ProductsRepository`, which calls stored procedures and maps the JSON results back to `Product` objects.

## Features

### Frontend

- **Product catalog** displayed in a PrimeVue DataTable with product code, name (with thumbnail image), description, in-stock status, and sale start date.
- **Server-side pagination** — page and page size are driven by the backend, with selectable rows-per-page (5/10/20/50).
- **Server-side sorting** — clicking a column header sorts via the API (sort field and direction passed as query params).
- **URL state sync** — the current page, page size, sort field, and sort order are written to the URL query string, so the view is shareable and survives refresh.
- **Live search** — client-side filtering across product code, name, and description.
- **Create / edit dialog** — a single modal handles both add and edit modes, with product name validation and an accessible inline error banner.
- **Image upload with instant preview** — selecting a file shows a local blob-URL preview immediately; the file is uploaded to the server only on save, and the returned path is stored with the product.
- **Delete confirmation** — a confirmation dialog guards against accidental deletion.
- **CSV export** — exports the current (filtered) product list to a spreadsheet-compatible file.
- **Auto-generated SKU** — new products are prefilled with the next available SKU fetched from the backend; the SKU field is read-only in the form.
- **Graceful API-error handling** — a visible banner is shown if the API or database is unavailable.

### Backend

- **RESTful product endpoints** — list (paged), get by id, create, update, delete.
- **JSON-parameter stored procedures** — create and update pass the product as a single JSON payload parsed server-side with `OPENJSON`; reads return JSON via `FOR JSON PATH, WITHOUT_ARRAY_WRAPPER`.
- **Paged + sorted list procedure** — `sp_Products_GetPageJson` returns items plus total count and page metadata in one round trip, with offset/fetch paging.
- **SQL-injection defense in depth** — all values are passed as parameters; the only dynamic SQL (the sortable column in the paged query) is guarded by a whitelist at the API layer, a second whitelist inside the procedure, and `QUOTENAME`, and executed via `sp_executesql` with typed parameters.
- **Next-SKU generator** — computes the next sequential SKU from existing product codes.
- **Image upload endpoint** — accepts an image file, validates by extension and content type, sanitizes the filename, stores it under `wwwroot/images/products`, and returns a server-relative path.
- **Self-initializing database** — on startup the app creates the LocalDB `.mdf` file if missing, creates the table and stored procedures if they don't exist, and seeds sample products on an empty table.
- **CORS enabled** for the frontend during development.
- **Swagger UI** available in development for exploring and testing the API.

## Data Model

| Field | Type | Notes |
|-------|------|-------|
| Id | INT IDENTITY | Primary key (surrogate) |
| Code | NVARCHAR(100) | Product SKU / business key |
| Name | NVARCHAR(200) | Required |
| Description | NVARCHAR(MAX) | Optional |
| SaleStartDate | DATE | Required |
| InStock | BIT | Defaults to 1 |
| Image | NVARCHAR(500) | Server-relative image path |

## API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/products?page=&pageSize=&sortBy=&sortOrder=` | Paged, sorted product list |
| GET | `/products/{id}` | Single product by id |
| GET | `/products/next-sku` | Next available SKU |
| POST | `/products` | Create a product |
| PUT | `/products/{id}` | Update a product |
| DELETE | `/products/{id}` | Delete a product |
| POST | `/products/upload-image` | Upload a product image, returns its path |

## Getting Started

### Prerequisites

- **.NET 8 SDK**
- **Node.js 18+** (includes npm)
- **SQL Server LocalDB** (the backend uses `(localdb)\MSSQLLocalDB`)

> **Note for Apple Silicon / Parallels users:** LocalDB is x64-only. If you develop on a Windows-on-ARM VM, the backend must run as an x64 process (e.g. pin `win-x64`) so it can load the LocalDB native library.

### Backend

```bash
cd backend
dotnet run
```

The API starts on `http://localhost:5000`. On first run it creates the database file, schema, stored procedures, and seed data automatically. Swagger UI is available at `/swagger` in development.

### Frontend

```bash
cd front
npm install
npm run dev
```

The dev server starts on `http://localhost:5173`.

> Start the **backend first**, then the frontend — the catalog fetches from `http://localhost:5000` and will show a "database unavailable" banner if the API isn't running.

## Project Structure

```
ProductsStore/
├── backend/
│   ├── Program.cs                     # Minimal-API endpoints, CORS, Swagger, startup
│   ├── Reposetories/
│   │   └── ProductsRepository.cs      # DB init, stored-proc calls, JSON mapping
│   ├── App_Data/                      # LocalDB .mdf / .ldf files (generated)
│   └── wwwroot/images/products/       # Uploaded product images
└── front/
    ├── src/
    │   ├── App.vue                    # Products view
    │   ├── composables/
    │   │   ├── useProducts.js         # Reactive product state + CRUD
    │   │   └── useSku.js              # SKU generation
    │   ├── services/
    │   │   └── productsApi.js         # HTTP layer
    │   └── utils/
    │       └── product.js            # normalizeProduct and helpers
    └── vite.config.js
```

## Notes

- The database is **self-initializing** — no manual migration step is required for a fresh setup.
- Stored procedures are created only if they don't already exist; to pick up changes to a procedure's definition, drop it first (or switch the definitions to `CREATE OR ALTER`).
- Product images are stored on disk with only their path persisted in the database.