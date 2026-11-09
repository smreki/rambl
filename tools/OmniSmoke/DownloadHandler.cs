using System.Net;

namespace Rambl.OmniSmoke;

/// <summary>Serves deterministic model files and can hold a download while other task screens inspect its state.</summary>
internal sealed class DownloadHandler(Dictionary<string, byte[]> files) : HttpMessageHandler
{
    internal TaskCompletionSource<bool>? Hold { get; set; }
    internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Requests { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        _ = Started.TrySetResult(true);
        if (Hold is { } hold) await hold.Task.WaitAsync(cancellationToken);
        var name = Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(files[name]), RequestMessage = request };
    }
}
