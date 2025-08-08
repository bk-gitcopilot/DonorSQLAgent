using Microsoft.Extensions.Caching.Memory;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel;
using System.Data;
using System.Text.Json;
using Dapper;

public class SQLServerAgentPlugin   // Defines a plugin for SQL-related tasks integrated with AI
{
    private readonly IDbConnection _db;   // Database connection object (e.g., SQLite or Postgres)
    private readonly Kernel _kernel;      // AI kernel for executing LLM prompts
    private readonly IMemoryCache _cache; // Cache to store frequently used SQL results or queries

    // Internal dictionary to keep user questions and generated SQL in memory
    private readonly Dictionary<string, string> _localMemory = new();
    private string? _cachedSchema;  // Cache for storing database schema (to avoid multiple DB calls)

    // Constructor - initializes DB connection, AI kernel, and memory cache
    public SQLServerAgentPlugin(IDbConnection db, Kernel kernel, IMemoryCache cache)
    {
        _db = db;        // Assign database connection instance
        _kernel = kernel;// Assign AI kernel instance
        _cache = cache;  // Assign cache instance
    }

    // Exposes function to AI kernel for fetching database schema in JSON format
    [KernelFunction("GetSchemaAsync")]
    public async Task<string> GetSchemaAsync()
    {
        if (!string.IsNullOrEmpty(_cachedSchema))
            return _cachedSchema;

        // Query table schema, column names, and data types
        var rows = await _db.QueryAsync<dynamic>(
            @"SELECT 
            TABLE_SCHEMA,
            TABLE_NAME,
            COLUMN_NAME,
            DATA_TYPE
          FROM INFORMATION_SCHEMA.COLUMNS
          WHERE TABLE_NAME NOT LIKE 'sys%'
          ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION");

        // Dictionary to hold schema: table => (column => data type)
        var schema = new Dictionary<string, Dictionary<string, string>>();

        foreach (var row in rows)
        {
            string fullTableName = $"{row.TABLE_SCHEMA}.{row.TABLE_NAME}";
            string columnName = row.COLUMN_NAME;
            string dataType = row.DATA_TYPE;

            if (!schema.ContainsKey(fullTableName))
                schema[fullTableName] = new Dictionary<string, string>();

            schema[fullTableName][columnName] = dataType;
        }

        // Serialize schema to JSON and cache it
        _cachedSchema = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        return _cachedSchema;
    }



    // Exposes function to AI kernel to generate SQL query using AI or heuristics
    [KernelFunction("GenerateSqlAsync")]
    public async Task<string> GenerateSqlAsync(string userQuestion, string closestQuery)
    {
        // Check if we already have SQL cached for the given question
        if (_cache.TryGetValue(userQuestion, out string cachedSql))
            return cachedSql;

        var lower = userQuestion.ToLower();  // Convert question to lowercase for pattern matching
        var schema = await GetSchemaAsync(); // Fetch database schema (cached if already available)

        // Generate SQL query using LLM with schema and closest query as context
        string sql = await GenerateSqlUsingLLM(userQuestion, schema, closestQuery);

        // Cache the generated SQL for future requests
        _cache.Set(userQuestion, sql);
        return sql; // Return generated SQL
    }

    // Exposes function to AI kernel to generate human-readable answer from SQL result
    [KernelFunction("GenerateAnsAsync")]
    public async Task<string> GenerateAnsAsync(string userQuestion, string Ans)
    {
        string? sql; // Placeholder for answer text

        // Generate human-friendly answer using LLM
        sql = await GenerateAns(userQuestion, Ans);

        // Return generated answer
        return sql;
    }

    // Simple rule-based detection for predefined aggregation queries (SUM, AVG, COUNT, etc.)
    private string DetectAggregationQuery(string lower)
    {
        string dateFilter = ""; // Holds date-based filtering condition

        // Detect date filters in user question
        if (lower.Contains("this year"))
            dateFilter = "WHERE strftime('%Y', donation_date) = strftime('%Y', 'now')";
        else if (lower.Contains("last year"))
            dateFilter = "WHERE strftime('%Y', donation_date) = strftime('%Y', 'now', '-1 year')";
        else if (lower.Contains("this month"))
            dateFilter = "WHERE strftime('%Y-%m', donation_date) = strftime('%Y-%m', 'now')";
        else if (lower.Contains("last month"))
            dateFilter = "WHERE strftime('%Y-%m', donation_date) = strftime('%Y-%m', 'now', '-1 month')";
        else if (lower.Contains("today"))
            dateFilter = "WHERE date(donation_date) = date('now')";

        // Detect common aggregation types and return direct SQL
        if (lower.Contains("total donation") || lower.Contains("sum donation"))
            return $"SELECT SUM(amount) AS TotalDonation FROM Donations {dateFilter};";

        if (lower.Contains("average donation") || lower.Contains("avg donation"))
            return $"SELECT AVG(amount) AS AverageDonation FROM Donations {dateFilter};";

        if (lower.Contains("number of donors") || lower.Contains("count donors"))
            return $"SELECT COUNT(DISTINCT donor_id) AS DonorCount FROM Donations {dateFilter};";

        if (lower.Contains("highest donation"))
            return $"SELECT MAX(amount) AS HighestDonation FROM Donations {dateFilter};";

        if (lower.Contains("lowest donation"))
            return $"SELECT MIN(amount) AS LowestDonation FROM Donations {dateFilter};";

        return null; // If no predefined query matches, fallback to LLM
    }

    // Uses AI (LLM) to generate SQL dynamically using schema and example queries
    private async Task<string> GenerateSqlUsingLLM(string question, string schema, string closestQuery)
    {
        // Prepare AI prompt
        var prompt = $@"
You are an expert SQL generator for SQL.
Rules:
1. Use only SELECT queries.
2. If the user asks for totals, averages, counts, highest, lowest, or similar,
   use aggregate functions (SUM, COUNT, AVG, MAX, MIN) and GROUP BY if needed.
3. Always produce deterministic SQL for the same question and schema.
4. Use only Sql server syntax, no other SQL dialects.
4. Return only the SQL query, nothing else.

User question: {question}
Database schema (JSON): {schema}
Closest Query (JSON): {closestQuery}
----------------------
### 🔗 Key Relationships:
| From | To | Join Condition |
|------|----|----------------|
| donation_database.donor_id | donor_info.donor_id | donor → donations |
| donation_database.campaign_id | campaign.campaign_id | campaign → donations |
| donation_database.location_id | donation_location.location_id | location → donations |
| donation_database.donation_type_id | donation_type.donation_type_id | type → donations |
| donation_database.program_id | program_name.program_id | program → donations |
| donation_database.account_type_id | account_type.account_type_id | account → donations |
| donation_database.stage_id | stage.stage_id | stage → donations | 
";

        // AI execution settings
        var settings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 300,   // Limit output size
            Temperature = 0,   // Deterministic output
            TopP = 1           // Focus on highest probability tokens
        };

        // Create AI function from prompt
        var function = _kernel.CreateFunctionFromPrompt(prompt, settings);

        // Execute prompt and get response
        var result = await _kernel.InvokeAsync(function);
        return result.GetValue<string>() ?? string.Empty; // Return SQL or empty string
    }

