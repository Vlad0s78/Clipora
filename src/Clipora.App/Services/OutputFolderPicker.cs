using Windows.Storage.Pickers;

namespace Clipora.App.Services;

public sealed class OutputFolderPicker : IOutputFolderPicker
{
    public async Task<string?> PickAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return folder?.Path;
    }
}
