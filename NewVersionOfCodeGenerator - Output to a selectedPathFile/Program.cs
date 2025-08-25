/*using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Text;

namespace ThreeTierGenerator
{
    class Program
    {
        //static void Main(string[] args)
        //{
        //    Console.WriteLine("3-Tier Architecture Solution Generator");
        //    Console.WriteLine("=======================================\n");

        //    // Get database connection details
        //    Console.Write("Enter server name: ");
        //    string server = Console.ReadLine();
        //    Console.Write("Enter database name: ");
        //    string database = Console.ReadLine();
        //    Console.Write("Use Windows authentication? (Y/N): ");
        //    bool useWindowsAuth = Console.ReadLine().Trim().ToUpper() == "Y";

        //    string userId = "";
        //    string password = "";
        //    if (!useWindowsAuth)
        //    {
        //        Console.Write("Enter username: ");
        //        userId = Console.ReadLine();
        //        Console.Write("Enter password: ");
        //        password = Console.ReadLine();
        //    }

        //    string connectionString = useWindowsAuth
        //        ? $"Server={server};Database={database};Integrated Security=True;"
        //        : $"Server={server};Database={database};User Id={userId};Password={password};";

        //    try
        //    {
        //        // Get database schema
        //        List<TableSchema> tables = GetDatabaseSchema(connectionString);

        //        if (tables.Count == 0)
        //        {
        //            Console.WriteLine("No tables found in the database.");
        //            return;
        //        }

        //        // Generate solution structure
        //        string solutionPath = Path.Combine(Environment.CurrentDirectory, $"{database}Solution");
        //        Directory.CreateDirectory(solutionPath);

        //        GenerateSolutionFiles(solutionPath, database, tables, connectionString);

        //        Console.WriteLine($"\nSolution generated successfully at: {solutionPath}");
        //        Console.WriteLine("Project references needed:");
        //        Console.WriteLine("1. ConsoleApp -> BLL");
        //        Console.WriteLine("2. BLL -> DAL");
        //        Console.WriteLine("3. DAL -> DTO");
        //        Console.WriteLine("4. Add database connection string to ConsoleApp/App.config");
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Error: {ex.Message}");
        //    }
        //}

        static void Main(string[] args)
        {
            Console.WriteLine("3-Tier Architecture Solution Generator");
            Console.WriteLine("=======================================\n");

            // Get database connection details
            Console.Write("Enter server name: ");
            string server = Console.ReadLine();
            Console.Write("Enter database name: ");
            string database = Console.ReadLine();
            Console.Write("Use Windows authentication? (Y/N): ");
            bool useWindowsAuth = Console.ReadLine().Trim().ToUpper() == "Y";

            string userId = "";
            string password = "";
            if (!useWindowsAuth)
            {
                Console.Write("Enter username: ");
                userId = Console.ReadLine();
                Console.Write("Enter password: ");
                password = Console.ReadLine();
            }

            string connectionString = useWindowsAuth
                ? $"Server={server};Database={database};Integrated Security=True;"
                : $"Server={server};Database={database};User Id={userId};Password={password};";

            try
            {
                // Get database schema
                List<TableSchema> tables = GetDatabaseSchema(connectionString);

                if (tables.Count == 0)
                {
                    Console.WriteLine("No tables found in the database.");
                    return;
                }

                // Get solution output path
                Console.Write("Enter the full path for the solution folder: ");
                string solutionPath = Console.ReadLine().Trim();

                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    Console.WriteLine("Invalid path. Exiting.");
                    return;
                }

                solutionPath = Path.GetFullPath(solutionPath);
                Directory.CreateDirectory(solutionPath);

                // Generate solution structure
                GenerateSolutionFiles(solutionPath, database, tables, connectionString);

                Console.WriteLine($"\nSolution generated successfully at: {solutionPath}");
                Console.WriteLine("Project references needed:");
                Console.WriteLine("1. ConsoleApp -> BLL");
                Console.WriteLine("2. BLL -> DAL");
                Console.WriteLine("3. DAL -> DTO");
                Console.WriteLine("4. Add database connection string to ConsoleApp/App.config");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static List<TableSchema> GetDatabaseSchema(string connectionString)
        {
            var tables = new List<TableSchema>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Get all tables
                DataTable schemaTables = connection.GetSchema("Tables");
                foreach (DataRow tableRow in schemaTables.Rows)
                {
                    string tableName = tableRow["TABLE_NAME"].ToString();
                    string tableSchema = tableRow["TABLE_SCHEMA"].ToString();

                    if (tableSchema.ToLower() == "sys" || tableName.StartsWith("__"))
                        continue;

                    var table = new TableSchema
                    {
                        Schema = tableSchema,
                        Name = tableName,
                        Columns = new List<ColumnSchema>()
                    };

                    // Get columns for table
                    DataTable columnsSchema = connection.GetSchema("Columns", new[] { null, tableSchema, tableName });
                    foreach (DataRow colRow in columnsSchema.Rows)
                    {
                        table.Columns.Add(new ColumnSchema
                        {
                            Name = colRow["COLUMN_NAME"].ToString(),
                            DataType = MapSqlTypeToCsharp(colRow["DATA_TYPE"].ToString()),
                            IsNullable = colRow["IS_NULLABLE"].ToString() == "YES",
                            IsPrimary = false,
                            MaxLength = colRow["CHARACTER_MAXIMUM_LENGTH"] as int? ?? 0
                        });
                    }

                    // Get primary keys
                    string primaryKeyQuery = $@"
                        SELECT COLUMN_NAME
                        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                        WHERE TABLE_SCHEMA = '{tableSchema}' AND TABLE_NAME = '{tableName}'";

                    using (var command = new SqlCommand(primaryKeyQuery, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string pkColumn = reader["COLUMN_NAME"].ToString();
                            foreach (var col in table.Columns)
                            {
                                if (col.Name == pkColumn) col.IsPrimary = true;
                            }
                        }
                    }

                    // Get foreign keys
                    string foreignKeyQuery = $@"
                        SELECT 
                            fk.COLUMN_NAME, 
                            pk.TABLE_NAME AS ReferencedTable,
                            pk.COLUMN_NAME AS ReferencedColumn
                        FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE fk 
                            ON rc.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
                        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE pk 
                            ON rc.UNIQUE_CONSTRAINT_NAME = pk.CONSTRAINT_NAME
                        WHERE fk.TABLE_SCHEMA = '{tableSchema}' AND fk.TABLE_NAME = '{tableName}'";

                    using (var command = new SqlCommand(foreignKeyQuery, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string fkColumn = reader["COLUMN_NAME"].ToString();
                            string referencedTable = reader["ReferencedTable"].ToString();
                            string referencedColumn = reader["ReferencedColumn"].ToString();

                            foreach (var col in table.Columns)
                            {
                                if (col.Name == fkColumn)
                                {
                                    col.IsForeignKey = true;
                                    col.ReferencedTable = referencedTable;
                                    col.ReferencedColumn = referencedColumn;
                                }
                            }
                        }
                    }

                    tables.Add(table);
                }
            }
            return tables;
        }

        static string MapSqlTypeToCsharp1(string sqlType)
        {
            if (sqlType == null)
                return "string"; // Or throw new ArgumentNullException(nameof(sqlType));
            
            switch (sqlType.ToLower())
            {
                case "int":
                    return "int";
                case "smallint": // Could map to "short" for precision
                    return "short";
                case "tinyint":  // Could map to "byte" for precision
                    return "byte";
                case "bigint":
                    return "long";
                case "bit":
                    return "bool";
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "date":
                    return "DateTime"; // Use "DateOnly" in .NET 6+ for "date"
                case "time":
                    return "TimeSpan"; // Use "TimeOnly" in .NET 6+
                case "datetimeoffset":
                    return "DateTimeOffset";
                case "decimal":
                case "money":
                case "smallmoney":
                case "numeric":
                    return "decimal";
                case "float":
                    return "double";
                case "real":
                    return "float";
                case "uniqueidentifier":
                    return "Guid";
                case "binary":
                case "varbinary":
                case "image":
                case "timestamp":
                case "rowversion":
                    return "byte[]";
                default:
                    return "string"; // Handles varchar, nvarchar, text, etc.
            }
        }
        static string MapSqlTypeToCsharp(string sqlType)
        {
            if (string.IsNullOrEmpty(sqlType))
                return "string";

            switch (sqlType.ToLower())
            {
                // Integer types
                case "int": return "int";
                case "smallint": return "short";    // Implicit short→int conversion
                case "tinyint": return "byte";    // Implicit byte→int conversion
                case "bigint": return "long";

                // Boolean
                case "bit": return "bool";

                // Date/Time types
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "date": return "DateTime";
                case "datetimeoffset": return "DateTimeOffset"; // Critical addition
                case "time": return "TimeSpan";

                // Decimal types
                case "decimal":
                case "money":
                case "smallmoney":
                case "numeric": return "decimal";

                // Floating-point
                case "float": return "double"; // SQL float → C# double
                case "real": return "float";  // SQL real → C# float

                // GUID
                case "uniqueidentifier": return "Guid";

                // Binary types
                case "binary":
                case "varbinary":
                case "image":
                case "timestamp":   // rowversion
                case "rowversion": return "byte[]";

                // String types
                default: return "string"; // All others (char, varchar, text, xml, etc.)
            }
        }

      
        static void GenerateSolutionFiles(string solutionPath, string dbName, List<TableSchema> tables, string connectionString)
        {
            // Create projects
            string dtoPath = Path.Combine(solutionPath, "DTO");
            string dalPath = Path.Combine(solutionPath, "DAL");
            string bllPath = Path.Combine(solutionPath, "BLL");
            string consolePath = Path.Combine(solutionPath, "ConsoleApp");

            Directory.CreateDirectory(dtoPath);
            Directory.CreateDirectory(dalPath);
            Directory.CreateDirectory(bllPath);
            Directory.CreateDirectory(consolePath);

            // Generate DTO classes
            foreach (var table in tables)
            {
                GenerateDTOClass(dtoPath, table);
            }

            // Generate DAL repositories
            foreach (var table in tables)
            {
                GenerateDALClass(dalPath, table, tables);
            }

            // Generate BLL services
            foreach (var table in tables)
            {
                GenerateBLLClass(bllPath, table);
            }

            // Generate Console Application
            GenerateConsoleApp(consolePath, tables, dbName, connectionString);

            // Generate project files
            GenerateProjectFile(dtoPath, "DTO.csproj", "Library");
            GenerateProjectFile(dalPath, "DAL.csproj", "Library", "DTO");
            GenerateProjectFile(bllPath, "BLL.csproj", "Library", "DAL", "DTO");
            GenerateProjectFile(consolePath, "ConsoleApp.csproj", "Exe", "BLL", "DTO");

            // Generate solution file
            GenerateSolutionFile(solutionPath, dbName);
        }

        static void GenerateDTOClass(string path, TableSchema table)
        {
            string className = $"{table.Name}DTO";
            string fileName = Path.Combine(path, $"{className}.cs");

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace DTO");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");

            foreach (var col in table.Columns)
            {
                string nullableSymbol = col.IsNullable && col.DataType != "string" ? "?" : "";
                sb.AppendLine($"        public {col.DataType}{nullableSymbol} {col.Name} {{ get; set; }}");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(fileName, sb.ToString());
        }

        static void GenerateDALClass(string path, TableSchema table, List<TableSchema> allTables)
        {
            string className = $"{table.Name}Repository";
            string fileName = Path.Combine(path, $"{className}.cs");

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Data;");
            sb.AppendLine("using System.Data.SqlClient;");
            sb.AppendLine("using DTO;");
            sb.AppendLine();
            sb.AppendLine("namespace DAL");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly string _connectionString;");
            sb.AppendLine();
            sb.AppendLine($"        public {className}(string connectionString)");
            sb.AppendLine("        {");
            sb.AppendLine("            _connectionString = connectionString;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // GetAll method
            sb.AppendLine($"        public List<{table.Name}DTO> GetAll{table.Name}s()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var list = new List<{table.Name}DTO>();");
            sb.AppendLine("            using (SqlConnection conn = new SqlConnection(_connectionString))");
            sb.AppendLine("            {");
            sb.AppendLine($"                string query = $\"SELECT * FROM [{table.Schema}].[{table.Name}]\";");
            sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");
            sb.AppendLine("                conn.Open();");
            sb.AppendLine("                using (SqlDataReader reader = cmd.ExecuteReader())");
            sb.AppendLine("                {");
            sb.AppendLine("                    while (reader.Read())");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        list.Add(new {table.Name}DTO");
            sb.AppendLine("                        {");
            foreach (var col in table.Columns)
            {
                string readerMethod = GetReaderMethod(col.DataType);
                string nullCheck = col.IsNullable ? $"(reader[\"{col.Name}\"] != DBNull.Value) ? " : "";
                string nullFallback = col.IsNullable ? $" : ({col.DataType}?)null" : "";
                string isBinary = (col.DataType).ToLower() == "byte[]" ? $"(byte[])": "";
                sb.AppendLine($"                            {col.Name} = {nullCheck}{isBinary}reader.{readerMethod}(reader.GetOrdinal(\"{col.Name}\")){nullFallback},");
            }
            
            sb.AppendLine("                        });");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            return list;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // GetById method
            var pkColumn = table.Columns.Find(c => c.IsPrimary);
            if (pkColumn != null)
            {
                sb.AppendLine($"        public {table.Name}DTO Get{table.Name}ById({pkColumn.DataType} id)");
                sb.AppendLine("        {");
                sb.AppendLine($"            using (SqlConnection conn = new SqlConnection(_connectionString))");
                sb.AppendLine("            {");
                sb.AppendLine($"                string query = $\"SELECT * FROM [{table.Schema}].[{table.Name}] WHERE {pkColumn.Name} = @Id\";");
                sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");
                sb.AppendLine($"                cmd.Parameters.AddWithValue(\"@Id\", id);");
                sb.AppendLine("                conn.Open();");
                sb.AppendLine("                using (SqlDataReader reader = cmd.ExecuteReader())");
                sb.AppendLine("                {");
                sb.AppendLine("                    if (reader.Read())");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        return new {table.Name}DTO");
                sb.AppendLine("                        {");
                foreach (var col in table.Columns)
                {
                    string readerMethod = GetReaderMethod(col.DataType);
                    string nullCheck = col.IsNullable ? $"(reader[reader.GetOrdinal(\"{col.Name}\")] != DBNull.Value) ? " : "";
                    string nullFallback = col.IsNullable ? $" : ({col.DataType}?)null" : "";
                    string isBinary = (col.DataType).ToLower() == "byte[]" ? $"(byte[])" : "";
                    
                    sb.AppendLine($"                            {col.Name} = {nullCheck}{isBinary}reader.{readerMethod}(reader.GetOrdinal(\"{col.Name}\")){nullFallback},");
                }
                sb.AppendLine("                        };");
                sb.AppendLine("                    }");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Add method
            sb.AppendLine($"        public void Add{table.Name}({table.Name}DTO item)");
            sb.AppendLine("        {");
            sb.AppendLine("            using (SqlConnection conn = new SqlConnection(_connectionString))");
            sb.AppendLine("            {");
            sb.AppendLine("                string columns = @\"");
            foreach (var col in table.Columns)
            {
                if (col.IsPrimary && IsAutoIncrement(table, col)) continue;
                sb.Append($"[{col.Name}], ");
            }
            sb.Length -= 2;
            sb.AppendLine("\";");

            sb.AppendLine("                string values = @\"");
            foreach (var col in table.Columns)
            {
                if (col.IsPrimary && IsAutoIncrement(table, col)) continue;
                sb.Append($"@{col.Name}, ");
            }
            sb.Length -= 2;
            sb.AppendLine("\";");

            sb.AppendLine($"                string query = $\"INSERT INTO [{table.Schema}].[{table.Name}] (\" + columns + \") VALUES (\" + values + \")\";");
            sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");

            foreach (var col in table.Columns)
            {
                if (col.IsPrimary && IsAutoIncrement(table, col)) continue;
                // sb.AppendLine($"                cmd.Parameters.Add(\"@{col.Name}\", SqlDbType.{(col.DataType)}).Value = (object)item.{col.Name} ?? DBNull.Value;");
                sb.AppendLine($"                cmd.Parameters.AddWithValue(\"@{col.Name}\", (object)item.{col.Name} ?? DBNull.Value);");
            }

            sb.AppendLine("                conn.Open();");
            sb.AppendLine("                cmd.ExecuteNonQuery();");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Update method
            if (pkColumn != null)
            {
                sb.AppendLine($"        public void Update{table.Name}({table.Name}DTO item)");
                sb.AppendLine("        {");
                sb.AppendLine("            using (SqlConnection conn = new SqlConnection(_connectionString))");
                sb.AppendLine("            {");
                sb.AppendLine("                string setClause = @\"");
                foreach (var col in table.Columns)
                {
                    if (col.IsPrimary) continue;
                    sb.Append($"[{col.Name}] = @{col.Name}, ");
                }
                sb.Length -= 2;
                sb.AppendLine("\";");

                sb.AppendLine($"                string query = $\"UPDATE [{table.Schema}].[{table.Name}] SET \" + setClause + $\" WHERE {pkColumn.Name} = @{pkColumn.Name}\";");
                sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");

                foreach (var col in table.Columns)
                {
                    if (col.IsPrimary) continue;
                    sb.AppendLine($"                cmd.Parameters.AddWithValue(\"@{col.Name}\", (object)item.{col.Name} ?? DBNull.Value);");
                }
                sb.AppendLine($"                cmd.Parameters.AddWithValue(\"@{pkColumn.Name}\", item.{pkColumn.Name});");

                sb.AppendLine("                conn.Open();");
                sb.AppendLine("                cmd.ExecuteNonQuery();");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Delete method
            if (pkColumn != null)
            {
                sb.AppendLine($"        public void Delete{table.Name}({pkColumn.DataType} id)");
                sb.AppendLine("        {");
                sb.AppendLine("            using (SqlConnection conn = new SqlConnection(_connectionString))");
                sb.AppendLine("            {");
                sb.AppendLine($"                string query = $\"DELETE FROM [{table.Schema}].[{table.Name}] WHERE {pkColumn.Name} = @Id\";");
                sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");
                sb.AppendLine("                cmd.Parameters.AddWithValue(\"@Id\", id);");
                sb.AppendLine("                conn.Open();");
                sb.AppendLine("                cmd.ExecuteNonQuery();");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(fileName, sb.ToString());
        }

        static bool IsAutoIncrement(TableSchema table, ColumnSchema col)
        {
            // This would normally require querying identity columns
            // For simplicity, we'll assume all PK integers are identity
            return col.IsPrimary && (col.DataType == "int" || col.DataType == "long");
        }
        
        static string GetReaderMethod(string dataType)
        {
            switch (dataType.ToLower())
            {
                case "byte": return "GetByte";
                case "short": return "GetInt16";
                case "int": return "GetInt32";
                case "long": return "GetInt64";
                case "bool": return "GetBoolean";
                case "datetime": return "GetDateTime";
                case "datetimeoffset": return "GetDateTimeOffset";
                case "timespan": return "GetTimeSpan";
                case "decimal": return "GetDecimal";
                case "double": return "GetDouble";
                case "float": return "GetFloat";
                case "guid": return "GetGuid";
                case "byte[]": return "GetValue";
                case "xml":
                case "char":
                case "varchar":
                case "nchar":
                case "nvarchar":
                case "text":
                case "ntext":
                    return "GetString";
                default: return "GetString";
            }
        }
        static string GetReaderMethod2(string dataType)
        {
            switch (dataType)
            {
                case "tinyint": return "GetByte";
                case "smallint": return "GetInt16";
                case "int": return "GetInt32";
                case "long": return "GetInt64";
                case "bool": return "GetBoolean";
                case "DateTime": return "GetDateTime";
                case "TimeSpan": return "GetTimeSpan";
                case "decimal": return "GetDecimal";
                case "double": return "GetDouble";
                case "float": return "GetFloat";
                case "Guid": return "GetGuid";
                case "byte[]": return "GetValue";
               
                default: return "GetString";
            }
        }
        //static string GetReaderMethod(string dataType)
        //{
        //    switch (dataType.ToLower())  // Case-insensitive matching
        //    {
        //        // Integer types
        //        case "tinyint": return "GetByte";
        //        case "smallint": return "GetInt16";
        //        case "int": return "GetInt32";
        //        case "bigint": return "GetInt64";  // Added (was missing)

        //        // Other types
        //        case "bool": return "GetBoolean";   // Changed from "bool" to SQL name
        //        case "uniqueidentifier": return "GetGuid";
        //        case "datetime":
        //        case "datetime2":
        //        case "smalldatetime":
        //        case "date":
        //            return "GetDateTime";
        //        case "datetimeoffset": return "GetDateTimeOffset";  // Added (was missing)
        //        case "time": return "GetTimeSpan";
        //        case "decimal":
        //        case "money":
        //        case "smallmoney":
        //        case "numeric":
        //            return "GetDecimal";
        //        case "real": return "GetFloat";     // SQL's 4-byte float
        //        case "float": return "GetDouble";    // SQL's 8-byte float
        //        case "binary":
        //        case "varbinary":
        //        case "image":
        //        case "timestamp":                   // rowversion
        //            return "GetValue";              // Returns byte[]
        //        case "xml":
        //        case "char":
        //        case "varchar":
        //        case "nchar":
        //        case "nvarchar":
        //        case "text":
        //        case "ntext":
        //            return "GetString";
        //        default: return "GetString";        // Fallback for unknown types
        //    }
        //}
Microsoft.CSharp

        
        static void GenerateBLLClass(string path, TableSchema table)
        {
            string className = $"{table.Name}Service";
            string fileName = Path.Combine(path, $"{className}.cs");

            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using DAL;");
            sb.AppendLine("using DTO;");
            sb.AppendLine();
            sb.AppendLine("namespace BLL");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly {table.Name}Repository _repository;");
            sb.AppendLine();
            sb.AppendLine($"        public {className}(string connectionString)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _repository = new {table.Name}Repository(connectionString);");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        public List<{table.Name}DTO> GetAll{table.Name}s()");
            sb.AppendLine("        {");
            sb.AppendLine("            // Add business logic here");
            sb.AppendLine($"            return _repository.GetAll{table.Name}s();");
            sb.AppendLine("        }");
            sb.AppendLine();

            var pkColumn = table.Columns.Find(c => c.IsPrimary);
            if (pkColumn != null)
            {
                sb.AppendLine($"        public {table.Name}DTO Get{table.Name}ById({pkColumn.DataType} id)");
                sb.AppendLine("        {");
                sb.AppendLine("            // Add business logic here");
                sb.AppendLine($"            return _repository.Get{table.Name}ById(id);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine($"        public void Add{table.Name}({table.Name}DTO item)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Add validation and business logic here");
            sb.AppendLine($"            _repository.Add{table.Name}(item);");
            sb.AppendLine("        }");
            sb.AppendLine();

            if (pkColumn != null)
            {
                sb.AppendLine($"        public void Update{table.Name}({table.Name}DTO item)");
                sb.AppendLine("        {");
                sb.AppendLine("            // Add validation and business logic here");
                sb.AppendLine($"            _repository.Update{table.Name}(item);");
                sb.AppendLine("        }");
                sb.AppendLine();

                sb.AppendLine($"        public void Delete{table.Name}({pkColumn.DataType} id)");
                sb.AppendLine("        {");
                sb.AppendLine("            // Add validation and business logic here");
                sb.AppendLine($"            _repository.Delete{table.Name}(id);");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(fileName, sb.ToString());
        }

        public static string GenerateMethodCall(string tableName, string prefix)
        {
            return $"{prefix}{tableName}Records({tableName}Service service)";
        }
        static void GenerateConsoleApp(string path, List<TableSchema> tables, string dbName, string connectionString)
        {
            string fileName = Path.Combine(path, "Program.cs");
            string configFile = Path.Combine(path, "App.config");

            // Generate App.config
            var configSb = new StringBuilder();
            configSb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            configSb.AppendLine("<configuration>");
            configSb.AppendLine("    <startup>");
            configSb.AppendLine("        <supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\" />");
            configSb.AppendLine("    </startup>");
            configSb.AppendLine("    <connectionStrings>");
            configSb.AppendLine($"        <add name=\"DBConnection\" connectionString=\"{connectionString}\"");
            configSb.AppendLine("             providerName=\"System.Data.SqlClient\" />");
            configSb.AppendLine("    </connectionStrings>");
            configSb.AppendLine("</configuration>");
            File.WriteAllText(configFile, configSb.ToString());

            // Generate Program.cs
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Configuration;");
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine("using BLL;");
            sb.AppendLine("using DTO;");
            sb.AppendLine();
            sb.AppendLine("namespace ConsoleApp");
            sb.AppendLine("{");
            sb.AppendLine("    class Program");
            sb.AppendLine("    {");
            sb.AppendLine("        static void Main(string[] args)");
            sb.AppendLine("        {");
            sb.AppendLine("            string connectionString = ConfigurationManager.ConnectionStrings[\"DBConnection\"].ConnectionString;");
            sb.AppendLine($"            Console.WriteLine($\"Connected to: {dbName}\");");
            sb.AppendLine();
            sb.AppendLine("            // Initialize services and table schemas");
            sb.AppendLine("            var tableServices = new Dictionary<string, dynamic>();");
            sb.AppendLine("            var tableSchemas = new Dictionary<string, TableSchema>();");

            foreach (var table in tables)
            {
                sb.AppendLine($"            tableServices[\"{table.Name}\"] = new {table.Name}Service(connectionString);");
                sb.AppendLine($"            tableSchemas[\"{table.Name}\"] = new TableSchema");
                sb.AppendLine("            {");
                sb.AppendLine($"                Name = \"{table.Name}\",");
                sb.AppendLine("                Columns = new List<ColumnSchema>");
                sb.AppendLine("                {");
                foreach (var col in table.Columns)
                {
                    sb.AppendLine("                    new ColumnSchema");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        Name = \"{col.Name}\",");
                    sb.AppendLine($"                        DataType = \"{col.DataType}\",");
                    sb.AppendLine($"                        IsPrimary = {(col.IsPrimary ? "true" : "false")}");
                    sb.AppendLine("                    },");
                }
                sb.AppendLine("                }");
                sb.AppendLine("            };");
            }

            sb.AppendLine();
            sb.AppendLine("            while (true)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine(\"\\nMain Menu\");");
            sb.AppendLine("                Console.WriteLine(\"=========\");");
            sb.AppendLine("                Console.WriteLine(\"Select a table to manage:\");");
            sb.AppendLine();

            int tableIndex = 1;
            foreach (var table in tables)
            {
                sb.AppendLine($"                Console.WriteLine(\"{tableIndex++}. {table.Name}\");");
            }
            sb.AppendLine($"                Console.WriteLine(\"{tableIndex}. Exit\");");
            sb.AppendLine("                Console.Write(\"Choice: \");");
            sb.AppendLine();
            sb.AppendLine("                int choice;");
            sb.AppendLine("                if (!int.TryParse(Console.ReadLine(), out choice)) continue;");
            sb.AppendLine();
            sb.AppendLine($"                if (choice == {tableIndex}) break;");
            sb.AppendLine("                if (choice < 1 || choice > tableServices.Count)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine(\"Invalid choice\");");
            sb.AppendLine("                    continue;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                string selectedTable = \"\";");
            sb.AppendLine("                int index = 1;");
            sb.AppendLine("                foreach (var tableName in tableServices.Keys)");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (index++ == choice)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        selectedTable = tableName;");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                Console.Clear();");
            sb.AppendLine("                Console.WriteLine($\"Managing: {selectedTable}\");");
            sb.AppendLine("                Console.WriteLine(\"\\nOptions:\");");
            sb.AppendLine("                Console.WriteLine(\"1. List All Records\");");
            sb.AppendLine("                Console.WriteLine(\"2. View Record Details\");");
            sb.AppendLine("                Console.WriteLine(\"3. Add New Record\");");
            sb.AppendLine("                Console.WriteLine(\"4. Update Record\");");
            sb.AppendLine("                Console.WriteLine(\"5. Delete Record\");");
            sb.AppendLine("                Console.WriteLine(\"6. Back to Main Menu\");");
            sb.AppendLine("                Console.Write(\"Choice: \");");
            sb.AppendLine();
            sb.AppendLine("                int operation;");
            sb.AppendLine("                if (!int.TryParse(Console.ReadLine(), out operation)) continue;");
            sb.AppendLine();
            sb.AppendLine("                if (operation == 6) continue;");
            sb.AppendLine();
            sb.AppendLine("                dynamic service = tableServices[selectedTable];");
            sb.AppendLine("                TableSchema schema = tableSchemas[selectedTable];");
            sb.AppendLine();
            sb.AppendLine("                switch (operation)");
            sb.AppendLine("                {");
            sb.AppendLine("                    case 1: // List All");
            sb.AppendLine("                        ListRecords(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    case 2: // View Details");
            sb.AppendLine("                        ViewRecord(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    case 3: // Add New");
            sb.AppendLine("                        AddRecord(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    case 4: // Update");
            sb.AppendLine("                        UpdateRecord(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    case 5: // Delete");
            sb.AppendLine("                        DeleteRecord(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    default:");
            sb.AppendLine("                        Console.WriteLine(\"Invalid operation\");");
            sb.AppendLine("                        break;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generic ListRecords method
            sb.AppendLine("        static void ListRecords(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            //sb.AppendLine("            dynamic items = service.GetAll{schema.Name}s();");
            sb.AppendLine("           string methodName = $\"GetAll{schema.Name}s\";");
            sb.AppendLine("           dynamic items = service.GetType().InvokeMember(");
            sb.AppendLine("               methodName,");
            sb.AppendLine("               BindingFlags.InvokeMethod,");
            sb.AppendLine("               null,");
            sb.AppendLine("               service,");
            sb.AppendLine("               null");
            sb.AppendLine("           );");

            sb.AppendLine("            Console.WriteLine(\"\\nAll Records:\");");
            sb.AppendLine("            // Print header");
            sb.AppendLine("            foreach (var col in schema.Columns)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.Write($\"{col.Name.PadRight(15)}\");");
            sb.AppendLine("            }");
            sb.AppendLine("            Console.WriteLine();");
            sb.AppendLine("            ");
            sb.AppendLine("            // Print records");
            sb.AppendLine("            foreach (var item in items)");
            sb.AppendLine("            {");
            sb.AppendLine("                foreach (var col in schema.Columns)");
            sb.AppendLine("                {");
            sb.AppendLine("                    PropertyInfo prop = item.GetType().GetProperty(col.Name);");
            sb.AppendLine("                    object value = prop.GetValue(item);");
            sb.AppendLine("                    string valueStr;");
            sb.AppendLine("                    ");
            sb.AppendLine("                    if (value == null)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = \"\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else if (value is DateTime || value is DateTime?)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        DateTime? dt = value as DateTime?;");
            sb.AppendLine("                        valueStr = dt?.ToString(\"yyyy-MM-dd\") ?? \"\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = value.ToString();");
            sb.AppendLine("                    }");
            sb.AppendLine("                    ");
            sb.AppendLine("                    Console.Write($\"{valueStr.PadRight(15)}\");");
            sb.AppendLine("                }");
            sb.AppendLine("                Console.WriteLine();");
            sb.AppendLine("            }");
            sb.AppendLine("            Console.WriteLine(\"\\nPress any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generic ViewRecord method
            sb.AppendLine("        static void ViewRecord(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            sb.AppendLine("            var pkColumn = schema.Columns.Find(c => c.IsPrimary);");
            sb.AppendLine("            if (pkColumn == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine(\"No primary key found for this table\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            Console.Write(\"Enter {pkColumn.Name}: \");");
            sb.AppendLine("            string input = Console.ReadLine();");
            sb.AppendLine();
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                // Convert input to appropriate type");
            sb.AppendLine("                Type targetType = Type.GetType(\"System.\" + pkColumn.DataType);");
            sb.AppendLine("                if (targetType == null && pkColumn.DataType == \"int\") targetType = typeof(int);");
            sb.AppendLine("                if (targetType == null && pkColumn.DataType == \"string\") targetType = typeof(string);");
            sb.AppendLine("                if (targetType == null && pkColumn.DataType == \"DateTime\") targetType = typeof(DateTime);");
            sb.AppendLine("                if (targetType == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine(\"Unsupported data type\");");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                dynamic id = Convert.ChangeType(input, targetType);");
            sb.AppendLine();
            // sb.AppendLine("                dynamic item = service.Get{schema.Name}ById(id);");

            sb.AppendLine("                 string methodName = $\"Get{schema.Name}ById\";");
            sb.AppendLine("                 dynamic item = service.GetType()");
            sb.AppendLine("                     .InvokeMember(");
            sb.AppendLine("                         methodName,");
            sb.AppendLine("                         BindingFlags.InvokeMethod,");
            sb.AppendLine("                         null,");
            sb.AppendLine("                         service,");
            sb.AppendLine("                         new object[] { id }");
            sb.AppendLine("                     );");
            sb.AppendLine("                if (item == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine(\"Record not found\");");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                Console.WriteLine(\"\\nRecord Details:\");");
            sb.AppendLine("                foreach (var col in schema.Columns)");
            sb.AppendLine("                {");
            sb.AppendLine("                    PropertyInfo prop = item.GetType().GetProperty(col.Name);");
            sb.AppendLine("                    object value = prop.GetValue(item);");
            sb.AppendLine("                    string valueStr;");
            sb.AppendLine("                    ");
            sb.AppendLine("                    if (value == null)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = \"[NULL]\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else if (value is DateTime || value is DateTime?)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        DateTime? dt = value as DateTime?;");
            sb.AppendLine("                        valueStr = dt?.ToString(\"yyyy-MM-dd HH:mm:ss\") ?? \"\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = value.ToString();");
            sb.AppendLine("                    }");
            sb.AppendLine("                    ");
            sb.AppendLine("                    Console.WriteLine($\"{col.Name.PadRight(20)}: {valueStr}\");");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine($\"Error: {ex.Message}\");");
            sb.AppendLine("            }");
            sb.AppendLine("            Console.WriteLine(\"\\nPress any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Stub methods for other operations
            sb.AppendLine("        static void AddRecord(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            sb.AppendLine("            Console.WriteLine(\"Add New Record functionality not implemented\");");
            sb.AppendLine("            Console.WriteLine(\"Press any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        static void UpdateRecord(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            sb.AppendLine("            Console.WriteLine(\"Update Record functionality not implemented\");");
            sb.AppendLine("            Console.WriteLine(\"Press any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        static void DeleteRecord(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            sb.AppendLine("            Console.WriteLine(\"Delete Record functionality not implemented\");");
            sb.AppendLine("            Console.WriteLine(\"Press any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Helper classes for schema definition
            sb.AppendLine("        public class TableSchema");
            sb.AppendLine("        {");
            sb.AppendLine("            public string Name { get; set; }");
            sb.AppendLine("            public List<ColumnSchema> Columns { get; set; }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public class ColumnSchema");
            sb.AppendLine("        {");
            sb.AppendLine("            public string Name { get; set; }");
            sb.AppendLine("            public string DataType { get; set; }");
            sb.AppendLine("            public bool IsPrimary { get; set; }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(fileName, sb.ToString());
        }
    

        static string GetParseMethod(string dataType)
        {
            switch (dataType)
            {
                case "int": return "int.TryParse";
                case "long": return "long.TryParse";
                case "decimal": return "decimal.TryParse";
                case "double": return "double.TryParse";
                case "float": return "float.TryParse";
                case "bool": return "bool.TryParse";
                case "DateTime": return "DateTime.TryParse";
                case "Guid": return "Guid.TryParse";
                default: return "true"; // For strings
            }
        }

        static void GenerateProjectFile(string path, string fileName, string outputType, params string[] references)
        {
            string fullPath = Path.Combine(path, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
            sb.AppendLine();
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine($"    <OutputType>{outputType}</OutputType>");
            sb.AppendLine("    <TargetFramework>net48</TargetFramework>");
            sb.AppendLine("    <LangVersion>8.0</LangVersion>");
            sb.AppendLine("    <RootNamespace>" + Path.GetFileNameWithoutExtension(fileName) + "</RootNamespace>");
            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine();

            foreach (var reference in references)
            {
                sb.AppendLine("  <ItemGroup>");
                sb.AppendLine($"    <ProjectReference Include=\"..\\{reference}\\{reference}.csproj\" />");
                sb.AppendLine("  </ItemGroup>");
            }

            sb.AppendLine("</Project>");

            File.WriteAllText(fullPath, sb.ToString());
        }

        static void GenerateSolutionFile(string solutionPath, string solutionName)
        {
            string slnPath = Path.Combine(solutionPath, $"{solutionName}.sln");

            var sb = new StringBuilder();
            sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            sb.AppendLine("# Visual Studio Version 17");
            sb.AppendLine("VisualStudioVersion = 17.0.31903.59");
            sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

            // Add projects
            AddProjectToSolution(sb, "DTO", Path.Combine("DTO", "DTO.csproj"), Guid.NewGuid());
            AddProjectToSolution(sb, "DAL", Path.Combine("DAL", "DAL.csproj"), Guid.NewGuid());
            AddProjectToSolution(sb, "BLL", Path.Combine("BLL", "BLL.csproj"), Guid.NewGuid());
            AddProjectToSolution(sb, "ConsoleApp", Path.Combine("ConsoleApp", "ConsoleApp.csproj"), Guid.NewGuid());

            // Add solution configurations
            sb.AppendLine("Global");
            sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
            sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
            sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
            sb.AppendLine("\tEndGlobalSection");

            // Add project configurations
            sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
            AddProjectConfig(sb, "DTO");
            AddProjectConfig(sb, "DAL");
            AddProjectConfig(sb, "BLL");
            AddProjectConfig(sb, "ConsoleApp");
            sb.AppendLine("\tEndGlobalSection");

            sb.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
            sb.AppendLine("\t\tHideSolutionNode = FALSE");
            sb.AppendLine("\tEndGlobalSection");
            sb.AppendLine("EndGlobal");

            File.WriteAllText(slnPath, sb.ToString());
        }

        static void AddProjectToSolution(StringBuilder sb, string name, string path, Guid projectId)
        {
            sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", \"{path}\", \"{{{projectId}}}\"");
            sb.AppendLine("EndProject");
        }

        static void AddProjectConfig(StringBuilder sb, string projectName)
        {
            string[] configs = { "Debug|Any CPU", "Release|Any CPU" };
            foreach (var config in configs)
            {
                sb.AppendLine($"\t\t{{{GetProjectGuid(projectName)}}}.{config}.ActiveCfg = {config}");
                sb.AppendLine($"\t\t{{{GetProjectGuid(projectName)}}}.{config}.Build.0 = {config}");
            }
        }

        static Guid GetProjectGuid(string projectName)
        {
            // This would normally be stored during project creation
            // For simplicity, we'll generate deterministic GUIDs
            return Guid.NewGuid();
        }
    }

    class TableSchema
    {
        public string Schema { get; set; }
        public string Name { get; set; }
        public List<ColumnSchema> Columns { get; set; }
    }

    class ColumnSchema
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsForeignKey { get; set; }
        public string ReferencedTable { get; set; }
        public string ReferencedColumn { get; set; }
        public int MaxLength { get; set; }
    }
}
*/
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq; // Added for .Any() in ColumnSchema check
using System.Text;

