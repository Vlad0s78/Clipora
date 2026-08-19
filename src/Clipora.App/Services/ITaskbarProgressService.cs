namespace Clipora.App.Services;

public interface ITaskbarProgressService
{
    void SetProgress(double percent);

    void Clear();
}
