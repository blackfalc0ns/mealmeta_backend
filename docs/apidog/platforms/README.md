# MealMate Apidog projects

Import each OpenAPI file as a separate Apidog project:

| Platform | File |
|---|---|
| Customer | customer.openapi.json |
| Driver | driver.openapi.json |
| Restaurant | estaurant.openapi.json |
| Delivery Manager | delivery_manager.openapi.json |
| Admin | dmin.openapi.json |
| Shared | common.openapi.json |

The legacy customer registration route /api/v1/auth/register/customer is intentionally absent.
Use /api/v1/auth/customers/register only.
