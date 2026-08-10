# MealMate Apidog API

For the normal Apidog import, use the unified file:

`../mealmate-apidog.openapi.json`

It contains every platform in one OpenAPI document and groups operations with
the tags `Customer`, `Driver`, `Restaurant`, `Delivery Manager`, `Admin`, and
`Shared`.

Separate platform files are also available:

| Platform | File |
|---|---|
| Customer | `customer.openapi.json` |
| Driver | `driver.openapi.json` |
| Restaurant | `restaurant.openapi.json` |
| Delivery Manager | `delivery_manager.openapi.json` |
| Admin | `admin.openapi.json` |
| Shared | `common.openapi.json` |

The legacy customer registration route `/api/v1/auth/register/customer` is intentionally absent.
Use `/api/v1/auth/customers/register` only.
