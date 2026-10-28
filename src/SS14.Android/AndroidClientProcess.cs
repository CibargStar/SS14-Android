using SS14.Launcher.Models;

namespace SS14.Android;

internal sealed class AndroidClientProcess : IClientProcess
{
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool HasExited => _exit.Task.IsCompleted;
    public int ExitCode => HasExited ? _exit.Task.GetAwaiter().GetResult() : throw new InvalidOperationException("Client is running.");
    public async Task WaitForExitAsync(CancellationToken cancel) => await _exit.Task.WaitAsync(cancel);
    public void Exited(int code) => _exit.TrySetResult(code);
}
