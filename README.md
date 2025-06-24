> **Language Notice**: 
> [View in Arabic (العربية)](README_AR.md) |

# Three-Tier Architecture Solution Generator

![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![SQL Server](https://img.shields.io/badge/Microsoft_SQL_Server-CC2927?style=for-the-badge&logo=microsoft-sql-server&logoColor=white)

## Overview

This C# tool automatically generates a complete 3-tier architecture solution based on your SQL Server database schema. It creates four projects:

1. **DTO** (Data Transfer Objects)
2. **DAL** (Data Access Layer)
3. **BLL** (Business Logic Layer)
4. **ConsoleApp** (Presentation Layer)

The generated solution includes complete CRUD operations for all tables and a console-based interface for database interaction.

## Features

- 🚀 Automatic generation of 3-tier architecture projects
- 🔍 Reads SQL Server database schema (tables, columns, PKs, FKs)
- 📦 Creates DTO classes for all database tables
- 💾 Generates repository pattern implementation in DAL
- 🧠 Creates business logic layer with placeholder methods
- 💻 Builds console application for database interaction
- 🔄 Full CRUD operations for all database tables
- 📂 Generates solution file and project references
- ⚙️ Automatic App.config with connection string

## Getting Started

### Prerequisites
- .NET Framework 4.8
- SQL Server instance
- Visual Studio 2019 or newer (to open generated solution)

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/ThreeTierGenerator.git
   ```
2. Open the solution in Visual Studio
3. Build the solution

### Usage
1. Run the compiled executable
2. Provide your SQL Server connection details:
   - Server name
   - Database name
   - Authentication method (Windows or SQL Server)
   - Credentials (if using SQL Server authentication)
3. Specify the output path for the generated solution
4. The tool will generate the complete solution structure

### Manual Setup After Generation
1. Open the generated solution in Visual Studio
2. Add project references:
   - ConsoleApp → BLL
   - BLL → DAL
   - DAL → DTO
3. Verify the connection string in `ConsoleApp/App.config`

## Solution Structure
```
GeneratedSolution/
├── BLL/
│   ├── [TableName]Service.cs
│   └── BLL.csproj
├── DAL/
│   ├── [TableName]Repository.cs
│   └── DAL.csproj
├── DTO/
│   ├── [TableName]DTO.cs
│   └── DTO.csproj
├── ConsoleApp/
│   ├── App.config
│   ├── Program.cs
│   └── ConsoleApp.csproj
└── DatabaseSolution.sln
```

## Generated Components

### DTO Project
- Creates `[TableName]DTO` classes with properties mapping to database columns
- Handles nullability based on database schema
- Example:
  ```csharp
  public class CustomerDTO
  {
      public int CustomerID { get; set; }
      public string Name { get; set; }
      public string Email { get; set; }
      public DateTime? RegistrationDate { get; set; }
  }
  ```

### DAL Project
- Creates repository classes with complete CRUD operations:
  - `GetAll[TableName]s()`
  - `Get[TableName]ById()`
  - `Add[TableName]()`
  - `Update[TableName]()`
  - `Delete[TableName]()`
- Uses ADO.NET for database access
- Handles parameterized queries and null values

### BLL Project
- Creates service classes that act as intermediaries
- Contains business logic placeholders for validation
- Example:
  ```csharp
  public void AddCustomer(CustomerDTO item)
  {
      // Add validation and business logic here
      _repository.AddCustomer(item);
  }
  ```

### Console Application
- Dynamic menu system for all tables
- Complete CRUD interface:
  - List all records
  - View record details
  - Add new records
  - Update existing records
  - Delete records
- Automatic data type handling
- Tabular data display

## Limitations
- Currently supports SQL Server only
- Assumes integer PKs are identity columns
- Console app CRUD operations need implementation
- Limited error handling in generated code
- No support for stored procedures
- Complex relationships may require manual adjustments

## Contributing
Contributions are welcome! Please fork the repository and create a pull request with your improvements.

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Notes
- The generated solution targets .NET Framework 4.8
- You may need to adjust null handling for specific data types
- Business logic layer contains placeholders for custom validation
- Console application provides a starting point for UI development

For any questions or issues, please open an issue on GitHub.
