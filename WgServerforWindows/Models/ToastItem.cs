using CommunityToolkit.Mvvm.ComponentModel;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public partial class ToastItem : ObservableObject
    {
        [ObservableProperty]
        private string _message;

        [ObservableProperty]
        private string _title;

        [ObservableProperty]
        private ToastType _type;
        
        public int DurationSeconds { get; set; }
    }
}
