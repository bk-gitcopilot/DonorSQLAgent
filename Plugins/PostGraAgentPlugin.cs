using Azure.AI.OpenAI;
using Azure;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Npgsql;
using OpenAI.Embeddings;
using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgvector;

using Microsoft.Extensions.AI;
using Pgvector.EntityFrameworkCore;




namespace DonorSQLAgent.Plugins
{
    public class PostGraAgentPlugin
    {
        private readonly IDbContextFactory<AppDbContext> _db;
        private readonly Kernel _kernel;
        private readonly IMemoryCache _cache;
       
        // Performance constants
        private const double SIMILARITY_THRESHOLD = 0.7;
        private const double DISTANCE_THRESHOLD = 1 - SIMILARITY_THRESHOLD;
        private const int MAX_RESULTS = 3;
        private const string EMBEDDING_CACHE_KEY_PREFIX = "embedding:";
        private const int EMBEDDING_CACHE_EXPIRY_MINUTES = 60;

        // Simple internal memory dictionary
        private readonly Dictionary<string, string> _localMemory = new();
        private string? _cachedSchema;

        public PostGraAgentPlugin(IDbContextFactory<AppDbContext> db, Kernel kernel, IMemoryCache cache)
        {
            _db = db;
            _kernel = kernel;
            _cache = cache;
        }

        [KernelFunction("GetClosestQuery")]
        public async Task<string> GetClosestQuery(string userQuestion)
        {
            var questionEmbedding = await GetQuestionEmbeddingDataAsync(userQuestion);
            var embeddedData = new Vector(questionEmbedding);

            var similiarChunks = await GetResultByEmbeddingAsync(embeddedData);

            var concatenatedText = GetdocumentMergeContent(similiarChunks);
            return concatenatedText.ToString();
        }

        private StringBuilder GetdocumentMergeContent(List<DocumentChuncksViewModel> documentChuncksViewModels)
        {
            var resultContent = new StringBuilder(documentChuncksViewModels.Count * 200); // Pre-allocate capacity

            try
            {
                for (int i = 0; i < documentChuncksViewModels.Count; i++)
                {
                    var chunk = documentChuncksViewModels[i];
                    var trimmedTextData = chunk.TextData?.Trim();
                    
                    if (string.IsNullOrWhiteSpace(trimmedTextData))
                        continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(trimmedTextData);
                        if (doc.RootElement.TryGetProperty("query", out var queryElement))
                        {
                            var query = queryElement.GetString();
                            if (!string.IsNullOrEmpty(query))
                            {
                                resultContent.AppendLine("---");
                                resultContent.AppendLine($"This is the SQL query number {i + 1}:");
                                resultContent.AppendLine(query);
                                resultContent.AppendLine();
                            }
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.WriteLine($"JSON parsing error for chunk {i}: {jsonEx.Message}");
                        // Continue processing other chunks even if one fails
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetdocumentMergeContent: {ex.Message}");
                throw;
            }

            return resultContent;
        }
        private async Task<List<DocumentChuncksViewModel>> GetResultByEmbeddingAsync(Vector vectorData)
        {
            using var db = _db.CreateDbContext();

            try
            {
                // Single query with optimized distance calculation
                var documentData = await (
                 from docChunk in db.documentchunkdetails
                 join doc in db.documentdetails on docChunk.document_id equals doc.document_id
                 where !string.IsNullOrWhiteSpace(docChunk.textdata)
                       && docChunk.embeddedchunkdata != null
                 let similarity = docChunk.embeddedchunkdata.CosineDistance(vectorData)
                 where similarity <= DISTANCE_THRESHOLD
                 select new DocumentChuncksViewModel
                 {
                     DocumentID = doc.document_id,
                     DocumentName = doc.documentname ?? string.Empty,
                     DocumentTotalPage = doc.documenttotalpage,
                     DocumentType = doc.documenttype ?? string.Empty,
                     TextData = docChunk.textdata ?? string.Empty,
                     EmbeddedChunkData = docChunk.embeddedchunkdata,
                     Similarity = similarity
                 })
                 .OrderBy(x => x.Similarity)
                 .Take(MAX_RESULTS)
                 .ToListAsync();

                return documentData;
            }
            catch (Exception ex)
            {
                // Log the actual exception for debugging
                Console.WriteLine($"Error in GetResultByEmbeddingAsync: {ex.Message}");
                throw;
            }
        }
        // Optional: for pgvector similarity search
        public async Task<string> GetClosestQueryByEmbeddingAsync(float[] embeddingVector)
        {
            using var db = _db.CreateDbContext();
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT sql_query
        FROM possible_queries
        ORDER BY embedding <-> @embedding LIMIT 1;
    ";

            var vectorParam = new Npgsql.NpgsqlParameter("embedding", embeddingVector);
            cmd.Parameters.Add(vectorParam);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "";
        }



        private async Task<ReadOnlyMemory<float>> GetQuestionEmbeddingDataAsync(string textData)
        {
            // Check cache first
            var cacheKey = EMBEDDING_CACHE_KEY_PREFIX + textData.GetHashCode();
            if (_cache.TryGetValue(cacheKey, out ReadOnlyMemory<float> cachedVector))
            {
                return cachedVector;
            }

            try
            {
                Uri oaiEndpoint = new("XXXXXXXXXXXXXXXXX");
                string oaiKey = "XXXXXXXXXXXXXXXXXXXXX";
                string embeddingModelName = "XXXXXXXXXXXXXXXXXX";
                AzureOpenAIClient azureClient = new(oaiEndpoint, new AzureKeyCredential(oaiKey));
                EmbeddingClient embeddingClient = azureClient.GetEmbeddingClient(embeddingModelName);
                
                var result = await embeddingClient.GenerateEmbeddingAsync(textData);
                var vector = result.Value.ToFloats();
                
                // Cache the result
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(EMBEDDING_CACHE_EXPIRY_MINUTES),
                    Priority = CacheItemPriority.Normal
                };
                _cache.Set(cacheKey, vector, cacheOptions);
                
                return vector;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetQuestionEmbeddingDataAsync: {ex.Message}");
                throw;
            }
        }

    }
}

