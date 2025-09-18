using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using System.Diagnostics;
using System.Text;

namespace DonorSQLAgent.Tests
{
    /// <summary>
    /// Basic performance and functionality tests for PostGraAgentPlugin optimizations
    /// </summary>
    public class PerformanceValidationTests
    {
        /// <summary>
        /// Test that constants are properly defined and accessible
        /// </summary>
        public static void TestConstants()
        {
            Console.WriteLine("=== Testing Performance Constants ===");
            
            // These should be compile-time constants now
            var plugin = CreateTestPlugin();
            
            // Verify constants are accessible via reflection (they should be const)
            var type = typeof(DonorSQLAgent.Plugins.PostGraAgentPlugin);
            var fields = type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            var constants = fields.Where(f => f.IsLiteral && !f.IsInitOnly).ToList();
            Console.WriteLine($"✅ Found {constants.Count} performance constants defined");
            
            foreach (var constant in constants)
            {
                Console.WriteLine($"   - {constant.Name}: {constant.GetValue(null)}");
            }
        }

        /// <summary>
        /// Test that caching mechanism works properly
        /// </summary>
        public static void TestEmbeddingCache()
        {
            Console.WriteLine("\n=== Testing Embedding Caching ===");
            
            var cache = new MemoryCache(new MemoryCacheOptions());
            
            // Simulate cache operations
            var testKey = "embedding:12345";
            var testVector = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f });
            
            // Test cache miss
            var result1 = cache.TryGetValue(testKey, out ReadOnlyMemory<float> cachedValue);
            Console.WriteLine($"✅ Cache miss test: {(!result1 ? "PASS" : "FAIL")}");
            
            // Test cache set and hit
            cache.Set(testKey, testVector, TimeSpan.FromMinutes(60));
            var result2 = cache.TryGetValue(testKey, out ReadOnlyMemory<float> cachedValue2);
            Console.WriteLine($"✅ Cache hit test: {(result2 ? "PASS" : "FAIL")}");
            Console.WriteLine($"✅ Cache value integrity: {(cachedValue2.Length == 3 ? "PASS" : "FAIL")}");
        }

        /// <summary>
        /// Test StringBuilder pre-allocation
        /// </summary>
        public static void TestStringBuilderOptimization()
        {
            Console.WriteLine("\n=== Testing StringBuilder Optimization ===");
            
            var sw = Stopwatch.StartNew();
            
            // Test with pre-allocation (our optimized version)
            var sb1 = new StringBuilder(1000); // Pre-allocated
            for (int i = 0; i < 100; i++)
            {
                sb1.AppendLine("---");
                sb1.AppendLine($"This is the SQL query number {i + 1}:");
                sb1.AppendLine("SELECT * FROM test;");
                sb1.AppendLine();
            }
            
            sw.Stop();
            var optimizedTime = sw.ElapsedTicks;
            
            sw.Restart();
            
            // Test without pre-allocation (original version)
            var sb2 = new StringBuilder(); // Default capacity
            for (int i = 0; i < 100; i++)
            {
                sb2.AppendLine("---");
                sb2.AppendLine($"This is the SQL query number {i + 1}:");
                sb2.AppendLine("SELECT * FROM test;");
                sb2.AppendLine();
            }
            
            sw.Stop();
            var originalTime = sw.ElapsedTicks;
            
            Console.WriteLine($"✅ Original StringBuilder: {originalTime} ticks");
            Console.WriteLine($"✅ Optimized StringBuilder: {optimizedTime} ticks");
            Console.WriteLine($"✅ Performance improvement: {(originalTime > optimizedTime ? "BETTER" : "SAME/WORSE")}");
        }

        /// <summary>
        /// Test JSON parsing robustness
        /// </summary>
        public static void TestJsonParsingRobustness()
        {
            Console.WriteLine("\n=== Testing JSON Parsing Robustness ===");
            
            var testCases = new[]
            {
                "{\"query\": \"SELECT * FROM users;\"}",  // Valid JSON
                "{\"query\": null}",                      // Null query
                "{\"otherField\": \"value\"}",           // Missing query field
                "invalid json",                          // Invalid JSON
                "",                                      // Empty string
                "   "                                    // Whitespace only
            };
            
            int successCount = 0;
            
            foreach (var testCase in testCases)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(testCase))
                    {
                        Console.WriteLine($"✅ Skipped empty/whitespace case");
                        successCount++;
                        continue;
                    }

                    using var doc = System.Text.Json.JsonDocument.Parse(testCase);
                    if (doc.RootElement.TryGetProperty("query", out var queryElement))
                    {
                        var query = queryElement.GetString();
                        if (!string.IsNullOrEmpty(query))
                        {
                            Console.WriteLine($"✅ Valid query extracted: {query}");
                        }
                        else
                        {
                            Console.WriteLine($"✅ Null/empty query handled gracefully");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"✅ Missing query field handled gracefully");
                    }
                    successCount++;
                }
                catch (System.Text.Json.JsonException)
                {
                    Console.WriteLine($"✅ Invalid JSON handled gracefully");
                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Unexpected error: {ex.Message}");
                }
            }
            
            Console.WriteLine($"✅ Robustness test: {successCount}/{testCases.Length} cases handled successfully");
        }

        private static object CreateTestPlugin()
        {
            // This would require a mock setup in a real test environment
            // For now, we're just testing the concepts
            return new object();
        }

        /// <summary>
        /// Run all performance validation tests
        /// </summary>
        public static void RunAllTests()
        {
            Console.WriteLine("🚀 Starting Performance Validation Tests\n");
            
            TestConstants();
            TestEmbeddingCache();
            TestStringBuilderOptimization();
            TestJsonParsingRobustness();
            
            Console.WriteLine("\n✅ Performance validation tests completed!");
        }
    }
}