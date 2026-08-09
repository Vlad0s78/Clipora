using Clipora.Core.Tools;

namespace Clipora.Core.Interfaces;

public interface IBundledToolResolver
{
    BundledToolPaths Resolve();
}
