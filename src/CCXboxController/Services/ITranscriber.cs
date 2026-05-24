using System;
using System.Threading;
using System.Threading.Tasks;

namespace CCXboxController.Services;

public interface ITranscriber : IDisposable
{
    string Name { get; }
    Task<string> TranscribeAsync(string wavPath, CancellationToken ct = default);
}
