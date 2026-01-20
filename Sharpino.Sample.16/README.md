# Sharpino Sample - Event Sourcing with Command Coordination

Note: emulating Saga/Process manager

Events cannot directly fire new commands (yet)
Alternative is that commands are executed together with other commands responsible for handling those events.

## Overview

### Key Concept: Command Coordination
Unlike traditional event sourcing where events can trigger new commands, this implementation uses a coordinated command approach:
- **Commands** are the primary drivers of state changes
- **Events** represent the results of command execution
- **Command coordination** ensures atomic execution of related operations

### Core Components

#### 1. Domain Models
- **Material**: Raw materials with quantity tracking
- **Product**: Finished goods requiring materials
- **WorkOrder**: Production orders managing multiple products

#### 2. Command/Event Pattern
- **Commands**: Intent to change state (e.g., `Consume`, `Add`, `Start`, `Complete`)
- **Events**: Immutable facts about what happened (e.g., `Consumed`, `Added`, `Started`, `Completed`)
- **No Event → Command**: Events cannot trigger new commands directly


## Getting Started

### Prerequisites
- Docker and Docker Compose
- .NET 10.0 SDK

### Setup Database
```bash
docker compose up -d
```
This starts PostgreSQL and automatically runs migration scripts in order:
- Schema creation
- Table setup for Materials, Products, WorkOrders
- User permissions configuration

### Run Application
```bash
dotnet run
```

### Run Tests
```bash
dotnet test
```

## Domain Rules

### Quantity Validation
- All quantities must be positive (> 0)
- Private constructors prevent invalid quantity creation
- Safe creation through `Quantity.New` function

### Material Management
- Materials can be consumed and added
- Inventory tracking ensures no negative quantities
- Material consumption is atomic with work order creation

### Work Order Lifecycle
1. **Initialized**: Work order created, materials consumed
2. **InProgress**: At least one working item started
3. **FullyCompleted**: All working items completed
4. **SomeFailed**: Some items failed with material restoration

## Configuration

Edit `.env` file to modify:
- Database connection strings
- PostgreSQL credentials
- Application settings

## Project Structure

```
├── Models/
│   ├── Materials/     # Material domain
│   ├── Products/      # Product domain  
│   └── WorkOrders/    # Work order domain
├── db/
│   ├── schema.sql     # Database schema
│   └── migrations/   # Versioned migrations
├── MaterialManager.fs # Command coordination logic
├── Tests.fs         # Comprehensive test suite
└── docker-compose.yml # Database setup
```

## Testing Strategy

The test suite demonstrates:
- Safe quantity creation and validation
- Atomic material consumption
- Work order state transitions
- Material restoration on failures
- Command coordination scenarios

All tests verify that commands execute atomically and maintain system consistency.