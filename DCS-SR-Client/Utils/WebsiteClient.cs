using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

namespace Vanguard.VCS.Client.Utils;

public static class WebsiteClient
{
    private static readonly HttpClient HttpClient;
    private static readonly string BaseUrl = "https://profile.vngd.net/_functions/";
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // Static constructor to initialize HttpClient with default headers
    static WebsiteClient()
    {
        HttpClient = new HttpClient();
        
        // Set the timeout to 5 seconds
        HttpClient.Timeout = TimeSpan.FromSeconds(5);
        
        // Add default headers that will be sent with every request
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "Vanguard-VCS-Client/1.0");
        HttpClient.DefaultRequestHeaders.Add("X-Client-Version", "1.0.0");
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        
        HttpClient.BaseAddress = new Uri(BaseUrl);
    }

    public static async Task<ServerInformation> GetServerInformation()
    {
        try
        {
            // Since BaseAddress is set, you can use relative URLs
            HttpResponseMessage response = await HttpClient.GetAsync("vcs_backend_information");
            
            response.EnsureSuccessStatusCode();

            string jsonString = await response.Content.ReadAsStringAsync();
            ServerInformationApiResponse result = JsonSerializer.Deserialize<ServerInformationApiResponse>(jsonString);

            return result.Data;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error(ex, $"HTTP Request Error when calling {BaseUrl}vcs_backend_information");
            return null;
        }
        catch (JsonException ex)
        {
            Logger.Error(ex, $"JSON Parsing Error when calling {BaseUrl}vcs_backend_information");
            return null;
        }
    }
}