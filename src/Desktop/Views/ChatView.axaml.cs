using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OmniConnect.Desktop.ViewModels;

namespace OmniConnect.Desktop.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();

        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Set up the native save dialog for file downloads requested by MainViewModel.
        if (DataContext is MainViewModel vm)
        {
            vm.PickSaveFilePathAsync = async suggestedFileName =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save file",
                    SuggestedFileName = suggestedFileName,
                    DefaultExtension = Path.GetExtension(suggestedFileName)
                });

                return file?.Path.LocalPath;
            };
        }
    }

    /// <summary>
    /// Keeps the resizable right panel inside the window. The splitter stores an
    /// absolute pixel width, so without this the panel would overflow (and clip)
    /// when the window is made narrower than the width the user dragged.
    /// </summary>
    private void OnChatRootGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var columns = ChatRootGrid.ColumnDefinitions;
        if (columns.Count < 4) return;

        var center = columns[1];
        var right = columns[3];
        if (!right.Width.IsAbsolute) return;

        double available = e.NewSize.Width - columns[0].ActualWidth - columns[2].ActualWidth;
        double maxRight = Math.Max(right.MinWidth, available - center.MinWidth);

        if (right.Width.Value > maxRight)
        {
            right.Width = new GridLength(maxRight);
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _ = vm.SearchUsersCommand.ExecuteAsync(null);
        }
    }

    private async void OnUploadFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to upload",
            AllowMultiple = true
        });

        if (files == null || files.Count == 0) return;

        foreach (var file in files)
        {
            await using var stream = await file.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            await vm.SendFileUploadAsync(file.Name, memoryStream.ToArray());
        }
    }
}
