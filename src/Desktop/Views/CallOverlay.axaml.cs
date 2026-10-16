using Avalonia.Controls;
using Avalonia.Media;
using OmniConnect.Desktop.ViewModels;

namespace OmniConnect.Desktop.Views;

public partial class CallOverlay : UserControl
{
    public CallOverlay()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is CallViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(CallViewModel.IsMuted):
                        UpdateMuteVisual(vm.IsMuted);
                        break;
                    case nameof(CallViewModel.IsSpeakerOn):
                        UpdateSpeakerVisual(vm.IsSpeakerOn);
                        break;
                }
            };
        }
    }

    private void UpdateMuteVisual(bool isMuted)
    {
        if (isMuted)
        {
            MuteBorder.Background = new SolidColorBrush(Color.Parse("#ef4444"));
            MuteBorder.Opacity = 0.8;
        }
        else
        {
            MuteBorder.Background = new SolidColorBrush(Color.Parse("#232a45"));
            MuteBorder.Opacity = 1.0;
        }
    }

    private void UpdateSpeakerVisual(bool isSpeakerOn)
    {
        if (isSpeakerOn)
        {
            SpeakerBorder.Background = new SolidColorBrush(Color.Parse("#3b82f6"));
            SpeakerBorder.Opacity = 0.8;
        }
        else
        {
            SpeakerBorder.Background = new SolidColorBrush(Color.Parse("#232a45"));
            SpeakerBorder.Opacity = 1.0;
        }
    }
}
