using System.Collections.Concurrent;
using System.Diagnostics;

Console.WriteLine("[+] Start Stress Test");

var requests = new ConcurrentQueue<Tuple<int, long>>();

var resetEvent = new ManualResetEvent(false);

const int requestsCount = 1000;

var httpClientHandler = new HttpClientHandler();
httpClientHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
var httpClient = new HttpClient(httpClientHandler);
httpClient.DefaultRequestHeaders.Host = "localhost";

for (var i = 0; i < requestsCount; i++)
{
    ThreadPool.QueueUserWorkItem(async (_) =>
    {
        var sw = Stopwatch.StartNew();

        HttpResponseMessage? httpResponseMessage = null;
        try
        {
            httpResponseMessage = await httpClient.GetAsync("https://127.0.0.1:4431/");
        }
        catch
        {
            // ignored
        }

        sw.Stop();

        if (httpResponseMessage?.IsSuccessStatusCode ?? false)
            requests.Enqueue(Tuple.Create(1, sw.ElapsedMilliseconds));
        else
            requests.Enqueue(Tuple.Create(0, sw.ElapsedMilliseconds));

        // var response = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken.Token);
        // Console.WriteLine("Response:");
        // Console.WriteLine(response);

        if (requestsCount == requests.Count)
            resetEvent.Set();
    });
}

resetEvent.WaitOne();

Console.WriteLine($"[+] Success:{requests.Count(i => i.Item1 == 1)} Failed:{requests.Count(i => i.Item1 == 0)}");
Console.WriteLine(
    $"[+] AVG: {requests.Average(i => i.Item2)} MAX: {requests.Max(i => i.Item2)} MIN: {requests.Min(i => i.Item2)}");

Console.WriteLine("[+] Stress Test Finished");