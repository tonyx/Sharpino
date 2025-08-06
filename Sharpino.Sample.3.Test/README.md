# Stadium Seat Booking System - Test Project

(A. I. Cascade helped me to write this)
This project contains the test suite for the Stadium Seat Booking System (Sharpino.Sample.3), which demonstrates event sourcing patterns using the Sharpino library.

## Test Coverage

The test suite verifies the following aspects of the Stadium Seat Booking System:

1. **Basic Row and Seat Management**
   - Adding and removing row references
   - Managing seats within rows
   - Validating seat states

2. **Booking Operations**
   - Single and multiple seat bookings
   - Booking constraints and validations
   - Error handling for invalid bookings

3. **Invariant Enforcement**
   - Middle seat constraint validation
   - State consistency checks
   - Concurrent booking scenarios

4. **Persistence**
   - In-memory storage tests
   - PostgreSQL integration tests
   - State restoration from event stream

## Running the Tests

### Prerequisites

- .NET 9.0 SDK
- PostgreSQL (for database tests)
- Environment variables set in `.env` file:
  ```
  password=your_postgres_password
  ```

### Running All Tests

```bash
dotnet test
```

### Running Specific Test Categories

To run only in-memory tests:

```bash
dotnet test --filter "FullyQualifiedName~Memory"
```

To run only PostgreSQL tests:

```bash
dotnet test --filter "FullyQualifiedName~Postgres"
```

## Test Structure

The test file `Sample.fs` is organized into test cases that verify different aspects of the system:

- **Basic Operations**: Tests for core functionality
- **Edge Cases**: Tests for boundary conditions and error scenarios
- **Concurrency**: Tests for handling concurrent operations
- **Persistence**: Tests for data persistence and retrieval

## Test Data

Test data is generated programmatically within each test case to ensure isolation and repeatability. The tests use both in-memory and PostgreSQL storage backends to verify behavior across different persistence layers.

## Debugging Tests

To debug tests in VS Code:

1. Set breakpoints in the test file
2. Use the VS Code test explorer to run/debug specific tests
3. Check test output in the Debug Console

## Test Output

Test results are displayed in the console with detailed information about passed and failed tests. For more detailed output, run:

```bash
dotnet test --logger "console;verbosity=detailed"
```

## Dependencies

- Expecto - F# test framework
- DotNetEnv - Environment variable management
- Microsoft.NET.Test.SDK - Test SDK
- YoloDev.Expecto.TestSdk - Test adapter for VS Code

## License

This test project is part of the Sharpino library and is licensed under the MIT License.
