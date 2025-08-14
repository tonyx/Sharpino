# Shopping Cart with Sharpino

(A.I. Cascade helped me to write this)

## Overview

This project demonstrates a shopping cart system built with the Sharpino event sourcing library. It showcases how to implement a multi-tenant e-commerce system with support for inventory management, cart operations, and transaction handling using event sourcing principles.

## Key Features

1. **Goods Management**
   - Add/remove products with quantities
   - Track inventory levels
   - Unique product identification

2. **Shopping Cart**
   - Add/remove items from cart
   - Update quantities
   - Handle concurrent modifications

3. **Transaction Support**
   - Atomic operations across aggregates
   - Transaction rollback on failure
   - Consistent state management

4. **Undo/Redo**
   - Command undo functionality
   - State restoration on rollback
   - Transaction history

## Architecture

The system is built around three main aggregates:

1. **Good**
   - Represents a product in inventory
   - Tracks available quantity
   - Enforces business rules (e.g., no negative inventory)

2. **Cart**
   - Manages a user's shopping cart
   - Handles item additions/removals
   - Maintains cart state

3. **GoodsContainer**
   - Serves as a registry for goods and carts
   - Maintains references between entities
   - Provides a global view of the system

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- PostgreSQL (for persistent storage)
- Environment variables in `.env`:
  ```
  password=your_postgres_password
  ```

### Running the Application

1. Navigate to the project directory:
   ```bash
   cd Sharpino.Sample.7/shoppingCartWithSharpino
   ```

2. Run the tests:
   ```bash
   dotnet test
   ```

## Example Usage

```fsharp
// Initialize the supermarket
let supermarket = Supermarket(eventStore, eventBroker)

// Add a new product
let goodId = Guid.NewGuid()
let! _ = supermarket.AddGood(goodId, "Laptop", 10, 999.99m)

// Create a shopping cart
let cartId = Guid.NewGuid()
let! _ = supermarket.AddCart(cartId)

// Add items to cart
let! _ = supermarket.AddGoodToCart(cartId, goodId, 2)

// Get cart total
let! total = supermarket.GetCartTotal(cartId)
```

## Testing

The test suite verifies:
- Basic CRUD operations
- Transaction integrity
- Concurrent modifications
- Error conditions
- Undo functionality

Run tests with:
```bash
dotnet test --filter "FullyQualifiedName~Tests"
```

## Project Structure

- `Goods/` - Product management
  - `Good.fs` - Product aggregate
  - `Events.fs` - Product-related events
  - `Commands.fs` - Product commands

- `Cart/` - Shopping cart implementation
  - `Cart.fs` - Cart aggregate
  - `Events.fs` - Cart events
  - `Commands.fs` - Cart commands

- `GoodsContainer/` - Registry and references
  - `GoodsContainer.fs` - Container aggregate
  - `Events.fs` - Container events
  - `Commands.fs` - Container commands

- `SuperMarket.fs` - Main module with business logic
- `Tests.fs` - Test suite

## Dependencies

- Sharpino.Lib - Event sourcing library
- Expecto - Testing framework
- DotNetEnv - Environment management

## License

This project is part of the Sharpino library and is licensed under the MIT License.
