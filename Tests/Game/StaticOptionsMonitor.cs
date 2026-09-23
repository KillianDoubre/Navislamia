using Microsoft.Extensions.Options;

namespace Tests.Game;

/// <summary>
/// An <see cref="IOptionsMonitor{TOptions}"/> over one mutable instance: a test edits the object and the
/// service reads the change on its next call, which is what a reload of the settings file does.
/// </summary>
public sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
