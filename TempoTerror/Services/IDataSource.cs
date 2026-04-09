namespace TempoTerror.Services;

using System;

public interface IDataSource : IDisposable
{
    event Action<string>? OnLogLine;

    bool IsConnected { get; }

    void Connect();
}
