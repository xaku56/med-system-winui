using System;
using System.Threading.Tasks;

namespace MedSystem.App.UI;

internal static class RecordAction
{
    public static async Task RunAsync(Func<Task> action, Func<Exception, Task> reportError)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await reportError(ex);
        }
    }
}
