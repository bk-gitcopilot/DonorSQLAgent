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
        public string GetClosestQuery(string userQuestion)
        {
            //using var conn = new NpgsqlConnection(_connectionString);
            //conn.Open();
            var questionEmbedding = GetQuestionEmbeddingData(userQuestion);
            var embeddedData = new Vector(questionEmbedding);

            List<DocumentChuncksViewModel> similiarChunks = GetResultByEmbedding(embeddedData);

            StringBuilder concatenatedText = GetdocumentMergeContent(similiarChunks);
            return concatenatedText.ToString();

        }

        private StringBuilder GetdocumentMergeContent(List<DocumentChuncksViewModel> documentChuncksViewModels)
        {
            StringBuilder resultContent = new StringBuilder();

            try
            {
                for (int i = 0; i < documentChuncksViewModels.Count; i++)
                {
                    var chunk = documentChuncksViewModels[i];
                    using var doc = JsonDocument.Parse(chunk.TextData.Trim());
                    string query = doc.RootElement.GetProperty("query").GetString()!;
                    resultContent.AppendLine($"---");
                    resultContent.AppendLine($"This is the SQL query number {i + 1}:");
                    resultContent.AppendLine(query);
                    resultContent.AppendLine();
                }
            }

            catch (Exception ex)
            {
                throw;
            }

            return resultContent;
        }
        private List<DocumentChuncksViewModel> GetResultByEmbedding(Vector vectorData)
        {
            using var db = _db.CreateDbContext();
            var documentData = new List<DocumentChuncksViewModel>();

            try
            {
                double similarityThreshold = double.Parse("0.7");
                double distanceThreshold = 1 - similarityThreshold;

                documentData = (
                 from docChunk in db.documentchunkdetails
                 join doc in db.documentdetails on docChunk.document_id equals doc.document_id
                 where !string.IsNullOrWhiteSpace(docChunk.textdata)
                       && docChunk.embeddedchunkdata != null
                       && docChunk.embeddedchunkdata.CosineDistance(vectorData) <= distanceThreshold
                 select new DocumentChuncksViewModel
                 {
                     DocumentID = doc.document_id,
                     DocumentName = doc.documentname,
                     DocumentTotalPage = doc.documenttotalpage,
                     DocumentType = doc.documenttype,
                     TextData = docChunk.textdata,
                     EmbeddedChunkData = docChunk.embeddedchunkdata,
                     Similarity = docChunk.embeddedchunkdata.CosineDistance(vectorData)
                 })
                 .OrderBy(x => x.Similarity)
                 .Take(3)
                 .ToList();
            }

            catch (Exception ex)
            {
                throw;
            }

            return documentData;
        }
        // Optional: for pgvector similarity search
        public string GetClosestQueryByEmbedding(float[] embeddingVector)
        {
            using var db = _db.CreateDbContext();
            var conn = db.Database.GetDbConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT sql_query
        FROM possible_queries
        ORDER BY embedding <-> @embedding LIMIT 1;
    ";

            var vectorParam = new Npgsql.NpgsqlParameter("embedding", embeddingVector);
            cmd.Parameters.Add(vectorParam);

            var result = cmd.ExecuteScalar();
            return result?.ToString() ?? "";
        }



        private ReadOnlyMemory<float> GetQuestionEmbeddingData(string textData)
        {
            ReadOnlyMemory<float> vector;
            try
            {
                Uri oaiEndpoint = new("XXXXXXXXXXXXXXXXX");
                string oaiKey = "XXXXXXXXXXXXXXXXXXXXX";
                string embeddingModelName = "XXXXXXXXXXXXXXXXXX";
                AzureOpenAIClient azureClient = new(oaiEndpoint, new AzureKeyCredential(oaiKey));
                EmbeddingClient embeddingClient = azureClient.GetEmbeddingClient(embeddingModelName);
                var result = embeddingClient.GenerateEmbedding(textData);
                vector = result.Value.ToFloats();
                return vector;
            }

            catch (Exception ex)
            {
                throw;
            }
        }

    }
}

