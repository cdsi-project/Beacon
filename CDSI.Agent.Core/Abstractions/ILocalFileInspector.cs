using CDSI.Agent.Core.Scanning;

namespace CDSI.Agent.Core.Abstractions;

public interface ILocalFileInspector
{
    DiscoveredFile Inspect(string path);
}
