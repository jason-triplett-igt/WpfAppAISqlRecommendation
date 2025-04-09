using SqlPerformanceAiAdvisor.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SqlPerformanceAiAdvisor.Service
{
    // Add this within the SqlPerformanceAiAdvisor.Services namespace
    public class OllamaAiService : IAiService
    {
        // Use a static HttpClient for performance and resource management
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        // Semaphore to ensure only one request is dispatched at a time
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _ollamaApiUrl = "http://10.1.10.65:11434/api/generate"; // Your Ollama server URL
        private readonly string _modelName = "gemma3:12b"; // Or choose another model like llama3, codellama, etc.

        public async Task<string> GetQueryOptimizationRecommendationAsync(string sqlQuery, string? executionPlanXml)
        {
            await _semaphore.WaitAsync();
            try
            {
                Console.WriteLine($"Ollama Service: Requesting recommendation for query using model '{_modelName}'...");

            // Construct a detailed prompt for better recommendations
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("You are an expert SQL Server performance tuning assistant.");
            promptBuilder.AppendLine("Analyze the following SQL query and its associated execution plan (if provided).");
            promptBuilder.AppendLine("Provide specific, actionable recommendations to improve its performance, focusing on indexing, query structure, and potential bottlenecks.");
            promptBuilder.AppendLine("Format your recommendations clearly, for example using bullet points.");
            promptBuilder.AppendLine("\n--- SQL Query ---");
            promptBuilder.AppendLine("```sql");
            promptBuilder.AppendLine(sqlQuery);
            promptBuilder.AppendLine("```");

            if (!string.IsNullOrWhiteSpace(executionPlanXml))
            {
                promptBuilder.AppendLine("\n--- Execution Plan (XML) ---");
                promptBuilder.AppendLine("```xml");
                // Avoid sending excessively large plans if necessary, though often helpful
                promptBuilder.AppendLine(executionPlanXml.Length > 10000 ? executionPlanXml.Substring(0, 10000) + "\n... (plan truncated)" : executionPlanXml);
                promptBuilder.AppendLine("```");
            }
            else
            {
                promptBuilder.AppendLine("\n--- Execution Plan (XML) ---");
                promptBuilder.AppendLine("Not Provided");
            }

            promptBuilder.AppendLine("\n--- Recommendations ---");
            // The model should continue from here

            var requestPayload = new OllamaGenerateRequest
            {
                Model = _modelName,
                Prompt = promptBuilder.ToString(),
                Stream = false // Get the full response at once
                               // Add other options if needed, e.g., "options": { "temperature": 0.7 }
            };

            try
            {
                // Using System.Net.Http.Json for convenience
                var response = await _httpClient.PostAsJsonAsync(_ollamaApiUrl, requestPayload);
                // Uncomment below if not using PostAsJsonAsync
                // var jsonPayload = JsonSerializer.Serialize(requestPayload);
                // var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                // var response = await _httpClient.PostAsync(_ollamaApiUrl, content);


                if (response.IsSuccessStatusCode)
                {
                    var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>();
                    // Uncomment below if not using ReadFromJsonAsync
                    // var responseString = await response.Content.ReadAsStringAsync();
                    // var ollamaResponse = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseString);


                    if (ollamaResponse != null && !string.IsNullOrWhiteSpace(ollamaResponse.Response))
                    {
                        Console.WriteLine("Ollama Service: Recommendation received successfully.");
                        return ollamaResponse.Response.Trim(); // Return the main response text
                    }
                    else if (ollamaResponse != null && !string.IsNullOrEmpty(ollamaResponse.Error))
                    {
                        Console.WriteLine($"Ollama Service: API returned an error: {ollamaResponse.Error}");
                        return $"Ollama Error: {ollamaResponse.Error}";
                    }
                    else
                    {
                        Console.WriteLine("Ollama Service: Received empty or invalid response format.");
                        return "Ollama Error: Received an empty or invalid response.";
                    }
                }
                else
                {
                    // Attempt to read error details if available
                    string errorBody = await response.Content.ReadAsStringAsync();
                    string errorMessage = $"Ollama Error: {response.StatusCode}";
                    if (!string.IsNullOrWhiteSpace(errorBody))
                    {
                        try
                        {
                            // Try parsing as standard Ollama error response
                            var errorResponse = JsonSerializer.Deserialize<OllamaErrorResponse>(errorBody);
                            if (errorResponse != null && !string.IsNullOrWhiteSpace(errorResponse.Error))
                            {
                                errorMessage += $"- {errorResponse.Error}";
                                if (errorResponse.Error.Contains("model not found"))
                                {
                                    errorMessage += $"\nHint: Make sure the model '{_modelName}' is available on the Ollama server ({_ollamaApiUrl.Replace("/api/generate", "")}). Try 'ollama pull {_modelName}'.";
                                }
                            }
                            else
                            {
                                errorMessage += $"- {errorBody}"; // Fallback to raw body
                            }
                        }
                        catch
                        {
                            errorMessage += $"- {errorBody}"; // Fallback if error body isn't JSON
                        }
                    }
                    Console.WriteLine($"Ollama Service: Request failed - {errorMessage}");
                    return errorMessage;
                }
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"Ollama Service: Network error - {httpEx.Message}");
                return $"Network Error: Could not connect to Ollama at {_ollamaApiUrl}. Ensure the server is running and accessible. Details: {httpEx.Message}";
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"Ollama Service: JSON parsing error - {jsonEx.Message}");
                return $"JSON Error: Could not parse response from Ollama. Details: {jsonEx.Message}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ollama Service: Unexpected error - {ex.Message}");
                return $"Unexpected Error: {ex.Message}";
            }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Helper classes for JSON serialization/deserialization
        private class OllamaGenerateRequest
        {
            // UseJsonPropertyName if property names differ from C# convention
            // [System.Text.Json.Serialization.JsonPropertyName("model")]
            public string Model { get; set; } = "";
            public string Prompt { get; set; } = "";
            public bool Stream { get; set; } = false;
            // Optional: Add other Ollama options if needed
            // public Dictionary<string, object>? Options { get; set; }
        }

        private class OllamaGenerateResponse
        {
            public string Model { get; set; } = "";
            public DateTime Created_at { get; set; }
            public string Response { get; set; } = ""; // The generated text
            public bool Done { get; set; }
            public string? Error { get; set; } // Ollama might return error in standard response too
                                               // Add other fields if needed (context, timings, etc.)
                                               // public long? total_duration { get; set; }
                                               // public int[]? context { get; set; }
        }

        private class OllamaErrorResponse
        {
            public string Error { get; set; } = "";
        }
    }
}
