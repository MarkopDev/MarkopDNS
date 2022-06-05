using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Diagnostics;
using System.Collections.Concurrent;

Console.WriteLine("[+] Start Stress Test");

const int requestsCount = 1000;
var requests = new ConcurrentQueue<Tuple<int, long>>();
var resetEvent = new ManualResetEvent(false);

var httpClientHandler = new HttpClientHandler();
httpClientHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
var httpClient = new HttpClient(httpClientHandler);
httpClient.DefaultRequestHeaders.Host = "localhost";

for (var j = 0; j < 10; j++)
{
    for (var i = 0; i < requestsCount; i++)
    {
        var requestsCopy = requests;
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
                requestsCopy.Enqueue(Tuple.Create(1, sw.ElapsedMilliseconds));
            else
                requestsCopy.Enqueue(Tuple.Create(0, sw.ElapsedMilliseconds));

            // var response = await httpResponseMessage.Content.ReadAsStringAsync(cancellationToken.Token);
            // Console.WriteLine("Response:");
            // Console.WriteLine(response);

            if (requestsCount == requestsCopy.Count)
                resetEvent.Set();
        });
    }

    resetEvent.WaitOne();

    Console.WriteLine($"[+] Success: {requests.Count(i => i.Item1 == 1)} Failed: {requests.Count(i => i.Item1 == 0)}");
    Console.WriteLine(
        $"[+] AVG: {requests.Average(i => i.Item2)} MAX: {requests.Max(i => i.Item2)} MIN: {requests.Min(i => i.Item2)}");

    requests = new ConcurrentQueue<Tuple<int, long>>();
    resetEvent = new ManualResetEvent(false);
}

Console.WriteLine("[+] Stress Test Finished");
Console.ReadLine();