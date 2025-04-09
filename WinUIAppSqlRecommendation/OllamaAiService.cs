// OllamaAiService.cs (Refactored with additional Debug Logging)
using System;
using System.Collections.Generic;
using System.Diagnostics; // Required for Debug.WriteLine
using System.IO;
using System.Net.Http;
using System.Net.Http.Json; // For ReadFromJsonAsync and Create
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization; // For JsonPropertyName
using System.Threading;
using System.Threading.Tasks;

namespace WinUIAppSqlRecommendation.Services // Ensure this namespace is correct
{
    public class OllamaAiService : IAiService
    {
        // --- Configuration ---
        private const string DefaultOllamaApiUrl = "http://10.1.10.84:11434/api/generate";
        private const string DefaultModelName = "gemma3:12b";

        // --- Dependencies & State ---
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
        private static readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly string _ollamaApiUrl;
        private readonly string _modelName;

        // --- Constructors ---
        public OllamaAiService(string? ollamaApiUrl = null, string? modelName = null)
        {
            _ollamaApiUrl = ollamaApiUrl ?? DefaultOllamaApiUrl;
            _modelName = modelName ?? DefaultModelName;
            if (!Uri.TryCreate(_ollamaApiUrl, UriKind.Absolute, out _))
            {
                throw new ArgumentException("Invalid Ollama API URL provided.", nameof(ollamaApiUrl));
            }
            Debug.WriteLine($"[OllamaAiService] Initialized. API URL: '{_ollamaApiUrl}', Model: '{_modelName}'");
        }

        // --- IAiService Implementation ---

        public async IAsyncEnumerable<string> GetQueryOptimizationRecommendationStreamAsync(
            string sqlQuery,
            string? executionPlanXml,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Attempting to acquire semaphore...");
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Semaphore acquired.");
            Stream? responseStream = null;
            HttpResponseMessage? response = null;

            try // Outer try-finally to ensure semaphore release
            {
                Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Building prompt for model '{_modelName}'...");
                string prompt = BuildPrompt(sqlQuery, executionPlanXml);
                var requestPayload = new OllamaGenerateRequest(Model: _modelName, Prompt: prompt, Stream: true);

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _ollamaApiUrl);
                requestMessage.Content = JsonContent.Create(requestPayload);
                Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Sending POST request to '{_ollamaApiUrl}'.");

                // --- Make Request and Get Stream ---
                try
                {
                    response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Received response headers with status code: {response.StatusCode}.");
                    response.EnsureSuccessStatusCode(); // Throws HttpRequestException on non-success codes
                    responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Obtained response stream.");
                }
                catch (HttpRequestException httpEx)
                {
                    string errorDetails = await TryReadErrorBodyAsync(response, cancellationToken).ConfigureAwait(false);
                    string errorMessage = $"Ollama HTTP Error: {(response?.StatusCode.ToString() ?? httpEx.StatusCode?.ToString() ?? "N/A")} - {errorDetails}";
                    if (errorDetails.Contains("model not found", StringComparison.OrdinalIgnoreCase))
                    {
                        errorMessage += $"\nHint: Ensure model '{_modelName}' is pulled via 'ollama pull {_modelName}'.";
                    }
                    Debug.WriteLine($"[OllamaAiService.GetStreamAsync] HTTP request failed: {errorMessage}");
                    yield return errorMessage;
                    yield break;
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[OllamaAiService.GetStreamAsync] Request canceled during HTTP setup.");
                    yield break;
                }
                catch (Exception setupEx)
                {
                    Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Error setting up stream: {setupEx.Message}");
                    yield return $"Ollama Setup Error: {setupEx.Message}";
                    yield break;
                }

                // --- Process Stream ---
                Debug.WriteLine("[OllamaAiService.GetStreamAsync] Starting to process stream...");
                await foreach (string chunk in ProcessJsonResponseStream(responseStream, cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested(); // Check between yields
                    Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Yielding chunk (length: {chunk.Length}).");
                    yield return chunk;
                }

                Debug.WriteLine("[OllamaAiService.GetStreamAsync] Finished processing stream.");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[OllamaAiService.GetStreamAsync] Streaming operation canceled.");
                yield return "[Operation Canceled]";
            }
            catch (Exception ex) // Catch unexpected errors during the overall process
            {
                Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Unexpected error: {ex}"); // Log full exception
                yield return $"[Unexpected Error: {ex.Message}]";
            }
            finally
            {
                response?.Dispose();
                Debug.WriteLine($"[OllamaAiService.GetStreamAsync] Releasing semaphore.");
                _semaphore.Release();
            }
        }