    // Converts raw SQL JSON result into a human-readable answer using AI
    private async Task<string> GenerateAns(string question, string Ans)
    {
        // Prepare AI prompt for natural language answer
        var prompt = $@"
You are a helpful assistant responding to a user's question using provided JSON data and prior conversation context.
Based on this JSON data: {Ans}, answer the following natural language question: {question}.
Guidelines:
- Provide a clear, concise, human-readable response.
- If the answer includes multiple data points or categories, format them using plain HTML elements such as <ul>, <li>, <b>, and <p>.
- Do not use markdown, code blocks, or raw JSON in the output.
- Not showing any database information like id and id value any other field name
- Return only the final answer";

        // AI execution settings
        var settings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 300,
            Temperature = 0,
            TopP = 1
        };

        // Execute prompt with AI kernel
        var function = _kernel.CreateFunctionFromPrompt(prompt, settings);
        var result = await _kernel.InvokeAsync(function);
        return result.GetValue<string>() ?? string.Empty; // Return natural language answer
    }

    // Executes given SQL query safely and returns JSON result
    [KernelFunction("ExecuteSqlAsync")]
    public async Task<string> ExecuteSqlAsync(string sql)
    {
        // Prevent dangerous queries like DROP, DELETE, UPDATE, INSERT
        var unsafeKeywords = new[] { "DROP", "DELETE", "UPDATE", "INSERT" };
        if (unsafeKeywords.Any(k => sql.ToUpper().Contains(k)))
            return JsonSerializer.Serialize(new { error = "Unsafe query detected." });

        // Execute SQL and return result as JSON
        var result = await _db.QueryAsync(sql);
        return JsonSerializer.Serialize(result);
    }

    // Stores question -> SQL mapping in memory
    [KernelFunction("SaveContextAsync")]
    public async Task SaveContextAsync(string question, string sql)
    {
        _localMemory[question] = sql; // Save mapping in dictionary
        await Task.CompletedTask;     // Return completed task
    }

    // Returns previous context (all stored question->SQL mappings)
    [KernelFunction("GetContextAsync")]
    public async Task<string> GetContextAsync(string question)
    {
        if (_localMemory.Count == 0)
            return "No previous context";

        return string.Join("\n", _localMemory.Select(x => $"{x.Key} -> {x.Value}"));
    }

    // Adds user log to the database for audit/history purposes
    [KernelFunction("AddUserLogAsync")]
    public async Task AddUserLogAsync(string userQuestion, string finalSql, string answer, string cleanedSql = null)
    {
        // Insert query for logging user interaction
        var sql = @"
            INSERT INTO user_log (Date_Time, user_question, cleaned_Sql, final_sql, answer)
            VALUES (@Date_Time, @User_Question, @Cleaned_Sql, @Final_Sql, @Answer);
            SELECT last_insert_rowid();";

        // Parameters for query
        var parameters = new
        {
            Date_Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            User_Question = userQuestion,
            Cleaned_Sql = cleanedSql,
            Final_Sql = finalSql,
            Answer = answer
        };

        // Execute insert and get new record ID (not returned to caller)
        var id = await _db.ExecuteScalarAsync<int>(sql, parameters);
    }
}
