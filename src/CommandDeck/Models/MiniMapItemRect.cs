using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CommandDeck.Models;

/// <summary>
/// Represents a single item rectangle projected into mini-map coordinate space.
/// Mutable so the ViewModel can update positions in-place (avoids ObservableCollection
/// Clear/Add churn on every camera/zoom change).
/// </summary>
public sealed class MiniMapItemRect : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private bool   _isTerminal;
    private bool   _isAiSession;
    private bool   _isSelected;

    /// <summary>Left position in mini-map pixels.</summary>
    public double X { get => _x; set => Set(ref _x, value); }

    /// <summary>Top position in mini-map pixels.</summary>
    public double Y { get => _y; set => Set(ref _y, value); }

    /// <summary>Width in mini-map pixels.</summary>
    public double Width { get => _width; set => Set(ref _width, value); }

    /// <summary>Height in mini-map pixels.</summary>
    public double Height { get => _height; set => Set(ref _height, value); }

    /// <summary>True when the underlying canvas item is a Terminal; false for widgets.</summary>
    public bool IsTerminal { get => _isTerminal; set => Set(ref _isTerminal, value); }

    /// <summary>True when the underlying canvas item is an AI terminal session.</summary>
    public bool IsAiSession { get => _isAiSession; set => Set(ref _isAiSession, value); }

    /// <summary>True when the underlying canvas item is currently selected.</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}
