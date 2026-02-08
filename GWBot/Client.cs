using Microsoft.Extensions.Http.Resilience;
using Polly;

public sealed class Client
{
    private static Client _instance = new();

    private static readonly HttpClient _httpClient;

    static Client()
    {
        // Copied from MSDN, I'm sure this is good and solid code that will not cause issues in the future
        var retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new HttpRetryStrategyOptions
            {
                BackoffType = DelayBackoffType.Exponential,
                MaxRetryAttempts = 3
            })
            .Build();

        var socketHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };
        var resilienceHandler = new ResilienceHandler(retryPipeline)
        {
            InnerHandler = socketHandler,
        };

        _httpClient = new HttpClient(resilienceHandler);
    }

    private Client()
    {

    }
    public static Client Instance => _instance;

    public static HttpClient HttpClient => _httpClient;
}
