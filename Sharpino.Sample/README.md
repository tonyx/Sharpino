# Sharpino.Sample - Todo Application with Categories and Tags

## ⚠️ Important Notice: Deprecation Warning

**This example demonstrates an older approach that is being deprecated.** The implementation in this sample primarily uses single-instance aggregates (Contexts) which is not the recommended pattern for production systems using Sharpino.

### Recommended Approach
For new development, please prefer using proper Aggregates (event-sourced objects) with multiple instances identified by specific IDs. The newer samples (Sharpino.Sample.3 and above) demonstrate these better practices.

## Overview

This is a sample Todo application built with Sharpino, demonstrating basic event sourcing concepts. The application allows managing todos with categories and tags in a web interface.

## Key Features

1. **Todo Management**
   - Create, update, and delete todos
   - Mark todos as completed
   - Organize todos with categories and tags

2. **Category Management**
   - Create and manage todo categories
   - Assign categories to todos

3. **Tag Management**
   - Create and manage tags
   - Assign multiple tags to todos

## Project Structure

- `Domain/` - Contains the core domain logic
  - `Todos/` - Todo-related aggregates and commands
  - `Categories/` - Category management
  - `Tags/` - Tag management
- `JSON/` - JSON serialization utilities
- `App.fs` - Main application module
- `AppVersions.fs` - Versioning support
- `Server.fs` - Web server setup (currently disabled)

## Technical Implementation

### Architecture
- Built using the Sharpino event sourcing library
- Uses single-instance aggregates (Contexts)
- Implements command-query separation
- Supports versioning of the domain model

### Storage
- Uses event sourcing for data persistence
- Supports in-memory and persistent storage backends
- Includes snapshotting for performance optimization

## Getting Started

### Prerequisites
- .NET 9.0 SDK
- F# development environment

### Setup
1. Clone the repository
2. Restore dependencies:
   ```bash
   dotnet restore
   ```
3. Configure the database connection in `.env` if using persistent storage

### Testing the Application
set the current directory to `Sharpino.Sample.Test`
```bash
dotnet run 
```

## Migration to Newer Patterns

If you're using this as a reference, consider these changes for modern Sharpino development:

1. **Use Multiple Aggregate Instances**
   - Instead of single-instance contexts, create multiple instances of aggregates with unique IDs

2. **Leverage Strong Typing**
   - Use F#'s type system to enforce domain constraints
   - Create distinct types for different IDs (e.g., `TodoId`, `CategoryId`)

3. **Event Sourcing Best Practices**
   - Design events to represent meaningful business occurrences
   - Keep aggregates small and focused
   - Use value objects for complex data structures

## Dependencies

- Sharpino.Lib
- FSharp.Core
- Fable.Remoting.Giraffe
- Saturn
- DotNetEnv

## License

This example is part of the Sharpino library and is licensed under the MIT License.
