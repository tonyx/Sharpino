# Pub Management System (Sharpino.Sample.6)

## Overview

(A.I. generated: ma need further review)
This example showcases a Pub Management System built with Sharpino, demonstrating event sourcing patterns in F#. The system manages a pub's kitchen operations, including dishes, ingredients, suppliers, and inventory management.

## Key Features

1. **Dish Management**
   - Create and manage menu items
   - Define recipes with required ingredients
   - Track dish availability based on ingredient stock

2. **Ingredient Management**
   - Track ingredient inventory
   - Set minimum stock levels
   - Monitor ingredient usage across dishes

3. **Supplier Management**
   - Maintain supplier information
   - Track supplier-specific ingredients
   - Manage supplier relationships


## Domain Model

The system is built around these core aggregates:

- **Dish**: Represents a menu item with its recipe and required ingredients
- **Ingredient**: Tracks individual ingredients with current stock levels
- **Supplier**: Manages supplier information and relationships
- **Kitchen**: Coordinates operations between dishes and ingredients

## Business Rules

1. **Inventory Management**
   - Prevent dish preparation if ingredients are insufficient
   - Track minimum stock levels for automatic reordering
   - Update inventory when dishes are prepared

2. **Supplier Relations**
   - Maintain preferred suppliers for ingredients
   - Track supplier performance and reliability

3. **Menu Planning**
   - Ensure dishes can be prepared with available ingredients
   - Update menu availability based on stock levels

## Technical Implementation

1. **Event Sourcing**
   - Full audit trail of all changes
   - Event replay capabilities
   - Consistent state management

2. **Aggregate Design**
   - Clear boundaries between aggregates
   - Optimistic concurrency control

3. **Serialization**
   - JSON serialization for human-readable events
   - Binary serialization for compact storage
   - Custom serialization framework for flexibility

## Getting Started

### Prerequisites
- .NET 9.0 SDK
- F# development environment
- (Optional) PostgreSQL for persistent storage

### Setup
1. Clone the repository
2. Configure the database connection in `.env` if using PostgreSQL
3. Build the solution

### testing the Application
- set current directory to `Sharpino.Sample.6.Test`
```bash
dotnet run
```

## Example Usage

```fsharp
// Create a new dish
let pizza = Dish.MakeDish (Guid.NewGuid(), "Pizza Margherita", [tomatoSauceId; mozzarellaId; basilId])

// Add an ingredient to inventory
let tomato = Ingredient(Guid.NewGuid(), "Tomato Sauce", 10.0, 2.0) // 10 units in stock, 2.0 minimum

// Add a supplier
let supplier = Supplier(Guid.NewGuid(), "Fresh Ingredients Inc.", [tomato.Id])

// Initialize the pub system
let pubSystem = PubSystem(eventStore)

// Add items to the system
let addDish = pubSystem.AddDish pizza
let addIngredient = pubSystem.AddIngredient (tomato.Id, "Tomato Sauce")
let addSupplier = pubSystem.AddSupplier supplier
```

## Project Structure

- `Dishes/`: Contains dish-related aggregates and commands
- `Ingredients/`: Manages ingredient inventory and operations
- `Kitchen/`: Coordinates kitchen operations
- `Suppliers/`: Handles supplier management
- `PubSystem.fs`: Main system module coordinating all components
- `Commons.fs`: Shared types and utilities

## Dependencies

- Sharpino.Lib
- FSharp.Core
- DotNetEnv (for configuration)

## License

This example is part of the Sharpino library and is licensed under the MIT License.
