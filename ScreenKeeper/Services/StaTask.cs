namespace ScreenKeeper.Services;

/// <summary>
/// Führt Arbeit in einem eigenen STA-Hintergrund-Thread aus (z. B. Papierkorb-Operationen der Shell), ohne den
/// UI-Thread zu blockieren.
/// </summary>
public static class StaTask {
    public static Task Run(Action action) {
        return Run<bool>(() => {
            action();
            return true;
        });
    }

    public static Task<T> Run<T>(Func<T> function) {
        TaskCompletionSource<T> completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new Thread(() => {
            try {
                completion.SetResult(function());
            } catch(Exception ex) {
                completion.SetException(ex);
            }
        }) {
            IsBackground = true,
            Name = "ScreenKeeper STA"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