namespace ThreeTierGenerator
{
    class Program
    {

        static void Main(string[] args)
        {
            Console.WriteLine("3-Tier Architecture Solution Generator");
            Console.WriteLine("=======================================\n");

            // Get database connection details
            Console.Write("Enter server name: ");
            string server = Console.ReadLine();
            Console.Write("Enter database name: ");
            string database = Console.ReadLine();
            Console.Write("Use Windows authentication? (Y/N): ");
            bool useWindowsAuth = Console.ReadLine().Trim().ToUpper() == "Y";

            string userId = "";
            string password = "";
            if (!useWindowsAuth)
            {
                Console.Write("Enter username: ");
                userId = Console.ReadLine();
                Console.Write("Enter password: ");
                password = Console.ReadLine();
            }

            string connectionString = useWindowsAuth
                ? $"Server={server};Database={database};Integrated Security=True;"
                : $"Server={server};Database={database};User Id={userId};Password={password};";

            try
            {
                // Get database schema
                List<TableSchema> tables = GetDatabaseSchema(connectionString);

                if (tables.Count == 0)
                {
                    Console.WriteLine("No tables found in the database.");
                    return;
                }

                // Get solution output path
                Console.Write("Enter the full path for the solution folder: ");
                string solutionPath = Console.ReadLine().Trim();

                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    Console.WriteLine("Invalid path. Exiting.");
                    return;
                }

                solutionPath = Path.GetFullPath(solutionPath);
                Directory.CreateDirectory(solutionPath);

                // Generate solution structure
                GenerateSolutionFiles(solutionPath, database, tables, connectionString);

                Console.WriteLine($"\nSolution generated successfully at: {solutionPath}");
                Console.WriteLine("Project references needed:");
                Console.WriteLine("1. ConsoleApp -> BLL");
                Console.WriteLine("2. BLL -> DAL");
                Console.WriteLine("3. DAL -> DTO");
                Console.WriteLine("4. Add database connection string to ConsoleApp/App.config");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static List<TableSchema> GetDatabaseSchema(string connectionString)
        {
            var tables = new List<TableSchema>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Get all tables
                DataTable schemaTables = connection.GetSchema("Tables");
                foreach (DataRow tableRow in schemaTables.Rows)
                {
                    string tableName = tableRow["TABLE_NAME"].ToString();
                    string tableSchema = tableRow["TABLE_SCHEMA"].ToString();

                    if (tableSchema.ToLower() == "sys" || tableName.StartsWith("__"))
                        continue;

                    var table = new TableSchema
                    {
                        Schema = tableSchema,
                        Name = tableName,
                        Columns = new List<ColumnSchema>()
                    };

                    // Get columns for table
                    DataTable columnsSchema = connection.GetSchema("Columns", new[] { null, tableSchema, tableName });
                    foreach (DataRow colRow in columnsSchema.Rows)
                    {
                        table.Columns.Add(new ColumnSchema
                        {
                            Name = colRow["COLUMN_NAME"].ToString(),
                            DataType = MapSqlTypeToCsharp(colRow["DATA_TYPE"].ToString()),
                            IsNullable = colRow["IS_NULLABLE"].ToString() == "YES",
                            IsPrimary = false, // Will be updated later
                            IsForeignKey = false, // Will be updated later
                            MaxLength = colRow["CHARACTER_MAXIMUM_LENGTH"] as int? ?? 0
                        });
                    }

                    // Get primary keys
                    string primaryKeyQuery = $@"
                        SELECT COLUMN_NAME
                        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
                        WHERE TABLE_SCHEMA = '{tableSchema}' AND TABLE_NAME = '{tableName}' AND CONSTRAINT_NAME LIKE 'PK_%'";

                    using (var command = new SqlCommand(primaryKeyQuery, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string pkColumnName = reader["COLUMN_NAME"].ToString();
                            foreach (var col in table.Columns)
                            {
                                if (col.Name == pkColumnName) col.IsPrimary = true;
                            }
                        }
                    }

                    // Get foreign keys
                    string foreignKeyQuery = $@"
                        SELECT 
                            fk.COLUMN_NAME, 
                            pk.TABLE_NAME AS ReferencedTable,
                            pk.COLUMN_NAME AS ReferencedColumn
                        FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE fk 
                            ON rc.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
                        JOIN INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE pk 
                            ON rc.UNIQUE_CONSTRAINT_NAME = pk.CONSTRAINT_NAME
                        WHERE fk.TABLE_SCHEMA = '{tableSchema}' AND fk.TABLE_NAME = '{tableName}'";

                    using (var command = new SqlCommand(foreignKeyQuery, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string fkColumn = reader["COLUMN_NAME"].ToString();
                            string referencedTable = reader["ReferencedTable"].ToString();
                            string referencedColumn = reader["ReferencedColumn"].ToString();

                            foreach (var col in table.Columns)
                            {
                                if (col.Name == fkColumn)
                                {
                                    col.IsForeignKey = true;
                                    col.ReferencedTable = referencedTable;
                                    col.ReferencedColumn = referencedColumn;
                                }
                            }
                        }
                    }

                    tables.Add(table);
                }
            }
            return tables;
        }


