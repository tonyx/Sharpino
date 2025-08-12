# Contributing to Sharpino

Thank you for your interest in contributing to Sharpino! We welcome contributions from the community to help improve this project. This document provides guidelines for contributing to the project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Testing](#testing)
- [Pull Request Process](#pull-request-process)
- [Reporting Issues](#reporting-issues)
- [Feature Requests](#feature-requests)
- [Documentation](#documentation)
- [License](#license)

## Code of Conduct

By participating in this project, you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md). Please read it before making any contributions.

## Getting Started

1. **Fork the repository** on GitHub
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/tonyx/Sharpino.git
   cd Sharpino
   ```
3. **Set up the development environment**:
    - Install [.NET SDK](https://dotnet.microsoft.com/download) (version specified in global.json)
    - Install [Visual Studio Code](https://code.visualstudio.com/) with Ionide extension for F# development
    - Or use JetBrains Rider/Visual Studio with F# support

4. **Build the solution**:
   ```bash
   dotnet build Sharpino.sln
   ```

5. **Running a single example**
   - Detailed instructions will come soon. However:
   - There are likely two options: using the eventstore (postgres) or the in-memory eventstore. 
   - In the tests you may find a way to select only in-memory event store
   - If you want to run the postgres eventstore you need to prepare it using the dbmate tool (see https://github.com/kevin-morgan/dbmate)
   - You may need to add a password for the user aimed to access to the postgres database in the .env file. Usually the username is 'safe'. The .env entry is like this: password=password

6. **Running all the examples**
   - for Unix systems there is a runTests.sh script that will run all the examples 
   - if you use Windows, you may need to create an equivalent .bat file
 
   ```cd exampledirectory``` 
## Development Workflow

1. **Create a new branch** for your feature or bugfix:
   ```bash
   git checkout -b feature/your-feature-name
   # or
   git checkout -b bugfix/issue-number-description
   ```

2. **Make your changes** following the coding standards

3. **Run tests** to ensure nothing is broken:
   ```bash
   dotnet test
   # or run specific test project
   cd Sharpino.Sample.Test
   dotnet test
   ```

4. **Commit your changes** with a clear and descriptive message:
   ```bash
   git commit -m "Add feature: brief description of changes"
   ```

5. **Push to your fork** and create a pull request

## Coding Standards

- Follow F# coding conventions and style guidelines
- Use meaningful names for variables, functions, and types
- Keep functions small and focused on a single responsibility
- Use consistent indentation (4 spaces)
- Follow functional programming principles where appropriate

## Testing

- Write unit tests for new features and bug fixes
- Ensure all tests pass before submitting a pull request
- Add integration tests for complex features
- Follow the existing test project structure

## Pull Request Process

1. Ensure any install or build dependencies are removed before the end of the layer when doing a build
2. Update the README.md with details of changes to the interface, this includes new environment variables, exposed ports, useful file locations, and container parameters
3. Increase the version numbers in any examples files and the README.md to the new version that this Pull Request would represent. The versioning scheme we use is [SemVer](http://semver.org/)
4. Your pull request should target the `main` branch
5. Make sure all tests pass
6. Update documentation as needed

## Reporting Issues

When reporting issues, please include:

- A clear title and description
- Steps to reproduce the issue
- Expected behavior
- Actual behavior
- Any relevant error messages or logs
- Version information (OS, .NET version, etc.)

## Feature Requests

For feature requests, please:

1. Check if a similar feature already exists or has been requested
2. Describe the feature and why it would be valuable
3. Include any relevant use cases or examples

## Documentation

Good documentation is crucial for the success of the project. When contributing:

- Update relevant documentation when adding new features or changing behavior
- Keep comments clear and concise
- Document any breaking changes
- Add examples for new features

## License

By contributing to this project, you agree that your contributions will be licensed under the project's [LICENSE](LICENSE) file.

---

Thank you for your interest in contributing to Sharpino! Your help is greatly appreciated.