        // --- Private Helper Methods ---

        private static async IAsyncEnumerable<string> ProcessJsonResponseStream(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Debug.WriteLine("[OllamaAiService.ProcessStream] Entering stream processor.");
            if (stream == null || !stream.CanRead)
            {
                Debug.WriteLine("[OllamaAiService.ProcessStream] Stream is null or unreadable, exiting.");
                yield break;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
            Debug.WriteLine("[OllamaAiService.ProcessStream] Created StreamReader.");
            string? line;
            OllamaStreamResponse? streamResponse = null;
            int lineCount = 0;

            while (true) // Read until break or cancellation
            {
                line = null; // Reset line for each iteration
                try
                {
                    // Debug.WriteLine($"[OllamaAiService.ProcessStream] Reading line {lineCount + 1}...");
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    // if(line != null) Debug.WriteLine($"[OllamaAiService.ProcessStream] Read line {lineCount + 1}: '{line.Substring(0, Math.Min(line.Length, 100))}'");
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[OllamaAiService.ProcessStream] ReadLineAsync canceled.");
                    throw; // Re-throw cancellation to be caught by the caller
                }
                catch (Exception readEx)
                {
                    Debug.WriteLine($"[OllamaAiService.ProcessStream] Error reading line {lineCount + 1}: {readEx.Message}");
                    yield return $"[Stream Read Error: {readEx.Message}]";
                    yield break; // Stop processing on read error
                }

                if (line == null)
                {
                    Debug.WriteLine($"[OllamaAiService.ProcessStream] End of stream reached after {lineCount} lines.");
                    break; // End of stream
                }
                lineCount++;

                if (string.IsNullOrWhiteSpace(line))
                {
                    // Debug.WriteLine($"[OllamaAiService.ProcessStream] Skipping blank line {lineCount}.");
                    continue;
                }

                try
                {
                    // Debug.WriteLine($"[OllamaAiService.ProcessStream] Deserializing line {lineCount}...");
                    streamResponse = JsonSerializer.Deserialize<OllamaStreamResponse>(line);
                    // Debug.WriteLine($"[OllamaAiService.ProcessStream] Deserialized line {lineCount}. Done: {streamResponse?.Done}, Error: {streamResponse?.Error}, Response Length: {streamResponse?.Response?.Length ?? 0}");

                }
                catch (JsonException jsonEx)
                {
                    Debug.WriteLine($"[OllamaAiService.ProcessStream] Skipping line {lineCount} due to JSON parsing error: {jsonEx.Message}. Line: '{line.Substring(0, Math.Min(line.Length, 100))}'");
                    continue; // Skip malformed line
                }

                if (streamResponse != null)
                {
                    if (!string.IsNullOrEmpty(streamResponse.Error))
                    {
                        Debug.WriteLine($"[OllamaAiService.ProcessStream] Stream reported error on line {lineCount}: {streamResponse.Error}");
                        yield return $"[Ollama Stream Error: {streamResponse.Error}]";
                        yield break; // Stop processing on stream error
                    }
                    if (!string.IsNullOrEmpty(streamResponse.Response))
                    {
                        // Debug.WriteLine($"[OllamaAiService.ProcessStream] Yielding response chunk from line {lineCount}.");
                        yield return streamResponse.Response;
                    }
                    if (streamResponse.Done)
                    {
                        Debug.WriteLine($"[OllamaAiService.ProcessStream] Stream processing complete (done=true received at line {lineCount}).");
                        yield break; // Stop processing normally
                    }
                }
            } // End while loop

            // Check if loop exited without 'done:true' and no specific error was yielded/thrown
            if (streamResponse == null || !streamResponse.Done)
            {
                Debug.WriteLine("[OllamaAiService.ProcessStream] Stream ended unexpectedly without 'done:true' flag.");
                // Optionally yield a warning: yield return "[Stream ended unexpectedly]";
            }
            Debug.WriteLine("[OllamaAiService.ProcessStream] Exiting stream processor.");
        }

        private static async Task<string> TryReadErrorBodyAsync(HttpResponseMessage? response, CancellationToken cancellationToken)
        {
            Debug.WriteLine("[OllamaAiService.TryReadErrorBodyAsync] Attempting to read error body...");
            if (response?.Content == null)
            {
                Debug.WriteLine("[OllamaAiService.TryReadErrorBodyAsync] No response or content found.");
                return "No response body.";
            }
            try
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Debug.WriteLine($"[OllamaAiService.TryReadErrorBodyAsync] Read error body (length: {errorBody?.Length ?? 0}).");
                if (string.IsNullOrWhiteSpace(errorBody)) return "Empty response body.";

                // Try parsing as standard Ollama error
                var errorResponse = JsonSerializer.Deserialize<OllamaErrorResponse>(errorBody);
                var specificError = errorResponse?.Error;
                if (!string.IsNullOrEmpty(specificError))
                {
                    Debug.WriteLine($"[OllamaAiService.TryReadErrorBodyAsync] Parsed specific error: '{specificError}'");
                    return specificError;
                }
                else
                {
                    Debug.WriteLine("[OllamaAiService.TryReadErrorBodyAsync] No specific error found in JSON, returning raw body.");
                    return errorBody; // Return raw body if parsing fails or 'error' field is missing/empty
                }
            }
            catch (JsonException jsonEx)
            {
                Debug.WriteLine($"[OllamaAiService.TryReadErrorBodyAsync] JSON parsing error while reading error body: {jsonEx.Message}");
                // Attempt to return raw body even if JSON parsing failed
                try { return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false); } catch { return "Failed to read raw error body after JSON parse error."; }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OllamaAiService.TryReadErrorBodyAsync] Failed to read error body: {ex.Message}");
                return $"Failed to read/parse error body: {ex.Message}";
            }
        }


        // Builds the prompt for the AI model
        private string BuildPrompt(string sqlQuery, string? executionPlanXml)
        {
            Debug.WriteLine("[OllamaAiService.BuildPrompt] Building prompt...");
            var promptBuilder = new StringBuilder();
            // (Prompt content remains the same)
            promptBuilder.AppendLine("You are an expert SQL Server performance tuning assistant.");
            promptBuilder.AppendLine("Analyze the following SQL query and its associated execution plan (if provided).");
            promptBuilder.AppendLine("Provide specific, actionable recommendations to improve its performance, focusing on indexing, query structure, and potential bottlenecks.");
            promptBuilder.AppendLine("Format your recommendations clearly using markdown bullet points.");
            promptBuilder.AppendLine("\n--- SQL Query ---");
            promptBuilder.AppendLine("```sql");
            promptBuilder.AppendLine(sqlQuery);
            promptBuilder.AppendLine("```");
            if (!string.IsNullOrWhiteSpace(executionPlanXml)) { /* ... */ promptBuilder.AppendLine("\n--- Execution Plan (XML) ---"); promptBuilder.AppendLine("```xml"); const int maxPlanLength = 10000; promptBuilder.AppendLine(executionPlanXml.Length > maxPlanLength ? executionPlanXml[..maxPlanLength] + "\n... (plan truncated)" : executionPlanXml); promptBuilder.AppendLine("```"); }
            else { /* ... */ promptBuilder.AppendLine("\n--- Execution Plan (XML) ---"); promptBuilder.AppendLine("Not Provided"); }
            promptBuilder.AppendLine("\n--- Recommendations ---");
            string prompt = promptBuilder.ToString();
            Debug.WriteLine($"[OllamaAiService.BuildPrompt] Prompt built (length: {prompt.Length}).");
            return prompt;
        }

        // --- Private Helper Records ---
        private record OllamaGenerateRequest(
            [property: JsonPropertyName("model")] string Model,
            [property: JsonPropertyName("prompt")] string Prompt,
            [property: JsonPropertyName("stream")] bool Stream
        );

        private record OllamaStreamResponse(
            [property: JsonPropertyName("model")] string Model = "",
            [property: JsonPropertyName("created_at")] DateTime CreatedAt = default,
            [property: JsonPropertyName("response")] string Response = "",
            [property: JsonPropertyName("done")] bool Done = false,
            [property: JsonPropertyName("error")] string? Error = null
        );

        private record OllamaErrorResponse(
             [property: JsonPropertyName("error")] string Error = ""
        );
    }
}