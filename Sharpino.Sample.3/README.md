# Stadium Seat Booking System (Sharpino.Sample.3)

## Overview

(A. I. Cascade helped me to write this)
This example demonstrates a Stadium Seat Booking System built with Sharpino, showcasing event sourcing patterns in F#. The system manages seat bookings for a stadium, handling seat reservations, row management, and booking operations.

## Key Features

1. **Seat Management**
   - Track individual seat states (Booked/Free)
   - Manage seats within rows
   - Handle seat booking operations

2. **Row Management**
   - Create and manage rows of seats
   - Track row references in the stadium
   - Handle row-level operations

3. **Booking System**
   - Book multiple seats in a single transaction
   - Validate seat availability
   - Maintain booking history

## Domain Model

The system is built around these core aggregates:

- **Stadium**: The main aggregate that manages row references
- **SeatsRow**: Represents a row of seats with booking capabilities
- **Seat**: Individual seat with state (Booked/Free)
- **Booking**: Represents a booking transaction for one or more seats

## Business Rules

1. **Seat Booking**
   - Seats can be either Booked or Free
   - Multiple seats can be booked in a single transaction
   - Prevents double-booking of seats

2. **Row Management**
   - Rows can be added to the stadium
   - Rows can be removed if they have no bookings
   - Each row maintains its own set of seats

3. **Invariants**
   - qeted expressions contains the invariant conditions and can injected to any aggregatedynamically

## Technical Implementation

1. **Event Sourcing**
   - Full audit trail of all booking operations
   - Event replay capabilities
   - Consistent state management

2. **Aggregate Design**
   - Clear boundaries between aggregates
   - Optimistic concurrency control
   - Command validation

3. **Serialization**
   - JSON serialization for human-readable events
   - Custom serialization for domain objects

## Getting Started

### Prerequisites
- .NET 9.0 SDK
- F# development environment
- (Optional) PostgreSQL for persistent storage

### Setup
1. Clone the repository
2. Configure the database connection in `.env` if using PostgreSQL
3. Build the solution

### Testing the Application
- Set the current directory to the `Sharpino.Sample.3.Test` project directory.
```bash
dotnet run 
```

## Example Usage

```fsharp
// Initialize the system
let system = StadiumBookingSystem(eventStore)

// Add a new row to the stadium
let! rowId = system.AddRowReference()

// Book seats in the row
let booking = 
    { Id = 1
      SeatIds = [1; 2; 3] }

let! bookingResult = system.BookSeats rowId booking

// Get all rows and their seat status
let! allRows = system.GetAllRowsSeatsTo()
```

## Project Structure

- `Stadium/`: Contains the main stadium aggregate and related commands/events
- `Rows/`: Implements seat row management and booking logic
- `StadiumSystem.fs`: Main system module coordinating all components

## Dependencies

- Sharpino.Lib
- FSharp.Core
- FsToolkit.ErrorHandling
- FSharpPlus

## License

This example is part of the Sharpino library and is licensed under the MIT License.