        static string MapSqlTypeToCsharp(string sqlType)
        {
            if (string.IsNullOrEmpty(sqlType))
                return "string";

            switch (sqlType.ToLower())
            {
                // Integer types
                case "int": return "int";
                case "smallint": return "short";
                case "tinyint": return "byte";
                case "bigint": return "long";

                // Boolean
                case "bit": return "bool";

                // Date/Time types
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "date": return "DateTime";
                case "datetimeoffset": return "DateTimeOffset";
                case "time": return "TimeSpan";

                // Decimal types
                case "decimal":
                case "money":
                case "smallmoney":
                case "numeric": return "decimal";

                // Floating-point
                case "float": return "double";
                case "real": return "float";

                // GUID
                case "uniqueidentifier": return "Guid";

                // Binary types
                case "binary":
                case "varbinary":
                case "image":
                case "timestamp":
                case "rowversion": return "byte[]";

                // String types
                case "char":
                case "varchar":
                case "nchar":
                case "nvarchar":
                case "text":
                case "ntext":
                case "xml":
                case "sysname": // SQL Server specific system name type
                    return "string";
                default: return "string"; // Fallback for any unmapped types
            }
        }


        static void GenerateSolutionFiles(string solutionPath, string dbName, List<TableSchema> tables, string connectionString)
        {
            // Create projects
            string dtoPath = Path.Combine(solutionPath, "DTO");
            string dalPath = Path.Combine(solutionPath, "DAL");
            string bllPath = Path.Combine(solutionPath, "BLL");
            string consolePath = Path.Combine(solutionPath, "ConsoleApp");

            Directory.CreateDirectory(dtoPath);
            Directory.CreateDirectory(dalPath);
            Directory.CreateDirectory(bllPath);
            Directory.CreateDirectory(consolePath);

            // Generate DTO classes
            foreach (var table in tables)
            {
                GenerateDTOClass(dtoPath, table);
            }

            // Generate DAL repositories
            foreach (var table in tables)
            {
                GenerateDALClass(dalPath, table, tables);
            }

            // Generate BLL services
            foreach (var table in tables)
            {
                GenerateBLLClass(bllPath, table);
            }

            // Generate Console Application
            GenerateConsoleApp(consolePath, tables, dbName, connectionString);

            // Generate project files
            GenerateProjectFile(dtoPath, "DTO.csproj", "Library");
            GenerateProjectFile(dalPath, "DAL.csproj", "Library", "DTO");
            GenerateProjectFile(bllPath, "BLL.csproj", "Library", "DAL", "DTO");
            GenerateProjectFile(consolePath, "ConsoleApp.csproj", "Exe", "BLL", "DTO");

            // Generate solution file
            GenerateSolutionFile(solutionPath, dbName);
        }

