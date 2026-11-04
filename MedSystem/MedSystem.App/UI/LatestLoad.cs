using System;
using System.Threading;
using System.Threading.Tasks;

namespace MedSystem.App.UI;

/// <summary>Отменяет предыдущую загрузку и оставляет индикатор под управлением последней.</summary>
internal sealed class LatestLoad
{
    private CancellationTokenSource? _cancellation;

    public void Cancel() => _cancellation?.Cancel();

    public async Task RunAsync(
        Func<CancellationToken, Task> load,
        Action<bool> setLoading,
        Action<Exception> reportError,
        int delayMilliseconds = 0)
    {
        Cancel();
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        try
        {
            if (delayMilliseconds > 0)
                await Task.Delay(delayMilliseconds, cancellation.Token);

            setLoading(true);
            await load(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Новый запрос или уход со страницы отменил загрузку.
        }
        catch (Exception ex)
        {
            reportError(ex);
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
                setLoading(false);
            }
        }
    }
}
