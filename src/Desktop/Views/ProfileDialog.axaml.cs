using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OmniConnect.Desktop.ViewModels;

namespace OmniConnect.Desktop.Views;

public partial class ProfileDialog : UserControl
{
    public ProfileDialog()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is ProfileViewModel vm)
        {
            vm.PickAvatarFileAsync = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select an avatar image",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp" }
                        }
                    }
                });

                if (files == null || files.Count == 0) return null;

                await using var stream = await files[0].OpenReadAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                return memoryStream.ToArray();
            };
        }
    }
}