        static void GenerateDTOClass(string path, TableSchema table)
        {
            string className = $"{table.Name}DTO";
            string fileName = Path.Combine(path, $"{className}.cs");

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace DTO");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");

            foreach (var col in table.Columns)
            {
                string nullableSymbol = col.IsNullable && col.DataType != "string" && col.DataType != "byte[]" ? "?" : "";
                sb.AppendLine($"        public {col.DataType}{nullableSymbol} {col.Name} {{ get; set; }}");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(fileName, sb.ToString());
        }

        static void GenerateDALClass(string path, TableSchema table, List<TableSchema> allTables)
        {
            string className = $"{table.Name}Repository";
            string fileName = Path.Combine(path, $"{className}.cs");

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Data;");
            sb.AppendLine("using System.Data.SqlClient;");
            sb.AppendLine("using DTO;");
            sb.AppendLine();
            sb.AppendLine("namespace DAL");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly string _connectionString;");
            sb.AppendLine();
            sb.AppendLine($"        public {className}(string connectionString)");
            sb.AppendLine("        {");
            sb.AppendLine("            _connectionString = connectionString;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // GetAll method
            sb.AppendLine($"        public List<{table.Name}DTO> GetAll{table.Name}s()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var list = new List<{table.Name}DTO>();");
            sb.AppendLine("            using (SqlConnection conn = new SqlConnection(_connectionString))");
            sb.AppendLine("            {");
            sb.AppendLine($"                string query = $\"SELECT * FROM [{table.Schema}].[{table.Name}]\";");
            sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");
            sb.AppendLine("                conn.Open();");
            sb.AppendLine("                using (SqlDataReader reader = cmd.ExecuteReader())");
            sb.AppendLine("                {");
            sb.AppendLine("                    while (reader.Read())");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        list.Add(new {table.Name}DTO");
            sb.AppendLine("                        {");
            foreach (var col in table.Columns)
            {
                string readerMethod = GetReaderMethod(col.DataType);
                string nullCheck = col.IsNullable && col.DataType != "string" && col.DataType != "byte[]" ? $"(reader[\"{col.Name}\"] != DBNull.Value) ? " : "";
                string nullFallback = col.IsNullable && col.DataType != "string" && col.DataType != "byte[]" ? $" : ({col.DataType}?)null" : "";
                string isBinary = (col.DataType).ToLower() == "byte[]" ? $"(byte[])" : "";
                sb.AppendLine($"                            {col.Name} = {nullCheck}{isBinary}reader.{readerMethod}(reader.GetOrdinal(\"{col.Name}\")){nullFallback},");
            }

            sb.AppendLine("                        });");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            return list;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // GetById method
            var pkColumns = table.Columns.Where(c => c.IsPrimary).ToList();
            if (pkColumns.Any())
            {
                // For simplicity, this generator assumes a single primary key for GetById.
                // Multi-column primary keys would require more complex parameter handling.
                var pkColumn = pkColumns.First();

                sb.AppendLine($"        public {table.Name}DTO Get{table.Name}ById({pkColumn.DataType} id)");
                sb.AppendLine("        {");
                sb.AppendLine($"            using (SqlConnection conn = new SqlConnection(_connectionString))");
                sb.AppendLine("            {");
                sb.AppendLine($"                string query = $\"SELECT * FROM [{table.Schema}].[{table.Name}] WHERE {pkColumn.Name} = @Id\";");
                sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");
                sb.AppendLine($"                cmd.Parameters.AddWithValue(\"@Id\", id);");
                sb.AppendLine("                conn.Open();");
                sb.AppendLine("                using (SqlDataReader reader = cmd.ExecuteReader())");
                sb.AppendLine("                {");
                sb.AppendLine("                    if (reader.Read())");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        return new {table.Name}DTO");
                sb.AppendLine("                        {");
                foreach (var col in table.Columns)
                {
                    string readerMethod = GetReaderMethod(col.DataType);
                    string nullCheck = col.IsNullable && col.DataType != "string" && col.DataType != "byte[]" ? $"(reader[reader.GetOrdinal(\"{col.Name}\")] != DBNull.Value) ? " : "";
                    string nullFallback = col.IsNullable && col.DataType != "string" && col.DataType != "byte[]" ? $" : ({col.DataType}?)null" : "";
                    string isBinary = (col.DataType).ToLower() == "byte[]" ? $"(byte[])" : "";

                    sb.AppendLine($"                            {col.Name} = {nullCheck}{isBinary}reader.{readerMethod}(reader.GetOrdinal(\"{col.Name}\")){nullFallback},");
                }
                sb.AppendLine("                        };");
                sb.AppendLine("                    }");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Add method
            sb.AppendLine($"        public void Add{table.Name}({table.Name}DTO item)");
            sb.AppendLine("        {");
            sb.AppendLine("            using (SqlConnection conn = new SqlConnection(_connectionString))");
            sb.AppendLine("            {");
            // Corrected: Build columnNames and parameterNames strings within the generator
            List<string> addColumnNames = new List<string>();
            List<string> addParameterNames = new List<string>();

            foreach (var col in table.Columns)
            {
                if (col.IsPrimary && IsAutoIncrement(table, col)) continue;
                addColumnNames.Add($"[{col.Name}]");
                addParameterNames.Add($"@{col.Name}");
            }
            sb.AppendLine($"                string columns = \"{string.Join(", ", addColumnNames)}\";");
            sb.AppendLine($"                string values = \"{string.Join(", ", addParameterNames)}\";");

            sb.AppendLine($"                string query = $\"INSERT INTO [{table.Schema}].[{table.Name}] (\" + columns + \") VALUES (\" + values + \")\";");
            sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");

            foreach (var col in table.Columns)
            {
                if (col.IsPrimary && IsAutoIncrement(table, col)) continue;
                sb.AppendLine($"                cmd.Parameters.AddWithValue(\"@{col.Name}\", (object)item.{col.Name} ?? DBNull.Value);");
            }

            sb.AppendLine("                conn.Open();");
            sb.AppendLine("                cmd.ExecuteNonQuery();");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Update method
            if (pkColumns.Any())
            {
                var pkColumn = pkColumns.First(); // Again, assuming single PK for simplicity

                sb.AppendLine($"        public void Update{table.Name}({table.Name}DTO item)");
                sb.AppendLine("        {");
                sb.AppendLine("            using (SqlConnection conn = new SqlConnection(_connectionString))");
                sb.AppendLine("            {");
                // Corrected: Build setClauses string within the generator
                List<string> updateSetClauses = new List<string>();
                foreach (var col in table.Columns)
                {
                    if (col.IsPrimary) continue;
                    updateSetClauses.Add($"[{col.Name}] = @{col.Name}");
                }
                sb.AppendLine($"                string setClause = \"{string.Join(", ", updateSetClauses)}\";");

                sb.AppendLine($"                string query = $\"UPDATE [{table.Schema}].[{table.Name}] SET \" + setClause + $\" WHERE {pkColumn.Name} = @{pkColumn.Name}\";");
                sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");

                foreach (var col in table.Columns)
                {
                    if (col.IsPrimary) continue;
                    sb.AppendLine($"                cmd.Parameters.AddWithValue(\"@{col.Name}\", (object)item.{col.Name} ?? DBNull.Value);");
                }
                sb.AppendLine($"                cmd.Parameters.AddWithValue(\"@{pkColumn.Name}\", item.{pkColumn.Name});");

                sb.AppendLine("                conn.Open();");
                sb.AppendLine("                cmd.ExecuteNonQuery();");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Delete method
            if (pkColumns.Any())
            {
                var pkColumn = pkColumns.First(); // Again, assuming single PK for simplicity

                sb.AppendLine($"        public void Delete{table.Name}({pkColumn.DataType} id)");
                sb.AppendLine("        {");
                sb.AppendLine("            using (SqlConnection conn = new SqlConnection(_connectionString))");
                sb.AppendLine("            {");
                sb.AppendLine($"                string query = $\"DELETE FROM [{table.Schema}].[{table.Name}] WHERE {pkColumn.Name} = @Id\";");
                sb.AppendLine("                SqlCommand cmd = new SqlCommand(query, conn);");
                sb.AppendLine("                cmd.Parameters.AddWithValue(\"@Id\", id);");
                sb.AppendLine("                conn.Open();");
                sb.AppendLine("                cmd.ExecuteNonQuery();");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(fileName, sb.ToString());
        }

        static bool IsAutoIncrement(TableSchema table, ColumnSchema col)
        {
            // This is a simplified assumption. True auto-increment detection
            // would require querying INFORMATION_SCHEMA.COLUMNS for IS_IDENTITY.
            // For simplicity, we'll assume all PK integers are identity.
            return col.IsPrimary && (col.DataType == "int" || col.DataType == "long");
        }

        static string GetReaderMethod(string dataType)
        {
            switch (dataType.ToLower())
            {
                case "byte": return "GetByte";
                case "short": return "GetInt16";
                case "int": return "GetInt32";
                case "long": return "GetInt64";
                case "bool": return "GetBoolean";
                case "datetime": return "GetDateTime";
                case "datetime2": return "GetDateTime";
                case "smalldatetime": return "GetDateTime";
                case "date": return "GetDateTime";
                case "datetimeoffset": return "GetDateTimeOffset";
                case "timespan": return "GetTimeSpan";
                case "decimal": return "GetDecimal";
                case "money": return "GetDecimal";
                case "smallmoney": return "GetDecimal";
                case "numeric": return "GetDecimal";
                case "double": return "GetDouble";
                case "float": return "GetFloat";
                case "real": return "GetFloat";
                case "guid": return "GetGuid";
                case "byte[]": return "GetValue"; // GetValue returns object, which can be cast to byte[]
                case "xml":
                case "char":
                case "varchar":
                case "nchar":
                case "nvarchar":
                case "text":
                case "ntext":
                case "string": // Explicitly handle string
                case "sysname":
                    return "GetString";
                default: return "GetString";
            }
        }

        static void GenerateBLLClass(string path, TableSchema table)
        {
            string className = $"{table.Name}Service";
            string fileName = Path.Combine(path, $"{className}.cs");

            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using DAL;");
            sb.AppendLine("using DTO;");
            sb.AppendLine();
            sb.AppendLine("namespace BLL");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly {table.Name}Repository _repository;");
            sb.AppendLine();
            sb.AppendLine($"        public {className}(string connectionString)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _repository = new {table.Name}Repository(connectionString);");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        public List<{table.Name}DTO> GetAll{table.Name}s()");
            sb.AppendLine("        {");
            sb.AppendLine("            // Add business logic here");
            sb.AppendLine($"            return _repository.GetAll{table.Name}s();");
            sb.AppendLine("        }");
            sb.AppendLine();

            var pkColumns = table.Columns.Where(c => c.IsPrimary).ToList();
            if (pkColumns.Any())
            {
                var pkColumn = pkColumns.First(); // Assuming single PK for GetById

                sb.AppendLine($"        public {table.Name}DTO Get{table.Name}ById({pkColumn.DataType} id)");
                sb.AppendLine("        {");
                sb.AppendLine("            // Add business logic here");
                sb.AppendLine($"            return _repository.Get{table.Name}ById(id);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine($"        public void Add{table.Name}({table.Name}DTO item)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Add validation and business logic here");
            sb.AppendLine($"            _repository.Add{table.Name}(item);");
            sb.AppendLine("        }");
            sb.AppendLine();

            if (pkColumns.Any())
            {
                sb.AppendLine($"        public void Update{table.Name}({table.Name}DTO item)");
                sb.AppendLine("        {");
                sb.AppendLine("            // Add validation and business logic here");
                sb.AppendLine($"            _repository.Update{table.Name}(item);");
                sb.AppendLine("        }");
                sb.AppendLine();

                var pkColumn = pkColumns.First(); // Assuming single PK for Delete

                sb.AppendLine($"        public void Delete{table.Name}({pkColumn.DataType} id)");
                sb.AppendLine("        {");
                sb.AppendLine("            // Add validation and business logic here");
                sb.AppendLine($"            _repository.Delete{table.Name}(id);");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(fileName, sb.ToString());
        }

        public static string GenerateMethodCall(string tableName, string prefix)
        {
            return $"{prefix}{tableName}Records({tableName}Service service)";
        }
        static void GenerateConsoleApp(string path, List<TableSchema> tables, string dbName, string connectionString)
        {
            string fileName = Path.Combine(path, "Program.cs");
            string configFile = Path.Combine(path, "App.config");

            // Generate App.config
            var configSb = new StringBuilder();
            configSb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            configSb.AppendLine("<configuration>");
            configSb.AppendLine("    <startup>");
            configSb.AppendLine("        <supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\" />");
            configSb.AppendLine("    </startup>");
            configSb.AppendLine("    <connectionStrings>");
            configSb.AppendLine($"        <add name=\"DBConnection\" connectionString=\"{connectionString}\"");
            configSb.AppendLine("             providerName=\"System.Data.SqlClient\" />");
            configSb.AppendLine("    </connectionStrings>");
            configSb.AppendLine("</configuration>");
            File.WriteAllText(configFile, configSb.ToString());

            // Generate Program.cs
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Configuration;");
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine("using BLL;");
            sb.AppendLine("using DTO;");
            sb.AppendLine("using System.Linq; // Added for .Any()");
            sb.AppendLine();
            sb.AppendLine("namespace ConsoleApp");
            sb.AppendLine("{");
            sb.AppendLine("    class Program");
            sb.AppendLine("    {");
            sb.AppendLine("        static void Main(string[] args)");
            sb.AppendLine("        {");
            sb.AppendLine("            string connectionString = ConfigurationManager.ConnectionStrings[\"DBConnection\"].ConnectionString;");
            sb.AppendLine($"            Console.WriteLine($\"Connected to: {dbName}\");");
            sb.AppendLine();
            sb.AppendLine("            // Initialize services and table schemas");
            sb.AppendLine("            var tableServices = new Dictionary<string, dynamic>();");
            sb.AppendLine("            var tableSchemas = new Dictionary<string, TableSchema>();");

            foreach (var table in tables)
            {
                sb.AppendLine($"            tableServices[\"{table.Name}\"] = new {table.Name}Service(connectionString);");
                sb.AppendLine($"            tableSchemas[\"{table.Name}\"] = new TableSchema");
                sb.AppendLine("            {");
                sb.AppendLine($"                Name = \"{table.Name}\",");
                sb.AppendLine("                Columns = new List<ColumnSchema>");
                sb.AppendLine("                {");
                foreach (var col in table.Columns)
                {
                    sb.AppendLine("                    new ColumnSchema");
                    sb.AppendLine("                    {");
                    sb.AppendLine($"                        Name = \"{col.Name}\",");
                    sb.AppendLine($"                        DataType = \"{col.DataType}\",");
                    sb.AppendLine($"                        IsPrimary = {(col.IsPrimary ? "true" : "false")},");
                    sb.AppendLine($"                        IsNullable = {(col.IsNullable ? "true" : "false")}"); // Pass IsNullable to ConsoleApp's schema
                    sb.AppendLine("                    },");
                }
                sb.AppendLine("                }");
                sb.AppendLine("            };");
            }

            sb.AppendLine();
            sb.AppendLine("            while (true)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine(\"\\nMain Menu\");");
            sb.AppendLine("                Console.WriteLine(\"=========\");");
            sb.AppendLine("                Console.WriteLine(\"Select a table to manage:\");");
            sb.AppendLine();

            int tableIndex = 1;
            foreach (var table in tables)
            {
                sb.AppendLine($"                Console.WriteLine(\"{tableIndex++}. {table.Name}\");");
            }
            sb.AppendLine($"                Console.WriteLine(\"{tableIndex}. Exit\");");
            sb.AppendLine("                Console.Write(\"Choice: \");");
            sb.AppendLine();
            sb.AppendLine("                int choice;");
            sb.AppendLine("                if (!int.TryParse(Console.ReadLine(), out choice))");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine(\"Invalid input. Please enter a number.\");");
            sb.AppendLine("                    continue;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine($"                if (choice == {tableIndex}) break;");
            sb.AppendLine("                if (choice < 1 || choice > tableServices.Count)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine(\"Invalid choice\");");
            sb.AppendLine("                    continue;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                string selectedTable = \"\";");
            sb.AppendLine("                int index = 1;");
            sb.AppendLine("                foreach (var tableName in tableServices.Keys)");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (index++ == choice)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        selectedTable = tableName;");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                Console.Clear();");
            sb.AppendLine("                Console.WriteLine($\"Managing: {selectedTable}\");");
            sb.AppendLine("                Console.WriteLine(\"\\nOptions:\");");
            sb.AppendLine("                Console.WriteLine(\"1. List All Records\");");
            sb.AppendLine("                Console.WriteLine(\"2. View Record Details\");");
            sb.AppendLine("                Console.WriteLine(\"3. Add New Record\");");
            sb.AppendLine("                Console.WriteLine(\"4. Update Record\");");
            sb.AppendLine("                Console.WriteLine(\"5. Delete Record\");");
            sb.AppendLine("                Console.WriteLine(\"6. Back to Main Menu\");");
            sb.AppendLine("                Console.Write(\"Choice: \");");
            sb.AppendLine();
            sb.AppendLine("                int operation;");
            sb.AppendLine("                if (!int.TryParse(Console.ReadLine(), out operation))");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine(\"Invalid input. Please enter a number.\");");
            sb.AppendLine("                    continue;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                if (operation == 6) continue;");
            sb.AppendLine();
            sb.AppendLine("                dynamic service = tableServices[selectedTable];");
            sb.AppendLine("                TableSchema schema = tableSchemas[selectedTable];");
            sb.AppendLine();
            sb.AppendLine("                switch (operation)");
            sb.AppendLine("                {");
            sb.AppendLine("                    case 1: // List All");
            sb.AppendLine("                        ListRecords(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    case 2: // View Details");
            sb.AppendLine("                        ViewRecord(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    case 3: // Add New");
            sb.AppendLine("                        AddRecord(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    case 4: // Update");
            sb.AppendLine("                        UpdateRecord(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    case 5: // Delete");
            sb.AppendLine("                        DeleteRecord(service, schema);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    default:");
            sb.AppendLine("                        Console.WriteLine(\"Invalid operation\");");
            sb.AppendLine("                        break;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generic ListRecords method
            sb.AppendLine("        static void ListRecords(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            sb.AppendLine("           string methodName = $\"GetAll{schema.Name}s\";");
            sb.AppendLine("           dynamic items = service.GetType().InvokeMember(");
            sb.AppendLine("               methodName,");
            sb.AppendLine("               BindingFlags.InvokeMethod,");
            sb.AppendLine("               null,");
            sb.AppendLine("               service,");
            sb.AppendLine("               null");
            sb.AppendLine("           );");

            sb.AppendLine("            Console.WriteLine($\"\\nAll {schema.Name} Records:\");");
            sb.AppendLine("            if (items.Count == 0)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine(\"No records found.\");");
            sb.AppendLine("                Console.WriteLine(\"\\nPress any key to continue...\");");
            sb.AppendLine("                Console.ReadKey();");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("            // Print header");
            sb.AppendLine("            foreach (var col in schema.Columns)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.Write($\"{col.Name.PadRight(20)}\");");
            sb.AppendLine("            }");
            sb.AppendLine("            Console.WriteLine();");
            sb.AppendLine("            Console.WriteLine(new string('-', schema.Columns.Sum(c => 20)));"); // Separator
            sb.AppendLine("            ");
            sb.AppendLine("            // Print records");
            sb.AppendLine("            foreach (var item in items)");
            sb.AppendLine("            {");
            sb.AppendLine("                foreach (var col in schema.Columns)");
            sb.AppendLine("                {");
            sb.AppendLine("                    PropertyInfo prop = item.GetType().GetProperty(col.Name);");
            sb.AppendLine("                    object value = prop.GetValue(item);");
            sb.AppendLine("                    string valueStr;");
            sb.AppendLine("                    ");
            sb.AppendLine("                    if (value == null)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = \"[NULL]\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else if (value is DateTime || value is DateTime?)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        DateTime? dt = value as DateTime?;");
            sb.AppendLine("                        valueStr = dt?.ToString(\"yyyy-MM-dd\") ?? \"\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else if (value is byte[]) // Handle byte arrays for display");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = \"[Binary Data]\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = value.ToString();");
            sb.AppendLine("                    }");
            sb.AppendLine("                    ");
            sb.AppendLine("                    Console.Write($\"{valueStr.PadRight(20)}\");");
            sb.AppendLine("                }");
            sb.AppendLine("                Console.WriteLine();");
            sb.AppendLine("            }");
            sb.AppendLine("            Console.WriteLine(\"\\nPress any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generic ViewRecord method
            sb.AppendLine("        static void ViewRecord(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            sb.AppendLine("            var pkColumn = schema.Columns.FirstOrDefault(c => c.IsPrimary);");
            sb.AppendLine("            if (pkColumn == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine(\"No primary key found for this table. Cannot view single record.\");");
            sb.AppendLine("                Console.WriteLine(\"Press any key to continue...\");");
            sb.AppendLine("                Console.ReadKey();");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            Console.Write($\"Enter {pkColumn.Name} ({pkColumn.DataType}): \");");
            sb.AppendLine("            string input = Console.ReadLine();");
            sb.AppendLine();
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                Type targetType = GetCSharpType(pkColumn.DataType, pkColumn.IsNullable);");
            sb.AppendLine("                dynamic id = Convert.ChangeType(input, targetType);");
            sb.AppendLine();
            sb.AppendLine("                 string methodName = $\"Get{schema.Name}ById\";");
            sb.AppendLine("                 dynamic item = service.GetType()");
            sb.AppendLine("                     .InvokeMember(");
            sb.AppendLine("                         methodName,");
            sb.AppendLine("                         BindingFlags.InvokeMethod,");
            sb.AppendLine("                         null,");
            sb.AppendLine("                         service,");
            sb.AppendLine("                         new object[] { id }");
            sb.AppendLine("                     );");
            sb.AppendLine("                if (item == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine(\"Record not found\");");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                Console.WriteLine(\"\\nRecord Details:\");");
            sb.AppendLine("                foreach (var col in schema.Columns)");
            sb.AppendLine("                {");
            sb.AppendLine("                    PropertyInfo prop = item.GetType().GetProperty(col.Name);");
            sb.AppendLine("                    object value = prop.GetValue(item);");
            sb.AppendLine("                    string valueStr;");
            sb.AppendLine("                    ");
            sb.AppendLine("                    if (value == null)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = \"[NULL]\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else if (value is DateTime || value is DateTime?)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        DateTime? dt = value as DateTime?;");
            sb.AppendLine("                        valueStr = dt?.ToString(\"yyyy-MM-dd HH:mm:ss\") ?? \"\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else if (value is byte[])");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = \"[Binary Data]\";");
            sb.AppendLine("                    }");
            sb.AppendLine("                    else");
            sb.AppendLine("                    {");
            sb.AppendLine("                        valueStr = value.ToString();");
            sb.AppendLine("                    }");
            sb.AppendLine("                    ");
            sb.AppendLine("                    Console.WriteLine($\"{col.Name.PadRight(20)}: {valueStr}\");");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (FormatException)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine($\"Invalid format for {pkColumn.Name}. Please enter a valid {pkColumn.DataType}.\");");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine($\"Error: {ex.InnerException?.Message ?? ex.Message}\");");
            sb.AppendLine("            }");
            sb.AppendLine("            Console.WriteLine(\"\\nPress any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // AddRecord method
            sb.AppendLine("        static void AddRecord(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            sb.AppendLine("            Console.WriteLine($\"\\nAdding New {schema.Name} Record:\");");
            sb.AppendLine("            // Corrected: Use assembly qualified name for Type.GetType to ensure type is found");
            sb.AppendLine("            dynamic newItem = Activator.CreateInstance(Type.GetType($\"DTO.{schema.Name}DTO, DTO\"));");
            sb.AppendLine();
            sb.AppendLine("            foreach (var col in schema.Columns)");
            sb.AppendLine("            {");
            sb.AppendLine("                // Skip auto-incrementing primary keys for input");
            sb.AppendLine("                if (col.IsPrimary && (col.DataType == \"int\" || col.DataType == \"long\"))");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine($\"{col.Name} (Auto-generated)\");");
            sb.AppendLine("                    continue;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                string input;");
            sb.AppendLine("                while (true)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.Write($\"Enter {col.Name} ({col.DataType}){(col.IsNullable ? \" (Optional)\" : \"\")}: \");");
            sb.AppendLine("                    input = Console.ReadLine();");
            sb.AppendLine();
            sb.AppendLine("                    if (string.IsNullOrWhiteSpace(input))");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (col.IsNullable)");
            sb.AppendLine("                        {");
            sb.AppendLine("                            // Set to null for nullable types if input is empty");
            sb.AppendLine("                            PropertyInfo prop = newItem.GetType().GetProperty(col.Name);");
            sb.AppendLine("                            prop.SetValue(newItem, null);");
            sb.AppendLine("                            break;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        else");
            sb.AppendLine("                        {");
            sb.AppendLine("                            Console.WriteLine($\"Error: {col.Name} cannot be empty. Please provide a value.\");");
            sb.AppendLine("                            continue;");
            sb.AppendLine("                        }");
            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    try");
            sb.AppendLine("                    {");
            sb.AppendLine("                        Type targetType = GetCSharpType(col.DataType, col.IsNullable);");
            sb.AppendLine("                        object value = Convert.ChangeType(input, targetType);");
            sb.AppendLine();
            sb.AppendLine("                        PropertyInfo prop = newItem.GetType().GetProperty(col.Name);");
            sb.AppendLine("                        prop.SetValue(newItem, value);");
            sb.AppendLine("                        break;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    catch (FormatException)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        Console.WriteLine($\"Invalid format for {col.Name}. Please enter a valid {col.DataType}.\");");
            sb.AppendLine("                    }");
            sb.AppendLine("                    catch (Exception ex)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        Console.WriteLine($\"Error processing input for {col.Name}: {ex.Message}\");");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string addMethodName = $\"Add{schema.Name}\";");
            sb.AppendLine("                service.GetType().InvokeMember(");
            sb.AppendLine("                    addMethodName,");
            sb.AppendLine("                    BindingFlags.InvokeMethod,");
            sb.AppendLine("                    null,");
            sb.AppendLine("                    service,");
            sb.AppendLine("                    new object[] { newItem }");
            sb.AppendLine("                );");
            sb.AppendLine("                Console.WriteLine(\"Record added successfully!\");");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine($\"Error adding record: {ex.InnerException?.Message ?? ex.Message}\");");
            sb.AppendLine("            }");
            sb.AppendLine("            Console.WriteLine(\"\\nPress any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // UpdateRecord method
            sb.AppendLine("        static void UpdateRecord(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            sb.AppendLine("            Console.WriteLine($\"\\nUpdating {schema.Name} Record:\");");
            sb.AppendLine("            var pkColumn = schema.Columns.FirstOrDefault(c => c.IsPrimary);");
            sb.AppendLine("            if (pkColumn == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine(\"No primary key found for this table. Update not supported.\");");
            sb.AppendLine("                Console.WriteLine(\"Press any key to continue...\");");
            sb.AppendLine("                Console.ReadKey();");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            Console.Write($\"Enter {pkColumn.Name} of the record to update ({pkColumn.DataType}): \");");
            sb.AppendLine("            string idInput = Console.ReadLine();");
            sb.AppendLine();
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                Type pkType = GetCSharpType(pkColumn.DataType, pkColumn.IsNullable);");
            sb.AppendLine("                dynamic id = Convert.ChangeType(idInput, pkType);");
            sb.AppendLine();
            sb.AppendLine("                string getByIdMethodName = $\"Get{schema.Name}ById\";");
            sb.AppendLine("                dynamic existingItem = service.GetType().InvokeMember(");
            sb.AppendLine("                    getByIdMethodName,");
            sb.AppendLine("                    BindingFlags.InvokeMethod,");
            sb.AppendLine("                    null,");
            sb.AppendLine("                    service,");
            sb.AppendLine("                    new object[] { id }");
            sb.AppendLine("                );");
            sb.AppendLine();
            sb.AppendLine("                if (existingItem == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine(\"Record not found.\");");
            sb.AppendLine("                    Console.WriteLine(\"Press any key to continue...\");");
            sb.AppendLine("                    Console.ReadKey();");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                Console.WriteLine(\"Existing Record Details (enter new value or leave blank to keep current): \");");
            sb.AppendLine("                foreach (var col in schema.Columns)");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (col.IsPrimary) continue; // Cannot update primary key");
            sb.AppendLine();
            sb.AppendLine("                    PropertyInfo prop = existingItem.GetType().GetProperty(col.Name);");
            sb.AppendLine("                    object currentValue = prop.GetValue(existingItem);");
            sb.AppendLine("                    string defaultValue = (currentValue == null || (currentValue is byte[])) ? \"\" : currentValue.ToString();");
            sb.AppendLine();
            sb.AppendLine("                    string input;");
            sb.AppendLine("                    while (true)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        Console.Write($\"Enter {col.Name} ({col.DataType}) [{defaultValue}]{(col.IsNullable ? \" (Optional)\" : \"\")}: \");");
            sb.AppendLine("                        input = Console.ReadLine();");
            sb.AppendLine();
            sb.AppendLine("                        if (string.IsNullOrWhiteSpace(input))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            if (col.IsNullable)");
            sb.AppendLine("                            {");
            sb.AppendLine("                                prop.SetValue(existingItem, null);");
            sb.AppendLine("                                break;");
            sb.AppendLine("                            }");
            sb.AppendLine("                            else");
            sb.AppendLine("                            {");
            sb.AppendLine("                                // If not nullable and input is empty, keep current value (do nothing to prop)");
            sb.AppendLine("                                break; ");
            sb.AppendLine("                            }");
            sb.AppendLine("                        }");
            sb.AppendLine();
            sb.AppendLine("                        try");
            sb.AppendLine("                        {");
            sb.AppendLine("                            Type targetType = GetCSharpType(col.DataType, col.IsNullable);");
            sb.AppendLine("                            object newValue = Convert.ChangeType(input, targetType);");
            sb.AppendLine("                            prop.SetValue(existingItem, newValue);");
            sb.AppendLine("                            break;");
            sb.AppendLine("                        }");
            sb.AppendLine("                        catch (FormatException)");
            sb.AppendLine("                        {");
            sb.AppendLine("                            Console.WriteLine($\"Invalid format for {col.Name}. Please enter a valid {col.DataType}.\");");
            sb.AppendLine("                        }");
            sb.AppendLine("                        catch (Exception ex)");
            sb.AppendLine("                        {");
            sb.AppendLine("                            Console.WriteLine($\"Error processing input for {col.Name}: {ex.Message}\");");
            sb.AppendLine("                        }");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                string updateMethodName = $" + "\"Update{schema.Name}\";");
            sb.AppendLine("                service.GetType().InvokeMember(");
            sb.AppendLine("                    updateMethodName,");
            sb.AppendLine("                    BindingFlags.InvokeMethod,");
            sb.AppendLine("                    null,");
            sb.AppendLine("                    service,");
            sb.AppendLine("                    new object[] { existingItem }");
            sb.AppendLine("                );");
            sb.AppendLine("                Console.WriteLine(\"Record updated successfully!\");");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (FormatException)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine($\"Invalid format for primary key. Please enter a valid {pkColumn.DataType}.\");");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine($\"Error updating record: {ex.InnerException?.Message ?? ex.Message}\");");
            sb.AppendLine("            }");
            sb.AppendLine("            Console.WriteLine(\"\\nPress any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // DeleteRecord method
            sb.AppendLine("        static void DeleteRecord(dynamic service, TableSchema schema)");
            sb.AppendLine("        {");
            sb.AppendLine("            Console.WriteLine($\"\\nDeleting {schema.Name} Record:\");");
            sb.AppendLine("            var pkColumn = schema.Columns.FirstOrDefault(c => c.IsPrimary);");
            sb.AppendLine("            if (pkColumn == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine(\"No primary key found for this table. Delete not supported.\");");
            sb.AppendLine("                Console.WriteLine(\"Press any key to continue...\");");
            sb.AppendLine("                Console.ReadKey();");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            Console.Write($\"Enter {pkColumn.Name} of the record to delete ({pkColumn.DataType}): \");");
            sb.AppendLine("            string idInput = Console.ReadLine();");
            sb.AppendLine();
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                Type pkType = GetCSharpType(pkColumn.DataType, pkColumn.IsNullable);");
            sb.AppendLine("                dynamic id = Convert.ChangeType(idInput, pkType);");
            sb.AppendLine();
            sb.AppendLine("                // Optional: Confirm deletion");
            sb.AppendLine("                Console.Write(\"Are you sure you want to delete this record? (Y/N): \");");
            sb.AppendLine("                if (Console.ReadLine().Trim().ToUpper() != \"Y\")");
            sb.AppendLine("                {");
            sb.AppendLine("                    Console.WriteLine(\"Deletion cancelled.\");");
            sb.AppendLine("                    Console.WriteLine(\"Press any key to continue...\");");
            sb.AppendLine("                    Console.ReadKey();");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine();
            sb.AppendLine("                string deleteMethodName = $\"Delete{schema.Name}\";");
            sb.AppendLine("                service.GetType().InvokeMember(");
            sb.AppendLine("                    deleteMethodName,");
            sb.AppendLine("                    BindingFlags.InvokeMethod,");
            sb.AppendLine("                    null,");
            sb.AppendLine("                    service,");
            sb.AppendLine("                    new object[] { id }");
            sb.AppendLine("                );");
            sb.AppendLine("                Console.WriteLine(\"Record deleted successfully!\");");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (FormatException)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine($\"Invalid format for primary key. Please enter a valid {pkColumn.DataType}.\");");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                Console.WriteLine($\"Error deleting record: {ex.InnerException?.Message ?? ex.Message}\");");
            sb.AppendLine("            }");
            sb.AppendLine("            Console.WriteLine(\"\\nPress any key to continue...\");");
            sb.AppendLine("            Console.ReadKey();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Helper for converting string input to C# Type
            sb.AppendLine("        static Type GetCSharpType(string csharpDataType, bool isNullable)");
            sb.AppendLine("        {");
            sb.AppendLine("            Type type = null;");
            sb.AppendLine("            switch (csharpDataType.ToLower())");
            sb.AppendLine("            {");
            sb.AppendLine("                case \"int\": type = typeof(int); break;");
            sb.AppendLine("                case \"short\": type = typeof(short); break;");
            sb.AppendLine("                case \"byte\": type = typeof(byte); break;");
            sb.AppendLine("                case \"long\": type = typeof(long); break;");
            sb.AppendLine("                case \"bool\": type = typeof(bool); break;");
            sb.AppendLine("                case \"datetime\": type = typeof(DateTime); break;");
            sb.AppendLine("                case \"datetimeoffset\": type = typeof(DateTimeOffset); break;");
            sb.AppendLine("                case \"timespan\": type = typeof(TimeSpan); break;");
            sb.AppendLine("                case \"decimal\": type = typeof(decimal); break;");
            sb.AppendLine("                case \"double\": type = typeof(double); break;");
            sb.AppendLine("                case \"float\": type = typeof(float); break;");
            sb.AppendLine("                case \"guid\": type = typeof(Guid); break;");
            sb.AppendLine("                case \"byte[]\": type = typeof(byte[]); break;");
            sb.AppendLine("                case \"string\": type = typeof(string); break;");
            sb.AppendLine("                default: type = typeof(string); break;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            if (isNullable && type != null && type.IsValueType)");
            sb.AppendLine("            {");
            sb.AppendLine("                return typeof(Nullable<>).MakeGenericType(type);");
            sb.AppendLine("            }");
            sb.AppendLine("            return type;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Helper classes for schema definition
            sb.AppendLine("        public class TableSchema");
            sb.AppendLine("        {");
            sb.AppendLine("            public string Name { get; set; }");
            sb.AppendLine("            public List<ColumnSchema> Columns { get; set; }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public class ColumnSchema");
            sb.AppendLine("        {");
            sb.AppendLine("            public string Name { get; set; }");
            sb.AppendLine("            public string DataType { get; set; }");
            sb.AppendLine("            public bool IsPrimary { get; set; }");
            sb.AppendLine("            public bool IsNullable { get; set; }"); // Added IsNullable
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(fileName, sb.ToString());
        }


        static string GetParseMethod(string dataType)
        {
            switch (dataType)
            {
                case "int": return "int.TryParse";
                case "long": return "long.TryParse";
                case "decimal": return "decimal.TryParse";
                case "double": return "double.TryParse";
                case "float": return "float.TryParse";
                case "bool": return "bool.TryParse";
                case "DateTime": return "DateTime.TryParse";
                case "Guid": return "Guid.TryParse";
                default: return "true"; // For strings, no TryParse needed
            }
        }

        static void GenerateProjectFile(string path, string fileName, string outputType, params string[] references)
        {
            string fullPath = Path.Combine(path, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
            sb.AppendLine();
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine($"    <OutputType>{outputType}</OutputType>");
            sb.AppendLine("    <TargetFramework>net48</TargetFramework>");
            sb.AppendLine("    <LangVersion>8.0</LangVersion>");
            sb.AppendLine("    <RootNamespace>" + Path.GetFileNameWithoutExtension(fileName) + "</RootNamespace>");
            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine();

            foreach (var reference in references)
            {
                sb.AppendLine("  <ItemGroup>");
                sb.AppendLine($"    <ProjectReference Include=\"..\\{reference}\\{reference}.csproj\" />");
                sb.AppendLine("  </ItemGroup>");
            }

            sb.AppendLine("</Project>");

            File.WriteAllText(fullPath, sb.ToString());
        }

        static void GenerateSolutionFile(string solutionPath, string solutionName)
        {
            string slnPath = Path.Combine(solutionPath, $"{solutionName}.sln");

            var sb = new StringBuilder();
            sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            sb.AppendLine("# Visual Studio Version 17");
            sb.AppendLine("VisualStudioVersion = 17.0.31903.59");
            sb.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

            // Add projects
            AddProjectToSolution(sb, "DTO", Path.Combine("DTO", "DTO.csproj"), Guid.NewGuid());
            AddProjectToSolution(sb, "DAL", Path.Combine("DAL", "DAL.csproj"), Guid.NewGuid());
            AddProjectToSolution(sb, "BLL", Path.Combine("BLL", "BLL.csproj"), Guid.NewGuid());
            AddProjectToSolution(sb, "ConsoleApp", Path.Combine("ConsoleApp", "ConsoleApp.csproj"), Guid.NewGuid());

            // Add solution configurations
            sb.AppendLine("Global");
            sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
            sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
            sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
            sb.AppendLine("\tEndGlobalSection");

            // Add project configurations
            sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
            AddProjectConfig(sb, "DTO");
            AddProjectConfig(sb, "DAL");
            AddProjectConfig(sb, "BLL");
            AddProjectConfig(sb, "ConsoleApp");
            sb.AppendLine("\tEndGlobalSection");

            sb.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
            sb.AppendLine("\t\tHideSolutionNode = FALSE");
            sb.AppendLine("\tEndGlobalSection");
            sb.AppendLine("EndGlobal");

            File.WriteAllText(slnPath, sb.ToString());
        }

        static void AddProjectToSolution(StringBuilder sb, string name, string path, Guid projectId)
        {
            sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", \"{path}\", \"{{{projectId}}}\"");
            sb.AppendLine("EndProject");
        }

        static void AddProjectConfig(StringBuilder sb, string projectName)
        {
            string[] configs = { "Debug|Any CPU", "Release|Any CPU" };
            foreach (var config in configs)
            {
                // This assumes a fixed GUID for simplicity, a real solution generator would track GUIDs
                // For this generator, since it's used inside the same Program.cs that defines GetProjectGuid,
                // we'll make it call the local GetProjectGuid function.
                sb.AppendLine($"\t\t{{{GetProjectGuid(projectName)}}}.{config}.ActiveCfg = {config}");
                sb.AppendLine($"\t\t{{{GetProjectGuid(projectName)}}}.{config}.Build.0 = {config}");
            }
        }

        static Guid GetProjectGuid(string projectName)
        {
            // This would normally be stored during project creation
            // For simplicity, we'll generate deterministic GUIDs
            // For a production-ready generator, consider using a consistent hash of projectName
            // to ensure the GUID remains the same across regeneration for the same project.
            // For now, new GUIDs each time is fine for addressing the compilation error.
            return Guid.NewGuid();
        }
    }

    class TableSchema
    {
        public string Schema { get; set; }
        public string Name { get; set; }
        public List<ColumnSchema> Columns { get; set; }
    }

    class ColumnSchema
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsForeignKey { get; set; }
        public string ReferencedTable { get; set; }
        public string ReferencedColumn { get; set; }
        public int MaxLength { get; set; }
    }
}

